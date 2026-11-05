using System.IO;
using Aurevix.Core;
using Aurevix.Core.Logging;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

/// <summary>Un cambio de optimización completo: parte de usuario (Control Center) + parte de sistema (Core Service).</summary>
public sealed class OptimizationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Label { get; set; } = "";
    public List<string> TweakIds { get; set; } = [];
    public string? UserBackupId { get; set; }
    public string? MachineBackupId { get; set; }
    public bool RequiresRestart { get; set; }
}

public sealed class OptimizationHistory
{
    public List<OptimizationRecord> Records { get; set; } = [];
}

public sealed record OptimizationResult(bool Success, bool RequiresRestart, IReadOnlyList<string> Messages, OptimizationRecord? Record);

/// <summary>
/// Orquesta niveles y ajustes personalizados: aplica la parte HKCU en el proceso del usuario
/// y pide al servicio la parte de sistema. Si una falla, deshace la otra.
/// </summary>
public static class OptimizationManager
{
    private static string HistoryFile => Path.Combine(AurevixPaths.UserRoot, "optimizations.json");
    public static OptimizationHistory History => JsonStore.LoadOrDefault<OptimizationHistory>(HistoryFile);

    public static async Task<Dictionary<string, TweakState>> StatusAsync(IReadOnlyList<TweakDefinition> tweaks)
    {
        var result = new Dictionary<string, (int m, int t)>();
        foreach (var t in tweaks)
        {
            var s = AppServices.Tweaks.GetStatus(t);
            result[t.Id] = (s.Matched, s.Total);
        }
        var machine = tweaks.Where(t => t.HasMachinePart).Select(t => t.Id).ToList();
        if (machine.Count > 0)
        {
            var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "status", Ids = machine });
            if (r.Ok && r.Statuses is not null)
                foreach (var (id, st) in r.Statuses)
                    result[id] = (result[id].m + st.Matched, result[id].t + st.Total);
            else
                foreach (var id in machine) result[id] = (result[id].m, -1); // desconocido
        }
        return result.ToDictionary(kv => kv.Key, kv =>
            kv.Value.t < 0 ? TweakState.Unknown :
            kv.Value.t == 0 ? TweakState.NotApplicable :
            kv.Value.m == 0 ? TweakState.NotApplied :
            kv.Value.m == kv.Value.t ? TweakState.Applied : TweakState.Partial);
    }

    public static async Task<OptimizationResult> ApplyAsync(IReadOnlyList<TweakDefinition> tweaks, string label)
    {
        var messages = new List<string>();
        var compatible = new List<TweakDefinition>();
        foreach (var t in tweaks)
        {
            var problem = AppServices.Tweaks.CompatibilityProblem(t);
            if (problem is null) compatible.Add(t);
            else messages.Add($"Omitido: {t.Title} — {problem}");
        }
        if (compatible.Count == 0) return new(false, false, messages.Count > 0 ? messages : ["No hay ajustes compatibles que aplicar."], null);

        var record = new OptimizationRecord { Label = label, TweakIds = compatible.Select(t => t.Id).ToList() };

        if (compatible.Any(t => t.HasMachinePart) && !await CoreServiceClient.IsAvailableAsync())
            return new(false, false, ["AUREVIX Core Service no está en ejecución. Reinstala AUREVIX o reinicia el equipo."], null);

        // 1. Parte de usuario.
        var user = compatible.Where(t => t.HasUserPart).ToList();
        if (user.Count > 0)
        {
            var r = AppServices.Tweaks.Apply(user, label);
            messages.AddRange(r.Messages);
            if (!r.Success) return new(false, false, messages, null);
            record.UserBackupId = r.Backup?.Id;
        }

        // 2. Parte de sistema (servicio).
        var machine = compatible.Where(t => t.HasMachinePart).Select(t => t.Id).ToList();
        if (machine.Count > 0)
        {
            var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "apply", Ids = machine, Label = label }, TimeSpan.FromMinutes(3));
            messages.AddRange(r.Messages.Where(m => !messages.Contains(m)));
            if (!r.Ok)
            {
                if (record.UserBackupId is { } ub && AppServices.Tweaks.FindBackup(ub) is { } b) AppServices.Tweaks.Revert(b);
                messages.Add(r.Error ?? "El servicio no pudo aplicar los cambios. Se ha deshecho todo.");
                return new(false, false, messages, null);
            }
            record.MachineBackupId = r.BackupId;
            record.RequiresRestart |= r.RequiresRestart;
        }
        record.RequiresRestart |= compatible.Any(t => t.RequiresRestart);

        var h = History;
        h.Records.Add(record);
        JsonStore.Save(HistoryFile, h);
        AurevixLog.Info($"Optimización aplicada: {label} ({record.TweakIds.Count} ajustes).");
        return new(true, record.RequiresRestart, messages, record);
    }

    public static async Task<OptimizationResult> RevertAsync(OptimizationRecord rec)
    {
        var messages = new List<string>();
        var restart = false;
        if (rec.MachineBackupId is { } mb)
        {
            var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "revert", BackupId = mb });
            if (!r.Ok) return new(false, false, [r.Error ?? "El servicio no pudo deshacer los cambios."], rec);
            restart = r.RequiresRestart;
        }
        if (rec.UserBackupId is { } ub && AppServices.Tweaks.FindBackup(ub) is { } b)
        {
            AppServices.Tweaks.Revert(b);
            AppServices.Tweaks.DeleteBackup(b.Id);
        }
        var h = History;
        h.Records.RemoveAll(r => r.Id == rec.Id);
        JsonStore.Save(HistoryFile, h);
        messages.Add($"Deshecho: {rec.Label}");
        return new(true, restart || rec.RequiresRestart, messages, rec);
    }

    /// <summary>Deshace todas las optimizaciones (de la más reciente a la más antigua).</summary>
    public static async Task<OptimizationResult> RevertAllAsync()
    {
        var restart = false;
        var msgs = new List<string>();
        foreach (var rec in History.Records.OrderByDescending(r => r.AtUtc).ToList())
        {
            var r = await RevertAsync(rec);
            msgs.AddRange(r.Messages);
            restart |= r.RequiresRestart;
            if (!r.Success) return new(false, restart, msgs, rec);
        }
        return new(true, restart, msgs, null);
    }
}
