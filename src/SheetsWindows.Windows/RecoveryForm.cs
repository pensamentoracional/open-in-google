using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class RecoveryForm : Form
{
    private bool busy;
    private readonly ListBox entries = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12), Text = "Restaure o XLSX original inicial em um novo arquivo. A restauração funciona offline, verifica o backup e nunca sobrescreve arquivos existentes. O Sheets e o atalho permanecem disponíveis." };
    public RecoveryForm()
    {
        Text = "Restaurar backups — Sheets Windows"; ClientSize = new Size(760, 360); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var restore = new Button { Text = "Restaurar em…", Dock = DockStyle.Bottom, Height = 44 };
        Controls.Add(entries); Controls.Add(status); Controls.Add(restore);
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        var service = new BackupRecovery(LocalStorage.ForCurrentUser());
        Shown += (_, _) =>
        {
            try { foreach (var entry in service.List()) entries.Items.Add(entry); if (entries.Items.Count == 0) status.Text = "Nenhum backup registrado neste usuário do Windows."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = LauncherErrors.Message(ex); }
        };
        restore.Click += async (_, _) =>
        {
            if (entries.SelectedItem is not RecoveryEntry entry) { status.Text = "Selecione um backup na lista."; return; }
            using var dialog = new SaveFileDialog { Filter = "Planilha Excel|*.xlsx", FileName = Path.GetFileNameWithoutExtension(entry.OriginalPath) + "-restaurado.xlsx", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            busy = true; restore.Enabled = false; entries.Enabled = false;
            try { await Task.Run(() => service.RestoreAsync(entry.Id, dialog.FileName)); status.Text = "Backup verificado e restaurado. O backup privado foi conservado."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Não foi possível restaurar. Escolha um nome inexistente e confira a pasta de destino. O backup foi conservado."; }
            finally { busy = false; restore.Enabled = true; entries.Enabled = true; }
        };
    }
}
