using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core.Diagnostics;
using Aurevix.Diagnostics;
using Aurevix.Update;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Inicio: estado general, perfil activo, avisos y accesos rápidos.</summary>
public sealed class HomePage : UserControl
{
    public HomePage(MainWindow main)
    {
        var (root, body) = Ui.Page("Bienvenido a AUREVIX OS", $"Windows {AppServices.WindowsBuild} · AUREVIX {AppServices.CurrentVersion}");

        var report = AppServices.LastReport;
        var statusText = report is null ? "Aún no se ha realizado ningún diagnóstico en esta sesión." : $"Estado general: {ReportWriter.LevelText(report.Overall)}";
        var status = Ui.Card(Ui.H2("Estado del equipo"), Ui.Text(statusText),
            Ui.Row(Ui.Button("Ejecutar diagnóstico", () => main.Navigate("diagnostics"), primary: true)));
        if (report is not null)
        {
            var problems = report.Findings.Where(f => f.Level is FindingLevel.Attention or FindingLevel.Review).Take(4).ToList();
            foreach (var p in problems) ((StackPanel)status.Child).Children.Insert(2, Ui.Muted($"• {p.Area}: {p.Title}"));
        }
        body.Children.Add(status);

        var profile = AppServices.Profiles.FirstOrDefault(p => p.Id == AppServices.State.ActiveProfile);
        body.Children.Add(Ui.Card(Ui.H2("Perfil activo"),
            Ui.Text(profile?.Name ?? "Ninguno (configuración de Windows por defecto)"),
            Ui.Muted(profile?.Description ?? "Elige un perfil para adaptar el equipo a tu uso."),
            Ui.Row(Ui.Button("Cambiar perfil", () => main.Navigate("profiles")))));

        var upd = AppServices.Updates.LastResult;
        var updText = upd is null ? "Comprobando actualizaciones en segundo plano…" : upd.Message;
        body.Children.Add(Ui.Card(Ui.H2("Avisos"), Ui.Text(updText),
            Ui.Row(Ui.Button(upd?.Status == UpdateStatus.Available ? "Ver actualización" : "Buscar actualizaciones", () => main.Navigate("updates")),
                   Ui.Button("Abrir Windows Update", SystemTools.OpenWindowsUpdate))));

        body.Children.Add(Ui.Card(Ui.H2("Accesos rápidos"),
            Ui.Wrap(
                Ui.Button("Rendimiento", () => main.Navigate("performance")),
                Ui.Button("Optimización", () => main.Navigate("optimization")),
                Ui.Button("Apariencia", () => main.Navigate("appearance")),
                Ui.Button("Recuperación", () => main.Navigate("recovery")),
                Ui.Button("Repetir asistente inicial", () => new FirstRunWindow().Show()))));

        Content = root;
    }
}
