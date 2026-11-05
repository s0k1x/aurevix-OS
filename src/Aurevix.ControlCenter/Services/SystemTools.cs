using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Aurevix.Core.Logging;

namespace Aurevix.ControlCenter.Services;

/// <summary>Utilidades del sistema basadas en APIs y herramientas oficiales de Windows.</summary>
public static class SystemTools
{
    /// <summary>
    /// Crea un punto de restauración con el cmdlet oficial Checkpoint-Computer.
    /// Requiere administrador: Windows mostrará el aviso de UAC. Devuelve false si el usuario cancela
    /// o si Protección del sistema está desactivada.
    /// </summary>
    public static async Task<bool> CreateRestorePointAsync(string description)
    {
        var safe = new string(description.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '.').ToArray());
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description '{safe}' -RestorePointType MODIFY_SETTINGS\"")
        {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
        };
        try
        {
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            AurevixLog.Info($"Punto de restauración '{safe}': código {p.ExitCode}");
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            AurevixLog.Info("Punto de restauración cancelado por el usuario (UAC).");
            return false;
        }
    }

    public static void OpenSystemRestore() => Start("rstrui.exe");
    public static void OpenWindowsUpdate() => Start("ms-settings:windowsupdate");
    public static void OpenFolder(string path) { Directory.CreateDirectory(path); Start(path); }

    public static void Start(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { AurevixLog.Error($"No se pudo abrir {target}", ex); }
    }

    // ---------- Métricas en vivo (sin privilegios) ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; public ulong Value => ((ulong)High << 32) | Low; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

    private static ulong _lastIdle, _lastTotal;

    /// <summary>Uso de CPU (0-100) desde la última llamada.</summary>
    public static double CpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var total = kernel.Value + user.Value; // kernel incluye idle
        var dIdle = idle.Value - _lastIdle;
        var dTotal = total - _lastTotal;
        _lastIdle = idle.Value; _lastTotal = total;
        return dTotal == 0 ? 0 : Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
    }

    public static (double usedGb, double totalGb, uint loadPercent) Memory()
    {
        var m = new MEMORYSTATUSEX();
        if (!GlobalMemoryStatusEx(m)) return (0, 0, 0);
        const double gb = 1024d * 1024 * 1024;
        return ((m.ullTotalPhys - m.ullAvailPhys) / gb, m.ullTotalPhys / gb, m.dwMemoryLoad);
    }

    // ---------- Fondo de pantalla ----------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string? vparam, uint winIni);

    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    public static bool SetWallpaper(string path) =>
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

    public static string? CurrentWallpaper() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;

    // Inicio automático: el instalador registra HKLM\...\Run con "--background" para todos los usuarios.
    // Si el usuario desactiva "Iniciar con Windows" en Ajustes, la app se cierra sola al arrancar en ese modo.
}
