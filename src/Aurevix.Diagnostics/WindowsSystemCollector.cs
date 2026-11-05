using System.Management;
using System.Runtime.Versioning;
using Aurevix.Core.Diagnostics;
using Aurevix.Core.Logging;

namespace Aurevix.Diagnostics;

/// <summary>
/// Recopila información del equipo usando WMI/CIM (APIs oficiales de Windows).
/// Es de SOLO LECTURA: no modifica nada. No recoge nombres de usuario ni datos personales.
/// Cada área es independiente: si una falla, se registra en CollectionErrors y se continúa.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemCollector
{
    private readonly Dictionary<string, string> _errors = new();

    public async Task<SystemSnapshot> CollectAsync(bool includeWindowsUpdate = true, CancellationToken ct = default)
    {
        _errors.Clear();
        var snapshot = await Task.Run(() =>
        {
            var cs = Try("Equipo", () => Query("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault());
            return new SystemSnapshot
            {
                Manufacturer = cs?["Manufacturer"]?.ToString()?.Trim(),
                Model = cs?["Model"]?.ToString()?.Trim(),
                TotalMemoryBytes = cs?["TotalPhysicalMemory"] is { } mem ? Convert.ToInt64(mem) : null,
                Os = Try("Sistema", ReadOs),
                Cpu = Try("Procesador", ReadCpu),
                Gpus = Try("Gráficos", ReadGpus) ?? [],
                Disks = Try("Discos", ReadDisks) ?? [],
                Volumes = Try("Volúmenes", ReadVolumes) ?? [],
                Network = Try("Red", ReadNetwork) ?? [],
                Displays = Try("Pantalla", ReadDisplays) ?? [],
                Battery = Try("Batería", ReadBattery),
                ProblemDevices = Try("Controladores", ReadProblemDevices) ?? [],
                // Temperaturas: WMI (MSAcpi_ThermalZoneTemperature) no es fiable en la mayoría de equipos
                // y requiere administrador. Hasta integrar una fuente validada, no se muestran valores.
                Temperatures = [],
            };
        }, ct);

        int? pending = null;
        if (includeWindowsUpdate)
            pending = await PendingWindowsUpdatesAsync(TimeSpan.FromSeconds(90), ct);

        return snapshot with { PendingWindowsUpdates = pending, CollectionErrors = new Dictionary<string, string>(_errors) };
    }

    private T? Try<T>(string area, Func<T> f)
    {
        try { return f(); }
        catch (Exception ex)
        {
            _errors[area] = "No se pudo leer esta información.";
            AurevixLog.Warn($"Diagnóstico: fallo en {area}: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    private static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }

    private static OsInfo ReadOs()
    {
        var o = Query("SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem").First();
        var edition = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", null)?.ToString() ?? "";
        var display = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null)?.ToString();
        var ubr = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", null);
        var build = o["BuildNumber"]?.ToString() ?? "";
        return new OsInfo(
            o["Caption"]?.ToString()?.Trim() ?? "Windows",
            display ?? o["Version"]?.ToString() ?? "",
            ubr is null ? build : $"{build}.{ubr}",
            edition,
            o["OSArchitecture"]?.ToString() ?? "",
            ParseCim(o["InstallDate"]),
            ParseCim(o["LastBootUpTime"]));
    }

    private static CpuInfo ReadCpu()
    {
        var c = Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").First();
        return new CpuInfo(c["Name"]?.ToString()?.Trim() ?? "", ToInt(c["NumberOfCores"]), ToInt(c["NumberOfLogicalProcessors"]), ToInt(c["MaxClockSpeed"]));
    }

    private static IReadOnlyList<GpuInfo> ReadGpus() =>
        Query("SELECT Name, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController")
            .Select(g => new GpuInfo(g["Name"]?.ToString() ?? "GPU", g["DriverVersion"]?.ToString(), ParseCim(g["DriverDate"]),
                g["AdapterRAM"] is { } r ? Convert.ToInt64(r) : null))
            .ToList();

    private static IReadOnlyList<DiskInfo> ReadDisks()
    {
        // MSFT_PhysicalDisk da HealthStatus (0 Healthy, 1 Warning, 2 Unhealthy) y tipo de medio, sin requerir administrador.
        return Query("SELECT FriendlyName, MediaType, Size, HealthStatus, BusType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
            .Select(d => new DiskInfo(
                d["FriendlyName"]?.ToString() ?? "Disco",
                ToInt(d["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Desconocido" },
                d["Size"] is { } s ? Convert.ToInt64(s) : 0,
                ToInt(d["HealthStatus"]) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => null },
                ToInt(d["BusType"]) switch { 7 => "USB", 11 => "SATA", 17 => "NVMe", _ => null }))
            .ToList();
    }

    private static IReadOnlyList<VolumeInfo> ReadVolumes() =>
        Query("SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3")
            .Select(v => new VolumeInfo(v["DeviceID"]?.ToString() ?? "", v["VolumeName"]?.ToString(), v["FileSystem"]?.ToString() ?? "",
                v["Size"] is { } s ? Convert.ToInt64(s) : 0, v["FreeSpace"] is { } f ? Convert.ToInt64(f) : 0))
            .ToList();

    private static IReadOnlyList<NetworkAdapterInfo> ReadNetwork() =>
        Query("SELECT Name, AdapterType, NetConnectionStatus, Speed FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE")
            .Select(n => new NetworkAdapterInfo(n["Name"]?.ToString() ?? "", n["AdapterType"]?.ToString(), ToInt(n["NetConnectionStatus"]) == 2,
                n["Speed"] is { } s ? Convert.ToInt64(s) : null))
            .ToList();

    private static IReadOnlyList<DisplayInfo> ReadDisplays() =>
        Query("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController")
            .Where(d => d["CurrentHorizontalResolution"] is not null)
            .Select(d => new DisplayInfo(d["Name"]?.ToString() ?? "", ToInt(d["CurrentHorizontalResolution"]), ToInt(d["CurrentVerticalResolution"]), ToInt(d["CurrentRefreshRate"])))
            .ToList();

    private static BatteryInfo? ReadBattery()
    {
        var b = Query("SELECT EstimatedChargeRemaining, BatteryStatus, DesignCapacity, FullChargeCapacity FROM Win32_Battery").FirstOrDefault();
        if (b is null) return null; // Equipo de sobremesa / mini PC sin batería.
        return new BatteryInfo(ToInt(b["EstimatedChargeRemaining"]), ToInt(b["BatteryStatus"]) == 2 ? "Conectado a corriente" : "Con batería",
            b["DesignCapacity"] is { } d ? ToInt(d) : null, b["FullChargeCapacity"] is { } f ? ToInt(f) : null);
    }

    private static IReadOnlyList<ProblemDevice> ReadProblemDevices() =>
        Query("SELECT Name, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Select(d => new ProblemDevice(d["Name"]?.ToString() ?? "Dispositivo desconocido", ToInt(d["ConfigManagerErrorCode"]), d["PNPClass"]?.ToString()))
            .ToList();

    /// <summary>Consulta Windows Update mediante la API COM oficial (Microsoft.Update.Session). Solo lectura.</summary>
    private async Task<int?> PendingWindowsUpdatesAsync(TimeSpan timeout, CancellationToken ct)
    {
        var task = Task.Factory.StartNew(() =>
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (type is null) return (int?)null;
            dynamic session = Activator.CreateInstance(type)!;
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.Online = false; // Usa la caché local: rápido y sin forzar una búsqueda en red.
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0");
            return (int?)result.Updates.Count;
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            var done = await Task.WhenAny(task, Task.Delay(timeout, ct));
            if (done == task) return await task;
            _errors["Windows Update"] = "La consulta de Windows Update tardó demasiado.";
        }
        catch (Exception ex)
        {
            _errors["Windows Update"] = "No se pudo consultar Windows Update.";
            AurevixLog.Warn($"Windows Update: {ex.Message}");
        }
        return null;
    }

    private static int ToInt(object? o) => o is null ? 0 : Convert.ToInt32(o);

    private static DateTime? ParseCim(object? o)
    {
        if (o is not string s || s.Length < 14) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s).ToUniversalTime(); } catch { return null; }
    }
}
