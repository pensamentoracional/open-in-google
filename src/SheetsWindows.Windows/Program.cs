using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (args.Length == 2 && args[0] == "--preview-branding")
            {
                ApplicationConfiguration.Initialize();
                var folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
                Branding.PreviewTheme(ApplicationTheme.Light);
                using var home = new LauncherForm(new LauncherRequest(LauncherAction.Home)); using var homeAdvanced = new LauncherForm(new(LauncherAction.Home), expanded: true);
                using var processingPreview = new ProcessingForm(new(LauncherAction.Open, "preview.xlsx"), preview: true); using var failurePreview = new ProcessingForm(new(LauncherAction.Open, "preview.xlsx"), preview: true, previewError: true);
                using var setupPreview = new SetupForm(); using var advancedPreview = new SetupForm(expanded: true); using var firstUsePreview = new SetupForm(firstUse: true); using var recoveryPreview = new RecoveryForm(preview: true); using var recoveryBusyPreview = new RecoveryForm(preview: true, previewBusy: true); using var aboutPreview = new AboutForm();
                foreach (var entry in new[] { ("home", (Form)home), ("home-advanced", (Form)homeAdvanced), ("setup", (Form)setupPreview), ("first-use", (Form)firstUsePreview), ("setup-advanced", (Form)advancedPreview), ("recovery", (Form)recoveryPreview), ("recovery-busy", (Form)recoveryBusyPreview), ("about", (Form)aboutPreview), ("processing", (Form)processingPreview), ("processing-error", (Form)failurePreview) })
                {
                    entry.Item2.Show(); Application.DoEvents(); entry.Item2.PerformLayout();
                    using var bitmap = new Bitmap(entry.Item2.Width, entry.Item2.Height);
                    entry.Item2.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(folder, entry.Item1 + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    entry.Item2.Hide();
                }
                Branding.PreviewTheme(ApplicationTheme.Dark);
                foreach (var entry in new[] { ("home-dark", (Form)home), ("home-advanced-dark", (Form)homeAdvanced), ("setup-dark", (Form)setupPreview), ("processing-dark", (Form)processingPreview), ("processing-error-dark", (Form)failurePreview) })
                {
                    entry.Item2.Show(); Application.DoEvents(); using var bitmap = new Bitmap(entry.Item2.Width, entry.Item2.Height);
                    entry.Item2.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, entry.Item1 + ".png"), System.Drawing.Imaging.ImageFormat.Png); entry.Item2.Hide();
                }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--verify-interface") { ApplicationConfiguration.Initialize(); return InterfaceVerification.Run(args[1]); }
            var request = LauncherRequest.Parse(args);
            if (request.Action == LauncherAction.Version) { Console.WriteLine("ZagoSheetsWin pilot 0.9.5"); return 0; }
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
            if (request.Action is LauncherAction.FirstUse or LauncherAction.Home or LauncherAction.Open or LauncherAction.Copy)
            {
                var storage = LocalStorage.ForCurrentUser();
                if (FirstUseState.NeedsAuthorization(storage))
                {
                    using var firstUse = new SetupForm(firstUse: true); Application.Run(firstUse);
                    if (FirstUseState.NeedsAuthorization(storage)) return 1;
                    if (request.Action is LauncherAction.Home or LauncherAction.FirstUse) return 0;
                }
                else if (request.Action == LauncherAction.FirstUse) return 0;
            }
            if (request.Action == LauncherAction.Setup) { using var setup = new SetupForm(); Application.Run(setup); return setup.ExitCode; }
            if (request.Action == LauncherAction.Recovery) { using var recovery = new RecoveryForm(); Application.Run(recovery); return 0; }
            if (request.Action == LauncherAction.Defaults) { new BrowserLauncher().Open(WindowsAssociationPlan.DefaultsUri); return 0; }
            if (request.Action == LauncherAction.Home) { using var home = new LauncherForm(request); Application.Run(home); return home.ExitCode; }
            using var form = new ProcessingForm(request, startedAt); Application.Run(form); return form.ExitCode;
        }
        catch (Exception ex) when (LauncherErrors.Expected(ex))
        {
            if (args.Length == 2 && args[0] == "--verify-interface") { Console.Error.WriteLine(ex.Message); return 1; }
            if (args.Length == 1 && args[0] is "--register" or "--unregister") { Console.Error.WriteLine("Association maintenance failed; existing state preserved."); return 1; }
            MessageBox.Show("Use o ZagoSheetsWin para abrir uma planilha suportada. " + LauncherErrors.Message(ex), "ZagoSheetsWin", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1;
        }
    }
}
