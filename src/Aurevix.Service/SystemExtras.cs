using System.Diagnostics;
using System.Management;
using System.Text.Json;
using Aurevix.Core.Logging;
using Microsoft.Win32;
using RegistryValueKind = Microsoft.Win32.RegistryValueKind;

namespace Aurevix.Service;

/// <summary>Operaciones del sistema que requieren privilegios: memoria virtual fija, procesos de fondo en baja prioridad, prioridad de componentes, Hyper-V y uso compartido.</summary>
public sealed partial class DeviceConfigurator
{
    // Lista CERRADA de procesos de fondo de Windows que pasan a prioridad baja (Image File Execution Options).
    private static readonly string[] BackgroundProcesses =
        ["SearchIndexer.exe", "SearchProtocolHost.exe", "SearchFilterHost.exe", "CompatTelRunner.exe", "MoUsoCoreWorker.exe", "TiWorker.exe", "WaaSMedicAgent.exe", "sihclient.exe"];
    private const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    public (bool ok, string message) SetBackgroundLowPriority(bool on)
    {
        try
        {
            foreach (var exe in BackgroundProcesses)
            {
                if (on)
                {
                    using var k = Registry.LocalMachine.CreateSubKey($@"{Ifeo}\{exe}\PerfOptions", true);
                    k.SetValue("CpuPriorityClass", 5, RegistryValueKind.DWord); // 5 = Por debajo de lo normal
                    k.SetValue("IoPriority", 1, RegistryValueKind.DWord);       // 1 = E/S baja
                }
                else SetGamePriority(exe, false);
            }
            return (true, on ? $"{BackgroundProcesses.Length} procesos de fondo de Windows pasan a prioridad baja (se aplica cuando se inician de nuevo)."
                             : "Prioridad de los procesos de fondo de Windows restaurada.");
        }
        catch (Exception ex) { AurevixLog.Error("Prioridad de fondo", ex); return (false, "No se pudo cambiar la prioridad de los procesos de fondo."); }
    }

    public (bool ok, string message) SetPagefile(bool fixedDoubleRam)
    {
        var ramMb = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
        var size = ramMb * 2;
        if (fixedDoubleRam)
        {
            var free = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace / 1024 / 1024;
            if (free < size + 10240) return (false, $"Hacen falta al menos {size / 1024 + 10} GB libres en C: para fijar la memoria virtual.");
        }
        var script = fixedDoubleRam
            ? "$cs = Get-CimInstance Win32_ComputerSystem; Set-CimInstance -InputObject $cs -Property @{ AutomaticManagedPagefile = $false }; " +
              "$pf = Get-CimInstance Win32_PageFileSetting | Where-Object { $_.Name -like 'C:*' }; " +
              "if (-not $pf) { $pf = New-CimInstance -ClassName Win32_PageFileSetting -Property @{ Name = 'C:\\pagefile.sys' } }; " +
              $"Set-CimInstance -InputObject $pf -Property @{{ InitialSize = [uint32]{size}; MaximumSize = [uint32]{size} }}"
            : "$cs = Get-CimInstance Win32_ComputerSystem; Set-CimInstance -InputObject $cs -Property @{ AutomaticManagedPagefile = $true }";
        var ok = Ps(script);
        return ok ? (true, fixedDoubleRam ? $"Memoria virtual fija de {size / 1024} GB en C:. Reinicia para aplicarla." : "Memoria virtual administrada por Windows. Reinicia para aplicarlo.")
                  : (false, "No se pudo cambiar la memoria virtual.");
    }

    private string IrqBackup => Path.Combine(stateDir, "irqprio-original.json");
    private const string AffinitySub = @"Device Parameters\Interrupt Management\Affinity Policy";

    public (bool ok, string message) SetComponentPriority(bool on)
    {
        try
        {
            var ids = new List<string>();
            foreach (var q in new[] { "SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", "SELECT PNPDeviceID FROM Win32_SCSIController", "SELECT PNPDeviceID FROM Win32_IDEController" })
            {
                using var s = new ManagementObjectSearcher(q);
                ids.AddRange(s.Get().Cast<ManagementBaseObject>().Select(o => o["PNPDeviceID"]?.ToString())
                    .Where(id => id is not null && id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)).Cast<string>());
            }
            var backup = File.Exists(IrqBackup) ? JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(IrqBackup)) ?? new() : new();
            foreach (var id in ids.Distinct())
            {
                using var k = Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Enum\{id}\{AffinitySub}", true);
                if (!backup.ContainsKey(id)) backup[id] = k.GetValue("DevicePriority") as int?;
                if (on) k.SetValue("DevicePriority", 3, RegistryValueKind.DWord); // 3 = Alta
                else if (backup[id] is { } v) k.SetValue("DevicePriority", v, RegistryValueKind.DWord);
                else k.DeleteValue("DevicePriority", false);
            }
            if (on) File.WriteAllText(IrqBackup, JsonSerializer.Serialize(backup)); else if (File.Exists(IrqBackup)) File.Delete(IrqBackup);
            return (true, on ? $"Prioridad alta en {ids.Distinct().Count()} componentes (red y almacenamiento). Reinicia para aplicarla." : "Prioridad de componentes restaurada. Reinicia para aplicarlo.");
        }
        catch (Exception ex) { AurevixLog.Error("Prioridad de componentes", ex); return (false, "No se pudo cambiar la prioridad de los componentes."); }
    }

    public (bool ok, string message) SetHypervisor(bool on)
    {
        var ok = Run("bcdedit.exe", on ? "/set hypervisorlaunchtype auto" : "/set hypervisorlaunchtype off");
        return ok ? (true, on ? "Hipervisor activado (Hyper-V, WSL2, aislamiento del núcleo). Reinicia." : "Hipervisor desactivado. WSL2, Hyper-V, Android y el aislamiento del núcleo no funcionarán hasta reactivarlo. Reinicia.")
                  : (false, "No se pudo cambiar la configuración del hipervisor.");
    }

    public (bool ok, string message) SetNetworkSharing(bool on)
    {
        // Grupos por identificador de recurso: funcionan en cualquier idioma de Windows.
        var v = on ? "True" : "False";
        var ok = Ps($"Set-NetFirewallRule -Group '@FirewallAPI.dll,-32752' -Profile Private -Enabled {v}; Set-NetFirewallRule -Group '@FirewallAPI.dll,-28502' -Profile Private -Enabled {v}");
        return ok ? (true, on ? "Detección de redes y uso compartido activados en redes privadas." : "Detección de redes y uso compartido desactivados.")
                  : (false, "No se pudo cambiar el uso compartido.");
    }

    private static bool Run(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(60000);
        return p.ExitCode == 0;
    }

    private static bool Ps(string script)
    {
        var enc = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script));
        return Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}");
    }
}
