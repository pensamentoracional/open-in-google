using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--preview-branding")
            {
                ApplicationConfiguration.Initialize();
                var folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
                using var home = new LauncherForm(new LauncherRequest(LauncherAction.Home));
                using var setupPreview = new SetupForm(); using var recoveryPreview = new RecoveryForm(preview: true); using var aboutPreview = new AboutForm();
                foreach (var entry in new[] { ("home", (Form)home), ("setup", (Form)setupPreview), ("recovery", (Form)recoveryPreview), ("about", (Form)aboutPreview) })
                {
                    entry.Item2.Show(); Application.DoEvents(); entry.Item2.PerformLayout();
                    using var bitmap = new Bitmap(entry.Item2.Width, entry.Item2.Height);
                    entry.Item2.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(folder, entry.Item1 + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    entry.Item2.Hide();
                }
                return 0;
            }
            var request = LauncherRequest.Parse(args);
            if (request.Action == LauncherAction.Version) { Console.WriteLine("ZagoSheetsWin pilot 0.9.1"); return 0; }
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
            MessageBox.Show("Use o ZagoSheetsWin para abrir uma planilha suportada. " + LauncherErrors.Message(ex), "ZagoSheetsWin", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1;
        }
    }
}
