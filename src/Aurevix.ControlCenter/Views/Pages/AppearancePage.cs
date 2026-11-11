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
            Ui.Muted("Windows minimalista en un clic: modo oscuro con acento verde lima, fondo y pantalla de bloqueo AUREVIX, " +
                     "barra de tareas limpia (iconos centrados, búsqueda solo icono, sin Vista de tareas ni widgets), " +
                     "menú Inicio sin recomendaciones y Explorador que abre en «Este equipo» sin recientes. Todo es reversible."),
            Ui.Row(
                Ui.Button(applied ? "Volver a aplicar" : "Aplicar estilo AUREVIX", () => _ = ApplyStyleAsync(), primary: true),
                Ui.Button("Restaurar Windows original", () => _ = RestoreStyleAsync()))));

        body.Children.Add(Ui.Card(
            Ui.Eyebrow("OPCIONAL · DE TERCEROS"),
            Ui.H2("Barra de tareas translúcida"),
            Ui.Muted("TranslucentTB hace la barra de tareas transparente o con desenfoque. Es gratuita y de código abierto (GPL-3.0), " +
                     "se distribuye en Microsoft Store y se instala con winget desde su origen oficial. AUREVIX no la modifica. " +
                     "Si una actualización de Windows le afecta, basta con desinstalarla."),
            Ui.Row(
                Ui.Button("Instalar TranslucentTB", () => Winget("install", "Instalar TranslucentTB")),
                Ui.Button("Desinstalar", () => Winget("uninstall", "Desinstalar TranslucentTB")))));

        body.Children.Add(Ui.Card(Ui.H2("Tema de color"),
            Ui.Muted("Cambia el modo de color de Windows y de las aplicaciones."),
            Ui.Row(
                Ui.Button("Modo oscuro AUREVIX", () => ApplyReg("Modo oscuro", [Dw(Personalize, "AppsUseLightTheme", 0), Dw(Personalize, "SystemUsesLightTheme", 0)]), primary: true),
                Ui.Button("Modo claro", () => ApplyReg("Modo claro", [Dw(Personalize, "AppsUseLightTheme", 1), Dw(Personalize, "SystemUsesLightTheme", 1)])))));

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
            walls.Count == 0 ? Ui.Muted("No se encontraron fondos instalados.") : Ui.Muted("Pulsa un fondo para ponerlo en el escritorio. Aurora es el predeterminado de AUREVIX OS."),
            wallRow,
            Ui.Row(Ui.Button("Restaurar fondo original", RestoreWallpaper))));

        var shell = Ui.Card(
            Ui.Eyebrow("EXPERIMENTAL · INYECCIÓN DE CÓDIGO · DE TERCEROS"),
            Ui.H2("AUREVIX Shell: barra de tareas y menú Inicio"),
            Ui.Muted("Para cambiar la forma de la barra de tareas y del menú Inicio hay que inyectar código en el Explorador de Windows. " +
                     "AUREVIX usa Windhawk, una herramienta gratuita y de código abierto (GPL-3.0), con el estilo AUREVIX: barra tipo dock en negro grafito, " +
                     "indicador verde lima y menú Inicio redondeado a juego."),
            Ui.Muted("Riesgos: una actualización de Windows puede dejar el estilo sin efecto hasta que el mod se actualice, y algunos juegos con anticheat " +
                     "no admiten programas que inyectan código. Para volver atrás basta con desactivar los mods o desinstalar Windhawk."),
            Ui.Row(
                Ui.Button("1 · Instalar Windhawk", () => WingetTool("install", ShellPresets.WindhawkWingetId, "Instalar Windhawk"), primary: true),
                Ui.Button("Desinstalar", () => WingetTool("uninstall", ShellPresets.WindhawkWingetId, "Desinstalar Windhawk"))));
        var shellBody = (StackPanel)shell.Child;
        foreach (var mod in ShellPresets.Mods)
        {
            shellBody.Children.Add(Ui.Row(
                Ui.Button($"2 · Abrir el mod de {mod.Name.ToLowerInvariant()}", () => SystemTools.Start(mod.Page)),
                Ui.Button($"3 · Copiar el estilo AUREVIX ({mod.Name.ToLowerInvariant()})", () =>
                {
                    try { Clipboard.SetText(mod.Json); } catch { return; }
                    MessageBox.Show($"Estilo copiado.\n\nEn Windhawk abre el mod «{mod.Name}» → Ajustes → Avanzado, borra lo que haya en «Mod settings», pega y pulsa Guardar.", "AUREVIX Shell");
                })));
        }
        shellBody.Children.Add(Ui.Muted("Pasos: instala Windhawk, abre cada mod y pulsa Install, y pega su estilo en Ajustes → Avanzado. Windhawk aún no permite instalar mods de forma automática."));
        body.Children.Add(shell);

        body.Children.Add(Ui.Card(Ui.H2("Volver a la apariencia original"),
            Ui.Muted("Deshace todos los cambios de apariencia hechos por AUREVIX, en orden inverso."),
            Ui.Row(Ui.Button("Restaurar apariencia de Windows", RestoreAll))));

        Content = root;
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

    /// <summary>Instala o desinstala una herramienta de terceros con winget (fuente oficial). Siempre tras confirmación.</summary>
    private static void WingetTool(string verb, string id, string title)
    {
        if (MessageBox.Show($"{title} con winget (Microsoft). ¿Continuar?", "Apariencia", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            var args = verb == "install"
                ? $"install --id {id} --exact --source winget --accept-package-agreements --accept-source-agreements"
                : $"uninstall --id {id} --exact";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("winget", args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo ejecutar winget: {ex.Message}\nInstala «App Installer» desde Microsoft Store.", "Apariencia");
        }
    }

    /// <summary>Instala/desinstala TranslucentTB con winget (fuente oficial). Siempre tras confirmación.</summary>
    private static void Winget(string verb, string title)
    {
        if (MessageBox.Show($"{title} con winget (Microsoft). ¿Continuar?", "Apariencia", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            var args = verb == "install"
                ? "install --id CharlesMilette.TranslucentTB --exact --source winget --accept-package-agreements --accept-source-agreements"
                : "uninstall --id CharlesMilette.TranslucentTB --exact";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("winget", args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo ejecutar winget: {ex.Message}\nInstala «App Installer» desde Microsoft Store.", "Apariencia");
        }
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

    private static void RestoreAll()
    {
        if (MessageBox.Show("¿Restaurar toda la apariencia original de Windows?", "Apariencia", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        // ListBackups está ordenado del más reciente al más antiguo: se revierte en ese orden.
        foreach (var b in AppServices.Tweaks.ListBackups().Where(b => b.Description.StartsWith(Prefix)))
        {
            AppServices.Tweaks.Revert(b);
            var file = Path.Combine(AurevixPaths.Backups, $"change-{b.Id}.json");
            try { File.Delete(file); } catch { }
        }
        if (File.Exists(OriginalWallpaperFile)) RestoreWallpaper();
        MessageBox.Show("Apariencia original restaurada. Algunos cambios se verán al cerrar sesión.", "Apariencia");
    }
}
