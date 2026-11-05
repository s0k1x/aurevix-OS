using System.Text;
using System.Text.Json;
using Aurevix.Core;
using Aurevix.Core.Diagnostics;

namespace Aurevix.Diagnostics;

/// <summary>Guarda informes de diagnóstico localmente (JSON + texto legible). Nada se envía fuera del equipo.</summary>
public static class ReportWriter
{
    public static string Save(DiagnosticReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        var stamp = report.Snapshot.CollectedAtUtc.ToString("yyyyMMdd-HHmmss");
        var json = Path.Combine(directory, $"diagnostico-{stamp}.json");
        File.WriteAllText(json, JsonSerializer.Serialize(report, JsonDefaults.Options));
        File.WriteAllText(Path.ChangeExtension(json, ".txt"), ToText(report), Encoding.UTF8);
        return json;
    }

    public static IReadOnlyList<string> ListReports(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "diagnostico-*.json").OrderByDescending(f => f).ToList()
            : [];

    public static string ToText(DiagnosticReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("AUREVIX OS — Informe de diagnóstico");
        sb.AppendLine($"Fecha (UTC): {r.Snapshot.CollectedAtUtc:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Equipo: {r.Snapshot.Manufacturer} {r.Snapshot.Model}");
        sb.AppendLine($"Estado general: {LevelText(r.Overall)}");
        sb.AppendLine();
        foreach (var g in r.Findings.GroupBy(f => f.Area))
        {
            sb.AppendLine($"== {g.Key} ==");
            foreach (var f in g)
            {
                sb.AppendLine($"[{LevelText(f.Level)}] {f.Title}");
                sb.AppendLine($"    {f.Detail}");
                if (f.Recommendation is not null) sb.AppendLine($"    Recomendación: {f.Recommendation}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string LevelText(FindingLevel l) => l switch
    {
        FindingLevel.Ok => "Correcto",
        FindingLevel.Review => "Revisar",
        FindingLevel.Attention => "Requiere atención",
        _ => "No disponible"
    };
}
