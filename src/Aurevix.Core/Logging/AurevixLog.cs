using System.Text;

namespace Aurevix.Core.Logging;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Logger de fichero sencillo y seguro para hilos. Un fichero por día,
/// retención configurable (por defecto 14 días). No registra datos personales:
/// quien llama es responsable de no pasar nombres de usuario, rutas de documentos, etc.
/// </summary>
public static class AurevixLog
{
    private static readonly object Gate = new();
    private static string? _dir;
    public static int RetentionDays { get; set; } = 14;

    public static void Initialize(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(directory);
        Cleanup();
    }

    public static string? CurrentFile =>
        _dir is null ? null : Path.Combine(_dir, $"aurevix-{DateTime.UtcNow:yyyyMMdd}.log");

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    public static void Write(LogLevel level, string message)
    {
        var line = $"{DateTime.UtcNow:O} [{level}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        var file = CurrentFile;
        if (file is null) return;
        try
        {
            lock (Gate) File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Nunca romper la aplicación por un fallo de log.
        }
    }

    /// <summary>Borra logs más antiguos que la retención.</summary>
    public static void Cleanup()
    {
        if (_dir is null || !Directory.Exists(_dir)) return;
        var limit = DateTime.UtcNow.AddDays(-RetentionDays);
        foreach (var f in Directory.EnumerateFiles(_dir, "aurevix-*.log"))
        {
            try { if (File.GetLastWriteTimeUtc(f) < limit) File.Delete(f); } catch { }
        }
    }

    /// <summary>Borra todos los logs (opción de privacidad del usuario).</summary>
    public static void DeleteAll()
    {
        if (_dir is null || !Directory.Exists(_dir)) return;
        lock (Gate)
            foreach (var f in Directory.EnumerateFiles(_dir, "*.log"))
                try { File.Delete(f); } catch { }
    }
}
