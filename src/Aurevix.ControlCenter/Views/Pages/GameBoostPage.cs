using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Performance;

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

        // Juegos y ajustes por juego
        var games = new StackPanel();
        foreach (var exe in s.Games.ToList()) games.Children.Add(GameRow(exe));
        if (s.Games.Count == 0) games.Children.Add(Ui.Muted("Abre un juego y añádelo desde la lista de abajo."));
        var running = new ComboBox { MinWidth = 320, Margin = new Thickness(0, 0, 10, 0), ItemsSource = RunningApps() };
        if (running.Items.Count > 0) running.SelectedIndex = 0;
        body.Children.Add(Ui.Card(Ui.H2("Tus juegos"),
            Ui.Muted("Además de Game Boost, cada juego puede tener sus propios ajustes de Windows (los mismos de Configuración > Pantalla > Gráficos y de Compatibilidad)."),
            games,
            Ui.Row(running, Ui.Button("Añadir juego", () =>
            {
                if (running.SelectedItem is not string sel) return;
                var exe = sel.Split(" — ")[0];
                if (!s.Games.Contains(exe, StringComparer.OrdinalIgnoreCase)) s.Games.Add(exe);
                var path = ProcessPath(exe);
                if (path is not null) s.GamePaths[exe] = path;
                GameBoost.Save(); Build();
            }, primary: true), Ui.Button("Actualizar", Build))));

        body.Children.Add(Ui.Card(Ui.H2("Al empezar a jugar"),
            Check("Vaciar la memoria en espera (como ISLC)", s.PurgeStandby, v => s.PurgeStandby = v),
            Check("Temporizador del sistema a 0,5 ms mientras juegas", s.TimerResolution, v => s.TimerResolution = v),
            Check("Reservar la mitad de los núcleos para el juego (las apps de fondo usan el resto; 8+ núcleos)", s.ReserveCores, v => s.ReserveCores = v),
            Check("Usar la gráfica de alto rendimiento en los juegos (útil en portátiles con dos gráficas)", s.AutoHighPerfGpu, v => s.AutoHighPerfGpu = v),
            Ui.Row(Ui.Button("Quitar preferencias de gráfica añadidas por AUREVIX", () =>
            {
                var n = GameBoost.RemoveAutoGpuPreferences();
                MessageBox.Show(n > 0 ? $"Se han quitado {n} preferencias de gráfica." : "No había preferencias añadidas por AUREVIX.", "Game Boost");
                Build();
            })),
            Ui.Muted("Memoria en espera: lo hace AUREVIX Core Service; útil si notas tirones con poca RAM libre. Temporizador: afecta a todo el sistema solo con «Resolución de temporizador global» (Optimización). " +
                     "Núcleos: compruébalo con Benchmark; en algunos juegos no cambia nada. Todo se restaura al cerrar el juego.")));
        body.Children.Add(ListCard("Apps de streaming y grabación", "Nunca se apartan: suben a prioridad «por encima de lo normal» mientras juegas (OBS, Streamlabs…).", s.Streaming));

        body.Children.Add(ListCard("Apps de fondo que se apartan", "Bajan a prioridad mínima mientras juegas; no se cierran.", s.Background));
        body.Children.Add(ListCard("Apps que se cierran al empezar a jugar", "Vacío por defecto. Se cierran de forma amable (como al pulsar la X): si tienen algo sin guardar, te lo preguntarán.", s.CloseOnStart));

        Content = root;
    }

    private UIElement GameRow(string exe)
    {
        var s = GameBoost.Settings;
        var path = s.GamePaths.GetValueOrDefault(exe);
        var opts = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (path is not null)
        {
            opts.Children.Add(Toggle("GPU de alto rendimiento", PerGameTweaks.HighPerfGpu(path), v => PerGameTweaks.SetHighPerfGpu(path, v)));
            opts.Children.Add(Toggle("Sin optimizaciones de pantalla completa", PerGameTweaks.NoFso(path), v => PerGameTweaks.SetNoFso(path, v)));
        }
        opts.Children.Add(Toggle("Prioridad alta siempre", false, v =>
            _ = CoreServiceClient.SendAsync(new CoreRequest { Op = v ? "game-priority-set" : "game-priority-remove", Action = exe })));
        if (path is not null)
            opts.Children.Add(Toggle("Excluir de Defender (reduce seguridad)", false, v => DefenderExclusion(System.IO.Path.GetDirectoryName(path)!, v)));

        var head = Ui.Columns(new StackPanel { Children = { Ui.Text(exe, 14, FontWeights.SemiBold), Ui.Muted(path ?? "Ruta desconocida: vuelve a añadirlo con el juego abierto para los ajustes de Windows.") } },
            Ui.Button("Quitar", () =>
            {
                s.Games.Remove(exe);
                if (path is not null) { PerGameTweaks.SetHighPerfGpu(path, false); PerGameTweaks.SetNoFso(path, false); }
                _ = CoreServiceClient.SendAsync(new CoreRequest { Op = "game-priority-remove", Action = exe });
                s.GamePaths.Remove(exe);
                GameBoost.Save(); Build();
            }));
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
        sp.Children.Add(head);
        sp.Children.Add(opts);
        return sp;
    }

    private static CheckBox Toggle(string text, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 18, 4) };
        cb.Checked += (_, _) => set(true);
        cb.Unchecked += (_, _) => set(false);
        return cb;
    }

    /// <summary>Exclusión de Defender con PowerShell elevado (UAC): el usuario lo autoriza siempre explícitamente.</summary>
    private static void DefenderExclusion(string folder, bool add)
    {
        if (add && MessageBox.Show($"Defender dejará de analizar en tiempo real esta carpeta:\n\n{folder}\n\nSolo hazlo con juegos de tiendas oficiales (Steam, Epic…). " +
                "Un archivo malicioso dentro de esa carpeta no se detectaría. ¿Continuar?", "Reduce la seguridad", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var f = folder.Replace("'", "''");
        var script = add ? $"Add-MpPreference -ExclusionPath '{f}'" : $"Remove-MpPreference -ExclusionPath '{f}'";
        try
        {
            var enc = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -EncodedCommand {enc}") { UseShellExecute = true, Verb = "runas" })!;
            p.WaitForExit(30000);
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string? ProcessPath(string exe)
    {
        foreach (var p in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(exe)))
        {
            try { return p.MainModule?.FileName; } catch { } finally { p.Dispose(); }
        }
        return null;
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
