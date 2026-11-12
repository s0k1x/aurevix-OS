using System.IO;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>
/// Apariencia mediante opciones SOPORTADAS de Windows (registro de usuario documentado y SystemParametersInfo).
/// No se reemplaza el shell ni se modifican binarios protegidos. Todo es reversible.
/// </summary>
public sealed class AppearancePage : UserControl
{
    private const string Prefix = "Apariencia: ";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public AppearancePage() => Build();

    private void Build()
    {
        var (root, body) = Ui.Page("Apariencia", "Identidad visual AUREVIX con métodos soportados por Windows. Puedes volver al aspecto original en cualquier momento.");

        var applied = AurevixStyle.IsApplied;
        body.Children.Add(Ui.Card(
            Ui.Eyebrow(applied ? "ACTIVO" : "RECOMENDADO"),
            Ui.H2("Estilo AUREVIX"),
            Ui.Muted("Windows minimalista en un clic: tema claro con acento verde y un toque de cristal, fondo y pantalla de bloqueo AUREVIX, " +
                     "barra de tareas limpia (iconos centrados, búsqueda solo icono, sin Vista de tareas ni widgets), " +
                     "menú Inicio sin recomendaciones y Explorador que abre en «Este equipo» sin recientes. Todo es reversible."),
            Ui.Row(
                Ui.Button(applied ? "Volver a aplicar" : "Aplicar estilo AUREVIX", () => _ = ApplyStyleAsync(), primary: true),
                Ui.Button("Restaurar Windows original", () => _ = RestoreStyleAsync()))));

        var shellOn = AppServices.Settings.ShellEnabled;
        body.Children.Add(Ui.Card(
            Ui.Eyebrow(AurevixShell.IsAvailable ? (shellOn ? "ACTIVADO" : "DESACTIVADO") : "NO DISPONIBLE EN ESTE EQUIPO"),
            Ui.H2("AUREVIX Shell"),
            Ui.Muted("Barra de tareas, menú Inicio y Configuración con los colores AUREVIX (claros u oscuros según tu tema) e indicadores verdes, desde que enciendes el equipo, sin que tengas que hacer nada. " +
                     "Es un componente propio de AUREVIX: solo cambia el aspecto en memoria, no modifica archivos de Windows."),
            Ui.Muted("Si algún juego con anticheat se queja o algo se ve raro, desactívalo: Windows 11 vuelve a verse como siempre al momento."),
            Ui.Row(
                Ui.Button(shellOn ? "Desactivar" : "Activar", () =>
                {
                    if (!AurevixShell.IsAvailable) { MessageBox.Show("AUREVIX Shell necesita Windows 11.", "Apariencia"); return; }
                    if (MessageBox.Show("Se reiniciará la barra de tareas unos segundos (no se cierra ningún programa). ¿Continuar?", "AUREVIX Shell",
                            MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    AurevixShell.SetEnabled(!AppServices.Settings.ShellEnabled);
                    Build();
                }, primary: !shellOn))));

        body.Children.Add(Ui.Card(Ui.H2("Tema de color"),
            Ui.Muted("Cambia el modo de color de Windows y de las aplicaciones."),
            Ui.Row(
                Ui.Button("Claro (predeterminado)", () => { ManualOff(); ApplyReg("Modo claro", [Dw(Personalize, "AppsUseLightTheme", 1), Dw(Personalize, "SystemUsesLightTheme", 1)]); }, primary: true),
                Ui.Button("Oscuro", () => { ManualOff(); ApplyReg("Modo oscuro", [Dw(Personalize, "AppsUseLightTheme", 0), Dw(Personalize, "SystemUsesLightTheme", 0)]); }))));

        body.Children.Add(AccentCard());
        body.Children.Add(AutoThemeCard());

        body.Children.Add(Ui.Card(Ui.H2("Modo visual"),
            Ui.Muted("Ligero: sin transparencias ni animaciones de ventana (equipos modestos). Elegante: transparencias y animaciones de Windows 11."),
            Ui.Row(
                Ui.Button("Clásico / Ligero", () => ApplyReg("Modo ligero", [Dw(Personalize, "EnableTransparency", 0), Sz(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0")])),
                Ui.Button("Elegante / Animado", () => ApplyReg("Modo elegante", [Dw(Personalize, "EnableTransparency", 1), Sz(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", "1")]), primary: true))));

        body.Children.Add(Ui.Card(Ui.H2("Iconos del escritorio"),
            Ui.Muted("Tamaño de los iconos del escritorio. Se aplica al reiniciar el Explorador."),
            Ui.Row(
                Ui.Button("Pequeños", () => ApplyReg("Iconos pequeños", [Dw(@"Software\Microsoft\Windows\Shell\Bags\1\Desktop", "IconSize", 32)])),
                Ui.Button("Medianos", () => ApplyReg("Iconos medianos", [Dw(@"Software\Microsoft\Windows\Shell\Bags\1\Desktop", "IconSize", 48)])),
                Ui.Button("Grandes", () => ApplyReg("Iconos grandes", [Dw(@"Software\Microsoft\Windows\Shell\Bags\1\Desktop", "IconSize", 96)])),
                Ui.Button("Reiniciar Explorador", AurevixStyle.RestartExplorer))));

        body.Children.Add(Ui.Card(Ui.H2("Barra de tareas"),
            Ui.Muted("Alineación de los iconos de la barra de tareas (opción oficial de Windows 11)."),
            Ui.Row(
                Ui.Button("Centrada", () => ApplyReg("Barra centrada", [Dw(Advanced, "TaskbarAl", 1)])),
                Ui.Button("A la izquierda", () => ApplyReg("Barra a la izquierda", [Dw(Advanced, "TaskbarAl", 0)])))));

        var walls = AurevixStyle.Wallpapers();
        var wallRow = Ui.Wrap();
        foreach (var w in walls) wallRow.Children.Add(WallpaperTile(w));
        body.Children.Add(Ui.Card(Ui.H2("Fondos AUREVIX"),
            walls.Count == 0 ? Ui.Muted("No se encontraron fondos instalados.") : Ui.Muted("Pulsa un fondo para ponerlo en el escritorio. Daylight es el predeterminado de AUREVIX OS; los cuatro primeros son claros y los otros cuatro oscuros."),
            wallRow,
            Ui.Row(Ui.Button("Restaurar fondo original", RestoreWallpaper))));

        body.Children.Add(Ui.Card(Ui.H2("Volver a Windows 11 original"),
            Ui.Muted("Desactiva AUREVIX Shell y deshace todos los cambios de apariencia hechos por AUREVIX, en orden inverso. Tus programas y archivos no se tocan."),
            Ui.Row(Ui.Button("Restablecer aspecto de Windows 11", RestoreAll))));

        Content = root;
    }

    /// <summary>Al elegir claro/oscuro a mano se desactiva el tema automático y se refresca la tarjeta.</summary>
    private void ManualOff()
    {
        var s = AppServices.Settings;
        if (s.ThemeSchedule == "off") return;
        s.ThemeSchedule = "off";
        AppServices.SaveSettings();
        Build();
    }

    private static readonly (string Name, string Hex)[] Accents =
    [
        ("Verde AUREVIX", "#8DE03C"), ("Azul", "#3B82F6"), ("Violeta", "#8B5CF6"), ("Rosa", "#EC4899"),
        ("Naranja", "#F97316"), ("Rojo", "#EF4444"), ("Turquesa", "#14B8A6")
    ];

    /// <summary>Siete círculos de color; el elegido lleva un anillo.</summary>
    private UIElement AccentCard()
    {
        var current = AppServices.Settings.AccentColor;
        var row = Ui.Wrap();
        foreach (var (name, hex) in Accents)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var brush = new System.Windows.Media.SolidColorBrush(color);
            var selected = string.Equals(current, hex, StringComparison.OrdinalIgnoreCase);
            var circle = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = brush };
            var ring = new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(2), Padding = new Thickness(3), Child = circle,
                BorderBrush = selected ? brush : System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name
            };
            ring.MouseLeftButtonUp += (_, _) =>
            {
                AppServices.Settings.AccentColor = hex;
                AppServices.SaveSettings();
                AurevixStyle.ApplyAccent(hex);
                Ui.ApplyAccentResources();
                Build();
            };
            row.Children.Add(ring);
        }
        return Ui.Card(Ui.H2("Color de acento"),
            Ui.Muted("Elige el color que Windows y AUREVIX usan para botones, ventanas y destacados."),
            row);
    }

    /// <summary>Cambio automático entre claro y oscuro: manual, por horario o con el sol.</summary>
    private UIElement AutoThemeCard()
    {
        var s = AppServices.Settings;
        var next = Ui.Muted(ThemeScheduler.NextChangeText());
        next.Margin = new Thickness(0, 10, 0, 0);

        void Choose(string mode)
        {
            s.ThemeSchedule = mode;
            AppServices.SaveSettings();
            ThemeScheduler.Evaluate();
            Build();
        }

        var options = Ui.Row(
            Ui.Button("Manual", () => Choose("off"), primary: s.ThemeSchedule == "off"),
            Ui.Button("Por horario", () => Choose("hours"), primary: s.ThemeSchedule == "hours"),
            Ui.Button("Al amanecer y al anochecer", () => Choose("sun"), primary: s.ThemeSchedule == "sun"));

        var card = new List<UIElement>
        {
            Ui.H2("Tema automático"),
            Ui.Muted("Cambia solo entre tema claro y oscuro sin que tengas que hacer nada. Si quieres, también cambia el fondo de día y de noche."),
            options
        };

        if (s.ThemeSchedule == "hours")
        {
            ComboBox Times(int minutes, Action<int> set)
            {
                var box = new ComboBox { Width = 90, Margin = new Thickness(8, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
                for (var m = 0; m < 1440; m += 15) box.Items.Add($"{m / 60:00}:{m % 60:00}");
                box.SelectedIndex = Math.Clamp(minutes / 15, 0, 95);
                box.SelectionChanged += (_, _) =>
                {
                    if (box.SelectedIndex < 0) return;
                    set(box.SelectedIndex * 15);
                    AppServices.SaveSettings();
                    ThemeScheduler.Evaluate();
                    next.Text = ThemeScheduler.NextChangeText();
                };
                return box;
            }
            var hours = Ui.Row(
                new TextBlock { Text = "Claro desde las", VerticalAlignment = VerticalAlignment.Center },
                Times(s.LightFromMinutes, m => s.LightFromMinutes = m),
                new TextBlock { Text = "Oscuro desde las", VerticalAlignment = VerticalAlignment.Center },
                Times(s.DarkFromMinutes, m => s.DarkFromMinutes = m));
            card.Add(hours);
        }
        else if (s.ThemeSchedule == "sun")
        {
            card.Add(Ui.Muted("Se calcula con la hora del sol en tu zona horaria; no se envía ningún dato."));
        }
        if (s.ThemeSchedule != "off")
        {
            var wall = new CheckBox { Content = "Cambiar también el fondo", Style = Ui.Res<Style>("Switch"), IsChecked = s.ThemeChangesWallpaper, Margin = new Thickness(0, 10, 0, 0) };
            wall.Click += (_, _) =>
            {
                s.ThemeChangesWallpaper = wall.IsChecked == true;
                AppServices.SaveSettings();
            };
            card.Add(wall);
            card.Add(next);
        }
        return Ui.Card([.. card]);
    }

    private async Task ApplyStyleAsync()
    {
        if (MessageBox.Show("Se aplicará el Estilo AUREVIX (tema, barra de tareas, Inicio, Explorador y pantalla de bloqueo).\n\n" +
                "Se guarda una copia de todo y puedes restaurar Windows original cuando quieras. ¿Continuar?",
                "Estilo AUREVIX", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var r = await AurevixStyle.ApplyAsync(includeLockScreen: true);
        var msg = "Aplicado:\n• " + string.Join("\n• ", r.Applied);
        if (r.Skipped.Count > 0) msg += "\n\nOmitido (Windows no lo permitió):\n• " + string.Join("\n• ", r.Skipped);
        msg += "\n\nPara ver la barra de tareas y el Explorador nuevos hay que reiniciar el Explorador de Windows " +
               "(se cierran las ventanas de carpetas abiertas, no tus programas). ¿Reiniciarlo ahora?";
        if (MessageBox.Show(msg, "Estilo AUREVIX", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            AurevixStyle.RestartExplorer();
        Build();
    }

    private async Task RestoreStyleAsync()
    {
        if (MessageBox.Show("¿Restaurar el aspecto original de Windows?", "Estilo AUREVIX", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await AurevixStyle.RestoreAsync();
        if (MessageBox.Show("Restaurado. ¿Reiniciar el Explorador para ver los cambios ahora?", "Estilo AUREVIX", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            AurevixStyle.RestartExplorer();
        Build();
    }

    private static RegistryAction Dw(string key, string name, int v) => new() { Hive = RegistryHive.CurrentUser, Key = key, Name = name, Kind = RegistryValueKind.DWord, Value = v.ToString() };
    private static RegistryAction Sz(string key, string name, string v) => new() { Hive = RegistryHive.CurrentUser, Key = key, Name = name, Kind = RegistryValueKind.String, Value = v };

    private static void ApplyReg(string name, List<RegistryAction> actions)
    {
        var tweak = new TweakDefinition { Id = "appearance." + name, Title = name, Category = TweakCategory.Interface, Registry = actions };
        var r = AppServices.Tweaks.Apply([tweak], Prefix + name);
        if (!r.Success) MessageBox.Show(string.Join("\n", r.Messages), "Apariencia", MessageBoxButton.OK, MessageBoxImage.Warning);
        else Core.Logging.AurevixLog.Info($"Apariencia aplicada: {name}");
    }

    /// <summary>Miniatura pulsable de un fondo (se decodifica a tamaño pequeño para no cargar el 4K entero).</summary>
    private static UIElement WallpaperTile(string path)
    {
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.DecodePixelWidth = 440;
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        var name = Path.GetFileNameWithoutExtension(path).Replace("Aurevix-", "");
        var frame = new Border
        {
            Width = 220, Height = 124, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("BorderBrushAx"),
            Background = new System.Windows.Media.ImageBrush(bmp) { Stretch = System.Windows.Media.Stretch.UniformToFill }, Cursor = System.Windows.Input.Cursors.Hand
        };
        var label = Ui.Muted(name);
        label.Margin = new Thickness(2, 6, 0, 0);
        var tile = new StackPanel { Margin = new Thickness(0, 0, 12, 12), Children = { frame, label } };
        frame.MouseEnter += (_, _) => frame.BorderBrush = Ui.Brush("Accent");
        frame.MouseLeave += (_, _) => frame.BorderBrush = Ui.Brush("BorderBrushAx");
        frame.MouseLeftButtonUp += (_, _) => SetWallpaper(path);
        return tile;
    }

    private static string OriginalWallpaperFile => Path.Combine(AurevixPaths.Backups, "original-wallpaper.txt");

    private static void SetWallpaper(string path)
    {
        if (!File.Exists(OriginalWallpaperFile))
            File.WriteAllText(OriginalWallpaperFile, SystemTools.CurrentWallpaper() ?? "");
        if (!SystemTools.SetWallpaper(path))
            MessageBox.Show("Windows no permitió cambiar el fondo.", "Apariencia");
    }

    private static void RestoreWallpaper()
    {
        if (!File.Exists(OriginalWallpaperFile)) { MessageBox.Show("No hay un fondo original guardado: AUREVIX no lo ha cambiado.", "Apariencia"); return; }
        var original = File.ReadAllText(OriginalWallpaperFile);
        SystemTools.SetWallpaper(original);
        File.Delete(OriginalWallpaperFile);
    }

    private static async void RestoreAll()
    {
        if (MessageBox.Show("¿Volver al aspecto original de Windows 11?\n\nSe desactiva AUREVIX Shell, se deshace el Estilo AUREVIX y se reinicia la barra de tareas unos segundos.",
                "Apariencia", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        AppServices.Settings.ThemeSchedule = "off";
        AppServices.Settings.AccentColor = "#8DE03C";
        AppServices.SaveSettings();
        try { await AurevixStyle.RestoreAsync(); } catch (Exception ex) { Core.Logging.AurevixLog.Error("Restaurar estilo", ex); }
        AurevixShell.SetEnabled(false, restartShell: true);
        // ListBackups está ordenado del más reciente al más antiguo: se revierte en ese orden.
        foreach (var b in AppServices.Tweaks.ListBackups().Where(b => b.Description.StartsWith(Prefix)))
        {
            AppServices.Tweaks.Revert(b);
            var file = Path.Combine(AurevixPaths.Backups, $"change-{b.Id}.json");
            try { File.Delete(file); } catch { }
        }
        if (File.Exists(OriginalWallpaperFile)) RestoreWallpaper();
        MessageBox.Show("Windows 11 original restaurado. Algunos detalles se verán al cerrar sesión.", "Apariencia");
    }
}
