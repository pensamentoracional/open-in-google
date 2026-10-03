using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

internal static class Branding
{
    public const string Name = "ZagoSheetsWin";
    public const string Credit = "Evolução Zagotools • Base: Open in Google, de Swati K (SwatiK425) • MIT";
    private static ApplicationTheme? selected;
    private static event Action? ThemeChanged;
    internal static ApplicationTheme Current
    {
        get
        {
            if (selected is null) { try { selected = ThemeSettings.Load(LocalStorage.ForCurrentUser()); } catch (Exception ex) when (LauncherErrors.Expected(ex)) { selected = ApplicationTheme.Light; } }
            return selected.Value;
        }
    }
    internal static void PreviewTheme(ApplicationTheme theme) { selected = theme; ThemeChanged?.Invoke(); }
    internal static void Refresh(Form form) => ThemeChanged?.Invoke();
    public static void Apply(Form form, bool aboutButton = true, bool compact = false)
    {
        using (var stream = typeof(Branding).Assembly.GetManifestResourceStream("Brand.icon.ico")!) form.Icon = new Icon(stream);
        form.Font = new Font("Consolas", 10);
        void Theme(Control control)
        {
            var highContrast = SystemInformation.HighContrast;
            var dark = Current == ApplicationTheme.Dark;
            var background = highContrast ? SystemColors.Window : dark ? Color.FromArgb(7, 16, 11) : Color.FromArgb(238, 247, 240);
            var panel = highContrast ? SystemColors.Window : dark ? Color.FromArgb(13, 28, 19) : Color.White;
            var foreground = highContrast ? SystemColors.WindowText : dark ? Color.FromArgb(237, 248, 240) : Color.FromArgb(23, 49, 38);
            var green = highContrast ? SystemColors.Highlight : dark ? Color.FromArgb(98, 217, 139) : Color.FromArgb(25, 134, 74);
            control.BackColor = control is Button or TextBox or ComboBox or ListBox ? panel : background;
            control.ForeColor = foreground;
            if (control is Button button) { button.FlatStyle = highContrast ? FlatStyle.System : FlatStyle.Flat; button.FlatAppearance.BorderColor = green; button.UseVisualStyleBackColor = false;
                button.FlatAppearance.MouseOverBackColor = highContrast ? SystemColors.Highlight : dark ? Color.FromArgb(18, 37, 26) : Color.FromArgb(244, 250, 246);
                button.FlatAppearance.MouseDownBackColor = button.FlatAppearance.MouseOverBackColor; }
            if (control is LinkLabel link) { link.LinkColor = green; link.ActiveLinkColor = foreground; link.VisitedLinkColor = green; }
            foreach (Control child in control.Controls) Theme(child);
            control.Invalidate();
        }
        var header = new Panel { Dock = DockStyle.Top, Height = compact ? 60 : 70, Padding = new Padding(12, 8, 12, 8) };
        using var source = typeof(Branding).Assembly.GetManifestResourceStream("Brand.logo.png")!;
        using var original = Image.FromStream(source); var logo = new Bitmap(original);
        var picture = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Left, Width = compact ? 105 : 130 };
        form.Disposed += (_, _) => logo.Dispose();
        var title = new Label { Text = Name + (compact ? "" : "\nPlanilhas no Google Sheets"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, Width = aboutButton ? 204 : 88, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        var toggle = new ThemeToggle { Width = 80, Height = 32 };
        var tooltip = new ToolTip();
        void UpdateToggle() { toggle.Dark = Current == ApplicationTheme.Dark; toggle.AccessibleName = toggle.Dark ? "Ativar modo claro" : "Ativar modo escuro"; tooltip.SetToolTip(toggle, toggle.AccessibleName); toggle.Invalidate(); }
        toggle.Click += async (_, _) =>
        {
            toggle.Enabled = false;
            try { var next = Current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark; await ThemeSettings.SaveAsync(LocalStorage.ForCurrentUser(), next); selected = next; ThemeChanged?.Invoke(); }
            catch (Exception ex) when (LauncherErrors.Expected(ex)) { MessageBox.Show(form, "Não foi possível salvar o tema. A preferência anterior foi preservada.", Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { if (!toggle.IsDisposed) toggle.Enabled = true; }
        };
        if (aboutButton)
        {
            var about = new LinkLabel { Text = "Sobre / MIT", AutoSize = false, Width = 110, Height = 32, TextAlign = ContentAlignment.MiddleCenter, TabStop = true, AccessibleName = "Sobre o ZagoSheetsWin e licença MIT" };
            about.LinkClicked += (_, _) => { using var info = new AboutForm(); info.ShowDialog(form); }; right.Controls.Add(about);
        }
        right.Controls.Add(toggle); header.Controls.Add(title); header.Controls.Add(picture); header.Controls.Add(right);
        form.Controls.Add(header); header.SendToBack();
        void RefreshTheme() { if (!form.IsDisposed) { Theme(form); UpdateToggle(); } }
        Action changed = RefreshTheme; ThemeChanged += changed;
        Microsoft.Win32.UserPreferenceChangedEventHandler preference = (_, _) => { if (form.IsHandleCreated && !form.IsDisposed) { try { form.BeginInvoke((Action)RefreshTheme); } catch (InvalidOperationException) { } } };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += preference;
        form.Disposed += (_, _) => { ThemeChanged -= changed; Microsoft.Win32.SystemEvents.UserPreferenceChanged -= preference; tooltip.Dispose(); };
        RefreshTheme();
    }
}
internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "Sobre — ZagoSheetsWin / Zagotools"; ClientSize = new Size(810, 520); MinimumSize = new Size(600, 400); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18), AutoScroll = true };
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(740, 0), Text = "ZagoSheetsWin 0.9.5 — Zagotools\n\nZagoSheetsWin é uma evolução do projeto Open in Google, de Swati K (SwatiK425), desenvolvida pelo Zagotools e distribuída sob licença MIT.\n\nCopyright (c) 2026 Swati K. A autoria e a licença originais foram preservadas." });
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
