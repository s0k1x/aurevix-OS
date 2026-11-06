using System.Diagnostics;

namespace Aurevix.Installer.Engine;

/// <summary>Formato de tiempos, tamaños y velocidades en español.</summary>
public static class Eta
{
    public static string Format(TimeSpan t)
    {
        if (t.TotalSeconds < 45) return "menos de 1 min";
        if (t.TotalMinutes < 90)
        {
            var m = Math.Max(1, (int)Math.Round(t.TotalMinutes));
            return m == 1 ? "1 min" : $"unos {m} min";
        }
        return $"{(int)t.TotalHours} h {t.Minutes:00} min";
    }

    public static string Bytes(long b) => b >= 1_000_000_000 ? $"{b / 1e9:0.0} GB" : $"{b / 1e6:0} MB";
    public static string Speed(double bytesPerSecond) => bytesPerSecond >= 10e6 ? $"{bytesPerSecond / 1e6:0} MB/s" : $"{bytesPerSecond / 1e6:0.0} MB/s";
}

/// <summary>Estima el tiempo restante a partir de la velocidad de los últimos segundos (estable y sin saltos).</summary>
public sealed class ProgressMeter
{
    private readonly Queue<(long ticks, double fraction)> _samples = new();
    private static readonly long Window = Stopwatch.Frequency * 25;

    public void Reset() => _samples.Clear();

    public void Add(double fraction)
    {
        var now = Stopwatch.GetTimestamp();
        _samples.Enqueue((now, fraction));
        while (_samples.Count > 2 && now - _samples.Peek().ticks > Window) _samples.Dequeue();
    }

    /// <summary>Tiempo restante, o null si todavía no hay datos suficientes (menos de 4 s de muestras).</summary>
    public TimeSpan? Remaining()
    {
        if (_samples.Count < 2) return null;
        var first = _samples.Peek();
        var last = _samples.Last();
        var seconds = (double)(last.ticks - first.ticks) / Stopwatch.Frequency;
        var gained = last.fraction - first.fraction;
        if (seconds < 4 || gained <= 1e-6) return null;
        var left = (1 - last.fraction) / (gained / seconds);
        return left is > 0 and < 60 * 60 * 24 ? TimeSpan.FromSeconds(left) : null;
    }
}
