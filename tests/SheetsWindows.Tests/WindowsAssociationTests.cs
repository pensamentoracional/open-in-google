using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SheetsWindows.Core;
using SheetsWindows.Infrastructure;
using Xunit;

namespace SheetsWindows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsAssociationTests
{
    [WindowsFact]
    public void LegacyNameMigratesOnlyOwnedValuesAndPreservesDefaults()
    {
        using var f = new Hive();
        foreach (var value in WindowsAssociationPlan.Values(f.Exe, legacy: true))
        { using var key = f.Root.CreateSubKey(value.Key); key.SetValue(value.Name, value.Data, value.Kind == AssociationValueKind.String ? RegistryValueKind.String : RegistryValueKind.None); }
        using (var ext = f.Root.CreateSubKey(@"Software\Classes\.xlsx")) ext.SetValue("", "Excel.Sheet.12");
        var registration = new WindowsAssociationRegistration(f.Root); registration.Register(f.Exe); registration.Register(f.Exe);
        using (var apps = f.Root.OpenSubKey(WindowsAssociationPlan.RegisteredApps)) { Assert.Equal(WindowsAssociationPlan.CapabilityPath, apps!.GetValue("ZagoSheetsWin")); Assert.Null(apps.GetValue("Sheets Windows")); }
        using (var capability = f.Root.OpenSubKey(WindowsAssociationPlan.CapabilityPath)) Assert.Equal("ZagoSheetsWin", capability!.GetValue("ApplicationName"));
        registration.Unregister(); using var after = f.Root.OpenSubKey(@"Software\Classes\.xlsx"); Assert.Equal("Excel.Sheet.12", after!.GetValue(""));
    }
    [WindowsFact]
    public void ModifiedLegacyNameIsPreservedAndBlocksMigration()
    {
        using var f = new Hive();
        foreach (var value in WindowsAssociationPlan.Values(f.Exe, legacy: true))
        { using var key = f.Root.CreateSubKey(value.Key); key.SetValue(value.Name, value.Data, value.Kind == AssociationValueKind.String ? RegistryValueKind.String : RegistryValueKind.None); }
        using (var key = f.Root.OpenSubKey(WindowsAssociationPlan.CapabilityPath, true)) key!.SetValue("ApplicationName", "Foreign name");
        Assert.Throws<LocalConflictException>(() => new WindowsAssociationRegistration(f.Root).Register(f.Exe));
        using var apps = f.Root.OpenSubKey(WindowsAssociationPlan.RegisteredApps); Assert.NotNull(apps!.GetValue("Sheets Windows")); Assert.Null(apps.GetValue("ZagoSheetsWin"));
    }
    [WindowsFact]
    public void RegistrationIsIdempotentAndPreservesExistingDefaults()
    {
        using var f = new Hive(); using (var ext = f.Root.CreateSubKey(@"Software\Classes\.xlsx")) ext.SetValue("", "Excel.Sheet.12");
        using (var choice = f.Root.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.xlsx\UserChoice")) choice.SetValue("ProgId", "Excel.Sheet.12");
        using (var other = f.Root.CreateSubKey(WindowsAssociationPlan.OpenWith)) other.SetValue("Other.Xlsx", Array.Empty<byte>(), RegistryValueKind.None);
        var r = new WindowsAssociationRegistration(f.Root); r.Register(f.Exe); r.Register(f.Exe);
        using (var command = f.Root.OpenSubKey(WindowsAssociationPlan.ProgRoot + @"\shell\open\command")) Assert.Equal(WindowsAssociationPlan.Command(f.Exe), command!.GetValue(""));
        r.Unregister(); r.Unregister();
        using var extAfter = f.Root.OpenSubKey(@"Software\Classes\.xlsx"); Assert.Equal("Excel.Sheet.12", extAfter!.GetValue(""));
        using var choiceAfter = f.Root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.xlsx\UserChoice"); Assert.Equal("Excel.Sheet.12", choiceAfter!.GetValue("ProgId"));
        using var others = f.Root.OpenSubKey(WindowsAssociationPlan.OpenWith); Assert.Contains("Other.Xlsx", others!.GetValueNames()); Assert.DoesNotContain(WindowsAssociationPlan.ProgId, others.GetValueNames());
        Assert.Null(f.Root.OpenSubKey(WindowsAssociationPlan.AppRoot));
    }
    [WindowsFact]
    public void ForeignKeysBlockRegistrationBeforeAnyWrites()
    {
        using var f = new Hive(); using (var foreign = f.Root.CreateSubKey(WindowsAssociationPlan.ProgRoot)) foreign.SetValue("", "foreign");
        Assert.Throws<LocalConflictException>(() => new WindowsAssociationRegistration(f.Root).Register(f.Exe)); Assert.Null(f.Root.OpenSubKey(WindowsAssociationPlan.AppRoot));
    }
    [WindowsFact]
    public void UnregistrationPreservesForeignAdditionsAndModifiedCommands()
    {
        using var f = new Hive(); var registration = new WindowsAssociationRegistration(f.Root); registration.Register(f.Exe);
        using (var foreign = f.Root.CreateSubKey(WindowsAssociationPlan.AppRoot + @"\Other")) foreign.SetValue("keep", "yes");
        using (var empty = f.Root.CreateSubKey(WindowsAssociationPlan.AppRoot + @"\OtherEmpty")) { }
        using (var modified = f.Root.CreateSubKey(WindowsAssociationPlan.ProgRoot + @"\shell\open\command")) modified.SetValue("", "foreign command");
        registration.Unregister();
        using var emptyAfter = f.Root.OpenSubKey(WindowsAssociationPlan.AppRoot + @"\OtherEmpty"); Assert.NotNull(emptyAfter);
        using var preserved = f.Root.OpenSubKey(WindowsAssociationPlan.AppRoot + @"\Other"); Assert.Equal("yes", preserved!.GetValue("keep"));
        using var command = f.Root.OpenSubKey(WindowsAssociationPlan.ProgRoot + @"\shell\open\command"); Assert.Equal("foreign command", command!.GetValue(""));
    }
    [WindowsFact]
    public void MovingExecutableRequiresExplicitUnregistration()
    {
        using var f = new Hive(); var registration = new WindowsAssociationRegistration(f.Root); registration.Register(f.Exe);
        var moved = Path.Combine(f.Files.Root, "moved", "SheetsWindows.exe"); Directory.CreateDirectory(Path.GetDirectoryName(moved)!); File.WriteAllBytes(moved, [1]);
        Assert.Throws<LocalConflictException>(() => registration.Register(moved));
        using var command = f.Root.OpenSubKey(WindowsAssociationPlan.ProgRoot + @"\shell\open\command"); Assert.Equal(WindowsAssociationPlan.Command(f.Exe), command!.GetValue(""));
    }
    [WindowsFact]
    public void PartialOwnedRegistrationCanResume()
    {
        using var f = new Hive(); using (var partial = f.Root.CreateSubKey(WindowsAssociationPlan.AppRoot)) partial.SetValue("SW_Owner", WindowsAssociationPlan.Owner);
        new WindowsAssociationRegistration(f.Root).Register(f.Exe);
        using var registered = f.Root.OpenSubKey(WindowsAssociationPlan.RegisteredApps); Assert.Equal(WindowsAssociationPlan.CapabilityPath, registered!.GetValue(WindowsAssociationPlan.AppName));
    }
    [WindowsFact]
    public void ShellCommandRoundTripsUnicodeSpacesAndMetacharactersAsOneArgument()
    {
        using var f = new Hive(); var file = Path.Combine(f.Files.Root, "Relatório & [2026] %foo%.xlsx");
        var command = WindowsAssociationPlan.Command(f.Exe).Replace("%1", file, StringComparison.Ordinal);
        var pointer = CommandLineToArgvW(command, out var count); Assert.NotEqual(IntPtr.Zero, pointer);
        try
        {
            var args = Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))).ToArray();
            Assert.Equal(new[] { f.Exe, "--open", file }, args);
        }
        finally { _ = LocalFree(pointer); }
    }
    private sealed class Hive : IDisposable
    {
        private readonly string name = @"Software\SheetsWindows.Tests\" + Guid.NewGuid().ToString("N");
        public Workspace Files { get; } = new();
        public RegistryKey Root { get; }
        public string Exe => Path.Combine(Files.Root, "App com espaço", "SheetsWindows.exe");
        public Hive() { Root = Registry.CurrentUser.CreateSubKey(name); Directory.CreateDirectory(Path.GetDirectoryName(Exe)!); File.WriteAllBytes(Exe, [1]); }
        public void Dispose() { Root.Dispose(); Registry.CurrentUser.DeleteSubKeyTree(name); Files.Dispose(); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
