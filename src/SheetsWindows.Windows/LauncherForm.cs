using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class LauncherForm : Form
{
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(540, 0), Text = "Abra uma planilha para importar para o Google Sheets. O original é substituído por um atalho após as verificações, com backup." };
    public int ExitCode { get; private set; }
    public LauncherForm(LauncherRequest request)
    {
        if (request.Action != LauncherAction.Home) throw new ArgumentException("Home request required.");
        Text = "ZagoSheetsWin"; ClientSize = new Size(620, 345); MinimumSize = new Size(620, 345); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Button Action(string text, EventHandler action) { var b = new Button { Text = text, Width = 540, Height = 38, AccessibleName = text.Replace("&", "") }; b.Click += action; return b; }
        void Pick(LauncherAction action)
        {
            using var dialog = new OpenFileDialog { Filter = "Planilhas|*.xlsx;*.xls;*.csv;*.tsv;*.ods", CheckFileExists = true, Multiselect = false, Title = action == LauncherAction.Copy ? "Importar cópia — conservar o original" : "Abrir planilha no Google Sheets" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            using var processing = new ProcessingForm(new LauncherRequest(action, dialog.FileName)); processing.ShowDialog(this); ExitCode = processing.ExitCode;
        }
        layout.Controls.Add(status);
        layout.Controls.Add(Action("&Abrir planilha…", (_, _) => Pick(LauncherAction.Open)));
        layout.Controls.Add(Action("&Configurações", (_, _) => { using var setup = new SetupForm(); setup.ShowDialog(this); }));
        layout.Controls.Add(Action("&Backups", (_, _) => { using var recovery = new RecoveryForm(); recovery.ShowDialog(this); }));
        var advanced = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        var toggle = new Button { Text = "Avançado ▸", AutoSize = true, AccessibleName = "Mostrar opções avançadas" };
        toggle.Click += (_, _) => { advanced.Visible = !advanced.Visible; toggle.Text = advanced.Visible ? "Avançado ▾" : "Avançado ▸"; toggle.AccessibleName = advanced.Visible ? "Ocultar opções avançadas" : "Mostrar opções avançadas"; ClientSize = new Size(ClientSize.Width, (int)((advanced.Visible ? 490 : 345) * DeviceDpi / 96.0)); };
        advanced.Controls.Add(Action("Importar cópia…", (_, _) => Pick(LauncherAction.Copy)));
        advanced.Controls.Add(Action("Abrir pasta de atalhos de cópia", (_, _) =>
        {
            var path = Path.Combine(LocalStorage.ForCurrentUser().Root, "shortcuts");
            try { if (Directory.Exists(path)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); else status.Text = "Nenhum atalho de cópia foi criado ainda."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Não foi possível abrir a pasta de atalhos. Confira as permissões deste usuário do Windows."; }
        }));
        advanced.Controls.Add(Action("Atualizações", (_, _) => { try { new BrowserLauncher().Open(new Uri("https://github.com/zagozago/ZagoSheetsWin/actions/workflows/local-core.yml")); } catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Consulte o guia da versão no repositório para atualizar."; } }));
        layout.Controls.Add(toggle); layout.Controls.Add(advanced); Controls.Add(layout); Branding.Apply(this, compact: true);
    }
}
