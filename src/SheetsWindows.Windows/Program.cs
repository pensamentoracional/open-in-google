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
            if (request.Action == LauncherAction.Version) { Console.WriteLine("Sheets Windows pilot 0.5"); return 0; }
            ApplicationConfiguration.Initialize();
            using var form = new LauncherForm(request); Application.Run(form); return form.ExitCode;
        }
        catch (Exception ex) when (LauncherErrors.Expected(ex))
        {
            MessageBox.Show("Use o Sheets Windows para abrir um arquivo XLSX local. " + LauncherErrors.Message(ex), "Sheets Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1;
        }
    }
}
