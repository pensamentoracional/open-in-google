using Xunit;
namespace SheetsWindows.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows file identity and sharing semantics.";
    }
}

public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Requires Unix permission bits.";
    }
}

public sealed class WindowsCiFactAttribute : FactAttribute
{
    public WindowsCiFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") Skip = "Requires disposable Windows CI profile with SMB share administration.";
    }
}
