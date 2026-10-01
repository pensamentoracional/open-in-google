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
        this.request = request; Text = "Sheets Windows"; ClientSize = new Size(720, 300); MinimumSize = new Size(500, 220);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.LeftToRight };
        var setup = new Button { Text = "Configurar piloto", AutoSize = true };
        var recovery = new Button { Text = "Recuperar operação / backup", AutoSize = true };
        setup.Click += (_, _) => { if (!busy) { using var form = new SetupForm(); form.ShowDialog(this); } };
        recovery.Click += (_, _) => { if (!busy) { using var form = new RecoveryForm(); form.ShowDialog(this); } };
        var copy = new Button { Text = "Importar cópia…", AutoSize = true };
        copy.Click += async (_, _) =>
        {
            if (busy) return;
            using var dialog = new OpenFileDialog { Filter = "Planilhas|*.xlsx;*.ods;*.xls;*.csv;*.tsv", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await RunAsync(LauncherAction.Copy, dialog.FileName);
        };
        var shortcuts = new Button { Text = "Abrir pasta de atalhos", AutoSize = true };
        shortcuts.Click += (_, _) =>
        {
            if (busy) return;
            var path = Path.Combine(LocalStorage.ForCurrentUser().Root, "shortcuts");
            try
            {
                if (Directory.Exists(path)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                else status.Text = "Nenhum atalho de importação de cópia foi criado ainda.";
            }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Não foi possível abrir a pasta de atalhos. Confira as permissões deste usuário do Windows."; }
        };
        var updates = new Button { Text = "Atualizações", AutoSize = true };
        updates.Click += (_, _) => { if (!busy) { try { new BrowserLauncher().Open(new Uri("https://github.com/pensamentoracional/open-in-google/actions/workflows/local-core.yml")); status.Text = "Escolha um pacote de CI aprovado e confira seu hash no guia da versão. Feche o aplicativo e execute o instalador; os backups e a configuração serão preservados."; } catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Consulte o guia da versão no repositório para atualizar."; } } };
        buttons.Controls.AddRange([setup, login, defaults, recovery, copy, shortcuts, updates, cancel]); Controls.Add(status); Controls.Add(buttons);
        status.Text = "Abra uma planilha da pasta local configurada. CSV, TSV e ODS simples são conferidos antes da substituição. XLS e ODS complexos conservam o original. Para OneDrive ou rede, use Importar cópia; o atalho fica na pasta privada do aplicativo.";
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
    private async Task RunAsync(LauncherAction action, string? path = null)
    {
        ExitCode = 0; busy = true; login.Enabled = false; defaults.Enabled = false; cancel.Text = "Cancelar";
        status.Text = action == LauncherAction.Login ? "Aguardando autorização no navegador…" : "Preparando sua planilha…";
        var diagnostics = new DiagnosticLog(LocalStorage.ForCurrentUser());
        await diagnostics.RecordAsync(DiagnosticEvent.Started);
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
            var service = new WindowsLauncher(LocalStorage.ForCurrentUser(), http, new BrowserLauncher());
            var progress = new Progress<string>(text => { if (!IsDisposed) status.Text = text; });
            if (action == LauncherAction.Login) { await Task.Run(() => service.LoginAsync(cancellation.Token)); status.Text = "Google autorizado. Você pode abrir sua planilha pelo Explorer."; }
            else if (action == LauncherAction.Copy) { await Task.Run(() => service.CopyAsync(path ?? request.Path!, progress, cancellation.Token)); status.Text = "Cópia aberta no Sheets, original preservado. Use Abrir pasta de atalhos nas próximas aberturas."; }
            else { await Task.Run(() => service.OpenAsync(request.Path!, progress, cancellation.Token)); busy = false; Close(); }
        }
        catch (Exception ex) when (LauncherErrors.Expected(ex))
        {
            await diagnostics.RecordAsync(DiagnosticLog.Failure(ex));
            ExitCode = 1;
            status.Text = ex is OperationCanceledException && !cancellation.IsCancellationRequested ? "A conexão demorou demais. Confira a internet e abra a planilha novamente; backups já criados foram conservados." : LauncherErrors.Message(ex);
            if (ex is OperationCanceledException && cancellation.IsCancellationRequested) { busy = false; Close(); }
        }
        finally { if (ExitCode == 0) await diagnostics.RecordAsync(DiagnosticEvent.Completed); busy = false; if (!IsDisposed) { login.Enabled = true; defaults.Enabled = true; cancel.Enabled = true; cancel.Text = "Fechar"; } }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) cancellation.Dispose(); base.Dispose(disposing);
    }
}
