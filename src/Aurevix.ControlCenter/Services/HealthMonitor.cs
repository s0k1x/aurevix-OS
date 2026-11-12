using System.IO;
using System.Management;
using Aurevix.Core.Logging;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// Vigilante de salud en segundo plano: disco lleno o con avisos, CPU muy caliente y driver de gráfica antiguo.
/// Cada tipo de aviso se muestra como mucho una vez cada 24 h.
/// </summary>
public static class HealthMonitor
{
    /// <summary>(título, texto, página a abrir: "cleanup" | "diagnostics" | "tools"). Lo conecta la bandeja.</summary>
    public static Action<string, string, string?>? Notify { get; set; }

    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(24);

    private static Timer? _timer;
    private static int _running;
    private static int _hotReadings;

    public static void Start()
    {
        if (_timer is not null) return;
        _timer = new Timer(_ => Task.Run(RunOnce), null, FirstDelay, Interval);
    }

    private static async Task RunOnce()
    {
        // Evita solapes si una comprobación tarda más que el intervalo.
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            if (!AppServices.Settings.HealthAlerts) return;
            Safe(CheckDiskSpace);
            await SafeAsync(CheckDiskHealth);
            Safe(CheckCpuTemperature);
            Safe(CheckGpuDriver);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    private static void Safe(Action a)
    {
        try { a(); } catch (Exception ex) { AurevixLog.Warn($"Vigilante de salud: {ex.Message}"); }
    }

    private static async Task SafeAsync(Func<Task> a)
    {
        try { await a(); } catch (Exception ex) { AurevixLog.Warn($"Vigilante de salud: {ex.Message}"); }
    }

    private static void CheckDiskSpace()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root)) return;
        var d = new DriveInfo(root);
        if (d.TotalSize <= 0) return;
        var free = d.AvailableFreeSpace;
        var pct = free * 100.0 / d.TotalSize;
        if (pct >= 10 && free >= 15_000_000_000L) return;
        var letter = root.TrimEnd('\\', '/');
        Raise("disk-space", "Disco casi lleno",
            $"Tu disco {letter} está casi lleno ({free / 1_000_000_000} GB libres). Abre Limpieza para liberar espacio.", "cleanup");
    }

    private static async Task CheckDiskHealth()
    {
        var checks = await HardwareChecks.RunAsync();
        foreach (var c in checks)
        {
            const string prefix = "Salud del disco ";
            if (c.Status != "review" || !c.Title.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var name = c.Title[prefix.Length..];
            var paren = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) name = name[..paren];
            Raise("disk-health", "Aviso de salud del disco",
                $"El disco {name} da avisos de salud. Haz copia de seguridad de tus archivos.", "diagnostics");
            return;
        }
    }

    private static void CheckCpuTemperature()
    {
        var t = ReadCpuTemperature();
        if (t is null || t.Value <= 90) { _hotReadings = 0; return; }
        // Sostenida: dos lecturas seguidas por encima de 90 °C.
        if (++_hotReadings < 2) return;
        Raise("cpu-temp", "CPU muy caliente",
            $"La CPU está muy caliente ({t.Value:0} °C). Revisa la ventilación.", "diagnostics");
    }

    /// <summary>Zona térmica ACPI; solo se aceptan valores plausibles (WMI no es fiable en todos los equipos).</summary>
    private static double? ReadCpuTemperature()
    {
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"root\WMI"),
                new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"));
            double? max = null;
            foreach (var o in s.Get().Cast<ManagementBaseObject>())
            {
                var c = Convert.ToDouble(o["CurrentTemperature"] ?? 0) / 10.0 - 273.15;
                if (c is >= 20 and <= 120) max = Math.Max(max ?? c, c);
            }
            return max;
        }
        catch { return null; }
    }

    private static void CheckGpuDriver()
    {
        using var s = new ManagementObjectSearcher("SELECT Name, DriverDate FROM Win32_VideoController");
        foreach (var o in s.Get().Cast<ManagementBaseObject>())
        {
            var name = $"{o["Name"]}".Trim();
            if (name.Length == 0 || name.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase)) continue;
            if (o["DriverDate"] is not string raw) continue;
            var date = ManagementDateTimeConverter.ToDateTime(raw);
            if (date >= DateTime.Now.AddMonths(-12)) continue;
            Raise("gpu-driver", "Driver de la gráfica antiguo",
                $"El driver de tu gráfica ({name}) tiene más de un año. Actualízalo para ganar rendimiento.", "tools");
            return;
        }
    }

    private static void Raise(string kind, string title, string text, string? page)
    {
        var st = AppServices.State;
        var now = DateTime.UtcNow;
        lock (st)
        {
            if (st.HealthAlertLastUtc.TryGetValue(kind, out var last) && now - last < Cooldown) return;
            st.HealthAlertLastUtc[kind] = now;
        }
        try { AppServices.SaveState(); } catch { /* no es crítico */ }

        var notify = Notify;
        if (notify is null || !AppServices.Settings.Notifications)
        {
            AurevixLog.Info($"Aviso de salud ({kind}): {text}");
            return;
        }
        notify(title, text, page);
    }
}
