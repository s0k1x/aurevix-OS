using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aurevix.Core.Logging;
using Microsoft.Win32;

namespace Aurevix.ControlCenter.Services;

/// <summary>Un juego instalado (Steam, Epic, GOG) o añadido a mano en Game Boost.</summary>
public sealed record GameEntry(string Name, string Source, string? LaunchUri, string? ExePath, string? InstallDir);

/// <summary>Detecta los juegos instalados leyendo los archivos y el registro de cada tienda. Solo lectura.</summary>
public static class GameLibrary
{
    private static readonly string[] SteamToolNames = ["Proton", "Redistributable", "SDK", "Dedicated Server"];

    public static Task<List<GameEntry>> ScanAsync() => Task.Run(() =>
    {
        var all = new List<GameEntry>();
        try { all.AddRange(Steam()); } catch (Exception ex) { AurevixLog.Warn($"Juegos: Steam: {ex.Message}"); }
        try { all.AddRange(Epic()); } catch (Exception ex) { AurevixLog.Warn($"Juegos: Epic: {ex.Message}"); }
        try { all.AddRange(Gog()); } catch (Exception ex) { AurevixLog.Warn($"Juegos: GOG: {ex.Message}"); }
        try { all.AddRange(Manual()); } catch (Exception ex) { AurevixLog.Warn($"Juegos: añadidos: {ex.Message}"); }

        // Sin duplicados por exe (los que no tienen exe se distinguen por nombre y origen).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GameEntry>();
        foreach (var g in all)
        {
            var key = !string.IsNullOrEmpty(g.ExePath) ? g.ExePath! : $"{g.Source}|{g.Name}";
            if (seen.Add(key)) result.Add(g);
        }
        return result.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    });

    private static IEnumerable<GameEntry> Steam()
    {
        var list = new List<GameEntry>();
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steamPath = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steamPath)) return list;
        steamPath = steamPath.Replace('/', Path.DirectorySeparatorChar);

        var libraries = new List<string> { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\").Replace('/', Path.DirectorySeparatorChar);
                if (!libraries.Contains(p, StringComparer.OrdinalIgnoreCase)) libraries.Add(p);
            }

        foreach (var lib in libraries)
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(acf);
                    var appId = AcfValue(text, "appid");
                    var name = AcfValue(text, "name");
                    var dir = AcfValue(text, "installdir");
                    if (appId is null || name is null || appId == "228980") continue;
                    if (SteamToolNames.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase))) continue;
                    var installDir = dir is null ? null : Path.Combine(apps, "common", dir);
                    list.Add(new GameEntry(name, "Steam", $"steam://rungameid/{appId}", LargestExe(installDir), installDir));
                }
                catch (Exception ex) { AurevixLog.Warn($"Juegos: {acf}: {ex.Message}"); }
            }
        }
        return list;
    }

    private static string? AcfValue(string text, string key)
    {
        var m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>El .exe más grande de la carpeta del juego (hasta 2 niveles): suele ser el del juego.</summary>
    private static string? LargestExe(string? dir)
    {
        if (dir is null || !Directory.Exists(dir)) return null;
        string? best = null;
        long bestSize = -1;
        void Scan(string d, int level)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(d, "*.exe"))
                {
                    var size = new FileInfo(f).Length;
                    if (size > bestSize) { bestSize = size; best = f; }
                }
                if (level >= 2) return;
                foreach (var sub in Directory.EnumerateDirectories(d)) Scan(sub, level + 1);
            }
            catch { }
        }
        Scan(dir, 1);
        return best;
    }

    private static IEnumerable<GameEntry> Epic()
    {
        var list = new List<GameEntry>();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) return list;
        foreach (var item in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var r = doc.RootElement;
                string? Get(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = Get("DisplayName");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var loc = Get("InstallLocation");
                var exeRel = Get("LaunchExecutable");
                var exe = !string.IsNullOrEmpty(loc) && !string.IsNullOrEmpty(exeRel) ? Path.Combine(loc, exeRel) : null;
                if (exe is not null && !File.Exists(exe)) exe = null;
                string? uri = null;
                if (Get("CatalogNamespace") is { } ns && Get("CatalogItemId") is { } id && Get("AppName") is { } app)
                    uri = $"com.epicgames.launcher://apps/{ns}%3A{id}%3A{app}?action=launch&silent=true";
                list.Add(new GameEntry(name, "Epic", uri, exe, loc));
            }
            catch (Exception ex) { AurevixLog.Warn($"Juegos: {item}: {ex.Message}"); }
        }
        return list;
    }

    private static IEnumerable<GameEntry> Gog()
    {
        var list = new List<GameEntry>();
        using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (games is null) return list;
        foreach (var id in games.GetSubKeyNames())
        {
            try
            {
                using var k = games.OpenSubKey(id);
                var name = k?.GetValue("gameName") as string;
                var exe = k?.GetValue("exe") as string;
                var path = k?.GetValue("path") as string;
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new GameEntry(name, "GOG", null, string.IsNullOrWhiteSpace(exe) ? null : exe, path));
            }
            catch (Exception ex) { AurevixLog.Warn($"Juegos: GOG {id}: {ex.Message}"); }
        }
        return list;
    }

    private static IEnumerable<GameEntry> Manual()
    {
        var s = GameBoost.Settings;
        foreach (var exe in s.Games.ToList())
        {
            var path = s.GamePaths.GetValueOrDefault(exe);
            var name = Path.GetFileNameWithoutExtension(exe);
            yield return new GameEntry(name, "Añadido", null, path,
                path is null ? null : Path.GetDirectoryName(path));
        }
    }
}
