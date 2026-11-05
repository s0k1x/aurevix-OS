using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core;
using Aurevix.Core.Benchmark;
using Aurevix.Core.Logging;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

public sealed class BenchmarkHistory
{
    public List<BenchmarkResult> Results { get; set; } = [];
}

/// <summary>
/// AUREVIX Benchmark: mide FPS reales de un juego con PresentMon (Intel, MIT) antes y después de optimizar.
/// Así cualquier mejora se demuestra con datos del propio equipo, sin promesas.
/// </summary>
public sealed class BenchmarkPage : UserControl
{
    private static string HistoryFile => Path.Combine(AurevixPaths.UserRoot, "benchmarks.json");
    private static string PresentMon => Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe");

    private readonly ComboBox _process = new() { MinWidth = 280, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ComboBox _duration = new() { ItemsSource = new[] { "30 segundos", "60 segundos", "120 segundos" }, SelectedIndex = 1, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBox _label = new() { Width = 220, Padding = new Thickness(8, 5, 8, 5) };
    private readonly TextBlock _status = Ui.Muted("");
    private readonly StackPanel _results = new();
    private readonly Button _start;

    public BenchmarkPage()
    {
        var (root, body) = Ui.Page("Benchmark", "Mide los FPS reales de tus juegos antes y después de optimizar. Los datos se quedan en tu equipo.");
        _label.Background = Ui.Brush("SurfaceAlt"); _label.Foreground = Ui.Brush("Text"); _label.BorderBrush = Ui.Brush("BorderBrushAx");
        _label.Text = OptimizationManager.History.Records.Count == 0 ? "Antes de optimizar" : "Con optimizaciones";
        _start = Ui.Button("Iniciar medición", () => _ = RunAsync(), primary: true);

        body.Children.Add(Ui.Card(
            Ui.Eyebrow("CÓMO FUNCIONA"),
            Ui.Muted("1. Abre el juego y llega a una escena repetible (un benchmark interno o el mismo recorrido).  " +
                     "2. Elige el juego aquí y pulsa Iniciar: tendrás 5 segundos para volver al juego.  " +
                     "3. Repite tras aplicar un nivel de optimización y compara."),
            Ui.Muted("Se mide con PresentMon de Intel (código abierto), ejecutado por AUREVIX Core Service: sin avisos de permisos.")));

        var refresh = Ui.Button("Actualizar lista", RefreshProcesses);
        body.Children.Add(Ui.Card(Ui.H2("Nueva medición"),
            Ui.Row(Ui.Muted("Juego:  "), _process, refresh),
            Ui.Row(Ui.Muted("Duración:  "), _duration, Ui.Muted("Etiqueta:  "), _label),
            Ui.Row(_start), _status));
        body.Children.Add(_results);

        if (!File.Exists(PresentMon))
        {
            _start.IsEnabled = false;
            _status.Text = "PresentMon no está instalado con esta versión de AUREVIX.";
        }
        RefreshProcesses();
        Render();
        Content = root;
    }

    private void RefreshProcesses()
    {
        var self = Environment.ProcessId;
        var items = Process.GetProcesses()
            .Where(p => { try { return p.Id != self && p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle); } catch { return false; } })
            .Select(p => { try { return $"{p.ProcessName}.exe — {p.MainWindowTitle}"; } catch { return null; } finally { p.Dispose(); } })
            .Where(s => s is not null).Distinct().OrderBy(s => s).ToList();
        _process.ItemsSource = items;
        if (items.Count > 0) _process.SelectedIndex = 0;
    }

    private async Task RunAsync()
    {
        if (_process.SelectedItem is not string sel) { MessageBox.Show("Abre el juego y elígelo en la lista.", "Benchmark"); return; }
        var exe = sel.Split(" — ")[0];
        var seconds = _duration.SelectedIndex switch { 0 => 30, 2 => 120, _ => 60 };
        var label = string.IsNullOrWhiteSpace(_label.Text) ? "Medición" : _label.Text.Trim();
        var csv = Path.Combine(Path.GetTempPath(), $"aurevix-bench-{Guid.NewGuid():N}.csv");

        _start.IsEnabled = false;
        try
        {
            for (var i = 5; i > 0; i--) { _status.Text = $"Vuelve al juego… la medición empieza en {i} s"; await Task.Delay(1000); }
            _status.Text = $"Midiendo {exe} durante {seconds} s…";
            // Primero con AUREVIX Core Service (sin aviso de permisos); si no está, PresentMon con UAC.
            var svc = await CoreServiceClient.SendAsync(new CoreRequest { Op = "benchmark", Value = exe, Seconds = seconds, Label = label }, TimeSpan.FromSeconds(seconds + 90));
            BenchmarkResult? r = null;
            if (svc.Ok && svc.Data is { } data) r = System.Text.Json.JsonSerializer.Deserialize<BenchmarkResult>(data, JsonDefaults.Options);
            else if (await CoreServiceClient.IsAvailableAsync()) throw new InvalidOperationException(svc.Error ?? "El servicio no pudo medir.");
            if (r is null)
            {
                var args = $"--process_name {exe} --output_file \"{csv}\" --timed {seconds} --terminate_after_timed --v1_metrics --no_console_stats --stop_existing_session";
                using var p = Process.Start(new ProcessStartInfo(PresentMon, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })!;
                await p.WaitForExitAsync();
                if (!File.Exists(csv)) throw new InvalidOperationException("PresentMon no generó datos. ¿Estaba el juego en marcha y en primer plano?");

                var frames = BenchmarkStats.ParsePresentMonCsv(File.ReadLines(csv), exe);
                r = BenchmarkStats.Compute(frames, exe, label);
            }
            var h = JsonStore.LoadOrDefault<BenchmarkHistory>(HistoryFile);
            h.Results.Add(r);
            JsonStore.Save(HistoryFile, h);
            AurevixLog.Info($"Benchmark {exe}: {r.AvgFps:0.0} FPS, 1% {r.Low1Fps:0.0}");
            _status.Text = $"Listo: {r.AvgFps:0} FPS de media, 1 % low {r.Low1Fps:0}.";
            Render();
        }
        catch (System.ComponentModel.Win32Exception) { _status.Text = "Medición cancelada (no se concedió el permiso)."; }
        catch (Exception ex) { _status.Text = "No se pudo medir: " + ex.Message; }
        finally
        {
            _start.IsEnabled = File.Exists(PresentMon);
            try { File.Delete(csv); } catch { }
        }
    }

    private void Render()
    {
        _results.Children.Clear();
        var all = JsonStore.LoadOrDefault<BenchmarkHistory>(HistoryFile).Results;
        foreach (var g in all.GroupBy(r => r.Process).OrderByDescending(g => g.Max(r => r.AtUtc)))
        {
            var runs = g.OrderBy(r => r.AtUtc).ToList();
            var baseRun = runs[0];
            var card = Ui.Card(Ui.H2(g.Key));
            var sp = (StackPanel)card.Child;
            sp.Children.Add(Ui.Muted("La primera medición es la referencia. La variación se calcula frente a ella."));
            foreach (var r in runs.AsEnumerable().Reverse().Take(10))
            {
                var dAvg = BenchmarkStats.Delta(baseRun.AvgFps, r.AvgFps);
                var dLow = BenchmarkStats.Delta(baseRun.Low1Fps, r.Low1Fps);
                var delta = r == baseRun ? "referencia" : $"{dAvg:+0.0;-0.0;0} % media · {dLow:+0.0;-0.0;0} % 1% low";
                var row = Ui.Columns(
                    new StackPanel { Children = { Ui.Text($"{r.Label}", 14, FontWeights.SemiBold), Ui.Muted($"{r.AtUtc.ToLocalTime():g} · {r.Frames} fotogramas · {r.Seconds:0} s") } },
                    new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { Ui.Text($"{r.AvgFps:0} FPS · 1% {r.Low1Fps:0} · 0,1% {r.Low01Fps:0}", 14, FontWeights.SemiBold), Ui.Muted(delta) }
                    });
                row.Margin = new Thickness(0, 6, 0, 6);
                sp.Children.Add(row);
            }
            _results.Children.Add(card);
        }
        if (all.Count == 0) _results.Children.Add(Ui.Muted("Aún no hay mediciones."));
    }
}
