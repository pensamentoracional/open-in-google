using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed record ImportReceipt(ImportOperation Operation, Uri Url, string? SourcePath = null, bool CanReplace = true);
public sealed record ReplacementRecord(Guid OperationId, string ShortcutPath, string Url, int Step, string? SourcePath = null);
public interface IRetirementLease : ISourceLease { void Retire(); }
public interface IRetirementReader { IRetirementLease Open(string path); }
public interface IBrowserLauncher { void Open(Uri url); }
public sealed class BrowserLauncher : IBrowserLauncher
{
    public void Open(Uri url) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
}

// A separate durable journal: Google and filesystem effects are reconciled, never described as one transaction.
public sealed class ReplacementJournal
{
    private readonly string connection;
    public ReplacementJournal(string path)
    {
        PrivateDirectory.Create(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Pooling = false }.ToString();
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(cmd.ExecuteScalar());
        if (version > 2) throw new NotSupportedException("Newer replacement journal.");
        if (version == 1) { cmd.CommandText = "ALTER TABLE replacements ADD COLUMN source_path TEXT"; cmd.ExecuteNonQuery(); }
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS replacements(id TEXT PRIMARY KEY,path TEXT NOT NULL,url TEXT NOT NULL,step INTEGER NOT NULL CHECK(step BETWEEN 0 AND 4),source_path TEXT); CREATE TABLE IF NOT EXISTS events(seq INTEGER PRIMARY KEY,id TEXT NOT NULL,step INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS failures(seq INTEGER PRIMARY KEY,id TEXT NOT NULL,code TEXT NOT NULL); PRAGMA user_version=2;";
        cmd.ExecuteNonQuery(); tx.Commit();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connection); db.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous=FULL"; cmd.ExecuteNonQuery(); return db;
    }
    public ReplacementRecord? Get(Guid id)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT path,url,step,source_path FROM replacements WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        using var r = cmd.ExecuteReader(); return r.Read() ? new(id, r.GetString(0), r.GetString(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3)) : null;
    }
    public ReplacementRecord Begin(Guid id, string path, Uri url, string? sourcePath = null)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO replacements(id,path,url,step,source_path) VALUES($id,$path,$url,0,$source)";
        cmd.Parameters.AddWithValue("$id", id.ToString("N")); cmd.Parameters.AddWithValue("$path", path); cmd.Parameters.AddWithValue("$url", url.AbsoluteUri); cmd.Parameters.AddWithValue("$source", (object?)sourcePath ?? DBNull.Value); cmd.ExecuteNonQuery(); return Get(id)!;
    }
    public void RecordFailure(Guid id, string code)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO failures(id,code) VALUES($id,$code)";
        cmd.Parameters.AddWithValue("$id", id.ToString("N")); cmd.Parameters.AddWithValue("$code", code); cmd.ExecuteNonQuery();
    }
    public void Advance(Guid id, int expected, int next)
    {
        if (next != expected + 1) throw new ArgumentException("Sequential transitions required.");
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "UPDATE replacements SET step=$next WHERE id=$id AND step=$old";
        cmd.Parameters.AddWithValue("$id", id.ToString("N")); cmd.Parameters.AddWithValue("$next", next); cmd.Parameters.AddWithValue("$old", expected);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Replacement state changed.");
        cmd.CommandText = "INSERT INTO events(id,step) VALUES($id,$next)"; cmd.ExecuteNonQuery(); tx.Commit();
    }
}

public static class InternetShortcut
{
    public static byte[] Bytes(Uri url)
    {
        var parts = url.AbsolutePath.Split('/');
        if (url.Scheme != "https" || url.Host != "docs.google.com" || !url.IsDefaultPort || url.UserInfo != "" || url.Query != "" || url.Fragment != ""
            || parts.Length != 5 || parts[1] != "spreadsheets" || parts[2] != "d" || parts[4] != "edit") throw new ArgumentException("Invalid Sheets editor URL.");
        GoogleDriveClient.ValidateId(parts[3]);
        if (GoogleDriveClient.Editor(parts[3]) != url) throw new ArgumentException("Noncanonical URL.");
        return Encoding.UTF8.GetBytes("[InternetShortcut]\r\nURL=" + url.AbsoluteUri + "\r\n");
    }
    public static string Choose(string source, Guid id)
    {
        var first = Path.ChangeExtension(source, ".url");
        if (!File.Exists(first) && !Directory.Exists(first)) return first;
        var second = source + ".url";
        if (!File.Exists(second) && !Directory.Exists(second)) return second;
        return source + "." + id.ToString("N") + ".url";
    }
    public static void Publish(string path, byte[] bytes)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            File.Move(tmp, path, overwrite: false);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static FileStream Hold(string path, byte[] expected)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || stream.Length != expected.Length) throw new LocalConflictException("Shortcut changed.");
            var bytes = new byte[expected.Length]; stream.ReadExactly(bytes);
            if (!bytes.SequenceEqual(expected)) throw new LocalConflictException("Shortcut changed.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}

public sealed class ReplacementCoordinator(IOperationRegistry local, IRemoteRegistry remote, IBackupStore backups,
    IOperationLock locks, ReplacementJournal journal, IRetirementReader sources, IBrowserLauncher browser, IConversionVerifier? conversion = null)
{
    public async Task<string> ReplaceAsync(ImportReceipt receipt, CancellationToken ct = default)
    {
        try { return await RunAsync(receipt, ct); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidDataException or IOException or InvalidOperationException or ArgumentException or NotSupportedException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            journal.RecordFailure(receipt.Operation.Id, ex is LocalConflictException ? "local_conflict" : ex is OperationCanceledException ? "cancelled" : "replacement_failed");
            throw;
        }
    }
    private async Task<string> RunAsync(ImportReceipt receipt, CancellationToken ct)
    {
        var supplied = receipt.Operation;
        await using var held = await locks.AcquireAsync(supplied.SourceKey, ct);
        var op = local.Get(supplied.Id) ?? throw new InvalidOperationException("Missing local association.");
        if (op != supplied) throw new LocalConflictException("Local association changed.");
        var snapshot = op.Snapshot ?? throw new InvalidOperationException("Backup required.");
        var mapping = remote.Get("sheet:" + op.Id.ToString("N"));
        if (mapping is not { Verified: true, Kind: "sheet", FileId: not null } || mapping.AccountId != op.AccountId || mapping.Hash != snapshot.Sha256
            || GoogleDriveClient.Editor(mapping.FileId) != receipt.Url) throw new InvalidOperationException("Verified Google association required.");
        var bytes = InternetShortcut.Bytes(receipt.Url);
        await backups.VerifyAsync(op.Id, snapshot, ct);
        // Keep verified backup protected against mutation until source retirement finishes.
        await using var backup = new FileStream(snapshot.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (backup.Length != snapshot.Length || Convert.ToHexString(await SHA256.HashDataAsync(backup, ct)) != snapshot.Sha256) throw new InvalidDataException("Backup changed.");
        var sourcePath = Path.GetFullPath(receipt.SourcePath ?? op.SourcePath);
        var record = journal.Get(op.Id) ?? journal.Begin(op.Id, InternetShortcut.Choose(sourcePath, op.Id), receipt.Url, sourcePath);
        if (record.Url != receipt.Url.AbsoluteUri || (record.SourcePath ?? op.SourcePath) != sourcePath || Path.GetDirectoryName(record.ShortcutPath) != Path.GetDirectoryName(sourcePath)) throw new LocalConflictException("Replacement binding changed.");
        if (record.Step == 0)
        {
            // A crash may leave our complete file before its journal transition; verify exact bytes on resume.
            if (!File.Exists(record.ShortcutPath)) InternetShortcut.Publish(record.ShortcutPath, bytes);
            using var check = InternetShortcut.Hold(record.ShortcutPath, bytes);
            journal.Advance(op.Id, 0, 1); record = record with { Step = 1 };
        }
        using var shortcut = InternetShortcut.Hold(record.ShortcutPath, bytes);
        if (record.Step == 4) return record.ShortcutPath; // Never touch a recreated original after completion.
        var sourceExists = true;
        try { _ = File.GetAttributes(sourcePath); }
        catch (FileNotFoundException) { sourceExists = false; }
        catch (DirectoryNotFoundException) { sourceExists = false; }
        if (!sourceExists)
        {
            if (record.Step != 3) throw new LocalConflictException("Original missing before retirement intent.");
            journal.Advance(op.Id, 3, 4); return record.ShortcutPath;
        }
        await using (var source = sources.Open(sourcePath))
        {
            if (source.Source.IdentityKey != op.SourceKey || source.Content.Length != snapshot.Length
                || Convert.ToHexString(await SHA256.HashDataAsync(source.Content, ct)) != snapshot.Sha256) throw new LocalConflictException("Original changed; retirement blocked.");
            if (op.Format != "xlsx")
            {
                if (conversion is null || !receipt.CanReplace) throw new CopyRequiredException();
                await conversion.VerifyAsync(op, mapping, ct);
            }
            if (record.Step == 1)
            {
                browser.Open(receipt.Url); // Acceptance by shell, not proof that the page loaded.
                journal.Advance(op.Id, 1, 2); record = record with { Step = 2 };
            }
            ct.ThrowIfCancellationRequested();
            if (record.Step == 2) { journal.Advance(op.Id, 2, 3); record = record with { Step = 3 }; }
            source.Retire(); // Exact held file, no delete-by-path race.
        }
        journal.Advance(op.Id, 3, 4); return record.ShortcutPath;
    }
}
