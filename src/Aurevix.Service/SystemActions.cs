using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Aurevix.Core;
using Aurevix.Core.Benchmark;
using Aurevix.Core.Logging;
using Aurevix.Performance;
using Aurevix.Update;

namespace Aurevix.Service;

/// <summary>
/// Acciones de sistema que el Control Center hacía con UAC. Como AUREVIX forma parte del sistema, las hace el
/// servicio sin preguntar a Windows. Las entradas ya llegan validadas por <see cref="CoreRequestHandler"/>.
/// </summary>
public sealed class SystemActions(string root) : ISystemActions
{
    private const string FirewallGroup = "AUREVIX";
    private static readonly string AppDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    private static readonly string PresentMon = Path.Combine(AppDir, "tools", "PresentMon.exe");
    private const string AppExe = "AurevixControlCenter.exe";
    private string RelaunchMarker => Path.Combine(root, "relaunch-after-update");
    private string UpdateStateFile => Path.Combine(root, "update-state.json");
    private static readonly SemaphoreSlim BenchmarkLock = new(1, 1);
    private static int _installing;

    // ------------------------------------------------------------------ Punto de restauración

    public (bool ok, string message) CreateRestorePoint(string description)
    {
        var (ok, _) = Shell.Ps($"Checkpoint-Computer -Description {Shell.Q(description)} -RestorePointType MODIFY_SETTINGS");
        return ok ? (true, "Punto de restauración creado.")
                  : (false, "No se pudo crear el punto de restauración. Comprueba que «Protección del sistema» está activada en C:.");
    }

    // ------------------------------------------------------------------ Firewall

    public (bool ok, string message) BlockProgram(string exePath)
    {
        if (!File.Exists(exePath)) return (false, "El programa no existe.");
        var name = Path.GetFileName(exePath);
        var (ok, _) = Shell.Ps(
            $"Get-NetFirewallRule -Group '{FirewallGroup}' -ErrorAction SilentlyContinue | Where-Object {{ ($_ | Get-NetFirewallApplicationFilter).Program -eq {Shell.Q(exePath)} }} | Remove-NetFirewallRule; " +
            $"New-NetFirewallRule -DisplayName {Shell.Q("AUREVIX Bloqueo " + name)} -Group '{FirewallGroup}' -Direction Outbound -Program {Shell.Q(exePath)} -Action Block | Out-Null; " +
            $"New-NetFirewallRule -DisplayName {Shell.Q("AUREVIX Bloqueo " + name + " (entrada)")} -Group '{FirewallGroup}' -Direction Inbound -Program {Shell.Q(exePath)} -Action Block | Out-Null");
        AurevixLog.Info($"Firewall: bloqueo de {exePath} ({ok})");
        return ok ? (true, $"{name} ya no puede usar internet.") : (false, "No se pudo cambiar el Firewall.");
    }

    public (bool ok, string message) UnblockAll()
    {
        var (ok, _) = Shell.Ps($"Remove-NetFirewallRule -Group '{FirewallGroup}' -ErrorAction SilentlyContinue");
        return ok ? (true, "Bloqueos de AUREVIX eliminados.") : (false, "No se pudo cambiar el Firewall.");
    }

    public IReadOnlyList<string> ListBlocked()
    {
        var (ok, output) = Shell.Ps($"Get-NetFirewallRule -Group '{FirewallGroup}' -Direction Outbound -ErrorAction SilentlyContinue | Get-NetFirewallApplicationFilter | ForEach-Object {{ $_.Program }} | Sort-Object -Unique");
        return ok ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    }

    // ------------------------------------------------------------------ Benchmark

    public (bool ok, string message, string? json) RunBenchmark(string exe, int seconds, string label)
    {
        if (!File.Exists(PresentMon)) return (false, "PresentMon no está instalado.", null);
        if (!BenchmarkLock.Wait(0)) return (false, "Ya hay una medición en curso.", null);
        var dir = Path.Combine(root, "bench");
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, $"{Guid.NewGuid():N}.csv");
        try
        {
            var args = $"--process_name {exe} --output_file \"{csv}\" --timed {seconds} --terminate_after_timed --v1_metrics --no_console_stats --stop_existing_session";
            using var p = Process.Start(new ProcessStartInfo(PresentMon, args) { UseShellExecute = false, CreateNoWindow = true })!;
            if (!p.WaitForExit((seconds + 45) * 1000)) { try { p.Kill(); } catch { } return (false, "PresentMon no terminó a tiempo.", null); }
            if (!File.Exists(csv)) return (false, "PresentMon no generó datos. ¿Estaba el juego en marcha y en primer plano?", null);
            var frames = BenchmarkStats.ParsePresentMonCsv(File.ReadLines(csv), exe);
            if (frames.Count < 30) return (false, "Se capturaron muy pocos fotogramas. Juega durante la medición.", null);
            var r = BenchmarkStats.Compute(frames, exe, label);
            AurevixLog.Info($"Benchmark {exe}: {r.AvgFps:0.0} FPS");
            return (true, $"{r.AvgFps:0} FPS de media.", JsonSerializer.Serialize(r, JsonDefaults.Options));
        }
        finally
        {
            try { File.Delete(csv); } catch { }
            BenchmarkLock.Release();
        }
    }

    // ------------------------------------------------------------------ Iconos AUREVIX

    private const string ShellIconsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
    /// <summary>Índice de imageres/shell32 que Windows usa para cada tipo → icono AUREVIX.</summary>
    private static readonly (string Index, string Icon)[] ShellIconMap =
        [("0", "file"), ("1", "file-text"), ("2", "app"), ("3", "folder"), ("4", "folder-open"), ("7", "usb"), ("8", "drive"),
         ("9", "network-drive"), ("10", "network-drive"), ("11", "disc"), ("12", "drive"), ("15", "pc"), ("16", "printer"),
         ("17", "network"), ("29", "shortcut"), ("34", "desktop"), ("38", "file-font"), ("43", "favorites"), ("47", "lock")];

    /// <summary>La unidad de Windows (C:) usa el icono SSD de AUREVIX; al restaurar solo se borra si es el nuestro.</summary>
    private static void SetSystemDriveIcon(string? icon, string? ourDir = null)
    {
        var letter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':');
        if (string.IsNullOrEmpty(letter)) return;
        var keyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{letter}\DefaultIcon";
        if (icon is not null)
        {
            if (!File.Exists(icon)) return;
            using var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(keyPath, true);
            if (k.GetValue("") is null or string { Length: 0 }) k.SetValue("", icon + ",0");
            return;
        }
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath, true);
        if (key?.GetValue("") is string v && ourDir is not null && v.StartsWith(ourDir, StringComparison.OrdinalIgnoreCase)) key.DeleteValue("", false);
    }

    public (bool ok, string message) SetShellIcons(bool on)
    {
        try
        {
            var dir = Path.Combine(AppDir, "Assets", "Icons");
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(ShellIconsKey, true);
            if (on)
            {
                if (!Directory.Exists(dir)) return (false, "No se encontraron los iconos de AUREVIX.");
                foreach (var (index, icon) in ShellIconMap)
                {
                    var file = Path.Combine(dir, icon + ".ico");
                    if (File.Exists(file)) key.SetValue(index, file + ",0", Microsoft.Win32.RegistryValueKind.String);
                }
                SetSystemDriveIcon(Path.Combine(dir, "ssd.ico"));
                return (true, "Iconos AUREVIX activados para carpetas y unidades.");
            }
            // Solo se borran los valores que apuntan a los iconos de AUREVIX.
            foreach (var (index, _) in ShellIconMap)
                if (key.GetValue(index) is string v && v.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) key.DeleteValue(index, false);
            SetSystemDriveIcon(null, dir);
            return (true, "Iconos de Windows restaurados.");
        }
        catch (Exception ex)
        {
            AurevixLog.Error("Iconos AUREVIX", ex);
            return (false, "No se pudieron cambiar los iconos.");
        }
    }

    // ------------------------------------------------------------------ Actualizaciones

    private sealed class UpdateState { public long LastSequence { get; set; } }

    public (bool ok, string message) InstallUpdate(string channel, string installId)
    {
        if (Interlocked.Exchange(ref _installing, 1) == 1) return (false, "Ya se está instalando una actualización.");
        try { return InstallUpdateAsync(channel, installId).GetAwaiter().GetResult(); }
        catch (Exception ex) { AurevixLog.Error("Actualización desde el servicio", ex); return (false, "No se pudo instalar la actualización: " + ex.Message); }
        finally { Interlocked.Exchange(ref _installing, 0); }
    }

    private async Task<(bool, string)> InstallUpdateAsync(string channel, string installId)
    {
        var cfg = UpdateEndpointConfig.Load();
        var current = CurrentVersion();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"AurevixCoreService/{current}");
        var client = new UpdateClient(new UpdateClientOptions
        {
            ManifestBaseUrl = new Uri(cfg.ManifestBaseUrl),
            AllowedPackageHosts = cfg.AllowedPackageHosts,
            TrustedKeys = ManifestSigner.LoadEmbeddedTrustedKeys()
        }, http);

        var state = JsonStore.LoadOrDefault<UpdateState>(UpdateStateFile);
        // El manifiesto se verifica aquí, con la clave pública incrustada: el cliente no puede elegir qué se instala.
        var result = await client.CheckAsync(current, channel, installId, state.LastSequence);
        if (result.Status != UpdateStatus.Available || result.Release is null) return (false, result.Message);
        if (result.Manifest is { } m && m.Sequence > state.LastSequence) { state.LastSequence = m.Sequence; JsonStore.Save(UpdateStateFile, state); }

        var dir = Path.Combine(root, "updates", result.Release.Version);
        string? installer = null;
        foreach (var p in result.Release.Packages)
        {
            var path = await client.DownloadAsync(p, dir); // comprueba host permitido, tamaño y SHA-256
            if (p.Kind == PackageKind.AppInstaller) installer = path;
        }
        if (installer is null) return (false, "La versión no incluye instalador.");

        File.WriteAllText(RelaunchMarker, DateTime.UtcNow.ToString("O"));
        AurevixLog.Info($"Instalando {result.Release.Version} desde el servicio");
        // Argumentos FIJOS. El instalador detiene este servicio, lo actualiza y lo vuelve a iniciar;
        // el servicio nuevo abre el Control Center en la sesión del usuario (RelaunchIfPending).
        Process.Start(new ProcessStartInfo(installer, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /AUREVIXUPDATE /AUREVIXSERVICE")
        { UseShellExecute = false, CreateNoWindow = true });
        return (true, $"Instalando AUREVIX {result.Release.Version}…");
    }

    private static SemVersion CurrentVersion()
    {
        var asm = typeof(SystemActions).Assembly;
        var info = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        var text = info?.InformationalVersion?.Split('+')[0];
        if (SemVersion.TryParse(text, out var v)) return v!;
        var av = asm.GetName().Version;
        return av is null ? new SemVersion(0, 1, 0) : new SemVersion(av.Major, av.Minor, av.Build);
    }

    /// <summary>Tras una actualización hecha por el servicio: abre el Control Center en la sesión del usuario para ofrecer el reinicio.</summary>
    public void RelaunchIfPending()
    {
        try
        {
            if (!File.Exists(RelaunchMarker)) return;
            var when = File.GetLastWriteTimeUtc(RelaunchMarker);
            File.Delete(RelaunchMarker);
            if (DateTime.UtcNow - when > TimeSpan.FromHours(2)) return;
            var exe = Path.Combine(AppDir, AppExe);
            if (File.Exists(exe)) UserSession.Start(exe, "--background --after-update");
        }
        catch (Exception ex) { AurevixLog.Error("No se pudo abrir el Control Center tras actualizar", ex); }
    }
}

/// <summary>PowerShell con el script codificado (sin interpolación de la línea de comandos).</summary>
internal static class Shell
{
    public static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    public static (bool ok, string output) Ps(string script, int timeoutMs = 120_000)
    {
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script));
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 })!;
        var errTask = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(timeoutMs);
        if (p.ExitCode != 0) AurevixLog.Warn($"PowerShell: {errTask.Result}");
        return (p.ExitCode == 0, output.Trim());
    }
}

/// <summary>Lanza un programa en la sesión del usuario conectado (el servicio corre en la sesión 0).</summary>
internal static class UserSession
{
    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(uint session, out IntPtr token);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool DestroyEnvironmentBlock(IntPtr env);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
        uint flags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    private const uint CREATE_UNICODE_ENVIRONMENT = 0x400;

    public static void Start(string exe, string args)
    {
        // El usuario puede tardar en iniciar sesión tras la actualización: se reintenta durante unos minutos.
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 60; i++)
            {
                if (TryStart(exe, args)) return;
                await Task.Delay(5000);
            }
        });
    }

    private static bool TryStart(string exe, string args)
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session == 0xFFFFFFFF || !WTSQueryUserToken(session, out var token)) return false;
        var env = IntPtr.Zero;
        try
        {
            CreateEnvironmentBlock(out env, token, false);
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            var cmd = new StringBuilder($"\"{exe}\" {args}");
            if (!CreateProcessAsUser(token, exe, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT, env, Path.GetDirectoryName(exe), ref si, out var pi))
                return false;
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            AurevixLog.Info("Control Center abierto en la sesión del usuario tras actualizar.");
            return true;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            CloseHandle(token);
        }
    }
}
