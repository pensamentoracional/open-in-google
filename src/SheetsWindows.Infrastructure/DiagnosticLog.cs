using SheetsWindows.Core;
using System.Text;
using System.Text.Json;

namespace SheetsWindows.Infrastructure;

public enum DiagnosticEvent { Started, Completed, Cancelled, AuthorizationRequired, ReconciliationRequired, LocalConflict, ConversionMismatch, Failed }
public sealed record DiagnosticEntry(DateTimeOffset Time, DiagnosticEvent Event, Guid? Operation, ProcessingMetrics? Metrics = null);
// Diagnostic files are disposable; durable operation journals and backups are never pruned.
public sealed class DiagnosticLog(LocalStorage storage)
{
    public const int MaxFileBytes = 64 * 1024;
    public const int FileCount = 4;
    public static DiagnosticEvent Failure(Exception ex) => ex switch
    {
        OperationCanceledException => DiagnosticEvent.Cancelled,
        AuthorizationRequiredException => DiagnosticEvent.AuthorizationRequired,
        ReconciliationRequiredException => DiagnosticEvent.ReconciliationRequired,
        SheetsWindows.Core.LocalConflictException => DiagnosticEvent.LocalConflict,
        ConversionMismatchException => DiagnosticEvent.ConversionMismatch,
        _ => DiagnosticEvent.Failed
    };
    public async Task RecordAsync(DiagnosticEvent kind, Guid? operation = null, ProcessingMetrics? metrics = null)
    {
        try
        {
            if (metrics is { IsValid: false }) throw new InvalidDataException("Invalid processing metrics.");
            if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown diagnostic event.");
            var root = Path.Combine(storage.Root, "logs"); PrivateDirectory.Create(root);
            await using var held = await new FileOperationLock(storage.LocksPath).AcquireAsync("diagnostics");
            var path = Path.Combine(root, "events.jsonl");
            for (var i = 0; i < FileCount; i++)
            {
                var candidate = i == 0 ? path : path + "." + i;
                if (File.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid diagnostic file.");
            }
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new DiagnosticEntry(DateTimeOffset.UtcNow, kind, operation, metrics)) + "\n");
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > MaxFileBytes)
            {
                var last = path + "." + (FileCount - 1); if (File.Exists(last)) File.Delete(last);
                for (var i = FileCount - 2; i >= 0; i--)
                {
                    var source = i == 0 ? path : path + "." + i;
                    if (File.Exists(source)) File.Move(source, path + "." + (i + 1), false);
                }
            }
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read); file.Write(bytes); file.Flush(true);
        }
        catch (Exception ex) when (LauncherErrors.Expected(ex)) { /* Diagnostics cannot change operation outcome. */ }
    }
    public async Task ExportAsync(string destination)
    {
        await using var held = await new FileOperationLock(storage.LocksPath).AcquireAsync("diagnostics");
        var root = Path.Combine(storage.Root, "logs"); var output = new StringBuilder();
        for (var i = FileCount - 1; i >= 0; i--)
        {
            var path = Path.Combine(root, "events.jsonl" + (i == 0 ? "" : "." + i));
            if (!File.Exists(path)) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("Invalid diagnostic file.");
            foreach (var line in await File.ReadAllLinesAsync(path))
            {
                var entry = JsonSerializer.Deserialize<DiagnosticEntry>(line) ?? throw new InvalidDataException("Invalid diagnostic entry.");
                if (entry.Metrics is { IsValid: false }) throw new InvalidDataException("Invalid processing metrics.");
                if (!Enum.IsDefined(entry.Event)) throw new InvalidDataException("Invalid diagnostic event.");
                output.AppendLine(JsonSerializer.Serialize(entry)); // Re-serialize only allowlisted fields.
            }
        }
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(Encoding.UTF8.GetBytes(output.ToString())); file.Flush(true);
    }
}
