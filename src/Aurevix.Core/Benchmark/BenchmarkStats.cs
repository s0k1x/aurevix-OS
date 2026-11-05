using System.Globalization;

namespace Aurevix.Core.Benchmark;

/// <summary>Resultado de una medición real con PresentMon.</summary>
public sealed record BenchmarkResult
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
    public string Process { get; init; } = "";
    public string Label { get; init; } = "";
    public int Frames { get; init; }
    public double Seconds { get; init; }
    public double AvgFps { get; init; }
    /// <summary>FPS medios del 1 % de fotogramas más lentos.</summary>
    public double Low1Fps { get; init; }
    /// <summary>FPS medios del 0,1 % de fotogramas más lentos.</summary>
    public double Low01Fps { get; init; }
    public double FrameTimeP99Ms { get; init; }
}

public static class BenchmarkStats
{
    /// <summary>Calcula métricas estándar a partir de los tiempos entre fotogramas (ms).</summary>
    public static BenchmarkResult Compute(IReadOnlyList<double> frameTimesMs, string process = "", string label = "")
    {
        var ft = frameTimesMs.Where(x => x > 0 && x < 1000 && double.IsFinite(x)).ToArray();
        if (ft.Length < 10) throw new InvalidOperationException("Muy pocos fotogramas capturados: ¿estaba el juego en primer plano?");
        var total = ft.Sum();
        var sorted = ft.OrderByDescending(x => x).ToArray(); // los más lentos primero
        double LowFps(double fraction)
        {
            var n = Math.Max(1, (int)Math.Ceiling(sorted.Length * fraction));
            return 1000.0 / sorted.Take(n).Average();
        }
        var p99Index = Math.Clamp((int)Math.Ceiling(sorted.Length * 0.01) - 1, 0, sorted.Length - 1);
        return new BenchmarkResult
        {
            Process = process, Label = label, Frames = ft.Length, Seconds = total / 1000.0,
            AvgFps = ft.Length / (total / 1000.0),
            Low1Fps = LowFps(0.01), Low01Fps = LowFps(0.001),
            FrameTimeP99Ms = sorted[p99Index]
        };
    }

    /// <summary>Lee un CSV de PresentMon (columna MsBetweenPresents; ignora valores NA).</summary>
    public static List<double> ParsePresentMonCsv(IEnumerable<string> lines, string? onlyProcess = null)
    {
        using var e = lines.GetEnumerator();
        if (!e.MoveNext()) return [];
        var header = e.Current.Split(',');
        var col = Array.FindIndex(header, h => h.Trim().Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
        if (col < 0) col = Array.FindIndex(header, h => h.Trim().Equals("FrameTime", StringComparison.OrdinalIgnoreCase));
        if (col < 0) throw new FormatException("El CSV no contiene la columna MsBetweenPresents ni FrameTime.");
        var app = Array.FindIndex(header, h => h.Trim().Equals("Application", StringComparison.OrdinalIgnoreCase));
        var result = new List<double>();
        while (e.MoveNext())
        {
            var cells = e.Current.Split(',');
            if (cells.Length <= col) continue;
            if (onlyProcess is not null && app >= 0 && !cells[app].Trim().Equals(onlyProcess, StringComparison.OrdinalIgnoreCase)) continue;
            if (double.TryParse(cells[col], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) result.Add(v);
        }
        return result;
    }

    /// <summary>Variación porcentual entre dos mediciones.</summary>
    public static double Delta(double before, double after) => before <= 0 ? 0 : (after - before) / before * 100.0;
}
