using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class RecoveryForm : Form
{
    private bool busy;
    private CancellationTokenSource? activeCancellation;
    private readonly ListBox entries = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12), Text = "Restaure o arquivo original inicial em um novo arquivo. A restauração funciona offline, verifica o backup e nunca sobrescreve arquivos existentes. O Sheets e o atalho permanecem disponíveis." };
    public RecoveryForm()
    {
        Text = "Recuperar operação e backups — ZagoSheetsWin"; ClientSize = new Size(900, 470); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var restore = new Button { Text = "Restaurar em…", Dock = DockStyle.Bottom, Height = 44 };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(8) };
        var copy = new Button { Text = "Retomar como cópia", AutoSize = true };
        var resume = new Button { Text = "Concluir substituição", AutoSize = true };
        var export = new Button { Text = "Exportar diagnóstico…", AutoSize = true };
        var cancel = new Button { Text = "Cancelar retomada", AutoSize = true, Enabled = false };
        cancel.Click += (_, _) => activeCancellation?.Cancel();
        Controls.Add(cancel); cancel.Dock = DockStyle.Bottom;
        actions.Controls.AddRange([copy, resume, export]);
        Controls.Add(entries); Controls.Add(status); Controls.Add(actions); Controls.Add(restore);
        async Task Resume(bool replace)
        {
            if (busy || entries.SelectedItem is not RecoveryEntry entry) return;
            busy = true; actions.Enabled = false; restore.Enabled = false; entries.Enabled = false;
            using var cancellation = new CancellationTokenSource(); activeCancellation = cancellation; cancel.Enabled = true;
            var diagnostics = new DiagnosticLog(LocalStorage.ForCurrentUser());
            await diagnostics.RecordAsync(DiagnosticEvent.Started, entry.Id);
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
                var launcher = new WindowsLauncher(LocalStorage.ForCurrentUser(), http, new BrowserLauncher());
                await Task.Run(() => launcher.ResumeAsync(entry.Id, replace, ct: cancellation.Token));
                await diagnostics.RecordAsync(DiagnosticEvent.Completed, entry.Id);
                status.Text = replace ? "Substituição concluída. Backup privado conservado." : "Cópia aberta no Google. Original conservado.";
            }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { await diagnostics.RecordAsync(DiagnosticLog.Failure(ex), entry.Id); status.Text = LauncherErrors.Message(ex); }
            finally { activeCancellation = null; cancel.Enabled = false; busy = false; actions.Enabled = true; restore.Enabled = true; entries.Enabled = true; }
        }
        copy.Click += async (_, _) => await Resume(false);
        resume.Click += async (_, _) => await Resume(true);
        export.Click += async (_, _) =>
        {
            if (busy) return;
            using var dialog = new SaveFileDialog { Filter = "Diagnóstico JSONL|*.jsonl", FileName = "sheets-windows-diagnostico.jsonl", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { await new DiagnosticLog(LocalStorage.ForCurrentUser()).ExportAsync(dialog.FileName); status.Text = "Diagnóstico exportado: apenas eventos, horários e IDs de operação."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Escolha um arquivo inexistente para exportar o diagnóstico."; }
        };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; activeCancellation?.Cancel(); } };
        var service = new BackupRecovery(LocalStorage.ForCurrentUser());
        Shown += (_, _) =>
        {
            try { foreach (var entry in service.List()) entries.Items.Add(entry); if (entries.Items.Count == 0) status.Text = "Nenhum backup registrado neste usuário do Windows."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = LauncherErrors.Message(ex); }
        };
        restore.Click += async (_, _) =>
        {
            if (entries.SelectedItem is not RecoveryEntry entry) { status.Text = "Selecione um backup na lista."; return; }
            using var dialog = new SaveFileDialog { Filter = "Arquivo original|*" + Path.GetExtension(entry.OriginalPath), DefaultExt = Path.GetExtension(entry.OriginalPath), FileName = Path.GetFileNameWithoutExtension(entry.OriginalPath) + "-restaurado" + Path.GetExtension(entry.OriginalPath), OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            busy = true; restore.Enabled = false; entries.Enabled = false; actions.Enabled = false;
            try { await Task.Run(() => service.RestoreAsync(entry.Id, dialog.FileName)); status.Text = "Backup verificado e restaurado. O backup privado foi conservado."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Não foi possível restaurar. Escolha um nome inexistente e confira a pasta de destino. O backup foi conservado."; }
            finally { busy = false; restore.Enabled = true; entries.Enabled = true; actions.Enabled = true; }
        };
        Branding.Apply(this);
    }
}
