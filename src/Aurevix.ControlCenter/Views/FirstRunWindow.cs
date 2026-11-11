using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core.Diagnostics;
using Aurevix.Core.Logging;
using Aurevix.Diagnostics;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views;

/// <summary>
/// AUREVIX Setup — asistente del primer inicio de sesión:
/// 1 bienvenida · 2 diagnóstico · 3 informe · 4 punto de restauración · 5 perfil · 6 resumen.
/// En cualquier momento se puede omitir y entrar directamente a Windows.
/// </summary>
public sealed class FirstRunWindow : Window
{
    private readonly ContentControl _host = new();
    private readonly TextBlock _step = Ui.Muted("");
    private readonly List<string> _done = [];
    private readonly List<string> _pending = [];
    private DiagnosticReport? _report;
    private string? _chosenProfile;
    private readonly CheckBox _optimize = new() { Content = new TextBlock { Text = "Optimizar Windows para rendimiento (nivel Rendimiento de AUREVIX: menos procesos y servicios en segundo plano, energía sin ahorros, prioridad a lo que usas). Todo reversible.", TextWrapping = TextWrapping.Wrap }, IsChecked = true, Margin = new Thickness(0, 10, 0, 6) };

    public FirstRunWindow()
    {
        Title = "AUREVIX Setup";
        Width = 900; Height = 640; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush("Bg");

        var header = new DockPanel { Margin = new Thickness(32, 24, 32, 0) };
        var brand = new TextBlock { Text = "AUREVIX", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Accent") };
        var skip = Ui.Button("Omitir y entrar en Windows", Skip);
        DockPanel.SetDock(skip, Dock.Right);
        header.Children.Add(skip);
        header.Children.Add(new StackPanel { Children = { brand, _step } });

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_host);
        Content = root;
        Welcome();
    }

    private void Show(int n, string name, params UIElement[] content)
    {
        _step.Text = $"Paso {n} de 7 · {name}";
        var sp = new StackPanel { Margin = new Thickness(32, 20, 32, 32), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in content) sp.Children.Add(c);
        _host.Content = new ScrollViewer { Content = sp };
    }

    // 1. Bienvenida
    private void Welcome() => Show(1, "Bienvenida",
        Ui.H1("Te damos la bienvenida a AUREVIX OS"),
        Ui.Muted($"Versión {AppServices.CurrentVersion} · basada en Windows 11 oficial"),
        Ui.Card(
            Ui.Text("Vamos a preparar tu equipo en unos pocos pasos:"),
            Ui.Muted("1. Analizar el equipo (solo lectura, nada sale de tu PC)."),
            Ui.Muted("2. Mostrarte un informe claro de lo que hemos detectado."),
            Ui.Muted("3. Crear un punto de restauración antes de cambiar nada."),
            Ui.Muted("4. Elegir un perfil: Equilibrado, Gaming, Productividad o Eficiencia."),
            Ui.Muted("5. Resumen de lo realizado y de lo pendiente.")),
        Ui.Muted("AUREVIX no desactiva Defender, Firewall, Windows Update, SmartScreen ni UAC. Todos los cambios se explican y se pueden deshacer."),
        Ui.Row(Ui.Button("Empezar", () => _ = Diagnose(), primary: true)));

    // 2-3. Diagnóstico e informe
    private async Task Diagnose()
    {
        var bar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 16, 0, 0) };
        Show(2, "Diagnóstico", Ui.H1("Analizando tu equipo…"),
            Ui.Muted("Detectando fabricante, modelo, CPU, GPU, RAM, discos, red, pantalla, controladores y Windows Update. Puede tardar hasta un minuto."), bar);
        try
        {
            _report = await AppServices.RunDiagnosticsAsync();
            _done.Add("Diagnóstico inicial realizado y guardado en tu equipo.");
            Report();
        }
        catch (Exception ex)
        {
            AurevixLog.Error("Diagnóstico del primer inicio fallido", ex);
            _pending.Add("Repetir el diagnóstico desde el Control Center.");
            Show(2, "Diagnóstico", Ui.H1("No se pudo completar el análisis"), Ui.Text(ex.Message),
                Ui.Row(Ui.Button("Reintentar", () => _ = Diagnose()), Ui.Button("Continuar sin diagnóstico", RestorePoint, primary: true)));
        }
    }

    private void Report()
    {
        var r = _report!;
        var s = r.Snapshot;
        var list = new StackPanel();
        foreach (var f in r.Findings.OrderByDescending(f => f.Level == FindingLevel.Attention).ThenByDescending(f => f.Level == FindingLevel.Review))
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var pill = Ui.Pill(ReportWriter.LevelText(f.Level), Ui.LevelBrush(f.Level));
            DockPanel.SetDock(pill, Dock.Left);
            row.Children.Add(pill);
            row.Children.Add(new StackPanel { Children = { Ui.Text($"{f.Area}: {f.Title}"), Ui.Muted(f.Recommendation ?? f.Detail) } });
            list.Children.Add(row);
            if (f.Level == FindingLevel.Attention && f.Recommendation is not null) _pending.Add($"{f.Area}: {f.Recommendation}");
        }
        Show(3, "Informe",
            Ui.H1($"{s.Manufacturer} {s.Model}".Trim()),
            Ui.Muted($"{s.Cpu?.Name} · {string.Join(", ", s.Gpus.Select(g => g.Name))} · {(s.TotalMemoryBytes ?? 0) / 1024d / 1024 / 1024:0} GB RAM"),
            Ui.Card(Ui.H2($"Estado general: {ReportWriter.LevelText(r.Overall)}"), list),
            Ui.Muted("Los controladores se recomiendan solo desde Windows Update o la web oficial del fabricante. AUREVIX no instala nada sin tu confirmación."),
            Ui.Row(Ui.Button("Continuar", RestorePoint, primary: true), Ui.Button("Abrir Windows Update", SystemTools.OpenWindowsUpdate)));
    }

    // 4. Punto de restauración
    private void RestorePoint() => Show(4, "Protección",
        Ui.H1("Crear un punto de restauración"),
        Ui.Card(Ui.Text("Antes de aplicar cambios conviene crear un punto de restauración de Windows."),
            Ui.Muted("AUREVIX lo crea por ti, sin avisos de permisos. Si Protección del sistema está desactivada, no se podrá crear; " +
                     "AUREVIX guarda igualmente una copia de cada valor que cambie.")),
        Ui.Row(
            Ui.Button("Crear punto y continuar", async () =>
            {
                var ok = await SystemTools.CreateRestorePointAsync("AUREVIX primer inicio");
                if (ok) _done.Add("Punto de restauración creado.");
                else _pending.Add("Crear un punto de restauración (Recuperación) — no se pudo crear durante el asistente.");
                ChooseProfile();
            }, primary: true),
            Ui.Button("Continuar sin punto", () => { _pending.Add("Crear un punto de restauración (opcional)."); ChooseProfile(); })));

    // 5. Perfil
    private void ChooseProfile()
    {
        var options = new StackPanel();
        foreach (var p in AppServices.Profiles)
        {
            var plan = AppServices.ProfileManager.ResolvePowerPlan(p);
            var rb = new RadioButton
            {
                GroupName = "profile", Margin = new Thickness(0, 6, 0, 6), Foreground = Ui.Brush("Text"),
                IsChecked = p.Id == "balanced",
                Content = new StackPanel { Children = { Ui.Text(p.Name, 15, FontWeights.SemiBold), Ui.Muted(p.Description),
                    Ui.Muted(plan is null ? "El plan de energía preferido no existe en este equipo: se mantiene el actual." : "") } }
            };
            rb.Checked += (_, _) => _chosenProfile = p.Id;
            options.Children.Add(rb);
        }
        _chosenProfile = "balanced";
        Show(5, "Perfil", Ui.H1("Elige cómo quieres usar tu equipo"),
            Ui.Muted("Podrás cambiarlo o deshacerlo en cualquier momento desde AUREVIX Control Center > Perfiles."),
            Ui.Card(options), _optimize,
            Ui.Row(Ui.Button("Aplicar", ApplyProfile, primary: true),
                   Ui.Button("No aplicar ningún perfil", () => { _pending.Add("Elegir un perfil (opcional)."); StyleStep(); })));
    }

    private async void ApplyProfile()
    {
        var p = AppServices.ProfileManager.Find(_chosenProfile ?? "balanced")!;
        var preview = AppServices.Tweaks.Preview(AppServices.ProfileManager.TweaksOf(p));
        var lines = string.Join("\n", preview.Select(c => $"• {c.Title}"));
        if (MessageBox.Show($"Se aplicará el perfil «{p.Name}»:\n\n{lines}\n\n¿Continuar?", "Confirmar", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        ApplyResult r = AppServices.ProfileManager.Switch(p.Id);
        if (r.Success)
        {
            AppServices.State.ActiveProfile = p.Id;
            _done.Add($"Perfil «{p.Name}» aplicado.");
            _done.AddRange(r.Messages.Where(m => !m.StartsWith("Aplicado")).Select(m => "  " + m));
        }
        else _pending.Add($"El perfil «{p.Name}» no se pudo aplicar: {string.Join(" ", r.Messages)}");

        if (_optimize.IsChecked == true)
        {
            try
            {
                var tweaks = TweakEngine.ForLevel(AppServices.Catalog, OptimizationLevel.Performance)
                    .Where(t => AppServices.Tweaks.CompatibilityProblem(t) is null).ToList();
                var o = await OptimizationManager.ApplyAsync(tweaks, "Nivel Rendimiento (asistente)");
                if (o.Success) _done.Add($"Optimización Rendimiento aplicada ({tweaks.Count} ajustes).");
                else _pending.Add("Optimización: " + string.Join(" ", o.Messages.Take(3)));
                if (o.RequiresRestart) _pending.Add("Reiniciar el equipo para terminar de aplicar la optimización.");
            }
            catch (Exception ex) { _pending.Add("Optimización no aplicada: " + ex.Message); }
        }
        StyleStep();
    }

    // 6. Aspecto
    private void StyleStep()
    {
        var shell = new CheckBox { Content = new TextBlock { Text = "AUREVIX Shell: barra de tareas y menú Inicio con el diseño AUREVIX", TextWrapping = TextWrapping.Wrap }, IsChecked = Services.AurevixShell.IsAvailable, IsEnabled = Services.AurevixShell.IsAvailable, Margin = new Thickness(0, 4, 0, 4) };
        var nusa = new CheckBox { Content = new TextBlock { Text = "Nusa: isla de música, letras y temporizador en la parte superior de la pantalla (Microsoft Store)", TextWrapping = TextWrapping.Wrap }, IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
        Show(6, "Aspecto",
            Ui.H1("Dale a Windows el estilo AUREVIX"),
            Ui.Muted("Así se ve AUREVIX OS. Todo se puede desactivar después en Apariencia, y «Restablecer aspecto de Windows 11» lo deja como estaba."),
            Ui.Card(
                Ui.Text("Qué cambia"),
                Ui.Muted("• Tema claro con fondo Daylight, iconos AUREVIX y acento verde (el modo oscuro, en Apariencia)"),
                Ui.Muted("• Barra de tareas limpia: iconos centrados, sin widgets, Copilot ni Vista de tareas"),
                Ui.Muted("• Inicio sin recomendaciones ni anuncios; pantalla de bloqueo sin sugerencias"),
                Ui.Muted("• Explorador: abre en «Este equipo», extensiones visibles, sin archivos recientes")),
            shell, nusa,
            Ui.Row(
                Ui.Button("Aplicar estilo AUREVIX", async () =>
                {
                    var r = await Services.AurevixStyle.ApplyAsync(includeLockScreen: true);
                    _done.Add("Estilo AUREVIX aplicado.");
                    foreach (var sk in r.Skipped) _pending.Add($"Estilo: {sk} (Windows no lo permitió).");
                    if (shell.IsChecked == true) { Services.AurevixShell.SetEnabled(true, restartShell: false); _done.Add("AUREVIX Shell activado."); }
                    else Services.AurevixShell.SetEnabled(false, restartShell: false);
                    Services.AurevixStyle.RestartExplorer();
                    if (nusa.IsChecked == true)
                    {
                        if (await Services.Nusa.InstallAsync()) _done.Add("Nusa instalada.");
                        else _pending.Add("Instalar Nusa desde Microsoft Store (Apps).");
                    }
                    Summary();
                }, primary: true),
                Ui.Button("Mantener el aspecto de Windows", () => { Services.AurevixShell.SetEnabled(false, restartShell: false); Summary(); })));
    }

    // 6. Resumen
    private void Summary()
    {
        Complete();
        var done = new StackPanel();
        foreach (var d in _done.DefaultIfEmpty("Ningún cambio aplicado.")) done.Children.Add(Ui.Text("✓ " + d));
        var pending = new StackPanel();
        foreach (var p in _pending.DefaultIfEmpty("Nada pendiente.")) pending.Children.Add(Ui.Muted("• " + p));
        Show(7, "Resumen", Ui.H1("¡Tu equipo está listo!"),
            Ui.Card(Ui.H2("Realizado"), done),
            Ui.Card(Ui.H2("Pendiente"), pending),
            Ui.Muted("AUREVIX Control Center queda disponible en el escritorio, en el menú Inicio y en la bandeja del sistema."),
            Ui.Row(Ui.Button("Abrir Control Center", () => { Close(); ((App)Application.Current).ShowMain(); }, primary: true),
                   Ui.Button("Ir al escritorio", Close)));
    }

    private void Skip()
    {
        if (MessageBox.Show("¿Omitir la configuración? Podrás hacerla después desde AUREVIX Control Center.", "AUREVIX Setup",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Complete();
        Close();
    }

    private static void Complete()
    {
        AppServices.State.FirstRunCompleted = true;
        AppServices.State.FirstRunCompletedAtUtc = DateTime.UtcNow;
        AppServices.SaveState();
        AurevixLog.Info("Asistente de primer inicio completado.");
    }
}
