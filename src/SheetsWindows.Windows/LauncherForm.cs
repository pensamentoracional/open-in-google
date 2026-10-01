using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class LauncherForm : Form
{
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(18), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button cancel = new() { Text = "Fechar", AutoSize = true };
    private readonly Button login = new() { Text = "Autorizar Google", AutoSize = true };
    private readonly Button defaults = new() { Text = "Escolher aplicativo padrão", AutoSize = true };
    private readonly CancellationTokenSource cancellation = new();
    private readonly LauncherRequest request;
    private bool busy;
    public int ExitCode { get; private set; }
    public LauncherForm(LauncherRequest request)
    {
        this.request = request; Text = "Sheets Windows"; ClientSize = new Size(560, 185); MinimumSize = new Size(500, 220);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange([login, defaults, cancel]); Controls.Add(status); Controls.Add(buttons);
        status.Text = "Abra uma planilha XLSX local pelo Explorer. Após a configuração inicial, ela será convertida para Sheets e substituída por atalho, com backup privado.";
        cancel.Click += (_, _) => { if (busy) { cancellation.Cancel(); cancel.Enabled = false; status.Text = "Interrompendo com segurança…"; } else Close(); };
        login.Click += async (_, _) => await RunAsync(LauncherAction.Login);
        defaults.Click += (_, _) => OpenDefaults();
        Shown += async (_, _) =>
        {
            if (request.Action == LauncherAction.Defaults) { if (OpenDefaults()) Close(); }
            else if (request.Action != LauncherAction.Home) await RunAsync(request.Action);
        };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; cancellation.Cancel(); cancel.Enabled = false; } };
    }
    private bool OpenDefaults()
    {
        try { new BrowserLauncher().Open(WindowsAssociationPlan.DefaultsUri); return true; }
        catch (Exception ex) when (LauncherErrors.Expected(ex)) { ExitCode = 1; status.Text = "Abra Configurações > Aplicativos > Aplicativos padrão e procure Sheets Windows."; return false; }
    }
    private async Task RunAsync(LauncherAction action)
    {
        ExitCode = 0; busy = true; login.Enabled = false; defaults.Enabled = false; cancel.Text = "Cancelar";
        status.Text = action == LauncherAction.Login ? "Aguardando autorização no navegador…" : "Preparando sua planilha…";
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
            var service = new WindowsLauncher(LocalStorage.ForCurrentUser(), http, new BrowserLauncher());
            var progress = new Progress<string>(text => { if (!IsDisposed) status.Text = text; });
            if (action == LauncherAction.Login) { await Task.Run(() => service.LoginAsync(cancellation.Token)); status.Text = "Google autorizado. Você pode abrir sua planilha pelo Explorer."; }
            else { await Task.Run(() => service.OpenAsync(request.Path!, progress, cancellation.Token)); busy = false; Close(); }
        }
        catch (Exception ex) when (LauncherErrors.Expected(ex))
        {
            ExitCode = 1;
            status.Text = ex is OperationCanceledException && !cancellation.IsCancellationRequested ? "A conexão demorou demais. Confira a internet e abra a planilha novamente; backups já criados foram conservados." : LauncherErrors.Message(ex);
            if (ex is OperationCanceledException && cancellation.IsCancellationRequested) { busy = false; Close(); }
        }
        finally { busy = false; if (!IsDisposed) { login.Enabled = true; defaults.Enabled = true; cancel.Enabled = true; cancel.Text = "Fechar"; } }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) cancellation.Dispose(); base.Dispose(disposing);
    }
}
