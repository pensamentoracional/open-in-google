using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Xml;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed class GoogleImport(LocalPreparation preparation, IOperationRegistry local, IRemoteRegistry remote,
    ISourceReader sources, IOperationLock locks, IGoogleAuth auth, GoogleDriveClient drive, TextImportOptions? textOptions = null)
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public async Task<Uri> ImportAsync(string path, CancellationToken ct = default) => (await ImportReceiptAsync(path, ct)).Url;
    public async Task<ImportReceipt> ImportReceiptAsync(string path, CancellationToken ct = default)
    {
        await using (var preflight = sources.Open(path))
            if (preflight.Content.Length > MaxBytes) throw new NotSupportedException("Imports support files up to 5 MiB.");
        var access = await auth.AccessAsync(cancellationToken: ct);
        var op = await preparation.PrepareAsync(access.AccountId, path, ct);
        await using var source = sources.Open(path);
        if (source.Source.IdentityKey != op.SourceKey) throw new LocalConflictException("Source identity changed; upload blocked.");
        await using var held = await locks.AcquireAsync(source.Source.IdentityKey, ct);
        op = local.Get(op.Id)!;
        var snapshot = op.Snapshot ?? throw new InvalidOperationException("Snapshot required.");
        source.Content.Position = 0;
        if (Convert.ToHexString(await SHA256.HashDataAsync(source.Content, ct)) != snapshot.Sha256) throw new LocalConflictException("Source changed; upload blocked.");
        // Hold the backup handle while checking and freezing the bytes that will be sent.
        await using var backup = new FileStream(snapshot.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (backup.Length > MaxBytes) throw new NotSupportedException("Imports support files up to 5 MiB.");
        using var buffer = new MemoryStream(); await backup.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();
        if (bytes.LongLength != snapshot.Length || Convert.ToHexString(SHA256.HashData(bytes)) != snapshot.Sha256) throw new InvalidDataException("Snapshot integrity failure.");
        var payload = SpreadsheetFormats.Prepare(op.Format, bytes, textOptions);
        var canReplace = op.Format == "xlsx" || payload.Expected is not null;
        var sheetKey = "sheet:" + op.Id.ToString("N");
        if (remote.Get(sheetKey) is { } pending)
        {
            if (pending.AccountId != access.AccountId || pending.Hash != snapshot.Sha256) throw new LocalConflictException("Upload binding changed.");
            if (pending.FileId is null && drive.CanResume(pending, payload.Bytes, payload.MimeType))
            {
                try { remote.Candidate(sheetKey, await drive.ResumeAsync(pending, payload.Bytes, payload.MimeType, ct)); }
                catch (ReconciliationRequiredException) { /* Reconcile the marker; never repeat initiation. */ }
            }
            var known = await EnsureAsync(sheetKey, access.AccountId, "sheet", snapshot.Sha256,
                _ => throw new ReconciliationRequiredException(), ct);
            return new ImportReceipt(op, GoogleDriveClient.Editor(known), source.Source.Path, canReplace);
        }
        var folderKey = "folder:" + access.AccountId;
        string folder;
        await using (var folderLock = await locks.AcquireAsync(folderKey, ct))
            folder = await EnsureAsync(folderKey, access.AccountId, "folder", "", a => drive.CreateAsync(a, "Sheets Windows", null, null, ct), ct);
        var id = await EnsureAsync(sheetKey, access.AccountId, "sheet", snapshot.Sha256,
            a => drive.CreateAsync(a, Path.GetFileNameWithoutExtension(path), folder, payload.Bytes, ct, payload.MimeType), ct);
        return new ImportReceipt(op, GoogleDriveClient.Editor(id), source.Source.Path, canReplace);
    }
    private async Task<string> EnsureAsync(string key, string account, string kind, string hash, Func<RemoteAttempt, Task<string>> create, CancellationToken ct)
    {
        var attempt = remote.Get(key);
        if (attempt is null)
        {
            attempt = remote.Begin(key, account, kind, hash); // commit intent BEFORE touching Google
            var candidate = await create(attempt); remote.Candidate(key, candidate);
            attempt = remote.Get(key)!;
        }
        if (attempt.AccountId != account || attempt.Kind != kind || attempt.Hash != hash) throw new InvalidOperationException("Association binding changed.");
        GoogleFile file;
        if (attempt.FileId is not null) file = await drive.GetAsync(account, attempt.FileId, ct);
        else
        {
            var found = await drive.FindAsync(attempt, ct);
            if (found.Count != 1) throw new ReconciliationRequiredException();
            file = found[0]; remote.Candidate(key, file.Id);
        }
        if (file.Trashed || !file.CanEdit || file.MimeType != (kind == "sheet" ? GoogleDriveClient.SheetMime : GoogleDriveClient.FolderMime)
            || !file.Properties.TryGetValue("sw_operation", out var marker) || marker != attempt.Marker
            || !file.Properties.TryGetValue("sw_hash", out var remoteHash) || remoteHash != hash)
            throw new InvalidDataException("Remote conversion or association validation failed.");
        remote.Verify(key, file.Id); return file.Id;
    }
    public static void ValidateXlsx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (zip.Entries.Count > 10000 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count) throw new InvalidDataException("Ambiguous or oversized ZIP index.");
        if (zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))) throw new NotSupportedException("Macros are not supported.");
        var types = zip.GetEntry("[Content_Types].xml") ?? throw new InvalidDataException("Not an XLSX package.");
        var workbook = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("Workbook missing.");
        if (types.Length > 1024 * 1024 || workbook.Length > 1024 * 1024) throw new InvalidDataException("Workbook metadata too large.");
        using var t = types.Open(); using var tr = XmlReader.Create(t, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 }); var xml = XDocument.Load(tr);
        if (!xml.Descendants().Any(e => e.Name.LocalName == "Override" && (string?)e.Attribute("PartName") == "/xl/workbook.xml" && (string?)e.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")) throw new InvalidDataException("Unsupported workbook content type.");
        using var w = workbook.Open(); using var wr = XmlReader.Create(w, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 }); var book = XDocument.Load(wr);
        if (book.Root?.Name != XName.Get("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")) throw new InvalidDataException("Invalid workbook XML.");
    }
}
