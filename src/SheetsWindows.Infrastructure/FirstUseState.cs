using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public static class FirstUseState
{
    public static bool NeedsSetup(LocalStorage storage)
    {
        if (!File.Exists(LauncherConfiguration.ClientPath(storage)) || !File.Exists(PilotSetup.PolicyPath(storage))) return true;
        // Validate existing data; never reset an invalid installation as if it were new.
        _ = LauncherConfiguration.LoadClientAsync(storage).GetAwaiter().GetResult();
        var policy = PilotSetup.PolicyPath(storage);
        if ((File.GetAttributes(policy) & FileAttributes.ReparsePoint) != 0 || new FileInfo(policy).Length > 32768) throw new InvalidDataException("Invalid folder policy.");
        if (!Path.IsPathFullyQualified(File.ReadAllText(policy))) throw new InvalidDataException("Invalid folder policy.");
        return false;
    }
    public static bool NeedsAuthorization(LocalStorage storage, ITokenVault? vault = null)
    {
        if (NeedsSetup(storage)) return true;
        var client = LauncherConfiguration.LoadClientAsync(storage).GetAwaiter().GetResult();
        try
        {
            var tokens = (vault ?? new DpapiTokenVault(Path.Combine(storage.Root, "auth"), client.Id)).Load();
            return tokens is null || tokens.ClientId != client.Id || !tokens.AccountId.StartsWith(client.Id + ":", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(tokens.RefreshToken);
        }
        catch (AuthorizationRequiredException) { return true; }
    }
}
