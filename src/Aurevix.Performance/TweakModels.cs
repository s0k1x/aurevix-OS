namespace Aurevix.Performance;

public enum RiskLevel { Low, Medium, High }

public enum TweakCategory
{
    Startup,        // Inicio y aplicaciones en segundo plano
    Power,          // Energía y planes de rendimiento
    Gaming,         // Juegos y latencia
    Graphics,       // GPU
    Storage,        // Almacenamiento y limpieza segura
    Network,        // Red y conectividad
    Interface,      // Interfaz y efectos visuales
    Privacy,        // Privacidad y telemetría (dentro de las opciones legales)
    Maintenance,    // Mantenimiento y actualizaciones
    Security        // Opciones de seguridad que Microsoft permite relajar para juegos (solo opt-in)
}

/// <summary>Niveles de optimización predefinidos.</summary>
public enum OptimizationLevel { Recommended, Performance, Maximum }

/// <summary>
/// CurrentUser: HKCU del usuario (sin privilegios).
/// LocalMachine: HKLM (requiere AUREVIX Core Service).
/// UserPolicy: HKCU\Software\Policies del usuario que hace la petición (solo escribible con privilegios: lo hace el servicio en HKEY_USERS\&lt;SID&gt;).
/// </summary>
public enum RegistryHive { CurrentUser, LocalMachine, UserPolicy }
public enum RegistryValueKind { DWord, String }

/// <summary>Ámbito de ejecución: el Control Center aplica la parte de usuario y el servicio la de sistema.</summary>
public enum TweakScope { User, Machine, All }

public sealed class RegistryAction
{
    public RegistryHive Hive { get; set; } = RegistryHive.CurrentUser;
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public RegistryValueKind Kind { get; set; } = RegistryValueKind.DWord;
    /// <summary>Valor recomendado como texto. DWord admite negativos para valores ≥ 0x80000000 (p. ej. -1 = 0xFFFFFFFF).</summary>
    public string Value { get; set; } = "";
    /// <summary>Con comodines («*» en cualquier segmento de la clave), aplicar solo donde el valor ya existe (p. ej. EnableUlps de AMD).</summary>
    public bool OnlyIfExists { get; set; }
    /// <summary>Si el valor no existía, al deshacer se elimina esta clave completa (debe contener a Key).</summary>
    public string? RevertDeleteKey { get; set; }

    public TweakScope Scope => Hive == RegistryHive.CurrentUser ? TweakScope.User : TweakScope.Machine;
}

/// <summary>Activa o desactiva una tarea programada de Windows (ámbito sistema). Si la tarea no existe en esta versión, se omite.</summary>
public sealed class TaskAction
{
    public string Path { get; set; } = "";
    public bool Enabled { get; set; }
}

/// <summary>Valor de un ajuste de energía del plan activo (powercfg). Ámbito sistema.</summary>
public sealed class PowerSettingAction
{
    public Guid Subgroup { get; set; }
    public Guid Setting { get; set; }
    public uint Ac { get; set; }
    /// <summary>Si es null, el valor con batería no se toca.</summary>
    public uint? Dc { get; set; }
}

/// <summary>Condiciones de hardware para que un ajuste tenga sentido.</summary>
public sealed class TweakRequirements
{
    public double? MinRamGb { get; set; }
    public bool RequiresSsd { get; set; }
    public bool DesktopOnly { get; set; }
    /// <summary>Solo con GPU de este fabricante: "AMD", "NVIDIA" o "Intel".</summary>
    public string? GpuVendor { get; set; }
}

/// <summary>Datos del equipo usados para decidir qué ajustes son compatibles.</summary>
public sealed record SystemProfile(double RamGb, bool HasSsd, bool IsLaptop, string? GpuVendor)
{
    public static readonly SystemProfile Unknown = new(16, true, false, null);
}

/// <summary>Ajuste del catálogo propio. Cada ajuste es explicable, validable y reversible.</summary>
public sealed class TweakDefinition
{
    public string Id { get; set; } = "";
    public TweakCategory Category { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ExpectedEffect { get; set; } = "";
    public string PossibleImpact { get; set; } = "";
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public bool RequiresSignOut { get; set; }
    public bool RequiresRestart { get; set; }
    /// <summary>Compilación mínima de Windows (22000 = Windows 11 21H2).</summary>
    public int MinWindowsBuild { get; set; } = 19041;
    public string Reversal { get; set; } = "Se restaura el valor anterior guardado antes de aplicar.";
    /// <summary>Niveles predefinidos que incluyen este ajuste.</summary>
    public List<OptimizationLevel> Levels { get; set; } = [];
    /// <summary>Nunca se incluye en un nivel: solo se activa a mano en Personalizado, con aviso.</summary>
    public bool OptIn { get; set; }
    public string? Warning { get; set; }
    /// <summary>De dónde viene la idea (p. ej. "WinterOS Rev05 · Optimizar sistema"), para la documentación.</summary>
    public string? Origin { get; set; }
    public TweakRequirements? Requires { get; set; }
    /// <summary>Plan de energía a activar (se crea a partir de la plantilla de Windows si no existe).</summary>
    public Guid? PowerPlan { get; set; }
    public List<RegistryAction> Registry { get; set; } = [];
    public List<TaskAction> Tasks { get; set; } = [];
    public List<PowerSettingAction> Power { get; set; } = [];

    public bool HasUserPart => Registry.Any(r => r.Scope == TweakScope.User) || PowerPlan is not null;
    public bool HasMachinePart => Registry.Any(r => r.Scope == TweakScope.Machine) || Tasks.Count > 0 || Power.Count > 0;
    public bool RequiresAdmin => HasMachinePart;
}

public sealed class ProfileDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Planes de energía preferidos por orden; se usa el primero que exista en el equipo.</summary>
    public List<Guid> PreferredPowerPlans { get; set; } = [];
    public List<string> Tweaks { get; set; } = [];
}

public enum TweakState { NotApplied, Applied, Partial, Unknown, NotApplicable }

public sealed record TweakStatus(TweakDefinition Tweak, TweakState State, string? CurrentValueSummary, int Matched = 0, int Total = 0);
