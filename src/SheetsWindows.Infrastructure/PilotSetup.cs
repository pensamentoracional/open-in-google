using System.Text;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public static class PilotSetup
{
    public static string PolicyPath(LocalStorage storage) => Path.Combine(storage.Root, "replacement-root.txt");
    public static void Configure(LocalStorage storage, string clientJson, string folder, bool acknowledged)
    {
        if (!acknowledged) throw new ArgumentException("Explicit unsynced-folder and conversion acknowledgement required.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(root) || root == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!)) throw new ArgumentException("Dedicated existing directory required.");
        for (var current = root; current is not null; current = Path.GetDirectoryName(current))
            if (((int)File.GetAttributes(current) & (0x400 | 0x1000 | 0x40000 | 0x400000)) != 0) throw new NotSupportedException("Redirected or cloud folder excluded.");
        if (OperatingSystem.IsWindows())
        {
            if (new DriveInfo(Path.GetPathRoot(root)!).DriveType != DriveType.Fixed) throw new NotSupportedException("Fixed drive required.");
            foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                var sync = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrEmpty(sync)) continue;
                var syncRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sync));
                if (root.Equals(syncRoot, StringComparison.OrdinalIgnoreCase) || root.StartsWith(syncRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Synced folder excluded.");
            }
        }
        var policy = PolicyPath(storage);
        if (File.Exists(policy) && !Path.GetFullPath(File.ReadAllText(policy)).Equals(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new LocalConflictException("Existing folder policy must be preserved.");
        LauncherConfiguration.SaveClient(storage, clientJson);
        if (File.Exists(policy)) return;
        var temp = policy + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(Encoding.UTF8.GetBytes(root)); file.Flush(true); }
            File.Move(temp, policy, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record RecoveryEntry(Guid Id, string OriginalPath, long Bytes, int? ReplacementStep, DateTimeOffset? CapturedAt = null, bool Available = true, bool CanClean = false, int CleanupState = 0)
{
    public override string ToString() => $"{Path.GetFileName(OriginalPath)} — {Bytes:N0} bytes — {CapturedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "data desconhecida"} — {(CleanupState == 2 ? "limpo" : CleanupState == 1 ? "limpeza pendente" : !Available ? "ausente" : CanClean ? "concluído" : "protegido / pendente")} — {Id}";
}
public sealed class BackupRecovery(LocalStorage storage)
{
    public IReadOnlyList<RecoveryEntry> List()
    {
        var path = Path.Combine(storage.Root, "replacement.db");
        var records = File.Exists(path) ? new ReplacementJournal(path).All() : [];
        return new BackupManagement(storage).Inspect().Entries.Select(entry =>
        {
            var record = records.GetValueOrDefault(entry.Id);
            return new RecoveryEntry(entry.Id,record?.SourcePath??entry.Operation.SourcePath,entry.Operation.Snapshot!.Length,record?.Step,entry.CapturedAt,entry.Available,entry.CanClean,entry.CleanupState);
        }).ToArray();
    }
    public async Task RestoreAsync(Guid id, string destination, CancellationToken ct = default)
    {
        var op = new SqliteOperationRegistry(storage.DatabasePath).Get(id) ?? throw new KeyNotFoundException();
        var snapshot = op.Snapshot ?? throw new InvalidDataException("No committed backup.");
        await using var held = await new FileOperationLock(storage.LocksPath).AcquireAsync(op.SourceKey, ct);
        await new ManagedBackupStore(storage).RestoreAsync(id, snapshot, destination, ct);
    }
}
