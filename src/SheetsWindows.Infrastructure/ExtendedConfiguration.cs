using System.Text.Json;

namespace SheetsWindows.Infrastructure;

public static class ExtendedConfiguration
{
    private static string SettingsPath(LocalStorage storage) => Path.Combine(storage.Root, "formats.json");
    public static void Save(LocalStorage storage, TextImportOptions options, bool enabled)
    {
        if (!enabled) return;
        if (options.Encoding is not ("auto" or "windows-1252") || options.Delimiter is not ("auto" or "comma" or "semicolon")) throw new ArgumentException("Invalid text settings.");
        PrivateDirectory.Create(storage.Root); var path = SettingsPath(storage);
        if (File.Exists(path))
        {
            if (Load(storage) != options) throw new InvalidOperationException("Changing interpretation requires migration.");
            return;
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(file, options); file.Flush(true); }
            File.Move(temp, path, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static TextImportOptions Load(LocalStorage storage)
    {
        var path = SettingsPath(storage);
        if (!File.Exists(path)) throw new LauncherNotConfiguredException();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 1024 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid text settings.");
        var options = JsonSerializer.Deserialize<TextImportOptions>(file) ?? throw new InvalidDataException("Invalid text settings.");
        if (options.Encoding is not ("auto" or "windows-1252") || options.Delimiter is not ("auto" or "comma" or "semicolon")) throw new InvalidDataException("Invalid text settings.");
        return options;
    }
}
