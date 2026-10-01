using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var request = LauncherRequest.Parse(args);
            if (request.Action == LauncherAction.Version) { Console.WriteLine("Sheets Windows pilot 0.6"); return 0; }
            if (request.Action is LauncherAction.Register or LauncherAction.Unregister)
            {
                var held = new FileOperationLock(LocalStorage.ForCurrentUser().LocksPath).AcquireAsync("windows-registration").AsTask().GetAwaiter().GetResult();
                try
                {
                    var registration = new WindowsAssociationRegistration(Microsoft.Win32.Registry.CurrentUser);
                    if (request.Action == LauncherAction.Register) registration.Register(Environment.ProcessPath!);
                    else registration.Unregister();
                    WindowsAssociationRegistration.NotifyShell(); return 0;
                }
                finally { held.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
            ApplicationConfiguration.Initialize();
            if (request.Action == LauncherAction.Setup) { using var setup = new SetupForm(); Application.Run(setup); return setup.ExitCode; }
            if (request.Action == LauncherAction.Recovery) { using var recovery = new RecoveryForm(); Application.Run(recovery); return 0; }
            using var form = new LauncherForm(request); Application.Run(form); return form.ExitCode;
        }
        catch (Exception ex) when (LauncherErrors.Expected(ex))
        {
            if (args.Length == 1 && args[0] is "--register" or "--unregister") { Console.Error.WriteLine("Association maintenance failed; existing state preserved."); return 1; }
            MessageBox.Show("Use o Sheets Windows para abrir um arquivo XLSX local. " + LauncherErrors.Message(ex), "Sheets Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1;
        }
    }
}
