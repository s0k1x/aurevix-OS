using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aurevix.Core;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

/// <summary>Lectura/escritura atómica de JSON (escribe a .tmp y reemplaza).</summary>
public static class JsonStore
{
    public static T LoadOrDefault<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.Options) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonDefaults.Options));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Preferencias del usuario.</summary>
public sealed class AurevixSettings
{
    public string UpdateChannel { get; set; } = "stable";
    public bool CheckUpdatesAutomatically { get; set; } = true;
    public int UpdateCheckIntervalHours { get; set; } = 4;
    public bool StartWithWindows { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool ReduceMotion { get; set; }
    /// <summary>AUREVIX Shell: estilo propio en la barra de tareas y el menú Inicio (activado por defecto).</summary>
    public bool ShellEnabled { get; set; } = true;
    public string Language { get; set; } = "es-ES";
    public int LogRetentionDays { get; set; } = 14;
    /// <summary>Por defecto no se envía nada al servidor salvo la consulta del manifiesto.</summary>
    public bool ShareAnonymousDiagnostics { get; set; }
}

/// <summary>Estado de la instalación (primer inicio, perfil activo, versión omitida…).</summary>
public sealed class AurevixState
{
    public bool FirstRunCompleted { get; set; }
    public DateTime? FirstRunCompletedAtUtc { get; set; }
    public string? ActiveProfile { get; set; }
    public string? PreviousProfile { get; set; }
    public string? SkippedVersion { get; set; }
    public DateTime? LastUpdateCheckUtc { get; set; }
    /// <summary>Obsoleto: secuencia única anterior a la separación por canal. Se conserva solo para leer estados antiguos.</summary>
    public string? LastSeenManifestSequence { get; set; }
    /// <summary>Última secuencia de manifiesto vista en cada canal (para detectar bajadas de versión sin confundir un cambio de canal).</summary>
    public Dictionary<string, long> ManifestSequenceByChannel { get; set; } = new();
}
