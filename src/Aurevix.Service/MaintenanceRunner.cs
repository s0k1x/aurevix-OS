using System.Diagnostics;
using Aurevix.Core.Logging;
using Aurevix.Performance;

namespace Aurevix.Service;

/// <summary>Tareas de mantenimiento con comandos FIJOS. El cliente solo elige un nombre de la lista.</summary>
public sealed class MaintenanceRunner : IMaintenanceRunner
{
    private static readonly Dictionary<string, (string file, string args)> Commands = new()
    {
        ["dism-cleanup"] = ("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup /NoRestart"),
        ["retrim"] = ("powershell.exe", "-NoProfile -NonInteractive -Command \"Optimize-Volume -DriveLetter $env:SystemDrive[0] -ReTrim\""),
        ["trim-on"] = ("fsutil.exe", "behavior set DisableDeleteNotify NTFS 0"),
        ["flush-dns"] = ("ipconfig.exe", "/flushdns"),
        ["tcp-defaults"] = ("netsh.exe", "int tcp set global autotuninglevel=normal rss=enabled"),
        ["network-repair"] = ("cmd.exe", "/c netsh winsock reset && netsh int ip reset"),
        ["sfc"] = ("sfc.exe", "/scannow"),
        ["dism-restorehealth"] = ("dism.exe", "/Online /Cleanup-Image /RestoreHealth /NoRestart"),
        ["chkdsk-scan"] = ("chkdsk.exe", "C: /scan"),
        ["power-defaults"] = ("powercfg.exe", "-restoredefaultschemes"),
        ["optimize-drives"] = ("powershell.exe", "-NoProfile -NonInteractive -Command \"Get-Volume | Where-Object { $_.DriveLetter -and $_.DriveType -eq 'Fixed' } | ForEach-Object { Optimize-Volume -DriveLetter $_.DriveLetter }\""),
        ["dism-checkhealth"] = ("dism.exe", "/Online /Cleanup-Image /CheckHealth"),
        ["dism-scanhealth"] = ("dism.exe", "/Online /Cleanup-Image /ScanHealth"),
        ["trim-query"] = ("fsutil.exe", "behavior query DisableDeleteNotify"),
        ["ip-renew"] = ("cmd.exe", "/c ipconfig /release && ipconfig /renew"),
        ["energy-report"] = ("powercfg.exe", "/energy /output \"%ProgramData%\\AUREVIX\\informe-energia.html\" /duration 30"),
    };

    /// <summary>Acciones cuya salida se devuelve al usuario (últimas líneas).</summary>
    private static readonly HashSet<string> WithOutput = ["dism-checkhealth", "dism-scanhealth", "trim-query", "ip-renew"];

    public (bool ok, string message) Run(string action)
    {
        if (action == "purge-standby") return StandbyList.Purge();
        if (!Commands.TryGetValue(action, out var c)) return (false, "No permitido.");
        try
        {
            var args = c.args;
            if (action == "energy-report")
            {
                Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(@"%ProgramData%\AUREVIX"));
                args = Environment.ExpandEnvironmentVariables(args);
            }
            using var p = Process.Start(new ProcessStartInfo(c.file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30 * 60 * 1000);
            AurevixLog.Info($"Mantenimiento {action}: código {p.ExitCode}");
            var text = p.ExitCode == 0
                ? $"{CoreServiceProtocol.MaintenanceActions[action]}: completado."
                : $"{CoreServiceProtocol.MaintenanceActions[action]}: terminó con código {p.ExitCode}.";
            if (WithOutput.Contains(action))
            {
                var tail = string.Join("\n", stdout.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).TakeLast(60));
                if (tail.Length > 0) text += "\n\n" + tail;
            }
            return (p.ExitCode == 0, text);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}

/// <summary>Vacía la lista de memoria en espera (lo mismo que hacen ISLC o RAMMap) con la API del sistema.</summary>
internal static class StandbyList
{
    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges newState, int len, IntPtr prev, IntPtr retLen);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges { public int Count; public long Luid; public int Attributes; }

    public static (bool ok, string message) Purge()
    {
        try
        {
            if (OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, 0x28, out var tok) &&
                LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid))
            {
                var tp = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            var cmd = 4; // MemoryPurgeStandbyList
            var status = NtSetSystemInformation(80, ref cmd, sizeof(int)); // SystemMemoryListInformation
            Aurevix.Core.Logging.AurevixLog.Info($"Purge standby: 0x{status:X}");
            return status == 0 ? (true, "Memoria en espera vaciada.") : (false, $"Windows no permitió vaciar la memoria en espera (0x{status:X}).");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
