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
    /// <summary>Subclaves directas (para acciones con comodín «\\*», p. ej. cada interfaz de red).</summary>
    IReadOnlyList<string> ListSubKeys(RegistryHive hive, string key);
    void DeleteKey(RegistryHive hive, string key);
}

public interface IPowerPlanAccess
{
    IReadOnlyList<(Guid Id, string Name)> ListPlans();
    Guid? GetActivePlan();
    void SetActivePlan(Guid id);
    /// <summary>Si el plan no existe (p. ej. Máximo rendimiento está oculto), lo crea con powercfg -duplicatescheme y devuelve su GUID.</summary>
    Guid EnsurePlan(Guid template);
}

public interface ITaskAccess
{
    /// <summary>null si la tarea no existe en esta versión de Windows.</summary>
    bool? IsEnabled(string path);
    void SetEnabled(string path, bool enabled);
}

public interface IPowerSettingAccess
{
    (uint Ac, uint Dc)? Get(Guid subgroup, Guid setting);
    void Set(Guid subgroup, Guid setting, uint ac, uint? dc);
}

public static class PowerPlans
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3519-4b5f-a6a9-5dc8bf2bd8b6");
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    /// <summary>GUID fijo para la copia AUREVIX de un plan oculto: así nunca se duplica dos veces.</summary>
    public static Guid DerivedFrom(Guid template)
    {
        var b = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("aurevix-plan:" + template));
        b[7] = (byte)((b[7] & 0x0F) | 0x50); // versión 5
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b.AsSpan(0, 16));
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    /// <summary>SID del usuario para RegistryHive.UserPolicy (lo fija el servicio con el SID de quien llama).</summary>
    public string? UserSid { get; init; }

    private (Microsoft.Win32.RegistryKey root, string key) Resolve(RegistryHive h, string key) => h switch
    {
        RegistryHive.CurrentUser => (Microsoft.Win32.Registry.CurrentUser, key),
        RegistryHive.LocalMachine => (Microsoft.Win32.Registry.LocalMachine, key),
        RegistryHive.UserPolicy when UserSid is not null => (Microsoft.Win32.Registry.Users, $@"{UserSid}\{key}"),
        RegistryHive.UserPolicy => (Microsoft.Win32.Registry.CurrentUser, key),
        _ => throw new ArgumentOutOfRangeException(nameof(h))
    };

    public string? GetValue(RegistryHive hive, string key, string name)
    {
        var (root, k) = Resolve(hive, key);
        using var sub = root.OpenSubKey(k);
        return sub?.GetValue(name)?.ToString();
    }

    public void SetValue(RegistryHive hive, string key, string name, RegistryValueKind kind, string value)
    {
        var (root, k) = Resolve(hive, key);
        using var sub = root.CreateSubKey(k, writable: true);
        if (kind == RegistryValueKind.DWord)
            sub.SetValue(name, unchecked((int)long.Parse(value)), Microsoft.Win32.RegistryValueKind.DWord);
        else
            sub.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
    }

    public void DeleteValue(RegistryHive hive, string key, string name)
    {
        var (root, k) = Resolve(hive, key);
        using var sub = root.OpenSubKey(k, writable: true);
        sub?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKey(RegistryHive hive, string key)
    {
        var (root, k) = Resolve(hive, key);
        root.DeleteSubKeyTree(k, throwOnMissingSubKey: false);
    }

    public IReadOnlyList<string> ListSubKeys(RegistryHive hive, string key)
    {
        var (root, k) = Resolve(hive, key);
        using var sub = root.OpenSubKey(k);
        return sub?.GetSubKeyNames() ?? [];
    }
}

/// <summary>Usa powercfg.exe (herramienta oficial de Windows).</summary>
[SupportedOSPlatform("windows")]
public sealed partial class PowercfgPowerPlanAccess : IPowerPlanAccess, IPowerSettingAccess
{
    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((.+?)\)")]
    private static partial Regex PlanLine();

    [GeneratedRegex(@"0x([0-9a-fA-F]{8})")]
    private static partial Regex Hex();

    public IReadOnlyList<(Guid Id, string Name)> ListPlans() =>
        PlanLine().Matches(Run("/list")).Select(m => (Guid.Parse(m.Groups[1].Value), m.Groups[2].Value)).ToList();

    public Guid? GetActivePlan()
    {
        var m = PlanLine().Match(Run("/getactivescheme"));
        return m.Success ? Guid.Parse(m.Groups[1].Value) : null;
    }

    public void SetActivePlan(Guid id) => Run($"/setactive {id}");

    public Guid EnsurePlan(Guid template)
    {
        var plans = ListPlans();
        if (plans.Any(p => p.Id == template)) return template;
        var dest = PowerPlans.DerivedFrom(template);
        if (plans.Any(p => p.Id == dest)) return dest;
        Run($"-duplicatescheme {template} {dest}");
        return dest;
    }

    /// <summary>Lee los índices AC/DC del plan activo. El texto de powercfg está traducido, así que solo se leen los dos últimos valores hexadecimales.</summary>
    public (uint Ac, uint Dc)? Get(Guid subgroup, Guid setting)
    {
        try
        {
            var hex = Hex().Matches(Run($"/query scheme_current {subgroup} {setting}")).Select(m => Convert.ToUInt32(m.Groups[1].Value, 16)).ToList();
            return hex.Count >= 2 ? (hex[^2], hex[^1]) : null;
        }
        catch { return null; }
    }

    public void Set(Guid subgroup, Guid setting, uint ac, uint? dc)
    {
        Run($"/setacvalueindex scheme_current {subgroup} {setting} {ac}");
        if (dc is { } d) Run($"/setdcvalueindex scheme_current {subgroup} {setting} {d}");
        Run("/setactive scheme_current");
    }

    private static string Run(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        if (p.ExitCode != 0) throw new InvalidOperationException($"powercfg {args} terminó con código {p.ExitCode}");
        return output;
    }
}

/// <summary>Tareas programadas mediante la API COM oficial del Programador de tareas (independiente del idioma).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTaskAccess : ITaskAccess
{
    private static dynamic? GetTask(string path)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Programador de tareas no disponible.");
        dynamic svc = Activator.CreateInstance(type)!;
        svc.Connect();
        var idx = path.LastIndexOf('\\');
        var folder = idx <= 0 ? "\\" : path[..idx];
        var name = path[(idx + 1)..];
        try { return svc.GetFolder(folder).GetTask(name); }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    public bool? IsEnabled(string path) => GetTask(path) is { } t ? (bool)t.Enabled : null;

    public void SetEnabled(string path, bool enabled)
    {
        var t = GetTask(path);
        if (t is null) return;
        t.Enabled = enabled;
    }
}
