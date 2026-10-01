using System.Security.Cryptography;
using SheetsWindows.Core;
using SheetsWindows.Infrastructure;
using Xunit;

namespace SheetsWindows.Tests;

public sealed class ReplacementTests
{
    [Theory]
    [InlineData("http://docs.google.com/spreadsheets/d/id/edit")]
    [InlineData("https://evil.test/spreadsheets/d/id/edit")]
    [InlineData("https://docs.google.com/spreadsheets/d/id/edit?x=1")]
    [InlineData("https://docs.google.com/spreadsheets/d/id/edit#x")]
    [InlineData("https://user@docs.google.com/spreadsheets/d/id/edit")]
    public void UnsafeShortcutRejected(string url) => Assert.Throws<ArgumentException>(() => InternetShortcut.Bytes(new Uri(url)));

    [Fact]
    public async Task HappyPathAndRepeatedCompletionPreserveRecreatedFile()
    {
        using var f = new Fixture(); await f.Replace();
        Assert.False(File.Exists(f.Source)); Assert.Equal(4, f.Journal.Get(f.Op.Id)!.Step); Assert.Equal(1, f.Browser.Count);
        Assert.True(File.Exists(f.Op.Snapshot!.BackupPath));
        File.WriteAllText(f.Source, "new original"); await f.Replace(); Assert.Equal("new original", File.ReadAllText(f.Source));
    }
    [Fact]
    public async Task BrowserFailurePreservesSourceAndCanResume()
    {
        using var f = new Fixture(); f.Browser.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => f.Replace()); Assert.True(File.Exists(f.Source)); Assert.Equal(1, f.Journal.Get(f.Op.Id)!.Step);
        f.Browser.Fail = false; await f.Replace(); Assert.False(File.Exists(f.Source));
    }
    [Fact]
    public async Task ChangedSourceIsNeverRetired()
    {
        using var f = new Fixture(); File.WriteAllText(f.Source, "changed");
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Replace()); Assert.Equal(0, f.Browser.Count); Assert.Equal("changed", File.ReadAllText(f.Source));
    }
    [Fact]
    public async Task CorruptBackupBlocksPublishing()
    {
        using var f = new Fixture(); File.WriteAllText(f.Op.Snapshot!.BackupPath, "bad");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Replace()); Assert.Null(f.Journal.Get(f.Op.Id)); Assert.True(File.Exists(f.Source));
    }
    [Fact]
    public async Task UnknownShortcutsAreNeverOverwritten()
    {
        using var f = new Fixture(); File.WriteAllText(Path.ChangeExtension(f.Source, ".url"), "occupied"); File.WriteAllText(f.Source + ".url", "occupied2");
        var path = await f.Replace(); Assert.Contains(f.Op.Id.ToString("N"), path); Assert.Equal("occupied", File.ReadAllText(Path.ChangeExtension(f.Source, ".url")));
    }
    [Fact]
    public async Task InterruptedPublicationResumesWithoutDuplicateShortcut()
    {
        using var f = new Fixture(); var path = InternetShortcut.Choose(f.Source, f.Op.Id); f.Journal.Begin(f.Op.Id, path, f.Url); InternetShortcut.Publish(path, InternetShortcut.Bytes(f.Url));
        Assert.Equal(path, await f.Replace()); Assert.Single(Directory.GetFiles(f.Root, "*.url"));
    }
    [Fact]
    public async Task TamperedShortcutBlocksRetirement()
    {
        using var f = new Fixture(); f.Browser.Fail = true; await Assert.ThrowsAsync<IOException>(() => f.Replace());
        File.WriteAllText(f.Journal.Get(f.Op.Id)!.ShortcutPath, "tampered");
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Replace()); Assert.True(File.Exists(f.Source));
    }
    [Fact]
    public async Task RetirementFailureLeavesIntentAndResumes()
    {
        using var f = new Fixture(); f.Reader.Fail = true; await Assert.ThrowsAsync<IOException>(() => f.Replace()); Assert.Equal(3, f.Journal.Get(f.Op.Id)!.Step); Assert.True(File.Exists(f.Source));
        f.Reader.Fail = false; await f.Replace(); Assert.Equal(1, f.Browser.Count);
    }
    [Fact]
    public async Task CrashAfterRemovalReconcilesMissingSource()
    {
        using var f = new Fixture(); var path = InternetShortcut.Choose(f.Source, f.Op.Id); f.Journal.Begin(f.Op.Id, path, f.Url); InternetShortcut.Publish(path, InternetShortcut.Bytes(f.Url));
        for (var step = 0; step < 3; step++) f.Journal.Advance(f.Op.Id, step, step + 1);
        File.Delete(f.Source); await f.Replace(); Assert.Equal(4, f.Journal.Get(f.Op.Id)!.Step); Assert.Equal(0, f.Browser.Count);
    }
    [Fact]
    public async Task MissingSourceBeforeIntentFailsClosed()
    {
        using var f = new Fixture(); File.Delete(f.Source); await Assert.ThrowsAsync<LocalConflictException>(() => f.Replace()); Assert.NotEqual(4, f.Journal.Get(f.Op.Id)!.Step);
    }
    [Fact]
    public async Task UnverifiedGoogleBindingBlocksAllLocalEffects()
    {
        using var f = new Fixture(false); await Assert.ThrowsAsync<InvalidOperationException>(() => f.Replace()); Assert.Null(f.Journal.Get(f.Op.Id)); Assert.True(File.Exists(f.Source));
    }
    [Fact]
    public async Task PublicationFailurePreservesOriginalAndBackup()
    {
        using var f = new Fixture(); var path = f.Source + ".url"; Directory.CreateDirectory(path); f.Journal.Begin(f.Op.Id, path, f.Url);
        await Assert.ThrowsAnyAsync<IOException>(() => f.Replace()); Assert.True(File.Exists(f.Source)); Assert.True(File.Exists(f.Op.Snapshot!.BackupPath)); Assert.Equal(0, f.Browser.Count);
    }
    [Fact]
    public async Task WrongRemoteUrlIsNeverPublished()
    {
        using var f = new Fixture(); await Assert.ThrowsAsync<InvalidOperationException>(() => f.Coordinator(f.Reader).ReplaceAsync(new(f.Op, GoogleDriveClient.Editor("other_id"))));
        Assert.Null(f.Journal.Get(f.Op.Id)); Assert.True(File.Exists(f.Source));
    }
    [Fact]
    public async Task CancelledOperationDoesNotRetireSource()
    {
        using var f = new Fixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Coordinator(f.Reader).ReplaceAsync(new(f.Op, f.Url), cancel.Token)); Assert.True(File.Exists(f.Source));
    }
    [Fact]
    public async Task ConcurrentRequestsHaveOneRetirementAndBrowserRequest()
    {
        using var f = new Fixture(); await Task.WhenAll(f.Replace(), f.Replace()); Assert.Equal(1, f.Browser.Count); Assert.Equal(4, f.Journal.Get(f.Op.Id)!.Step);
    }
    [Fact]
    public async Task CrashAfterBrowserReceiptResumesWithoutOpeningAgain()
    {
        using var f = new Fixture(); var path = InternetShortcut.Choose(f.Source, f.Op.Id); f.Journal.Begin(f.Op.Id, path, f.Url); InternetShortcut.Publish(path, InternetShortcut.Bytes(f.Url));
        f.Journal.Advance(f.Op.Id, 0, 1); f.Journal.Advance(f.Op.Id, 1, 2);
        await f.Replace(); Assert.False(File.Exists(f.Source)); Assert.Equal(0, f.Browser.Count);
    }
    [Fact]
    public async Task BackupRestoresOriginalWithoutOverwritingExistingFiles()
    {
        using var f = new Fixture(); await f.Replace(); await f.Backups.RestoreAsync(f.Op.Id, f.Op.Snapshot!, f.Source);
        Assert.Equal("original bytes", File.ReadAllText(f.Source));
        await Assert.ThrowsAnyAsync<IOException>(() => f.Backups.RestoreAsync(f.Op.Id, f.Op.Snapshot!, f.Source));
    }
    [WindowsFact]
    public async Task RecreatedSameBytesAfterRetirementIntentArePreserved()
    {
        using var f = new Fixture(); var path = InternetShortcut.Choose(f.Source, f.Op.Id); f.Journal.Begin(f.Op.Id, path, f.Url); InternetShortcut.Publish(path, InternetShortcut.Bytes(f.Url));
        for (var step = 0; step < 3; step++) f.Journal.Advance(f.Op.Id, step, step + 1);
        File.Move(f.Source, f.Source + ".old"); File.WriteAllText(f.Source, "original bytes");
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Coordinator(new WindowsRetirementReader(f.Root)).ReplaceAsync(new(f.Op, f.Url)));
        Assert.True(File.Exists(f.Source));
    }
    [Fact]
    public void JournalRejectsOutOfOrderTransitions()
    {
        using var f = new Fixture(); f.Journal.Begin(f.Op.Id, f.Source + ".url", f.Url);
        Assert.Throws<InvalidOperationException>(() => f.Journal.Advance(f.Op.Id, 1, 2)); Assert.Equal(0, f.Journal.Get(f.Op.Id)!.Step);
    }
    [WindowsFact]
    public async Task RenamedSourcePublishesShortcutInCurrentDirectory()
    {
        using var f = new Fixture(); var folder = Path.Combine(f.Root, "renamed"); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "Novo nome.xlsx");
        File.Move(f.Source, path);
        var shortcut = await f.Coordinator(new WindowsRetirementReader(f.Root)).ReplaceAsync(new(f.Op, f.Url, path));
        Assert.Equal(Path.ChangeExtension(path, ".url"), shortcut); Assert.False(File.Exists(path)); Assert.False(File.Exists(Path.ChangeExtension(f.Source, ".url")));
        Assert.Equal(path, f.Journal.Get(f.Op.Id)!.SourcePath);
    }
    [Fact]
    public void JournalMigratesExistingIntentWithoutLosingEvidence()
    {
        using var f = new Fixture(); var path = Path.Combine(f.Root, "legacy.db");
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TABLE replacements(id TEXT PRIMARY KEY,path TEXT NOT NULL,url TEXT NOT NULL,step INTEGER NOT NULL); PRAGMA user_version=1; INSERT INTO replacements VALUES($id,$path,$url,3)";
            cmd.Parameters.AddWithValue("$id", f.Op.Id.ToString("N")); cmd.Parameters.AddWithValue("$path", f.Source + ".url"); cmd.Parameters.AddWithValue("$url", f.Url.AbsoluteUri); cmd.ExecuteNonQuery();
        }
        var migrated = new ReplacementJournal(path).Get(f.Op.Id)!; Assert.Equal(3, migrated.Step); Assert.Equal(f.Url.AbsoluteUri, migrated.Url); Assert.Null(migrated.SourcePath);
    }
    [WindowsFact]
    public async Task NativeHandleRetiresOnlyVerifiedOriginal()
    {
        using var f = new Fixture(); var native = new WindowsRetirementReader(f.Root);
        f.Browser.OnOpen = () =>
        {
            Assert.Throws<IOException>(() => File.WriteAllText(f.Source, "changed"));
            Assert.Throws<IOException>(() => File.WriteAllText(f.Op.Snapshot!.BackupPath, "changed"));
            Assert.Throws<IOException>(() => File.WriteAllText(Path.ChangeExtension(f.Source, ".url"), "changed"));
        };
        var coordinator = f.Coordinator(native); await coordinator.ReplaceAsync(new(f.Op, f.Url)); Assert.False(File.Exists(f.Source)); Assert.True(File.Exists(f.Op.Snapshot!.BackupPath));
    }
    [WindowsFact]
    public async Task NativeLeaseDeniesWritesAndRenameAndChecksRoot()
    {
        using var f = new Fixture(); var native = new WindowsRetirementReader(f.Root);
        await using var lease = native.Open(f.Source);
        Assert.Throws<IOException>(() => File.WriteAllText(f.Source, "bad")); Assert.Throws<IOException>(() => File.Move(f.Source, f.Source + ".moved"));
        Assert.Throws<NotSupportedException>(() => new WindowsRetirementReader(Path.Combine(f.Root, "other")).Open(f.Source));
    }
    [Fact]
    public void RetirementDisabledOutsideWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Throws<PlatformNotSupportedException>(() => new WindowsRetirementReader("/tmp").Open("/tmp/a.xlsx"));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sw-replacement-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "Relatório [2026].xlsx");
        public ImportOperation Op { get; }
        public Uri Url { get; } = GoogleDriveClient.Editor("sheet_id");
        public SqliteOperationRegistry Local { get; }
        public GoogleRemoteRegistry Remote { get; }
        public BackupStore Backups { get; }
        public ReplacementJournal Journal { get; }
        public FakeBrowser Browser { get; } = new();
        public FakeReader Reader { get; } = new();
        public Fixture(bool verified = true)
        {
            Directory.CreateDirectory(Root); File.WriteAllText(Source, "original bytes");
            Local = new(Path.Combine(Root, "private", "registry.db")); Remote = new(Path.Combine(Root, "private", "google.db")); Backups = new(Path.Combine(Root, "private", "backups")); Journal = new(Path.Combine(Root, "private", "replacement.db"));
            var preparation = new LocalPreparation(Local, new SourceReader(), new FileOperationLock(Path.Combine(Root, "private", "locks")), Backups);
            Op = preparation.PrepareAsync("account", Source).GetAwaiter().GetResult();
            var key = "sheet:" + Op.Id.ToString("N"); Remote.Begin(key, "account", "sheet", Op.Snapshot!.Sha256); Remote.Candidate(key, "sheet_id"); if (verified) Remote.Verify(key, "sheet_id");
        }
        public ReplacementCoordinator Coordinator(IRetirementReader reader) => new(Local, Remote, Backups, new FileOperationLock(Path.Combine(Root, "private", "locks")), Journal, reader, Browser);
        public Task<string> Replace() => Coordinator(Reader).ReplaceAsync(new(Op, Url));
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class FakeBrowser : IBrowserLauncher
    {
        public bool Fail; public int Count; public Action? OnOpen;
        public void Open(Uri url) { if (Fail) throw new IOException("Browser refused"); OnOpen?.Invoke(); Count++; }
    }
    // Delete-by-path exists ONLY in this test double; production requires a Windows DELETE handle.
    private sealed class FakeReader : IRetirementReader
    {
        public bool Fail;
        public IRetirementLease Open(string path) => new FakeLease(new SourceReader().Open(path), this);
        private sealed class FakeLease(ISourceLease lease, FakeReader owner) : IRetirementLease
        {
            private bool retire;
            public SourceDescriptor Source => lease.Source;
            public Stream Content => lease.Content;
            public void Retire() { if (owner.Fail) throw new IOException("Retirement refused"); retire = true; }
            public async ValueTask DisposeAsync() { await lease.DisposeAsync(); if (retire) File.Delete(Source.Path); }
        }
    }
}
