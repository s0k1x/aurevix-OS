using System.Text.Json;
using Aurevix.Core;
using Aurevix.Core.Logging;

namespace Aurevix.Performance;

public sealed class RegistryBackupEntry
{
    public RegistryHive Hive { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public RegistryValueKind Kind { get; set; }
    public bool Existed { get; set; }
    public string? PreviousValue { get; set; }
}

/// <summary>Copia de seguridad creada antes de cada cambio. Permite deshacerlo exactamente.</summary>
public sealed class ChangeBackup
{
    public string Id { get; set; } = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..23];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = "";
    public List<string> TweakIds { get; set; } = [];
    public string? ProfileId { get; set; }
    public Guid? PreviousPowerPlan { get; set; }
    public List<RegistryBackupEntry> Registry { get; set; } = [];
}

public sealed record ApplyResult(bool Success, ChangeBackup? Backup, IReadOnlyList<string> Messages);

public sealed record ChangePreview(string TweakId, string Title, string Location, string? CurrentValue, string NewValue);

/// <summary>
/// Motor de ajustes: vista previa → copia de seguridad → aplicar → validar → (deshacer).
/// Si una validación falla, revierte automáticamente lo aplicado.
/// </summary>
public sealed class TweakEngine(IRegistryAccess registry, IPowerPlanAccess? power, string backupDirectory, int windowsBuild, bool isAdmin)
{
    public static IReadOnlyList<TweakDefinition> LoadCatalog() => LoadResource<List<TweakDefinition>>("Aurevix.Performance.tweaks.json");
    public static IReadOnlyList<ProfileDefinition> LoadProfiles() => LoadResource<List<ProfileDefinition>>("Aurevix.Performance.profiles.json");

    private static T LoadResource<T>(string name)
    {
        using var s = typeof(TweakEngine).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Recurso no encontrado: {name}");
        return JsonSerializer.Deserialize<T>(s, JsonDefaults.Options)!;
    }

    /// <summary>Indica si el ajuste es compatible y si se puede aplicar sin elevar privilegios.</summary>
    public string? CompatibilityProblem(TweakDefinition t)
    {
        if (windowsBuild < t.MinWindowsBuild) return $"Requiere Windows compilación {t.MinWindowsBuild} o superior.";
        if (t.RequiresAdmin && !isAdmin) return "Requiere privilegios de administrador (se hará a través de AUREVIX Core Service).";
        return null;
    }

    public TweakStatus GetStatus(TweakDefinition t)
    {
        try
        {
            var matches = t.Registry.Count(a => Normalize(registry.GetValue(a.Hive, a.Key, a.Name)) == Normalize(a.Value));
            var state = matches == 0 ? TweakState.NotApplied : matches == t.Registry.Count ? TweakState.Applied : TweakState.Partial;
            var summary = string.Join(", ", t.Registry.Select(a => $"{a.Name}={registry.GetValue(a.Hive, a.Key, a.Name) ?? "(sin definir)"}"));
            return new(t, state, summary);
        }
        catch (Exception ex)
        {
            return new(t, TweakState.Unknown, ex.Message);
        }
    }

    public IReadOnlyList<ChangePreview> Preview(IEnumerable<TweakDefinition> tweaks) =>
        tweaks.SelectMany(t => t.Registry.Select(a => new ChangePreview(t.Id, t.Title,
            $"{(a.Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{a.Key}\\{a.Name}",
            registry.GetValue(a.Hive, a.Key, a.Name), a.Value))).ToList();

    public ApplyResult Apply(IReadOnlyList<TweakDefinition> tweaks, string description, Guid? powerPlan = null, string? profileId = null)
    {
        var messages = new List<string>();
        foreach (var t in tweaks)
        {
            var problem = CompatibilityProblem(t);
            if (problem is not null) return new(false, null, [$"{t.Title}: {problem}"]);
        }

        // 1. Copia de seguridad de TODO antes de tocar nada.
        var backup = new ChangeBackup { Description = description, TweakIds = tweaks.Select(t => t.Id).ToList(), ProfileId = profileId };
        foreach (var a in tweaks.SelectMany(t => t.Registry))
        {
            var prev = registry.GetValue(a.Hive, a.Key, a.Name);
            backup.Registry.Add(new RegistryBackupEntry { Hive = a.Hive, Key = a.Key, Name = a.Name, Kind = a.Kind, Existed = prev is not null, PreviousValue = prev });
        }

        Guid? planToSet = null;
        if (powerPlan is { } wanted && power is not null)
        {
            try
            {
                backup.PreviousPowerPlan = power.GetActivePlan();
                if (power.ListPlans().Any(p => p.Id == wanted)) planToSet = wanted;
                else messages.Add("El plan de energía solicitado no existe en este equipo; se mantiene el actual.");
            }
            catch (Exception ex) { messages.Add($"No se pudo leer el plan de energía: {ex.Message}"); }
        }
        SaveBackup(backup);

        // 2. Aplicar y 3. validar.
        try
        {
            foreach (var t in tweaks)
            {
                foreach (var a in t.Registry) registry.SetValue(a.Hive, a.Key, a.Name, a.Kind, a.Value);
                if (GetStatus(t).State != TweakState.Applied)
                    throw new InvalidOperationException($"La validación de '{t.Title}' ha fallado.");
                messages.Add($"Aplicado: {t.Title}{(t.RequiresSignOut ? " (requiere cerrar sesión)" : "")}");
            }
            if (planToSet is { } plan)
            {
                power!.SetActivePlan(plan);
                messages.Add("Plan de energía actualizado.");
            }
            AurevixLog.Info($"Cambios aplicados ({description}). Backup {backup.Id}.");
            return new(true, backup, messages);
        }
        catch (Exception ex)
        {
            AurevixLog.Error($"Fallo al aplicar ({description}); revirtiendo.", ex);
            Revert(backup);
            messages.Add($"Error: {ex.Message}. Se han deshecho los cambios.");
            return new(false, backup, messages);
        }
    }

    public void Revert(ChangeBackup backup)
    {
        // Orden inverso para respetar valores repetidos.
        foreach (var e in Enumerable.Reverse(backup.Registry))
        {
            try
            {
                if (e.Existed && e.PreviousValue is not null) registry.SetValue(e.Hive, e.Key, e.Name, e.Kind, e.PreviousValue);
                else registry.DeleteValue(e.Hive, e.Key, e.Name);
            }
            catch (Exception ex) { AurevixLog.Error($"No se pudo revertir {e.Key}\\{e.Name}", ex); }
        }
        if (backup.PreviousPowerPlan is { } plan && power is not null)
        {
            try { power.SetActivePlan(plan); } catch (Exception ex) { AurevixLog.Error("No se pudo restaurar el plan de energía", ex); }
        }
        AurevixLog.Info($"Backup {backup.Id} revertido.");
    }

    public IReadOnlyList<ChangeBackup> ListBackups()
    {
        if (!Directory.Exists(backupDirectory)) return [];
        return Directory.EnumerateFiles(backupDirectory, "change-*.json")
            .Select(f => { try { return JsonSerializer.Deserialize<ChangeBackup>(File.ReadAllText(f), JsonDefaults.Options); } catch { return null; } })
            .Where(b => b is not null).Cast<ChangeBackup>()
            .OrderByDescending(b => b.CreatedAtUtc).ToList();
    }

    private void SaveBackup(ChangeBackup b) => JsonStore.Save(Path.Combine(backupDirectory, $"change-{b.Id}.json"), b);

    private static string? Normalize(string? v) => v?.Trim();
}
