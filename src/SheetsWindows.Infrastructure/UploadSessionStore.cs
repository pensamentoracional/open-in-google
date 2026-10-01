using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public interface IUploadSecretProtector
{
    byte[] Protect(byte[] bytes);
    byte[] Unprotect(byte[] bytes);
}
public sealed class DpapiUploadProtector : IUploadSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SheetsWindows.Upload.v1");
    public byte[] Protect(byte[] bytes)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI required.");
        return ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
    }
    public byte[] Unprotect(byte[] bytes)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI required.");
        return ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
    }
}
public sealed record UploadSession(string Key, string Account, string SourceHash, string PayloadHash, int Length, string MimeType, string Location)
{
    public override string ToString() => "UploadSession [protected]";
}
// Caller holds the source operation lock. Session files are immutable and never replaced.
public sealed class UploadSessionStore(string root, IUploadSecretProtector? protector = null)
{
    private readonly IUploadSecretProtector secrets = protector ?? new DpapiUploadProtector();
    private string PathFor(string key) => Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".session");
    public UploadSession? Get(RemoteAttempt attempt, byte[] bytes, string mime)
    {
        PrivateDirectory.Create(root); var path = PathFor(attempt.Key);
        if (!File.Exists(path)) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 65536 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid upload session.");
        using var buffer = new MemoryStream(); file.CopyTo(buffer);
        var session = JsonSerializer.Deserialize<UploadSession>(secrets.Unprotect(buffer.ToArray())) ?? throw new InvalidDataException("Invalid upload session.");
        if (session.Key != attempt.Key || session.Account != attempt.AccountId || session.SourceHash != attempt.Hash || session.Length != bytes.Length
            || session.PayloadHash != Convert.ToHexString(SHA256.HashData(bytes)) || session.MimeType != mime) throw new LocalConflictException("Upload binding changed.");
        ValidateLocation(session.Location); return session;
    }
    public void Save(RemoteAttempt attempt, byte[] bytes, string mime, Uri location)
    {
        ValidateLocation(location.AbsoluteUri); PrivateDirectory.Create(root);
        var session = new UploadSession(attempt.Key, attempt.AccountId, attempt.Hash, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, mime, location.AbsoluteUri);
        var encrypted = secrets.Protect(JsonSerializer.SerializeToUtf8Bytes(session)); var path = PathFor(attempt.Key); var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(encrypted); file.Flush(true); }
            File.Move(tmp, path, false);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static Uri ValidateLocation(string location)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "www.googleapis.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/upload/drive/v3/files"
            || !uri.Query.Contains("upload_id=", StringComparison.Ordinal) || location.Length > 8192) throw new InvalidDataException("Untrusted upload session.");
        return uri;
    }
}
