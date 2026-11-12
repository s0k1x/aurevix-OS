using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using Aurevix.ControlCenter.Views;
using Aurevix.Core.Logging;
using Aurevix.Installer.Controls;
using Aurevix.Installer.Engine;

namespace Aurevix.Installer;

/// <summary>
/// AUREVIX Installer — tres opciones:
///   1. Instalar AUREVIX en este Windows (sin borrar nada).
///   2. Reinstalar este PC limpio con AUREVIX (Windows Setup oficial, sin USB).
///   3. Crear un USB de instalación (detecta el pendrive al enchufarlo) o una ISO.
/// Cada operación muestra porcentaje, tiempo restante aproximado y los pasos que van quedando.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly string AppVersion = (System.Reflection.CustomAttributeExtensions
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainWindow).Assembly)?.InformationalVersion ?? "").Split('+')[0];

    private readonly MediaEngine _engine = new();
    private readonly ContentControl _host = new();
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _usbTimer;
    private Action? _prepDetach, _prepNext;
    private static readonly bool Screenshots = Environment.GetCommandLineArgs().Any(a => a.Equals("--render-screenshots", StringComparison.OrdinalIgnoreCase));

    public MainWindow()
    {
        Title = "AUREVIX Installer";
        Width = 1000; Height = 720; MinWidth = 780; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush("Bg");
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/aurevix.ico"));

        var footer = new TextBlock
        {
            Text = $"AUREVIX Installer {AppVersion}  ·  Windows se descarga de Microsoft y se activa con tu licencia",
            Style = Ui.Res<Style>("Muted"), FontSize = 12, Margin = new Thickness(0, 6, 0, 16), HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.NoWrap
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new Ellipse
        {
            Width = 900, Height = 900, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -520, -380, 0), IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(0x22, 0x8D, 0xE0, 0x3C), Color.FromArgb(0x00, 0x8D, 0xE0, 0x3C))
        });
        Grid.SetRow(footer, 1);
        root.Children.Add(_host);
        root.Children.Add(footer);
        Content = root;

        Closing += (_, e) =>
        {
            if (_cts is not null && MessageBox.Show("Hay una operación en curso. ¿Cancelarla y salir?", "AUREVIX Installer", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                e.Cancel = true;
            else
            {
                _cts?.Cancel();
                IsoPreparer.Instance.Shutdown(); // deja guardado el avance de la descarga para continuar la próxima vez
            }
        };
        Home();
    }

    // ------------------------------------------------------------------ Navegación

    private void Present(params UIElement[] content)
    {
        _usbTimer?.Stop();
        _prepDetach?.Invoke(); // la tarjeta de preparación de la pantalla anterior deja de escuchar
        _prepDetach = _prepNext;
        _prepNext = null;
        var column = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(40, 32, 40, 20) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 30) };
        brand.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/aurevix.ico")), Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(new TextBlock { Text = "AUREVIX", FontWeight = FontWeights.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = "  Installer", Foreground = Ui.Brush("TextMuted"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        column.Children.Add(brand);
        foreach (var c in content) column.Children.Add(c);
        _host.Content = new ScrollViewer { Content = column };
        Ui.Stagger(column);
    }

    private static T Spaced<T>(T el, double bottom) where T : FrameworkElement { el.Margin = new Thickness(0, 0, 0, bottom); return el; }

    private static TextBlock Heading(string text) { var t = Ui.H1(text); t.FontSize = 38; t.Margin = new Thickness(0, 0, 0, 10); return t; }

    private void Home()
    {
        Present(
        Ui.Eyebrow("BIENVENIDO"),
        Heading("¿Qué quieres hacer?"),
        Spaced(Ui.Muted("AUREVIX OS es Windows 11 oficial con una capa propia: diagnóstico, perfiles, estilo minimalista y actualizaciones firmadas."), 22),
        Kit.OptionCard("\uE896", "SIN BORRAR NADA  ·  UNOS 2 MIN", "Instalar AUREVIX en este Windows",
            "Añade AUREVIX Control Center y el asistente de bienvenida. Tus archivos y programas siguen igual.", InstallHere),
        Kit.OptionCard("\uE777", "INSTALACIÓN LIMPIA  ·  UNOS 45-90 MIN", "Reinstalar este PC con AUREVIX OS",
            "Descarga Windows 11 oficial de Microsoft, reinstala Windows desde cero y deja AUREVIX listo. Borra programas y archivos de la unidad del sistema.", CleanInstallIntro),
        Kit.OptionCard("\uE88E", "PARA OTRO PC O PARA GUARDAR  ·  UNOS 15-40 MIN", "Crear un USB o una ISO de AUREVIX OS",
            "Enchufa un pendrive de 8 GB o más y AUREVIX monta dentro Windows 11 oficial con todo preparado. También puedes crear solo la ISO.", () => MediaIntro()),
        PreparedCard());
        if (!Screenshots) IsoPreparer.Instance.Start(); // preparación automática en segundo plano (si hay sitio y no está desactivada)
    }

    /// <summary>Tarjeta pequeña con el estado de la ISO que se prepara en segundo plano, y la casilla para desactivarla.</summary>
    private UIElement PreparedCard()
    {
        var prep = IsoPreparer.Instance;
        var title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("Text") };
        var detail = Ui.Muted(""); detail.FontSize = 12; detail.Margin = new Thickness(0, 2, 0, 0); detail.TextTrimming = TextTrimming.CharacterEllipsis;
        var bar = new ThinProgress { Margin = new Thickness(0, 10, 0, 0) };
        var action = Ui.Button("Pausar", () => { });
        action.Margin = new Thickness(0, 10, 0, 0); action.HorizontalAlignment = HorizontalAlignment.Left;
        Action? onAction = null;
        action.Click += (_, _) => onAction?.Invoke();
        var auto = new CheckBox { Content = "Preparar la ISO automáticamente", IsChecked = prep.AutoEnabled, Margin = new Thickness(0, 10, 0, 0), Foreground = Ui.Brush("TextMuted") };
        auto.Click += (_, _) => prep.AutoEnabled = auto.IsChecked == true;

        void Render()
        {
            var st = prep.State;
            var pct = (int)Math.Round(prep.Fraction * 100);
            title.Visibility = bar.Visibility = detail.Visibility = action.Visibility = Visibility.Visible;
            detail.Text = "";
            switch (st)
            {
                case PrepState.Running:
                    title.Text = $"Preparando la ISO de AUREVIX en segundo plano · {pct} %";
                    detail.Text = prep.Detail;
                    bar.SetValue(prep.Fraction);
                    action.Content = "Pausar"; onAction = prep.Pause;
                    break;
                case PrepState.Paused:
                    title.Text = $"Preparación de la ISO en pausa · {pct} %";
                    bar.SetValue(prep.Fraction);
                    action.Content = "Continuar"; onAction = prep.Resume;
                    break;
                case PrepState.Ready:
                    title.Text = "ISO lista";
                    detail.Text = "Crear la ISO será casi inmediato.";
                    bar.Visibility = action.Visibility = Visibility.Collapsed;
                    break;
                case PrepState.NoSpace:
                    title.Text = "No se prepara la ISO en segundo plano";
                    detail.Text = prep.Detail;
                    bar.Visibility = action.Visibility = Visibility.Collapsed;
                    break;
                case PrepState.Failed:
                    title.Text = "No se pudo preparar la ISO en segundo plano";
                    detail.Text = prep.Detail;
                    bar.Visibility = Visibility.Collapsed;
                    action.Content = "Reintentar"; onAction = prep.Retry;
                    break;
                default:
                    title.Visibility = bar.Visibility = detail.Visibility = action.Visibility = Visibility.Collapsed;
                    break;
            }
        }
        Render();

        var dispatcher = Dispatcher;
        var queued = 0;
        void OnChanged()
        {
            if (Interlocked.Exchange(ref queued, 1) == 1) return; // como mucho una actualización pendiente
            _ = dispatcher.InvokeAsync(() => { Interlocked.Exchange(ref queued, 0); Render(); });
        }
        prep.Changed += OnChanged;
        _prepNext = () => prep.Changed -= OnChanged;

        var card = Ui.Card(title, detail, bar, action, auto);
        card.Margin = new Thickness(0, 6, 0, 0);
        return card;
    }

    // ------------------------------------------------------------------ Progreso común

    private ProgressView Progress(string title, string note, params StageSpec[] stages)
    {
        var view = new ProgressView(stages);
        Present(Ui.Eyebrow("EN CURSO"), Heading(title), Spaced(Ui.Muted(note), 34), view.Root,
            Ui.Row(Ui.Button("Cancelar", () =>
            {
                if (_cts is not null && MessageBox.Show("¿Cancelar la operación? Podrás volver a empezar cuando quieras.", "AUREVIX Installer", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    _cts.Cancel();
            }), Ui.Button("Ver registro", () => OpenFolder(App.LogDir))));
        return view;
    }

    private async Task RunAsync(Func<CancellationToken, Task> work, Action onDone, bool waitForPreparer = false)
    {
        _cts = new CancellationTokenSource();
        try
        {
            // La preparación en segundo plano usa las mismas carpetas de trabajo: se detiene (y continúa luego), salvo
            // que esta operación sea «Crear ISO», que espera a que termine para no duplicar trabajo.
            if (!waitForPreparer) await IsoPreparer.Instance.SuspendAsync();
            await work(_cts.Token);
            _cts = null;
            onDone();
        }
        catch (OperationCanceledException)
        {
            _cts = null;
            Done("Cancelado", "No se ha hecho ningún cambio más. Puedes volver a empezar cuando quieras.", ok: false);
        }
        catch (MicrosoftBlockedException ex)
        {
            _cts = null;
            AurevixLog.Warn(ex.Detail);
            Present(Kit.ResultBadge(false), Heading("Microsoft no ha permitido la descarga"),
                Spaced(Ui.Muted(ex.Message + " Puedes esperar un rato y reintentar, o descargar la ISO tú mismo desde la web de Microsoft " +
                                "y elegirla con «Usar mi propia ISO»."), 24),
                Ui.Row(Ui.Button("Abrir la web de Microsoft", () => OpenUrl("https://www.microsoft.com/software-download/windows11"), primary: true),
                       Ui.Button("Volver al inicio", Home)));
        }
        catch (Exception ex)
        {
            _cts = null;
            AurevixLog.Error("Operación fallida", ex);
            Fail("Algo ha fallado", ex.Message);
        }
    }

    private void Done(string title, string text, bool ok, params UIElement[] extra)
    {
        var items = new List<UIElement> { Kit.ResultBadge(ok), Heading(title), Spaced(Ui.Muted(text), 26) };
        items.AddRange(extra);
        items.Add(Ui.Row(Ui.Button("Volver al inicio", Home, primary: extra.Length == 0), Ui.Button("Ver registro", () => OpenFolder(App.LogDir))));
        Present(items.ToArray());
    }

    private void Fail(string title, string text)
    {
        Present(Kit.ResultBadge(false), Heading(title), Spaced(Ui.Muted(text), 26),
            Ui.Row(Ui.Button("Volver al inicio", Home, primary: true),
                   Ui.Button("Copiar detalles", () =>
                   {
                       try { Clipboard.SetText($"{title}\n{text}\n\n--- registro ---\n{LogTail()}"); } catch { }
                   }),
                   Ui.Button("Ver registro", () => OpenFolder(App.LogDir))));
    }

    private static string LogTail()
    {
        try
        {
            var file = new DirectoryInfo(App.LogDir).EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (file is null) return "";
            using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(fs);
            return string.Join("\n", r.ReadToEnd().Split('\n').TakeLast(60));
        }
        catch { return ""; }
    }

    private UIElement CleanupRow()
    {
        var size = MediaEngine.CacheBytes();
        if (size < 500_000_000) return new Border { Height = 0 };
        var row = Ui.Row(Ui.Button($"Liberar espacio · borrar la ISO de Windows descargada ({Eta.Bytes(size)})", () =>
        {
            MediaEngine.ClearCache();
            Home();
        }));
        row.Margin = new Thickness(0, 0, 0, 14);
        return row;
    }

    // ------------------------------------------------------------------ 1. Instalar aquí

    private void InstallHere()
    {
        var v = Progress("Instalando AUREVIX", "Descargamos la última versión verificada y la instalamos en este Windows.",
            new StageSpec("Descargar AUREVIX", 15), new StageSpec("Instalar en este Windows", 60));
        _ = RunAsync(async ct =>
        {
            v.SetStage(0);
            var setup = await _engine.DownloadAurevixAsync(v.Reporter, ct);
            v.SetStage(1);
            v.Reporter.Report(new Step("Abriendo el instalador de AUREVIX"));
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup) { UseShellExecute = true });
            if (p is not null) await p.WaitForExitAsync(ct);
            v.Complete();
        }, () => Done("AUREVIX instalado", "Se abrirá el asistente de bienvenida. Puedes cerrar esta ventana.", ok: true,
            Ui.Row(Ui.Button("Cerrar", Close, primary: true))));
    }

    // ------------------------------------------------------------------ 2. Reinstalación limpia

    private void CleanInstallIntro()
    {
        var blockers = 0;
        var checkDone = false;
        var confirm = new TextBox
        {
            FontSize = 16, Padding = new Thickness(14, 9, 14, 9), Width = 260, HorizontalAlignment = HorizontalAlignment.Left,
            Background = Ui.Brush("SurfaceAlt"), Foreground = Ui.Brush("Text"), BorderBrush = Ui.Brush("BorderBrushAx"), CaretBrush = Ui.Brush("Text")
        };
        var go = Ui.Button("Reinstalar ahora", () => _ = CleanInstall());
        go.Style = Ui.Res<Style>("Danger");
        go.IsEnabled = false;
        void UpdateGo() => go.IsEnabled = checkDone && blockers == 0 && confirm.Text.Trim() == "BORRAR";
        confirm.TextChanged += (_, _) => UpdateGo();

        var issues = new StackPanel { Margin = new Thickness(0, 0, 0, 14), Children = { Ui.Muted("Comprobando que tu equipo está listo…") } };

        Present(Ui.Eyebrow("INSTALACIÓN LIMPIA  ·  UNOS 45-90 MIN"),
            Heading("Reinstalar este PC con AUREVIX OS"),
            Spaced(Ui.Muted("Se descargará Windows 11 oficial de Microsoft (unos 7 GB) y se reinstalará con el instalador oficial de Windows. " +
                            "El equipo se reiniciará solo varias veces. Al terminar verás el asistente de AUREVIX."), 24),
            Ui.Card(Ui.H2("Se borrará"),
                Ui.Muted("•  Todos los programas instalados"),
                Ui.Muted("•  Tus archivos de la unidad del sistema (Escritorio, Documentos, Descargas…)"),
                Ui.Muted("•  Los ajustes de Windows"),
                Spaced(Ui.Muted("Haz una copia de seguridad antes (OneDrive, un disco externo…). Otras unidades (D:, E:…) no se tocan."), 0)),
            issues,
            Spaced(Ui.Text("Para confirmar escribe BORRAR:"), 8),
            confirm,
            Ui.Row(go, Ui.Button("Volver", Home)));
        confirm.Focus();

        // La comprobación (espacio, cargador, BitLocker) tarda unos segundos: la pantalla ya se ve mientras tanto.
        _ = Task.Run(async () =>
        {
            MediaEngine.Precheck check;
            try { check = await MediaEngine.CheckCleanInstallAsync(); }
            catch (Exception ex)
            {
                AurevixLog.Error("Comprobación previa", ex);
                check = new MediaEngine.Precheck(["No se pudo comprobar tu equipo. Mira el registro o inténtalo de nuevo."], []);
            }
            await Dispatcher.InvokeAsync(() =>
            {
                issues.Children.Clear();
                foreach (var b in check.Blockers) issues.Children.Add(Spaced(Ui.Text("✕  " + b), 6));
                foreach (var w in check.Warnings) issues.Children.Add(Spaced(Ui.Muted("⚠  " + w), 6));
                if (check.Blockers.Count == 0 && check.Warnings.Count == 0) issues.Children.Add(Ui.Muted("✓  Tu equipo está listo: hay espacio y corriente."));
                blockers = check.Blockers.Count;
                checkDone = true;
                UpdateGo();
            });
        });
    }

    private Task CleanInstall()
    {
        var v = Progress("Preparando la reinstalación", "No apagues el equipo. Cuando todo esté listo, Windows Setup tomará el control.",
            new StageSpec("Descargar AUREVIX", 15), new StageSpec("Obtener Windows 11", 600),
            new StageSpec("Preparar la instalación", 20), new StageSpec("Iniciar Windows Setup", 30));
        return RunAsync(async ct =>
        {
            MediaEngine.RequireFreeSpace(MediaEngine.WorkRoot, _engine.IsoCached ? 1_000_000_000 : MediaEngine.WindowsIsoBytes + 1_000_000_000, "La descarga de Windows 11");
            v.SetStage(0);
            var setup = await _engine.DownloadAurevixAsync(v.Reporter, ct);
            v.SetStage(1);
            var iso = await _engine.DownloadWindowsIsoAsync(v.Reporter, ct);
            v.SetStage(2);
            v.Reporter.Report(new Step("Montando la ISO de Windows"));
            var root = await _engine.MountAsync(iso, ct);
            v.SetStage(3);
            await _engine.StartCleanInstallAsync(root, setup, v.Reporter, ct);
            v.Complete();
        }, () => Done("Windows Setup en marcha", "Sigue las indicaciones de Windows Setup. El equipo se reiniciará y, al terminar, se abrirá AUREVIX.", ok: true));
    }

    // ------------------------------------------------------------------ 3. USB / ISO

    private void MediaIntro(bool isoSelected = false)
    {
        var kind = new ChoiceGroup();   // 0 = USB, 1 = ISO
        var src = new ChoiceGroup();    // 0 = descargar, 1 = ISO propia
        var disks = new ChoiceGroup();
        var diskList = Array.Empty<UsbDisk>();
        string? ownIso = null;

        var summary = Ui.Muted("");
        var create = Ui.Button("Crear USB", () => { }, primary: true);

        // Dónde se guarda la ISO: Descargas por defecto; «Cambiar…» abre el diálogo de Windows.
        var isoOutput = DefaultIsoPath();
        Action? refresh = null; // se asigna a Update() más abajo
        var destPath = new TextBlock { Text = isoOutput, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Brush("Text") };
        var destFree = Ui.Muted("");
        void UpdateDest()
        {
            destPath.Text = isoOutput;
            var free = MediaEngine.FreeBytes(Path.GetDirectoryName(isoOutput)!);
            var enough = free >= MediaEngine.WindowsIsoBytes + 1_000_000_000;
            destFree.Text = free < 0 ? "No se pudo leer el espacio libre de esa unidad." :
                $"{Eta.Bytes(free)} libres en {Path.GetPathRoot(isoOutput)}" + (enough ? " · suficiente" : " · hacen falta unos 9 GB: elige otra unidad");
            destFree.Foreground = Ui.Brush(enough ? "TextMuted" : "Attention");
        }
        var destPanel = new StackPanel
        {
            Children =
            {
                Spaced(Ui.Eyebrow("GUARDAR LA ISO EN"), 8),
                new Border
                {
                    Padding = new Thickness(18, 14, 14, 14), CornerRadius = new CornerRadius(14), BorderBrush = Ui.Brush("BorderBrushAx"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10),
                    Child = Ui.Columns(new StackPanel { Children = { destPath, destFree } }, Ui.Button("Cambiar…", () =>
                    {
                        var dlg = new Microsoft.Win32.SaveFileDialog
                        {
                            Title = "Dónde guardar la ISO de AUREVIX OS", Filter = "Imagen ISO|*.iso", DefaultExt = ".iso",
                            FileName = Path.GetFileName(isoOutput), InitialDirectory = Path.GetDirectoryName(isoOutput), OverwritePrompt = true
                        };
                        if (dlg.ShowDialog(this) == true) { isoOutput = dlg.FileName; UpdateDest(); refresh?.Invoke(); }
                    }))
                }
            }
        };
        UpdateDest();

        var kindGrid = new UniformGrid { Columns = 2 };
        var tUsb = kind.Add("USB de instalación", "Para instalar en otro PC. Listo para arrancar.");
        var tIso = kind.Add("Solo la ISO", "Un archivo para guardar o usar en una máquina virtual.");
        tUsb.Margin = new Thickness(0, 0, 6, 10); tIso.Margin = new Thickness(6, 0, 0, 10);
        kindGrid.Children.Add(tUsb); kindGrid.Children.Add(tIso);

        var usbList = new StackPanel();
        var pulse = new Ellipse { Width = 8, Height = 8, Fill = Ui.Brush("Accent"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        if (Ui.AnimationsEnabled)
            pulse.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        var empty = new Border
        {
            Padding = new Thickness(18, 16, 18, 16), CornerRadius = new CornerRadius(14), BorderBrush = Ui.Brush("BorderBrushAx"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { pulse, new TextBlock { Text = "Enchufa un pendrive de 8 GB o más: lo detectamos solos.", Foreground = Ui.Brush("TextMuted") } } }
        };
        var usbPanel = new StackPanel { Children = { Spaced(Ui.Eyebrow("PENDRIVE"), 8), empty, usbList } };

        var tAuto = src.Add("Descargar de Microsoft", "Windows 11 oficial en español. Si ya lo descargaste antes, se reutiliza.", _engine.IsoCached ? "Ya descargado" : null);
        var tOwn = src.Add("Usar mi propia ISO", "Elige una ISO oficial de Windows 11 que ya tengas.");

        void Update()
        {
            var usb = kind.Selected == 0;
            usbPanel.Visibility = usb ? Visibility.Visible : Visibility.Collapsed;
            destPanel.Visibility = usb ? Visibility.Collapsed : Visibility.Visible;
            create.Content = usb ? "Crear USB" : "Crear ISO";
            var haveIso = src.Selected == 1 || _engine.IsoCached;
            var time = (usb, haveIso) switch
            {
                (true, true) => "unos 10-15 min",
                (true, false) => "entre 20 y 45 min, según tu conexión",
                (false, true) => "unos 5-10 min",
                _ => "entre 15 y 35 min, según tu conexión"
            };
            summary.Text = $"Tiempo aproximado: {time}." + (haveIso ? "" : " Descarga unos 7 GB de Windows desde Microsoft.") +
                           (usb ? " Se borrará el contenido del pendrive." : $" Necesita unos 9 GB libres en {Path.GetPathRoot(isoOutput)}.");
            create.IsEnabled = !usb || (disks.Selected >= 0 && disks.Selected < diskList.Length);
        }
        refresh = Update;
        kind.Changed += _ => Update();
        disks.Changed += _ => Update();
        src.Changed += i =>
        {
            if (i == 1)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Imagen ISO|*.iso", Title = "ISO oficial de Windows 11" };
                if (dlg.ShowDialog(this) == true) { ownIso = dlg.FileName; Kit.SetTileText(tOwn, Path.GetFileName(ownIso)); }
                else { src.Select(0, raise: false); }
            }
            Update();
        };
        kind.Select(isoSelected ? 1 : 0, raise: false);
        src.Select(0, raise: false);

        create.Click += (_, _) =>
        {
            var iso = src.Selected == 1 ? ownIso : null;
            if (kind.Selected == 0)
            {
                if (disks.Selected < 0 || disks.Selected >= diskList.Length) return;
                var d = diskList[disks.Selected];
                if (d.SizeBytes < 8_000_000_000) { MessageBox.Show("Ese pendrive es demasiado pequeño: hacen falta 8 GB como mínimo.", "AUREVIX Installer"); return; }
                if (MessageBox.Show($"Se BORRARÁ todo el contenido de:\n\n{d.Display}\n\nComprueba que es el pendrive correcto. Esta acción no se puede deshacer.\n\n¿Continuar?",
                        "Crear USB", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                _ = CreateUsb(d, iso);
            }
            else _ = CreateIso(iso, isoOutput);
        };

        Present(Ui.Eyebrow("USB O ISO"),
            Heading("Crear AUREVIX OS"),
            Spaced(Ui.Muted("Instalación en español casi automática: solo elegirás el disco, aceptarás la licencia de Microsoft y crearás tu cuenta."), 26),
            Spaced(Ui.Eyebrow("QUÉ QUIERES CREAR"), 8), kindGrid,
            usbPanel, destPanel,
            Spaced(Ui.Eyebrow("WINDOWS 11"), 8), tAuto, tOwn,
            Spaced(summary, 14),
            Ui.Row(create, Ui.Button("Volver", Home)));
        Update();

        // Detección en vivo de pendrives (cada 3 s); solo se redibuja si cambia algo.
        async Task RefreshAsync()
        {
            var list = (await _engine.ListUsbDisksAsync()).ToArray();
            if (list.Select(d => $"{d.Number}{d.Letters}{d.SizeBytes}").SequenceEqual(diskList.Select(d => $"{d.Number}{d.Letters}{d.SizeBytes}"))) return;
            var keep = disks.Selected >= 0 && disks.Selected < diskList.Length ? diskList[disks.Selected].Number : (int?)null;
            diskList = list;
            usbList.Children.Clear();
            disks.Clear();
            foreach (var d in diskList)
                usbList.Children.Add(disks.Add(d.Name, $"{d.SizeBytes / 1e9:0.#} GB" + (d.Letters.Length > 0 ? $"  ·  unidad {d.Letters}" : "") + (d.SizeBytes < 8_000_000_000 ? "  ·  demasiado pequeño (mínimo 8 GB)" : "")));
            empty.Visibility = diskList.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var idx = keep is null ? (diskList.Length == 1 ? 0 : -1) : Array.FindIndex(diskList, d => d.Number == keep);
            if (idx >= 0) disks.Select(idx, raise: false);
            Update();
        }
        _ = RefreshAsync();
        _usbTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _usbTimer.Tick += async (_, _) => await RefreshAsync();
        _usbTimer.Start();
    }

    private async Task<string> GetIsoAsync(string? ownIso, IProgress<Step> rep, CancellationToken ct)
    {
        if (ownIso is null) return await _engine.DownloadWindowsIsoAsync(rep, ct);
        rep.Report(new Step("Usando tu ISO de Windows", 1, Path.GetFileName(ownIso)));
        return ownIso;
    }

    /// <summary>Progreso que descarta avisos hasta que se «abre» (así los de Windows no pisan la etapa de AUREVIX).</summary>
    private sealed class GatedProgress(IProgress<Step> inner) : IProgress<Step>
    {
        public volatile bool Open;
        public void Report(Step value) { if (Open) inner.Report(value); }
    }

    /// <summary>
    /// Descarga AUREVIX y Windows a la vez. La etapa 0 se da por hecha en cuanto termina AUREVIX; después se muestra
    /// el avance de Windows (etapa 1). Si una de las dos falla, se cancela la otra. <paramref name="knownSetup"/> evita repetir AUREVIX.
    /// </summary>
    private async Task<(string setup, string iso)> DownloadBothAsync(string? ownIso, ProgressView v, CancellationToken ct, string? knownSetup = null)
    {
        v.SetStage(knownSetup is null ? 0 : 1);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var gate = new GatedProgress(v.Reporter) { Open = knownSetup is not null };
        var setupTask = knownSetup is not null ? Task.FromResult(knownSetup) : _engine.DownloadAurevixAsync(v.Reporter, linked.Token);
        var isoTask = GetIsoAsync(ownIso, gate, linked.Token);
        try
        {
            var first = await Task.WhenAny(setupTask, isoTask);
            if (first == isoTask) await isoTask; // si Windows falla antes, se avisa enseguida
            var setup = await setupTask;
            if (knownSetup is null) { v.SetStage(1); gate.Open = true; }
            return (setup, await isoTask);
        }
        catch
        {
            linked.Cancel();
            try { await Task.WhenAll(setupTask, isoTask); } catch { }
            throw;
        }
    }

    private Task CreateUsb(UsbDisk disk, string? ownIso)
    {
        var v = Progress("Creando el USB de AUREVIX OS", "No desenchufes el pendrive ni apagues el equipo hasta que termine.",
            new StageSpec("Descargar AUREVIX", 15),
            new StageSpec(ownIso is null ? "Obtener Windows 11" : "Usar tu ISO de Windows", ownIso is null && !_engine.IsoCached ? 600 : 10),
            new StageSpec("Preparar la ISO", 15), new StageSpec("Crear y comprobar el USB", 480));
        return RunAsync(async ct =>
        {
            if (ownIso is null) MediaEngine.RequireFreeSpace(MediaEngine.WorkRoot, _engine.IsoCached ? 500_000_000 : MediaEngine.WindowsIsoBytes + 500_000_000, "La descarga de Windows 11");
            var (setup, iso) = await DownloadBothAsync(ownIso, v, ct);
            v.SetStage(2);
            v.Reporter.Report(new Step("Montando la ISO de Windows"));
            var root = await _engine.MountAsync(iso, ct);
            try
            {
                v.SetStage(3);
                await _engine.WriteUsbAsync(disk, root, setup, v.Reporter, ct);
            }
            finally { await _engine.DismountAsync(iso); }
            v.Complete();
        }, () => Done("USB listo", "Comprobado: tiene los archivos de arranque, Windows completo y AUREVIX. Enchúfalo en el PC donde quieras instalar AUREVIX OS, " +
                                   "arranca desde el USB (F12, F11 o Esc al encender, según la marca) y sigue los pasos.", ok: true, CleanupRow()));
    }

    private static string DefaultIsoPath()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var output = Path.Combine(downloads, $"AUREVIX_OS_{DateTime.Now:yyyyMMdd}.iso");
        return File.Exists(output) ? Path.Combine(downloads, $"AUREVIX_OS_{DateTime.Now:yyyyMMdd_HHmm}.iso") : output;
    }

    private Task CreateIso(string? ownIso, string output)
    {
        var downloads = Path.GetDirectoryName(output)!;
        string? hash = null;
        var usedPrepared = false;
        var v = Progress("Creando la ISO de AUREVIX OS", $"Se guardará en {output}",
            new StageSpec("Descargar AUREVIX", 15),
            new StageSpec(ownIso is null ? "Obtener Windows 11" : "Usar tu ISO de Windows", ownIso is null && !_engine.IsoCached ? 600 : 10),
            new StageSpec("Preparar la ISO", 15), new StageSpec("Crear y verificar la ISO", 300));
        return RunAsync(async ct =>
        {
            Directory.CreateDirectory(downloads);
            MediaEngine.RequireFreeSpace(downloads, MediaEngine.WindowsIsoBytes + 1_000_000_000, "La ISO resultante");
            string? knownSetup = null;
            var prep = IsoPreparer.Instance;
            if (ownIso is null && (prep.IsBusy || IsoPreparer.HasPrepared))
            {
                // Hay una ISO preparada (o en preparación): se necesita la versión actual de AUREVIX para saber si sirve.
                v.SetStage(0);
                knownSetup = await _engine.DownloadAurevixAsync(v.Reporter, ct);
                if (prep.IsBusy)
                {
                    v.SetStage(1);
                    await prep.WaitAsync(v.Reporter, ct);
                }
                if (IsoPreparer.IsReadyFor(_engine.AurevixVersion))
                {
                    v.SetStage(3);
                    v.Reporter.Report(new Step("Usando la ISO ya preparada", 0));
                    var sameDrive = string.Equals(Path.GetPathRoot(Path.GetFullPath(IsoPreparer.IsoPath)), Path.GetPathRoot(Path.GetFullPath(output)), StringComparison.OrdinalIgnoreCase);
                    var move = sameDrive && MessageBox.Show(
                        "La ISO ya preparada está en la misma unidad que el destino.\n\n¿Quieres moverla (es instantáneo y no ocupa el doble)? " +
                        "Si la mueves, AUREVIX tendrá que prepararla de nuevo otra vez.\n\nSí = mover   ·   No = copiar", "AUREVIX Installer",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                    hash = await prep.DeliverAsync(output, move, v.Reporter, ct);
                    usedPrepared = true;
                    v.Complete();
                    return;
                }
            }
            if (ownIso is null) MediaEngine.RequireFreeSpace(MediaEngine.WorkRoot, _engine.IsoCached ? 500_000_000 : MediaEngine.WindowsIsoBytes + 500_000_000, "La descarga de Windows 11");
            var (setup, iso) = await DownloadBothAsync(ownIso, v, ct, knownSetup);
            v.SetStage(2);
            v.Reporter.Report(new Step("Montando la ISO de Windows"));
            var root = await _engine.MountAsync(iso, ct);
            try
            {
                v.SetStage(3);
                hash = await _engine.BuildIsoAsync(root, setup, output, v.Reporter, ct);
            }
            finally { await _engine.DismountAsync(iso); }
            v.Complete();
        }, waitForPreparer: ownIso is null, onDone: () => Done("ISO lista", (usedPrepared ? "Usando la ISO ya preparada: no hizo falta volver a construirla. " : "") +
            "Es para tu uso: no la compartas, la licencia de Windows no lo permite.", ok: true,
            Ui.Card(Ui.Muted("Archivo"), Ui.Text(output), Spaced(Ui.Muted("SHA-256 (también guardado en un .sha256 junto a la ISO)"), 0), Ui.Text(hash ?? "", 12)),
            Ui.Row(Ui.Button("Abrir carpeta", () => OpenFolder(downloads), primary: true)),
            CleanupRow()));
    }

    // ------------------------------------------------------------------ Capturas (workflow de GitHub Actions)

    public async Task RenderScreenshotsAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        Ui.ReduceMotion = true;
        Home(); await Task.Delay(1200); Save(Path.Combine(dir, "inst-01-inicio.png"));
        MediaIntro(); await Task.Delay(5000); Save(Path.Combine(dir, "inst-02-usb-iso.png"));
        MediaIntro(isoSelected: true); await Task.Delay(3000); Save(Path.Combine(dir, "inst-02b-iso-destino.png"));
        CleanInstallIntro(); await Task.Delay(9000); Save(Path.Combine(dir, "inst-03-reinstalar.png"));

        var v = Progress("Creando el USB de AUREVIX OS", "No desenchufes el pendrive ni apagues el equipo hasta que termine.",
            new StageSpec("Descargar AUREVIX", 15), new StageSpec("Obtener Windows 11", 600),
            new StageSpec("Preparar la ISO", 15), new StageSpec("Crear y comprobar el USB", 480));
        v.SetStage(0); v.Reporter.Report(new Step("Descargando AUREVIX", 1)); v.SetStage(1);
        for (var i = 0; i < 26; i++)
        {
            var f = 0.40 + i * 0.004;
            v.Reporter.Report(new Step("Descargando Windows 11 de Microsoft", f, $"{f * 6.9:0.0} de 6,9 GB · 38 MB/s"));
            await Task.Delay(250);
        }
        await Task.Delay(300); Save(Path.Combine(dir, "inst-04-progreso.png"));
        _usbTimer?.Stop();

        Done("ISO lista", "Es para tu uso: no la compartas, la licencia de Windows no lo permite.", ok: true,
            Ui.Card(Ui.Muted("Archivo"), Ui.Text(@"C:\Users\Tu\Downloads\AUREVIX_OS_20261106.iso"), Spaced(Ui.Muted("SHA-256"), 0), Ui.Text("3f9a…c21d", 12)),
            Ui.Row(Ui.Button("Abrir carpeta", () => { }, primary: true)));
        await Task.Delay(900); Save(Path.Combine(dir, "inst-05-listo.png"));

        Fail("Algo ha fallado", "Falta espacio en C:\\: La descarga de Windows 11 necesita unos 9 GB libres y hay 4,2 GB. Libera espacio y vuelve a intentarlo.");
        await Task.Delay(900); Save(Path.Combine(dir, "inst-06-error.png"));
    }

    private void Save(string path)
    {
        UpdateLayout();
        var el = (FrameworkElement)Content;
        int w = (int)Math.Ceiling(el.ActualWidth), h = (int)Math.Ceiling(el.ActualHeight);
        const double dpi = 144;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w * 1.5), (int)(h * 1.5), dpi, dpi, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            ctx.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        }
        rtb.Render(dv);
        rtb.Render(el);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    // ------------------------------------------------------------------ Utilidades

    private static void OpenFolder(string dir) { Directory.CreateDirectory(dir); OpenUrl(dir); }
    private static void OpenUrl(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}
