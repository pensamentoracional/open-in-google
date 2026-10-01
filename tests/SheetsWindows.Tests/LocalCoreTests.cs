using System.Security.AccessControl;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SheetsWindows.Core;
using SheetsWindows.Infrastructure;
using Xunit;
namespace SheetsWindows.Tests;

public sealed class Workspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "sheets-tests", Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Root, "Relatório [2026].xlsx");
    public string Database => Path.Combine(Root, "state", "registry.db");
    public string Backups => Path.Combine(Root, "state", "backups");
    public string Locks => Path.Combine(Root, "state", "locks");
    public Workspace() { Directory.CreateDirectory(Root); File.WriteAllBytes(Source, [1, 2, 3, 4]); }
    public SqliteOperationRegistry Registry() => new(Database);
    public BackupStore Store() => new(Backups);
    public LocalPreparation Coordinator(IOperationRegistry? registry = null, IBackupStore? backups = null) => new(registry ?? Registry(), new SourceReader(), new FileOperationLock(Locks), backups ?? Store());
    public void Dispose() => Directory.Delete(Root, true);
}
public sealed class LocalCoreTests
{
    [Fact]
    public async Task SnapshotAndJournalCommitTogether()
    {
        using var w = new Workspace(); var r = w.Registry(); var op = await w.Coordinator(r).PrepareAsync("A", w.Source);
        Assert.Equal(OperationState.SnapshotReady, op.State); Assert.Equal(1, op.Version);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(op.Snapshot!.BackupPath));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(w.Source));
        Assert.Equal(new[] { "Prepared", "SnapshotReady" }, r.Events(op.Id).Select(e => e.Kind));
        await w.Store().VerifyAsync(op.Id, op.Snapshot);
    }
    [Fact]
    public async Task RepeatCreatesNoDuplicate()
    {
        using var w = new Workspace(); var c = w.Coordinator(); var a = await c.PrepareAsync("A", w.Source); var b = await c.PrepareAsync("A", w.Source);
        Assert.Equal(a, b); Assert.Single(w.Registry().Pending()); Assert.Equal(2, w.Registry().Events(a.Id).Count); Assert.Single(Directory.GetFiles(w.Backups));
    }
    [Fact]
    public async Task ChangedSourcePreservesBothVersions()
    {
        using var w = new Workspace(); var c = w.Coordinator(); var op = await c.PrepareAsync("A", w.Source); File.WriteAllBytes(w.Source, [5, 6]);
        await Assert.ThrowsAsync<LocalConflictException>(() => c.PrepareAsync("A", w.Source));
        Assert.Equal(new byte[] { 5, 6 }, File.ReadAllBytes(w.Source)); Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(op.Snapshot!.BackupPath));
        Assert.Equal("source_conflict", w.Registry().Events(op.Id).Last().Code);
    }
    [Fact]
    public async Task IdenticalCopiesRemainIndependent()
    {
        using var w = new Workspace(); Directory.CreateDirectory(Path.Combine(w.Root, "other")); var other = Path.Combine(w.Root, "other", Path.GetFileName(w.Source)); File.Copy(w.Source, other);
        var a = await w.Coordinator().PrepareAsync("A", w.Source); var b = await w.Coordinator().PrepareAsync("A", other);
        Assert.NotEqual(a.Id, b.Id); Assert.Equal(a.Snapshot!.Sha256, b.Snapshot!.Sha256); Assert.Equal(2, w.Registry().Pending().Count);
    }
    [Fact]
    public async Task AccountsAreIsolated()
    {
        using var w = new Workspace(); var c = w.Coordinator(); var a = await c.PrepareAsync("A", w.Source); var b = await c.PrepareAsync("B", w.Source);
        Assert.NotEqual(a.Id, b.Id); Assert.NotEqual(a.Snapshot!.BackupPath, b.Snapshot!.BackupPath);
    }
    [Fact]
    public async Task SixteenConcurrentPreparationsHaveOneSnapshot()
    {
        using var w = new Workspace(); _ = w.Registry();
        var ops = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => w.Coordinator().PrepareAsync("A", w.Source))));
        Assert.Single(ops.Select(o => o.Id).Distinct()); Assert.Single(w.Registry().Pending()); Assert.Single(Directory.GetFiles(w.Backups)); Assert.Equal(2, w.Registry().Events(ops[0].Id).Count);
    }
    [Fact]
    public async Task ReopenRecoversDurableState()
    {
        using var w = new Workspace(); var op = await w.Coordinator().PrepareAsync("A", w.Source);
        Assert.Equal(op, w.Registry().Get(op.Id)); Assert.Equal(op, Assert.Single(await w.Coordinator().VerifyRecoveryAsync()));
    }
    [Fact]
    public async Task CorruptBackupStopsRecovery()
    {
        using var w = new Workspace(); var op = await w.Coordinator().PrepareAsync("A", w.Source); File.WriteAllBytes(op.Snapshot!.BackupPath, [9, 9, 9, 9]);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.Coordinator().VerifyRecoveryAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => w.Coordinator().PrepareAsync("A", w.Source));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(w.Source));
    }
    [Fact]
    public async Task MissingBackupStopsRecovery()
    {
        using var w = new Workspace(); var op = await w.Coordinator().PrepareAsync("A", w.Source); File.Delete(op.Snapshot!.BackupPath);
        await Assert.ThrowsAsync<FileNotFoundException>(() => w.Coordinator().VerifyRecoveryAsync()); Assert.True(File.Exists(w.Source));
    }
    [Fact]
    public async Task BackupWriteFailureLeavesPreparedOriginal()
    {
        using var w = new Workspace(); await Assert.ThrowsAsync<IOException>(() => w.Coordinator(backups: new FailingBackup()).PrepareAsync("A", w.Source));
        var op = Assert.Single(w.Registry().Pending()); Assert.Equal(OperationState.Prepared, op.State); Assert.Null(op.Snapshot);
        Assert.Equal(new[] { "Prepared", "Failure" }, w.Registry().Events(op.Id).Select(e => e.Kind)); Assert.True(File.Exists(w.Source));
    }
    [Fact]
    public async Task FailureAfterBackupReusesOrphanOnResume()
    {
        using var w = new Workspace(); var r = w.Registry(); await Assert.ThrowsAsync<IOException>(() => w.Coordinator(new FailingCommit(r)).PrepareAsync("A", w.Source));
        var a = Assert.Single(r.Pending()); Assert.Equal(OperationState.Prepared, a.State); Assert.Single(Directory.GetFiles(w.Backups));
        var b = await w.Coordinator().PrepareAsync("A", w.Source); Assert.Equal(a.Id, b.Id); Assert.Equal(OperationState.SnapshotReady, b.State); Assert.Single(Directory.GetFiles(w.Backups));
    }
    [Fact]
    public async Task ChangedSourceNeverOverwritesOrphan()
    {
        using var w = new Workspace(); await Assert.ThrowsAsync<IOException>(() => w.Coordinator(new FailingCommit(w.Registry())).PrepareAsync("A", w.Source));
        var backup = Assert.Single(Directory.GetFiles(w.Backups)); File.WriteAllBytes(w.Source, [8]);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.Coordinator().PrepareAsync("A", w.Source));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(backup)); Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(w.Source));
    }
    [Fact]
    public async Task RestoreNeverOverwritesExistingDestination()
    {
        using var w = new Workspace(); var op = await w.Coordinator().PrepareAsync("A", w.Source); var dest = Path.Combine(w.Root, "restored.xlsx");
        await w.Store().RestoreAsync(op.Id, op.Snapshot!, dest); Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(dest)); File.WriteAllBytes(dest, [9]);
        await Assert.ThrowsAsync<IOException>(() => w.Store().RestoreAsync(op.Id, op.Snapshot!, dest)); Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(dest)); Assert.Empty(Directory.GetFiles(w.Root, "*.restore.tmp"));
    }
    [Fact]
    public async Task BackupPathCannotEscapeOperationSlot()
    {
        using var w = new Workspace(); var op = await w.Coordinator().PrepareAsync("A", w.Source);
        await Assert.ThrowsAsync<InvalidDataException>(() => w.Store().VerifyAsync(op.Id, op.Snapshot! with { BackupPath = w.Source }));
    }
    [Fact]
    public async Task InterruptedReadCleansPartialBackup()
    {
        using var w = new Workspace(); await Assert.ThrowsAsync<IOException>(() => w.Store().CaptureAsync(Guid.NewGuid(), new BrokenReadStream()));
        Assert.Empty(Directory.GetFiles(w.Backups)); Assert.True(File.Exists(w.Source));
    }
    [Fact]
    public async Task CancellationPublishesNothing()
    {
        using var w = new Workspace(); using var c = new CancellationTokenSource(); c.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => w.Store().CaptureAsync(Guid.NewGuid(), new MemoryStream([1]), c.Token)); Assert.Empty(Directory.GetFiles(w.Backups));
    }
    [Fact]
    public async Task ChangingInputIsRejected()
    {
        using var w = new Workspace(); await Assert.ThrowsAsync<LocalConflictException>(() => w.Store().CaptureAsync(Guid.NewGuid(), new ChangingStream())); Assert.Empty(Directory.GetFiles(w.Backups));
    }
    [Fact]
    public void StaleTransitionDoesNotAppendEvent()
    {
        using var w = new Workspace(); var r = w.Registry(); var op = r.GetOrCreate("A", new("source", w.Source, "xlsx"));
        Assert.Throws<InvalidOperationException>(() => r.CommitSnapshot(op.Id, 10, new("path", new string('A', 64), 4)));
        Assert.Equal(OperationState.Prepared, r.Get(op.Id)!.State); Assert.Single(r.Events(op.Id));
    }
    [Fact]
    public void EventFailureRollsBackState()
    {
        using var w = new Workspace(); var r = w.Registry(); var op = r.GetOrCreate("A", new("source", w.Source, "xlsx"));
        using var db = new SqliteConnection($"Data Source={w.Database};Pooling=False"); db.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TRIGGER reject_snapshot BEFORE INSERT ON journal WHEN NEW.kind='SnapshotReady' BEGIN SELECT RAISE(ABORT,'injected'); END;"; cmd.ExecuteNonQuery();
        Assert.Throws<SqliteException>(() => r.CommitSnapshot(op.Id, 0, new("path", new string('A', 64), 4)));
        Assert.Equal(OperationState.Prepared, r.Get(op.Id)!.State); Assert.Single(r.Events(op.Id));
    }
    [Fact]
    public void FutureSchemaIsRejected()
    {
        using var w = new Workspace(); _ = w.Registry(); using (var db = new SqliteConnection($"Data Source={w.Database};Pooling=False")) { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99"; cmd.ExecuteNonQuery(); }
        Assert.Throws<NotSupportedException>(() => w.Registry());
    }
    [Fact]
    public void CorruptDatabaseIsNotRecreated()
    {
        using var w = new Workspace(); Directory.CreateDirectory(Path.GetDirectoryName(w.Database)!); File.WriteAllText(w.Database, "not a database");
        Assert.Throws<SqliteException>(() => w.Registry()); Assert.Equal("not a database", File.ReadAllText(w.Database));
    }
    [Fact]
    public async Task LockCancellationDoesNotStealOwnership()
    {
        using var w = new Workspace(); var locks = new FileOperationLock(w.Locks); await using var a = await locks.AcquireAsync("key"); using var c = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await using var b = await locks.AcquireAsync("key", c.Token); });
    }
    [Fact]
    public async Task LockTimeoutIsExplicit()
    {
        using var w = new Workspace(); var locks = new FileOperationLock(w.Locks, TimeSpan.FromMilliseconds(75)); await using var a = await locks.AcquireAsync("key");
        await Assert.ThrowsAsync<TimeoutException>(async () => { await using var b = await locks.AcquireAsync("key"); });
    }
    [Fact]
    public async Task CrossProcessLockIsReleasedByCrash()
    {
        using var w = new Workspace(); var marker = Path.Combine(w.Root, "probe-ready"); var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(typeof(SheetsWindows.LockProbe.Marker).Assembly.Location); start.ArgumentList.Add(w.Locks); start.ArgumentList.Add("cross-process"); start.ArgumentList.Add(marker);
        using var process = Process.Start(start)!;
        try
        {
            var timer = Stopwatch.StartNew(); while (!File.Exists(marker) && !process.HasExited && timer.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(25);
            Assert.True(File.Exists(marker), process.HasExited ? await process.StandardError.ReadToEndAsync() : "probe timeout");
            var locks = new FileOperationLock(w.Locks, TimeSpan.FromMilliseconds(75)); await Assert.ThrowsAsync<TimeoutException>(async () => { await using var held = await locks.AcquireAsync("cross-process"); });
            process.Kill(); await process.WaitForExitAsync(); await using var recovered = await locks.AcquireAsync("cross-process");
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
    }
    [WindowsFact]
    public async Task WindowsHandleDeniesWrites()
    {
        using var w = new Workspace(); await using (var source = new SourceReader().Open(w.Source)) { Assert.Throws<IOException>(() => File.WriteAllBytes(w.Source, [9])); }
        File.WriteAllBytes(w.Source, [9]); Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(w.Source));
    }
    [WindowsFact]
    public async Task WindowsRenamePreservesIdentityButCopyDoesNot()
    {
        using var w = new Workspace(); var c = w.Coordinator(); var a = await c.PrepareAsync("A", w.Source); var moved = Path.Combine(w.Root, "renamed.xlsx"); File.Move(w.Source, moved);
        var b = await c.PrepareAsync("A", moved); Assert.Equal(a.Id, b.Id); File.Copy(moved, w.Source); var copy = await c.PrepareAsync("A", w.Source); Assert.NotEqual(a.Id, copy.Id);
    }
    [UnixFact]
    public async Task PrivateStorageIsRestrictedOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;
        using var w = new Workspace();
        var layout = new LocalStorage(Path.Combine(w.Root, "private"));
        await layout.CreatePreparation().PrepareAsync("A", w.Source);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(layout.Root));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Assert.Single(Directory.GetFiles(layout.BackupsPath))));
    }

    [WindowsFact]
    public async Task PrivateStorageHasProtectedUserAcl()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var w = new Workspace();
        var layout = new LocalStorage(Path.Combine(w.Root, "private"));
        await layout.CreatePreparation().PrepareAsync("A", w.Source);
        var acl = new DirectoryInfo(layout.Root).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        Assert.Equal(identity.User, acl.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)));
    }

    private sealed class FailingBackup : IBackupStore
    {
        public Task<Snapshot> CaptureAsync(Guid id, Stream source, CancellationToken cancellationToken = default) => throw new IOException("injected write failure");
        public Task VerifyAsync(Guid id, Snapshot s, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RestoreAsync(Guid id, Snapshot s, string destination, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    private sealed class FailingCommit(IOperationRegistry inner) : IOperationRegistry
    {
        public ImportOperation GetOrCreate(string a, SourceDescriptor s) => inner.GetOrCreate(a, s);
        public ImportOperation? Get(Guid id) => inner.Get(id);
        public ImportOperation CommitSnapshot(Guid id, long v, Snapshot s) => throw new IOException("injected commit failure");
        public void RecordFailure(Guid id, string c) => inner.RecordFailure(id, c);
        public IReadOnlyList<ImportOperation> Pending() => inner.Pending(); public IReadOnlyList<JournalEvent> Events(Guid id) => inner.Events(id);
    }
    private sealed class BrokenReadStream : MemoryStream
    {
        public BrokenReadStream() : base([1, 2, 3]) { }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("injected read failure");
    }
    private sealed class ChangingStream : MemoryStream
    {
        private int resets; public ChangingStream() : base([1, 2, 3]) { }
        public override long Position { get => base.Position; set { if (value == 0 && ++resets == 2) { base.Position = 0; WriteByte(9); } base.Position = value; } }
    }
}
