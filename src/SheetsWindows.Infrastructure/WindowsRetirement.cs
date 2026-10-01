using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

// The configured root is an explicit pilot boundary, declared unsynced during one-time setup.
public sealed class WindowsRetirementReader(string allowedRoot) : IRetirementReader
{
    public IRetirementLease Open(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Retirement requires Windows handles.");
        var full = Path.GetFullPath(path); var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Source outside configured local root.");
        if (new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed) throw new NotSupportedException("Fixed local drive required.");
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var sync = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(sync) && full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sync)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Synced sources excluded.");
        }
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if (((int)File.GetAttributes(current) & (0x400 | 0x1000 | 0x40000 | 0x400000)) != 0) throw new NotSupportedException("Redirected or cloud sources excluded.");
        // DELETE + GENERIC_READ, share read only: writes, rename and replacement are denied while held.
        var handle = CreateFileW(full, 0x80010000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (info.Links != 1 || (info.Attributes & 0x400) != 0) throw new NotSupportedException("Hard links and redirects excluded.");
            var key = SourceReader.WindowsKey(handle);
            return new Lease(new FileStream(handle, FileAccess.Read), new SourceDescriptor(key, full, "xlsx"));
        }
        catch { handle.Dispose(); throw; }
    }
    private sealed class Lease(FileStream stream, SourceDescriptor source) : IRetirementLease
    {
        public SourceDescriptor Source => source;
        public Stream Content => stream;
        public void Retire()
        {
            byte delete = 1;
            if (!SetFileInformationByHandle(stream.SafeFileHandle, 4, ref delete, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Information
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref byte information, uint size);
}
