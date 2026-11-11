using System.Diagnostics;
using Aurevix.Core.Logging;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// Nusa (de rama2555): convierte el borde superior de la pantalla en una «isla» con música, letras, temporizador y
/// archivos. Es una app de terceros: AUREVIX no copia su instalador (no tiene licencia de redistribución); la
/// instala desde Microsoft Store, su canal oficial, con winget.
/// </summary>
public static class Nusa
{
    public const string StoreId = "9NQMSXF0N0WM";
    public const string Web = "https://rama2555.github.io/nusa/";

    public static bool IsInstalled()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("winget", $"list --id {StoreId} --source msstore --accept-source-agreements")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30000);
            return p.ExitCode == 0 && output.Contains(StoreId, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Instala Nusa desde Microsoft Store. Devuelve true si quedó instalada (o ya lo estaba).</summary>
    public static async Task<bool> InstallAsync()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("winget",
                $"install --id {StoreId} --source msstore --silent --accept-package-agreements --accept-source-agreements")
            { UseShellExecute = false, CreateNoWindow = true })!;
            await p.WaitForExitAsync();
            // 0 = instalada; 0x8A15002B = ya instalada.
            var ok = p.ExitCode is 0 or -1978335189;
            AurevixLog.Info($"Nusa: winget terminó con {p.ExitCode}");
            return ok;
        }
        catch (Exception ex)
        {
            AurevixLog.Warn($"Nusa no se pudo instalar: {ex.Message}");
            return false;
        }
    }

    public static void OpenStorePage() => SystemTools.Start($"ms-windows-store://pdp/?ProductId={StoreId}");
}
