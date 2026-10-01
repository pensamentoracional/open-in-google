using System.Security.Cryptography;

namespace SheetsWindows.Core;

/// <summary>Only prepares recoverable local bytes. Does not upload, update or delete a source.</summary>
public sealed class LocalPreparation(IOperationRegistry registry, ISourceReader reader,
    IOperationLock operationLock, IBackupStore backups)
{
    public async Task<ImportOperation> PrepareAsync(string accountId, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        // Keep the source handle open throughout capture. On Windows it denies writers and renames.
        await using var source = reader.Open(path);
        await using var held = await operationLock.AcquireAsync(source.Source.IdentityKey, cancellationToken);
        var operation = registry.GetOrCreate(accountId, source.Source);
        try
        {
            if (operation.Snapshot is { } existing)
            {
                await backups.VerifyAsync(operation.Id, existing, cancellationToken);
                source.Content.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(source.Content, cancellationToken));
                if (!string.Equals(hash, existing.Sha256, StringComparison.Ordinal))
                    throw new LocalConflictException("Source changed after snapshot; original is preserved.");
                return operation;
            }
            var snapshot = await backups.CaptureAsync(operation.Id, source.Content, cancellationToken);
            return registry.CommitSnapshot(operation.Id, operation.Version, snapshot);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException)
        {
            // Never persist raw exception messages, which can contain private paths/content.
            registry.RecordFailure(operation.Id, ex is LocalConflictException ? "source_conflict" :
                ex is OperationCanceledException ? "cancelled" : "local_preparation_failed");
            throw;
        }
    }

    public async Task<IReadOnlyList<ImportOperation>> VerifyRecoveryAsync(CancellationToken cancellationToken = default)
    {
        var pending = registry.Pending();
        var verified = new List<ImportOperation>();
        foreach (var pendingOperation in pending)
        {
            await using var held = await operationLock.AcquireAsync(pendingOperation.SourceKey, cancellationToken);
            var op = registry.Get(pendingOperation.Id) ?? throw new InvalidOperationException("Operation disappeared during recovery.");
            if (op.Snapshot is { } snapshot)
                await backups.VerifyAsync(op.Id, snapshot, cancellationToken);
            verified.Add(op);
        }
        // Prepared operations must be resumed against a locked source, never inferred safe from a filename.
        return verified;
    }
}
