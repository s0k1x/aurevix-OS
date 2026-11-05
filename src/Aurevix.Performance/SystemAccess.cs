using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Aurevix.Performance;

/// <summary>Acceso al registro abstraído para poder probar el motor sin Windows.</summary>
public interface IRegistryAccess
{
    /// <summary>Devuelve el valor como texto o null si no existe.</summary>
    string? GetValue(RegistryHive hive, string key, string name);
    void SetValue(RegistryHive hive, string key, string name, RegistryValueKind kind, string value);
    void DeleteValue(RegistryHive hive, string key, string name);
}

public interface IPowerPlanAccess
{
    IReadOnlyList<(Guid Id, string Name)> ListPlans();
    Guid? GetActivePlan();
    void SetActivePlan(Guid id);
}

public static class PowerPlans
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3519-4b5f-a6a9-5dc8bf2bd8b6");
}

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    private static Microsoft.Win32.RegistryKey Root(RegistryHive h) =>
        h == RegistryHive.CurrentUser ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;

    public string? GetValue(RegistryHive hive, string key, string name)
    {
        using var k = Root(hive).OpenSubKey(key);
        return k?.GetValue(name)?.ToString();
    }

    public void SetValue(RegistryHive hive, string key, string name, RegistryValueKind kind, string value)
    {
        using var k = Root(hive).CreateSubKey(key, writable: true);
        if (kind == RegistryValueKind.DWord)
            k.SetValue(name, int.Parse(value), Microsoft.Win32.RegistryValueKind.DWord);
        else
            k.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
    }

    public void DeleteValue(RegistryHive hive, string key, string name)
    {
        using var k = Root(hive).OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>Usa powercfg.exe (herramienta oficial de Windows). Cambiar entre planes existentes no requiere administrador.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class PowercfgPowerPlanAccess : IPowerPlanAccess
{
    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((.+?)\)(\s*\*)?")]
    private static partial Regex PlanLine();

    public IReadOnlyList<(Guid Id, string Name)> ListPlans() =>
        PlanLine().Matches(Run("/list")).Select(m => (Guid.Parse(m.Groups[1].Value), m.Groups[2].Value)).ToList();

    public Guid? GetActivePlan()
    {
        var m = PlanLine().Match(Run("/getactivescheme"));
        return m.Success ? Guid.Parse(m.Groups[1].Value) : null;
    }

    public void SetActivePlan(Guid id) => Run($"/setactive {id}");

    private static string Run(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10000);
        if (p.ExitCode != 0) throw new InvalidOperationException($"powercfg {args} terminó con código {p.ExitCode}");
        return output;
    }
}
