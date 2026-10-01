using System.Diagnostics;
using SheetsWindows.Core;
using SheetsWindows.Infrastructure;

if (args.Length != 3 || args[0] is not ("login" or "import" or "replace" or "resume" or "configure-replacement" or "restore" or "configure-launcher" or "unregister-windows"))
{
    Console.Error.WriteLine("Commands: configure-launcher/unregister-windows <desktop-client.json> <published-SheetsWindows.exe>; configure-replacement <desktop-client.json> <unsynced-local-folder>, replace <desktop-client.json> <file.xlsx>, resume/restore <desktop-client.json> <operation-guid>. Usage: SheetsWindows.Cli login <desktop-client.json> <storage-root> OR import <desktop-client.json> <file.xlsx>"); return 2;
}
try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This pilot requires Windows DPAPI.");
    var client = OAuthClient.FromJson(await File.ReadAllTextAsync(args[1]));
    var storage = args[0] == "login" ? new LocalStorage(Path.GetFullPath(args[2])) : LocalStorage.ForCurrentUser();
    // The login root must be the same default root used for import; arbitrary roots are for tests only.
    if (args[0] == "login" && Path.GetFullPath(storage.Root) != Path.GetFullPath(LocalStorage.ForCurrentUser().Root)) throw new ArgumentException("Use LOCALAPPDATA/SheetsWindows for login.");
    if (args[0] is "configure-launcher" or "unregister-windows")
    {
        await using var registryLock = await new FileOperationLock(storage.LocksPath).AcquireAsync("windows-registration");
        var registration = new WindowsAssociationRegistration(Microsoft.Win32.Registry.CurrentUser);
        if (args[0] == "configure-launcher")
        {
            var exe = Path.GetFullPath(args[2]); _ = WindowsAssociationPlan.Command(exe);
            if (!File.Exists(exe)) throw new FileNotFoundException("Publish launcher first.");
            LauncherConfiguration.SaveClient(storage, await File.ReadAllTextAsync(args[1])); registration.Register(exe);
            Console.WriteLine("ZagoSheetsWin registrado para XLSX. Escolha o padrão nas Configurações do Windows; autorize o Google uma vez.");
        }
        else { registration.Unregister(); Console.WriteLine("Registro do candidato removido. Documentos, atalhos e backups foram conservados."); }
        WindowsAssociationRegistration.NotifyShell(); return 0;
    }
    var policyPath = Path.Combine(storage.Root, "replacement-root.txt");
    if (args[0] == "configure-replacement")
    {
        var root = Path.GetFullPath(args[2]);
        if (!Directory.Exists(root)) throw new ArgumentException("Existing local directory required.");
        storage.CreatePreparation();
        Console.WriteLine("Configuração: esta pasta foi declarada local, particular e sem sincronização. A conversão pode perder recursos do Excel. Após backup e validação, replace retirará o XLSX e manterá um .url; backups privados serão conservados.");
        if (File.Exists(policyPath)) throw new InvalidOperationException("Replacement already configured.");
        var temporaryPolicy = policyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var policy = new FileStream(temporaryPolicy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var policyBytes = System.Text.Encoding.UTF8.GetBytes(root); policy.Write(policyBytes); policy.Flush(true);
            }
            File.Move(temporaryPolicy, policyPath, overwrite: false); return 0;
        }
        finally { if (File.Exists(temporaryPolicy)) File.Delete(temporaryPolicy); }
    }
    if (args[0] == "restore")
    {
        var operation = new SqliteOperationRegistry(storage.DatabasePath).Get(Guid.Parse(args[2])) ?? throw new InvalidOperationException("Unknown operation.");
        var destination = new ReplacementJournal(Path.Combine(storage.Root, "replacement.db")).Get(operation.Id)?.SourcePath ?? operation.SourcePath;
        await new BackupStore(storage.BackupsPath).RestoreAsync(operation.Id, operation.Snapshot ?? throw new InvalidOperationException("Missing snapshot."), destination);
        Console.WriteLine("Backup restaurado sem sobrescrever arquivos: " + destination); return 0;
    }
    var sourceReader = new SourceReader(copyEnvironments: args[0] == "import");
    var textOptions = args[0] is "import" or "replace" && SpreadsheetFormats.Format(args[2]) != "xlsx" ? ExtendedConfiguration.Load(storage) : null;
    var preparation = storage.CreatePreparation(sourceReader); var locks = new FileOperationLock(storage.LocksPath);
    using var handler = new HttpClientHandler { AllowAutoRedirect = false }; using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    var auth = new GoogleOAuth(http, client, new DpapiTokenVault(Path.Combine(storage.Root, "auth"), client.Id),
        new LoopbackAuthorizationReceiver(uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })), locks);
    if (args[0] == "login") { await auth.ConnectAsync(); Console.WriteLine("Google autorizado."); return 0; }
    var importer = new GoogleImport(preparation, new SqliteOperationRegistry(storage.DatabasePath), new GoogleRemoteRegistry(Path.Combine(storage.Root, "google.db")), sourceReader, locks, auth, new GoogleDriveClient(http, auth, new UploadSessionStore(Path.Combine(storage.Root, "uploads"))), textOptions);
    if (args[0] is "replace" or "resume")
    {
        var root = await File.ReadAllTextAsync(policyPath);
        var reader = new WindowsRetirementReader(root);
        ImportReceipt receipt;
        if (args[0] == "replace")
        {
            await using (var eligibility = reader.Open(args[2])) { }
            receipt = await importer.ImportReceiptAsync(args[2]);
        }
        else
        {
            var operation = new SqliteOperationRegistry(storage.DatabasePath).Get(Guid.Parse(args[2])) ?? throw new InvalidOperationException("Unknown operation.");
            var mapping = new GoogleRemoteRegistry(Path.Combine(storage.Root, "google.db")).Get("sheet:" + operation.Id.ToString("N")) ?? throw new InvalidOperationException("Missing association.");
            var access = await auth.AccessAsync();
            if (access.AccountId != operation.AccountId || mapping.FileId is null) throw new InvalidOperationException("Account changed.");
            var file = await new GoogleDriveClient(http, auth).GetAsync(access.AccountId, mapping.FileId, CancellationToken.None);
            if (file.Trashed || !file.CanEdit || file.MimeType != GoogleDriveClient.SheetMime || !file.Properties.TryGetValue("sw_operation", out var marker) || marker != mapping.Marker
                || !file.Properties.TryGetValue("sw_hash", out var hash) || hash != mapping.Hash) throw new InvalidOperationException("Remote association changed.");
            receipt = new ImportReceipt(operation, GoogleDriveClient.Editor(mapping.FileId), new ReplacementJournal(Path.Combine(storage.Root, "replacement.db")).Get(operation.Id)?.SourcePath ?? operation.SourcePath);
        }
        Console.WriteLine("Operação: " + receipt.Operation.Id);
        var replacement = new ReplacementCoordinator(new SqliteOperationRegistry(storage.DatabasePath), new GoogleRemoteRegistry(Path.Combine(storage.Root, "google.db")),
            new BackupStore(storage.BackupsPath), locks, new ReplacementJournal(Path.Combine(storage.Root, "replacement.db")), reader, new BrowserLauncher(), new ConversionVerifier(new GoogleDriveClient(http, auth), textOptions ?? (receipt.Operation.Format == "xlsx" ? null : ExtendedConfiguration.Load(storage))));
        Console.WriteLine(await replacement.ReplaceAsync(receipt)); return 0;
    }
    var url = await importer.ImportAsync(args[2]); Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); Console.WriteLine(url.AbsoluteUri); return 0;
}
catch (Exception ex) when (LauncherErrors.Expected(ex))
{
    Console.Error.WriteLine(ex is AuthorizationRequiredException ? "Autentique com o comando login." : ex is ReconciliationRequiredException ? "Resultado pendente de reconciliação. O original permanece preservado." : "A operação não foi concluída. Consulte o journal e o backup privado antes de retomar; não recrie nem remova arquivos manualmente."); return 1;
}
