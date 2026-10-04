using SheetsWindows.Infrastructure;

namespace SheetsWindows.Windows;

// Offline native regression checks on the same WinForms message loop used by the app.
internal static class InterfaceVerification
{
    public static int Run(string output)
    {
        var measurements = new Dictionary<string, ProcessingMetrics>();
        void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
        using (var action = new MenuActionButton())
        {
            var invoked = 0; action.Click += (_, _) => invoked++;
            action.InvokeAction(); action.Enabled = false; action.InvokeAction();
            Require(invoked == 1, "Recovery menu must invoke enabled handlers and refuse disabled actions.");
        }
        Branding.PreviewTheme(ApplicationTheme.Light);
        using var home = new LauncherForm(new(LauncherAction.Home));
        using var preview = new ProcessingForm(new(LauncherAction.Open, "preview.xlsx"), preview: true);
        home.Show(); preview.Show(); Application.DoEvents();
        var about = Descendants(home).OfType<LinkLabel>().Single(l => l.Text == "Sobre / Licenças");
        Require(about.Width >= TextRenderer.MeasureText(about.Text, about.Font, Size.Empty, TextFormatFlags.NoPadding).Width, "MIT credit link must not be clipped.");
        using (var recovery = new RecoveryForm(preview: true, previewBusy: true))
        {
            recovery.Show(); Application.DoEvents();
            var stop = Descendants(recovery).OfType<Button>().Single(b => b.Text == "Cancelar retomada");
            var list = Descendants(recovery).OfType<ListView>().Single();
            Require(stop.Visible && stop.Enabled && !stop.Bounds.IntersectsWith(list.Bounds) && stop.Bottom <= recovery.ClientSize.Height, "Recovery cancellation must remain visible outside the list.");
            var numbers = Descendants(recovery).OfType<NumericUpDown>().ToArray();
            Require(numbers.Any(n => n.Value == 30 && n.Maximum == 365) && numbers.Any(n => n.Value == 200 && n.Maximum == 1000), "Backup defaults and quota ceiling must be visible.");
            Require(!Descendants(recovery).OfType<CheckBox>().Single().Checked, "Automatic cleanup requires informed opt-in.");
            Require(list.Columns.Count == 4 && list.Items.Cast<ListViewItem>().All(i => i.Tag is RecoveryEntry), "Backup table must preserve recovery identity and expose four readable columns.");
            Require(list.Items.Count == 3, "Backup preview must cover completed, protected and cleaned history.");
            foreach (var button in Descendants(recovery).OfType<Button>().Where(b => b.Visible)) Require(button.Bottom <= button.Parent!.ClientSize.Height && button.Right <= button.Parent.ClientSize.Width, "Backup actions must fit their container.");
            recovery.Hide();
        }
        using (var setup = new SetupForm())
        {
            setup.Show(); Application.DoEvents();
            var advanced = Descendants(setup).OfType<Button>().Single(b => b.Text == "Opções avançadas…");
            var dialog = setup.AdvancedDialog;
            var close = dialog.Controls.OfType<Button>().Single(b => b.Text == "Fechar");
            var checkedModal = false;
            dialog.Shown += (_, _) => dialog.BeginInvoke((Action)(() =>
            {
                checkedModal = dialog.Modal && dialog.Owner == setup && close.Visible && close.Bottom <= dialog.ClientSize.Height;
                close.PerformClick();
            }));
            advanced.PerformClick();
            Require(checkedModal && setup.Enabled && !dialog.Visible, "Advanced settings must be modal, keep Close visible and restore the owner after closing.");
            advanced.PerformClick();
            Require(!dialog.IsDisposed && setup.Enabled, "Advanced settings must reopen after closing.");
            setup.Hide();
        }
        using (var tutorial = new TutorialForm())
        {
            tutorial.Show(); Application.DoEvents();
            Require(!Descendants(tutorial).OfType<CheckBox>().Single().Checked, "Tutorial dismissal must require explicit opt-in.");
            var next = Descendants(tutorial).OfType<Button>().Single(b => b.Text == "Próximo");
            for (var i = 0; i < 3; i++) next.PerformClick();
            Require(next.Text == "Começar", "Tutorial must reach its final page.");
            Descendants(tutorial).OfType<Button>().Single(b => b.Text == "Pular tutorial").PerformClick();
            Require(!tutorial.Visible, "Tutorial must be skippable on the final page.");
        }
        Require(home.Font.Name == "Segoe UI", "Native interface must use the shared readable font.");
        Require(Descendants(home).OfType<Button>().Any(b => b.Tag as string == "primary" && b.Text.Contains("Abrir planilha")), "Home must emphasize its primary action.");
        var light = home.BackColor;
        Branding.PreviewTheme(ApplicationTheme.Dark);
        Require(home.BackColor != light && home.BackColor == preview.BackColor, "Theme must update open forms together.");
        var toggle = Descendants(home).OfType<ThemeToggle>().Single();
        Require(toggle.Dark && toggle.AccessibilityObject.State.HasFlag(AccessibleStates.Checked), "Theme accessibility state must match dark selection.");
        Branding.PreviewTheme(ApplicationTheme.Light);
        Require(home.BackColor == light && !toggle.Dark, "Theme must switch back without restarting.");
        home.Hide(); preview.Hide();
        using (var success = new ProcessingForm(new(LauncherAction.Open, "preview.xlsx"), execute: async (_, _) => await Task.Yield(), recordDiagnostics: false))
        {
            success.ShowDialog(); measurements["OfflineSuccessUi"] = success.LastMetrics!; Require(success.ExitCode == 0 && !success.IsBusy, "Successful processing must close automatically.");
        }
        using (var observed = new ProcessingForm(new(LauncherAction.Open, "preview.xlsx"), execute: async (_, ct) => await Task.Delay(350, ct), recordDiagnostics: false))
        {
            observed.ShowDialog(); measurements["OfflineSampledCpuUi"] = observed.LastMetrics!;
            Require(observed.LastMetrics!.PeakCpuPercent is >= 0 and <= 100, "Processing must sample CPU without keeping the process resident.");
        }
        var cancelled = false;
        using (var cancel = new ProcessingForm(new(LauncherAction.Open, "preview.xlsx"), execute: async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { cancelled = true; throw; }
        }, recordDiagnostics: false))
        using (var timer = new System.Windows.Forms.Timer { Interval = 25 })
        {
            timer.Tick += (_, _) => { if (cancel.IsBusy) { timer.Stop(); cancel.Close(); } }; timer.Start();
            cancel.ShowDialog(); measurements["OfflineCancelledUi"] = cancel.LastMetrics!; Require(cancelled && cancel.ExitCode == 1 && !cancel.IsBusy, "Closing busy processing must cancel and await the worker.");
        }
        using (var failure = new ProcessingForm(new(LauncherAction.Copy, "preview.csv"), execute: (_, _) => Task.FromException(new ConversionMismatchException()), recordDiagnostics: false))
        using (var timer = new System.Windows.Forms.Timer { Interval = 25 })
        {
            var failureVisible = false;
            timer.Tick += (_, _) =>
            {
                if (failure.IsBusy || failure.ExitCode != 1) return;
                failureVisible = failure.Visible && Descendants(failure).OfType<Button>().Any(b => b.Text == "Exportar diagnóstico…")
                    && Descendants(failure).OfType<Button>().Any(b => b.Text == "Recuperação / backups");
                timer.Stop(); failure.Close();
            };
            timer.Start(); failure.ShowDialog(); measurements["OfflineFailureUi"] = failure.LastMetrics!; Require(failureVisible, "Failure must stay visible with diagnosis and recovery actions.");
        }
        // Synthetic CSV matching the user case by shape, without publishing their contents.
        var csv = new System.Text.StringBuilder();
        for (var row = 0; row < 5000; row++) { for (var column = 0; column < 40; column++) { if (column > 0) csv.Append(';'); csv.Append("cell_").Append(row).Append('_').Append(column); } csv.Append('\n'); }
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        var benchmark = new ProcessingTelemetry(); SpreadsheetPayload payload;
        using (benchmark.Begin(ProcessingPhase.Conversion)) payload = SpreadsheetFormats.Prepare("csv", bytes, new("auto", "semicolon"));
        using (benchmark.Begin(ProcessingPhase.Verification)) SpreadsheetFormats.VerifyValues(payload.Expected!, payload.Bytes);
        measurements["OfflineCsv5000x40RoundTrip"] = benchmark.Capture();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { Measurements = measurements, InputBytes = bytes.Length, Rows = 5000, Columns = 40, GoogleNetwork = false }));
        return 0;
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
