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
        var track = new Rectangle(1, 2, Width - 3, Height - 5);
        using var shape = new GraphicsPath(); var radius = track.Height;
        shape.AddArc(track.X, track.Y, radius, radius, 90, 180); shape.AddArc(track.Right - radius, track.Y, radius, radius, 270, 180); shape.CloseFigure();
        using var fill = new SolidBrush(Dark ? Color.FromArgb(54, 116, 72) : Color.FromArgb(214, 228, 218)); g.FillPath(fill, shape);
        using var border = new Pen(Dark ? Color.FromArgb(98, 217, 139) : Color.FromArgb(25, 134, 74)); g.DrawPath(border, shape);
        var diameter = track.Height - 6; var thumbX = Dark ? track.Right - diameter - 3 : track.X + 3;
        using var thumb = new SolidBrush(Color.White); g.FillEllipse(thumb, thumbX, track.Y + 3, diameter, diameter);
        using var sunFont = new Font("Segoe UI Historic", 12); using var moonFont = new Font("Segoe UI Symbol", 13);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, "𖤓", sunFont, new Rectangle(4, 3, 28, Height - 5), Color.FromArgb(21, 76, 43), flags);
        TextRenderer.DrawText(g, "☾", moonFont, new Rectangle(Width - 33, 3, 28, Height - 5), Color.FromArgb(21, 76, 43), flags);
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
