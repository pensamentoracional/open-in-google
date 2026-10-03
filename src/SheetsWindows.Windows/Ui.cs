namespace SheetsWindows.Windows;

// Shared native controls. Text remains selectable/readable at Windows DPI and keyboard focus is native.
internal static class Ui
{
    internal static Label Text(string text, bool heading = false) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(480, 0),
        Margin = new Padding(0, heading ? 12 : 4, 0, 8),
        Font = new Font("Segoe UI", heading ? 11 : 10, heading ? FontStyle.Bold : FontStyle.Regular)
    };
    internal static Panel Separator() => new() { Height = 1, Width = 480, Margin = new Padding(0, 10, 0, 10), AccessibleName = "Separador", Tag = "separator" };
    internal static void Primary(Button button) { button.Tag = "primary"; button.MinimumSize = new Size(0, 38); }
    internal static void Adapt(FlowLayoutPanel layout)
    {
        void ResizeChildren()
        {
            var available = Math.Max(120, layout.ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
            foreach (Control child in layout.Controls)
            {
                if (child is FlowLayoutPanel nested) { nested.Width = available; nested.MaximumSize = new Size(available, 0); }
                else if (child is Label label) label.MaximumSize = new Size(available - label.Margin.Horizontal, 0);
                else if (child is CheckBox check) { check.AutoSize = false; check.Width = available; check.Height = TextRenderer.MeasureText(check.Text, check.Font, new Size(available - 28, 0), TextFormatFlags.WordBreak).Height + 12; }
                else if (child is Button or TextBox or ComboBox or TableLayoutPanel || child.Tag as string == "separator") child.Width = available - child.Margin.Horizontal;
            }
        }
        layout.Resize += (_, _) => ResizeChildren();
        layout.FontChanged += (_, _) => ResizeChildren();
        layout.HandleCreated += (_, _) => ResizeChildren();
        ResizeChildren();
    }
}
