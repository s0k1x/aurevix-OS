namespace Aurevix.Core.Diagnostics;

/// <summary>Nivel de un hallazgo del diagnóstico, tal como exige el prompt.</summary>
public enum FindingLevel
{
    /// <summary>Correcto.</summary>
    Ok,
    /// <summary>Revisar: indicio, no fallo confirmado.</summary>
    Review,
    /// <summary>Requiere atención.</summary>
    Attention,
    /// <summary>Información no disponible (sensor o API no accesible). Nunca se inventa.</summary>
    Unavailable
}

public sealed record Finding(string Area, string Title, FindingLevel Level, string Detail, string? Recommendation = null);

public sealed record CpuInfo(string Name, int Cores, int LogicalProcessors, int MaxClockMhz);
public sealed record GpuInfo(string Name, string? DriverVersion, DateTime? DriverDate, long? AdapterRamBytes);
public sealed record DiskInfo(string Model, string MediaType, long SizeBytes, string? HealthStatus, string? BusType);
public sealed record VolumeInfo(string Letter, string? Label, string FileSystem, long SizeBytes, long FreeBytes);
public sealed record NetworkAdapterInfo(string Name, string? Type, bool Connected, long? SpeedBps);
public sealed record DisplayInfo(string Name, int? Width, int? Height, int? RefreshRate);
public sealed record BatteryInfo(int? ChargePercent, string? Status, int? DesignCapacity, int? FullChargeCapacity);
public sealed record ProblemDevice(string Name, int ErrorCode, string? DeviceClass);
public sealed record TemperatureReading(string Sensor, double Celsius, string Source);

public sealed record OsInfo(string Caption, string Version, string Build, string Edition, string Architecture, DateTime? InstallDate, DateTime? LastBoot);

public sealed record SystemSnapshot
{
    public DateTime CollectedAtUtc { get; init; } = DateTime.UtcNow;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public OsInfo? Os { get; init; }
    public CpuInfo? Cpu { get; init; }
    public long? TotalMemoryBytes { get; init; }
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];
    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = [];
    public IReadOnlyList<NetworkAdapterInfo> Network { get; init; } = [];
    public IReadOnlyList<DisplayInfo> Displays { get; init; } = [];
    public BatteryInfo? Battery { get; init; }
    public IReadOnlyList<ProblemDevice> ProblemDevices { get; init; } = [];
    /// <summary>Vacío si no hay fuente fiable. La UI debe mostrar "no disponible".</summary>
    public IReadOnlyList<TemperatureReading> Temperatures { get; init; } = [];
    public int? PendingWindowsUpdates { get; init; }
    /// <summary>Errores al recopilar (área → mensaje). Permite ver qué no se pudo leer.</summary>
    public IReadOnlyDictionary<string, string> CollectionErrors { get; init; } = new Dictionary<string, string>();
}

public sealed record DiagnosticReport(SystemSnapshot Snapshot, IReadOnlyList<Finding> Findings)
{
    public FindingLevel Overall =>
        Findings.Any(f => f.Level == FindingLevel.Attention) ? FindingLevel.Attention :
        Findings.Any(f => f.Level == FindingLevel.Review) ? FindingLevel.Review : FindingLevel.Ok;
}
