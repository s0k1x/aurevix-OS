using System.Management;
using System.Runtime.Versioning;
using Aurevix.Performance;

namespace Aurevix.Diagnostics;

/// <summary>Datos mínimos del equipo para decidir qué ajustes son adecuados (RAM, SSD, portátil, fabricante de GPU).</summary>
[SupportedOSPlatform("windows")]
public static class SystemProfileDetector
{
    public static SystemProfile Detect()
    {
        double ram = 16; bool ssd = true, laptop = false; string? gpu = null;
        try
        {
            using var cs = new ManagementObjectSearcher("SELECT TotalPhysicalMemory, PCSystemType FROM Win32_ComputerSystem");
            foreach (var o in cs.Get())
            {
                ram = Convert.ToDouble(o["TotalPhysicalMemory"]) / 1024 / 1024 / 1024;
                laptop = Convert.ToInt32(o["PCSystemType"] ?? 0) == 2;
            }
        }
        catch { }
        try
        {
            using var b = new ManagementObjectSearcher("SELECT Name FROM Win32_Battery");
            if (b.Get().Count > 0) laptop = true;
        }
        catch { }
        try
        {
            using var d = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT MediaType FROM MSFT_PhysicalDisk");
            var types = d.Get().Cast<ManagementBaseObject>().Select(o => Convert.ToInt32(o["MediaType"] ?? 0)).ToList();
            ssd = types.Count == 0 || types.Any(t => t == 4); // 4 = SSD
        }
        catch { }
        try
        {
            using var v = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var names = string.Join(" ", v.Get().Cast<ManagementBaseObject>().Select(o => o["Name"]?.ToString()));
            gpu = names.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                : names.Contains("AMD", StringComparison.OrdinalIgnoreCase) || names.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
                : names.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : null;
        }
        catch { }
        return new SystemProfile(Math.Round(ram, 1), ssd, laptop, gpu);
    }
}
