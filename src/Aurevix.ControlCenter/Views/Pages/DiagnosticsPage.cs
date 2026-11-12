using System.IO;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core;
using Aurevix.Core.Diagnostics;
using Aurevix.Diagnostics;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Diagnóstico bajo demanda (solo lectura) e historial de informes.</summary>
public sealed class DiagnosticsPage : UserControl
{
    private readonly StackPanel _results = new();
    private readonly StackPanel _hardware = new();
    private readonly StackPanel _drivers = new();
    private readonly Button _run;
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };

    public DiagnosticsPage()
    {
        var (root, body) = Ui.Page("Diagnóstico", "Análisis no invasivo: solo lee información del sistema. Nada sale de tu equipo.");
        _run = Ui.Button("Analizar ahora", () => _ = RunAsync(), primary: true);
        body.Children.Add(Ui.Card(
            Ui.Text("Revisa hardware, controladores, almacenamiento, batería, temperaturas (si hay sensor fiable) y Windows Update."),
            Ui.Row(_run, Ui.Button("Abrir carpeta de informes", () => SystemTools.OpenFolder(AurevixPaths.Reports))),
            _progress));
        body.Children.Add(_results);
        body.Children.Add(_hardware);
        body.Children.Add(_drivers);
        _ = LoadHardwareAsync();
        _ = LoadDriversAsync();

        var history = ReportWriter.ListReports(AurevixPaths.Reports).Take(8).ToList();
        if (history.Count > 0)
        {
            var list = new StackPanel();
            foreach (var h in history)
            {
                var name = Path.GetFileNameWithoutExtension(h);
                var btn = Ui.Button(name, () => SystemTools.Start(Path.ChangeExtension(h, ".txt")));
                btn.Margin = new Thickness(0, 0, 0, 6);
                btn.HorizontalAlignment = HorizontalAlignment.Left;
                list.Children.Add(btn);
            }
            body.Children.Add(Ui.Card(Ui.H2("Historial de informes"), list));
        }

        if (AppServices.LastReport is { } last) Render(last);
        Content = root;
    }

    /// <summary>Revisión de hardware en segundo plano; no bloquea la interfaz.</summary>
    private async Task LoadHardwareAsync()
    {
        try
        {
            var checks = await HardwareChecks.RunAsync();
            if (checks.Count == 0) return;
            var card = Ui.Card(Ui.H2("Revisión de tu hardware"));
            var sp = (StackPanel)card.Child;
            foreach (var c in checks)
            {
                var (label, key) = c.Status switch
                {
                    "ok" => ("Correcto", "Ok"),
                    "review" => ("Revisar", "Review"),
                    _ => ("Consejo", "Unavailable")
                };
                var text = new StackPanel();
                text.Children.Add(Ui.Text(c.Title, 14, FontWeights.SemiBold));
                text.Children.Add(Ui.Muted(c.Detail));
                if (!string.IsNullOrEmpty(c.Advice)) text.Children.Add(Ui.Muted("→ " + c.Advice));
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var pill = Ui.Pill(label, Ui.Brush(key));
                DockPanel.SetDock(pill, Dock.Left);
                row.Children.Add(pill);
                row.Children.Add(text);
                sp.Children.Add(row);
            }
            _hardware.Children.Clear();
            _hardware.Children.Add(card);
        }
        catch { /* la revisión es opcional */ }
    }

    /// <summary>Asistente de drivers: carga en segundo plano y enlaza a las páginas oficiales.</summary>
    private async Task LoadDriversAsync()
    {
        _drivers.Children.Add(Ui.Card(Ui.H2("Drivers"), Ui.Muted("Comprobando drivers…")));
        try
        {
            var list = await DriverAdvisor.ScanAsync();
            var card = Ui.Card(Ui.H2("Drivers"));
            var sp = (StackPanel)card.Child;
            foreach (var d in list)
            {
                var key = d.Status == DriverAdvisor.StatusOk ? "Ok" : "Review";
                var text = new StackPanel();
                text.Children.Add(Ui.Text($"{d.Kind}: {d.Name}", 14, FontWeights.SemiBold));
                text.Children.Add(Ui.Muted($"Versión {d.Version} · {(d.Date is { } dt ? dt.ToString("d") : "fecha desconocida")}"));
                var buttons = new List<UIElement>();
                if (d.Url is { } official) buttons.Add(Ui.Button("Descargar oficial", () => OpenUrl(official)));
                else buttons.Add(Ui.Muted("Búscalo en la web del fabricante o usa Windows Update"));
                if (d.OemUrl is { } oem) buttons.Add(Ui.Button("Web del fabricante del PC", () => OpenUrl(oem)));
                var btnRow = Ui.Row(buttons.ToArray());
                btnRow.Margin = new Thickness(0, 6, 0, 0);
                text.Children.Add(btnRow);
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var old = d.Status == DriverAdvisor.StatusOld;
                var pill = Ui.Pill(old ? "Antiguo · actualízalo" : d.Status, Ui.Brush(key));
                if (old && pill.Child is TextBlock pt) pt.FontWeight = FontWeights.SemiBold;
                DockPanel.SetDock(pill, Dock.Left);
                row.Children.Add(pill);
                row.Children.Add(text);
                sp.Children.Add(row);
            }
            if (list.Count == 0) sp.Children.Add(Ui.Muted("No hay drivers de gráfica, red, audio o chipset de un fabricante que revisar (pasa, por ejemplo, en máquinas virtuales)."));
            sp.Children.Add(Ui.Muted("AUREVIX no instala drivers por su cuenta: te lleva a la página oficial correcta. Windows Update también instala drivers de forma segura."));
            _drivers.Children.Clear();
            _drivers.Children.Add(card);
        }
        catch { _drivers.Children.Clear(); }
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* sin navegador no hay nada que hacer */ }
    }

    private async Task RunAsync()
    {
        _run.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _results.Children.Clear();
        _results.Children.Add(Ui.Muted("Analizando… la consulta de Windows Update puede tardar hasta un minuto."));
        try
        {
            Render(await AppServices.RunDiagnosticsAsync());
        }
        catch (Exception ex)
        {
            _results.Children.Clear();
            _results.Children.Add(Ui.Card(Ui.Text($"No se pudo completar el diagnóstico: {ex.Message}")));
        }
        finally
        {
            _run.IsEnabled = true;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private void Render(DiagnosticReport r)
    {
        _results.Children.Clear();
        var s = r.Snapshot;
        var ram = s.TotalMemoryBytes is { } b ? $"{b / 1024d / 1024 / 1024:0.#} GB" : "—";
        _results.Children.Add(Ui.Card(
            Ui.H2($"{s.Manufacturer} {s.Model}".Trim()),
            Ui.Text($"CPU: {s.Cpu?.Name ?? "—"} ({s.Cpu?.Cores} núcleos / {s.Cpu?.LogicalProcessors} hilos)"),
            Ui.Text($"GPU: {string.Join(", ", s.Gpus.Select(g => g.Name))}"),
            Ui.Text($"RAM: {ram}"),
            Ui.Text($"Discos: {string.Join(", ", s.Disks.Select(d => $"{d.Model} ({d.MediaType}, {d.SizeBytes / 1_000_000_000} GB)"))}"),
            Ui.Text($"Red: {string.Join(", ", s.Network.Where(n => n.Connected).Select(n => n.Name)).OrIfEmpty("sin conexión")}"),
            Ui.Text($"Pantallas: {string.Join(", ", s.Displays.Select(d => $"{d.Width}×{d.Height} @ {d.RefreshRate} Hz"))}"),
            Ui.Row(Ui.Pill(ReportWriter.LevelText(r.Overall), Ui.LevelBrush(r.Overall)), Ui.Muted($"Analizado {s.CollectedAtUtc.ToLocalTime():g}"))));

        foreach (var group in r.Findings.GroupBy(f => f.Area))
        {
            var card = Ui.Card(Ui.H2(group.Key));
            var sp = (StackPanel)card.Child;
            foreach (var f in group)
            {
                var text = new StackPanel();
                text.Children.Add(Ui.Text(f.Title, 14, FontWeights.SemiBold));
                text.Children.Add(Ui.Muted(f.Detail));
                if (f.Recommendation is not null) text.Children.Add(Ui.Muted("→ " + f.Recommendation));
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var pill = Ui.Pill(ReportWriter.LevelText(f.Level), Ui.LevelBrush(f.Level));
                DockPanel.SetDock(pill, Dock.Left);
                row.Children.Add(pill);
                row.Children.Add(text);
                sp.Children.Add(row);
            }
            _results.Children.Add(card);
        }
    }
}

internal static class StringExt
{
    public static string OrIfEmpty(this string s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s;
}
