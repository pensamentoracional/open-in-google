using SheetsWindows.Infrastructure;
using System.Drawing.Drawing2D;

namespace SheetsWindows.Windows;

// Offline illustrations: no web content, tracking or Google credentials.
internal sealed class TutorialForm : Form
{
    private readonly CheckBox hide = new() { Text = "Não mostrar novamente", AutoSize = true, Checked = false };
    private readonly Label heading = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 17, FontStyle.Bold) };
    private readonly Label explanation = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly TutorialPicture picture = new() { Dock = DockStyle.Fill };
    private readonly Button previous = new() { Text = "Voltar", AutoSize = true };
    private readonly Button next = new() { Text = "Próximo", AutoSize = true };
    private readonly Label count = new() { AutoSize = true };
    private int page;
    private bool saving;
    private static readonly (string Title, string Text)[] Pages =
    [
        ("Suas planilhas no Google Sheets", "Abra uma planilha: o aplicativo importa, confere o resultado e cria um atalho no mesmo lugar. O original só é retirado após as verificações e com backup. Se a conversão não puder ser conferida, ele é preservado."),
        ("Conecte sua conta Google", "No primeiro uso, escolha sua pasta local e confirme a substituição com backup. Depois clique em Salvar e conectar Google. No navegador, escolha sua conta e autorize o acesso. Volte ao aplicativo para continuar. Suas planilhas ficam no seu Drive."),
        ("Escolha como abrir os arquivos", "Para usar dois cliques, abra Aplicativos padrão do Windows, procure ZagoSheetsWin e associe CSV, XLS e XLSX. TSV é opcional; ODS é experimental. A associação é opcional: você pode usar botão direito → Abrir com → ZagoSheetsWin, ou Abrir planilha no aplicativo."),
        ("O original tem um backup", "Backups permite restaurar o arquivo inicial completo. O padrão é 30 dias e 200 MB, com teto configurável até 1 GB. A limpeza automática exige sua ativação; arquivos protegidos para recuperação não são apagados por ela. O backup não inclui edições futuras no Sheets. Macros XLS não funcionam no Sheets; fórmulas e formatação também podem mudar.")
    ];
    public TutorialForm(int initialPage = 0)
    {
        page = Math.Clamp(initialPage, 0, Pages.Length - 1);
        Text = "Como funciona — ZagoSheetsWin"; ClientSize = new Size(620, 570); MinimumSize = new Size(620, 570); StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(picture, 0, 1); layout.Controls.Add(explanation, 0, 2); layout.Controls.Add(hide, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var skip = new Button { Text = "Pular", AutoSize = true };
        actions.Controls.Add(skip); actions.Controls.Add(previous); actions.Controls.Add(next); actions.Controls.Add(count); layout.Controls.Add(actions, 0, 4);
        previous.Click += (_, _) => { page--; RefreshPage(); }; next.Click += (_, _) => { if (page == Pages.Length - 1) Close(); else { page++; RefreshPage(); } }; skip.Click += (_, _) => Close();
        Controls.Add(layout); Branding.Apply(this); RefreshPage();
        FormClosing += async (_, e) =>
        {
            if (saving) { e.Cancel = true; return; }
            if (!hide.Checked) return;
            e.Cancel = true; saving = true; layout.Enabled = false;
            try { await TutorialSettings.SaveAsync(LocalStorage.ForCurrentUser(), true); hide.Checked = false; saving = false; Close(); }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { saving = false; layout.Enabled = true; MessageBox.Show(this, LauncherErrors.Message(ex), "Preferência do tutorial", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
    }
    private void RefreshPage()
    {
        heading.Text = Pages[page].Title; explanation.Text = Pages[page].Text; count.Text = $"{page + 1} / {Pages.Length}";
        previous.Enabled = page > 0; next.Text = page == Pages.Length - 1 ? "Começar" : "Próximo"; picture.Page = page; picture.AccessibleName = Pages[page].Title; picture.Invalidate();
    }
}
internal sealed class TutorialPicture : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Page { get; set; }
    public TutorialPicture() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Min(Width / 560f, Height / 150f); g.TranslateTransform((Width - 560 * scale) / 2, (Height - 150 * scale) / 2); g.ScaleTransform(scale, scale);
        using var pen = new Pen(ForeColor, 2); using var green = new SolidBrush(Color.FromArgb(25, 134, 74)); using var font = new Font("Segoe UI", 12, FontStyle.Bold); using var ink = new SolidBrush(ForeColor);
        void Card(float x, string label, bool grid)
        {
            g.DrawRectangle(pen, x, 15, 135, 100); if (grid) { for (int n = 1; n < 4; n++) { g.DrawLine(pen, x + 8, 30 + n * 17, x + 127, 30 + n * 17); g.DrawLine(pen, x + n * 32, 40, x + n * 32, 105); } }
            else { g.FillEllipse(green, x + 48, 32, 38, 38); g.DrawLine(pen, x + 34, 88, x + 100, 88); }
            g.DrawString(label, font, ink, x, 120);
        }
        string[] labels = Page switch { 1 => ["Aplicativo", "Autorizar", "Seu Drive"], 2 => ["CSV / XLS / XLSX", "Abrir com", "Sheets"], 3 => ["Original", "Backup local", "Restaurar"], _ => ["Planilha local", "Conferência", "Atalho → Sheets"] };
        Card(5, labels[0], Page != 1); Card(205, labels[1], Page == 3); Card(405, labels[2], Page == 1 || Page == 2);
        foreach (var x in new[] { 155, 355 }) { g.DrawLine(pen, x, 65, x + 35, 65); g.DrawLine(pen, x + 35, 65, x + 25, 55); g.DrawLine(pen, x + 35, 65, x + 25, 75); }
    }
}
