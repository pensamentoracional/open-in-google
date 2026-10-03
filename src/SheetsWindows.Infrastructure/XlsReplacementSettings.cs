using System.Text.Json;

namespace SheetsWindows.Infrastructure;

public static class XlsReplacementSettings
{
    private static string PathFor(LocalStorage storage) => Path.Combine(storage.Root, "xls-replacement.json");
    public static bool Load(LocalStorage storage)
    {
        var path = PathFor(storage);
        if (!File.Exists(path)) return true;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid XLS settings.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 1024) throw new InvalidDataException("Invalid XLS settings.");
        return JsonSerializer.Deserialize<bool>(file);
    }
    public static async Task SaveAsync(LocalStorage storage, bool replace, CancellationToken ct = default)
    {
        PrivateDirectory.Create(storage.Root);
        await using var held = await new FileOperationLock(storage.LocksPath).AcquireAsync("xls-settings", ct);
        var path = PathFor(storage); if (File.Exists(path)) _ = Load(storage);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(file, replace); file.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
