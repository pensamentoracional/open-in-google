using System.Drawing.Drawing2D;

namespace SheetsWindows.Windows;

// Native button: Space/Enter, Tab navigation and visible keyboard focus.
internal sealed class ThemeToggle : Button
{
    [System.ComponentModel.DefaultValue(false)]
    public bool Dark { get; set; }
    public ThemeToggle() { Text = "𖤓  ☾"; AccessibleRole = AccessibleRole.CheckButton; AccessibleDescription = "Alterna entre tema claro e escuro; preferência salva neste usuário do Windows."; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
        int Px(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
        var track = new Rectangle(Px(1), Px(2), Width - Px(3), Height - Px(5));
        using var shape = new GraphicsPath(); var radius = track.Height;
        shape.AddArc(track.X, track.Y, radius, radius, 90, 180); shape.AddArc(track.Right - radius, track.Y, radius, radius, 270, 180); shape.CloseFigure();
        using var fill = new SolidBrush(Dark ? Color.FromArgb(54, 116, 72) : Color.FromArgb(214, 228, 218)); g.FillPath(fill, shape);
        using var border = new Pen(Dark ? Color.FromArgb(98, 217, 139) : Color.FromArgb(25, 134, 74)); g.DrawPath(border, shape);
        var diameter = track.Height - Px(6); var thumbX = Dark ? track.Right - diameter - Px(3) : track.X + Px(3);
        using var thumb = new SolidBrush(Color.White); g.FillEllipse(thumb, thumbX, track.Y + Px(3), diameter, diameter);
        // Center vector symbols in each half of the track; independent of font bearings.
        var cy = track.Top + track.Height / 2f;
        var left = track.Left + track.Height / 2f;
        var right = track.Right - track.Height / 2f;
        using var symbol = new Pen(Color.FromArgb(21, 76, 43), Px(1));
        var r = Px(4);
        g.DrawEllipse(symbol, left - r, cy - r, r * 2, r * 2);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            g.DrawLine(symbol, left + (float)Math.Cos(angle) * Px(6), cy + (float)Math.Sin(angle) * Px(6),
                left + (float)Math.Cos(angle) * Px(8), cy + (float)Math.Sin(angle) * Px(8));
        }
        using var crescent = new GraphicsPath();
        crescent.AddEllipse(right - Px(7), cy - Px(7), Px(14), Px(14));
        using var moon = new Region(crescent);
        using var cutout = new GraphicsPath();
        cutout.AddEllipse(right - Px(2), cy - Px(8), Px(14), Px(14));
        moon.Exclude(cutout);
        using var ink = new SolidBrush(Color.FromArgb(21, 76, 43));
        g.FillRegion(ink, moon);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width - 1, Height - 1), ForeColor, BackColor);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ToggleAccessibility(this);
    private sealed class ToggleAccessibility(ThemeToggle owner) : ControlAccessibleObject(owner)
    {
        public override string DefaultAction => "Alternar tema";
        public override void DoDefaultAction() => owner.PerformClick();
        public override string? Value { get => owner.Dark ? "Escuro" : "Claro"; set { } }
        public override AccessibleStates State => base.State | (owner.Dark ? AccessibleStates.Checked : AccessibleStates.None);
    }
}
