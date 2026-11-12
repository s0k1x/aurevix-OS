using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Recuperación: puntos de restauración, copias de configuración y deshacer cambios.</summary>
public sealed class RecoveryPage : UserControl
{
    public RecoveryPage() => Build();

    private void Build()
    {
        var (root, body) = Ui.Page("Recuperación", "Herramientas para deshacer cambios y proteger la configuración.");

        body.Children.Add(Ui.Card(Ui.H2("Puntos de restauración de Windows"),
            Ui.Muted("Crea un punto con la herramienta oficial de Windows, sin avisos de permisos. Necesita que Protección del sistema esté activada en C:."),
            Ui.Row(
                Ui.Button("Crear punto de restauración", async () =>
                {
                    var ok = await SystemTools.CreateRestorePointAsync("AUREVIX manual");
                    MessageBox.Show(ok ? "Punto de restauración creado." : "No se pudo crear. Comprueba que Protección del sistema está activada en C: (Windows solo permite un punto cada 24 h).", "Recuperación");
                }, primary: true),
                Ui.Button("Abrir Restaurar sistema", SystemTools.OpenSystemRestore))));

        body.Children.Add(Ui.Card(Ui.H2("Copia de la configuración de AUREVIX"),
            Ui.Muted("Exporta ajustes, estado, copias de seguridad de cambios e informes a un archivo .zip en tu carpeta Documentos."),
            Ui.Row(Ui.Button("Exportar configuración", Export), Ui.Button("Abrir carpeta de datos", () => SystemTools.OpenFolder(AurevixPaths.UserRoot)))));

        body.Children.Add(Ui.Card(Ui.H2("Copia del registro (.reg)"),
            Ui.Muted("Crea un archivo .reg en Documentos con los valores originales que AUREVIX guardó antes de cambiar nada, para que puedas restaurarlos a mano."),
            Ui.Muted("Las copias de sistema las guarda AUREVIX Core Service y se restauran desde Optimización."),
            Ui.Row(Ui.Button("Exportar copia del registro (.reg)", ExportRegistry))));

        var backups = AppServices.Tweaks.ListBackups().Take(15).ToList();
        var list = new StackPanel();
        if (backups.Count == 0) list.Children.Add(Ui.Muted("No hay cambios registrados."));
        foreach (var b in backups)
        {
            var undo = Ui.Button("Deshacer", () =>
            {
                if (MessageBox.Show($"¿Restaurar los valores anteriores a «{b.Description}»?", "Deshacer cambio", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                AppServices.Tweaks.Revert(b);
                if (b.ProfileId is not null && AppServices.State.ActiveProfile == b.ProfileId) { AppServices.State.ActiveProfile = null; AppServices.SaveState(); }
                Build();
            });
            var row = Ui.Columns(new StackPanel { Children = { Ui.Text(b.Description), Ui.Muted($"{b.CreatedAtUtc.ToLocalTime():g} · {b.Registry.Count} valor(es)") } }, undo);
            row.Margin = new Thickness(0, 4, 0, 8);
            list.Children.Add(row);
        }
        body.Children.Add(Ui.Card(Ui.H2("Historial de cambios"), Ui.Muted("Cada cambio de AUREVIX guarda los valores anteriores."), list));

        Content = root;
    }

    private static void ExportRegistry()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var (path, count) = RegistryExport.ExportOriginals(AppServices.Tweaks.ListBackups(), docs);
            if (count == 0) { MessageBox.Show("Todavía no hay valores guardados: AUREVIX aún no ha cambiado nada en el registro.", "Recuperación"); return; }
            var open = MessageBox.Show($"Copia creada con {count} valor(es):\n{path}\n\n¿Quieres abrir la carpeta?", "Recuperación", MessageBoxButton.YesNo);
            if (open == MessageBoxResult.Yes) SystemTools.OpenFolder(docs);
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo exportar: {ex.Message}", "Recuperación"); }
    }

    private static void Export()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var target = Path.Combine(docs, $"AUREVIX-config-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var tmp = Path.Combine(Path.GetTempPath(), "aurevix-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            foreach (var f in new[] { AurevixPaths.SettingsFile, AurevixPaths.StateFile })
                if (File.Exists(f)) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
            foreach (var dir in new[] { AurevixPaths.Backups, AurevixPaths.Reports })
                if (Directory.Exists(dir))
                {
                    var dest = Path.Combine(tmp, Path.GetFileName(dir));
                    Directory.CreateDirectory(dest);
                    foreach (var f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
                }
            ZipFile.CreateFromDirectory(tmp, target);
            MessageBox.Show($"Configuración exportada a:\n{target}", "Recuperación");
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo exportar: {ex.Message}", "Recuperación"); }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
