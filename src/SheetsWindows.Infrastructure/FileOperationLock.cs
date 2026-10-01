using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

/// <summary>Cross-process exclusive handle. Lock files remain; ownership ends when handle closes/crashes.</summary>
public sealed class FileOperationLock : IOperationLock
{
    private readonly string directory;
    private readonly TimeSpan timeout;
    public FileOperationLock(string directory, TimeSpan? timeout = null)
    {
        this.directory = Path.GetFullPath(directory);
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        PrivateDirectory.Create(this.directory);
    }
    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".lock";
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new Handle(new FileStream(Path.Combine(directory, name), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                if (timer.Elapsed >= timeout) throw new TimeoutException("Source is busy.", ex);
                await Task.Delay(25, cancellationToken);
            }
        }
    }
    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xffff) is 11 or 32 or 33;
    private sealed class Handle(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
