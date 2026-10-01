using SheetsWindows.Infrastructure;
namespace SheetsWindows.LockProbe;

public sealed class Marker;
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3) return 2;
        await using var held = await new FileOperationLock(args[0]).AcquireAsync(args[1]);
        // Publish readiness only after all marker I/O handles have closed.
        var temporary = args[2] + ".tmp";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(System.Text.Encoding.UTF8.GetBytes("held")); file.Flush(true); }
        File.Move(temporary, args[2], false);
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }
}
