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

        body.Children.Add(Ui.Card(Ui.H2("Barra de tareas"),
            Ui.Muted("Alineación de los iconos de la barra de tareas (opción oficial de Windows 11)."),
            Ui.Row(
                Ui.Button("Centrada", () => ApplyReg("Barra centrada", [Dw(Advanced, "TaskbarAl", 1)])),
                Ui.Button("A la izquierda", () => ApplyReg("Barra a la izquierda", [Dw(Advanced, "TaskbarAl", 0)])))));

        var wallDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpapers");
        var walls = Directory.Exists(wallDir) ? Directory.GetFiles(wallDir, "*.png") : Array.Empty<string>();
        var wallRow = Ui.Wrap();
        foreach (var w in walls)
        {
            var b = Ui.Button(Path.GetFileNameWithoutExtension(w).Replace('-', ' '), () => SetWallpaper(w));
            b.Margin = new Thickness(0, 0, 8, 8);
            wallRow.Children.Add(b);
        }
        body.Children.Add(Ui.Card(Ui.H2("Fondos AUREVIX"),
            walls.Length == 0 ? Ui.Muted("No se encontraron fondos instalados.") : Ui.Muted("Elige un fondo de la colección AUREVIX."),
            wallRow,
            Ui.Row(Ui.Button("Restaurar fondo original", RestoreWallpaper))));

        body.Children.Add(Ui.Card(Ui.H2("Menú Inicio, Explorador, iconos y cursores"),
            Ui.Muted("En esta versión AUREVIX no sustituye el menú Inicio, el Explorador ni el shell de Windows: esas modificaciones dependen de " +
                     "herramientas de terceros que pueden romperse con las actualizaciones de Windows. Se evaluarán una a una (licencia, reputación, " +
                     "compatibilidad) e incluirán un modo de recuperación antes de añadirse. Ver docs/adr/0005-personalizacion-visual.md.")));

        body.Children.Add(Ui.Card(Ui.H2("Volver a la apariencia original"),
            Ui.Muted("Deshace todos los cambios de apariencia hechos por AUREVIX, en orden inverso."),
            Ui.Row(Ui.Button("Restaurar apariencia de Windows", RestoreAll))));

        Content = root;
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
