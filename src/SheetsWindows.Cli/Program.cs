using System.Diagnostics;
using SheetsWindows.Core;
using SheetsWindows.Infrastructure;

if (args.Length != 3 || args[0] is not ("login" or "import"))
{
    Console.Error.WriteLine("Usage: SheetsWindows.Cli login <desktop-client.json> <storage-root> OR import <desktop-client.json> <file.xlsx>"); return 2;
}
try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This pilot requires Windows DPAPI.");
    var client = OAuthClient.FromJson(await File.ReadAllTextAsync(args[1]));
    var storage = args[0] == "login" ? new LocalStorage(Path.GetFullPath(args[2])) : LocalStorage.ForCurrentUser();
    // The login root must be the same default root used for import; arbitrary roots are for tests only.
    if (args[0] == "login" && Path.GetFullPath(storage.Root) != Path.GetFullPath(LocalStorage.ForCurrentUser().Root)) throw new ArgumentException("Use LOCALAPPDATA/SheetsWindows for login.");
    var preparation = storage.CreatePreparation(); var locks = new FileOperationLock(storage.LocksPath);
    using var handler = new HttpClientHandler { AllowAutoRedirect = false }; using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    var auth = new GoogleOAuth(http, client, new DpapiTokenVault(Path.Combine(storage.Root, "auth"), client.Id),
        new LoopbackAuthorizationReceiver(uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })), locks);
    if (args[0] == "login") { await auth.ConnectAsync(); Console.WriteLine("Google autorizado."); return 0; }
    var importer = new GoogleImport(preparation, new SqliteOperationRegistry(storage.DatabasePath), new GoogleRemoteRegistry(Path.Combine(storage.Root, "google.db")), new SourceReader(), locks, auth, new GoogleDriveClient(http, auth));
    var url = await importer.ImportAsync(args[2]); Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); Console.WriteLine(url.AbsoluteUri); return 0;
}
catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or System.Xml.XmlException or KeyNotFoundException)
{
    Console.Error.WriteLine(ex is AuthorizationRequiredException ? "Autentique com o comando login." : ex is ReconciliationRequiredException ? "Resultado pendente de reconciliação. O original permanece preservado." : "A operação não foi concluída. O arquivo original permanece preservado."); return 1;
}
