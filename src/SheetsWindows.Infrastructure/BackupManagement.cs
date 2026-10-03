using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public sealed record ManagedBackupEntry(ImportOperation Operation, DateTimeOffset? CapturedAt, bool Available, bool CanClean, int CleanupState)
{
    public Guid Id => Operation.Id;
}
public sealed record BackupSummary(long UsedBytes, long UnmanagedBytes, IReadOnlyList<ManagedBackupEntry> Entries);
public sealed record CleanupResult(int Count, long Bytes);
public sealed class BackupManagement(LocalStorage storage, Func<DateTimeOffset>? clock = null)
{
    public const string LockKey = "backup-maintenance";
    private DateTimeOffset Now => (clock?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
    public string Slot(Guid id) => Path.Combine(storage.BackupsPath, id.ToString("N") + ".snapshot");
    private BackupCatalog? ExistingCatalog() => File.Exists(BackupCatalog.PathFor(storage)) ? new(storage) : null;
    public bool IsRemoved(Guid id) => ExistingCatalog()?.Get(id)?.State is >= 1;
    public void RequireAvailable(Guid id) { if (IsRemoved(id)) throw new BackupRemovedException(); }
    public long UsedBytes()
    {
        if (!Directory.Exists(storage.BackupsPath)) return 0;
        long total=0;var pending=new Stack<string>();pending.Push(storage.BackupsPath);
        while(pending.TryPop(out var folder))
        {
            if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Redirected backup storage.");
            foreach(var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Redirected backup storage.");
                if(item is DirectoryInfo directory)pending.Push(directory.FullName);else total=checked(total+((FileInfo)item).Length);
            }
        }
        return total;
    }
    private ManagedBackupEntry Evaluate(ImportOperation op, DateTimeOffset? date, BackupRecord? record, ReplacementRecord? replacement, RemoteAttempt? mapping)
    {
        var snapshot=op.Snapshot??throw new InvalidDataException("No committed backup.");
        if(!Path.GetFullPath(snapshot.BackupPath).Equals(Path.GetFullPath(Slot(op.Id)),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidDataException("Invalid backup slot.");
        if(record is not null && (record.Hash!=snapshot.Sha256 || record.Bytes!=snapshot.Length))throw new InvalidDataException("Backup catalog conflict.");
        var state=record?.State??0;var present=File.Exists(snapshot.BackupPath);
        if(present && (File.GetAttributes(snapshot.BackupPath)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Redirected backup.");
        var verified=mapping is {Verified:true,Kind:"sheet",FileId:not null} && mapping.AccountId==op.AccountId && mapping.Hash==snapshot.Sha256;
        var complete=verified && (replacement is {Step:4} && replacement.Url==GoogleDriveClient.Editor(mapping!.FileId!).AbsoluteUri || record?.CopyCompleted==true && replacement is null);
        return new(op,record?.CapturedAt??date,present&&state==0,complete&&state!=2 && (present||state==1),state);
    }
    public BackupSummary Inspect()
    {
        var used=UsedBytes(); if(!File.Exists(storage.DatabasePath))return new(used,used,[]);
        var registry=new SqliteOperationRegistry(storage.DatabasePath);var dates=registry.SnapshotDates();
        var replacements=File.Exists(Path.Combine(storage.Root,"replacement.db"))?new ReplacementJournal(Path.Combine(storage.Root,"replacement.db")).All():[];
        var mappings=File.Exists(Path.Combine(storage.Root,"google.db"))?new GoogleRemoteRegistry(Path.Combine(storage.Root,"google.db")).All():[];
        var catalog=ExistingCatalog()?.All()??[];
        var entries=registry.Pending().Where(op=>op.Snapshot is not null).Select(op=>Evaluate(op,dates.GetValueOrDefault(op.Id),catalog.GetValueOrDefault(op.Id),replacements.GetValueOrDefault(op.Id),mappings.GetValueOrDefault("sheet:"+op.Id.ToString("N")))).ToArray();
        var accounted=entries.Where(e=>File.Exists(e.Operation.Snapshot!.BackupPath)).Sum(e=>new FileInfo(e.Operation.Snapshot!.BackupPath).Length);
        return new(used,Math.Max(0,used-accounted),entries);
    }
    private ManagedBackupEntry Current(Guid id)
    {
        var registry=new SqliteOperationRegistry(storage.DatabasePath);var op=registry.Get(id)??throw new KeyNotFoundException();
        var replacement=File.Exists(Path.Combine(storage.Root,"replacement.db"))?new ReplacementJournal(Path.Combine(storage.Root,"replacement.db")).Get(id):null;
        var mapping=File.Exists(Path.Combine(storage.Root,"google.db"))?new GoogleRemoteRegistry(Path.Combine(storage.Root,"google.db")).Get("sheet:"+id.ToString("N")):null;
        return Evaluate(op,registry.SnapshotCreatedAt(id),ExistingCatalog()?.Get(id),replacement,mapping);
    }
    public async Task RegisterCopyCompletionAsync(ImportReceipt receipt,CancellationToken ct=default)
    {
        // Called while PublishCopy holds the source lock, after verified mapping/shortcut and browser acceptance.
        await using var held=await new FileOperationLock(storage.LocksPath).AcquireAsync(LockKey,ct);
        var registry=new SqliteOperationRegistry(storage.DatabasePath);var op=registry.Get(receipt.Operation.Id)??throw new KeyNotFoundException();
        if(op!=receipt.Operation || op.Snapshot is null)throw new LocalConflictException("Copy binding changed.");
        var mapping=new GoogleRemoteRegistry(Path.Combine(storage.Root,"google.db")).Get("sheet:"+op.Id.ToString("N"));
        if(mapping is not {Verified:true,FileId:not null,Kind:"sheet"} || mapping.Hash!=op.Snapshot.Sha256 || mapping.AccountId!=op.AccountId || GoogleDriveClient.Editor(mapping.FileId)!=receipt.Url)throw new LocalConflictException("Copy completion unverified.");
        var catalog=new BackupCatalog(storage);catalog.Register(op.Id,op.Snapshot,registry.SnapshotCreatedAt(op.Id)??Now);catalog.CopyComplete(op.Id,Now);
    }
    public async Task<CleanupResult> DeleteAsync(Guid id,CancellationToken ct=default)
    {
        var registry=new SqliteOperationRegistry(storage.DatabasePath);var original=registry.Get(id)??throw new KeyNotFoundException();
        // Same order as capture/restore: source first, maintenance second. Never hold maintenance while waiting for another source.
        await using var source=await new FileOperationLock(storage.LocksPath).AcquireAsync(original.SourceKey,ct);
        await using var maintenance=await new FileOperationLock(storage.LocksPath).AcquireAsync(LockKey,ct);
        var entry=Current(id);if(entry.CleanupState==2)return new(0,0);
        if(!entry.CanClean || entry.CapturedAt is null)throw new InvalidOperationException("Pending or unverified backup is protected.");
        var snapshot=entry.Operation.Snapshot!;var catalog=new BackupCatalog(storage);catalog.Register(id,snapshot,entry.CapturedAt.Value);
        if(entry.CleanupState==0)catalog.BeginDelete(id,Now);
        ct.ThrowIfCancellationRequested();
        if(Directory.Exists(snapshot.BackupPath))throw new InvalidDataException("Backup slot is a directory.");
        var present=File.Exists(snapshot.BackupPath);
        if(present)await SnapshotRemoval.DeleteVerifiedAsync(snapshot,ct);
        catalog.FinishDelete(id,Now);return new(1,present?snapshot.Length:0);
    }
    public IReadOnlyList<Guid> Plan(long incomingBytes=0,bool includeExpired=true,Guid? excludeId=null)
    {
        if(incomingBytes<0)throw new ArgumentException("Invalid incoming size.");
        var policy=BackupPolicy.Load(storage);var summary=Inspect();var remaining=summary.UsedBytes;var result=new List<Guid>();
        foreach(var entry in summary.Entries.Where(e=>e.Id!=excludeId && e.CanClean && e.CapturedAt is not null).OrderBy(e=>e.CapturedAt).ThenBy(e=>e.Id))
        {
            if(entry.CleanupState==1 || includeExpired && entry.CapturedAt<=Now.AddDays(-policy.RetentionDays) || remaining+incomingBytes>policy.QuotaBytes)
            { result.Add(entry.Id);remaining-=File.Exists(entry.Operation.Snapshot!.BackupPath)?new FileInfo(entry.Operation.Snapshot.BackupPath).Length:0; }
        }
        return result;
    }
    public async Task<CleanupResult> MaintainAsync(CancellationToken ct=default,Guid? excludeId=null)
    {
        var policy=BackupPolicy.Load(storage);var ids=policy.AutomaticCleanup?Plan(excludeId:excludeId):Inspect().Entries.Where(e=>e.Id!=excludeId&&e.CleanupState==1&&e.CanClean).Select(e=>e.Id).ToArray();
        return await CleanAsync(ids,ct);
    }
    public async Task<CleanupResult> CleanAsync(IEnumerable<Guid> ids,CancellationToken ct=default)
    {
        var count=0;long bytes=0;foreach(var id in ids.Distinct()){ct.ThrowIfCancellationRequested();var result=await DeleteAsync(id,ct);count+=result.Count;bytes+=result.Bytes;}return new(count,bytes);
    }
    public async Task MakeRoomAsync(long incomingBytes,CancellationToken ct=default,Guid? excludeId=null)
    {
        var policy=BackupPolicy.Load(storage);
        if (incomingBytes > policy.QuotaBytes) throw new BackupQuotaException();
        if(policy.AutomaticCleanup)await CleanAsync(Plan(incomingBytes,excludeId:excludeId),ct);
    }
}
public sealed class ManagedBackupStore(LocalStorage storage):IBackupStore
{
    public async Task<Snapshot> CaptureAsync(Guid operationId,Stream source,CancellationToken ct=default)
    {
        var manager=new BackupManagement(storage);
        await using var held=await new FileOperationLock(storage.LocksPath).AcquireAsync(BackupManagement.LockKey,ct);
        var catalog=new BackupCatalog(storage);if(catalog.Get(operationId)?.State is >=1)throw new BackupRemovedException();
        var existing=File.Exists(manager.Slot(operationId))?new FileInfo(manager.Slot(operationId)).Length:0;
        if(manager.UsedBytes()-existing+source.Length>BackupPolicy.Load(storage).QuotaBytes)throw new BackupQuotaException();
        var snapshot=await new BackupStore(storage.BackupsPath).CaptureAsync(operationId,source,ct);
        catalog.Register(operationId,snapshot,DateTimeOffset.UtcNow);return snapshot;
    }
    public Task VerifyAsync(Guid id,Snapshot snapshot,CancellationToken ct=default) {new BackupManagement(storage).RequireAvailable(id);return new BackupStore(storage.BackupsPath).VerifyAsync(id,snapshot,ct);}
    public Task RestoreAsync(Guid id,Snapshot snapshot,string destination,CancellationToken ct=default) {new BackupManagement(storage).RequireAvailable(id);return new BackupStore(storage.BackupsPath).RestoreAsync(id,snapshot,destination,ct);}
}
