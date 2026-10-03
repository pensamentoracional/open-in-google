using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SheetsWindows.Core;

namespace SheetsWindows.Infrastructure;

public enum AssociationValueKind { String, None }

public static class WindowsAssociationPlan
{
    public const string AppName = "ZagoSheetsWin";
    public const string LegacyName = "Sheets Windows";
    public const string ProgId = "SheetsWindows.Xlsx";
    public const string AppRoot = @"Software\SheetsWindows\Integration";
    public const string ProgRoot = @"Software\Classes\SheetsWindows.Xlsx";
    public const string ExecutableRoot = @"Software\Classes\Applications\SheetsWindows.exe";
    public const string CapabilityPath = AppRoot + @"\Capabilities";
    public const string RegisteredApps = @"Software\RegisteredApplications";
    public const string OpenWith = @"Software\Classes\.xlsx\OpenWithProgids";
    public const string Owner = "SheetsWindows.Integration.v1";
    public static readonly string[] OwnedRoots = [AppRoot, ProgRoot, ExecutableRoot];
    public static Uri DefaultsUri => new("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppName));
    public static string Command(string exe)
    {
        if (!Path.IsPathFullyQualified(exe) || exe.Any(char.IsControl) || exe.Contains('"') || exe.Contains('%')
            || !string.Equals(Path.GetFileName(exe), "SheetsWindows.exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Stable SheetsWindows.exe path required.");
        return "\"" + Path.GetFullPath(exe) + "\" --open \"%1\"";
    }
    public sealed record Value(string Key, string Name, object Data, AssociationValueKind Kind);
    public static IReadOnlyList<Value> Values(string exe, bool legacy = false, bool legacyIcon = false)
    {
        var name = legacy ? LegacyName : AppName;
        var command = Command(exe); var full = Path.GetFullPath(exe);
        var fileIcon = legacy || legacyIcon ? full : Path.Combine(Path.GetDirectoryName(full)!, "sheet-shortcut.ico");
        List<Value> values = [
            new(AppRoot, "SW_Owner", Owner, AssociationValueKind.String), new(AppRoot, "SW_Executable", full, AssociationValueKind.String),
            new(ProgRoot, "SW_Owner", Owner, AssociationValueKind.String), new(ProgRoot, "SW_Executable", full, AssociationValueKind.String),
            new(ExecutableRoot, "SW_Owner", Owner, AssociationValueKind.String), new(ExecutableRoot, "SW_Executable", full, AssociationValueKind.String),
            new(CapabilityPath, "ApplicationName", name, AssociationValueKind.String),
            new(CapabilityPath, "ApplicationDescription", "Importa XLSX para Google Sheets e substitui por atalho, mantendo backup privado.", AssociationValueKind.String),
            new(CapabilityPath, "ApplicationIcon", "\"" + full + "\",0", AssociationValueKind.String),
            new(CapabilityPath + @"\FileAssociations", ".xlsx", ProgId, AssociationValueKind.String),
            new(ProgRoot, "", "Planilha no Google Sheets", AssociationValueKind.String), new(ProgRoot, "FriendlyTypeName", "Planilha no Google Sheets", AssociationValueKind.String),
            new(ProgRoot + @"\DefaultIcon", "", "\"" + fileIcon + "\",0", AssociationValueKind.String),
            new(ProgRoot + @"\shell\open\command", "", command, AssociationValueKind.String),
            new(ExecutableRoot, "FriendlyAppName", name, AssociationValueKind.String),
            new(ExecutableRoot + @"\SupportedTypes", ".xlsx", "", AssociationValueKind.String),
            new(ExecutableRoot + @"\shell\open\command", "", command, AssociationValueKind.String),
            new(OpenWith, ProgId, Array.Empty<byte>(), AssociationValueKind.None),
            new(RegisteredApps, name, CapabilityPath, AssociationValueKind.String)
        ];
        foreach (var extension in SpreadsheetFormats.Extensions.Where(e => e != ".xlsx"))
        {
            values.Add(new(CapabilityPath + @"\FileAssociations", extension, ProgId, AssociationValueKind.String));
            values.Add(new(ExecutableRoot + @"\SupportedTypes", extension, "", AssociationValueKind.String));
            values.Add(new(@"Software\Classes\" + extension + @"\OpenWithProgids", ProgId, Array.Empty<byte>(), AssociationValueKind.None));
        }
        return values;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAssociationRegistration(RegistryKey userRoot)
{
    private static RegistryValueKind Kind(AssociationValueKind value) => value == AssociationValueKind.String ? RegistryValueKind.String : RegistryValueKind.None;
    private static bool Equal(object? a, object b) => a is byte[] aa && b is byte[] bb ? aa.SequenceEqual(bb) : Equals(a, b);
    private static bool Matches(RegistryKey key, WindowsAssociationPlan.Value value) => key.GetValueNames().Contains(value.Name, StringComparer.OrdinalIgnoreCase)
        && key.GetValueKind(value.Name) == Kind(value.Kind) && Equal(key.GetValue(value.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), value.Data);
    public void Register(string exe)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException("Publish launcher first.");
        var full = Path.GetFullPath(exe);
        if (new Uri(full).IsUnc || new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed) throw new NotSupportedException("Install launcher on a fixed local drive.");
        for (var path = full; path is not null; path = Path.GetDirectoryName(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new NotSupportedException("Redirected launcher installation excluded.");
        var plan = WindowsAssociationPlan.Values(exe); var old = new Dictionary<string, IReadOnlyList<WindowsAssociationPlan.Value>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in WindowsAssociationPlan.OwnedRoots)
        {
            using var key = userRoot.OpenSubKey(root);
            if (key is null) continue;
            if (!Equals(key.GetValue("SW_Owner"), WindowsAssociationPlan.Owner)) throw new LocalConflictException("Reserved registry key is not owned by this app.");
            if (key.GetValue("SW_Executable") is string prior)
            {
                if (!string.Equals(prior, Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) throw new LocalConflictException("Unregister before moving the executable.");
                old[root] = WindowsAssociationPlan.Values(prior, legacy: true).Concat(WindowsAssociationPlan.Values(prior, legacyIcon: true)).ToArray();
            }
        }
        foreach (var value in plan)
        {
            using var key = userRoot.OpenSubKey(value.Key);
            if (key is null || !key.GetValueNames().Contains(value.Name, StringComparer.OrdinalIgnoreCase) || Matches(key, value)) continue;
            var ownerRoot = WindowsAssociationPlan.OwnedRoots.FirstOrDefault(r => value.Key == r || value.Key.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase));
            var previous = ownerRoot is not null && old.TryGetValue(ownerRoot, out var priorPlan) ? priorPlan.FirstOrDefault(v => v.Key == value.Key && v.Name == value.Name && Matches(key, v)) : null;
            if (previous is null || !Matches(key, previous)) throw new LocalConflictException("Registry value changed; registration blocked.");
        }
        using (var apps = userRoot.OpenSubKey(WindowsAssociationPlan.RegisteredApps))
        {
            if (apps is not null && apps.GetValueNames().Contains(WindowsAssociationPlan.LegacyName)
                && (!old.ContainsKey(WindowsAssociationPlan.AppRoot)
                    || apps.GetValueKind(WindowsAssociationPlan.LegacyName) != RegistryValueKind.String
                    || !Equals(apps.GetValue(WindowsAssociationPlan.LegacyName), WindowsAssociationPlan.CapabilityPath)))
                throw new LocalConflictException("Legacy registered app was modified.");
        }
        // Registry writes are not a distributed transaction; ownership is written first, allowing safe re-registration after interruption.
        foreach (var value in plan)
        {
            using var key = userRoot.CreateSubKey(value.Key, writable: true); key.SetValue(value.Name, value.Data, Kind(value.Kind)); key.Flush();
        }
        // Retire only the exact legacy alias after the new registration has been published.
        using var registered = userRoot.OpenSubKey(WindowsAssociationPlan.RegisteredApps, writable: true);
        if (registered is not null && Equals(registered.GetValue(WindowsAssociationPlan.LegacyName), WindowsAssociationPlan.CapabilityPath))
            registered.DeleteValue(WindowsAssociationPlan.LegacyName, false);
    }
    public void Unregister()
    {
        using var main = userRoot.OpenSubKey(WindowsAssociationPlan.AppRoot);
        if (main is null) return;
        if (!Equals(main.GetValue("SW_Owner"), WindowsAssociationPlan.Owner)) throw new LocalConflictException("Registration is not owned by this app.");
        var exe = main.GetValue("SW_Executable") as string ?? throw new LocalConflictException("Incomplete registration; re-register first.");
        var plan = WindowsAssociationPlan.Values(exe).Concat(WindowsAssociationPlan.Values(exe, legacy: true)).Concat(WindowsAssociationPlan.Values(exe, legacyIcon: true)).ToArray();
        foreach (var root in WindowsAssociationPlan.OwnedRoots)
        {
            using var key = userRoot.OpenSubKey(root);
            if (key is not null && !Equals(key.GetValue("SW_Owner"), WindowsAssociationPlan.Owner)) throw new LocalConflictException("Foreign registration preserved.");
        }
        foreach (var value in plan.Reverse())
        {
            using var key = userRoot.OpenSubKey(value.Key, writable: true);
            if (key is not null && Matches(key, value)) key.DeleteValue(value.Name, throwOnMissingValue: false);
        }
        foreach (var root in WindowsAssociationPlan.OwnedRoots)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in plan.Where(v => v.Key == root || v.Key.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)))
                for (var path = value.Key; ; path = path[..path.LastIndexOf('\\')])
                {
                    paths.Add(path); if (path == root) break;
                }
            foreach (var path in paths.OrderByDescending(p => p.Length)) RemoveEmpty(path);
        }
    }
    private void RemoveEmpty(string path)
    {
        using (var key = userRoot.OpenSubKey(path))
            if (key is null || key.ValueCount != 0 || key.SubKeyCount != 0) return;
        userRoot.DeleteSubKey(path, throwOnMissingSubKey: false);
    }
    public static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
