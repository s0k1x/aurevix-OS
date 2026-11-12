using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// Foto del estado del equipo en un momento dado (procesos, servicios, memoria, inicio...).
/// Cada campo es null si no se ha podido medir: nunca lanza excepciones.
/// </summary>
public sealed record SystemSnapshot(
    DateTime CapturedAtUtc,
    int? Processes,
    int? ServicesRunning,
    double? RamUsedGb,
    int? ScheduledTasksActive,
    int? StartupApps,
    double? BootSeconds)
{
    /// <summary>Toma la foto en segundo plano, sin bloquear la interfaz.</summary>
    public static Task<SystemSnapshot> CaptureAsync() => Task.Run(async () =>
    {
        // Lo lento (procesos externos) se lanza a la vez.
        var services = ServicesAsync();
        var tasks = TasksAsync();
        var boot = BootAsync();

        var procs = Safe(() =>
        {
            var all = Process.GetProcesses();
            var n = all.Length;
            foreach (var p in all) p.Dispose();
            return (int?)n;
        });
        var ram = Safe(() =>
        {
            var (used, total, _) = SystemTools.Memory();
            return total > 0 ? (double?)Math.Round(used, 2) : null;
        });
        var startup = Safe(StartupCount);

        return new SystemSnapshot(DateTime.UtcNow, procs, await services, ram, await tasks, startup, await boot);
    });

    private static T? Safe<T>(Func<T?> f)
    {
        try { return f(); } catch { return default; }
    }

    private static int? StartupCount()
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        var n = 0;
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = root.OpenSubKey(key);
                if (k is not null) n += k.GetValueNames().Count(v => v.Length > 0);
            }
            catch { /* sin acceso a esa rama: se ignora */ }
        }
        return n;
    }

    private static async Task<int?> ServicesAsync()
    {
        var o = await RunAsync("sc.exe", "query type= service", 15000);
        if (o is null) return null;
        var n = Regex.Matches(o, @"^\s*\w+\s*:\s*4\s+RUNNING", RegexOptions.Multiline | RegexOptions.IgnoreCase).Count;
        return n > 0 ? n : null;
    }

    private static async Task<int?> TasksAsync()
    {
        var o = await RunAsync("schtasks.exe", "/query /fo csv /nh", 15000);
        if (o is null) return null;
        var n = 0;
        foreach (var raw in o.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('"')) continue;
            var status = line.Split("\",\"").Last().Trim('"', ' ');
            if (status.Equals("Deshabilitado", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) continue;
            n++;
        }
        return n > 0 ? n : null;
    }

    /// <summary>Último arranque medido por Windows (evento 100). Suele pedir administrador: si no se puede, null.</summary>
    private static async Task<double?> BootAsync()
    {
        var o = await RunAsync("wevtutil.exe",
            "qe Microsoft-Windows-Diagnostics-Performance/Operational \"/q:*[System[(EventID=100)]]\" /c:1 /rd:true /f:xml", 15000);
        if (o is null) return null;
        var m = Regex.Match(o, @"Name=['""]BootTime['""]\s*>\s*(\d+)\s*<");
        return m.Success && double.TryParse(m.Groups[1].Value, out var ms) && ms > 0 ? Math.Round(ms / 1000.0, 1) : null;
    }

    /// <summary>Ejecuta un programa oculto y devuelve su salida; null si falla, da error o pasa del tiempo.</summary>
    private static async Task<string?> RunAsync(string exe, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var cts = new CancellationTokenSource(timeoutMs);
            var outTask = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return null;
            }
            return p.ExitCode == 0 ? await outTask : null;
        }
        catch { return null; }
    }
}
