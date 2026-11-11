using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Aurevix.Core.Logging;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// AUREVIX Shell (propio): aplica el estilo AUREVIX a la barra de tareas, el menú Inicio y Configuración desde el arranque.
///
/// Carga <c>shell\AurevixShell.dll</c> en el Explorador y en el menú Inicio con <c>InitializeXamlDiagnosticsEx</c>,
/// la API pública de diagnóstico de XAML de Windows. Los cambios viven solo en memoria: al reiniciar el Explorador
/// con AUREVIX Shell desactivado, Windows 11 vuelve a verse exactamente como antes.
///
/// Protección: si el Explorador se cae 3 veces en poco tiempo después de aplicar el estilo, AUREVIX Shell se
/// desactiva solo y lo avisa.
/// </summary>
public static class AurevixShell
{
    private static readonly Guid TapClsid = new("8C2D5B1E-6A3F-4E8B-9D47-A7E15C0B3F21");
    public static string DllPath => Path.Combine(AppContext.BaseDirectory, "shell", "AurevixShell.dll");
    public static bool IsAvailable => File.Exists(DllPath) && Environment.OSVersion.Version.Build >= 22000;

    public static event Action<string>? Notify;

    private static readonly HashSet<int> Injected = [];
    /// <summary>Intentos fallidos por proceso: tras 20 (un minuto) se deja de intentar con ese proceso.</summary>
    private static readonly Dictionary<int, int> Failures = [];
    private static readonly Queue<DateTime> ExplorerRestarts = new();
    private static System.Threading.Timer? _timer;
    private static int _lastExplorer;
    private static int _busy;

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int InitializeXamlDiagnosticsExFn(string endPointName, uint pid, string? wszDllXamlDiagnostics,
        string wszTapDllName, Guid tapClsid, string? wszInitializationData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string cls, string? name);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, IntPtr proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr w, IntPtr l);

    private static IntPtr _shellModule, _hookProc;

    private static InitializeXamlDiagnosticsExFn? _init;
    private static int _goodVariant;
    /// <summary>Último resultado de un intento (para el registro y la prueba automática).</summary>
    public static string LastResult { get; private set; } = "";

    /// <summary>Empieza a vigilar el Explorador y el menú Inicio (cada 3 s; coste casi nulo).</summary>
    public static void Start()
    {
        if (!AppServices.Settings.ShellEnabled || !IsAvailable) return;
        _timer ??= new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        AurevixLog.Info("AUREVIX Shell activo.");
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        Injected.Clear();
        _lastExplorer = 0;
    }

    /// <summary>Activa o desactiva AUREVIX Shell. Al desactivar se reinicia el Explorador para volver a Windows 11 original.</summary>
    public static void SetEnabled(bool on, bool restartShell = true)
    {
        AppServices.Settings.ShellEnabled = on;
        AppServices.SaveSettings();
        if (on) { Start(); if (restartShell) RestartShellProcesses(); }
        else { Stop(); if (restartShell) RestartShellProcesses(); }
    }

    /// <summary>Reinicia el Explorador y el menú Inicio (los dos vuelven a cargarse solos).</summary>
    public static void RestartShellProcesses()
    {
        Injected.Clear();
        foreach (var p in Process.GetProcessesByName("StartMenuExperienceHost")) { try { if (p.SessionId == Process.GetCurrentProcess().SessionId) p.Kill(); } catch { } finally { p.Dispose(); } }
        AurevixStyle.RestartExplorer();
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (!AppServices.Settings.ShellEnabled) return;
            var explorer = ExplorerPid();
            if (explorer != 0 && explorer != _lastExplorer)
            {
                if (_lastExplorer != 0 && Injected.Contains(_lastExplorer)) GuardAgainstCrashLoop();
                _lastExplorer = explorer;
            }
            if (!AppServices.Settings.ShellEnabled) return;
            if (explorer != 0 && !Injected.Contains(explorer)) TryInject(explorer, "Explorador");

            var session = Process.GetCurrentProcess().SessionId;
            foreach (var (proc, what) in Targets)
                foreach (var p in Process.GetProcessesByName(proc))
                {
                    try { if (p.SessionId == session && !Injected.Contains(p.Id)) TryInject(p.Id, what); }
                    catch { }
                    finally { p.Dispose(); }
                }
            Injected.RemoveWhere(pid => pid != _lastExplorer && IsGone(pid));
            foreach (var dead in Failures.Keys.Where(IsGone).ToList()) Failures.Remove(dead);
        }
        catch (Exception ex) { AurevixLog.Warn($"AUREVIX Shell: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>Además del Explorador (barra de tareas y Explorador de archivos): menú Inicio y Configuración.</summary>
    private static readonly (string Process, string What)[] Targets =
        [("StartMenuExperienceHost", "menú Inicio"), ("SystemSettings", "Configuración")];

    private static bool IsGone(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.HasExited; }
        catch { return true; }
    }

    private static int ExplorerPid()
    {
        var tray = FindWindowW("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(tray, out var pid);
        return (int)pid;
    }

    private static void GuardAgainstCrashLoop()
    {
        var now = DateTime.UtcNow;
        ExplorerRestarts.Enqueue(now);
        while (ExplorerRestarts.Count > 0 && now - ExplorerRestarts.Peek() > TimeSpan.FromMinutes(3)) ExplorerRestarts.Dequeue();
        if (ExplorerRestarts.Count < 3) return;
        AurevixLog.Warn("AUREVIX Shell: el Explorador se reinició 3 veces en 3 minutos. Se desactiva por seguridad.");
        AppServices.Settings.ShellEnabled = false;
        AppServices.SaveSettings();
        Stop();
        Notify?.Invoke("AUREVIX Shell se ha desactivado porque el Explorador se reinició varias veces. Puedes volver a activarlo en Apariencia.");
    }

    /// <summary>Hilos con ventanas del proceso: el Explorador, el de la barra de tareas; los demás, todos sus hilos.</summary>
    private static List<uint> ThreadsOf(int pid)
    {
        var tray = FindWindowW("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero)
        {
            var tid = GetWindowThreadProcessId(tray, out var owner);
            if (owner == (uint)pid) return [tid];
        }
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.Threads.Cast<ProcessThread>().Select(t => (uint)t.Id).ToList();
        }
        catch { return []; }
    }

    private static bool IsLoadedIn(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            foreach (ProcessModule m in p.Modules)
                if (string.Equals(m.FileName, DllPath, StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Arranque automático: AurevixShell.dll se carga en el hilo de la ventana del proceso y se conecta desde dentro
    /// (Windows solo acepta así la conexión de XAML). Después se retira el procedimiento; la DLL queda activa.
    /// </summary>
    private static int StartInside(int pid)
    {
        if (_shellModule == IntPtr.Zero)
        {
            _shellModule = LoadLibraryExW(DllPath, IntPtr.Zero, 0);
            _hookProc = _shellModule == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(_shellModule, "AurevixHook");
            if (_hookProc == IntPtr.Zero) return -1;
        }
        var hooks = new List<IntPtr>();
        foreach (var tid in ThreadsOf(pid))
        {
            var hook = SetWindowsHookExW(3 /* WH_GETMESSAGE */, _hookProc, _shellModule, tid);
            if (hook == IntPtr.Zero) continue; // hilo sin cola de mensajes
            hooks.Add(hook);
            PostThreadMessageW(tid, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
        }
        if (hooks.Count == 0) return -2;
        try { for (var i = 0; i < 25 && !IsLoadedIn(pid); i++) Thread.Sleep(100); }
        finally { foreach (var h in hooks) UnhookWindowsHookEx(h); }
        return IsLoadedIn(pid) ? 1 : 0;
    }

    private static bool TryInject(int pid, string what)
    {
        if (Failures.TryGetValue(pid, out var fails) && fails >= 100) return false;
        try
        {
            // El menú Inicio está «dormido» hasta que se abre: carga la DLL en ese momento.
            var inside = IsLoadedIn(pid) ? 1 : StartInside(pid);
            if (inside == 1)
            {
                LastResult = $"{what} pid {pid}: dentro";
                Injected.Add(pid);
                AurevixLog.Info($"AUREVIX Shell aplicado al {what} (PID {pid}).");
                return true;
            }
            var insideResult = $"dentro={inside}";
            if (_init is null)
            {
                var module = LoadLibraryExW("Windows.UI.Xaml.dll", IntPtr.Zero, 0x00000800 /* LOAD_LIBRARY_SEARCH_SYSTEM32 */);
                var fn = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "InitializeXamlDiagnosticsEx");
                if (fn == IntPtr.Zero) { AurevixLog.Warn("AUREVIX Shell: este Windows no tiene InitializeXamlDiagnosticsEx."); Stop(); return false; }
                _init = Marshal.GetDelegateForFunctionPointer<InitializeXamlDiagnosticsExFn>(fn);
            }
            // Se prueban las dos formas de llamada que usan las herramientas de diagnóstico de XAML y se recuerda la que funcione.
            var hr = unchecked((int)0x80004005);
            var tried = new List<string> { insideResult };
            for (var v = 0; v < 2; v++)
            {
                var variant = (_goodVariant + v) % 2;
                hr = variant == 0
                    ? _init($"AurevixShell-{pid}-{Environment.TickCount64}", (uint)pid, "", DllPath, TapClsid, "")
                    : _init("VisualDiagConnection1", (uint)pid, null, DllPath, TapClsid, null);
                tried.Add($"v{variant}=0x{hr:X8}");
                if (hr >= 0) { _goodVariant = variant; break; }
            }
            LastResult = $"{what} pid {pid}: {string.Join(" ", tried)}";
            if (hr >= 0)
            {
                Injected.Add(pid);
                AurevixLog.Info($"AUREVIX Shell aplicado al {what} (PID {pid}).");
                return true;
            }
            // XAML aún no está cargado (el proceso acaba de arrancar): se reintenta en el siguiente ciclo.
            Failures[pid] = fails + 1;
            if (fails == 0 || fails == 99) AurevixLog.Info($"AUREVIX Shell: el {what} aún no está listo ({LastResult}).");
            return false;
        }
        catch (Exception ex)
        {
            LastResult = $"{what} pid {pid}: {ex.GetType().Name} {ex.Message}";
            AurevixLog.Warn($"AUREVIX Shell no se pudo aplicar al {what}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Modo prueba (CI): aplica una vez y espera a que haga efecto.</summary>
    public static async Task<string> ApplyOnceAsync()
    {
        Failures.Clear();
        for (var i = 0; i < 30; i++)
        {
            var pid = ExplorerPid();
            if (pid != 0 && TryInject(pid, "Explorador")) return $"OK explorer {pid} admin={AppServices.IsAdmin} {LastResult}";
            await Task.Delay(1000);
        }
        return $"FAIL explorer={ExplorerPid()} admin={AppServices.IsAdmin} dll={File.Exists(DllPath)} build={Environment.OSVersion.Version.Build} last={LastResult}";
    }
}
