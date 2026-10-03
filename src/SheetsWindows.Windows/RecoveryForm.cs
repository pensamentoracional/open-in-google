using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class RecoveryForm : Form
{
    private bool busy;
    private CancellationTokenSource? activeCancellation;
    private readonly ListBox entries = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12), Text = "Restaure o arquivo original inicial em um novo arquivo. A restauração funciona offline, verifica o backup e nunca sobrescreve arquivos existentes. O Sheets e o atalho permanecem disponíveis." };
    public RecoveryForm(bool preview = false, bool previewBusy = false)
    {
        Text = "Recuperar operação e backups — ZagoSheetsWin"; ClientSize = new Size(1000, 650); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var storage = LocalStorage.ForCurrentUser();
        var manager = new BackupManagement(storage);
        var service = new BackupRecovery(storage);
        var policyPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 105, Padding = new Padding(8) };
        var days = new NumericUpDown { Minimum = 1, Maximum = 365, Value = 30, Width = 70, AccessibleName = "Retenção em dias" };
        var quota = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 200, Width = 80, AccessibleName = "Teto em MB" };
        var automatic = new CheckBox { Text = "Limpeza automática de backups concluídos", AutoSize = true };
        var save = new Button { Text = "Salvar regras", AutoSize = true };
        var usage = new Label { AutoSize = true, Text = "Teto: 200 MB · máximo: 1 GB. Operações pendentes são protegidas." };
        policyPanel.Controls.AddRange([new Label { Text = "Dias:", AutoSize = true }, days, new Label { Text = "Teto (MB):", AutoSize = true }, quota, automatic, save, usage]);
        var restore = new Button { Text = "Restaurar em…", Dock = DockStyle.Bottom, Height = 44 };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(8) };
        var copy = new Button { Text = "Retomar como cópia", AutoSize = true };
        var resume = new Button { Text = "Concluir substituição", AutoSize = true };
        var export = new Button { Text = "Exportar diagnóstico…", AutoSize = true };
        var cancel = new Button { Text = "Cancelar retomada", AutoSize = true, Enabled = previewBusy, Visible = previewBusy };
        cancel.Click += (_, _) => activeCancellation?.Cancel();
        var delete = new Button { Text = "Apagar backup selecionado…", AutoSize = true };
        var clean = new Button { Text = "Limpar vencidos / excesso…", AutoSize = true };
        actions.Controls.AddRange([copy, resume, export, delete, clean]);
        Controls.Add(entries); Controls.Add(policyPanel); Controls.Add(status); Controls.Add(actions); Controls.Add(restore); Controls.Add(cancel); cancel.Dock = DockStyle.Bottom;
        async Task Resume(bool replace)
        {
            if (busy || entries.SelectedItem is not RecoveryEntry { Available: true } entry) return;
            busy = true; actions.Enabled = false; restore.Enabled = false; entries.Enabled = false;
            using var cancellation = new CancellationTokenSource(); activeCancellation = cancellation; cancel.Visible = true; cancel.Enabled = true;
            var diagnostics = new DiagnosticLog(LocalStorage.ForCurrentUser());
            await diagnostics.RecordAsync(DiagnosticEvent.Started, entry.Id);
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
                var launcher = new WindowsLauncher(LocalStorage.ForCurrentUser(), http, new BrowserLauncher());
                await Task.Run(() => launcher.ResumeAsync(entry.Id, replace, ct: cancellation.Token));
                await diagnostics.RecordAsync(DiagnosticEvent.Completed, entry.Id);
                await RefreshEntries();
                status.Text = replace ? "Substituição concluída. Backup privado conservado." : "Cópia aberta no Google. Original conservado.";
            }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { await diagnostics.RecordAsync(DiagnosticLog.Failure(ex), entry.Id); status.Text = LauncherErrors.Message(ex); }
            finally { activeCancellation = null; cancel.Enabled = false; cancel.Visible = false; busy = false; actions.Enabled = true; restore.Enabled = true; entries.Enabled = true; }
        }
        copy.Click += async (_, _) => await Resume(false);
        resume.Click += async (_, _) => await Resume(true);
        export.Click += async (_, _) =>
        {
            if (busy) return;
            using var dialog = new SaveFileDialog { Filter = "Diagnóstico JSONL|*.jsonl", FileName = "sheets-windows-diagnostico.jsonl", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { await new DiagnosticLog(LocalStorage.ForCurrentUser()).ExportAsync(dialog.FileName); status.Text = "Diagnóstico exportado: eventos, horários, IDs e medidas numéricas, sem arquivos, contas ou conteúdo."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Escolha um arquivo inexistente para exportar o diagnóstico."; }
        };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; activeCancellation?.Cancel(); } };
        async Task RefreshEntries()
        {
            var result = await Task.Run(() => (Rows: service.List(), Summary: manager.Inspect()));
            entries.Items.Clear(); foreach (var row in result.Rows) entries.Items.Add(row);
            usage.Text = $"Uso: {result.Summary.UsedBytes / 1_000_000d:N2} MB · teto: {quota.Value} MB · {result.Summary.Entries.Count} registros · {result.Summary.Entries.Count(e => !e.CanClean && e.Available)} protegidos";
            if (entries.Items.Count == 0) status.Text = "Nenhum backup registrado neste usuário do Windows.";
        }
        if (!preview) Shown += async (_, _) =>
        {
            busy = true;
            try { var policy = BackupPolicy.Load(storage); days.Value = policy.RetentionDays; quota.Value = policy.QuotaMb; automatic.Checked = policy.AutomaticCleanup; await RefreshEntries(); }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = LauncherErrors.Message(ex); }
            finally { busy = false; }
        };
        save.Click += async (_, _) =>
        {
            if (busy || preview) return;
            if (automatic.Checked && MessageBox.Show(this, "A limpeza automática aplica estas regras também aos backups já existentes. Apaga backups concluídos após o prazo ou para liberar espaço, dos mais antigos para os recentes. Depois não será possível restaurar o original local. Planilhas no Google e atalhos permanecem. Ativar?", "Ativar limpeza automática", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            busy = true;
            try { await BackupPolicy.SaveAsync(storage, new((int)days.Value, (int)quota.Value, automatic.Checked)); status.Text = "Regras salvas. A limpeza automática ocorre nas próximas importações; não há serviço residente."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = LauncherErrors.Message(ex); }
            finally { busy = false; }
        };
        async Task DeleteBackups(bool selected)
        {
            if (busy || preview) return;
            busy = true; actions.Enabled = false; restore.Enabled = false; policyPanel.Enabled = false; entries.Enabled = false;
            try
            {
                var ids = selected ? entries.SelectedItem is RecoveryEntry { CanClean: true } entry ? new[] { entry.Id } : Array.Empty<Guid>() : (await Task.Run(() => manager.Plan())).ToArray();
                if (ids.Length == 0) { status.Text = "Nenhum backup concluído elegível. Operações pendentes ou ambíguas são protegidas."; return; }
                if (MessageBox.Show(this, $"Apagar {ids.Length} backup(s) local(is)? Não será possível restaurar os originais após a limpeza. As planilhas no Google, os atalhos e o histórico permanecem.", "Apagar backups", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                var result = await Task.Run(() => manager.CleanAsync(ids)); await RefreshEntries(); status.Text = $"{result.Count} backup(s) apagado(s), {result.Bytes / 1_000_000d:N2} MB liberados.";
            }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = LauncherErrors.Message(ex); }
            finally { busy = false; actions.Enabled = true; restore.Enabled = true; policyPanel.Enabled = true; entries.Enabled = true; }
        }
        delete.Click += async (_, _) => await DeleteBackups(true);
        clean.Click += async (_, _) => await DeleteBackups(false);
        entries.SelectedIndexChanged += (_, _) => { var row = entries.SelectedItem as RecoveryEntry; restore.Enabled = row?.Available == true; copy.Enabled = row?.Available == true; resume.Enabled = row?.Available == true; delete.Enabled = row?.CanClean == true; };
        restore.Click += async (_, _) =>
        {
            if (busy || entries.SelectedItem is not RecoveryEntry { Available: true } entry) { status.Text = "Selecione um backup na lista."; return; }
            using var dialog = new SaveFileDialog { Filter = "Arquivo original|*" + Path.GetExtension(entry.OriginalPath), DefaultExt = Path.GetExtension(entry.OriginalPath), FileName = Path.GetFileNameWithoutExtension(entry.OriginalPath) + "-restaurado" + Path.GetExtension(entry.OriginalPath), OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            busy = true; restore.Enabled = false; entries.Enabled = false; actions.Enabled = false;
            try { await Task.Run(() => service.RestoreAsync(entry.Id, dialog.FileName)); status.Text = "Backup verificado e restaurado. O backup privado foi conservado."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Não foi possível restaurar. Escolha um nome inexistente e confira a pasta de destino. O backup foi conservado."; }
            finally { busy = false; restore.Enabled = true; entries.Enabled = true; actions.Enabled = true; }
        };
        if (preview)
        {
            entries.Items.AddRange(new object[] {
                new RecoveryEntry(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Relatório.xlsx", 2_400_000, 4, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), true, true),
                new RecoveryEntry(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Importação pendente.csv", 600_000, 1, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)),
                new RecoveryEntry(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Arquivo antigo.xls", 100_000, 4, new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero), false, false, 2) });
            usage.Text = "Uso: 3,00 MB · teto: 200 MB · 1 protegido · 1 limpo";
        }
        Branding.Apply(this);
    }
}
