using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal static class Branding
{
    public const string Name = "ZagoSheetsWin";
    public const string Credit = "Evolução Zagotools • Base: Open in Google, de Swati K (SwatiK425) • MIT";
    private static readonly Color Background = Color.FromArgb(7, 16, 11);
    private static readonly Color Panel = Color.FromArgb(13, 28, 19);
    private static readonly Color Foreground = Color.FromArgb(237, 248, 240);
    private static readonly Color Green = Color.FromArgb(98, 217, 139);
    public static void Apply(Form form, bool aboutButton = true)
    {
        using (var stream = typeof(Branding).Assembly.GetManifestResourceStream("Brand.icon.ico")!) form.Icon = new Icon(stream);
        form.Font = new Font("Consolas", 10);
        void Theme(Control control)
        {
            if (!SystemInformation.HighContrast)
            {
                control.BackColor = control is Button or TextBox or ComboBox or ListBox ? Panel : Background;
                control.ForeColor = Foreground;
                if (control is Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Green; }
                if (control is LinkLabel link) { link.LinkColor = Green; link.ActiveLinkColor = Foreground; link.VisitedLinkColor = Green; }
            }
            foreach (Control child in control.Controls) Theme(child);
        }
        var header = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(12) };
        using var source = typeof(Branding).Assembly.GetManifestResourceStream("Brand.logo.png")!;
        using var original = Image.FromStream(source); var logo = new Bitmap(original);
        var picture = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Left, Width = 145 };
        form.Disposed += (_, _) => logo.Dispose();
        var title = new Label { Text = Name + "\nPlanilhas no Google Sheets", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
        header.Controls.Add(title); header.Controls.Add(picture);
        if (aboutButton)
        {
            var about = new Button { Text = "Sobre / MIT", Dock = DockStyle.Right, Width = 132 };
            about.Click += (_, _) => { using var info = new AboutForm(); info.ShowDialog(form); }; header.Controls.Add(about);
        }
        form.Controls.Add(header); header.SendToBack(); Theme(form);
    }
}
internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "Sobre — ZagoSheetsWin / Zagotools"; ClientSize = new Size(810, 520); MinimumSize = new Size(600, 400); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18), AutoScroll = true };
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(740, 0), Text = "ZagoSheetsWin 0.9.4 — Zagotools\n\nZagoSheetsWin é uma evolução do projeto Open in Google, de Swati K (SwatiK425), desenvolvida pelo Zagotools e distribuída sob licença MIT.\n\nCopyright (c) 2026 Swati K. A autoria e a licença originais foram preservadas." });
        foreach (var item in new[] { ("Projeto original — Open in Google", "https://github.com/SwatiK425/open-in-google/"), ("Autora original — SwatiK425", "https://github.com/SwatiK425"), ("Código da evolução — Zagotools", "https://github.com/zagozago/ZagoSheetsWin") })
        {
            var link = new LinkLabel { Text = item.Item1, AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
            link.LinkClicked += (_, _) => { try { new BrowserLauncher().Open(new Uri(item.Item2)); } catch (Exception ex) when (LauncherErrors.Expected(ex)) { MessageBox.Show(item.Item2, "Link do projeto"); } };
            body.Controls.Add(link);
        }
        var license = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        body.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 740, Height = 170, Text = File.ReadAllText(license) });
        body.Controls.Add(new Label { Text = "ExcelDataReader: licença MIT incluída em third-party/ExcelDataReader-LICENSE.txt.", AutoSize = true, MaximumSize = new Size(740, 0) });
        Controls.Add(body); Branding.Apply(this, aboutButton: false);
    }
}
