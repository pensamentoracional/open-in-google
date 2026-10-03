using System.Text.Json;

namespace SheetsWindows.Infrastructure;

public enum ApplicationTheme { Light, Dark }
public static class ThemeSettings
{
    private static string PathFor(LocalStorage storage) => Path.Combine(storage.Root, "theme.json");
    public static ApplicationTheme Load(LocalStorage storage)
    {
        var path = PathFor(storage);
        if (!File.Exists(path)) return ApplicationTheme.Light;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid theme settings.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 1024) throw new InvalidDataException("Invalid theme settings.");
        return JsonSerializer.Deserialize<string>(file) switch { "light" => ApplicationTheme.Light, "dark" => ApplicationTheme.Dark, _ => throw new InvalidDataException("Invalid theme settings.") };
    }
    public static async Task SaveAsync(LocalStorage storage, ApplicationTheme theme, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentException("Invalid theme.");
        PrivateDirectory.Create(storage.Root);
        await using var held = await new FileOperationLock(storage.LocksPath).AcquireAsync("theme-settings", ct);
        var path = PathFor(storage); if (File.Exists(path)) _ = Load(storage);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(file, theme == ApplicationTheme.Light ? "light" : "dark"); file.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
