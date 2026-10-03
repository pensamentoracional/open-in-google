using System.Text.Json;
using SheetsWindows.Infrastructure;
using Xunit;

namespace SheetsWindows.Tests;

public sealed class InterfaceSettingsTests
{
    [Fact]
    public async Task TutorialOnlyHidesAfterExplicitPreferenceAndPreservesOtherSettings()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "interface"));
        Assert.False(TutorialSettings.Load(storage)); Assert.False(Directory.Exists(storage.Root));
        await ThemeSettings.SaveAsync(storage, ApplicationTheme.Dark);
        await TutorialSettings.SaveAsync(storage, true); Assert.True(TutorialSettings.Load(storage));
        Assert.Equal(ApplicationTheme.Dark, ThemeSettings.Load(storage));
        await TutorialSettings.SaveAsync(storage, false); Assert.False(TutorialSettings.Load(storage));
        Assert.Empty(Directory.GetFiles(storage.Root, "*.tmp"));
    }
    [Fact]
    public async Task ThemeDefaultsToLightWithoutCreatingStateAndPersistsIndependently()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "interface"));
        Assert.Equal(ApplicationTheme.Light, ThemeSettings.Load(storage)); Assert.False(Directory.Exists(storage.Root));
        await XlsReplacementSettings.SaveAsync(storage, false); var xls = File.ReadAllBytes(Path.Combine(storage.Root, "xls-replacement.json"));
        await ThemeSettings.SaveAsync(storage, ApplicationTheme.Dark); Assert.Equal(ApplicationTheme.Dark, ThemeSettings.Load(storage));
        await ThemeSettings.SaveAsync(storage, ApplicationTheme.Light); Assert.Equal(ApplicationTheme.Light, ThemeSettings.Load(storage));
        Assert.Equal(xls, File.ReadAllBytes(Path.Combine(storage.Root, "xls-replacement.json")));
        Assert.Empty(Directory.GetFiles(storage.Root, "*.tmp"));
    }
    [Fact]
    public async Task InvalidThemeIsPreservedRatherThanResetOrOverwritten()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "interface")); Directory.CreateDirectory(storage.Root);
        var path = Path.Combine(storage.Root, "theme.json"); File.WriteAllText(path, "\"unexpected\"");
        Assert.Throws<InvalidDataException>(() => ThemeSettings.Load(storage));
        await Assert.ThrowsAsync<InvalidDataException>(() => ThemeSettings.SaveAsync(storage, ApplicationTheme.Dark));
        Assert.Equal("\"unexpected\"", File.ReadAllText(path));
    }
    [Fact]
    public async Task MetricsIdentifyOnlyStagesActuallyReachedAndExportOnlyNumericData()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "interface"));
        var telemetry = new ProcessingTelemetry(); telemetry.MarkReady();
        Assert.Null(telemetry.Capture().ConversionMs);
        using (telemetry.Begin(ProcessingPhase.Conversion)) { await Task.Yield(); }
        var scope = telemetry.Begin(ProcessingPhase.Upload); scope.Dispose(); var before = telemetry.Capture().UploadMs; scope.Dispose();
        var metrics = telemetry.Capture(); Assert.True(metrics.IsValid); Assert.NotNull(metrics.ReadyMs); Assert.NotNull(metrics.ConversionMs);
        Assert.Equal(before, metrics.UploadMs); Assert.Null(metrics.VerificationMs);
        var log = new DiagnosticLog(storage); await log.RecordAsync(DiagnosticEvent.Completed, metrics: metrics);
        var destination = Path.Combine(w.Root, "metrics.jsonl"); await log.ExportAsync(destination);
        var entry = JsonSerializer.Deserialize<DiagnosticEntry>(File.ReadAllText(destination)); Assert.Equal(metrics, entry!.Metrics);
        Assert.DoesNotContain(storage.Root, File.ReadAllText(destination));
        File.WriteAllText(Path.Combine(storage.Root, "logs", "events.jsonl"), "{\"Time\":\"2026-10-03T00:00:00Z\",\"Event\":1,\"Operation\":null,\"Metrics\":{\"ElapsedMs\":0,\"source\":\"secret.csv\"},\"account\":\"private\"}\n");
        var scrubbed = Path.Combine(w.Root, "scrubbed.jsonl"); await log.ExportAsync(scrubbed);
        Assert.DoesNotContain("secret.csv", File.ReadAllText(scrubbed)); Assert.DoesNotContain("private", File.ReadAllText(scrubbed));
    }
    [Fact]
    public async Task CpuSamplesUseBoundedIntervalsAndRejectNonFiniteMetrics()
    {
        var telemetry = new ProcessingTelemetry(); telemetry.SampleCpu(); telemetry.SampleCpu();
        Assert.Null(telemetry.Capture().PeakCpuPercent);
        await Task.Delay(120); telemetry.SampleCpu();
        Assert.InRange(telemetry.Capture().PeakCpuPercent!.Value, 0, 100);
        Assert.False(new ProcessingMetrics(0, null, null, null, null, null, null, double.NaN).IsValid);
        Assert.False(new ProcessingMetrics(0, null, null, null, null, null, null, 101).IsValid);
    }
    [Fact]
    public async Task InvalidMetricsCannotBeExported()
    {
        using var w = new Workspace(); var storage = new LocalStorage(Path.Combine(w.Root, "interface"));
        var log = new DiagnosticLog(storage); await log.RecordAsync(DiagnosticEvent.Started);
        var entry = new DiagnosticEntry(DateTimeOffset.UtcNow, DiagnosticEvent.Failed, null, new(-1, null, null, null, null, null, null));
        File.WriteAllText(Path.Combine(storage.Root, "logs", "events.jsonl"), JsonSerializer.Serialize(entry) + "\n");
        var destination = Path.Combine(w.Root, "invalid.jsonl");
        await Assert.ThrowsAsync<InvalidDataException>(() => log.ExportAsync(destination)); Assert.False(File.Exists(destination));
    }
}
