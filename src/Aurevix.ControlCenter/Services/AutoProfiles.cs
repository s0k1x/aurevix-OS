using System.Diagnostics;
using System.IO;
using Aurevix.Core.Logging;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// Perfiles automáticos: cambia de perfil según lo que haces (jugar, ir con batería, trabajar).
/// Mapeo a los ids de profiles.json:
///   Juego   -> "gaming"        (el más parecido: máximo rendimiento)
///   Batería -> "efficiency"    (ahorro de energía)
///   Trabajo -> "productivity"  (herramientas de trabajo)
///   Ninguna regla -> el perfil que el usuario eligió a mano (o "balanced" si no había ninguno).
/// Es barato: cada 5 s mira procesos y estado de batería, sin WMI. Espera 15 s de estabilidad antes de cambiar.
/// </summary>
public static class AutoProfiles
{
    private const string GameProfile = "gaming", BatteryProfile = "efficiency", WorkProfile = "productivity", DefaultProfile = "balanced";
    private static readonly TimeSpan Stability = TimeSpan.FromSeconds(15);

    private static readonly string[] WorkProcesses =
        ["WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "Teams", "ms-teams", "Code", "devenv"];

    private static readonly string[] GameFolders =
        [@"\steamapps\common\", @"\Epic Games\", @"\Riot Games\", @"\XboxGames\"];

    /// <summary>Se llama al cambiar de perfil con un texto claro (para la bandeja).</summary>
    public static Action<string>? Notify { get; set; }
    /// <summary>Se dispara tras cada cambio automático, con el motivo.</summary>
    public static event Action<string>? Changed;
    /// <summary>Texto para la interfaz, p. ej. «perfil Juego (cs2.exe abierto)».</summary>
    public static string Status { get; private set; } = "sin actividad detectada";

    private static System.Threading.Timer? _timer;
    private static int _busy;
    private static string? _currentRule;      // regla aplicada ahora ("game", "battery", "work", "none")
    private static string? _candidate;        // regla vista en el último ciclo
    private static string _candidateReason = "";
    private static DateTime _candidateSince;
    private static string? _manualProfile;    // perfil elegido a mano por el usuario
    private static string? _lastAutoApplied;  // último perfil puesto por este módulo
    private static readonly Dictionary<int, bool> PathCache = new();

    public static void Start()
    {
        _timer ??= new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var s = AppServices.Settings;
            if (!s.AutoProfiles)
            {
                // Apagado: si habíamos cambiado algo, se vuelve al perfil manual.
                if (_currentRule is not null && _currentRule != "none") Evaluate("none", "modo automático desactivado", immediate: true);
                _currentRule = null; _candidate = null; _lastAutoApplied = null;
                Status = "desactivado";
                return;
            }

            // Si el usuario cambió de perfil a mano, ese pasa a ser su perfil de referencia.
            var active = AppServices.State.ActiveProfile;
            if (active != _lastAutoApplied) _manualProfile = active;

            var (rule, reason) = Detect(s);
            Evaluate(rule, reason, immediate: false);
        }
        catch (Exception ex) { AurevixLog.Error("Perfiles automáticos", ex); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private static (string Rule, string Reason) Detect(Aurevix.Core.AurevixSettings s)
    {
        Process[] procs = [];
        try
        {
            procs = Process.GetProcesses();
            if (s.AutoProfileGaming && FindGame(procs) is { } game) return ("game", $"{game} abierto");
            if (s.AutoProfileBattery && OnBattery()) return ("battery", "portátil con batería");
            if (s.AutoProfileWork && FindWork(procs) is { } work) return ("work", $"{work} abierto");
            return ("none", "ninguna regla aplica");
        }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    private static bool OnBattery()
    {
        try
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            return ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery
                && ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
        }
        catch { return false; }
    }

    private static string? FindWork(Process[] procs)
    {
        foreach (var p in procs)
            foreach (var w in WorkProcesses)
                if (string.Equals(p.ProcessName, w, StringComparison.OrdinalIgnoreCase)) return p.ProcessName + ".exe";
        return null;
    }

    private static string? FindGame(Process[] procs)
    {
        // 1) Lista de juegos de Game Boost (solo lectura).
        var known = GameBoost.Settings.Games;
        foreach (var g in known)
        {
            var name = Path.GetFileNameWithoutExtension(g);
            if (procs.Any(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase))) return g;
        }

        // 2) Carpetas típicas de tiendas de juegos (solo procesos con ventana, y con caché por proceso).
        var alive = new HashSet<int>();
        string? found = null;
        foreach (var p in procs)
        {
            alive.Add(p.Id);
            try
            {
                if (!PathCache.TryGetValue(p.Id, out var isGame))
                {
                    isGame = false;
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        var path = p.MainModule?.FileName ?? "";
                        isGame = GameFolders.Any(f => path.Contains(f, StringComparison.OrdinalIgnoreCase));
                    }
                    PathCache[p.Id] = isGame;
                }
                if (isGame && found is null) found = p.ProcessName + ".exe";
            }
            catch { PathCache[p.Id] = false; }
        }
        foreach (var id in PathCache.Keys.Where(k => !alive.Contains(k)).ToList()) PathCache.Remove(id);
        return found;
    }

    private static void Evaluate(string rule, string reason, bool immediate)
    {
        if (rule != _candidate) { _candidate = rule; _candidateSince = DateTime.UtcNow; }
        _candidateReason = reason;

        if (rule == _currentRule) { Status = Describe(rule, reason); return; }
        if (!immediate && DateTime.UtcNow - _candidateSince < Stability) return; // esperar a que se estabilice

        var target = rule switch
        {
            "game" => GameProfile,
            "battery" => BatteryProfile,
            "work" => WorkProfile,
            _ => _manualProfile ?? (_currentRule is null ? null : DefaultProfile)
        };

        var first = _currentRule is null;
        if (first && rule == "none") { _currentRule = rule; Status = Describe(rule, reason); return; }

        if (target is not null && target != AppServices.State.ActiveProfile)
        {
            if (!ApplyProfile(target)) return; // sin servicio o fallo: no hace nada y reintenta luego
            _lastAutoApplied = target;
        }
        _currentRule = rule;
        Status = Describe(rule, reason);
        var name = AppServices.Profiles.FirstOrDefault(p => p.Id == target)?.Name ?? target ?? "anterior";
        var msg = rule == "none" ? $"Vuelves a tu perfil «{name}»" : $"Perfil «{name}» activado ({reason})";
        AurevixLog.Info("Perfiles automáticos: " + msg);
        Notify?.Invoke(msg);
        Changed?.Invoke(msg);
    }

    private static string Describe(string rule, string reason)
    {
        var name = rule switch
        {
            "game" => "Juego",
            "battery" => "Batería",
            "work" => "Trabajo",
            _ => null
        };
        if (name is null)
        {
            var manual = AppServices.Profiles.FirstOrDefault(p => p.Id == AppServices.State.ActiveProfile)?.Name;
            return manual is null ? "sin cambios, se mantiene tu configuración" : $"tu perfil «{manual}» (ninguna regla activa)";
        }
        return $"perfil {name} ({reason})";
    }

    /// <summary>Misma ruta que ProfilesPage: ProfileManager.Switch y guardar el estado.</summary>
    private static bool ApplyProfile(string id)
    {
        try
        {
            if (AppServices.ProfileManager.Find(id) is null) return false;
            var r = AppServices.ProfileManager.Switch(id);
            if (!r.Success) return false;
            AppServices.State.PreviousProfile = AppServices.State.ActiveProfile;
            AppServices.State.ActiveProfile = id;
            AppServices.SaveState();
            return true;
        }
        catch (Exception ex) { AurevixLog.Warn("Perfiles automáticos: no se pudo aplicar: " + ex.Message); return false; }
    }
}
