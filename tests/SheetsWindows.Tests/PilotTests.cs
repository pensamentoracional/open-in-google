using SheetsWindows.Core;
using SheetsWindows.Infrastructure;
using Xunit;

namespace SheetsWindows.Tests;

public sealed class PilotTests
{
    private const string Client = "{\"installed\":{\"client_id\":\"pilot.apps.googleusercontent.com\",\"client_secret\":\"test-only\"}}";
    [Fact]
    public void SetupRequiresDeclarationBeforeWriting()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "private"));
        Assert.Throws<ArgumentException>(() => PilotSetup.Configure(storage, Client, w.Root, false));
        Assert.False(Directory.Exists(storage.Root));
    }
    [Fact]
    public void SetupCanResumeAndPreservesExistingPolicyAndClient()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "private"));
        PilotSetup.Configure(storage, Client, w.Root, true); PilotSetup.Configure(storage, Client, w.Root, true);
        Assert.Equal(Client, File.ReadAllText(LauncherConfiguration.ClientPath(storage)));
        var other = Path.Combine(w.Root, "other"); Directory.CreateDirectory(other);
        Assert.Throws<LocalConflictException>(() => PilotSetup.Configure(storage, Client, other, true));
        Assert.Throws<LocalConflictException>(() => PilotSetup.Configure(storage, Client.Replace("pilot.apps", "different.apps"), w.Root, true));
        Assert.Equal(w.Root, File.ReadAllText(PilotSetup.PolicyPath(storage)));
    }
    [Fact]
    public async Task OfflineRecoveryListsAndRestoresWithoutClientOrGoogle()
    {
        using var w = new Workspace(); var op = await w.Coordinator().PrepareAsync("A", w.Source);
        var service = new BackupRecovery(new LocalStorage(Path.Combine(w.Root, "state")));
        Assert.Equal(op.Id, Assert.Single(service.List()).Id);
        var destination = Path.Combine(w.Root, "restored.xlsx"); await service.RestoreAsync(op.Id, destination);
        Assert.Equal(File.ReadAllBytes(w.Source), File.ReadAllBytes(destination));
        Assert.True(File.Exists(op.Snapshot!.BackupPath));
        await Assert.ThrowsAsync<IOException>(() => service.RestoreAsync(op.Id, destination));
        Assert.Equal(File.ReadAllBytes(w.Source), File.ReadAllBytes(destination));
    }
    [Fact]
    public async Task RecoveryRejectsCorruptionAndDoesNotPublishDestination()
    {
        using var w = new Workspace(); var op = await w.Coordinator().PrepareAsync("A", w.Source);
        File.WriteAllBytes(op.Snapshot!.BackupPath, [9, 9, 9, 9]);
        var destination = Path.Combine(w.Root, "restored.xlsx");
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupRecovery(new LocalStorage(Path.Combine(w.Root, "state"))).RestoreAsync(op.Id, destination));
        Assert.False(File.Exists(destination));
    }
    [Fact]
    public void EmptyRecoveryDoesNotCreateState()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "absent"));
        Assert.Empty(new BackupRecovery(storage).List()); Assert.False(Directory.Exists(storage.Root));
    }
    [Theory]
    [InlineData("--setup", LauncherAction.Setup)]
    [InlineData("--recovery", LauncherAction.Recovery)]
    [InlineData("--register", LauncherAction.Register)]
    [InlineData("--unregister", LauncherAction.Unregister)]
    public void MaintenanceCommandsAreStrict(string argument, LauncherAction action)
    {
        Assert.Equal(action, LauncherRequest.Parse([argument]).Action);
        Assert.Throws<ArgumentException>(() => LauncherRequest.Parse([argument, "extra"]));
    }
}
