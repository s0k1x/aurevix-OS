using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Aurevix.Core;
using Aurevix.Core.Logging;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

public sealed class GameBoostSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Ejecutables de juegos (p. ej. "cs2.exe").</summary>
    public List<string> Games { get; set; } = [];
    /// <summary>Procesos que bajan a prioridad baja y liberan memoria mientras juegas.</summary>
    public List<string> Background { get; set; } =
    [
        "OneDrive.exe", "ms-teams.exe", "Teams.exe", "PhoneExperienceHost.exe", "YourPhone.exe",
        "Widgets.exe", "WidgetService.exe", "Copilot.exe", "SearchHost.exe", "CompPkgSrv.exe"
    ];
    /// <summary>Aplicaciones que se cierran (de forma amable, como al pulsar la X) al empezar a jugar. Vacío por defecto.</summary>
    public List<string> CloseOnStart { get; set; } = [];
    public bool SwitchPowerPlan { get; set; } = true;
    public bool TrimMemory { get; set; } = true;
    /// <summary>Vaciar la memoria en espera al empezar a jugar (como ISLC), mediante AUREVIX Core Service.</summary>
    public bool PurgeStandby { get; set; } = true;
    /// <summary>Apps de streaming/grabación: nunca se apartan y suben a prioridad «por encima de lo normal».</summary>
    public List<string> Streaming { get; set; } = ["obs64.exe", "obs32.exe", "Streamlabs OBS.exe", "Medal.exe"];
    /// <summary>Ruta completa de cada juego (para los ajustes por juego de Windows).</summary>
    public Dictionary<string, string> GamePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Ajustes por juego con las opciones oficiales de Windows (los mismos que en Configuración > Pantalla > Gráficos
/// y en Propiedades > Compatibilidad). Se activan y desactivan por separado.
/// </summary>
public static class PerGameTweaks
{
    private const string GpuKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string CompatKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    public static bool HighPerfGpu(string path) =>
        (Microsoft.Win32.Registry.CurrentUser.OpenSubKey(GpuKey)?.GetValue(path) as string)?.Contains("GpuPreference=2") == true;

    public static void SetHighPerfGpu(string path, bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(GpuKey, true);
        if (on) k.SetValue(path, "GpuPreference=2;"); else k.DeleteValue(path, false);
    }

    public static bool NoFso(string path) =>
        (Microsoft.Win32.Registry.CurrentUser.OpenSubKey(CompatKey)?.GetValue(path) as string)?.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE") == true;

    public static void SetNoFso(string path, bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(CompatKey, true);
        var cur = (k.GetValue(path) as string ?? "").Replace("~", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(f => f != "DISABLEDXMAXIMIZEDWINDOWEDMODE").ToList();
        if (on) cur.Add("DISABLEDXMAXIMIZEDWINDOWEDMODE");
        if (cur.Count == 0) k.DeleteValue(path, false); else k.SetValue(path, "~ " + string.Join(' ', cur));
    }
}


/// <summary>
/// AUREVIX Game Boost: al detectar un juego de la lista, prioriza el juego y aparta lo demás. Todo se restaura al cerrarlo.
/// Solo usa APIs de proceso del usuario (sin privilegios). No mata procesos del sistema.
/// </summary>
public static class GameBoost
{
    private static string File_ => Path.Combine(AurevixPaths.UserRoot, "gameboost.json");
    public static GameBoostSettings Settings { get; private set; } = new();
    public static string? ActiveGame { get; private set; }
    public static event Action<string>? Notify;

    private static System.Threading.Timer? _timer;
    private static readonly Dictionary<int, ProcessPriorityClass> Lowered = new();
    private static Guid? _previousPlan;
    private static int _busy;

    public static void Load() => Settings = JsonStore.LoadOrDefault<GameBoostSettings>(File_);
    public static void Save() => JsonStore.Save(File_, Settings);

    public static void Start()
    {
        Load();
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(3));
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (!Settings.Enabled || Settings.Games.Count == 0) { if (ActiveGame is not null) Restore(); return; }
            if (ActiveGame is null)
            {
                foreach (var g in Settings.Games)
                {
                    var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(g));
                    if (procs.Length == 0) continue;
                    Boost(g, procs);
                    foreach (var p in procs) p.Dispose();
                    break;
                }
            }
            else if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ActiveGame)).Length == 0)
                Restore();
        }
        catch (Exception ex) { AurevixLog.Error("Game Boost", ex); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private static void Boost(string game, Process[] gameProcs)
    {
        ActiveGame = game;
        foreach (var p in gameProcs)
            try { p.PriorityClass = ProcessPriorityClass.High; } catch { }

        var me = Environment.ProcessId;
        foreach (var name in Settings.Background.Except(Settings.Streaming, StringComparer.OrdinalIgnoreCase))
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
            {
                try
                {
                    if (p.Id == me || p.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    Lowered[p.Id] = p.PriorityClass;
                    p.PriorityClass = ProcessPriorityClass.Idle;
                    if (Settings.TrimMemory) EmptyWorkingSet(p.Handle);
                }
                catch { }
                finally { p.Dispose(); }
            }

        foreach (var name in Settings.Streaming)
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
            {
                try { Lowered[p.Id] = p.PriorityClass; p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
                finally { p.Dispose(); }
            }

        if (Settings.PurgeStandby)
            _ = CoreServiceClient.SendAsync(new CoreRequest { Op = "maintenance", Action = "purge-standby" });

        foreach (var name in Settings.CloseOnStart)
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
            {
                try { p.CloseMainWindow(); } catch { }
                finally { p.Dispose(); }
            }

        if (Settings.SwitchPowerPlan)
        {
            try
            {
                var power = new PowercfgPowerPlanAccess();
                _previousPlan = power.GetActivePlan();
                var target = AppServices.Hardware.IsLaptop ? PowerPlans.HighPerformance : PowerPlans.Ultimate;
                power.SetActivePlan(power.EnsurePlan(target));
            }
            catch (Exception ex) { AurevixLog.Warn($"Game Boost: plan de energía: {ex.Message}"); }
        }
        AurevixLog.Info($"Game Boost activado para {game}: {Lowered.Count} procesos en segundo plano apartados.");
        Notify?.Invoke($"Game Boost activado para {game}");
    }

    public static void Restore()
    {
        foreach (var (pid, prio) in Lowered)
        {
            try { using var p = Process.GetProcessById(pid); p.PriorityClass = prio; } catch { }
        }
        Lowered.Clear();
        if (_previousPlan is { } plan)
        {
            try { new PowercfgPowerPlanAccess().SetActivePlan(plan); } catch { }
            _previousPlan = null;
        }
        AurevixLog.Info($"Game Boost desactivado ({ActiveGame}). Todo restaurado.");
        Notify?.Invoke("Game Boost desactivado: todo restaurado");
        ActiveGame = null;
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
}
