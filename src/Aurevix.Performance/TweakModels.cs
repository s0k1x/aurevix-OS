namespace Aurevix.Performance;

public enum RiskLevel { Low, Medium, High }

public enum TweakCategory
{
    Startup,        // Inicio y aplicaciones en segundo plano
    Power,          // Energía y planes de rendimiento
    Gaming,         // Juegos y latencia
    Storage,        // Almacenamiento y limpieza segura
    Network,        // Red y conectividad
    Interface,      // Interfaz y efectos visuales
    Privacy,        // Privacidad y telemetría (dentro de las opciones legales)
    Maintenance     // Mantenimiento y actualizaciones
}

public enum RegistryHive { CurrentUser, LocalMachine }
public enum RegistryValueKind { DWord, String }

public sealed class RegistryAction
{
    public RegistryHive Hive { get; set; } = RegistryHive.CurrentUser;
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public RegistryValueKind Kind { get; set; } = RegistryValueKind.DWord;
    /// <summary>Valor recomendado (se guarda como texto; DWord se parsea a entero).</summary>
    public string Value { get; set; } = "";
}

/// <summary>
/// Ajuste del catálogo propio. Cada ajuste es explicable, validable y reversible.
/// </summary>
public sealed class TweakDefinition
{
    public string Id { get; set; } = "";
    public TweakCategory Category { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ExpectedEffect { get; set; } = "";
    public string PossibleImpact { get; set; } = "";
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public bool RequiresAdmin { get; set; }
    public bool RequiresSignOut { get; set; }
    /// <summary>Compilación mínima de Windows (22000 = Windows 11 21H2).</summary>
    public int MinWindowsBuild { get; set; } = 22000;
    public string Reversal { get; set; } = "Se restaura el valor anterior guardado antes de aplicar.";
    public List<RegistryAction> Registry { get; set; } = [];
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

public enum TweakState { NotApplied, Applied, Partial, Unknown }

public sealed record TweakStatus(TweakDefinition Tweak, TweakState State, string? CurrentValueSummary);
