using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>
/// Optimización: niveles predefinidos (Recomendado, Rendimiento, Máximo), un interruptor por ajuste con recomendación,
/// "Más FPS de verdad" (funciones del driver), mantenimiento e historial con deshacer.
/// </summary>
public sealed class OptimizationPage : UserControl
{
    private static readonly Dictionary<TweakCategory, string> CategoryNames = new()
    {
        [TweakCategory.Modes] = "Modos de juego AUREVIX",
        [TweakCategory.PowerPlans] = "Planes de energía AUREVIX",
        [TweakCategory.Gaming] = "Juegos y latencia",
        [TweakCategory.Input] = "Ratón, teclado y USB",
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
    private string _filter = "all";   // all | recommended | applied
    private string _query = "";
    private StackPanel? _catalogHost;
    private bool _busy;

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

        // Cada ajuste, con su interruptor
        _body.Children.Add(Section("Todos los ajustes", "Activa o desactiva cada ajuste al momento. Cada uno indica si AUREVIX lo recomienda, para qué sirve y qué inconvenientes tiene. Al desactivarlo vuelve exactamente el valor que tenías."));
        var search = new TextBox { Width = 220, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Buscar un ajuste", Text = _query };
        search.TextChanged += (_, _) => { _query = search.Text.Trim(); BuildCatalog(); };
        var bar = Ui.Wrap(Ui.Muted("Buscar:"), search);
        foreach (var (id, name) in new[] { ("all", "Todos"), ("recommended", "Recomendados"), ("applied", "Activados") })
        {
            var rb = new RadioButton { Content = name, GroupName = "tweakfilter", IsChecked = _filter == id, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            rb.Checked += (_, _) => { _filter = id; BuildCatalog(); };
            bar.Children.Add(rb);
        }
        ((TextBlock)bar.Children[0]).Margin = new Thickness(0, 4, 8, 0);
        _body.Children.Add(Ui.Card(bar));
        _catalogHost = new StackPanel();
        _body.Children.Add(_catalogHost);
        BuildCatalog();

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

    private void BuildCatalog()
    {
        if (_catalogHost is null) return;
        _catalogHost.Children.Clear();
        _checks.Clear();
        var shown = 0;
        foreach (var group in AppServices.Catalog.GroupBy(t => t.Category).OrderBy(g => g.Key))
        {
            var items = group.Where(Matches).ToList();
            if (items.Count == 0) continue;
            var on = group.Count(t => _states.GetValueOrDefault(t.Id) == TweakState.Applied);
            var head = Ui.Columns(new StackPanel { Children = { Ui.H2(CategoryNames.GetValueOrDefault(group.Key, group.Key.ToString())), Ui.Muted($"{on} de {group.Count()} activados") } },
                Ui.Button("Activar recomendados", () => _ = EnableRecommendedAsync(group.ToList())));
            var card = Ui.Card(head);
            var sp = (StackPanel)card.Child;
            foreach (var t in items) { sp.Children.Add(TweakRow(t)); shown++; }
            _catalogHost.Children.Add(card);
        }
        if (shown == 0) _catalogHost.Children.Add(Ui.Card(Ui.Muted("Ningún ajuste coincide con la búsqueda.")));
    }

    private bool Matches(TweakDefinition t)
    {
        if (_filter == "recommended" && !t.Levels.Contains(OptimizationLevel.Recommended)) return false;
        if (_filter == "applied" && _states.GetValueOrDefault(t.Id) != TweakState.Applied) return false;
        return _query.Length == 0 || t.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase) ||
               t.Description.Contains(_query, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Recomendación de AUREVIX según los niveles en los que aparece el ajuste.</summary>
    private static (string Text, string Brush) Advice(TweakDefinition t)
    {
        if (t.OptIn) return ("Solo si sabes lo que haces", "Attention");
        if (t.Levels.Contains(OptimizationLevel.Recommended)) return ("Recomendado", "Ok");
        if (t.Levels.Contains(OptimizationLevel.Performance)) return ("Para jugar", "Accent");
        if (t.Levels.Contains(OptimizationLevel.Maximum)) return ("Competitivo", "Review");
        return ("Opcional", "Unavailable");
    }

    private async Task EnableRecommendedAsync(IReadOnlyList<TweakDefinition> group)
    {
        var list = group.Where(t => t.Levels.Contains(OptimizationLevel.Recommended) && _states.GetValueOrDefault(t.Id) != TweakState.Applied &&
                                    AppServices.Tweaks.CompatibilityProblem(t) is null && (!t.HasMachinePart || _serviceOk)).ToList();
        if (list.Count == 0) { MessageBox.Show("Los ajustes recomendados de esta sección ya están activados.", "Optimización"); return; }
        await ApplyAsync(list, "Recomendados");
    }

    private async Task ToggleAsync(TweakDefinition t, CheckBox cb)
    {
        if (_busy) return;
        var turnOn = cb.IsChecked == true;
        if (turnOn && (t.OptIn || t.Risk != RiskLevel.Low))
        {
            var warn = t.OptIn ? t.Warning : t.PossibleImpact;
            if (MessageBox.Show($"{t.Title}\n\n{warn}\n\n¿Activarlo?", "Ajuste avanzado", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            { cb.IsChecked = false; return; }
        }
        _busy = true;
        cb.IsEnabled = false;
        try
        {
            var r = turnOn ? await OptimizationManager.ApplyAsync([t], t.Title) : await OptimizationManager.DisableTweakAsync(t, AppServices.Catalog);
            if (!r.Success) MessageBox.Show(string.Join("\n", r.Messages), t.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (r.RequiresRestart) RestartPrompt.Ask("Reinicia para completar el cambio", $"«{t.Title}» se termina de aplicar al reiniciar.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cambiar «{t.Title}»:\n{ex.Message}", t.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            await ReloadAsync();
        }
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
        var applied = state == TweakState.Applied;
        var sw = new CheckBox
        {
            Style = Ui.Res<Style>("Switch"), IsChecked = applied,
            IsEnabled = problem is null && !needsService && (!applied || OptimizationManager.History.Records.Any(r => r.TweakIds.Contains(t.Id))),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 2, 0, 0),
            ToolTip = applied ? "Desactivar (vuelve el valor original)" : "Activar"
        };
        sw.Click += (_, _) => _ = ToggleAsync(t, sw);
        _checks[t.Id] = sw;

        var head = new WrapPanel();
        head.Children.Add(Ui.Text(t.Title, 14, FontWeights.SemiBold));
        void AddTag(string text, string brush) { var p = Ui.Pill(text, Ui.Brush(brush)); p.Margin = new Thickness(8, 1, 0, 0); head.Children.Add(p); }
        var (advice, adviceBrush) = Advice(t);
        AddTag(advice, adviceBrush);
        if (t.Risk != RiskLevel.Low) AddTag(t.Risk == RiskLevel.Medium ? "Riesgo medio" : "Riesgo alto", "Review");
        if (t.RequiresRestart) AddTag("Reinicio", "Unavailable");

        var info = new StackPanel();
        info.Children.Add(head);
        info.Children.Add(Ui.Muted(t.Description));
        info.Children.Add(Ui.Muted($"Efecto: {t.ExpectedEffect}  ·  Inconvenientes: {t.PossibleImpact}"));
        if (problem is not null) info.Children.Add(Ui.Muted("No aplicable en este equipo: " + problem));
        else if (needsService) info.Children.Add(Ui.Muted("Requiere AUREVIX Core Service."));
        else if (applied && !sw.IsEnabled) info.Children.Add(Ui.Muted("Ya estaba así en Windows (no lo cambió AUREVIX)."));

        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
        DockPanel.SetDock(sw, Dock.Right);
        row.Children.Add(sw);
        row.Children.Add(info);
        return row;
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
