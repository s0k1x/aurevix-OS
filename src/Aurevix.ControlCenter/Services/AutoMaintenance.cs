using System.IO;
using System.Runtime.InteropServices;
using Aurevix.Core.Logging;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// Mantenimiento automático semanal: limpia temporales (la papelera no), optimiza las unidades (TRIM)
/// y comprueba la imagen de Windows (DISM CheckHealth). Solo se ejecuta si el usuario no está usando el PC,
/// no hay juego activo y no se va con batería baja. No toca archivos personales.
/// </summary>
public static class AutoMaintenance
{
    /// <summary>Texto claro para la bandeja (solo se usa si Settings.Notifications).</summary>
    public static Action<string>? Notify { get; set; }
    /// <summary>Se dispara al terminar un mantenimiento (para refrescar la interfaz).</summary>
    public static event Action? Changed;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Weekly = TimeSpan.FromDays(7);
    private static readonly TimeSpan FirstRunGrace = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IdleNeeded = TimeSpan.FromMinutes(10);

    private static System.Threading.Timer? _timer;
    private static int _busy;
    private static readonly DateTime StartedUtc = DateTime.UtcNow;
    /// <summary>Hora del último intento (si todo falló, no se reintenta hasta pasadas 24 h).</summary>
    private static DateTime? _lastAttemptUtc;

    public static void Start()
    {
        _timer ??= new System.Threading.Timer(_ => Task.Run(TickAsync), null, Interval, Interval);
    }

    private static async Task TickAsync()
    {
        try
        {
            if (!ShouldRun()) return;
            await RunNowAsync();
        }
        catch (Exception ex) { AurevixLog.Warn("Mantenimiento automático: " + ex.Message); }
    }

    private static bool ShouldRun()
    {
        var s = AppServices.Settings;
        if (!s.AutoMaintenance) return false;
        var now = DateTime.UtcNow;
        var last = AppServices.State.LastAutoMaintenanceUtc;
        if (last is null ? now - StartedUtc < FirstRunGrace : now - last.Value < Weekly) return false;
        if (_lastAttemptUtc is { } att && now - att < TimeSpan.FromHours(24)) return false;
        if (s.AutoMaintenanceDay is >= 1 and <= 7)
        {
            var today = DateTime.Now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)DateTime.Now.DayOfWeek;
            if (today != s.AutoMaintenanceDay) return false;
        }
        if (GameBoost.ActiveGame is not null) return false;
        if (IdleTime() < IdleNeeded) return false;
        if (LowBattery()) return false;
        return true;
    }

    /// <summary>Hace el mantenimiento ya (botón «Hacer ahora»). Devuelve el resumen, o null si ya había uno en marcha.</summary>
    public static async Task<string?> RunNowAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return null;
        try
        {
            var parts = new List<string>();

            var freed = await Task.Run(CleanTemp);
            parts.Add($"Liberados {FormatSize(freed)}");
            var anyOk = freed > 0;

            var trim = await CoreServiceClient.SendAsync(new CoreRequest { Op = "maintenance", Action = "optimize-drives" }, TimeSpan.FromMinutes(40));
            parts.Add(trim.Ok ? "TRIM hecho" : "TRIM no disponible");
            anyOk |= trim.Ok;

            var dism = await CoreServiceClient.SendAsync(new CoreRequest { Op = "maintenance", Action = "dism-checkhealth" }, TimeSpan.FromMinutes(10));
            parts.Add(dism.Ok ? "Windows sano" : "Windows sin comprobar");
            anyOk |= dism.Ok;

            var summary = string.Join(" · ", parts);
            var st = AppServices.State;
            _lastAttemptUtc = DateTime.UtcNow;
            // Solo cuenta como hecho si al menos un paso fue bien.
            if (anyOk)
            {
                st.LastAutoMaintenanceUtc = DateTime.UtcNow;
                st.LastAutoMaintenanceSummary = summary;
                try { AppServices.SaveState(); } catch { /* no es crítico */ }
            }
            AurevixLog.Info("Mantenimiento automático: " + summary);
            if (AppServices.Settings.Notifications) Notify?.Invoke(summary);
            Changed?.Invoke();
            return summary;
        }
        catch (Exception ex)
        {
            AurevixLog.Warn("Mantenimiento automático: " + ex.Message);
            return null;
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>Borra temporales del usuario con más de 2 h. No toca la papelera ni documentos.</summary>
    private static long CleanTemp()
    {
        long freed = 0;
        try
        {
            freed = CleanDir(Path.GetTempPath(), DateTime.UtcNow.AddDays(-7));
        }
        catch (Exception ex) { AurevixLog.Warn("Mantenimiento automático (temporales): " + ex.Message); }
        return freed;
    }

    /// <summary>Borra archivos con escritura y acceso anteriores al límite; no entra en carpetas recientes.</summary>
    private static long CleanDir(string dir, DateTime limitUtc)
    {
        long freed = 0;
        var opts = new EnumerationOptions { IgnoreInaccessible = true };
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", opts))
            {
                try
                {
                    var info = new FileInfo(f);
                    if (info.LastWriteTimeUtc > limitUtc || info.LastAccessTimeUtc > limitUtc) continue;
                    var len = info.Length;
                    info.Delete();
                    freed += len;
                }
                catch { /* en uso o sin permiso */ }
            }
            foreach (var d in Directory.EnumerateDirectories(dir, "*", opts))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(d) > limitUtc) continue;
                    freed += CleanDir(d, limitUtc);
                }
                catch { }
            }
        }
        catch { }
        return freed;
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.0} GB" : $"{bytes / 1e6:0} MB";

    private static bool LowBattery()
    {
        try
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) return false;
            return ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline && ps.BatteryLifePercent < 0.5f;
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static TimeSpan IdleTime()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.dwTime);
        }
        catch { return TimeSpan.Zero; }
    }
}
