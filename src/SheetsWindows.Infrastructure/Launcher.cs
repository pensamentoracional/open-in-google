using System.ComponentModel;
using System.Security;
using System.Text;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public enum LauncherAction { Home, Open, Login, Defaults, Version, Setup, Recovery, Register, Unregister, Copy }
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
            args.Length == 2 && args[0] is "--open" or "--copy" ? args[1] : throw new ArgumentException("One spreadsheet path required.");
        if (!System.IO.Path.IsPathFullyQualified(path) || path.Any(char.IsControl) || path.Contains('"')
            || !SpreadsheetFormats.Extensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) throw new ArgumentException("Absolute supported spreadsheet path required.");
        return new(args.Length == 2 && args[0] == "--copy" ? LauncherAction.Copy : LauncherAction.Open, System.IO.Path.GetFullPath(path));
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
        or TimeoutException or NotSupportedException or ArgumentException or Win32Exception or HttpRequestException or OperationCanceledException
        or System.Security.Cryptography.CryptographicException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException or System.Xml.XmlException or KeyNotFoundException or SecurityException or ExcelDataReader.Exceptions.ExcelReaderException;
    public static string Message(Exception ex) => ex switch
    {
        LauncherNotConfiguredException => "Conclua a configuração do piloto antes de abrir planilhas. Abra o ZagoSheetsWin e clique em Configurar piloto.",
        AuthorizationRequiredException => "O Google precisa de autorização. Abra o ZagoSheetsWin e clique em Autorizar Google; depois abra a planilha novamente.",
        ReconciliationRequiredException => "A importação aguarda reconciliação. Não repita o upload manualmente. Consulte o guia de recuperação.",
        ConversionMismatchException => "A conferência encontrou diferença nos dados convertidos. O original e o backup foram preservados. Pode existir uma cópia no Google; consulte a recuperação antes de repetir.",
        SpreadsheetCapacityException capacity => capacity.Message + " O original foi preservado. Divida a tabela em arquivos menores para tentar novamente.",
        InvalidDataException data when data.Message is "Invalid text encoding or characters." or "Encoding conflicts with BOM." => "Não foi possível ler a codificação do CSV/TSV. Use UTF-8 ou UTF-16 com BOM; para arquivos antigos, selecione Windows-1252 nas opções de texto. O original foi preservado.",
        InvalidDataException data when data.Message == "Ambiguous CSV delimiter; configure it explicitly." => "O CSV pode usar vírgula ou ponto e vírgula. Escolha o separador nas opções de texto. O original foi preservado.",
        InvalidDataException data when data.Message is "Irregular delimited table." or "Invalid quoted field." or "Unclosed quoted field." or "Empty text spreadsheet." or "Empty table." => "O CSV/TSV está vazio ou contém linhas, separadores ou aspas inconsistentes. Confira o separador e a estrutura do arquivo. O original foi preservado.",
        InvalidDataException data when data.Message == "Export too large." => "A exportação excedeu o limite da conferência. O original e o backup foram preservados; consulte a recuperação antes de repetir a importação.",
        GoogleApiException api when api.Status == 429 || api.Status >= 500 => "O Google está temporariamente indisponível ou limitou as requisições. O original foi preservado. Aguarde e retome pela recuperação, evitando upload repetido.",
        GoogleApiException api when api.Status is 401 or 403 => "O Google recusou o acesso. Confira a autorização e as permissões da conta/pasta. O original foi preservado.",
        GoogleApiException => "O Google recusou a importação ou a exportação para conferência. O original foi preservado. Consulte o diagnóstico e a recuperação antes de repetir.",
        HttpRequestException => "Falha ao comunicar com o Google. Confira a conexão e retome pelo aplicativo; o original foi preservado e uma operação pendente pode precisar de recuperação.",
        CopyRequiredException => "Esta planilha requer importação de cópia; o original deve ser preservado.",
        LocalConflictException => "O arquivo ou sua associação mudou. A substituição foi interrompida para conservar as versões existentes.",
        TimeoutException => "Outra operação ainda está usando o arquivo ou o registro. Aguarde e retome; o original e os backups foram conservados.",
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
        // Local replacement is limited to the configured unsynced root.
        var request = LauncherRequest.Parse(["--open", path]);
        var client = await LauncherConfiguration.LoadClientAsync(storage, ct);
        var policy = System.IO.Path.Combine(storage.Root, "replacement-root.txt");
        if (!File.Exists(policy)) throw new LauncherNotConfiguredException();
        if (SpreadsheetFormats.Format(path) == "xls") return await CopyAsync(path, progress, ct);
        var root = await File.ReadAllTextAsync(policy, ct); var sources = new WindowsRetirementReader(root);
        await using (var eligibility = sources.Open(request.Path!)) { }
        progress?.Report("Conferindo arquivo e backup…");
        var textOptions = SpreadsheetFormats.Format(path) == "xlsx" ? null : ExtendedConfiguration.Load(storage);
        var preparation = storage.CreatePreparation(); var local = new SqliteOperationRegistry(storage.DatabasePath);
        var remote = new GoogleRemoteRegistry(System.IO.Path.Combine(storage.Root, "google.db")); var locks = new FileOperationLock(storage.LocksPath);
        var auth = Auth(client, locks);
        var importer = new GoogleImport(preparation, local, remote, new SourceReader(), locks, auth, new GoogleDriveClient(http, auth, new UploadSessionStore(Path.Combine(storage.Root, "uploads"))), textOptions);
        progress?.Report("Abrindo sua planilha no Google Sheets…");
        var receipt = await importer.ImportReceiptAsync(request.Path!, ct);
        if (!receipt.CanReplace) return await PublishCopyAsync(receipt, progress, ct);
        progress?.Report("Concluindo substituição. Operação: " + receipt.Operation.Id);
        var coordinator = new ReplacementCoordinator(local, remote, new BackupStore(storage.BackupsPath), locks,
            new ReplacementJournal(System.IO.Path.Combine(storage.Root, "replacement.db")), sources, browser, new ConversionVerifier(new GoogleDriveClient(http, auth), textOptions));
        return await coordinator.ReplaceAsync(receipt, ct);
    }
    public async Task<string> CopyAsync(string path, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _ = LauncherRequest.Parse(["--copy", path]);
        var client = await LauncherConfiguration.LoadClientAsync(storage, ct);
        var options = SpreadsheetFormats.Format(path) == "xlsx" ? null : ExtendedConfiguration.Load(storage);
        var sources = new SourceReader(copyEnvironments: true);
        // Preflight is read-only, does not hydrate online-only placeholders and precedes OAuth/network.
        await using (var preflight = sources.Open(path)) { }
        var locks = new FileOperationLock(storage.LocksPath); var auth = Auth(client, locks);
        var importer = new GoogleImport(storage.CreatePreparation(sources), new SqliteOperationRegistry(storage.DatabasePath),
            new GoogleRemoteRegistry(Path.Combine(storage.Root, "google.db")), sources, locks, auth, new GoogleDriveClient(http, auth, new UploadSessionStore(Path.Combine(storage.Root, "uploads"))), options);
        progress?.Report("Importando cópia; o original será conservado…");
        return await PublishCopyAsync(await importer.ImportReceiptAsync(path, ct), progress, ct);
    }
    public async Task<string> ResumeAsync(Guid id, bool replace, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var local = new SqliteOperationRegistry(storage.DatabasePath);
        var operation = local.Get(id) ?? throw new InvalidOperationException("Unknown operation.");
        var journal = new ReplacementJournal(Path.Combine(storage.Root, "replacement.db"));
        var recorded = journal.Get(id); var path = recorded?.SourcePath ?? operation.SourcePath;
        var remote = new GoogleRemoteRegistry(Path.Combine(storage.Root, "google.db"));
        var mapping = remote.Get("sheet:" + id.ToString("N")) ?? throw new ReconciliationRequiredException();
        var client = await LauncherConfiguration.LoadClientAsync(storage, ct); var locks = new FileOperationLock(storage.LocksPath); var auth = Auth(client, locks);
        var access = await auth.AccessAsync(cancellationToken: ct);
        if (access.AccountId != operation.AccountId || mapping.AccountId != operation.AccountId) throw new LocalConflictException("Account changed.");
        var drive = new GoogleDriveClient(http, auth, new UploadSessionStore(Path.Combine(storage.Root, "uploads")));
        var options = operation.Format == "xlsx" ? null : ExtendedConfiguration.Load(storage);
        ImportReceipt receipt;
        if (File.Exists(path))
        {
            var sources = new SourceReader(copyEnvironments: !replace);
            await using (var source = sources.Open(path))
            {
                if (source.Source.IdentityKey != operation.SourceKey || source.Source.Format != operation.Format) throw new LocalConflictException("Source changed.");
                var importer = new GoogleImport(storage.CreatePreparation(sources), local, remote, sources, locks, auth, drive, options);
                receipt = await importer.ImportReceiptAsync(path, ct);
                if (receipt.Operation.Id != id) throw new LocalConflictException("Operation changed.");
            }
        }
        else
        {
            // Only a recorded retirement can reconcile an absent original; no new upload is allowed.
            if (!replace || recorded is null || recorded.Step < 3 || mapping.FileId is null || !mapping.Verified) throw new ReconciliationRequiredException();
            var file = await drive.GetAsync(access.AccountId, mapping.FileId, ct);
            if (file.Trashed || !file.CanEdit || file.MimeType != GoogleDriveClient.SheetMime
                || !file.Properties.TryGetValue("sw_operation", out var marker) || marker != mapping.Marker
                || !file.Properties.TryGetValue("sw_hash", out var hash) || hash != mapping.Hash) throw new ReconciliationRequiredException();
            receipt = new(operation, GoogleDriveClient.Editor(mapping.FileId), path);
        }
        if (!replace) return await PublishCopyAsync(receipt, progress, ct);
        if (!receipt.CanReplace || operation.Format == "xls") throw new CopyRequiredException();
        var root = await File.ReadAllTextAsync(PilotSetup.PolicyPath(storage), ct);
        return await new ReplacementCoordinator(local, remote, new BackupStore(storage.BackupsPath), locks, journal,
            new WindowsRetirementReader(root), browser, new ConversionVerifier(drive, options)).ReplaceAsync(receipt, ct);
    }

    private async Task<string> PublishCopyAsync(ImportReceipt receipt, IProgress<string>? progress, CancellationToken ct)
    {
        await using var held = await new FileOperationLock(storage.LocksPath).AcquireAsync(receipt.Operation.SourceKey, ct);
        var folder = Path.Combine(storage.Root, "shortcuts"); PrivateDirectory.Create(folder);
        var shortcut = Path.Combine(folder, receipt.Operation.Id.ToString("N") + ".url"); var bytes = InternetShortcut.Bytes(receipt.Url);
        if (!File.Exists(shortcut)) InternetShortcut.Publish(shortcut, bytes);
        using var checkedShortcut = InternetShortcut.Hold(shortcut, bytes);
        ct.ThrowIfCancellationRequested(); browser.Open(receipt.Url);
        progress?.Report("Cópia aberta no Sheets. Original preservado; atalho disponível na pasta de atalhos do aplicativo.");
        return shortcut;
    }

}
