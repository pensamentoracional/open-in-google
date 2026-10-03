using System.Security.Cryptography;

namespace SheetsWindows.Infrastructure;

public static class ShortcutIcon
{
    public static string Ensure(LocalStorage storage)
    {
        PrivateDirectory.Create(storage.Root);
        var path = Path.Combine(storage.Root, "shortcut-icon-v1.ico");
        using var resource = typeof(ShortcutIcon).Assembly.GetManifestResourceStream("Brand.shortcut.ico")!;
        using var buffer = new MemoryStream(); resource.CopyTo(buffer); var bytes = buffer.ToArray();
        if (!File.Exists(path))
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
                try { File.Move(temp, path, false); } catch (IOException) when (File.Exists(path)) { }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid shortcut icon.");
        using var check = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (check.Length != bytes.Length || !SHA256.HashData(check).SequenceEqual(SHA256.HashData(bytes))) throw new InvalidDataException("Shortcut icon changed.");
        return path;
    }
}
