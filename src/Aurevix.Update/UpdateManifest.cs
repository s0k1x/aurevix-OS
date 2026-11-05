using System.Text.Json.Serialization;

namespace Aurevix.Update;

/// <summary>
/// Manifiesto de versiones publicado por el servidor (ver docs/UPDATE_MANIFEST.md).
/// El fichero manifest.json se firma byte a byte; la firma va en manifest.json.sig.
/// </summary>
public sealed class UpdateManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Product { get; set; } = "aurevix-os";
    public string Channel { get; set; } = "stable";
    /// <summary>Número monotónico creciente. El cliente rechaza secuencias menores a la última vista (anti-replay/downgrade).</summary>
    public long Sequence { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    /// <summary>El cliente rechaza manifiestos caducados (protección contra "congelación").</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    public ReleaseInfo? Latest { get; set; }
}

public sealed class ReleaseInfo
{
    public string Version { get; set; } = "";
    /// <summary>Versión mínima instalada desde la que se puede actualizar directamente.</summary>
    public string? MinimumVersion { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool RequiresRestart { get; set; }
    /// <summary>Despliegue gradual: 0-100 % de equipos.</summary>
    public int RolloutPercent { get; set; } = 100;
    public List<PackageInfo> Packages { get; set; } = [];
}

/// <summary>Se serializa en camelCase: appInstaller, themePack, tweakCatalog.</summary>
public enum PackageKind
{
    /// <summary>Instalador de la aplicación AUREVIX (Inno Setup). Se ejecuta con argumentos fijos definidos por el cliente.</summary>
    AppInstaller,
    /// <summary>Paquete de tema/fondos (zip de recursos, sin ejecutables).</summary>
    ThemePack,
    /// <summary>Catálogo de ajustes (JSON de datos, sin ejecutables).</summary>
    TweakCatalog
}

public sealed class PackageInfo
{
    public string Id { get; set; } = "";
    public PackageKind Kind { get; set; }
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>Fichero .sig que acompaña al manifiesto.</summary>
public sealed class ManifestSignature
{
    public string KeyId { get; set; } = "";
    public string Algorithm { get; set; } = ManifestSigner.Algorithm;
    public string Signature { get; set; } = "";
}
