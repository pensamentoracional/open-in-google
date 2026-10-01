using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed record GoogleFile(string Id, string MimeType, bool Trashed, bool CanEdit, Dictionary<string, string> Properties);
public sealed class GoogleDriveClient(HttpClient http, IGoogleAuth auth, UploadSessionStore? sessions = null)
{
    private readonly ResumableUpload? uploads = sessions is null ? null : new(http, auth, sessions);
    public bool CanResume(RemoteAttempt attempt, byte[] bytes, string mime) => uploads?.HasSession(attempt, bytes, mime) == true;
    public Task<string> ResumeAsync(RemoteAttempt attempt, byte[] bytes, string mime, CancellationToken ct) =>
        uploads?.ResumeAsync(attempt, bytes, mime, ct) ?? throw new ReconciliationRequiredException();
    public const string SheetMime = "application/vnd.google-apps.spreadsheet";
    public const string FolderMime = "application/vnd.google-apps.folder";
    public static void ValidateId(string id)
    { if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,200}$")) throw new InvalidDataException("Invalid Google file ID."); }
    public static Uri Editor(string id) { ValidateId(id); return new Uri($"https://docs.google.com/spreadsheets/d/{id}/edit"); }
    private async Task<JsonDocument> SendAsync(Func<HttpRequestMessage> request, string account, CancellationToken ct)
    {
        var refreshed = false; var refresh = false; var retries = 0;
        for (var i = 0; i < 5; i++)
        {
            var access = await auth.AccessAsync(refresh, ct); refresh = false;
            if (access.AccountId != account) throw new InvalidOperationException("Google account changed.");
            using var req = request(); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
            HttpResponseMessage response;
            try { response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException) when (req.Method == HttpMethod.Get && ++retries <= 3)
            { await Task.Delay(TimeSpan.FromMilliseconds(200 * retries), ct); continue; }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized && req.Method == HttpMethod.Get && !refreshed)
                { refreshed = true; refresh = true; continue; }
                if (req.Method == HttpMethod.Get && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) && ++retries <= 3)
                { await Task.Delay(TimeSpan.FromMilliseconds(200 * retries), ct); continue; }
                if (!response.IsSuccessStatusCode) throw new GoogleApiException((int)response.StatusCode);
                const int max = 1024 * 1024;
                if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Google response too large.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct); using var buffer = new MemoryStream(); var chunk = new byte[81920]; int count;
                while ((count = await stream.ReadAsync(chunk, ct)) != 0) { if (buffer.Length + count > max) throw new InvalidDataException("Google response too large."); buffer.Write(chunk, 0, count); }
                return JsonDocument.Parse(buffer.ToArray());
            }
        }
        throw new ReconciliationRequiredException();
    }
    private static GoogleFile File(JsonElement e)
    {
        var id = e.GetProperty("id").GetString()!; ValidateId(id);
        var props = new Dictionary<string, string>();
        if (e.TryGetProperty("appProperties", out var p)) foreach (var x in p.EnumerateObject()) props[x.Name] = x.Value.GetString()!;
        return new(id, e.GetProperty("mimeType").GetString()!, e.GetProperty("trashed").GetBoolean(),
            e.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty("canEdit", out var edit) && edit.GetBoolean(), props);
    }
    private const string Fields = "id,mimeType,trashed,capabilities(canEdit),appProperties";
    public async Task<GoogleFile> GetAsync(string account, string id, CancellationToken ct)
    {
        ValidateId(id); using var json = await SendAsync(() => new(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{id}?fields={Uri.EscapeDataString(Fields)}"), account, ct);
        return File(json.RootElement);
    }
    public async Task<IReadOnlyList<GoogleFile>> FindAsync(RemoteAttempt attempt, CancellationToken ct)
    {
        var files = new List<GoogleFile>(); string? page = null; var pages = new HashSet<string>();
        do
        {
            var q = $"appProperties has {{ key='sw_operation' and value='{attempt.Marker}' }} and trashed=false";
            var url = "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(q) + "&pageSize=100&fields=" + Uri.EscapeDataString($"nextPageToken,files({Fields})");
            if (page is not null) url += "&pageToken=" + Uri.EscapeDataString(page);
            using var json = await SendAsync(() => new(HttpMethod.Get, url), attempt.AccountId, ct);
            foreach (var e in json.RootElement.GetProperty("files").EnumerateArray()) files.Add(File(e));
            if (files.Count > 1) throw new ReconciliationRequiredException();
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (page is not null && (!pages.Add(page) || pages.Count > 100)) throw new ReconciliationRequiredException();
        } while (page is not null);
        return files;
    }
    public async Task<byte[]> ExportXlsxAsync(string account, string id, CancellationToken ct)
    {
        ValidateId(id);
        for (var retry = 0; retry < 2; retry++)
        {
            var access = await auth.AccessAsync(retry == 1, ct);
            if (access.AccountId != account) throw new InvalidOperationException("Google account changed.");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{id}/export?mimeType={Uri.EscapeDataString(SpreadsheetFormats.XlsxMime)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (retry == 0 && response.StatusCode == HttpStatusCode.Unauthorized) continue;
            if (!response.IsSuccessStatusCode) throw new GoogleApiException((int)response.StatusCode);
            const int max = 10 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Export too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct); using var buffer = new MemoryStream();
            var chunk = new byte[81920]; int count;
            while ((count = await stream.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + count > max) throw new InvalidDataException("Export too large.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
            }
            return buffer.ToArray();
        }
        throw new AuthorizationRequiredException();
    }
    public async Task<string> CreateAsync(RemoteAttempt attempt, string name, string? folder, byte[]? bytes, CancellationToken ct, string mediaType = SpreadsheetFormats.XlsxMime)
    {
        var meta = new Dictionary<string, object>
        {
            ["name"] = name,
            ["mimeType"] = attempt.Kind == "sheet" ? SheetMime : FolderMime,
            ["appProperties"] = new Dictionary<string, string> { ["sw_operation"] = attempt.Marker, ["sw_hash"] = attempt.Hash }
        };
        if (folder is not null) { ValidateId(folder); meta["parents"] = new[] { folder }; }
        var metadata = JsonSerializer.Serialize(meta);
        if (bytes is not null && uploads is not null) return await uploads.StartAsync(attempt, metadata, bytes, mediaType, ct);
        using var json = await SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, bytes is null ? "https://www.googleapis.com/drive/v3/files?fields=id" : "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id");
            if (bytes is null) req.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
            else
            {
                var content = new MultipartContent("related"); content.Add(new StringContent(metadata, Encoding.UTF8, "application/json"));
                var media = new ByteArrayContent(bytes); media.Headers.ContentType = new MediaTypeHeaderValue(mediaType); content.Add(media); req.Content = content;
            }
            return req;
        }, attempt.AccountId, ct);
        var id = json.RootElement.GetProperty("id").GetString()!; ValidateId(id); return id;
    }
}
