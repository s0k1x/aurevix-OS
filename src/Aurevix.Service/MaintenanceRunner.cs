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
        ["flush-dns"] = ("ipconfig.exe", "/flushdns"),
        ["tcp-defaults"] = ("netsh.exe", "int tcp set global autotuninglevel=normal rss=enabled"),
        ["network-repair"] = ("cmd.exe", "/c netsh winsock reset && netsh int ip reset"),
    };

    public (bool ok, string message) Run(string action)
    {
        if (!Commands.TryGetValue(action, out var c)) return (false, "No permitido.");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(c.file, c.args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(30 * 60 * 1000);
            AurevixLog.Info($"Mantenimiento {action}: código {p.ExitCode}");
            return p.ExitCode == 0
                ? (true, $"{CoreServiceProtocol.MaintenanceActions[action]}: completado.")
                : (false, $"{CoreServiceProtocol.MaintenanceActions[action]}: terminó con código {p.ExitCode}.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
