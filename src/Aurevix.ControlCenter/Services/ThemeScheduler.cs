using Aurevix.Core.Logging;
using Microsoft.Win32;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// Tema automático: cada minuto decide si toca modo claro u oscuro (por horario o según amanecer/anochecer)
/// y, si no coincide con el de Windows, lo cambia con AurevixStyle.SetMode.
/// </summary>
public static class ThemeScheduler
{
    private static Timer? _timer;
    private static readonly object Gate = new();

    public static void Start()
    {
        lock (Gate)
        {
            _timer ??= new Timer(_ => Evaluate(), null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>Aplica el modo que toca ahora. No hace nada si el tema automático está en «Manual».</summary>
    public static void Evaluate()
    {
        try
        {
            var mode = AppServices.Settings.ThemeSchedule;
            if (mode != "hours" && mode != "sun") return;
            var wantLight = WantLight(DateTime.Now);
            if (wantLight != WindowsIsLight()) AurevixStyle.SetMode(wantLight);
        }
        catch (Exception ex) { AurevixLog.Warn($"Tema automático: {ex.Message}"); }
    }

    private static bool WindowsIsLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is not int v || v != 0;

    private static bool WantLight(DateTime now)
    {
        var s = AppServices.Settings;
        var m = now.Hour * 60 + now.Minute;
        if (s.ThemeSchedule == "sun")
        {
            var (rise, set) = SunTimes(now.Date);
            return rise < set ? (m >= rise && m < set) : (m >= rise || m < set);
        }
        int l = s.LightFromMinutes, d = s.DarkFromMinutes;
        if (l == d) return true;
        return l < d ? m >= l && m < d : !(m >= d && m < l);
    }

    /// <summary>Próximo cambio previsto («oscuro» o «claro») y la hora; null si el tema automático está en Manual.</summary>
    public static (bool ToLight, DateTime At)? NextChange()
    {
        var s = AppServices.Settings;
        if (s.ThemeSchedule != "hours" && s.ThemeSchedule != "sun") return null;
        var now = DateTime.Now;
        var events = new List<(bool ToLight, DateTime At)>();
        for (var day = 0; day <= 2; day++)
        {
            var date = now.Date.AddDays(day);
            if (s.ThemeSchedule == "sun")
            {
                var (rise, set) = SunTimes(date);
                events.Add((true, date.AddMinutes(rise)));
                events.Add((false, date.AddMinutes(set)));
            }
            else if (s.LightFromMinutes != s.DarkFromMinutes)
            {
                events.Add((true, date.AddMinutes(s.LightFromMinutes)));
                events.Add((false, date.AddMinutes(s.DarkFromMinutes)));
            }
        }
        var future = events.Where(e => e.At > now).OrderBy(e => e.At).ToList();
        if (future.Count == 0) return null;
        return future[0];
    }

    /// <summary>Texto para la página, p. ej. «Próximo cambio: oscuro a las 20:00».</summary>
    public static string NextChangeText()
    {
        if (NextChange() is not { } n) return "";
        var day = n.At.Date == DateTime.Today ? "" : n.At.Date == DateTime.Today.AddDays(1) ? " de mañana" : $" del {n.At:dd/MM}";
        return $"Próximo cambio: {(n.ToLight ? "claro" : "oscuro")} a las {n.At:HH:mm}{day}";
    }

    // ------------------------------------------------------------------ Amanecer y anochecer (NOAA simplificada)

    /// <summary>Minutos desde medianoche (hora local) del amanecer y del anochecer de ese día.</summary>
    public static (int Sunrise, int Sunset) SunTimes(DateTime date)
    {
        var (lat, lon) = Location();
        var gamma = 2 * Math.PI / 365 * (date.DayOfYear - 1);
        var eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                               - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        var decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma)
                   + 0.000907 * Math.Sin(2 * gamma) - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
        var latR = lat * Math.PI / 180;
        var cosHa = Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(latR) * Math.Cos(decl)) - Math.Tan(latR) * Math.Tan(decl);
        var ha = Math.Acos(Math.Clamp(cosHa, -1, 1)) * 180 / Math.PI;
        var offset = TimeZoneInfo.Local.GetUtcOffset(date.AddHours(12)).TotalMinutes;
        var rise = 720 - 4 * (lon + ha) - eqTime + offset;
        var set = 720 - 4 * (lon - ha) - eqTime + offset;
        return (Wrap((int)Math.Round(rise)), Wrap((int)Math.Round(set)));

        static int Wrap(int m) => ((m % 1440) + 1440) % 1440;
    }

    /// <summary>Latitud y longitud aproximadas según la zona horaria de Windows (si no está en la tabla, se deduce del desfase UTC).</summary>
    private static (double Lat, double Lon) Location() => TimeZoneInfo.Local.Id switch
    {
        "Romance Standard Time" => (40.4, -3.7),            // Madrid, París, Bruselas
        "GMT Standard Time" => (51.5, -0.1),                // Londres, Lisboa, Dublín
        "W. Europe Standard Time" => (48.9, 9.2),           // Berlín, Roma, Ámsterdam
        "Central Europe Standard Time" => (47.5, 19.0),     // Budapest, Praga
        "Central European Standard Time" => (52.2, 21.0),   // Varsovia
        "GTB Standard Time" => (38.0, 23.7),                // Atenas, Bucarest
        "E. Europe Standard Time" => (35.2, 33.4),
        "FLE Standard Time" => (50.4, 30.5),                // Kiev, Helsinki
        "Mexico Standard Time" or "Central Standard Time (Mexico)" => (19.4, -99.1),
        "SA Pacific Standard Time" => (4.7, -74.1),         // Bogotá, Lima, Quito
        "Argentina Standard Time" => (-34.6, -58.4),
        "E. South America Standard Time" => (-23.5, -46.6), // Brasilia / São Paulo
        "Pacific SA Standard Time" => (-33.4, -70.7),       // Santiago
        "Venezuela Standard Time" => (10.5, -66.9),
        "SA Western Standard Time" => (-16.5, -68.1),       // La Paz
        "Paraguay Standard Time" => (-25.3, -57.6),
        "Montevideo Standard Time" => (-34.9, -56.2),
        "Central America Standard Time" => (14.6, -90.5),
        "Cuba Standard Time" => (23.1, -82.4),
        "Atlantic Standard Time" => (18.5, -66.1),
        "Eastern Standard Time" => (40.7, -74.0),
        "Central Standard Time" => (41.9, -87.6),
        "Mountain Standard Time" => (39.7, -105.0),
        "Pacific Standard Time" => (34.1, -118.2),
        _ => (40.0, TimeZoneInfo.Local.BaseUtcOffset.TotalHours * 15) // longitud este = positiva, como en SunTimes
    };
}
