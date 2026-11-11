using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Aurevix.Core;
using Aurevix.Core.Logging;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Services;

/// <summary>
/// "Estilo AUREVIX": Windows minimalista con mecanismos OFICIALES y reversibles:
///   - Tema .theme propio (tema claro, acento verde, fondo Daylight) aplicado por Windows. El modo oscuro se elige en Apariencia.
///   - Barra de tareas limpia (iconos centrados, búsqueda solo icono, sin Vista de tareas ni widgets).
///   - Menú Inicio sin recomendaciones ni sugerencias.
///   - Explorador abierto en "Este equipo", extensiones visibles, sin recientes/frecuentes.
///   - Animaciones y transparencias de Windows activadas.
///   - Ventanas con barra de título y borde en los tonos AUREVIX (color de ventana de DWM).
///   - Pantalla de bloqueo AUREVIX sin anuncios ni «datos curiosos» (API oficial Windows.System.UserProfile.LockScreen).
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
    private const string Dwm = @"Software\Microsoft\Windows\DWM";

    /// <summary>Fondo predeterminado de AUREVIX OS (escritorio y pantalla de bloqueo).</summary>
    public const string DefaultWallpaper = "Aurevix-Daylight";

    private static RegistryAction Dw(string key, string name, int v) =>
        new() { Hive = RegistryHive.CurrentUser, Key = key, Name = name, Kind = RegistryValueKind.DWord, Value = v.ToString() };
    private static RegistryAction Sz(string key, string name, string v) =>
        new() { Hive = RegistryHive.CurrentUser, Key = key, Name = name, Kind = RegistryValueKind.String, Value = v };
    /// <summary>Color de DWM en formato ABGR (0xAABBGGRR).</summary>
    private static RegistryAction Color(string name, uint abgr) =>
        new() { Hive = RegistryHive.CurrentUser, Key = Dwm, Name = name, Kind = RegistryValueKind.DWord, Value = abgr.ToString() };

    /// <summary>
    /// Se aplica DESPUÉS del tema (que restablece los colores de ventana): barra de título y borde de todas las
    /// ventanas en negro grafito, el rasgo más reconocible de AUREVIX OS. El acento verde lima no cambia.
    /// </summary>
    private static readonly TweakDefinition WindowFrames = new()
    {
        Id = "style.windows", Title = "Ventanas AUREVIX (barra de título gris perla)", Category = TweakCategory.Interface,
        Registry = [Dw(Dwm, "ColorPrevalence", 1), Color("AccentColor", 0xFFF2EFEC), Color("AccentColorInactive", 0xFFF9F8F7)]
    };

    /// <summary>Grupos de ajustes. Cada grupo es un TweakDefinition independiente: si Windows bloquea uno, los demás siguen.</summary>
    public static IReadOnlyList<TweakDefinition> Groups { get; } =
    [
        new()
        {
            Id = "style.colors", Title = "Tema claro AUREVIX y transparencias", Category = TweakCategory.Interface,
            Registry = [Dw(Personalize, "AppsUseLightTheme", 1), Dw(Personalize, "SystemUsesLightTheme", 1), Dw(Personalize, "EnableTransparency", 1)]
        },
        new()
        {
            Id = "style.taskbar", Title = "Barra de tareas limpia", Category = TweakCategory.Interface,
            Registry = [Dw(Advanced, "TaskbarAl", 1), Dw(Advanced, "ShowTaskViewButton", 0), Dw(Search, "SearchboxTaskbarMode", 1),
                        Dw(Advanced, "ShowCopilotButton", 0)]
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
            Id = "style.lock", Title = "Pantalla de bloqueo sin anuncios ni sugerencias", Category = TweakCategory.Interface,
            Registry = [Dw(Cdm, "RotatingLockScreenOverlayEnabled", 0), Dw(Cdm, "SubscribedContent-338387Enabled", 0)]
        },
        new()
        {
            Id = "style.motion", Title = "Animaciones de Windows activadas", Category = TweakCategory.Interface,
            Registry = [Sz(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", "1"),
                        Dw(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 1)]
        },
    ];

    private const string ClsidIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID";
    public static string IconPath(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", name + ".ico") + ",0";

    /// <summary>Iconos AUREVIX del escritorio y del Explorador para este usuario: Este equipo, Papelera, Red y carpeta de usuario.</summary>
    private static TweakDefinition DesktopIcons() => new()
    {
        Id = "style.icons", Title = "Iconos AUREVIX (Este equipo, Papelera, Red, usuario, Panel de control, Inicio, Galería, Impresoras)", Category = TweakCategory.Interface,
        Registry =
        [
            Sz($@"{ClsidIcons}\{{20D04FE0-3AEA-1069-A2D8-08002B30309D}}\DefaultIcon", "", IconPath("pc")),
            Sz($@"{ClsidIcons}\{{645FF040-5081-101B-9F08-00AA002F954E}}\DefaultIcon", "", IconPath("bin-empty")),
            Sz($@"{ClsidIcons}\{{645FF040-5081-101B-9F08-00AA002F954E}}\DefaultIcon", "empty", IconPath("bin-empty")),
            Sz($@"{ClsidIcons}\{{645FF040-5081-101B-9F08-00AA002F954E}}\DefaultIcon", "full", IconPath("bin-full")),
            Sz($@"{ClsidIcons}\{{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}}\DefaultIcon", "", IconPath("network")),
            Sz($@"{ClsidIcons}\{{59031a47-3f72-44a7-89c5-5595fe6b30ee}}\DefaultIcon", "", IconPath("user")),
            Sz($@"{ClsidIcons}\{{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}}\DefaultIcon", "", IconPath("control-panel")),
            Sz($@"{ClsidIcons}\{{26EE0668-A00A-44D7-9371-BEB064C98683}}\DefaultIcon", "", IconPath("control-panel")),
            Sz($@"{ClsidIcons}\{{21EC2020-3AEA-1069-A2DD-08002B30309D}}\DefaultIcon", "", IconPath("control-panel")),
            Sz($@"{ClsidIcons}\{{f874310e-b6b7-47dc-bc84-b9e6b38f5903}}\DefaultIcon", "", IconPath("home")),
            Sz($@"{ClsidIcons}\{{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}}\DefaultIcon", "", IconPath("gallery")),
            Sz($@"{ClsidIcons}\{{2227A280-3AEA-1069-A2DE-08002B30309D}}\DefaultIcon", "", IconPath("printer")),
        ]
    };

    // ------------------------------------------------------------------ Iconos de Documentos, Descargas, Música…

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
    }

    private static IEnumerable<(string Path, string Icon)> KnownFolders()
    {
        (Guid id, string icon)[] list =
        [
            (new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), "documents"), (new("374DE290-123F-4565-9164-39C4925E467B"), "downloads"),
            (new("4BD8D571-6D19-48D3-BE97-422220080E43"), "music"), (new("33E28130-4E1E-4676-835A-98395C3BC3BB"), "pictures"),
            (new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), "videos"), (new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), "desktop"),
            (new("1777F761-68AD-4D8A-87BD-30B759FA33DD"), "favorites"), (new("56784854-C6CB-462B-8169-88E350ACB882"), "contacts"),
            (new("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4"), "saved-games"), (new("31C0DD25-9439-4F12-BF41-7FF4EDA38722"), "objects-3d"),
            (new("BFB9D5E0-C6A9-404C-B2B2-AE6DB6AF4968"), "links"), (new("7D1D3A04-DEBB-4115-95CF-2F29DA2920DA"), "searches"),
            (new("B7BEDE81-DF94-4682-A7D8-57A52620B86F"), "screenshots"),
        ];
        foreach (var (id, icon) in list)
            if (KnownFolder(id) is { } path && Directory.Exists(path)) yield return (path, icon);
    }

    private static string KnownFolderBackup => Path.Combine(AurevixPaths.Backups, "known-folder-icons.json");

    /// <summary>Pone el icono AUREVIX en Documentos, Descargas, Música, Imágenes, Vídeos y Escritorio (desktop.ini). Guarda lo que había.</summary>
    private static int ApplyKnownFolderIcons()
    {
        var backup = JsonStore.LoadOrDefault<Dictionary<string, string?>>(KnownFolderBackup);
        var done = 0;
        foreach (var (path, icon) in KnownFolders())
        {
            try
            {
                var ini = Path.Combine(path, "desktop.ini");
                var lines = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : ["[.ShellClassInfo]"];
                if (!backup.ContainsKey(path)) backup[path] = lines.FirstOrDefault(l => l.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase));
                lines.RemoveAll(l => l.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase));
                var section = lines.FindIndex(l => l.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase));
                if (section < 0) { lines.Insert(0, "[.ShellClassInfo]"); section = 0; }
                lines.Insert(section + 1, "IconResource=" + IconPath(icon));
                WriteDesktopIni(ini, lines);
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                done++;
            }
            catch (Exception ex) { AurevixLog.Warn($"Icono de {path}: {ex.Message}"); }
        }
        JsonStore.Save(KnownFolderBackup, backup);
        return done;
    }

    private static void RestoreKnownFolderIcons()
    {
        if (!File.Exists(KnownFolderBackup)) return;
        var backup = JsonStore.LoadOrDefault<Dictionary<string, string?>>(KnownFolderBackup);
        foreach (var (path, original) in backup)
        {
            try
            {
                var ini = Path.Combine(path, "desktop.ini");
                if (!File.Exists(ini)) continue;
                var lines = File.ReadAllLines(ini).ToList();
                var i = lines.FindIndex(l => l.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase));
                if (i < 0) continue;
                if (original is null) lines.RemoveAt(i); else lines[i] = original;
                WriteDesktopIni(ini, lines);
            }
            catch (Exception ex) { AurevixLog.Warn($"Restaurar icono de {path}: {ex.Message}"); }
        }
        File.Delete(KnownFolderBackup);
    }

    /// <summary>desktop.ini va en UTF-16, oculto y de sistema (así lo crea Windows).</summary>
    private static void WriteDesktopIni(string ini, List<string> lines)
    {
        if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
        File.WriteAllLines(ini, lines, System.Text.Encoding.Unicode);
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Pide a Windows que vuelva a leer los iconos (sin reiniciar sesión).</summary>
    public static void RefreshIcons()
    {
        try
        {
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
            Process.Start(new ProcessStartInfo("ie4uinit.exe", "-show") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Exception ex) { AurevixLog.Warn($"Refrescar iconos: {ex.Message}"); }
    }

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

        // 2b. Iconos AUREVIX: los del usuario aquí; carpetas y unidades (todo el equipo) los pone AUREVIX Core Service.
        var icons = AppServices.Tweaks.Apply([DesktopIcons()], $"{BackupPrefix} · Iconos AUREVIX");
        (icons.Success ? applied : skipped).Add("Iconos AUREVIX del escritorio");
        var machineIcons = await CoreServiceClient.SendAsync(new CoreRequest { Op = "icons-on" }, TimeSpan.FromSeconds(20));
        (machineIcons.Ok ? applied : skipped).Add(machineIcons.Ok ? "Iconos AUREVIX de carpetas y unidades" : "Iconos de carpetas y unidades (AUREVIX Core Service no disponible)");
        var folders = ApplyKnownFolderIcons();
        if (folders > 0) applied.Add($"Iconos AUREVIX de Documentos, Descargas, Música, Imágenes, Vídeos y Escritorio ({folders})");
        RefreshIcons();

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
            if (await TrySetLockScreenAsync(WallpaperPath(DefaultWallpaper))) applied.Add("Pantalla de bloqueo AUREVIX");
            else skipped.Add("Pantalla de bloqueo (Windows no lo permitió en este equipo)");
        }

        // 5. Color de las ventanas: Windows tarda unos segundos en terminar de aplicar el tema y, al hacerlo,
        //    restablece estos valores; por eso van al final.
        await Task.Delay(4000);
        var frames = AppServices.Tweaks.Apply([WindowFrames], $"{BackupPrefix} · {WindowFrames.Title}");
        (frames.Success ? applied : skipped).Add(WindowFrames.Title);

        BroadcastSettingChange();
        AurevixLog.Info($"Estilo AUREVIX aplicado: {applied.Count} cambios, {skipped.Count} omitidos.");
        return new Result(applied, skipped, NeedsExplorerRestart: true);
    }

    public static async Task RestoreAsync()
    {
        await CoreServiceClient.SendAsync(new CoreRequest { Op = "icons-off" }, TimeSpan.FromSeconds(20));
        RestoreKnownFolderIcons();
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
        RefreshIcons();
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

    public static string WallpaperDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpapers");

    public static string WallpaperPath(string name)
    {
        var jpg = Path.Combine(WallpaperDirectory, name + ".jpg");
        return File.Exists(jpg) ? jpg : Path.Combine(WallpaperDirectory, name + ".png");
    }

    /// <summary>Fondos incluidos, con el predeterminado primero.</summary>
    public static IReadOnlyList<string> Wallpapers() =>
        !Directory.Exists(WallpaperDirectory) ? [] : Directory.GetFiles(WallpaperDirectory)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => { var i = Array.IndexOf(WallpaperOrder, Path.GetFileNameWithoutExtension(f)); return i < 0 ? 99 : i; })
            .ThenBy(f => f).ToList();

    /// <summary>Claros primero (el predeterminado, Daylight, el primero) y después los oscuros.</summary>
    private static readonly string[] WallpaperOrder =
        ["Aurevix-Daylight", "Aurevix-Mist", "Aurevix-Sand", "Aurevix-Glacier", "Aurevix-Aurora", "Aurevix-Tide", "Aurevix-Dusk", "Aurevix-Nebula"];

    /// <summary>Genera AUREVIX.theme (formato .theme documentado por Microsoft).</summary>
    private static string WriteThemeFile()
    {
        var dir = Path.Combine(AurevixPaths.UserRoot, "theme");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "AUREVIX.theme");
        var wall = WallpaperPath(DefaultWallpaper);
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
            ColorizationColor=0XC44E9A1E
            SystemMode=Light
            AppMode=Light
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
