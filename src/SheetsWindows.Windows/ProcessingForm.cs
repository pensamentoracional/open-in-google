using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class ProcessingForm : Form
{
    private readonly Label status = new() { Dock = DockStyle.Fill, Padding = new Padding(18), TextAlign = ContentAlignment.MiddleLeft, Text = "Preparando sua planilha…", AccessibleName = "Estado do processamento" };
    private readonly Button cancel = new() { AutoSize = true, Text = "Cancelar" };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), WrapContents = true };
    private readonly CancellationTokenSource cancellation = new();
    private readonly LauncherRequest request;
    private readonly Func<IProgress<string>, CancellationToken, Task>? execute;
    private readonly bool recordDiagnostics;
    private readonly long? startedAt;
    private bool busy;
    public int ExitCode { get; private set; }
    internal bool IsBusy => busy;
    internal ProcessingMetrics? LastMetrics { get; private set; }
    public ProcessingForm(LauncherRequest request, long? startedAt = null, bool preview = false, bool previewError = false,
        Func<IProgress<string>, CancellationToken, Task>? execute = null, bool recordDiagnostics = true)
    {
        if (request.Action is not (LauncherAction.Open or LauncherAction.Copy or LauncherAction.Login)) throw new ArgumentException("Processing request required.");
        this.request = request; this.execute = execute; this.recordDiagnostics = recordDiagnostics; this.startedAt = startedAt ?? System.Diagnostics.Stopwatch.GetTimestamp();
        Text = "Abrindo no Google Sheets — ZagoSheetsWin"; ClientSize = new Size(620, 240); MinimumSize = new Size(620, 240);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        cancel.Click += (_, _) => { if (busy) { cancellation.Cancel(); cancel.Enabled = false; status.Text = "Interrompendo com segurança…"; } else Close(); };
        actions.Controls.Add(cancel); Controls.Add(status); Controls.Add(actions);
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; cancellation.Cancel(); cancel.Enabled = false; status.Text = "Interrompendo com segurança…"; } };
        Branding.Apply(this, aboutButton: false, compact: true);
        if (preview) { status.Text = "Conferindo os dados convertidos antes da substituição…"; if (previewError) ShowFailure(new ConversionMismatchException()); }
        else Shown += async (_, _) => await RunAsync();
    }
    private void ShowFailure(Exception ex)
    {
        Text = "Importação interrompida — ZagoSheetsWin";
        status.Text = ex is OperationCanceledException && !cancellation.IsCancellationRequested ? "A conexão demorou demais. Confira a internet e retome pela recuperação; backups já criados foram conservados." : LauncherErrors.Message(ex);
        var recovery = new Button { Text = "Recuperação / backups", AutoSize = true };
        recovery.Click += (_, _) => { using var form = new RecoveryForm(); form.ShowDialog(this); };
        var export = new Button { Text = "Exportar diagnóstico…", AutoSize = true };
        export.Click += async (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "Diagnóstico JSONL|*.jsonl", FileName = "zagosheetswin-diagnostico.jsonl", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { await new DiagnosticLog(LocalStorage.ForCurrentUser()).ExportAsync(dialog.FileName); status.Text = "Diagnóstico exportado: eventos, horários, IDs e medidas numéricas; sem arquivos, contas ou conteúdo."; }
            catch (Exception failure) when (LauncherErrors.Expected(failure)) { status.Text = "Escolha um arquivo inexistente e confira as permissões para exportar."; }
        };
        var setup = new Button { Text = "Configurações", AutoSize = true };
        setup.Click += (_, _) => { using var form = new SetupForm(); form.ShowDialog(this); };
        actions.Controls.AddRange([recovery, export, setup]); cancel.Text = "Fechar"; cancel.Enabled = true;
        ClientSize = new Size((int)(620 * DeviceDpi / 96.0), (int)(330 * DeviceDpi / 96.0)); Branding.Refresh(this);
    }
    private async Task RunAsync()
    {
        busy = true;
        var progress = new Progress<string>(text => { if (!IsDisposed && !Disposing && busy && !cancellation.IsCancellationRequested) status.Text = text; });
        var telemetry = new ProcessingTelemetry(startedAt, progress); telemetry.MarkReady(); telemetry.SampleCpu();
        using var cpuSampler = new System.Windows.Forms.Timer { Interval = 100 };
        cpuSampler.Tick += (_, _) => telemetry.SampleCpu(); cpuSampler.Start();
        var diagnostics = new DiagnosticLog(LocalStorage.ForCurrentUser());
        var outcome = DiagnosticEvent.Completed;
        if (recordDiagnostics) await diagnostics.RecordAsync(DiagnosticEvent.Started);
        try
        {
            status.Text = request.Action == LauncherAction.Login ? "Aguardando autorização no navegador…" : "Conferindo arquivo e backup…";
            if (execute is not null) await execute(progress, cancellation.Token);
            else
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
                var launcher = new WindowsLauncher(LocalStorage.ForCurrentUser(), http, new BrowserLauncher(), telemetry);
                await Task.Run(async () =>
                {
                    if (request.Action == LauncherAction.Login) await launcher.LoginAsync(cancellation.Token);
                    else if (request.Action == LauncherAction.Copy) await launcher.CopyAsync(request.Path!, progress, cancellation.Token);
                    else await launcher.OpenAsync(request.Path!, progress, cancellation.Token);
                });
            }
        }
        catch (Exception ex) when (LauncherErrors.Expected(ex))
        {
            ExitCode = 1; outcome = DiagnosticLog.Failure(ex);
            if (ex is not OperationCanceledException || !cancellation.IsCancellationRequested) ShowFailure(ex);
        }
        finally
        {
            cpuSampler.Stop();
            LastMetrics = telemetry.Capture();
            if (recordDiagnostics) await diagnostics.RecordAsync(outcome, metrics: LastMetrics);
            busy = false;
        }
        if (ExitCode == 0 || cancellation.IsCancellationRequested) Close();
    }
    protected override void Dispose(bool disposing) { if (disposing) cancellation.Dispose(); base.Dispose(disposing); }
}
