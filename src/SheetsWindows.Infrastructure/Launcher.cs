using System.ComponentModel;
using System.Security;
using System.Text;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public enum LauncherAction { Home, Open, Login, Defaults, Version, Setup, Recovery, Register, Unregister }
public sealed record LauncherRequest(LauncherAction Action, string? Path = null)
{
    public static LauncherRequest Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--setup") return new(LauncherAction.Setup);
        if (args.Length == 1 && args[0] == "--recovery") return new(LauncherAction.Recovery);
        if (args.Length == 1 && args[0] == "--register") return new(LauncherAction.Register);
        if (args.Length == 1 && args[0] == "--unregister") return new(LauncherAction.Unregister);
        if (args.Length == 0) return new(LauncherAction.Home);
        if (args.Length == 1 && args[0] == "--version") return new(LauncherAction.Version);
        if (args.Length == 1 && args[0] == "--login") return new(LauncherAction.Login);
        if (args.Length == 1 && args[0] == "--defaults") return new(LauncherAction.Defaults);
        var path = args.Length == 1 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] :
            args.Length == 2 && args[0] == "--open" ? args[1] : throw new ArgumentException("One XLSX path required.");
        if (!System.IO.Path.IsPathFullyQualified(path) || path.Any(char.IsControl) || path.Contains('"')
            || !string.Equals(System.IO.Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Absolute XLSX path required.");
        return new(LauncherAction.Open, System.IO.Path.GetFullPath(path));
    }
}
public sealed class LauncherNotConfiguredException() : InvalidOperationException("Launcher setup required.");
public static class LauncherConfiguration
{
    public static string ClientPath(LocalStorage storage) => System.IO.Path.Combine(storage.Root, "launcher-client.json");
    public static void SaveClient(LocalStorage storage, string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 65536) throw new InvalidDataException("Client configuration too large.");
        var client = OAuthClient.FromJson(json); PrivateDirectory.Create(storage.Root);
        var path = ClientPath(storage);
        if (File.Exists(path))
        {
            if (OAuthClient.FromJson(File.ReadAllText(path)).Id != client.Id) throw new LocalConflictException("Changing OAuth client requires a separate migration.");
            return; // Preserve configured client; never change account namespace through registration.
        }
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(Encoding.UTF8.GetBytes(json)); file.Flush(true); }
            File.Move(tmp, path, overwrite: false);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static async Task<OAuthClient> LoadClientAsync(LocalStorage storage, CancellationToken ct = default)
    {
        var path = ClientPath(storage);
        if (!File.Exists(path)) throw new LauncherNotConfiguredException();
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 65536 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid launcher configuration.");
        using var reader = new StreamReader(file); return OAuthClient.FromJson(await reader.ReadToEndAsync(ct));
    }
}
public static class LauncherErrors
{
    public static bool Expected(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
        or NotSupportedException or ArgumentException or Win32Exception or HttpRequestException or OperationCanceledException
        or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException or System.Xml.XmlException or KeyNotFoundException or SecurityException;
    public static string Message(Exception ex) => ex switch
    {
        LauncherNotConfiguredException => "Conclua a configuração do piloto antes de abrir planilhas. Abra o Sheets Windows e clique em Configurar piloto.",
        AuthorizationRequiredException => "O Google precisa de autorização. Abra o Sheets Windows e clique em Autorizar Google; depois abra a planilha novamente.",
        ReconciliationRequiredException => "A importação aguarda reconciliação. Não repita o upload manualmente. Consulte o guia de recuperação.",
        LocalConflictException => "O arquivo ou sua associação mudou. A substituição foi interrompida para conservar as versões existentes.",
        OperationCanceledException => "Operação interrompida. O backup e o registro de recuperação, quando criados, foram conservados.",
        _ => "Não foi possível concluir. Confira a configuração, a conexão e se o arquivo está aberto em outro aplicativo. Backups já criados permanecem disponíveis."
    };
}

public sealed class WindowsLauncher(LocalStorage storage, HttpClient http, IBrowserLauncher browser)
{
    private GoogleOAuth Auth(OAuthClient client, FileOperationLock locks) => new(http, client,
        new DpapiTokenVault(System.IO.Path.Combine(storage.Root, "auth"), client.Id), new LoopbackAuthorizationReceiver(browser.Open), locks);
    public async Task LoginAsync(CancellationToken ct = default)
    {
        var client = await LauncherConfiguration.LoadClientAsync(storage, ct);
        await Auth(client, new FileOperationLock(storage.LocksPath)).ConnectAsync(ct);
    }
    public async Task<string> OpenAsync(string path, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Explorer activation is always replacement, never the diagnostic import command.
        var request = LauncherRequest.Parse(["--open", path]);
        var client = await LauncherConfiguration.LoadClientAsync(storage, ct);
        var policy = System.IO.Path.Combine(storage.Root, "replacement-root.txt");
        if (!File.Exists(policy)) throw new LauncherNotConfiguredException();
        var root = await File.ReadAllTextAsync(policy, ct); var sources = new WindowsRetirementReader(root);
        await using (var eligibility = sources.Open(request.Path!)) { }
        progress?.Report("Conferindo arquivo e backup…");
        var preparation = storage.CreatePreparation(); var local = new SqliteOperationRegistry(storage.DatabasePath);
        var remote = new GoogleRemoteRegistry(System.IO.Path.Combine(storage.Root, "google.db")); var locks = new FileOperationLock(storage.LocksPath);
        var auth = Auth(client, locks);
        var importer = new GoogleImport(preparation, local, remote, new SourceReader(), locks, auth, new GoogleDriveClient(http, auth));
        progress?.Report("Abrindo sua planilha no Google Sheets…");
        var receipt = await importer.ImportReceiptAsync(request.Path!, ct);
        progress?.Report("Concluindo substituição. Operação: " + receipt.Operation.Id);
        var coordinator = new ReplacementCoordinator(local, remote, new BackupStore(storage.BackupsPath), locks,
            new ReplacementJournal(System.IO.Path.Combine(storage.Root, "replacement.db")), sources, browser);
        return await coordinator.ReplaceAsync(receipt, ct);
    }
}
