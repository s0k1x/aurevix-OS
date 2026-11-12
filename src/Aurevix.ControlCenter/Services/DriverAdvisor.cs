using System.Management;

namespace Aurevix.ControlCenter.Services;

/// <summary>Un driver detectado: tipo (Gráfica/Red/Audio/Chipset), antigüedad, estado y enlaces oficiales.</summary>
public sealed record DriverInfo(string Kind, string Name, string Version, DateTime? Date, int AgeMonths, string Status, string Url, string? OemUrl);

/// <summary>Asistente de drivers: solo lee Windows y propone la página oficial. Nunca instala nada.</summary>
public static class DriverAdvisor
{
    public const string StatusOk = "Al día";
    public const string StatusReview = "Conviene revisar";
    public const string StatusOld = "Antiguo";

    private static readonly string[] Ignored =
    [
        "Virtual", "Remote", "Parsec", "Basic Display", "Hyper-V", "VMware", "VirtualBox",
        "WAN Miniport", "Bluetooth", "TAP", "Loopback"
    ];

    private static readonly string[] ChipsetWords = ["Chipset", "SMBus", "PCI Express Root", "PSP", "Management Engine"];

    public static Task<List<DriverInfo>> ScanAsync() => Task.Run(Scan);

    private static List<DriverInfo> Scan()
    {
        var result = new List<DriverInfo>();
        try
        {
            var oem = OemUrl();
            var rows = Query("SELECT DeviceName, DriverVersion, DriverDate, Manufacturer, DeviceClass FROM Win32_PnPSignedDriver");
            var seen = new HashSet<string>();
            foreach (var kind in new[] { "Gráfica", "Red", "Audio", "Chipset" })
            {
                foreach (var r in rows)
                {
                    try
                    {
                        var cls = $"{r["DeviceClass"]}".ToUpperInvariant();
                        var name = $"{r["DeviceName"]}".Trim();
                        var maker = $"{r["Manufacturer"]}".Trim();
                        if (name.Length == 0 || IsIgnored(name) || IsGenericMicrosoft(maker)) continue;
                        var match = kind switch
                        {
                            "Gráfica" => cls == "DISPLAY",
                            "Red" => cls == "NET",
                            "Audio" => cls == "MEDIA",
                            _ => cls == "SYSTEM" && (Has(maker, "Intel") || Has(maker, "AMD") || Has(maker, "Advanced Micro"))
                                 && ChipsetWords.Any(w => Has(name, w))
                        };
                        if (!match) continue;
                        // Un solo driver por tipo y nombre; el chipset, solo uno en total.
                        if (!seen.Add(kind == "Chipset" ? kind : kind + "|" + name)) continue;

                        DateTime? date = null;
                        if (r["DriverDate"] is string raw && raw.Length > 0)
                            date = ManagementDateTimeConverter.ToDateTime(raw);
                        var months = date is { } d ? Math.Max(0, (DateTime.Now.Year - d.Year) * 12 + DateTime.Now.Month - d.Month) : 0;
                        var (warn, old) = kind == "Gráfica" ? (6, 12) : (12, 24);
                        var status = date is null ? StatusReview : months < warn ? StatusOk : months <= old ? StatusReview : StatusOld;
                        result.Add(new DriverInfo(kind, name, $"{r["DriverVersion"]}".Trim(), date, months, status,
                            OfficialUrl(kind, maker, name), oem));
                    }
                    catch { /* un driver ilegible no impide mostrar el resto */ }
                }
            }
        }
        catch { /* sin WMI no hay lista: se devuelve lo que haya */ }
        return result;
    }

    private static bool Has(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnored(string name) => Ignored.Any(w => Has(name, w));

    private static bool IsGenericMicrosoft(string maker) => Has(maker, "Microsoft");

    private static string OfficialUrl(string kind, string maker, string name)
    {
        var s = maker + " " + name;
        if (Has(s, "NVIDIA")) return "https://www.nvidia.com/es-es/geforce/drivers/";
        if (Has(s, "Realtek")) return kind == "Red" ? "https://www.realtek.com/Download/List?cate_id=593" : "https://www.realtek.com/Download/List?cate_id=593";
        if (Has(s, "Killer") || Has(s, "Qualcomm")) return "https://www.intel.com/content/www/us/en/download/19779/";
        if (Has(s, "MediaTek")) return "https://www.mediatek.com/";
        if (Has(s, "AMD") || Has(s, "Advanced Micro") || Has(s, "Radeon")) return "https://www.amd.com/es/support/download/drivers.html";
        if (Has(s, "Intel")) return "https://www.intel.es/content/www/es/es/support/detect.html";
        return "https://www.intel.es/content/www/es/es/support/detect.html";
    }

    /// <summary>Soporte del fabricante del equipo, solo en portátiles.</summary>
    private static string? OemUrl()
    {
        try
        {
            int[] mobile = [8, 9, 10, 11, 12, 13, 14, 30, 31, 32];
            var laptop = false;
            foreach (var e in Query("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                if (e["ChassisTypes"] is ushort[] types && types.Any(t => mobile.Contains(t))) laptop = true;
            if (!laptop) return null;
            var maker = $"{Query("SELECT Manufacturer FROM Win32_ComputerSystem").FirstOrDefault()?["Manufacturer"]}";
            if (Has(maker, "Lenovo")) return "https://pcsupport.lenovo.com";
            if (Has(maker, "HP") || Has(maker, "Hewlett")) return "https://support.hp.com/es-es/drivers";
            if (Has(maker, "Dell")) return "https://www.dell.com/support/home/es-es";
            if (Has(maker, "ASUS")) return "https://www.asus.com/es/support/download-center/";
            if (Has(maker, "Acer")) return "https://www.acer.com/es-es/support";
            if (Has(maker, "Micro-Star") || Has(maker, "MSI")) return "https://es.msi.com/support";
        }
        catch { }
        return null;
    }

    private static List<ManagementObject> Query(string wql)
    {
        var list = new List<ManagementObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject o in searcher.Get()) list.Add(o);
        }
        catch { }
        return list;
    }
}
