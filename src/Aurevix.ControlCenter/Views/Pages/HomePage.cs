using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Aurevix.ControlCenter.Services;
using Aurevix.Core.Diagnostics;
using Aurevix.Diagnostics;
using Aurevix.Update;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Inicio: saludo, estado en vivo, accesos rápidos y avisos.</summary>
public sealed class HomePage : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public HomePage(MainWindow main)
    {
        var hour = DateTime.Now.Hour;
        var hello = hour < 6 ? "Buenas noches" : hour < 13 ? "Buenos días" : hour < 21 ? "Buenas tardes" : "Buenas noches";
        var (root, body) = Ui.Page(hello, $"AUREVIX {AppServices.CurrentVersion} · Windows {AppServices.WindowsBuild} · {Environment.MachineName}");

        // Indicadores en vivo
        var tiles = Ui.Wrap(
            Ui.Metric("Procesador", out var cpu, out var cpuD),
            Ui.Metric("Memoria", out var ram, out var ramD),
            Ui.Metric("Disco del sistema", out var disk, out var diskD),
            Ui.Metric("Optimización", out var opt, out var optD));
        foreach (Border t in tiles.Children) t.Width = 214;
        body.Children.Add(tiles);

        var records = OptimizationManager.History.Records;
        opt.Text = records.Count == 0 ? "Sin aplicar" : $"{records.Sum(r => r.TweakIds.Count)} ajustes";
        opt.Foreground = records.Count == 0 ? Ui.Brush("TextMuted") : Ui.Brush("Accent");
        optD.Text = records.Count == 0 ? "Empieza por el nivel Recomendado" : records.OrderByDescending(r => r.AtUtc).First().Label;

        SystemTools.CpuUsage();
        void Refresh()
        {
            cpu.Text = $"{SystemTools.CpuUsage():0} %";
            cpuD.Text = $"{Environment.ProcessorCount} hilos";
            var (used, total, load) = SystemTools.Memory();
            ram.Text = $"{load} %";
            ramD.Text = $"{used:0.0} de {total:0.0} GB";
            try
            {
                var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
                disk.Text = $"{c.AvailableFreeSpace / 1e9:0} GB";
                diskD.Text = $"libres de {c.TotalSize / 1e9:0} GB";
            }
            catch { disk.Text = "—"; }
        }
        Refresh();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        // Acciones principales
        body.Children.Add(Section("Empieza aquí"));
        var actions = Ui.Wrap(
            Action("Optimizar", "Niveles Recomendado, Rendimiento y Máximo", "", () => main.Navigate("optimization")),
            Action("Game Boost", "Prioriza tus juegos automáticamente", "", () => main.Navigate("gameboost")),
            Action("Benchmark", "Mide tus FPS reales", "", () => main.Navigate("benchmark")),
            Action("Estilo AUREVIX", "Windows minimalista en un clic", "", () => main.Navigate("appearance")),
            Action("Red", "DNS rápido, ping y adaptador", "", () => main.Navigate("network")),
            Action("Herramientas", "Reparar Windows y drivers", "", () => main.Navigate("tools")));
        body.Children.Add(actions);

        // Estado y avisos
        body.Children.Add(Section("Estado"));
        var report = AppServices.LastReport;
        var statusText = report is null ? "Aún no se ha hecho un diagnóstico en esta sesión." : $"Estado general: {ReportWriter.LevelText(report.Overall)}";
        var upd = AppServices.Updates.LastResult;
        body.Children.Add(Ui.Card(
            Ui.Columns(new StackPanel { Children = { Ui.Text("Diagnóstico", 15, FontWeights.SemiBold), Ui.Muted(statusText) } },
                Ui.Button("Analizar", () => main.Navigate("diagnostics"))),
            Spacer(),
            Ui.Columns(new StackPanel { Children = { Ui.Text("Actualizaciones", 15, FontWeights.SemiBold), Ui.Muted(upd?.Message ?? "Comprobando en segundo plano…") } },
                Ui.Button(upd?.Status == UpdateStatus.Available ? "Ver novedades" : "Buscar", () => main.Navigate("updates"))),
            Spacer(),
            Ui.Columns(new StackPanel { Children = { Ui.Text("Perfil de uso", 15, FontWeights.SemiBold),
                    Ui.Muted(AppServices.Profiles.FirstOrDefault(p => p.Id == AppServices.State.ActiveProfile)?.Name ?? "Ninguno (Windows por defecto)") } },
                Ui.Button("Cambiar", () => main.Navigate("profiles")))));

        Content = root;
    }

    private static TextBlock Section(string text)
    {
        var t = Ui.H2(text);
        t.Margin = new Thickness(0, 18, 0, 10);
        return t;
    }

    private static FrameworkElement Spacer() => new Border { Height = 1, Background = Ui.Brush("BorderBrushAx"), Margin = new Thickness(0, 14, 0, 14) };

    private static Border Action(string title, string text, string glyph, System.Action onClick)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 20, Foreground = Ui.Brush("Accent"), Margin = new Thickness(0, 0, 0, 12) };
        var card = Ui.Card(icon, Ui.Text(title, 15, FontWeights.SemiBold), Ui.Muted(text));
        card.Width = 290;
        card.Margin = new Thickness(0, 0, 12, 12);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, _) => onClick();
        card.MouseEnter += (_, _) => card.BorderBrush = Ui.Brush("Accent");
        card.MouseLeave += (_, _) => card.BorderBrush = Ui.Brush("BorderBrushAx");
        return card;
    }

    public void Dispose() => _timer.Stop();
}
