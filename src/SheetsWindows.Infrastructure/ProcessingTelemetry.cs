using System.Diagnostics;

namespace SheetsWindows.Infrastructure;

public enum ProcessingPhase { Conversion, Upload, Verification }
public sealed record ProcessingMetrics(long ElapsedMs, long? ReadyMs, long? ConversionMs, long? UploadMs, long? VerificationMs, long? PeakWorkingSetBytes, long? CpuMs, double? PeakCpuPercent = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => ElapsedMs >= 0 && ReadyMs is null or >= 0 && ConversionMs is null or >= 0 && UploadMs is null or >= 0 && VerificationMs is null or >= 0 && PeakWorkingSetBytes is null or >= 0 && CpuMs is null or >= 0
        && (PeakCpuPercent is null || double.IsFinite(PeakCpuPercent.Value) && PeakCpuPercent is >= 0 and <= 100);
}
// Numeric, per-run counters only. No source path, account, content or upload URL.
public sealed class ProcessingTelemetry(long? startedAt = null, IProgress<string>? progress = null)
{
    private readonly long started = startedAt ?? Stopwatch.GetTimestamp();
    private readonly long[] ticks = new long[3];
    private readonly int[] observed = new int[3];
    private long ready = -1;
    private readonly TimeSpan? initialCpu = Cpu();
    private readonly object cpuGate = new();
    private TimeSpan? previousCpu;
    private long previousCpuAt;
    private double? peakCpu;
    // UI timer calls this every ~100 ms. Percent is normalized to all logical processors.
    public void SampleCpu()
    {
        lock (cpuGate)
        {
            var now = Stopwatch.GetTimestamp(); var value = Cpu();
            if (value is null) return;
            if (previousCpu is null) { previousCpu = value; previousCpuAt = now; return; }
            var elapsed = (now - previousCpuAt) * 1000.0 / Stopwatch.Frequency;
            if (elapsed < 80) return; // Avoid reporting short, quantized CPU-clock spikes.
            var percent = Math.Clamp((value.Value - previousCpu.Value).TotalMilliseconds / elapsed * 100 / Environment.ProcessorCount, 0, 100);
            peakCpu = Math.Max(peakCpu ?? 0, percent); previousCpu = value; previousCpuAt = now;
        }
    }
    private static TimeSpan? Cpu() { try { using var p = Process.GetCurrentProcess(); return p.TotalProcessorTime; } catch (Exception ex) when (LauncherErrors.Expected(ex)) { return null; } }
    public void MarkReady() => Interlocked.CompareExchange(ref ready, Stopwatch.GetTimestamp() - started, -1);
    public IDisposable Begin(ProcessingPhase phase)
    {
        if (!Enum.IsDefined(phase)) throw new ArgumentException("Invalid phase.");
        try { progress?.Report(phase switch { ProcessingPhase.Conversion => "Convertendo a planilha…", ProcessingPhase.Upload => "Enviando e conferindo a importação no Google…", _ => "Conferindo os dados convertidos antes da substituição…" }); }
        catch (Exception ex) when (LauncherErrors.Expected(ex)) { /* UI telemetry cannot change import outcome. */ }
        return new Scope(this, phase);
    }
    private sealed class Scope(ProcessingTelemetry owner, ProcessingPhase phase) : IDisposable
    {
        private readonly long start = Stopwatch.GetTimestamp(); private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; Interlocked.Add(ref owner.ticks[(int)phase], Stopwatch.GetTimestamp() - start); Volatile.Write(ref owner.observed[(int)phase], 1); }
    }
    private static long Ms(long value) => (long)(value * 1000.0 / Stopwatch.Frequency);
    public ProcessingMetrics Capture()
    {
        long? memory = null, cpu = null;
        try { using var p = Process.GetCurrentProcess(); p.Refresh(); memory = p.PeakWorkingSet64; if (initialCpu is { } initial) cpu = Math.Max(0, (long)(p.TotalProcessorTime - initial).TotalMilliseconds); }
        catch (Exception ex) when (LauncherErrors.Expected(ex)) { }
        long? Stage(int index) => Volatile.Read(ref observed[index]) == 0 ? null : Ms(Interlocked.Read(ref ticks[index]));
        var readyTicks = Interlocked.Read(ref ready);
        double? peak; lock (cpuGate) peak = peakCpu;
        return new(Ms(Stopwatch.GetTimestamp() - started), readyTicks < 0 ? null : Ms(readyTicks), Stage(0), Stage(1), Stage(2), memory, cpu, peak);
    }
}
