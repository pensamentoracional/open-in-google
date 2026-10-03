using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal sealed class LauncherForm : Form
{
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(382, 0), Text = "Abra uma planilha do computador e continue no Google Sheets.\n\nDepois de importar e conferir, o aplicativo guarda um backup do original e cria um atalho no lugar do arquivo." };
    public int ExitCode { get; private set; }
    public LauncherForm(LauncherRequest request, bool expanded = false)
    {
        if (request.Action != LauncherAction.Home) throw new ArgumentException("Home request required.");
        Text = "ZagoSheetsWin"; ClientSize = new Size(500, 380); MinimumSize = new Size(500, 380); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        Button Action(string text, EventHandler action) { var b = new Button { Text = text, Width = 382, Height = 38, AccessibleName = text.Replace("&", "") }; b.Click += action; return b; }
        void Pick(LauncherAction action)
        {
            using var dialog = new OpenFileDialog { Filter = "Planilhas|*.xlsx;*.xls;*.csv;*.tsv;*.ods", CheckFileExists = true, Multiselect = false, Title = action == LauncherAction.Copy ? "Importar cópia — conservar o original" : "Abrir planilha no Google Sheets" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            using var processing = new ProcessingForm(new LauncherRequest(action, dialog.FileName)); processing.ShowDialog(this); ExitCode = processing.ExitCode;
        }
        layout.Controls.Add(status);
        var open = Action("&Abrir planilha…", (_, _) => Pick(LauncherAction.Open)); Ui.Primary(open); layout.Controls.Add(open); layout.Controls.Add(Ui.Separator());
        var navigation = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Height = 48, Width = 440, Margin = new Padding(0, 4, 0, 12) };
        for (var i = 0; i < 3; i++) navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        var settings = Action("&Configurações", (_, _) => { using var setup = new SetupForm(); setup.ShowDialog(this); });
        var backups = Action("&Backups", (_, _) => { using var recovery = new RecoveryForm(); recovery.ShowDialog(this); });
        var help = Action("&Ajuda — como funciona", (_, _) => { using var tutorial = new TutorialForm(); tutorial.ShowDialog(this); });
        foreach (var button in new[] { settings, backups, help }) { button.Dock = DockStyle.Fill; button.Font = new Font("Segoe UI", 9); navigation.Controls.Add(button); }
        layout.Controls.Add(navigation);
        var advanced = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        var toggle = new Button { Text = "Opções avançadas ▸", AutoSize = true, AccessibleName = "Mostrar opções avançadas" };
        toggle.Click += (_, _) => { advanced.Visible = !advanced.Visible; toggle.Text = advanced.Visible ? "Opções avançadas ▾" : "Opções avançadas ▸"; toggle.AccessibleName = advanced.Visible ? "Ocultar opções avançadas" : "Mostrar opções avançadas"; ClientSize = new Size(ClientSize.Width, (int)((advanced.Visible ? 530 : 380) * DeviceDpi / 96.0)); };
        advanced.Controls.Add(Ui.Text("Quer manter o arquivo original no lugar? Importe como cópia."));
        advanced.Controls.Add(Action("Importar como cópia…", (_, _) => Pick(LauncherAction.Copy)));
        advanced.Controls.Add(Action("Abrir pasta de atalhos das cópias", (_, _) =>
        {
            var path = Path.Combine(LocalStorage.ForCurrentUser().Root, "shortcuts");
            try { if (Directory.Exists(path)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); else status.Text = "Nenhum atalho de cópia foi criado ainda."; }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { status.Text = "Não foi possível abrir a pasta de atalhos. Confira as permissões deste usuário do Windows."; }
        }));
        layout.Controls.Add(toggle); layout.Controls.Add(advanced); Controls.Add(layout); Branding.Apply(this, compact: true); Ui.Adapt(layout); Ui.Adapt(advanced);
        if (expanded) { advanced.Visible = true; toggle.Text = "Opções avançadas ▾"; toggle.AccessibleName = "Ocultar opções avançadas"; ClientSize = new Size(500, 530); }
    }
}
