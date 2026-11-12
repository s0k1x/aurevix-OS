using Aurevix.Core;

namespace Aurevix.Update;

public sealed record UpdateHistoryEntry(DateTime AtUtc, string FromVersion, string ToVersion, string Channel, string Result, string? InstallerBackupPath);

/// <summary>
/// Registro local de actualizaciones (versión anterior y nueva). Conserva el instalador
/// de la versión anterior para poder hacer rollback reinstalándolo.
/// </summary>
public sealed class UpdateHistory
{
    public List<UpdateHistoryEntry> Entries { get; set; } = [];

    public static string FilePath => Path.Combine(AurevixPaths.UserRoot, "update-history.json");

    public static UpdateHistory Load() => JsonStore.LoadOrDefault<UpdateHistory>(FilePath);
    public void Save() => JsonStore.Save(FilePath, this);

    public void Add(UpdateHistoryEntry e)
    {
        Entries.Add(e);
        if (Entries.Count > 50) Entries.RemoveRange(0, Entries.Count - 50);
    }

    /// <summary>Última actualización lanzada/instalada con un instalador anterior guardado para rollback.</summary>
    public UpdateHistoryEntry? LastRollbackCandidate() =>
        Entries.LastOrDefault(e => e.Result is "ok" or "launched" or "installed" && e.InstallerBackupPath is not null && File.Exists(e.InstallerBackupPath));

    /// <summary>Actualiza el resultado de la última entrada hacia esa versión (p. ej. "installed" al arrancar con ella). Devuelve false si no existe.</summary>
    public bool MarkResult(string version, string result)
    {
        var i = Entries.FindLastIndex(e => e.ToVersion == version);
        if (i < 0) return false;
        Entries[i] = Entries[i] with { Result = result };
        return true;
    }
}
