namespace Aurevix.Core.Diagnostics;

/// <summary>
/// Convierte una instantánea del sistema en hallazgos legibles.
/// Regla: si solo hay indicios, el nivel es "Revisar", nunca se diagnostica un fallo físico.
/// Es lógica pura (sin APIs de Windows) para poder probarla en cualquier plataforma.
/// </summary>
public static class DiagnosticAnalyzer
{
    public const double LowFreeSpaceAttentionRatio = 0.10;
    public const double LowFreeSpaceReviewRatio = 0.20;
    public const long MinFreeBytesAttention = 10L * 1024 * 1024 * 1024; // 10 GB
    public const int OldDriverReviewDays = 730; // 2 años

    public static DiagnosticReport Analyze(SystemSnapshot s, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var f = new List<Finding>();

        // Sistema operativo
        if (s.Os is { } os)
            f.Add(new("Sistema", $"{os.Caption} ({os.Version}, compilación {os.Build})", FindingLevel.Ok,
                $"Edición {os.Edition}, arquitectura {os.Architecture}."));
        else
            f.Add(new("Sistema", "Sistema operativo", FindingLevel.Unavailable, "No se pudo leer la información del sistema."));

        // Memoria
        if (s.TotalMemoryBytes is { } ram)
        {
            var gb = ram / 1024d / 1024 / 1024;
            f.Add(gb < 8
                ? new("Memoria", $"RAM: {gb:0.#} GB", FindingLevel.Review, "Menos de 8 GB puede limitar el rendimiento en Windows 11.",
                    "Usa el perfil Eficiencia y el modo visual ligero.")
                : new("Memoria", $"RAM: {gb:0.#} GB", FindingLevel.Ok, "Memoria suficiente para Windows 11."));
        }

        // Almacenamiento: espacio libre
        foreach (var v in s.Volumes.Where(v => v.SizeBytes > 0))
        {
            var ratio = (double)v.FreeBytes / v.SizeBytes;
            var free = v.FreeBytes / 1024d / 1024 / 1024;
            var title = $"Unidad {v.Letter} — {free:0.#} GB libres ({ratio:P0})";
            if (ratio < LowFreeSpaceAttentionRatio || v.FreeBytes < MinFreeBytesAttention)
                f.Add(new("Almacenamiento", title, FindingLevel.Attention, "Poco espacio libre: Windows Update y el sistema pueden fallar.",
                    "Libera espacio con Configuración > Sistema > Almacenamiento (Sensor de almacenamiento)."));
            else if (ratio < LowFreeSpaceReviewRatio)
                f.Add(new("Almacenamiento", title, FindingLevel.Review, "El espacio libre empieza a ser bajo."));
            else
                f.Add(new("Almacenamiento", title, FindingLevel.Ok, "Espacio libre suficiente."));
        }

        // Almacenamiento: salud informada por Windows (no es un diagnóstico físico propio)
        foreach (var d in s.Disks)
        {
            var level = d.HealthStatus?.ToLowerInvariant() switch
            {
                "healthy" => FindingLevel.Ok,
                "warning" => FindingLevel.Review,
                "unhealthy" => FindingLevel.Attention,
                _ => FindingLevel.Unavailable
            };
            var detail = level == FindingLevel.Unavailable
                ? "Windows no expone el estado de salud de este disco."
                : $"Estado informado por Windows: {d.HealthStatus}.";
            f.Add(new("Almacenamiento", $"Disco {d.Model} ({d.MediaType})", level, detail,
                level == FindingLevel.Attention ? "Haz una copia de seguridad de tus datos cuanto antes." : null));
        }

        // Controladores con problemas
        if (s.ProblemDevices.Count == 0)
            f.Add(new("Controladores", "Dispositivos", FindingLevel.Ok, "No hay dispositivos con errores en el Administrador de dispositivos."));
        else
            foreach (var p in s.ProblemDevices)
                f.Add(new("Controladores", p.Name, p.ErrorCode == 28 ? FindingLevel.Attention : FindingLevel.Review,
                    p.ErrorCode == 28 ? "No hay controlador instalado (código 28)." : $"El dispositivo informa el código de error {p.ErrorCode}.",
                    "Busca el controlador en Windows Update o en la web oficial del fabricante."));

        // GPU: antigüedad del controlador
        foreach (var g in s.Gpus)
        {
            if (g.DriverDate is { } date && (now - date).TotalDays > OldDriverReviewDays)
                f.Add(new("Gráficos", g.Name, FindingLevel.Review,
                    $"Controlador {g.DriverVersion} con fecha {date:yyyy-MM-dd} (más de 2 años).",
                    "Consulta Windows Update o la web oficial del fabricante (AMD, Intel o NVIDIA)."));
            else
                f.Add(new("Gráficos", g.Name, FindingLevel.Ok, $"Controlador {g.DriverVersion ?? "desconocido"}."));
        }

        // Batería
        if (s.Battery is { } b && b.DesignCapacity is > 0 && b.FullChargeCapacity is > 0)
        {
            var wear = 1 - (double)b.FullChargeCapacity.Value / b.DesignCapacity.Value;
            f.Add(new("Batería", $"Batería al {b.ChargePercent}%", wear > 0.4 ? FindingLevel.Review : FindingLevel.Ok,
                $"Capacidad actual estimada: {(1 - wear):P0} de la original."));
        }

        // Temperaturas: nunca inventar valores
        if (s.Temperatures.Count == 0)
            f.Add(new("Temperaturas", "Sensores de temperatura", FindingLevel.Unavailable,
                "No hay una fuente de temperatura fiable y compatible en este equipo. No se muestran valores."));
        else
            foreach (var t in s.Temperatures)
                f.Add(new("Temperaturas", t.Sensor, t.Celsius >= 90 ? FindingLevel.Review : FindingLevel.Ok,
                    $"{t.Celsius:0} °C (fuente: {t.Source})."));

        // Windows Update
        if (s.PendingWindowsUpdates is { } pending)
            f.Add(pending > 0
                ? new("Windows Update", $"{pending} actualización(es) pendiente(s)", FindingLevel.Review,
                    "Hay actualizaciones oficiales disponibles.", "Instálalas desde Configuración > Windows Update.")
                : new("Windows Update", "Al día", FindingLevel.Ok, "No hay actualizaciones pendientes."));
        else
            f.Add(new("Windows Update", "Estado de Windows Update", FindingLevel.Unavailable, "No se pudo consultar Windows Update."));

        // Errores de recopilación
        foreach (var (area, msg) in s.CollectionErrors)
            f.Add(new(area, "Información no disponible", FindingLevel.Unavailable, msg));

        return new DiagnosticReport(s, f);
    }
}
