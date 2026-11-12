using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace Aurevix.ControlCenter.Services;

/// <summary>Resultado de una comprobación. Status: ok | review | info.</summary>
public sealed record HardwareCheck(string Title, string Status, string Detail, string Advice);

/// <summary>Revisión de hardware de solo lectura. Cada comprobación es independiente: si falla, se omite.</summary>
public static class HardwareChecks
{
    public static Task<IReadOnlyList<HardwareCheck>> RunAsync() => Task.Run<IReadOnlyList<HardwareCheck>>(() =>
    {
        var list = new List<HardwareCheck>();
        void Add(Func<IEnumerable<HardwareCheck>> f)
        {
            try { list.AddRange(f()); } catch { /* se omite */ }
        }
        Add(RamSpeed);
        Add(RamChannels);
        Add(DiskHealth);
        Add(FreeSpace);
        Add(Bios);
        Add(RefreshRate);
        list.Add(new HardwareCheck("Resizable BAR", "info",
            "No se puede comprobar de forma fiable desde aquí.",
            "Actívalo en la BIOS (Above 4G Decoding + Re-Size BAR) si tu gráfica es RTX 30+/RX 6000+/Arc."));
        return list;
    });

    private static List<ManagementObject> Query(string q, string? scope = null)
    {
        using var s = scope is null ? new ManagementObjectSearcher(q) : new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(q));
        return s.Get().Cast<ManagementObject>().ToList();
    }

    private static IEnumerable<HardwareCheck> RamSpeed()
    {
        var mods = Query("SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
        uint speed = 0, conf = 0;
        foreach (var m in mods)
        {
            speed = Math.Max(speed, Convert.ToUInt32(m["Speed"] ?? 0));
            var c = Convert.ToUInt32(m["ConfiguredClockSpeed"] ?? 0);
            conf = conf == 0 ? c : Math.Min(conf, c);
        }
        if (speed == 0 || conf == 0) yield break;
        if (conf < speed * 0.9)
            yield return new("RAM a su velocidad", "review", $"Tu RAM va a {conf} MHz pero admite {speed} MHz.",
                "Activa XMP (Intel) o EXPO (AMD) en la BIOS.");
        else
            yield return new("RAM a su velocidad", "ok", $"Tu RAM va a {conf} MHz.", "");
    }

    private static IEnumerable<HardwareCheck> RamChannels()
    {
        var mods = Query("SELECT BankLabel, DeviceLocator FROM Win32_PhysicalMemory");
        if (mods.Count == 0) yield break;
        var where = string.Join(", ", mods.Select(m => $"{m["DeviceLocator"]}".Trim()).Where(x => x.Length > 0));
        if (mods.Count == 1)
            yield return new("Canales de memoria", "review", $"Un solo módulo: memoria en un canal ({where}).",
                "Con dos módulos iguales la RAM rinde casi el doble en juegos.");
        else
            yield return new("Canales de memoria", "ok", $"{mods.Count} módulos de RAM ({where}).", "");
    }

    private static IEnumerable<HardwareCheck> DiskHealth()
    {
        foreach (var d in Query("SELECT FriendlyName, MediaType, HealthStatus FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
        {
            var name = $"{d["FriendlyName"]}".Trim();
            var type = Convert.ToInt32(d["MediaType"] ?? 0) switch { 3 => "HDD", 4 => "SSD", _ => "disco" };
            var h = Convert.ToInt32(d["HealthStatus"] ?? 0);
            var title = $"Salud del disco {name} ({type})";
            if (h == 0) yield return new(title, "ok", "El disco está sano.", "");
            else yield return new(title, "review", h == 1 ? "Windows avisa de posibles problemas en este disco." : "Windows indica que este disco está en mal estado.",
                "Haz copia de seguridad y revísalo con CrystalDiskInfo.");
        }
    }

    private static IEnumerable<HardwareCheck> FreeSpace()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root)) yield break;
        var d = new DriveInfo(root);
        var pct = d.TotalSize <= 0 ? 100 : d.AvailableFreeSpace * 100.0 / d.TotalSize;
        var detail = $"Libre {pct:0} % ({d.AvailableFreeSpace / 1_000_000_000} GB de {d.TotalSize / 1_000_000_000} GB).";
        if (pct < 15) yield return new("Espacio libre", "review", detail, "Libera espacio: los SSD se ralentizan casi llenos.");
        else yield return new("Espacio libre", "ok", detail, "");
    }

    private static IEnumerable<HardwareCheck> Bios()
    {
        var b = Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        if (b?["ReleaseDate"] is not string raw) yield break;
        var date = ManagementDateTimeConverter.ToDateTime(raw);
        var detail = $"Versión {b["SMBIOSBIOSVersion"]} del {date:d}.";
        if (date < DateTime.Now.AddYears(-2))
            yield return new("BIOS", "review", detail, "Busca una BIOS más nueva en la web de tu placa: mejora estabilidad, RAM y seguridad.");
        else
            yield return new("BIOS", "ok", detail, "");
    }

    private static IEnumerable<HardwareCheck> RefreshRate()
    {
        var cur = NewMode();
        if (!EnumDisplaySettings(null, -1, ref cur) || cur.dmDisplayFrequency == 0) yield break;
        uint max = cur.dmDisplayFrequency;
        for (var i = 0; i < 5000; i++)
        {
            var m = NewMode();
            if (!EnumDisplaySettings(null, i, ref m)) break;
            if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmDisplayFrequency > max)
                max = m.dmDisplayFrequency;
        }
        if (cur.dmDisplayFrequency < max)
            yield return new("Frecuencia del monitor", "review", $"Tu pantalla va a {cur.dmDisplayFrequency} Hz y admite {max} Hz.",
                "Configuración > Pantalla > Pantalla avanzada > Frecuencia de actualización.");
        else
            yield return new("Frecuencia del monitor", "ok", $"Tu pantalla va a {cur.dmDisplayFrequency} Hz, su máximo.", "");
    }

    private static DEVMODE NewMode() => new() { dmDeviceName = new string('\0', 32), dmFormName = new string('\0', 32), dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
}
