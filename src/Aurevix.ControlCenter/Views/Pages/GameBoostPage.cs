using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Configuración de AUREVIX Game Boost.</summary>
public sealed class GameBoostPage : UserControl
{
    public GameBoostPage() => Build();

    private void Build()
    {
        var s = GameBoost.Settings;
        var (root, body) = Ui.Page("Game Boost", "Al abrir uno de tus juegos, AUREVIX lo prioriza y aparta lo demás. Al cerrarlo, todo vuelve a como estaba.");

        body.Children.Add(Ui.Card(
            Ui.Eyebrow(GameBoost.ActiveGame is { } g ? $"ACTIVO · {g}" : s.Enabled ? "EN ESPERA" : "DESACTIVADO"),
            Ui.H2("Qué hace"),
            Ui.Muted("• Sube la prioridad de CPU del juego.  • Baja a prioridad mínima las apps de fondo de la lista y libera su memoria RAM.  " +
                     "• Opcional: cambia a Máximo rendimiento (Alto rendimiento en portátiles).  • Opcional: cierra las apps que elijas, como si pulsaras la X."),
            Check("Game Boost activado", s.Enabled, v => s.Enabled = v),
            Check("Cambiar el plan de energía mientras juegas", s.SwitchPowerPlan, v => s.SwitchPowerPlan = v),
            Check("Liberar memoria de las apps de fondo", s.TrimMemory, v => s.TrimMemory = v)));

        // Juegos
        var games = new StackPanel();
        foreach (var exe in s.Games.ToList())
        {
            var row = Ui.Columns(Ui.Text(exe), Ui.Button("Quitar", () => { s.Games.Remove(exe); GameBoost.Save(); Build(); }));
            row.Margin = new Thickness(0, 4, 0, 4);
            games.Children.Add(row);
        }
        if (s.Games.Count == 0) games.Children.Add(Ui.Muted("Abre un juego y añádelo desde la lista de abajo."));
        var running = new ComboBox { MinWidth = 320, Margin = new Thickness(0, 0, 10, 0), ItemsSource = RunningApps() };
        if (running.Items.Count > 0) running.SelectedIndex = 0;
        body.Children.Add(Ui.Card(Ui.H2("Tus juegos"), games,
            Ui.Row(running, Ui.Button("Añadir juego", () =>
            {
                if (running.SelectedItem is not string sel) return;
                var exe = sel.Split(" — ")[0];
                if (!s.Games.Contains(exe, StringComparer.OrdinalIgnoreCase)) s.Games.Add(exe);
                GameBoost.Save(); Build();
            }, primary: true), Ui.Button("Actualizar", Build))));

        body.Children.Add(ListCard("Apps de fondo que se apartan", "Bajan a prioridad mínima mientras juegas; no se cierran.", s.Background));
        body.Children.Add(ListCard("Apps que se cierran al empezar a jugar", "Vacío por defecto. Se cierran de forma amable (como al pulsar la X): si tienen algo sin guardar, te lo preguntarán.", s.CloseOnStart));

        Content = root;
    }

    private Border ListCard(string title, string text, List<string> list)
    {
        var box = new TextBox
        {
            Text = string.Join(Environment.NewLine, list), AcceptsReturn = true, MinHeight = 110, Padding = new Thickness(8),
            Background = Ui.Brush("SurfaceAlt"), Foreground = Ui.Brush("Text"), BorderBrush = Ui.Brush("BorderBrushAx"), CaretBrush = Ui.Brush("Text"),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 8, 0, 0)
        };
        return Ui.Card(Ui.H2(title), Ui.Muted(text + " Un ejecutable por línea."), box,
            Ui.Row(Ui.Button("Guardar", () =>
            {
                list.Clear();
                list.AddRange(box.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase));
                GameBoost.Save();
                MessageBox.Show("Guardado.", "Game Boost");
            })));
    }

    private static CheckBox Check(string text, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Content = text, IsChecked = value };
        cb.Checked += (_, _) => { set(true); GameBoost.Save(); };
        cb.Unchecked += (_, _) => { set(false); GameBoost.Save(); };
        return cb;
    }

    private static List<string> RunningApps()
    {
        var self = Environment.ProcessId;
        return Process.GetProcesses()
            .Where(p => { try { return p.Id != self && p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle); } catch { return false; } })
            .Select(p => { try { return $"{p.ProcessName}.exe — {p.MainWindowTitle}"; } catch { return null; } finally { p.Dispose(); } })
            .Where(x => x is not null).Cast<string>().Distinct().OrderBy(x => x).ToList();
    }
}
