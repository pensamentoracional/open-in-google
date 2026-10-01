using SheetsWindows.Infrastructure;
namespace SheetsWindows.LockProbe;

public sealed class Marker;
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3) return 2;
        await using var held = await new FileOperationLock(args[0]).AcquireAsync(args[1]);
        await File.WriteAllTextAsync(args[2], "held");
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }
}
