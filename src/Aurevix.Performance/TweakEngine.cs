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
    /// <summary>Clave que se elimina al deshacer si el valor no existía (p. ej. el menú contextual clásico).</summary>
    public string? DeleteKeyOnRevert { get; set; }
}

public sealed class TaskBackupEntry
{
    public string Path { get; set; } = "";
    public bool WasEnabled { get; set; }
}

public sealed class PowerSettingBackupEntry
{
    public Guid Subgroup { get; set; }
    public Guid Setting { get; set; }
    public uint Ac { get; set; }
    public uint Dc { get; set; }
}

/// <summary>Copia de seguridad creada antes de cada cambio. Permite deshacerlo exactamente.</summary>
public sealed class ChangeBackup
{
    public string Id { get; set; } = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..23];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = "";
    public List<string> TweakIds { get; set; } = [];
    public string? ProfileId { get; set; }
    /// <summary>SID de quien pidió el cambio (solo en copias del servicio). Solo ese usuario puede deshacerlo.</summary>
    public string? OwnerSid { get; set; }
    public Guid? PreviousPowerPlan { get; set; }
    public bool RequiresRestart { get; set; }
    public List<RegistryBackupEntry> Registry { get; set; } = [];
    public List<TaskBackupEntry> Tasks { get; set; } = [];
    public List<PowerSettingBackupEntry> Power { get; set; } = [];
}

public sealed record ApplyResult(bool Success, ChangeBackup? Backup, IReadOnlyList<string> Messages);

public sealed record ChangePreview(string TweakId, string Title, string Location, string? CurrentValue, string NewValue);

/// <summary>Dependencias del motor. Cada proceso usa el ámbito que le corresponde.</summary>
public sealed class TweakContext
{
    public required IRegistryAccess Registry { get; init; }
    public IPowerPlanAccess? PowerPlans { get; init; }
    public IPowerSettingAccess? PowerSettings { get; init; }
    public ITaskAccess? Tasks { get; init; }
    public required string BackupDirectory { get; init; }
    public int WindowsBuild { get; init; } = 22631;
    public TweakScope Scope { get; init; } = TweakScope.All;
    public SystemProfile System { get; init; } = SystemProfile.Unknown;
}

/// <summary>
/// Motor de ajustes: vista previa → copia de seguridad → aplicar → validar → (deshacer).
/// Si una validación falla, revierte automáticamente todo lo aplicado en esa operación.
/// El Control Center ejecuta el ámbito User y AUREVIX Core Service el ámbito Machine.
/// </summary>
public sealed class TweakEngine
{
    private readonly TweakContext _c;

    public TweakEngine(TweakContext context) => _c = context;

    /// <summary>Constructor simple (pruebas y perfiles de usuario).</summary>
    public TweakEngine(IRegistryAccess registry, IPowerPlanAccess? power, string backupDirectory, int windowsBuild, bool isAdmin)
        : this(new TweakContext { Registry = registry, PowerPlans = power, BackupDirectory = backupDirectory, WindowsBuild = windowsBuild, Scope = TweakScope.All }) { }

    public TweakScope Scope => _c.Scope;

    public static IReadOnlyList<TweakDefinition> LoadCatalog() => LoadResource<List<TweakDefinition>>("Aurevix.Performance.tweaks.json");
    public static IReadOnlyList<ProfileDefinition> LoadProfiles() => LoadResource<List<ProfileDefinition>>("Aurevix.Performance.profiles.json");

    private static T LoadResource<T>(string name)
    {
        using var s = typeof(TweakEngine).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Recurso no encontrado: {name}");
        return JsonSerializer.Deserialize<T>(s, JsonDefaults.Options)!;
    }

    /// <summary>Ajustes de un nivel predefinido compatibles con este equipo (nunca incluye los OptIn).</summary>
    public static IReadOnlyList<TweakDefinition> ForLevel(IEnumerable<TweakDefinition> catalog, OptimizationLevel level) =>
        catalog.Where(t => !t.OptIn && t.Levels.Contains(level)).ToList();

    private bool InScope(TweakScope s) => _c.Scope == TweakScope.All || _c.Scope == s;
    private IEnumerable<RegistryAction> Reg(TweakDefinition t) => t.Registry.Where(r => InScope(r.Scope)).SelectMany(Expand);

    /// <summary>
    /// Comodines: cada segmento «*» de la clave se expande a las subclaves existentes (p. ej. todas las interfaces de red,
    /// o Enum\USB\*\*\Device Parameters). Con OnlyIfExists solo se toca donde el valor ya existe.
    /// </summary>
    private IEnumerable<RegistryAction> Expand(RegistryAction a)
    {
        if (!a.Key.Split('\\').Contains("*")) return [a];
        var keys = new List<string> { "" };
        foreach (var seg in a.Key.Split('\\'))
        {
            if (seg != "*") { keys = keys.Select(k => k.Length == 0 ? seg : $@"{k}\{seg}").ToList(); continue; }
            var next = new List<string>();
            foreach (var k in keys)
            {
                try { next.AddRange(_c.Registry.ListSubKeys(a.Hive, k).Select(sub => $@"{k}\{sub}")); } catch { }
            }
            keys = next;
            if (keys.Count > 2000) return []; // límite de seguridad: nunca aplicar rutas incompletas
        }
        var result = new List<RegistryAction>();
        foreach (var k in keys)
        {
            if (a.OnlyIfExists)
            {
                try { if (_c.Registry.GetValue(a.Hive, k, a.Name) is null) continue; } catch { continue; }
            }
            result.Add(new RegistryAction { Hive = a.Hive, Key = k, Name = a.Name, Kind = a.Kind, Value = a.Value, RevertDeleteKey = a.RevertDeleteKey });
        }
        return result;
    }

    private IEnumerable<TaskAction> Tasks(TweakDefinition t) => InScope(TweakScope.Machine) ? t.Tasks : [];
    private IEnumerable<PowerSettingAction> Power(TweakDefinition t) => InScope(TweakScope.Machine) ? t.Power : [];
    private bool PlanInScope(TweakDefinition t) => t.PowerPlan is not null && InScope(TweakScope.User);

    /// <summary>Motivo por el que el ajuste no es adecuado para este equipo, o null.</summary>
    public string? CompatibilityProblem(TweakDefinition t)
    {
        if (_c.WindowsBuild < t.MinWindowsBuild) return $"Requiere Windows compilación {t.MinWindowsBuild} o superior.";
        if (t.Requires is { } r)
        {
            if (r.MinRamGb is { } min && _c.System.RamGb < min) return $"Solo recomendado con {min:0} GB de RAM o más.";
            if (r.RequiresSsd && !_c.System.HasSsd) return "Solo recomendado con el sistema en un SSD.";
            if (r.DesktopOnly && _c.System.IsLaptop) return "Solo para equipos de sobremesa o mini PC (en portátiles reduce mucho la batería).";
            if (r.GpuVendor is { } v && !string.Equals(_c.System.GpuVendor, v, StringComparison.OrdinalIgnoreCase)) return $"Solo para gráficas {v}.";
            if (r.CpuVendor is { } cv && !string.Equals(_c.System.CpuVendor, cv, StringComparison.OrdinalIgnoreCase)) return $"Solo para procesadores {cv}.";
            if (r.MinThreads is { } mt && _c.System.Threads < mt) return $"Necesita un procesador de al menos {mt} hilos.";
        }
        return null;
    }

    public TweakStatus GetStatus(TweakDefinition t)
    {
        try
        {
            int matched = 0, total = 0;
            var parts = new List<string>();
            foreach (var a in Reg(t))
            {
                total++;
                var v = _c.Registry.GetValue(a.Hive, a.Key, a.Name);
                if (Normalize(v) == Normalize(a.Value)) matched++;
                parts.Add($"{a.Name}={v ?? "(sin definir)"}");
            }
            foreach (var a in Tasks(t))
            {
                var en = _c.Tasks?.IsEnabled(a.Path);
                if (en is null) continue; // tarea inexistente en esta versión
                total++;
                if (en == a.Enabled) matched++;
            }
            foreach (var a in Power(t))
            {
                var v = _c.PowerSettings?.Get(a.Subgroup, a.Setting);
                total++;
                if (v is { } x && x.Ac == a.Ac && (a.Dc is null || x.Dc == a.Dc)) matched++;
            }
            if (PlanInScope(t) && _c.PowerPlans is { } pp)
            {
                total++;
                var active = pp.GetActivePlan();
                if (active == t.PowerPlan || active == PowerPlans.DerivedFrom(t.PowerPlan!.Value)) matched++;
            }
            var state = total == 0 ? TweakState.NotApplicable : matched == 0 ? TweakState.NotApplied : matched == total ? TweakState.Applied : TweakState.Partial;
            return new(t, state, string.Join(", ", parts), matched, total);
        }
        catch (Exception ex)
        {
            return new(t, TweakState.Unknown, ex.Message);
        }
    }

    public IReadOnlyList<ChangePreview> Preview(IEnumerable<TweakDefinition> tweaks) =>
        tweaks.SelectMany(t => Reg(t).Select(a => new ChangePreview(t.Id, t.Title, Location(a),
                _c.Registry.GetValue(a.Hive, a.Key, a.Name), a.Value))
            .Concat(Tasks(t).Select(a => new ChangePreview(t.Id, t.Title, "Tarea " + a.Path, null, a.Enabled ? "activada" : "desactivada")))
            .Concat(Power(t).Select(a => new ChangePreview(t.Id, t.Title, $"Energía {a.Setting}", null, a.Ac.ToString())))
            .Concat(PlanInScope(t) ? [new ChangePreview(t.Id, t.Title, "Plan de energía", null, t.PowerPlan.ToString()!)] : []))
        .ToList();

    private static string Location(RegistryAction a) =>
        $"{a.Hive switch { RegistryHive.CurrentUser => "HKCU", RegistryHive.LocalMachine => "HKLM", _ => "HKCU (directiva)" }}\\{a.Key}\\{a.Name}";

    public ApplyResult Apply(IReadOnlyList<TweakDefinition> tweaks, string description, Guid? powerPlan = null, string? profileId = null, string? ownerSid = null)
    {
        var messages = new List<string>();
        foreach (var t in tweaks)
        {
            var problem = CompatibilityProblem(t);
            if (problem is not null) return new(false, null, [$"{t.Title}: {problem}"]);
        }

        // 1. Copia de seguridad de TODO antes de tocar nada.
        var backup = new ChangeBackup
        {
            Description = description, TweakIds = tweaks.Select(t => t.Id).ToList(), ProfileId = profileId, OwnerSid = ownerSid,
            RequiresRestart = tweaks.Any(t => t.RequiresRestart)
        };
        foreach (var a in tweaks.SelectMany(Reg))
        {
            var prev = _c.Registry.GetValue(a.Hive, a.Key, a.Name);
            backup.Registry.Add(new RegistryBackupEntry
            {
                Hive = a.Hive, Key = a.Key, Name = a.Name, Kind = a.Kind, Existed = prev is not null, PreviousValue = prev,
                DeleteKeyOnRevert = prev is null ? a.RevertDeleteKey : null
            });
        }
        foreach (var a in tweaks.SelectMany(Tasks))
            if (_c.Tasks?.IsEnabled(a.Path) is { } en) backup.Tasks.Add(new TaskBackupEntry { Path = a.Path, WasEnabled = en });
        foreach (var a in tweaks.SelectMany(Power))
            if (_c.PowerSettings?.Get(a.Subgroup, a.Setting) is { } v) backup.Power.Add(new PowerSettingBackupEntry { Subgroup = a.Subgroup, Setting = a.Setting, Ac = v.Ac, Dc = v.Dc });

        // Plan de energía: el del perfil o el último ajuste que lo defina.
        var wantedPlan = powerPlan ?? tweaks.LastOrDefault(PlanInScope)?.PowerPlan;
        Guid? planToSet = null;
        if (wantedPlan is { } wanted && _c.PowerPlans is { } pp)
        {
            try
            {
                backup.PreviousPowerPlan = pp.GetActivePlan();
                planToSet = powerPlan is not null
                    ? (pp.ListPlans().Any(p => p.Id == wanted) ? wanted : null)
                    : pp.EnsurePlan(wanted);
                if (planToSet is null) messages.Add("El plan de energía solicitado no existe en este equipo; se mantiene el actual.");
            }
            catch (Exception ex) { messages.Add($"No se pudo preparar el plan de energía: {ex.Message}"); }
        }
        SaveBackup(backup);

        // 2. Aplicar y 3. validar.
        try
        {
            foreach (var t in tweaks)
            {
                foreach (var a in Reg(t)) _c.Registry.SetValue(a.Hive, a.Key, a.Name, a.Kind, a.Value);
                foreach (var a in Tasks(t)) _c.Tasks?.SetEnabled(a.Path, a.Enabled);
                foreach (var a in Power(t)) _c.PowerSettings?.Set(a.Subgroup, a.Setting, a.Ac, a.Dc);

                var st = GetStatusWithoutPlan(t);
                if (st is TweakState.NotApplied or TweakState.Partial or TweakState.Unknown)
                    throw new InvalidOperationException($"La validación de '{t.Title}' ha fallado.");
                messages.Add($"Aplicado: {t.Title}{(t.RequiresRestart ? " (requiere reiniciar)" : t.RequiresSignOut ? " (requiere cerrar sesión)" : "")}");
            }
            if (planToSet is { } plan)
            {
                _c.PowerPlans!.SetActivePlan(plan);
                messages.Add("Plan de energía actualizado.");
            }
            AurevixLog.Info($"Cambios aplicados ({description}, ámbito {_c.Scope}). Backup {backup.Id}.");
            return new(true, backup, messages);
        }
        catch (Exception ex)
        {
            AurevixLog.Error($"Fallo al aplicar ({description}); revirtiendo.", ex);
            Revert(backup);
            messages.Add($"Error: {ex.Message} Se han deshecho los cambios.");
            return new(false, backup, messages);
        }
    }

    private TweakState GetStatusWithoutPlan(TweakDefinition t)
    {
        var copy = new TweakDefinition { Id = t.Id, Title = t.Title, Registry = t.Registry, Tasks = t.Tasks, Power = t.Power };
        return GetStatus(copy).State;
    }

    public void Revert(ChangeBackup backup)
    {
        foreach (var e in Enumerable.Reverse(backup.Registry))
        {
            try
            {
                if (e.Existed && e.PreviousValue is not null) _c.Registry.SetValue(e.Hive, e.Key, e.Name, e.Kind, e.PreviousValue);
                else if (e.DeleteKeyOnRevert is { Length: > 0 } dk) _c.Registry.DeleteKey(e.Hive, dk);
                else _c.Registry.DeleteValue(e.Hive, e.Key, e.Name);
            }
            catch (Exception ex) { AurevixLog.Error($"No se pudo revertir {e.Key}\\{e.Name}", ex); }
        }
        foreach (var e in backup.Tasks)
        {
            try { _c.Tasks?.SetEnabled(e.Path, e.WasEnabled); }
            catch (Exception ex) { AurevixLog.Error($"No se pudo revertir la tarea {e.Path}", ex); }
        }
        foreach (var e in backup.Power)
        {
            try { _c.PowerSettings?.Set(e.Subgroup, e.Setting, e.Ac, e.Dc); }
            catch (Exception ex) { AurevixLog.Error($"No se pudo revertir el ajuste de energía {e.Setting}", ex); }
        }
        if (backup.PreviousPowerPlan is { } plan && _c.PowerPlans is not null)
        {
            try { _c.PowerPlans.SetActivePlan(plan); } catch (Exception ex) { AurevixLog.Error("No se pudo restaurar el plan de energía", ex); }
        }
        AurevixLog.Info($"Backup {backup.Id} revertido.");
    }

    public IReadOnlyList<ChangeBackup> ListBackups()
    {
        if (!Directory.Exists(_c.BackupDirectory)) return [];
        return Directory.EnumerateFiles(_c.BackupDirectory, "change-*.json")
            .Select(f => { try { return JsonSerializer.Deserialize<ChangeBackup>(File.ReadAllText(f), JsonDefaults.Options); } catch { return null; } })
            .Where(b => b is not null).Cast<ChangeBackup>()
            .OrderByDescending(b => b.CreatedAtUtc).ToList();
    }

    public ChangeBackup? FindBackup(string id) => ListBackups().FirstOrDefault(b => b.Id == id);

    public void DeleteBackup(string id)
    {
        try { File.Delete(Path.Combine(_c.BackupDirectory, $"change-{id}.json")); } catch { }
    }

    private void SaveBackup(ChangeBackup b) => JsonStore.Save(Path.Combine(_c.BackupDirectory, $"change-{b.Id}.json"), b);

    private static string? Normalize(string? v) => v?.Trim();
}
