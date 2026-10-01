using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SheetsWindows.Core;
using SheetsWindows.Infrastructure;
using Xunit;

namespace SheetsWindows.Tests;

public sealed class RobustnessTests
{
    private sealed class Auth : IGoogleAuth
    {
        public string Account = "A"; public int Refreshes;
        public Task<GoogleAccess> AccessAsync(bool refresh = false, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); if (refresh) Refreshes++; return Task.FromResult(new GoogleAccess(Account, "private-token")); }
    }
    private sealed class Protector : IUploadSecretProtector
    {
        // Test-only authenticated encryption, never used in production.
        private readonly byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("fixture"));
        public byte[] Protect(byte[] bytes)
        {
            var result = new byte[28 + bytes.Length]; RandomNumberGenerator.Fill(result.AsSpan(0, 12));
            using var aes = new AesGcm(key, 16); aes.Encrypt(result.AsSpan(0, 12), bytes, result.AsSpan(28), result.AsSpan(12, 16)); return result;
        }
        public byte[] Unprotect(byte[] bytes)
        { var result = new byte[bytes.Length - 28]; using var aes = new AesGcm(key, 16); aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), result); return result; }
    }
    private sealed class Server : HttpMessageHandler
    {
        public int Posts, Queries, Chunks; public bool LoseChunk, LoseFinal, Offline, Expired, NoProgress, Unauthorized; public string? BadRange;
        public string Location = "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=secret-session";
        public readonly MemoryStream Received = new(); public readonly List<long> Starts = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("private-token", request.Headers.Authorization!.Parameter);
            if (request.Method == HttpMethod.Post)
            { Posts++; var result = new HttpResponseMessage(HttpStatusCode.OK); result.Headers.Location = new Uri(Location); return result; }
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/upload/drive/v3/files", request.RequestUri!.AbsolutePath);
            if (Offline) throw new HttpRequestException("private detail");
            if (Expired) return new(HttpStatusCode.NotFound);
            if (Unauthorized) { Unauthorized = false; return new(HttpStatusCode.Unauthorized); }
            var range = request.Content!.Headers.ContentRange!;
            if (range.From is null) Queries++;
            else
            {
                Chunks++; Starts.Add(range.From.Value);
                if (!NoProgress)
                {
                    Assert.Equal(Received.Length, range.From); var bytes = await request.Content.ReadAsByteArrayAsync(ct); Received.Write(bytes);
                    if (LoseChunk) { LoseChunk = false; Offline = true; throw new HttpRequestException("lost chunk acknowledgment"); }
                }
            }
            if (Received.Length == range.Length)
            {
                if (LoseFinal) { LoseFinal = false; Offline = true; throw new HttpRequestException("lost completion"); }
                return Json(new { id = "sheet_1" });
            }
            var response = new HttpResponseMessage((HttpStatusCode)308);
            if (BadRange is not null) response.Headers.TryAddWithoutValidation("Range", BadRange);
            else if (Received.Length > 0) response.Headers.TryAddWithoutValidation("Range", "bytes=0-" + (Received.Length - 1));
            return response;
        }
    }
    private static HttpResponseMessage Json(object obj) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(obj)) };
    private static RemoteAttempt Attempt() => new("sheet:test", "A", "sheet", "source-hash", "marker", null, false);
    private static UploadSessionStore Store(Workspace w) => new(Path.Combine(w.Root, "state", "uploads"), new Protector());
    [Fact]
    public async Task RestartAfterLostChunkUsesServerOffsetAndEncryptedDurableSession()
    {
        using var w = new Workspace(); using var server = new Server { LoseChunk = true }; using var http = new HttpClient(server); var auth = new Auth(); var bytes = new byte[700000]; RandomNumberGenerator.Fill(bytes);
        var uploader = new ResumableUpload(http, auth, Store(w));
        await Assert.ThrowsAsync<HttpRequestException>(() => uploader.StartAsync(Attempt(), "{}", bytes, "application/test", default));
        Assert.Equal(256 * 1024, server.Received.Length); Assert.Equal(1, server.Posts);
        var disk = File.ReadAllBytes(Assert.Single(Directory.GetFiles(Path.Combine(w.Root, "state", "uploads")))); Assert.DoesNotContain("secret-session", Encoding.UTF8.GetString(disk));
        server.Offline = false;
        Assert.Equal("sheet_1", await new ResumableUpload(http, auth, Store(w)).ResumeAsync(Attempt(), bytes, "application/test", default));
        Assert.Equal(bytes, server.Received.ToArray()); Assert.Equal(new long[] { 0, 262144, 524288 }, server.Starts); Assert.Equal(1, server.Posts); Assert.True(server.Queries >= 2);
        Assert.Equal("UploadSession [protected]", Store(w).Get(Attempt(), bytes, "application/test")!.ToString());
    }
    [Fact]
    public async Task LostCompletionResumesWithoutSendingMoreContent()
    {
        using var w = new Workspace(); using var server = new Server { LoseFinal = true }; using var http = new HttpClient(server); var bytes = new byte[100];
        var upload = new ResumableUpload(http, new Auth(), Store(w));
        await Assert.ThrowsAsync<HttpRequestException>(() => upload.StartAsync(Attempt(), "{}", bytes, "application/test", default));
        server.Offline = false; Assert.Equal("sheet_1", await upload.ResumeAsync(Attempt(), bytes, "application/test", default)); Assert.Equal(1, server.Chunks); Assert.Equal(1, server.Posts);
    }
    [Fact]
    public async Task ExpiredSessionRequiresReconciliationWithoutAnotherPost()
    {
        using var w = new Workspace(); using var server = new Server { Expired = true }; using var http = new HttpClient(server); var bytes = new byte[100];
        var upload = new ResumableUpload(http, new Auth(), Store(w));
        await Assert.ThrowsAsync<ReconciliationRequiredException>(() => upload.StartAsync(Attempt(), "{}", bytes, "application/test", default));
        await Assert.ThrowsAsync<ReconciliationRequiredException>(() => upload.ResumeAsync(Attempt(), bytes, "application/test", default)); Assert.Equal(1, server.Posts); Assert.Equal(0, server.Chunks);
    }
    [Theory]
    [InlineData("bytes=1-42")]
    [InlineData("bytes=0-999999999999999999")]
    [InlineData("bytes=0-100")]
    [InlineData("bytes=0-99")]
    public async Task MalformedOrComplete308AcknowledgmentBlocksContent(string range)
    {
        using var w = new Workspace(); using var server = new Server { BadRange = range }; using var http = new HttpClient(server);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ResumableUpload(http, new Auth(), Store(w)).StartAsync(Attempt(), "{}", new byte[100], "application/test", default)); Assert.Equal(0, server.Chunks);
    }
    [Fact]
    public async Task NoProgressAndCancellationAreBounded()
    {
        using var w = new Workspace(); using var server = new Server { NoProgress = true }; using var http = new HttpClient(server);
        var upload = new ResumableUpload(http, new Auth(), Store(w)); var bytes = new byte[100];
        await Assert.ThrowsAsync<ReconciliationRequiredException>(() => upload.StartAsync(Attempt(), "{}", bytes, "application/test", default)); Assert.Equal(3, server.Chunks);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload.ResumeAsync(Attempt(), bytes, "application/test", cancel.Token)); Assert.Equal(1, server.Posts);
    }
    [Fact]
    public async Task ChangedPayloadAccountAndMimeCannotResume()
    {
        using var w = new Workspace(); var store = Store(w); var bytes = new byte[100]; store.Save(Attempt(), bytes, "application/test", new Uri(new Server().Location));
        Assert.Throws<LocalConflictException>(() => store.Get(Attempt() with { AccountId = "B" }, bytes, "application/test"));
        Assert.Throws<LocalConflictException>(() => store.Get(Attempt(), bytes, "application/other")); bytes[0] = 1;
        Assert.Throws<LocalConflictException>(() => store.Get(Attempt(), bytes, "application/test"));
        using var server = new Server(); using var http = new HttpClient(server); var auth = new Auth { Account = "B" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ResumableUpload(http, auth, store).ResumeAsync(Attempt(), new byte[100], "application/test", default)); Assert.Equal(0, server.Queries);
    }
    [Theory]
    [InlineData("http://www.googleapis.com/upload/drive/v3/files?upload_id=secret")]
    [InlineData("https://evil.test/upload/drive/v3/files?upload_id=secret")]
    [InlineData("https://www.googleapis.com/drive/v3/files/other?upload_id=secret")]
    [InlineData("https://user@www.googleapis.com/upload/drive/v3/files?upload_id=secret")]
    public async Task UntrustedSessionIsRejectedBeforeContent(string location)
    {
        using var w = new Workspace(); using var server = new Server { Location = location }; using var http = new HttpClient(server);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ResumableUpload(http, new Auth(), Store(w)).StartAsync(Attempt(), "{}", new byte[100], "application/test", default)); Assert.Equal(0, server.Chunks);
    }
    [Fact]
    public async Task UnauthorizedStatusRefreshesAccountBeforeRetry()
    {
        using var w = new Workspace(); using var server = new Server { Unauthorized = true }; using var http = new HttpClient(server); var auth = new Auth();
        Assert.Equal("sheet_1", await new ResumableUpload(http, auth, Store(w)).StartAsync(Attempt(), "{}", new byte[100], "application/test", default)); Assert.Equal(1, auth.Refreshes); Assert.Equal(1, server.Posts);
    }
    [WindowsFact]
    public void ProductionSessionUsesCurrentUserDpapiAndRejectsTampering()
    {
        using var w = new Workspace(); var store = new UploadSessionStore(Path.Combine(w.Root, "uploads")); var bytes = new byte[100];
        store.Save(Attempt(), bytes, "application/test", new Uri(new Server().Location)); Assert.NotNull(store.Get(Attempt(), bytes, "application/test"));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(w.Root, "uploads"))); var encrypted = File.ReadAllBytes(path);
        Assert.DoesNotContain("secret-session", Encoding.UTF8.GetString(encrypted)); encrypted[^1] ^= 1; File.WriteAllBytes(path, encrypted);
        Assert.Throws<CryptographicException>(() => store.Get(Attempt(), bytes, "application/test"));
    }
    [Fact]
    public async Task DiagnosticsRotateRemainBoundedAndExportOnlyKnownFields()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "state")); var log = new DiagnosticLog(storage); var id = Guid.NewGuid();
        await log.RecordAsync(DiagnosticEvent.Started, id);
        var current = Path.Combine(storage.Root, "logs", "events.jsonl");
        var line = JsonSerializer.Serialize(new DiagnosticEntry(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), DiagnosticEvent.Failed, id)) + "\n";
        var fullFile = string.Concat(Enumerable.Repeat(line, DiagnosticLog.MaxFileBytes / Encoding.UTF8.GetByteCount(line)));
        // Represent accumulated history without thousands of redundant fsyncs/ACL writes on CI.
        for (var i = 0; i < 5; i++) { File.WriteAllText(current, fullFile, new UTF8Encoding(false)); await log.RecordAsync(DiagnosticEvent.Failed, id); }
        var files = Directory.GetFiles(Path.Combine(storage.Root, "logs")); Assert.Equal(4, files.Length); Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, DiagnosticLog.MaxFileBytes));
        File.AppendAllText(current, "{\"Time\":\"2026-01-01T00:00:00Z\",\"Event\":0,\"Operation\":null,\"Token\":\"secret\",\"Path\":\"private.xlsx\"}\n");
        var destination = Path.Combine(w.Root, "diagnostic.jsonl"); await log.ExportAsync(destination); var content = File.ReadAllText(destination);
        Assert.DoesNotContain("secret", content); Assert.DoesNotContain("private.xlsx", content); Assert.Contains(id.ToString(), content);
        await Assert.ThrowsAsync<IOException>(() => log.ExportAsync(destination));
    }
    [Fact]
    public async Task BrokenDiagnosticDirectoryDoesNotChangeOperationOutcome()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "state")); Directory.CreateDirectory(storage.Root); File.WriteAllText(Path.Combine(storage.Root, "logs"), "blocked");
        await new DiagnosticLog(storage).RecordAsync(DiagnosticEvent.Failed);
    }
    private sealed class Responses(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request, ++Calls));
    }
    [Fact]
    public async Task ReadOnlyMetadataRetriesTransientFailureWithoutPosting()
    {
        using var server = new Responses((request, count) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return count <= 2 ? new(HttpStatusCode.ServiceUnavailable) : Json(new { id = "sheet_1", mimeType = GoogleDriveClient.SheetMime, trashed = false, capabilities = new { canEdit = true } });
        });
        using var http = new HttpClient(server); Assert.Equal("sheet_1", (await new GoogleDriveClient(http, new Auth()).GetAsync("A", "sheet_1", default)).Id); Assert.Equal(3, server.Calls);
    }
    [Fact]
    public async Task CreatePostIsNeverRetriedAfterTransientFailure()
    {
        using var server = new Responses((_, _) => new(HttpStatusCode.ServiceUnavailable)); using var http = new HttpClient(server);
        await Assert.ThrowsAsync<GoogleApiException>(() => new GoogleDriveClient(http, new Auth()).CreateAsync(Attempt(), "name", null, null, default)); Assert.Equal(1, server.Calls);
    }
    [Fact]
    public async Task RepeatedPaginationTokenAndOversizedMetadataAreRejected()
    {
        using var repeated = new Responses((_, _) => Json(new { nextPageToken = "same", files = Array.Empty<object>() })); using var http = new HttpClient(repeated);
        await Assert.ThrowsAsync<ReconciliationRequiredException>(() => new GoogleDriveClient(http, new Auth()).FindAsync(Attempt(), default)); Assert.Equal(2, repeated.Calls);
        using var oversized = new Responses((_, _) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1024 * 1024 + 1]) }); using var httpLarge = new HttpClient(oversized);
        await Assert.ThrowsAsync<InvalidDataException>(() => new GoogleDriveClient(httpLarge, new Auth()).GetAsync("A", "sheet_1", default));
    }
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
    }
    [Fact]
    public async Task ResponseBodyAfterHeadersIsCoveredByOperationDeadline()
    {
        using var server = new Responses((_, _) => new(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }); using var http = new HttpClient(server);
        var drive = new GoogleDriveClient(http, new Auth(), requestTimeout: TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drive.GetAsync("A", "sheet_1", default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drive.ExportXlsxAsync("A", "sheet_1", default));
        using var w = new Workspace(); var store = Store(w); var bytes = new byte[100]; store.Save(Attempt(), bytes, "application/test", new Uri("https://www.googleapis.com/upload/drive/v3/files?upload_id=secret"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ResumableUpload(http, new Auth(), store, TimeSpan.FromMilliseconds(50)).ResumeAsync(Attempt(), bytes, "application/test", default));
        Assert.NotNull(store.Get(Attempt(), bytes, "application/test"));
    }
    [Fact]
    public void NormalizedTextPayloadIsDeterministicForResumeBinding()
    {
        var bytes = Encoding.UTF8.GetBytes("id,value\n001,=1+1"); Assert.Equal(SpreadsheetFormats.Prepare("csv", bytes).Bytes, SpreadsheetFormats.Prepare("csv", bytes).Bytes);
    }
}
