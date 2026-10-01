using System.Security.Cryptography;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed class BackupStore : IBackupStore
{
    private readonly string directory;
    public BackupStore(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        PrivateDirectory.Create(this.directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(this.directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private string Target(Guid id) => Path.Combine(directory, id.ToString("N") + ".snapshot");

    public async Task<Snapshot> CaptureAsync(Guid operationId, Stream source, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Operation ID required.");
        if (!source.CanSeek) throw new ArgumentException("Source must be seekable.");
        var target = Target(operationId);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            source.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var buffer = new byte[81920];
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    length += count;
                }
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            var snapshot = new Snapshot(target, Convert.ToHexString(hash.GetHashAndReset()), length);
            // Re-read the held source to detect a changing input, including on non-Windows test hosts.
            source.Position = 0;
            var again = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
            if (source.Length != length || !string.Equals(again, snapshot.Sha256, StringComparison.Ordinal))
                throw new LocalConflictException("Source changed during snapshot.");
            if (File.Exists(target))
                await VerifyAsync(operationId, snapshot, cancellationToken); // orphan from crash; reuse only exact bytes
            else File.Move(temporary, target, overwrite: false);
            await VerifyAsync(operationId, snapshot, cancellationToken);
            return snapshot;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task VerifyAsync(Guid operationId, Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetFullPath(snapshot.BackupPath), Target(operationId),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Backup is outside its operation slot.");
        if ((File.GetAttributes(snapshot.BackupPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Backup redirects are forbidden.");
        await using var input = new FileStream(snapshot.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        if (input.Length != snapshot.Length || !string.Equals(hash, snapshot.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Backup integrity check failed; source must be retained.");
    }

    public async Task RestoreAsync(Guid operationId, Snapshot snapshot, string destination, CancellationToken cancellationToken = default)
    {
        await VerifyAsync(operationId, snapshot, cancellationToken);
        var full = Path.GetFullPath(destination);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".restore.tmp";
        try
        {
            // Verification and copy use separate handles: verify copied bytes before publishing as well.
            await using (var source = new FileStream(snapshot.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            await using (var read = File.OpenRead(temporary))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(read, cancellationToken));
                if (read.Length != snapshot.Length || hash != snapshot.Sha256)
                    throw new InvalidDataException("Restoration integrity check failed.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, full, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
