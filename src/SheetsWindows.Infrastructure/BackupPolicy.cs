using System.Text.Json;

namespace SheetsWindows.Infrastructure;

public sealed record BackupPolicy(int RetentionDays = 30, int QuotaMb = 200, bool AutomaticCleanup = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long QuotaBytes => QuotaMb * 1_000_000L;
    public void Validate() { if (RetentionDays is < 1 or > 365 || QuotaMb is < 1 or > 1000) throw new InvalidDataException("Invalid backup policy."); }
    private static string PathFor(LocalStorage storage) => Path.Combine(storage.Root, "backup-policy.json");
    public static BackupPolicy Load(LocalStorage storage)
    {
        var path = PathFor(storage); if (!File.Exists(path)) return new();
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid backup policy.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 2048) throw new InvalidDataException("Invalid backup policy.");
        var policy = JsonSerializer.Deserialize<BackupPolicy>(file) ?? throw new InvalidDataException("Invalid backup policy."); policy.Validate(); return policy;
    }
    public static async Task SaveAsync(LocalStorage storage, BackupPolicy policy, CancellationToken ct = default)
    {
        policy.Validate(); PrivateDirectory.Create(storage.Root);
        await using var held = await new FileOperationLock(storage.LocksPath).AcquireAsync(BackupManagement.LockKey, ct);
        var path = PathFor(storage); if (File.Exists(path)) _ = Load(storage);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(file, policy); file.Flush(true); } File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public sealed class BackupQuotaException() : IOException("Backup quota exceeded.");
public sealed class BackupRemovedException() : IOException("Backup cleanup was recorded; restore unavailable.");
