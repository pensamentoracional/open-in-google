using System.Security.AccessControl;
using System.Security.Principal;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed record LocalStorage(string Root)
{
    public string DatabasePath => Path.Combine(Root, "registry.db");
    public string BackupsPath => Path.Combine(Root, "backups");
    public string LocksPath => Path.Combine(Root, "locks");

    public static LocalStorage ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("No local application data directory.");
        return new LocalStorage(Path.Combine(local, "SheetsWindows"));
    }

    public LocalPreparation CreatePreparation(ISourceReader? sourceReader = null)
    {
        PrivateDirectory.Create(Root);
        return new LocalPreparation(new SqliteOperationRegistry(DatabasePath), sourceReader ?? new SourceReader(),
            new FileOperationLock(LocksPath), new BackupStore(BackupsPath));
    }
}

internal static class PrivateDirectory
{
    public static void Create(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(full);
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("Private storage cannot use redirected directories.");
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User ?? throw new InvalidOperationException("No Windows user identity.");
            var acl = new DirectorySecurity();
            acl.SetOwner(user);
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inheritance,
                PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(full).SetAccessControl(acl);
        }
        else File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
