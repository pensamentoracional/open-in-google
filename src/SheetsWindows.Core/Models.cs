namespace SheetsWindows.Core;

public enum OperationState { Prepared, SnapshotReady }
public sealed record SourceDescriptor(string IdentityKey, string Path, string Format);
public sealed record Snapshot(string BackupPath, string Sha256, long Length);
public sealed record ImportOperation(Guid Id, string AccountId, string SourceKey, string SourcePath,
    string Format, OperationState State, Snapshot? Snapshot, long Version);
public sealed record JournalEvent(long Sequence, Guid OperationId, string Kind, string? Code);
public sealed class LocalConflictException(string message) : IOException(message);

public interface IOperationRegistry
{
    ImportOperation GetOrCreate(string accountId, SourceDescriptor source);
    ImportOperation? Get(Guid id);
    ImportOperation CommitSnapshot(Guid id, long expectedVersion, Snapshot snapshot);
    void RecordFailure(Guid id, string code);
    IReadOnlyList<ImportOperation> Pending();
    IReadOnlyList<JournalEvent> Events(Guid id);
}
public interface ISourceLease : IAsyncDisposable
{
    SourceDescriptor Source { get; }
    Stream Content { get; }
}
public interface ISourceReader { ISourceLease Open(string path); }
public interface IOperationLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}
public interface IBackupStore
{
    Task<Snapshot> CaptureAsync(Guid operationId, Stream source, CancellationToken cancellationToken = default);
    Task VerifyAsync(Guid operationId, Snapshot snapshot, CancellationToken cancellationToken = default);
    Task RestoreAsync(Guid operationId, Snapshot snapshot, string destination, CancellationToken cancellationToken = default);
}
