using System.Management;
using System.Text.Json;
using Aurevix.Core.Logging;
using Aurevix.Performance;
using Microsoft.Win32;
using RegistryValueKind = Microsoft.Win32.RegistryValueKind;

namespace Aurevix.Service;

/// <summary>
/// Modo MSI de la GPU y prioridad de CPU por juego. Las rutas de registro las calcula el servicio
/// (el cliente solo envía on/off o un nombre de .exe validado). Guarda los valores originales.
/// </summary>
public sealed class DeviceConfigurator(string stateDir) : IDeviceConfigurator
{
    private string MsiBackup => Path.Combine(stateDir, "msi-original.json");
    private const string MsiSub = @"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";

    private static List<string> GpuInstances()
    {
        using var s = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController");
        return s.Get().Cast<ManagementBaseObject>().Select(o => o["PNPDeviceID"]?.ToString())
            .Where(id => id is not null && id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)).Cast<string>().ToList();
    }

    public (bool ok, string message) SetGpuMsi(bool enabled)
    {
        try
        {
            var gpus = GpuInstances();
            if (gpus.Count == 0) return (false, "No se detectó ninguna GPU PCI.");
            var backup = File.Exists(MsiBackup) ? JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(MsiBackup)) ?? new() : new();
            foreach (var id in gpus)
            {
                var path = $@"SYSTEM\CurrentControlSet\Enum\{id}\{MsiSub}";
                using var k = Registry.LocalMachine.CreateSubKey(path, true);
                if (!backup.ContainsKey(id)) backup[id] = k.GetValue("MSISupported") as int?;
                if (enabled) k.SetValue("MSISupported", 1, RegistryValueKind.DWord);
                else if (backup[id] is { } orig) k.SetValue("MSISupported", orig, RegistryValueKind.DWord);
                else k.DeleteValue("MSISupported", false);
            }
            if (enabled) File.WriteAllText(MsiBackup, JsonSerializer.Serialize(backup));
            else if (File.Exists(MsiBackup)) File.Delete(MsiBackup);
            AurevixLog.Info($"MSI GPU {(enabled ? "activado" : "restaurado")} en {gpus.Count} dispositivo(s).");
            return (true, enabled ? $"Modo MSI activado en {gpus.Count} GPU. Reinicia para aplicarlo." : "Modo de interrupciones original restaurado. Reinicia para aplicarlo.");
        }
        catch (Exception ex)
        {
            AurevixLog.Error("MSI GPU", ex);
            return (false, "No se pudo cambiar el modo de interrupciones.");
        }
    }

    public (bool ok, string message) SetGamePriority(string exe, bool high)
    {
        try
        {
            var key = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exe}";
            if (high)
            {
                using var k = Registry.LocalMachine.CreateSubKey(key + @"\PerfOptions", true);
                k.SetValue("CpuPriorityClass", 3, RegistryValueKind.DWord); // 3 = Alta
                return (true, $"{exe} se iniciará siempre con prioridad alta.");
            }
            using (var parent = Registry.LocalMachine.OpenSubKey(key, true))
                parent?.DeleteSubKeyTree("PerfOptions", false);
            // Si la clave del juego quedó vacía (la creó AUREVIX), se elimina; nunca se tocan otras opciones (p. ej. Debugger).
            using (var check = Registry.LocalMachine.OpenSubKey(key))
                if (check is not null && check.SubKeyCount == 0 && check.ValueCount == 0)
                    Registry.LocalMachine.DeleteSubKey(key, false);
            return (true, $"Prioridad de {exe} restaurada.");
        }
        catch (Exception ex)
        {
            AurevixLog.Error("Prioridad de juego", ex);
            return (false, "No se pudo cambiar la prioridad del juego.");
        }
    }
}
