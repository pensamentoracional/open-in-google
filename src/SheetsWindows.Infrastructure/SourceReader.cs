using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed class SourceReader(bool copyEnvironments = false) : ISourceReader
{
    public ISourceLease Open(string path)
    {
        var full = Path.GetFullPath(path);
        var format = SpreadsheetFormats.Format(full);
        SourceEnvironment.ValidateRead(full, copyEnvironments);
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            var key = OperatingSystem.IsWindows() ? WindowsKey(stream.SafeFileHandle) :
                "path:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)));
            if (SourceEnvironment.IsNetwork(full) && key.Split(':')[2] == "0000000000000000") throw new NotSupportedException("Network server does not expose stable file identity.");
            return new Lease(stream, new SourceDescriptor(SourceEnvironment.ScopeIdentity(full, key), full, format));
        }
        catch { stream.Dispose(); throw; }
    }

    private sealed class Lease(FileStream stream, SourceDescriptor source) : ISourceLease
    {
        public SourceDescriptor Source => source;
        public Stream Content => stream;
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    internal static string WindowsKey(SafeFileHandle handle)
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
