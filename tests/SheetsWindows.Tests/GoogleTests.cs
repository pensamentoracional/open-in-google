using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SheetsWindows.Core;
using SheetsWindows.Infrastructure;
using Xunit;
namespace SheetsWindows.Tests;

public sealed class GoogleTests
{
    private static byte[] Workbook(bool macro = false)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            void Entry(string name, string content) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(content); }
            Entry("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/></Types>");
            Entry("xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'/>");
            if (macro) Entry("xl/vbaProject.bin", "macro");
        }
        return bytes.ToArray();
    }
    private sealed class FixedAuth : IGoogleAuth
    {
        public string Account = "A"; public int Refreshes;
        public Task<GoogleAccess> AccessAsync(bool refresh = false, CancellationToken cancellationToken = default)
        { if (refresh) Refreshes++; return Task.FromResult(new GoogleAccess(Account, "token")); }
    }
    private sealed class DriveServer : HttpMessageHandler
    {
        public byte[]? ExportOverride; public int Exports; public bool ExportUnauthorizedOnce;
        public readonly Dictionary<string, byte[]> Uploaded = []; public readonly List<string> MediaTypes = [];
        private readonly Dictionary<string, (string Id, object File, string Mime, MemoryStream Bytes)> sessions = [];
        public bool Offline, DisconnectOnLoss;
        public int Posts; public bool LoseSheetResponse, LoseFolderResponse, ListLag, DuplicateList, WrongMime, Trashed, DenyEdit, Get401;
        public readonly Dictionary<string, object> Files = [];
        public readonly List<string> Methods = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Methods.Add(req.Method.Method); var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post)
            {
                Posts++; var body = await req.Content!.ReadAsStringAsync(ct);
                string Value(string key) => Regex.Match(body, "\"" + key + "\":\"([^\"]*)\"").Groups[1].Value;
                var mime = Value("mimeType"); var id = "file_" + Posts;
                var file = new { id, mimeType = WrongMime && mime == GoogleDriveClient.SheetMime ? "application/octet-stream" : mime, trashed = Trashed && mime == GoogleDriveClient.SheetMime, capabilities = new { canEdit = !(DenyEdit && mime == GoogleDriveClient.SheetMime) }, appProperties = new Dictionary<string, string> { { "sw_operation", Value("sw_operation") }, { "sw_hash", Value("sw_hash") } } };
                if (req.RequestUri.Query.Contains("uploadType=resumable"))
                {
                    sessions[id] = (id, file, req.Headers.GetValues("X-Upload-Content-Type").Single(), new MemoryStream());
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Headers.Location = new Uri("https://www.googleapis.com/upload/drive/v3/files?upload_id=" + id);
                    return response;
                }
                Files[id] = file;
                if (req.Content is MultipartContent parts)
                {
                    var media = parts.Last(); MediaTypes.Add(media.Headers.ContentType!.MediaType!); Uploaded[id] = await media.ReadAsByteArrayAsync(ct);
                }
                if ((mime == GoogleDriveClient.SheetMime && LoseSheetResponse) || (mime == GoogleDriveClient.FolderMime && LoseFolderResponse))
                { LoseSheetResponse = false; LoseFolderResponse = false; throw new HttpRequestException("Simulated lost response."); }
                return Json(new { id });
            }
            if (req.Method == HttpMethod.Put && path == "/upload/drive/v3/files")
            {
                if (Offline) throw new HttpRequestException("Simulated disconnection");
                var id = req.RequestUri.Query.Split("upload_id=")[1]; var session = sessions[id];
                var range = req.Content!.Headers.ContentRange!;
                if (range.From is not null)
                {
                    if (range.From != session.Bytes.Length) throw new InvalidOperationException("Unexpected upload offset.");
                    var data = await req.Content.ReadAsByteArrayAsync(ct); session.Bytes.Write(data);
                }
                if (session.Bytes.Length == range.Length)
                {
                    Files[id] = session.File;
                    if (!Uploaded.ContainsKey(id)) { Uploaded[id] = session.Bytes.ToArray(); MediaTypes.Add(session.Mime); }
                    if (LoseSheetResponse) { LoseSheetResponse = false; Offline = DisconnectOnLoss; throw new HttpRequestException("Lost completion response"); }
                    return Json(new { id });
                }
                var response = new HttpResponseMessage((HttpStatusCode)308);
                if (session.Bytes.Length > 0) response.Headers.TryAddWithoutValidation("Range", "bytes=0-" + (session.Bytes.Length - 1));
                return response;
            }
            if (req.Method != HttpMethod.Get) throw new InvalidOperationException("Unexpected destructive HTTP method.");
            if (path.EndsWith("/export"))
            {
                Exports++;
                if (ExportUnauthorizedOnce) { ExportUnauthorizedOnce = false; return new(HttpStatusCode.Unauthorized); }
                var id = path.Split('/')[^2]; var upload = Uploaded[id];
                var bytes = ExportOverride ?? (MediaTypes.Last() == "application/vnd.oasis.opendocument.spreadsheet" ? SpreadsheetFormats.WriteXlsx(SpreadsheetFormats.ReadOds(upload)!) : MediaTypes.Last() == "application/vnd.ms-excel" ? SpreadsheetFormats.WriteXlsx(SpreadsheetFormats.ReadExcel(upload, binary: true)) : upload);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            if (Get401) { Get401 = false; return new(HttpStatusCode.Unauthorized); }
            if (path.EndsWith("/files"))
            {
                var query = Uri.UnescapeDataString(req.RequestUri.Query); var marker = Regex.Match(query, "value='([^']+)'").Groups[1].Value;
                var matched = Files.Values.Where(f => JsonSerializer.Serialize(f).Contains(marker, StringComparison.Ordinal)).ToList();
                if (ListLag) matched.Clear(); if (DuplicateList && matched.Count > 0) matched.Add(matched[0]); return Json(new { files = matched });
            }
            var key = path.Split('/').Last(); return Files.TryGetValue(key, out var found) ? Json(found) : new(HttpStatusCode.NotFound);
        }
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static GoogleImport Importer(Workspace w, DriveServer server, FixedAuth? auth = null, IRemoteRegistry? registry = null)
    {
        auth ??= new FixedAuth(); var http = new HttpClient(server, false) { Timeout = TimeSpan.FromSeconds(10) };
        return new(w.Coordinator(), w.Registry(), registry ?? new GoogleRemoteRegistry(Path.Combine(w.Root, "state", "google.db")), new SourceReader(), new FileOperationLock(w.Locks), auth, new GoogleDriveClient(http, auth));
    }
    private sealed class LauncherBrowser : IBrowserLauncher
    {
        public bool Fail; public List<Uri> Opened { get; } = [];
        public void Open(Uri uri) { if (Fail) throw new IOException("Browser unavailable"); Opened.Add(uri); }
    }
    [Fact]
    public async Task CsvImportKeepsRawBackupUploadsTypedWorkbookAndReopensWithoutPost()
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, ".csv"); var original = Encoding.UTF8.GetBytes("codigo,valor\n001,=1+1"); File.WriteAllBytes(path, original);
        using var server = new DriveServer(); var importer = Importer(w, server); var receipt = await importer.ImportReceiptAsync(path);
        Assert.True(receipt.CanReplace); Assert.Equal("csv", receipt.Operation.Format); Assert.Equal(original, File.ReadAllBytes(receipt.Operation.Snapshot!.BackupPath));
        Assert.Equal(SpreadsheetFormats.XlsxMime, Assert.Single(server.MediaTypes));
        SpreadsheetFormats.VerifyValues(SpreadsheetFormats.Prepare("csv", original).Expected!, server.Uploaded["file_2"]);
        Assert.Equal(receipt.Url, await importer.ImportAsync(path)); Assert.Equal(2, server.Posts); Assert.True(File.Exists(path));
    }
    [Fact]
    public async Task LargeCsvImportPreservesRawBackupAndVerifiesExportWithoutDuplicateUpload()
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, ".csv"); var original = FormatTests.LargeCsv(); File.WriteAllBytes(path, original);
        using var server = new DriveServer(); var importer = Importer(w, server); var receipt = await importer.ImportReceiptAsync(path);
        Assert.True(receipt.CanReplace); Assert.Equal(original, File.ReadAllBytes(receipt.Operation.Snapshot!.BackupPath));
        using var http = new HttpClient(server, false); var drive = new GoogleDriveClient(http, new FixedAuth());
        var mapping = new GoogleRemoteRegistry(Path.Combine(w.Root, "state", "google.db")).Get("sheet:" + receipt.Operation.Id.ToString("N"))!;
        await new ConversionVerifier(drive).VerifyAsync(receipt.Operation, mapping, default);
        Assert.Equal(receipt.Url, await importer.ImportAsync(path)); Assert.Equal(2, server.Posts); Assert.True(File.Exists(path));
    }
    [Fact]
    public async Task ExportRefreshesOnceChecksAccountAndComparesActualValues()
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, ".tsv"); File.WriteAllText(path, "codigo\tvalor\n001\t=1+1");
        using var server = new DriveServer { ExportUnauthorizedOnce = true }; var auth = new FixedAuth(); var receipt = await Importer(w, server, auth).ImportReceiptAsync(path);
        using var http = new HttpClient(server, false); var drive = new GoogleDriveClient(http, auth);
        var mapping = new GoogleRemoteRegistry(Path.Combine(w.Root, "state", "google.db")).Get("sheet:" + receipt.Operation.Id.ToString("N"))!;
        var verifier = new ConversionVerifier(drive); await verifier.VerifyAsync(receipt.Operation, mapping, default); Assert.Equal(2, server.Exports); Assert.Equal(1, auth.Refreshes);
        server.ExportOverride = SpreadsheetFormats.WriteXlsx([new SheetValues("Dados", [new object?[] { "changed" }])]);
        await Assert.ThrowsAsync<ConversionMismatchException>(() => verifier.VerifyAsync(receipt.Operation, mapping, default));
        auth.Account = "other"; await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync(receipt.Operation, mapping, default));
    }
    private static void ConfigureLauncher(Workspace w, out LocalStorage storage)
    {
        storage = new(Path.Combine(w.Root, "state")); const string client = "pilot.apps.googleusercontent.com";
        LauncherConfiguration.SaveClient(storage, "{\"installed\":{\"client_id\":\"" + client + "\"}}"); File.WriteAllText(PilotSetup.PolicyPath(storage), w.Root);
        ExtendedConfiguration.Save(storage, new(), true);
        if (OperatingSystem.IsWindows()) new DpapiTokenVault(Path.Combine(storage.Root, "auth"), client).Save(new(client, client + ":user", "test-token", "test-refresh", DateTimeOffset.UtcNow.AddHours(1)));
    }
    [WindowsFact]
    public async Task NewLocalFormatsRetireOnlyAfterExportedValuesMatch()
    {
        foreach (var format in new[] { "csv", "tsv", "ods", "large-csv" })
        {
            using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, "." + (format == "large-csv" ? "csv" : format));
            var bytes = format == "large-csv" ? FormatTests.LargeCsv() : format == "ods" ? FormatTests.Ods("<table:table-row><table:table-cell office:value-type='string'><text:p>001</text:p></table:table-cell></table:table-row>") : Encoding.UTF8.GetBytes(format == "csv" ? "id,valor\n001,=1+1" : "id\tvalor\n001\t=1+1");
            File.WriteAllBytes(path, bytes); ConfigureLauncher(w, out var storage); using var server = new DriveServer(); using var http = new HttpClient(server); var browser = new LauncherBrowser();
            var shortcut = await new WindowsLauncher(storage, http, browser).OpenAsync(path);
            Assert.False(File.Exists(path)); Assert.True(File.Exists(shortcut)); Assert.Equal(1, server.Exports); Assert.Equal(2, server.Posts);
            Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(w.Registry().Pending()).Snapshot!.BackupPath));
        }
    }
    [WindowsFact]
    public async Task FidelityFailurePreservesOriginalThenResumesWithoutNewUpload()
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, ".csv"); File.WriteAllBytes(path, FormatTests.LargeCsv()); ConfigureLauncher(w, out var storage);
        using var server = new DriveServer { ExportOverride = SpreadsheetFormats.WriteXlsx([new SheetValues("Dados", [new object?[] { "wrong" }])]) }; using var http = new HttpClient(server); var browser = new LauncherBrowser(); var launcher = new WindowsLauncher(storage, http, browser);
        await Assert.ThrowsAsync<ConversionMismatchException>(() => launcher.OpenAsync(path)); Assert.True(File.Exists(path)); Assert.Empty(browser.Opened); Assert.Equal(2, server.Posts);
        server.ExportOverride = null; await launcher.OpenAsync(path); Assert.False(File.Exists(path)); Assert.Equal(2, server.Posts);
    }
    [WindowsFact]
    public async Task CopyModeAndComplexOdsNeverRetireOrWriteIntoTheSourceFolder()
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, ".ods"); var bytes = FormatTests.Ods("<table:table-row><table:table-cell table:formula='of:=1+1' office:value-type='float' office:value='2'/></table:table-row>"); File.WriteAllBytes(path, bytes);
        ConfigureLauncher(w, out var storage); using var server = new DriveServer(); using var http = new HttpClient(server); var browser = new LauncherBrowser(); var launcher = new WindowsLauncher(storage, http, browser);
        var shortcut = await launcher.OpenAsync(path); Assert.StartsWith(Path.Combine(storage.Root, "shortcuts"), shortcut); Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(shortcut, await launcher.CopyAsync(path)); Assert.Equal(2, server.Posts); Assert.Equal(0, server.Exports); Assert.Empty(Directory.GetFiles(w.Root, "*.url"));
    }
    [WindowsFact]
    public async Task BinaryXlsDefaultRetiresOnlyAfterVerificationAndCanBeConfiguredToCopy()
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, ".xls"); var bytes = FormatTests.Xls(); File.WriteAllBytes(path, bytes); ConfigureLauncher(w, out var storage);
        using var server = new DriveServer(); using var http = new HttpClient(server); var launcher = new WindowsLauncher(storage, http, new LauncherBrowser());
        var shortcut = await launcher.OpenAsync(path); Assert.True(File.Exists(shortcut)); Assert.False(File.Exists(path)); Assert.Contains("IconFile=" + Path.Combine(storage.Root, "shortcut-icon-v1.ico"), File.ReadAllText(shortcut)); Assert.Equal("application/vnd.ms-excel", Assert.Single(server.MediaTypes));
        Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(w.Registry().Pending()).Snapshot!.BackupPath)); Assert.Equal(1, server.Exports);
        var second = Path.Combine(w.Root, "copia.xls"); File.WriteAllBytes(second, bytes);
        await XlsReplacementSettings.SaveAsync(storage, false);
        await launcher.OpenAsync(second); Assert.Equal(bytes, File.ReadAllBytes(second)); Assert.Equal(1, server.Exports);
    }
    [WindowsFact]
    public async Task XlsMismatchPreservesOriginalAndResumesSameUpload()
    {
        using var w = new Workspace(); var path = Path.ChangeExtension(w.Source, ".xls"); var bytes = FormatTests.Xls(); File.WriteAllBytes(path, bytes); ConfigureLauncher(w, out var storage);
        using var server = new DriveServer { ExportOverride = SpreadsheetFormats.WriteXlsx([new SheetValues("wrong", [new object?[] { "wrong" }])]) };
        using var http = new HttpClient(server); var launcher = new WindowsLauncher(storage, http, new LauncherBrowser());
        await Assert.ThrowsAsync<ConversionMismatchException>(() => launcher.OpenAsync(path)); Assert.Equal(bytes, File.ReadAllBytes(path));
        var op = Assert.Single(w.Registry().Pending()); Assert.Equal(bytes, File.ReadAllBytes(op.Snapshot!.BackupPath));
        await XlsReplacementSettings.SaveAsync(storage, false); await Assert.ThrowsAsync<CopyRequiredException>(() => launcher.ResumeAsync(op.Id, true));
        await XlsReplacementSettings.SaveAsync(storage, true); server.ExportOverride = null;
        await launcher.ResumeAsync(op.Id, true); Assert.False(File.Exists(path)); Assert.Equal(2, server.Posts);
    }
    [WindowsFact]
    public async Task CopyImportOnKnownOneDriveRootPreservesOriginalAndReusesTheMapping()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); ConfigureLauncher(w, out var storage); using var server = new DriveServer(); using var http = new HttpClient(server); var launcher = new WindowsLauncher(storage, http, new LauncherBrowser());
        var old = Environment.GetEnvironmentVariable("OneDrive");
        try
        {
            Environment.SetEnvironmentVariable("OneDrive", w.Root);
            await Assert.ThrowsAsync<NotSupportedException>(() => launcher.OpenAsync(w.Source)); Assert.Equal(0, server.Posts);
            var shortcut = await launcher.CopyAsync(w.Source); Assert.True(File.Exists(w.Source)); Assert.Equal(shortcut, await launcher.CopyAsync(w.Source)); Assert.Equal(2, server.Posts);
        }
        finally { Environment.SetEnvironmentVariable("OneDrive", old); }
    }
    [WindowsCiFact]
    public async Task ReadOnlySmbShareCopiesWithoutWritingOrRetiringSource()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); ConfigureLauncher(w, out var storage);
        var share = "SheetsWindowsTest_" + Guid.NewGuid().ToString("N"); var script = Path.Combine(w.Root, "share.ps1");
        File.WriteAllText(script, "param($Action,$Name,$Root)\n$ErrorActionPreference='Stop'\nif ($Action -eq 'create') { New-SmbShare -Name $Name -Path $Root -ReadAccess ([Security.Principal.WindowsIdentity]::GetCurrent().Name) | Out-Null } else { if (Get-SmbShare -Name $Name -ErrorAction SilentlyContinue) { Remove-SmbShare -Name $Name -Force -ErrorAction Stop } }");
        async Task Share(string action)
        {
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, action, share, w.Root }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            await output; var error = await errors;
            Assert.True(process.ExitCode == 0, "Disposable SMB setup/cleanup failed: " + error);
        }
        try
        {
            await Share("create");
            var unc = @"\\localhost\" + share + "\\" + Path.GetFileName(w.Source);
            using var server = new DriveServer(); using var http = new HttpClient(server); var launcher = new WindowsLauncher(storage, http, new LauncherBrowser());
            await Assert.ThrowsAsync<NotSupportedException>(() => launcher.OpenAsync(unc)); Assert.Equal(0, server.Posts);
            var shortcut = await launcher.CopyAsync(unc); Assert.True(File.Exists(shortcut)); Assert.Equal(File.ReadAllBytes(w.Source), File.ReadAllBytes(unc));
            Assert.Equal(shortcut, await launcher.CopyAsync(unc)); Assert.Equal(2, server.Posts); Assert.Empty(Directory.GetFiles(w.Root, "*.url"));
            Assert.StartsWith("network:", Assert.Single(w.Registry().Pending()).SourceKey);
        }
        finally { await Share("remove"); }
    }
    [WindowsFact]
    public async Task LauncherRestartRecoversLostUploadCompletionFromProtectedSession()
    {
        using var w = new Workspace(); var original = Workbook(); File.WriteAllBytes(w.Source, original); ConfigureLauncher(w, out var storage);
        using var server = new DriveServer { LoseSheetResponse = true, DisconnectOnLoss = true }; using var http = new HttpClient(server); var browser = new LauncherBrowser();
        await Assert.ThrowsAsync<HttpRequestException>(() => new WindowsLauncher(storage, http, browser).OpenAsync(w.Source));
        Assert.True(File.Exists(w.Source)); Assert.Empty(browser.Opened); var operation = Assert.Single(w.Registry().Pending());
        Assert.False(new GoogleRemoteRegistry(Path.Combine(storage.Root, "google.db")).Get("sheet:" + operation.Id.ToString("N"))!.Verified);
        Assert.Single(Directory.GetFiles(Path.Combine(storage.Root, "uploads"), "*.session")); server.Offline = false;
        var shortcut = await new WindowsLauncher(storage, http, browser).ResumeAsync(operation.Id, true);
        Assert.False(File.Exists(w.Source)); Assert.True(File.Exists(shortcut)); Assert.Equal(2, server.Posts); Assert.Equal(original, File.ReadAllBytes(operation.Snapshot!.BackupPath));
    }
    [WindowsFact]
    public async Task RecoveryByIdCompletesBrowserFailureWithoutUploadingAgain()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); ConfigureLauncher(w, out var storage);
        using var server = new DriveServer(); using var http = new HttpClient(server); var browser = new LauncherBrowser { Fail = true }; var launcher = new WindowsLauncher(storage, http, browser);
        await Assert.ThrowsAsync<IOException>(() => launcher.OpenAsync(w.Source)); var operation = Assert.Single(w.Registry().Pending());
        Assert.True(File.Exists(w.Source)); browser.Fail = false;
        await launcher.ResumeAsync(operation.Id, true); Assert.False(File.Exists(w.Source)); Assert.Equal(2, server.Posts);
        await launcher.ResumeAsync(operation.Id, true); Assert.Equal(2, server.Posts);
    }
    [WindowsFact]
    public async Task RecoveryByIdCanChooseCopyAndRefusesARecreatedSource()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); ConfigureLauncher(w, out var storage);
        using var server = new DriveServer(); using var http = new HttpClient(server); var browser = new LauncherBrowser { Fail = true }; var launcher = new WindowsLauncher(storage, http, browser);
        await Assert.ThrowsAsync<IOException>(() => launcher.CopyAsync(w.Source)); var operation = Assert.Single(w.Registry().Pending()); browser.Fail = false;
        var shortcut = await launcher.ResumeAsync(operation.Id, false); Assert.True(File.Exists(w.Source)); Assert.True(File.Exists(shortcut)); Assert.Equal(2, server.Posts);
        File.Move(w.Source, w.Source + ".old"); File.WriteAllBytes(w.Source, Workbook());
        await Assert.ThrowsAsync<LocalConflictException>(() => launcher.ResumeAsync(operation.Id, true)); Assert.True(File.Exists(w.Source)); Assert.Equal(2, server.Posts);
    }
    [WindowsFact]
    public async Task ExplorerActivationImportsAndRetiresThroughTheComposedLauncher()
    {
        using var w = new Workspace(); var bytes = Workbook(); File.WriteAllBytes(w.Source, bytes); using var server = new DriveServer(); using var http = new HttpClient(server);
        var storage = new LocalStorage(Path.Combine(w.Root, "state")); const string client = "pilot.apps.googleusercontent.com";
        LauncherConfiguration.SaveClient(storage, "{\"installed\":{\"client_id\":\"" + client + "\"}}"); File.WriteAllText(Path.Combine(storage.Root, "replacement-root.txt"), w.Root);
        new DpapiTokenVault(Path.Combine(storage.Root, "auth"), client).Save(new(client, client + ":user", "test-token", "test-refresh", DateTimeOffset.UtcNow.AddHours(1)));
        var browser = new LauncherBrowser(); var shortcut = await new WindowsLauncher(storage, http, browser).OpenAsync(w.Source);
        Assert.False(File.Exists(w.Source)); Assert.Equal(2, server.Posts); Assert.Single(browser.Opened); Assert.Contains("file_2", File.ReadAllText(shortcut));
        var operation = Assert.Single(w.Registry().Pending()); Assert.Equal(bytes, File.ReadAllBytes(operation.Snapshot!.BackupPath));
        Assert.Equal(4, new ReplacementJournal(Path.Combine(storage.Root, "replacement.db")).Get(operation.Id)!.Step);
    }
    [WindowsFact]
    public async Task ExplorerActivationResumesAfterBrowserFailureWithoutNewUpload()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer(); using var http = new HttpClient(server);
        var storage = new LocalStorage(Path.Combine(w.Root, "state")); const string client = "pilot.apps.googleusercontent.com";
        LauncherConfiguration.SaveClient(storage, "{\"installed\":{\"client_id\":\"" + client + "\"}}"); File.WriteAllText(Path.Combine(storage.Root, "replacement-root.txt"), w.Root);
        new DpapiTokenVault(Path.Combine(storage.Root, "auth"), client).Save(new(client, client + ":user", "test-token", "test-refresh", DateTimeOffset.UtcNow.AddHours(1)));
        var browser = new LauncherBrowser { Fail = true }; var launcher = new WindowsLauncher(storage, http, browser);
        await Assert.ThrowsAsync<IOException>(() => launcher.OpenAsync(w.Source)); Assert.True(File.Exists(w.Source));
        browser.Fail = false; await launcher.OpenAsync(w.Source); Assert.Equal(2, server.Posts); Assert.False(File.Exists(w.Source));
    }
    [Fact]
    public async Task FirstImportVerifiesSheetAndRepeatDoesNotPost()
    {
        using var w = new Workspace(); var bytes = Workbook(); File.WriteAllBytes(w.Source, bytes); using var server = new DriveServer(); var importer = Importer(w, server);
        var first = await importer.ImportAsync(w.Source); var second = await importer.ImportAsync(w.Source);
        Assert.Equal(first, second); Assert.Equal(2, server.Posts); Assert.DoesNotContain("PATCH", server.Methods); Assert.DoesNotContain("DELETE", server.Methods); Assert.Equal(bytes, File.ReadAllBytes(w.Source));
        var op = Assert.Single(w.Registry().Pending()); var remote = new GoogleRemoteRegistry(Path.Combine(w.Root, "state", "google.db")).Get("sheet:" + op.Id.ToString("N")); Assert.True(remote!.Verified);
    }
    [Fact]
    public async Task ConcurrentCallsCreateOnlyOneFolderAndSheet()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer(); var importer = Importer(w, server);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => importer.ImportAsync(w.Source))); Assert.Single(results.Distinct()); Assert.Equal(2, server.Posts);
    }
    [Fact]
    public async Task LostSheetResponseIsReconciledWithoutAnotherUpload()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer { LoseSheetResponse = true }; var importer = Importer(w, server);
        await Assert.ThrowsAsync<HttpRequestException>(() => importer.ImportAsync(w.Source)); var url = await importer.ImportAsync(w.Source); Assert.Contains("file_2", url.AbsoluteUri); Assert.Equal(2, server.Posts);
    }
    [Fact]
    public async Task LostFolderResponseIsReconciledWithoutAnotherFolder()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer { LoseFolderResponse = true }; var importer = Importer(w, server);
        await Assert.ThrowsAsync<HttpRequestException>(() => importer.ImportAsync(w.Source)); await importer.ImportAsync(w.Source); Assert.Equal(2, server.Posts);
    }
    [Fact]
    public async Task SearchLagStopsInsteadOfDuplicating()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer { LoseSheetResponse = true }; var importer = Importer(w, server);
        await Assert.ThrowsAsync<HttpRequestException>(() => importer.ImportAsync(w.Source)); server.ListLag = true;
        await Assert.ThrowsAsync<ReconciliationRequiredException>(() => importer.ImportAsync(w.Source)); Assert.Equal(2, server.Posts); Assert.True(File.Exists(w.Source));
    }
    [Fact]
    public async Task DuplicateMarkersStopReconciliation()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer { LoseSheetResponse = true }; var importer = Importer(w, server);
        await Assert.ThrowsAsync<HttpRequestException>(() => importer.ImportAsync(w.Source)); server.DuplicateList = true;
        await Assert.ThrowsAsync<ReconciliationRequiredException>(() => importer.ImportAsync(w.Source)); Assert.Equal(2, server.Posts);
    }
    [Fact]
    public async Task InvalidConversionNeverBecomesVerified()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer { WrongMime = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => Importer(w, server).ImportAsync(w.Source));
        var op = Assert.Single(w.Registry().Pending()); Assert.False(new GoogleRemoteRegistry(Path.Combine(w.Root, "state", "google.db")).Get("sheet:" + op.Id.ToString("N"))!.Verified); Assert.True(File.Exists(w.Source));
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TrashedOrReadOnlyConversionIsNotVerified(bool trashed, bool denyEdit)
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer { Trashed = trashed, DenyEdit = denyEdit };
        await Assert.ThrowsAsync<InvalidDataException>(() => Importer(w, server).ImportAsync(w.Source));
        var op = Assert.Single(w.Registry().Pending()); Assert.False(new GoogleRemoteRegistry(Path.Combine(w.Root, "state", "google.db")).Get("sheet:" + op.Id.ToString("N"))!.Verified); Assert.True(File.Exists(w.Source));
    }
    [Fact]
    public async Task OversizedWorkbookIsRejectedBeforeAnyPost()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, new byte[GoogleImport.MaxBytes + 1]); using var server = new DriveServer();
        await Assert.ThrowsAsync<SpreadsheetCapacityException>(() => Importer(w, server).ImportAsync(w.Source)); Assert.Equal(0, server.Posts); Assert.Empty(w.Registry().Pending());
    }
    [Fact]
    public async Task DeletedRemoteIsNotRecreated()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer(); var importer = Importer(w, server); await importer.ImportAsync(w.Source); server.Files.Remove("file_2");
        await Assert.ThrowsAsync<GoogleApiException>(() => importer.ImportAsync(w.Source)); Assert.Equal(2, server.Posts);
    }
    [Fact]
    public async Task ChangedLocalFileCannotOverwriteRemote()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer(); var importer = Importer(w, server); await importer.ImportAsync(w.Source); File.WriteAllBytes(w.Source, [9]);
        await Assert.ThrowsAsync<LocalConflictException>(() => importer.ImportAsync(w.Source)); Assert.Equal(2, server.Posts); Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(w.Source));
    }
    [Fact]
    public async Task InvalidPackageAndMacrosHaveNoRemoteEffects()
    {
        using var w = new Workspace(); using var server = new DriveServer(); await Assert.ThrowsAsync<InvalidDataException>(() => Importer(w, server).ImportAsync(w.Source)); Assert.Equal(0, server.Posts);
        using var second = new Workspace(); File.WriteAllBytes(second.Source, Workbook(true)); await Assert.ThrowsAsync<NotSupportedException>(() => Importer(second, server).ImportAsync(second.Source)); Assert.Equal(0, server.Posts);
    }
    [Fact]
    public async Task Get401RefreshesOnceButDoesNotRepeatCreation()
    {
        using var server = new DriveServer { Get401 = true }; server.Files["known"] = new { id = "known", mimeType = GoogleDriveClient.SheetMime, trashed = false, capabilities = new { canEdit = true }, appProperties = new Dictionary<string, string>() }; var auth = new FixedAuth(); using var http = new HttpClient(server);
        var result = await new GoogleDriveClient(http, auth).GetAsync("A", "known", default); Assert.Equal("known", result.Id); Assert.Equal(1, auth.Refreshes); Assert.Equal(0, server.Posts);
    }
    [Fact]
    public void CallbackRequiresStatePathAndUniqueParameters()
    {
        var proof = new OAuthProof(); Assert.Null(proof.Callback("/callback?code=x&state=wrong", "/callback")); Assert.Null(proof.Callback("/other?code=x&state=" + proof.State, "/callback"));
        Assert.Null(proof.Callback("/callback?code=x&code=y&state=" + proof.State, "/callback")); Assert.Equal("x", proof.Callback("/callback?code=x&state=" + proof.State, "/callback"));
        Assert.Throws<AuthorizationRequiredException>(() => proof.Callback("/callback?error=access_denied&state=" + proof.State, "/callback"));
        Assert.Equal(43, proof.Verifier.Length); Assert.Contains("code_challenge_method=S256", proof.AuthorizationUri("client", new("http://127.0.0.1:4321/callback")).Query);
    }
    [Fact]
    public void WebClientCredentialsAreRejected()
    { Assert.Throws<InvalidDataException>(() => OAuthClient.FromJson("{\"web\":{\"client_id\":\"id\"}}")); }
    private sealed class MemoryVault : ITokenVault
    { public GoogleTokens? Tokens; public GoogleTokens? Load() => Tokens; public void Save(GoogleTokens t) => Tokens = t; }
    private sealed class Receiver : IAuthorizationReceiver
    {
        public OAuthProof? Proof;
        public Task<AuthorizationCode> ReceiveAsync(OAuthClient client, OAuthProof proof, CancellationToken ct) { Proof = proof; return Task.FromResult(new AuthorizationCode("auth-code", new("http://127.0.0.1:4321/callback"))); }
    }
    private sealed class OAuthServer : HttpMessageHandler
    {
        public bool Revoked, DeniedScope, OtherAccount; public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.RequestUri!.AbsolutePath == "/token")
            {
                LastBody = await req.Content!.ReadAsStringAsync(ct); if (Revoked) return new(HttpStatusCode.BadRequest);
                var values = new Dictionary<string, object> { { "access_token", "new-access" }, { "token_type", "Bearer" }, { "expires_in", 3600 }, { "scope", DeniedScope ? "other" : GoogleOAuth.Scope } };
                if (LastBody.Contains("grant_type=authorization_code", StringComparison.Ordinal)) values["refresh_token"] = "initial-refresh";
                return Json(values);
            }
            return Json(new { user = new { permissionId = OtherAccount ? "other" : "user" } });
        }
    }
    [Fact]
    public async Task RefreshPreservesOldRefreshTokenAndChecksAccount()
    {
        using var w = new Workspace(); var vault = new MemoryVault { Tokens = new("client", "client:user", "expired", "refresh", DateTimeOffset.UtcNow.AddHours(-1)) }; using var server = new OAuthServer(); using var http = new HttpClient(server);
        var oauth = new GoogleOAuth(http, new("client", null), vault, new Receiver(), new FileOperationLock(w.Locks)); var access = await oauth.AccessAsync();
        Assert.Equal("new-access", access.Token); Assert.Equal("refresh", vault.Tokens!.RefreshToken); Assert.Contains("grant_type=refresh_token", server.LastBody); Assert.DoesNotContain("refresh", vault.Tokens.ToString());
    }
    [Fact]
    public async Task RevokedRefreshRequiresLoginAndDoesNotReplaceVault()
    {
        using var w = new Workspace(); var initial = new GoogleTokens("client", "client:user", "expired", "refresh", DateTimeOffset.UtcNow.AddHours(-1)); var vault = new MemoryVault { Tokens = initial }; using var server = new OAuthServer { Revoked = true }; using var http = new HttpClient(server);
        await Assert.ThrowsAsync<AuthorizationRequiredException>(() => new GoogleOAuth(http, new("client", null), vault, new Receiver(), new FileOperationLock(w.Locks)).AccessAsync()); Assert.Same(initial, vault.Tokens);
    }
    [Fact]
    public async Task RefreshedAccountMismatchIsRejected()
    {
        using var w = new Workspace(); var initial = new GoogleTokens("client", "client:user", "expired", "refresh", DateTimeOffset.UtcNow.AddHours(-1)); var vault = new MemoryVault { Tokens = initial }; using var server = new OAuthServer { OtherAccount = true }; using var http = new HttpClient(server);
        await Assert.ThrowsAsync<AuthorizationRequiredException>(() => new GoogleOAuth(http, new("client", null), vault, new Receiver(), new FileOperationLock(w.Locks)).AccessAsync()); Assert.Same(initial, vault.Tokens);
    }
    [Fact]
    public async Task TokenWithoutRequestedScopeIsRejected()
    {
        using var w = new Workspace(); var vault = new MemoryVault { Tokens = new("client", "client:user", "expired", "refresh", DateTimeOffset.UtcNow.AddHours(-1)) }; using var server = new OAuthServer { DeniedScope = true }; using var http = new HttpClient(server);
        await Assert.ThrowsAsync<AuthorizationRequiredException>(() => new GoogleOAuth(http, new("client", null), vault, new Receiver(), new FileOperationLock(w.Locks)).AccessAsync());
    }
    [Fact]
    public async Task ExistingSheetOpensEvenAfterImportFolderWasRemoved()
    {
        using var w = new Workspace(); File.WriteAllBytes(w.Source, Workbook()); using var server = new DriveServer(); var importer = Importer(w, server);
        var first = await importer.ImportAsync(w.Source); server.Files.Remove("file_1"); Assert.Equal(first, await importer.ImportAsync(w.Source)); Assert.Equal(2, server.Posts);
    }
    [Fact]
    public async Task AuthorizationCodeExchangeSendsPkceAndPersistsIdentity()
    {
        using var w = new Workspace(); var vault = new MemoryVault(); var receiver = new Receiver(); using var server = new OAuthServer(); using var http = new HttpClient(server);
        var result = await new GoogleOAuth(http, new("client", null), vault, receiver, new FileOperationLock(w.Locks)).ConnectAsync();
        Assert.Equal("client:user", result.AccountId); Assert.Equal("initial-refresh", vault.Tokens!.RefreshToken);
        Assert.Contains("code_verifier=" + receiver.Proof!.Verifier, server.LastBody); Assert.Contains("grant_type=authorization_code", server.LastBody);
    }
    [WindowsFact]
    public async Task LoopbackListenerIsReadyBeforeBrowserAndChecksState()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new LoopbackAuthorizationReceiver(uri =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var parameters = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
                    using var http = new HttpClient(); var redirect = parameters["redirect_uri"];
                    using var invalid = await http.GetAsync(redirect + "?code=bad&state=wrong"); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                    using var valid = await http.GetAsync(redirect + "?code=valid&state=" + parameters["state"]); Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
                    completion.SetResult();
                }
                catch (Exception ex) { completion.SetException(ex); }
            });
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await receiver.ReceiveAsync(new("client", null), new OAuthProof(), timeout.Token); await completion.Task;
        Assert.Equal("valid", result.Code);
    }
    [WindowsFact]
    public void TokenVaultUsesDpapiAndRoundTrips()
    {
        using var w = new Workspace(); var vault = new DpapiTokenVault(Path.Combine(w.Root, "auth"), "client"); var tokens = new GoogleTokens("client", "client:user", "access-secret", "refresh-secret", DateTimeOffset.UtcNow.AddHours(1)); vault.Save(tokens);
        Assert.Equal(tokens.RefreshToken, vault.Load()!.RefreshToken); Assert.DoesNotContain("refresh-secret", Encoding.UTF8.GetString(File.ReadAllBytes(Assert.Single(Directory.GetFiles(Path.Combine(w.Root, "auth"))))));
    }
}
