using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

internal static class SnapshotRemoval
{
    public static async Task DeleteVerifiedAsync(Snapshot snapshot, CancellationToken ct)
    {
        if ((File.GetAttributes(snapshot.BackupPath) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Redirected backup.");
        if (!OperatingSystem.IsWindows())
        {
            await using (var input = new FileStream(snapshot.BackupPath, FileMode.Open, FileAccess.Read, FileShare.None)) await Verify(input, snapshot, ct);
            ct.ThrowIfCancellationRequested(); File.Delete(snapshot.BackupPath); return; // Non-Windows test host; production uses the held Windows handle.
        }
        var handle=CreateFileW(snapshot.BackupPath,0x80010000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
        if(handle.IsInvalid){handle.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error());}
        await using var stream=new FileStream(handle,FileAccess.Read);
        if(!GetFileInformationByHandle(handle,out var info))throw new Win32Exception(Marshal.GetLastWin32Error());
        if(info.Links!=1 || (info.Attributes & 0x400)!=0)throw new NotSupportedException("Linked backup cannot be cleaned.");
        await Verify(stream,snapshot,ct);ct.ThrowIfCancellationRequested();byte delete=1;
        if(!SetFileInformationByHandle(handle,4,ref delete,1))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private static async Task Verify(Stream input,Snapshot snapshot,CancellationToken ct)
    { if(input.Length!=snapshot.Length || Convert.ToHexString(await SHA256.HashDataAsync(input,ct))!=snapshot.Sha256)throw new InvalidDataException("Backup integrity failure; cleanup blocked."); }
    [StructLayout(LayoutKind.Sequential)]private struct Information {public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out Information info);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int kind,ref byte info,uint size);
}
