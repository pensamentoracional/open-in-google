using SheetsWindows.Infrastructure;
using System.Drawing.Drawing2D;

namespace SheetsWindows.Windows;

// Offline illustrations: no web content, tracking or Google credentials.
internal sealed class TutorialForm : Form
{
    private readonly CheckBox hide = new() { Text = "Não mostrar este tutorial ao abrir o aplicativo", AutoSize = true, Checked = false };
    private readonly Label heading = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
    private readonly FlowLayoutPanel explanation = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly TutorialPicture picture = new() { Dock = DockStyle.Fill };
    private readonly Button previous = new() { Text = "Voltar", AutoSize = true };
    private readonly Button next = new() { Text = "Próximo", AutoSize = true };
    private readonly Label count = new() { AutoSize = true };
    private int page;
    private bool saving;
    private static readonly (string Title, string Text)[] Pages =
    [
        ("Abra suas planilhas no Google Sheets", "A ideia é simples: você abre um arquivo de planilha, e o ZagoSheetsWin importa esse arquivo para o Google Sheets.\nDepois, você continua trabalhando pelo navegador.|No lugar do arquivo original, o aplicativo cria um atalho para a planilha no Google Sheets.\nO original só é retirado depois das verificações e da criação de um backup no computador.\nSe a conversão não puder ser conferida, o aplicativo mantém o original."),
        ("Conecte sua conta Google", "Você usa sua própria conta Google. As planilhas importadas ficam no Google Drive dessa conta.|Nas configurações, escolha a pasta local e confirme a substituição do original por um atalho, com backup.\nDepois, clique em Salvar e conectar Google.|No navegador, escolha sua conta e autorize o acesso.\nQuando terminar, volte ao ZagoSheetsWin para continuar."),
        ("Escolha como abrir suas planilhas", "Quer abrir uma planilha com dois cliques?\nNos Aplicativos padrão do Windows, escolha ZagoSheetsWin para CSV, XLS e XLSX.|Essa escolha é opcional.\nVocê também pode usar Abrir com → ZagoSheetsWin, no botão direito do arquivo.\nOu usar Abrir planilha dentro do aplicativo.|Também há suporte a TSV. O formato ODS ainda é experimental."),
        ("Guarde o original. Saiba como recuperar.", "O backup guarda uma cópia completa do arquivo original. Para recuperar, abra Backups.\nO backup não inclui as alterações feitas depois no Google Sheets.|A configuração padrão é de 30 dias e 200 MB. O limite pode ser ajustado até 1 GB.\nA limpeza automática só funciona quando ativada. Os arquivos protegidos para recuperação são mantidos.|Na conversão, fórmulas e formatação podem mudar. Macros de arquivos XLS não funcionam no Google Sheets.")
    ];
    public TutorialForm(int initialPage = 0)
    {
        page = Math.Clamp(initialPage, 0, Pages.Length - 1);
        Text = "Como funciona — ZagoSheetsWin"; ClientSize = new Size(560, 650); MinimumSize = new Size(480, 540); StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 135)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(picture, 0, 1); layout.Controls.Add(explanation, 0, 2); layout.Controls.Add(hide, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var skip = new Button { Text = "Pular tutorial", AutoSize = true };
        actions.Controls.Add(skip); actions.Controls.Add(previous); actions.Controls.Add(next); actions.Controls.Add(count); layout.Controls.Add(actions, 0, 4);
        previous.Click += (_, _) => { page--; RefreshPage(); }; next.Click += (_, _) => { if (page == Pages.Length - 1) Close(); else { page++; RefreshPage(); } }; skip.Click += (_, _) => Close();
        Ui.Primary(next); Controls.Add(layout); Branding.Apply(this); Ui.Adapt(explanation); RefreshPage();
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
        heading.Text = Pages[page].Title;
        var old = explanation.Controls.Cast<Control>().ToArray(); explanation.Controls.Clear(); foreach (var control in old) control.Dispose();
        var groups = Pages[page].Text.Split('|');
        for (var i = 0; i < groups.Length; i++) { if (i > 0) explanation.Controls.Add(Ui.Separator()); var paragraph = Ui.Text(groups[i]); paragraph.MaximumSize = new Size(Math.Max(120, explanation.ClientSize.Width - 25), 0); explanation.Controls.Add(paragraph); }
        explanation.PerformLayout(); Branding.Refresh(this);
        count.Text = $"{page + 1} de {Pages.Length}";
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
        using var pen = new Pen(ForeColor, 2); using var green = new SolidBrush(Color.FromArgb(25, 134, 74)); using var font = new Font("Segoe UI", 10, FontStyle.Bold); using var ink = new SolidBrush(ForeColor);
        void Card(float x, string label, bool grid)
        {
            g.DrawRectangle(pen, x + 30, 15, 75, 85); if (grid) { for (int n = 1; n < 4; n++) { g.DrawLine(pen, x + 38, 25 + n * 16, x + 97, 25 + n * 16); g.DrawLine(pen, x + 30 + n * 18, 35, x + 30 + n * 18, 89); } }
            else if (Page == 0 && x == 205) { g.FillEllipse(green, x + 45, 32, 44, 44); using var white = new Pen(Color.White, 4); g.DrawLines(white, [new PointF(x + 54, 53), new PointF(x + 64, 63), new PointF(x + 80, 43)]); }
            else if (Page == 3) { g.FillRectangle(green, x + 40, 42, 55, 38); g.FillRectangle(green, x + 40, 35, 25, 12); }
            else { g.FillEllipse(green, x + 56, 30, 22, 22); g.DrawArc(pen, x + 45, 55, 44, 28, 180, 180); }
            g.DrawString(label, font, ink, new RectangleF(x - 8, 108, 160, 42), new StringFormat { Alignment = StringAlignment.Center });
        }
        string[] labels = Page switch { 1 => ["Aplicativo", "Autorizar", "Seu Drive"], 2 => ["CSV / XLS / XLSX", "Abrir com", "Sheets"], 3 => ["Original", "Backup local", "Restaurar"], _ => ["Arquivo local", "Conferir importação", "Atalho para o Sheets"] };
        Card(5, labels[0], Page != 1); Card(205, labels[1], Page == 3); Card(405, labels[2], Page == 1 || Page == 2);
        foreach (var x in new[] { 155, 355 }) { g.DrawLine(pen, x, 65, x + 35, 65); g.DrawLine(pen, x + 35, 65, x + 25, 55); g.DrawLine(pen, x + 35, 65, x + 25, 75); }
    }
}
