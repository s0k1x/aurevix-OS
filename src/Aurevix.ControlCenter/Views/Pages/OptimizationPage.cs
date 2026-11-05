using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>
/// Optimización: niveles predefinidos (Recomendado, Rendimiento, Máximo), modo Personalizado con cada ajuste,
/// "Más FPS de verdad" (funciones del driver), mantenimiento e historial con deshacer.
/// </summary>
public sealed class OptimizationPage : UserControl
{
    private static readonly Dictionary<TweakCategory, string> CategoryNames = new()
    {
        [TweakCategory.Gaming] = "Juegos y latencia",
        [TweakCategory.Graphics] = "Gráficos",
        [TweakCategory.Power] = "Energía",
        [TweakCategory.Startup] = "Inicio y segundo plano",
        [TweakCategory.Storage] = "Almacenamiento",
        [TweakCategory.Network] = "Red",
        [TweakCategory.Interface] = "Interfaz y Explorador",
        [TweakCategory.Privacy] = "Privacidad y telemetría",
        [TweakCategory.Maintenance] = "Mantenimiento",
        [TweakCategory.Security] = "Seguridad (solo si sabes lo que haces)",
    };

    private readonly StackPanel _body;
    private Dictionary<string, TweakState> _states = new();
    private readonly Dictionary<string, CheckBox> _checks = new();
    private bool _serviceOk;

    public OptimizationPage()
    {
        (Content, _body) = Ui.Page("Rendimiento y optimización",
            "Catálogo propio y revisado. Ningún ajuste desactiva Defender, Firewall, Windows Update, SmartScreen ni UAC. Todo se puede deshacer.");
        _body.Children.Add(Ui.Muted("Comprobando el estado de los ajustes…"));
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _serviceOk = await CoreServiceClient.IsAvailableAsync();
        _states = await OptimizationManager.StatusAsync(AppServices.Catalog);
        Build();
    }

    private void Build()
    {
        // Conservar título y subtítulo.
        while (_body.Children.Count > 2) _body.Children.RemoveAt(2);
        _checks.Clear();

        if (!_serviceOk)
            _body.Children.Add(Ui.Card(Ui.Eyebrow("AVISO"), Ui.H2("AUREVIX Core Service no está en ejecución"),
                Ui.Muted("Sin el servicio solo se pueden aplicar los ajustes de tu usuario. Reinicia el equipo o reinstala AUREVIX para activar los ajustes de sistema.")));

        _body.Children.Add(FpsCard());

        // Niveles
        _body.Children.Add(Section("Niveles de optimización", "Cada nivel incluye al anterior. Los ajustes que no encajan con tu equipo se omiten solos."));
        var levels = new WrapPanel();
        levels.Children.Add(LevelCard(OptimizationLevel.Recommended, "Recomendado", "Seguro para cualquier equipo. Limpia segundo plano, telemetría opcional y mejora la respuesta."));
        levels.Children.Add(LevelCard(OptimizationLevel.Performance, "Rendimiento", "Para jugar: energía sin ahorro, prioridad al juego, sin grabación en segundo plano."));
        levels.Children.Add(LevelCard(OptimizationLevel.Maximum, "Máximo", "Competitivo: plan Máximo rendimiento, temporizador global, sin animaciones. Más consumo."));
        _body.Children.Add(levels);

        // Personalizado
        _body.Children.Add(Section("Personalizado", "Marca los ajustes que quieras y pulsa «Aplicar selección». Los ya aplicados aparecen marcados; para quitarlos usa el historial."));
        foreach (var group in AppServices.Catalog.GroupBy(t => t.Category).OrderBy(g => g.Key))
        {
            var card = Ui.Card(Ui.H2(CategoryNames.GetValueOrDefault(group.Key, group.Key.ToString())));
            var sp = (StackPanel)card.Child;
            foreach (var t in group) sp.Children.Add(TweakRow(t));
            _body.Children.Add(card);
        }
        _body.Children.Add(Ui.Row(Ui.Button("Aplicar selección", () => _ = ApplyCustomAsync(), primary: true)));

        // Mantenimiento
        _body.Children.Add(Section("Mantenimiento", "Tareas puntuales con herramientas oficiales de Windows."));
        var maint = Ui.Wrap();
        foreach (var (id, name) in CoreServiceProtocol.MaintenanceActions)
        {
            var b = Ui.Button(name, () => _ = MaintenanceAsync(id, name));
            b.Margin = new Thickness(0, 0, 8, 8);
            b.IsEnabled = _serviceOk;
            maint.Children.Add(b);
        }
        _body.Children.Add(Ui.Card(maint));

        // Historial
        var hist = OptimizationManager.History.Records.OrderByDescending(r => r.AtUtc).ToList();
        _body.Children.Add(Section("Cambios aplicados", hist.Count == 0 ? "Todavía no has aplicado optimizaciones." : "Deshaz un cambio concreto o todos a la vez."));
        if (hist.Count > 0)
        {
            var list = new StackPanel();
            foreach (var r in hist)
            {
                var row = Ui.Columns(new StackPanel { Children = { Ui.Text(r.Label), Ui.Muted($"{r.AtUtc.ToLocalTime():g} · {r.TweakIds.Count} ajustes{(r.RequiresRestart ? " · requiere reinicio" : "")}") } },
                    Ui.Button("Deshacer", () => _ = RevertAsync(r)));
                row.Margin = new Thickness(0, 4, 0, 8);
                list.Children.Add(row);
            }
            _body.Children.Add(Ui.Card(list, Ui.Row(Ui.Button("Deshacer todo y volver a Windows original", () => _ = RevertAllAsync()))));
        }
        Ui.Stagger(_body);
    }

    private static StackPanel Section(string title, string subtitle)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 22, 0, 10) };
        sp.Children.Add(Ui.H2(title));
        sp.Children.Add(Ui.Muted(subtitle));
        return sp;
    }

    private Border FpsCard()
    {
        var vendor = AppServices.Hardware.GpuVendor;
        var (title, text, url) = vendor switch
        {
            "AMD" => ("AMD Fluid Motion Frames (AFMF) + FSR",
                "Tu gráfica AMD puede generar fotogramas intermedios desde el driver y casi duplicar los FPS que ves en pantalla (añade algo de latencia). " +
                "Actívalo en AMD Software: Adrenalin Edition > Gaming > Gráficos > AMD Fluid Motion Frames. Combínalo con FSR o Radeon Super Resolution.",
                "https://www.amd.com/es/support/download/drivers.html"),
            "NVIDIA" => ("DLSS, Frame Generation y Smooth Motion",
                "En RTX 40/50 la generación de fotogramas (DLSS FG o Smooth Motion en la app NVIDIA) puede casi duplicar los FPS mostrados. En otras GeForce usa DLSS o NIS.",
                "https://www.nvidia.com/es-es/software/nvidia-app/"),
            "Intel" => ("XeSS y driver Intel Arc",
                "Usa XeSS en los juegos compatibles y mantén el driver al día con Intel Driver & Support Assistant.",
                "https://www.intel.es/content/www/es/es/support/detect.html"),
            _ => ("Generación de fotogramas y reescalado",
                "Las mayores subidas de FPS vienen de las funciones del driver (AFMF, DLSS, FSR, XeSS), no de ajustes de Windows.", "https://www.amd.com/es/support")
        };
        return Ui.Card(Ui.Eyebrow("MÁS FPS DE VERDAD"), Ui.H2(title), Ui.Muted(text),
            Ui.Muted("Los ajustes de Windows suelen dar entre un 0 y un 10 % de FPS medios y, sobre todo, menos tirones. Compruébalo con AUREVIX Benchmark."),
            Ui.Row(Ui.Button("Descargar driver oficial", () => SystemTools.Start(url), primary: true)));
    }

    private Border LevelCard(OptimizationLevel level, string name, string text)
    {
        var tweaks = TweakEngine.ForLevel(AppServices.Catalog, level);
        var compatible = tweaks.Where(t => AppServices.Tweaks.CompatibilityProblem(t) is null).ToList();
        var applied = compatible.Count(t => _states.GetValueOrDefault(t.Id) == TweakState.Applied);
        var card = Ui.Card(Ui.H2(name), Ui.Muted(text),
            Ui.Muted($"{compatible.Count} ajustes para tu equipo · {applied} ya aplicados"),
            Ui.Row(Ui.Button($"Aplicar {name}", () => _ = ApplyAsync(compatible, $"Nivel {name}"), primary: level == OptimizationLevel.Recommended)));
        card.Width = 300;
        card.Margin = new Thickness(0, 0, 14, 14);
        return card;
    }

    private UIElement TweakRow(TweakDefinition t)
    {
        var state = _states.GetValueOrDefault(t.Id, TweakState.Unknown);
        var problem = AppServices.Tweaks.CompatibilityProblem(t);
        var needsService = t.HasMachinePart && !_serviceOk;
        var cb = new CheckBox
        {
            IsChecked = state == TweakState.Applied,
            IsEnabled = state != TweakState.Applied && problem is null && !needsService,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 10, 0)
        };
        _checks[t.Id] = cb;

        var head = new WrapPanel();
        head.Children.Add(Ui.Text(t.Title, 14, FontWeights.SemiBold));
        void AddTag(string text, string brush) { var p = Ui.Pill(text, Ui.Brush(brush)); p.Margin = new Thickness(8, 1, 0, 0); head.Children.Add(p); }
        if (state == TweakState.Applied) AddTag("Aplicado", "Ok");
        if (t.Risk != RiskLevel.Low) AddTag(t.Risk == RiskLevel.Medium ? "Riesgo medio" : "Riesgo alto", "Review");
        if (t.RequiresRestart) AddTag("Reinicio", "Unavailable");
        if (t.OptIn) AddTag("Reduce seguridad", "Attention");

        var info = new StackPanel();
        info.Children.Add(head);
        info.Children.Add(Ui.Muted(t.Description));
        info.Children.Add(Ui.Muted($"Efecto: {t.ExpectedEffect}  ·  Inconvenientes: {t.PossibleImpact}"));
        if (problem is not null) info.Children.Add(Ui.Muted("No aplicable: " + problem));
        else if (needsService) info.Children.Add(Ui.Muted("Requiere AUREVIX Core Service."));

        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        DockPanel.SetDock(cb, Dock.Left);
        row.Children.Add(cb);
        row.Children.Add(info);
        return row;
    }

    private async Task ApplyCustomAsync()
    {
        var selected = AppServices.Catalog.Where(t => _checks.TryGetValue(t.Id, out var c) && c.IsEnabled && c.IsChecked == true).ToList();
        if (selected.Count == 0) { MessageBox.Show("Marca al menos un ajuste nuevo.", "Optimización"); return; }
        foreach (var t in selected.Where(t => t.OptIn))
            if (MessageBox.Show($"{t.Title}\n\n{t.Warning}\n\n¿Seguro que quieres activarlo?", "Aviso de seguridad",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await ApplyAsync(selected, "Personalizado");
    }

    private async Task ApplyAsync(IReadOnlyList<TweakDefinition> tweaks, string label)
    {
        var pending = tweaks.Where(t => _states.GetValueOrDefault(t.Id) != TweakState.Applied).ToList();
        if (pending.Count == 0) { MessageBox.Show("Todos estos ajustes ya están aplicados.", "Optimización"); return; }
        var list = string.Join("\n", pending.Take(25).Select(t => "• " + t.Title)) + (pending.Count > 25 ? $"\n… y {pending.Count - 25} más" : "");
        if (MessageBox.Show($"Se aplicarán {pending.Count} ajustes:\n\n{list}\n\nSe guarda una copia de seguridad de todo. ¿Continuar?",
                label, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var r = await OptimizationManager.ApplyAsync(pending, label);
        var applied = r.Messages.Count(m => m.StartsWith("Aplicado"));
        var skipped = r.Messages.Where(m => !m.StartsWith("Aplicado")).ToList();
        MessageBox.Show((r.Success ? $"{applied} ajustes aplicados." : "No se pudo completar.") +
                        (skipped.Count > 0 ? "\n\n" + string.Join("\n", skipped.Take(12)) : ""),
            label, MessageBoxButton.OK, r.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (r.Success && r.RequiresRestart)
            RestartPrompt.Ask("Reinicia para aplicarlo todo", "Algunos ajustes de sistema (servicios, energía, GPU) solo se activan por completo tras reiniciar.");
        await ReloadAsync();
    }

    private async Task RevertAsync(OptimizationRecord rec)
    {
        if (MessageBox.Show($"¿Deshacer «{rec.Label}»?", "Optimización", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var r = await OptimizationManager.RevertAsync(rec);
        if (!r.Success) MessageBox.Show(string.Join("\n", r.Messages), "Optimización");
        else if (r.RequiresRestart) RestartPrompt.Ask("Reinicia para terminar de deshacer", "Los valores originales se han restaurado; algunos se aplican al reiniciar.");
        await ReloadAsync();
    }

    private async Task RevertAllAsync()
    {
        if (MessageBox.Show("¿Deshacer TODAS las optimizaciones y volver a la configuración original de Windows?", "Optimización",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var r = await OptimizationManager.RevertAllAsync();
        if (!r.Success) MessageBox.Show(string.Join("\n", r.Messages), "Optimización");
        else if (r.RequiresRestart) RestartPrompt.Ask("Reinicia para volver a Windows original", "Todo se ha restaurado; algunos valores se aplican al reiniciar.");
        await ReloadAsync();
    }

    private static async Task MaintenanceAsync(string id, string name)
    {
        if (MessageBox.Show($"{name}\n\nPuede tardar varios minutos. ¿Continuar?", "Mantenimiento", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "maintenance", Action = id }, TimeSpan.FromMinutes(40));
        MessageBox.Show(r.Ok ? string.Join("\n", r.Messages) : r.Error ?? "Error", "Mantenimiento");
    }
}
