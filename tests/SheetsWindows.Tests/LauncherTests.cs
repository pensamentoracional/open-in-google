using SheetsWindows.Core;
using SheetsWindows.Infrastructure;
using Xunit;

namespace SheetsWindows.Tests;

public sealed class LauncherTests
{
    [Theory]
    [InlineData("report.csv")]
    [InlineData("report.xlsx")]
    [InlineData("https://docs.google.com/spreadsheets/d/id/edit")]
    [InlineData("--unknown")]
    [InlineData("/tmp/file.url")]
    [InlineData("/tmp/file.xlsx\n")]
    public void InvalidActivationsRejected(string path) => Assert.Throws<ArgumentException>(() => LauncherRequest.Parse([path]));
    [Fact]
    public void MultipleFilesAndUnexpectedOptionsRejected()
    {
        Assert.Throws<ArgumentException>(() => LauncherRequest.Parse(["--open", "a.xlsx", "b.xlsx"]));
        Assert.Throws<ArgumentException>(() => LauncherRequest.Parse(["--open"]));
        Assert.Throws<ArgumentException>(() => LauncherRequest.Parse(["--login", "a.xlsx"]));
    }
    [Fact]
    public void QuotedShellArgumentsAreAlreadyParsedAndRemainLiteral()
    {
        using var w = new Workspace(); var path = Path.Combine(w.Root, "Relatório & [2026] %teste%.XLSX");
        Assert.Equal(path, LauncherRequest.Parse(["--open", path]).Path); Assert.Equal(path, LauncherRequest.Parse([path]).Path);
        Assert.Equal(LauncherAction.Home, LauncherRequest.Parse([]).Action); Assert.Equal(LauncherAction.Login, LauncherRequest.Parse(["--login"]).Action);
    }
    [Fact]
    public void RegistrationNeverClaimsUnsupportedFormatsOrUserChoice()
    {
        using var w = new Workspace(); var plan = WindowsAssociationPlan.Values(Path.Combine(w.Root, "SheetsWindows.exe"));
        Assert.DoesNotContain(plan, v => v.Key.Contains("UserChoice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan, v => v.Key == @"Software\Classes\.xlsx");
        Assert.DoesNotContain(plan, v => v.Key.Contains(".url", StringComparison.OrdinalIgnoreCase) || v.Name is ".xlsm" or ".xlsb");
        Assert.Equal(SpreadsheetFormats.Extensions.Order(), plan.Where(v => v.Key == WindowsAssociationPlan.CapabilityPath + @"\FileAssociations").Select(v => v.Name).Order());
        Assert.Equal("ZagoSheetsWin", Uri.UnescapeDataString(WindowsAssociationPlan.DefaultsUri.Query.Split('=')[1]));
    }
    [Fact]
    public void UnsafeExecutableCommandsRejected()
    {
        using var w = new Workspace();
        Assert.Throws<ArgumentException>(() => WindowsAssociationPlan.Command("SheetsWindows.exe"));
        Assert.Throws<ArgumentException>(() => WindowsAssociationPlan.Command(Path.Combine(w.Root, "cmd.exe")));
        Assert.Throws<ArgumentException>(() => WindowsAssociationPlan.Command(Path.Combine(w.Root, "%1", "SheetsWindows.exe")));
    }
    [Fact]
    public async Task ConfigurationPreservesClientNamespaceAndIsBounded()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "state"));
        LauncherConfiguration.SaveClient(storage, "{\"installed\":{\"client_id\":\"one.apps.googleusercontent.com\"}}");
        LauncherConfiguration.SaveClient(storage, "{\"installed\":{\"client_id\":\"one.apps.googleusercontent.com\"}}");
        Assert.Equal("one.apps.googleusercontent.com", (await LauncherConfiguration.LoadClientAsync(storage)).Id);
        Assert.Throws<LocalConflictException>(() => LauncherConfiguration.SaveClient(storage, "{\"installed\":{\"client_id\":\"two.apps.googleusercontent.com\"}}"));
        Assert.Throws<InvalidDataException>(() => LauncherConfiguration.SaveClient(storage, new string('x', 65537)));
        Assert.Empty(Directory.GetFiles(storage.Root, "*.tmp"));
    }
    [Fact]
    public async Task SetupUsesOfficialClientAndPinsIdentity()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "state"));
        if (typeof(LauncherConfiguration).Assembly.GetManifestResourceInfo("OAuth.official.desktop.json") is null)
        {
            await Assert.ThrowsAsync<LauncherNotConfiguredException>(() => LauncherConfiguration.SetupClientJsonAsync(storage));
            return;
        }
        var json = await LauncherConfiguration.SetupClientJsonAsync(storage);
        Assert.EndsWith(".apps.googleusercontent.com", OAuthClient.FromJson(json).Id);
        Assert.False(File.Exists(LauncherConfiguration.ClientPath(storage)));
        LauncherConfiguration.SaveClient(storage, json);
        Assert.Equal(json, await LauncherConfiguration.SetupClientJsonAsync(storage, "missing-custom.json"));
    }
    [Fact]
    public async Task SetupPreservesCustomAndRejectsInvalidExistingClient()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "state"));
        var custom = Path.Combine(w.Root, "custom.json");
        var json = "{\"installed\":{\"client_id\":\"custom.apps.googleusercontent.com\"}}";
        File.WriteAllText(custom, json);
        Assert.Equal(json, await LauncherConfiguration.SetupClientJsonAsync(storage, custom));
        LauncherConfiguration.SaveClient(storage, json);
        Assert.Equal(json, await LauncherConfiguration.SetupClientJsonAsync(storage));
        File.WriteAllText(LauncherConfiguration.ClientPath(storage), "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => LauncherConfiguration.SetupClientJsonAsync(storage));
        Assert.Equal("{}", File.ReadAllText(LauncherConfiguration.ClientPath(storage)));
    }
    [Fact]
    public async Task MissingConfigurationStopsBeforeNetworkOrFilesystemEffects()
    {
        using var w = new Workspace(); using var http = new HttpClient(new RejectNetwork());
        var service = new WindowsLauncher(new LocalStorage(Path.Combine(w.Root, "unconfigured")), http, new RejectBrowser());
        await Assert.ThrowsAsync<LauncherNotConfiguredException>(() => service.OpenAsync(w.Source)); Assert.True(File.Exists(w.Source));
    }
    [Fact]
    public void ErrorsNeverExposeExceptionDetails()
    {
        var ex = new HttpRequestException("access_token=secret and private contents");
        Assert.True(LauncherErrors.Expected(ex)); Assert.DoesNotContain("secret", LauncherErrors.Message(ex));
        Assert.Contains("Salvar e conectar Google", LauncherErrors.Message(new AuthorizationRequiredException()));
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Unexpected network.");
    }
    private sealed class RejectBrowser : IBrowserLauncher { public void Open(Uri uri) => throw new InvalidOperationException("Unexpected browser."); }
}
