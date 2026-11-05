using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Aurevix.Core;
using Aurevix.Core.Logging;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// "Estilo AUREVIX": Windows minimalista con mecanismos OFICIALES y reversibles:
///   - Tema .theme propio (modo oscuro, acento verde lima, fondo AUREVIX) aplicado por Windows.
///   - Barra de tareas limpia (iconos centrados, búsqueda solo icono, sin Vista de tareas ni widgets).
///   - Menú Inicio sin recomendaciones ni sugerencias.
///   - Explorador abierto en "Este equipo", extensiones visibles, sin recientes/frecuentes.
///   - Animaciones y transparencias de Windows activadas.
///   - Pantalla de bloqueo AUREVIX (API oficial Windows.System.UserProfile.LockScreen).
/// Todo se guarda en una copia de seguridad (TweakEngine) y se puede deshacer.
/// </summary>
public static class AurevixStyle
{
    public const string BackupPrefix = "Apariencia: Estilo AUREVIX";

    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string Themes = @"Software\Microsoft\Windows\CurrentVersion\Themes";

    private static RegistryAction Dw(string key, string name, int v) =>
        new() { Hive = RegistryHive.CurrentUser, Key = key, Name = name, Kind = RegistryValueKind.DWord, Value = v.ToString() };
    private static RegistryAction Sz(string key, string name, string v) =>
        new() { Hive = RegistryHive.CurrentUser, Key = key, Name = name, Kind = RegistryValueKind.String, Value = v };

    /// <summary>Grupos de ajustes. Cada grupo es un TweakDefinition independiente: si Windows bloquea uno, los demás siguen.</summary>
    public static IReadOnlyList<TweakDefinition> Groups { get; } =
    [
        new()
        {
            Id = "style.colors", Title = "Modo oscuro y transparencias", Category = TweakCategory.Interface,
            Registry = [Dw(Personalize, "AppsUseLightTheme", 0), Dw(Personalize, "SystemUsesLightTheme", 0), Dw(Personalize, "EnableTransparency", 1)]
        },
        new()
        {
            Id = "style.taskbar", Title = "Barra de tareas limpia", Category = TweakCategory.Interface,
            Registry = [Dw(Advanced, "TaskbarAl", 1), Dw(Advanced, "ShowTaskViewButton", 0), Dw(Search, "SearchboxTaskbarMode", 1)]
        },
        new()
        {
            Id = "style.chat", Title = "Sin icono de Chat/Teams en la barra de tareas", Category = TweakCategory.Interface,
            Registry = [Dw(Advanced, "TaskbarMn", 0)]
        },
        new()
        {
            // Windows protege este valor en algunas versiones: si no se puede cambiar, se omite sin afectar al resto.
            Id = "style.widgets", Title = "Ocultar widgets de la barra de tareas", Category = TweakCategory.Interface,
            Registry = [Dw(Advanced, "TaskbarDa", 0)]
        },
        new()
        {
            Id = "style.start", Title = "Menú Inicio sin recomendaciones ni sugerencias", Category = TweakCategory.Interface,
            Registry = [Dw(Advanced, "Start_Layout", 1), Dw(Advanced, "Start_IrisRecommendations", 0),
                        Dw(Cdm, "SubscribedContent-338388Enabled", 0), Dw(Cdm, "SystemPaneSuggestionsEnabled", 0)]
        },
        new()
        {
            Id = "style.explorer", Title = "Explorador de archivos limpio", Category = TweakCategory.Interface,
            Registry = [Dw(Advanced, "LaunchTo", 1), Dw(Advanced, "HideFileExt", 0), Dw(Advanced, "UseCompactMode", 0),
                        Dw(Explorer, "ShowRecent", 0), Dw(Explorer, "ShowFrequent", 0)]
        },
        new()
        {
            Id = "style.motion", Title = "Animaciones de Windows activadas", Category = TweakCategory.Interface,
            Registry = [Sz(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", "1"),
                        Dw(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 1)]
        },
    ];

    public sealed record Result(List<string> Applied, List<string> Skipped, bool NeedsExplorerRestart);

    public static async Task<Result> ApplyAsync(bool includeLockScreen)
    {
        var applied = new List<string>();
        var skipped = new List<string>();

        // 1. Recordar el tema actual para poder volver a él.
        var stateFile = Path.Combine(AurevixPaths.Backups, "style-original.json");
        if (!File.Exists(stateFile))
        {
            var original = new OriginalStyle
            {
                ThemePath = Microsoft.Win32.Registry.GetValue($@"HKEY_CURRENT_USER\{Themes}", "CurrentTheme", null) as string,
                Wallpaper = SystemTools.CurrentWallpaper(),
                LockScreen = TryGetLockScreen()
            };
            JsonStore.Save(stateFile, original);
        }

        // 2. Registro, por grupos (cada uno con su copia de seguridad).
        foreach (var g in Groups)
        {
            var r = AppServices.Tweaks.Apply([g], $"{BackupPrefix} · {g.Title}");
            (r.Success ? applied : skipped).Add(g.Title);
        }

        // 3. Tema AUREVIX (.theme): Windows aplica acento, colores y fondo de forma oficial.
        try
        {
            var theme = WriteThemeFile();
            Process.Start(new ProcessStartInfo(theme) { UseShellExecute = true });
            applied.Add("Tema AUREVIX (colores, acento y fondo)");
        }
        catch (Exception ex)
        {
            AurevixLog.Error("No se pudo aplicar el tema AUREVIX", ex);
            skipped.Add("Tema AUREVIX");
        }

        // 4. Pantalla de bloqueo.
        if (includeLockScreen)
        {
            if (await TrySetLockScreenAsync(WallpaperPath("Aurevix-Graphite"))) applied.Add("Pantalla de bloqueo AUREVIX");
            else skipped.Add("Pantalla de bloqueo (Windows no lo permitió en este equipo)");
        }

        BroadcastSettingChange();
        AurevixLog.Info($"Estilo AUREVIX aplicado: {applied.Count} cambios, {skipped.Count} omitidos.");
        return new Result(applied, skipped, NeedsExplorerRestart: true);
    }

    public static async Task RestoreAsync()
    {
        foreach (var b in AppServices.Tweaks.ListBackups().Where(b => b.Description.StartsWith(BackupPrefix)))
        {
            AppServices.Tweaks.Revert(b);
            try { File.Delete(Path.Combine(AurevixPaths.Backups, $"change-{b.Id}.json")); } catch { }
        }

        var stateFile = Path.Combine(AurevixPaths.Backups, "style-original.json");
        if (File.Exists(stateFile))
        {
            var o = JsonStore.LoadOrDefault<OriginalStyle>(stateFile);
            try
            {
                var theme = o.ThemePath is { Length: > 0 } t && File.Exists(Environment.ExpandEnvironmentVariables(t))
                    ? Environment.ExpandEnvironmentVariables(t)
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Resources\Themes\aero.theme");
                Process.Start(new ProcessStartInfo(theme) { UseShellExecute = true });
            }
            catch (Exception ex) { AurevixLog.Error("No se pudo restaurar el tema original", ex); }
            if (o.Wallpaper is { Length: > 0 } w && File.Exists(w)) SystemTools.SetWallpaper(w);
            if (o.LockScreen is { Length: > 0 } l && File.Exists(l)) await TrySetLockScreenAsync(l);
            File.Delete(stateFile);
        }
        BroadcastSettingChange();
        AurevixLog.Info("Estilo AUREVIX restaurado.");
    }

    public static bool IsApplied => AppServices.Tweaks.ListBackups().Any(b => b.Description.StartsWith(BackupPrefix));

    /// <summary>Reinicia el Explorador para que la barra de tareas y el Explorador muestren los cambios. Requiere confirmación previa.</summary>
    public static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { } finally { p.Dispose(); }
        }
        // Windows suele relanzarlo solo; si no, lo lanzamos.
        Thread.Sleep(1500);
        if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
    }

    private sealed class OriginalStyle
    {
        public string? ThemePath { get; set; }
        public string? Wallpaper { get; set; }
        public string? LockScreen { get; set; }
    }

    public static string WallpaperPath(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpapers", name + ".png");

    /// <summary>Genera AUREVIX.theme (formato .theme documentado por Microsoft).</summary>
    private static string WriteThemeFile()
    {
        var dir = Path.Combine(AurevixPaths.UserRoot, "theme");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "AUREVIX.theme");
        var wall = WallpaperPath("Aurevix-Graphite");
        File.WriteAllText(path, $"""
            ; AUREVIX OS — tema de Windows
            [Theme]
            DisplayName=AUREVIX

            [Control Panel\Desktop]
            Wallpaper={wall}
            WallpaperStyle=10
            TileWallpaper=0

            [VisualStyles]
            Path=%SystemRoot%\resources\themes\Aero\Aero.msstyles
            ColorStyle=NormalColor
            Size=NormalSize
            AutoColorization=0
            ColorizationColor=0XC48DE03C
            SystemMode=Dark
            AppMode=Dark
            Transparency=1

            [MasterThemeSelector]
            MTSM=RJSPBS
            """);
        return path;
    }

    private static string? TryGetLockScreen()
    {
        try
        {
            var uri = Windows.System.UserProfile.LockScreen.OriginalImageFile;
            return uri is { IsFile: true } ? uri.LocalPath : null;
        }
        catch { return null; }
    }

    private static async Task<bool> TrySetLockScreenAsync(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            await Windows.System.UserProfile.LockScreen.SetImageFileAsync(file);
            return true;
        }
        catch (Exception ex)
        {
            AurevixLog.Warn($"Pantalla de bloqueo no cambiada: {ex.Message}");
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>Avisa a Windows de que cambió la configuración (WM_SETTINGCHANGE), como hace el propio panel de Configuración.</summary>
    private static void BroadcastSettingChange()
    {
        foreach (var area in new[] { "ImmersiveColorSet", "WindowsThemeElement", "TraySettings", "Environment" })
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, area, 0x0002, 1000, out _);
    }
}
