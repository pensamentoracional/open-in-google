using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed class SourceReader : ISourceReader
{
    public ISourceLease Open(string path)
    {
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(full), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Stage 2 accepts XLSX sources only.");
        // Reject redirects: policy for OneDrive/reparse points/network sources comes later.
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("Reparse-point sources are not supported in this stage.");
        if (OperatingSystem.IsWindows() && new Uri(full).IsUnc)
            throw new NotSupportedException("Network sources are not supported in this stage.");
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            var key = OperatingSystem.IsWindows() ? WindowsKey(stream.SafeFileHandle) :
                "path:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)));
            return new Lease(stream, new SourceDescriptor(key, full, "xlsx"));
        }
        catch { stream.Dispose(); throw; }
    }

    private sealed class Lease(FileStream stream, SourceDescriptor source) : ISourceLease
    {
        public SourceDescriptor Source => source;
        public Stream Content => stream;
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    private static string WindowsKey(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return $"win:{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}:{info.CreationTime.dwHighDateTime:X8}{info.CreationTime.dwLowDateTime:X8}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
}
