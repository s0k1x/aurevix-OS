using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Aurevix.ControlCenter.Views;
using Aurevix.Core.Logging;
using Aurevix.Installer.Engine;

namespace Aurevix.Installer;

/// <summary>
/// AUREVIX Installer — tres opciones:
///   1. Instalar AUREVIX en este Windows (sin borrar nada).
///   2. Reinstalar este PC limpio con AUREVIX (Windows Setup oficial, sin USB).
///   3. Crear un USB de instalación (detecta el pendrive al enchufarlo) o una ISO.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly MediaEngine _engine = new();
    private readonly ContentControl _host = new();
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _usbTimer;

    public MainWindow()
    {
        Title = "AUREVIX Installer";
        Width = 960; Height = 680; MinWidth = 760; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush("Bg");
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/aurevix.ico"));
        Content = _host;
        Closing += (_, e) =>
        {
            if (_cts is not null && MessageBox.Show("Hay una operación en curso. ¿Cancelarla y salir?", "AUREVIX Installer", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                e.Cancel = true;
            else _cts?.Cancel();
        };
        Home();
    }

    // ------------------------------------------------------------------ Navegación

    private void Show(params UIElement[] content)
    {
        _usbTimer?.Stop();
        var sp = new StackPanel { Margin = new Thickness(56, 48, 56, 40), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 36) };
        brand.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/aurevix.ico")), Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(new TextBlock { Text = "AUREVIX Installer", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(brand);
        foreach (var c in content) sp.Children.Add(c);
        _host.Content = new ScrollViewer { Content = sp };
        Ui.Stagger(sp);
    }

    private static Border Option(string eyebrow, string title, string text, Action onClick)
    {
        var card = Ui.Card(Ui.Eyebrow(eyebrow), Ui.H2(title), Ui.Muted(text));
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.Focusable = true;
        card.MouseLeftButtonUp += (_, _) => onClick();
        card.KeyUp += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) onClick(); };
        card.MouseEnter += (_, _) => card.BorderBrush = Ui.Brush("TextMuted");
        card.MouseLeave += (_, _) => card.BorderBrush = Ui.Brush("BorderBrushAx");
        return card;
    }

    private void Home() => Show(
        Ui.H1("¿Qué quieres hacer?"),
        Spaced(Ui.Muted("AUREVIX OS es Windows 11 oficial con una capa propia: diagnóstico, perfiles, estilo minimalista y actualizaciones firmadas."), 28),
        Option("SIN BORRAR NADA", "Instalar AUREVIX en este Windows",
            "Añade AUREVIX Control Center y el asistente de bienvenida. Tus archivos y programas siguen igual.", InstallHere),
        Option("INSTALACIÓN LIMPIA · SIN USB", "Reinstalar este PC con AUREVIX OS",
            "Descarga Windows 11 oficial de Microsoft, reinstala Windows desde cero y deja AUREVIX listo. Borra programas y archivos de la unidad del sistema.", CleanInstallIntro),
        Option("PARA OTRO PC O PARA GUARDAR", "Crear un USB o una ISO de AUREVIX OS",
            "Enchufa un pendrive de 8 GB o más y AUREVIX monta dentro Windows 11 oficial con todo preparado. También puedes crear solo la ISO.", MediaIntro),
        Spaced(Ui.Muted("Windows se descarga siempre de los servidores oficiales de Microsoft y se activa con tu propia licencia."), 12));

    private static T Spaced<T>(T el, double bottom) where T : FrameworkElement { el.Margin = new Thickness(0, 0, 0, bottom); return el; }

    // ------------------------------------------------------------------ Progreso común

    private (TextBlock text, ProgressBar bar) ProgressPage(string title, string note)
    {
        var text = Ui.Text("Preparando…");
        var bar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 18, 0, 10) };
        var cancel = Ui.Button("Cancelar", () => _cts?.Cancel());
        Show(Ui.H1(title), Spaced(Ui.Muted(note), 28), text, bar,
            Ui.Row(cancel, Ui.Button("Ver registro", () => OpenFolder(App.LogDir))));
        return (text, bar);
    }

    private static IProgress<Step> Reporter(TextBlock text, ProgressBar bar) => new Progress<Step>(s =>
    {
        text.Text = s.Text;
        bar.IsIndeterminate = s.Fraction is null;
        if (s.Fraction is { } f) bar.Value = f * 100;
    });

    private async Task RunAsync(Func<CancellationToken, Task> work, Action onDone)
    {
        _cts = new CancellationTokenSource();
        try
        {
            await work(_cts.Token);
            _cts = null;
            onDone();
        }
        catch (OperationCanceledException)
        {
            _cts = null;
            Done("Cancelado", "No se ha hecho ningún cambio adicional.", ok: false);
        }
        catch (MicrosoftBlockedException ex)
        {
            _cts = null;
            AurevixLog.Warn(ex.Detail);
            Show(Ui.H1("Microsoft no ha permitido la descarga"),
                Spaced(Ui.Muted(ex.Message + " Puedes esperar un rato y reintentar, o descargar la ISO tú mismo desde la web de Microsoft " +
                                "y elegirla con «Usar mi propia ISO»."), 20),
                Ui.Row(Ui.Button("Abrir la web de Microsoft", () => OpenUrl("https://www.microsoft.com/software-download/windows11"), primary: true),
                       Ui.Button("Volver", Home)));
        }
        catch (Exception ex)
        {
            _cts = null;
            AurevixLog.Error("Operación fallida", ex);
            Done("Algo ha fallado", ex.Message, ok: false);
        }
    }

    private void Done(string title, string text, bool ok, params UIElement[] extra)
    {
        var items = new List<UIElement>
        {
            new TextBlock { Text = ok ? "✓" : "!", FontSize = 40, Foreground = ok ? Ui.Brush("Ok") : Ui.Brush("Review"), Margin = new Thickness(0, 0, 0, 8) },
            Ui.H1(title), Spaced(Ui.Muted(text), 24)
        };
        items.AddRange(extra);
        items.Add(Ui.Row(Ui.Button("Volver al inicio", Home), Ui.Button("Ver registro", () => OpenFolder(App.LogDir))));
        Show(items.ToArray());
    }

    // ------------------------------------------------------------------ 1. Instalar aquí

    private void InstallHere()
    {
        var (text, bar) = ProgressPage("Instalando AUREVIX", "Descargamos la última versión verificada y la instalamos en este Windows.");
        _ = RunAsync(async ct =>
        {
            var setup = await _engine.DownloadAurevixAsync(Reporter(text, bar), ct);
            text.Text = "Abriendo el instalador de AUREVIX…";
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup) { UseShellExecute = true });
            if (p is not null) await p.WaitForExitAsync(ct);
        }, () => Done("AUREVIX instalado", "Se abrirá el asistente de bienvenida. Puedes cerrar esta ventana.", ok: true,
            Ui.Row(Ui.Button("Cerrar", Close, primary: true))));
    }

    // ------------------------------------------------------------------ 2. Reinstalación limpia

    private async void CleanInstallIntro()
    {
        var check = await MediaEngine.CheckCleanInstallAsync();
        var confirm = new TextBox
        {
            FontSize = 16, Padding = new Thickness(12, 8, 12, 8), Width = 260, HorizontalAlignment = HorizontalAlignment.Left,
            Background = Ui.Brush("SurfaceAlt"), Foreground = Ui.Brush("Text"), BorderBrush = Ui.Brush("BorderBrushAx"), CaretBrush = Ui.Brush("Text")
        };
        var go = Ui.Button("Reinstalar ahora", () => _ = CleanInstall());
        go.Style = Ui.Res<Style>("Danger");
        go.IsEnabled = false;
        confirm.TextChanged += (_, _) => go.IsEnabled = confirm.Text.Trim() == "BORRAR" && check.Blockers.Count == 0;

        var issues = new StackPanel();
        foreach (var b in check.Blockers) issues.Children.Add(Ui.Text("✕ " + b));
        foreach (var w in check.Warnings) issues.Children.Add(Ui.Muted("⚠ " + w));

        Show(Ui.Eyebrow("INSTALACIÓN LIMPIA"),
            Ui.H1("Reinstalar este PC con AUREVIX OS"),
            Spaced(Ui.Muted("Se descargará Windows 11 oficial de Microsoft (unos 7 GB) y se reinstalará con el instalador oficial de Windows. " +
                            "El equipo se reiniciará solo varias veces. Al terminar verás el asistente de AUREVIX."), 20),
            Ui.Card(Ui.H2("Se borrará"),
                Ui.Muted("• Todos los programas instalados"),
                Ui.Muted("• Tus archivos de la unidad del sistema (Escritorio, Documentos, Descargas…)"),
                Ui.Muted("• Los ajustes de Windows"),
                Spaced(Ui.Muted("Haz una copia de seguridad antes (OneDrive, un disco externo…). Otras unidades (D:, E:…) no se tocan."), 0)),
            issues,
            Spaced(Ui.Text("Para confirmar escribe BORRAR:"), 8),
            confirm,
            Ui.Row(go, Ui.Button("Volver", Home)));
        confirm.Focus();
    }

    private Task CleanInstall()
    {
        var (text, bar) = ProgressPage("Preparando la reinstalación", "No apagues el equipo. Cuando todo esté listo, Windows Setup tomará el control.");
        string? iso = null;
        return RunAsync(async ct =>
        {
            var rep = Reporter(text, bar);
            var setup = await _engine.DownloadAurevixAsync(rep, ct);
            iso = await _engine.DownloadWindowsIsoAsync(rep, ct);
            rep.Report(new Step("Montando la ISO de Windows…"));
            var root = await _engine.MountAsync(iso, ct);
            await _engine.StartCleanInstallAsync(root, setup, rep, ct);
        }, () => Done("Windows Setup en marcha", "Sigue las indicaciones de Windows Setup. El equipo se reiniciará y, al terminar, se abrirá AUREVIX.", ok: true));
    }

    // ------------------------------------------------------------------ 3. USB / ISO

    private void MediaIntro()
    {
        var usbList = new StackPanel();
        var empty = Ui.Muted("Enchufa un pendrive de 8 GB o más… lo detectaremos automáticamente.");
        UsbDisk? selected = null;
        string? ownIso = null;

        var useUsb = new RadioButton { Content = "Crear un USB de instalación", IsChecked = true, GroupName = "kind", Margin = new Thickness(0, 4, 0, 4) };
        var useIso = new RadioButton { Content = "Crear solo la ISO (para grabarla después o usarla en una máquina virtual)", GroupName = "kind", Margin = new Thickness(0, 4, 0, 4) };
        var srcAuto = new RadioButton { Content = "Descargar Windows 11 oficial de Microsoft automáticamente (español)", IsChecked = true, GroupName = "src", Margin = new Thickness(0, 4, 0, 4) };
        var srcOwn = new RadioButton { Content = "Usar mi propia ISO de Windows 11…", GroupName = "src", Margin = new Thickness(0, 4, 0, 4) };
        srcOwn.Checked += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Imagen ISO|*.iso", Title = "ISO oficial de Windows 11" };
            if (dlg.ShowDialog(this) == true) { ownIso = dlg.FileName; srcOwn.Content = "Usar mi ISO: " + Path.GetFileName(ownIso); }
            else srcAuto.IsChecked = true;
        };

        var create = Ui.Button("Crear", () =>
        {
            var iso = srcOwn.IsChecked == true ? ownIso : null;
            if (useUsb.IsChecked == true)
            {
                if (selected is null) { MessageBox.Show("Elige primero un pendrive.", "AUREVIX Installer"); return; }
                if (MessageBox.Show($"Se BORRARÁ todo el contenido de:\n\n{selected.Display}\n\n¿Continuar?", "Crear USB",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                _ = CreateUsb(selected, iso);
            }
            else _ = CreateIso(iso);
        }, primary: true);

        var usbCard = Ui.Card(Ui.H2("Pendrive"), empty, usbList);
        useIso.Checked += (_, _) => usbCard.Visibility = Visibility.Collapsed;
        useUsb.Checked += (_, _) => usbCard.Visibility = Visibility.Visible;

        Show(Ui.Eyebrow("USB O ISO"),
            Ui.H1("Crear AUREVIX OS"),
            Spaced(Ui.Muted("Instalación en español casi automática: solo elegirás el disco, aceptarás la licencia de Microsoft y crearás tu cuenta."), 20),
            Ui.Card(useUsb, useIso), usbCard, Ui.Card(Ui.H2("Windows 11"), srcAuto, srcOwn),
            Ui.Row(create, Ui.Button("Volver", Home)));

        // Detección en vivo de pendrives (cada 2 s).
        async Task RefreshAsync()
        {
            var disks = await _engine.ListUsbDisksAsync();
            empty.Visibility = disks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var keep = selected is not null && disks.Any(d => d.Number == selected.Number) ? selected.Number : (int?)null;
            if (keep is null) selected = null;
            usbList.Children.Clear();
            foreach (var d in disks)
            {
                var rb = new RadioButton { Content = d.Display, GroupName = "usb", Margin = new Thickness(0, 4, 0, 4), IsChecked = d.Number == keep };
                rb.Checked += (_, _) => selected = d;
                usbList.Children.Add(rb);
            }
            if (disks.Count == 1 && selected is null) { selected = disks[0]; ((RadioButton)usbList.Children[0]).IsChecked = true; }
        }
        _ = RefreshAsync();
        _usbTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _usbTimer.Tick += async (_, _) => await RefreshAsync();
        _usbTimer.Start();
    }

    private async Task<string> GetIsoAsync(string? ownIso, IProgress<Step> rep, CancellationToken ct) =>
        ownIso ?? await _engine.DownloadWindowsIsoAsync(rep, ct);

    private Task CreateUsb(UsbDisk disk, string? ownIso)
    {
        var (text, bar) = ProgressPage("Creando el USB de AUREVIX OS", "Puede tardar entre 15 y 40 minutos según tu conexión y el pendrive. No lo desenchufes.");
        string? iso = null;
        return RunAsync(async ct =>
        {
            var rep = Reporter(text, bar);
            var setup = await _engine.DownloadAurevixAsync(rep, ct);
            iso = await GetIsoAsync(ownIso, rep, ct);
            rep.Report(new Step("Montando la ISO de Windows…"));
            var root = await _engine.MountAsync(iso, ct);
            try { await _engine.WriteUsbAsync(disk, root, setup, rep, ct); }
            finally { await _engine.DismountAsync(iso); }
        }, () => Done("USB listo", "Enchúfalo en el PC donde quieras instalar AUREVIX OS, arranca desde el USB (F12, F11 o Esc al encender, según la marca) y sigue los pasos.", ok: true));
    }

    private Task CreateIso(string? ownIso)
    {
        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", $"AUREVIX_OS_{DateTime.Now:yyyyMMdd}.iso");
        var (text, bar) = ProgressPage("Creando la ISO de AUREVIX OS", $"Se guardará en {output}.");
        return RunAsync(async ct =>
        {
            var rep = Reporter(text, bar);
            if (MediaEngine.FindOscdimg() is null)
            {
                rep.Report(new Step("Instalando la herramienta oficial de Microsoft para crear ISOs (Windows ADK)…"));
                var r = await _engine.InstallAdkAsync(ct);
                if (MediaEngine.FindOscdimg() is null)
                    throw new InvalidOperationException("No se pudo instalar Windows ADK automáticamente. Instálalo con «winget install Microsoft.WindowsADK» (Deployment Tools) y reintenta.");
            }
            var setup = await _engine.DownloadAurevixAsync(rep, ct);
            var iso = await GetIsoAsync(ownIso, rep, ct);
            rep.Report(new Step("Montando la ISO de Windows…"));
            var root = await _engine.MountAsync(iso, ct);
            try { await _engine.BuildIsoAsync(root, setup, output, rep, ct); }
            finally { await _engine.DismountAsync(iso); }
        }, () => Done("ISO lista", $"Guardada en {output} (con su .sha256). Es para tu uso: no la compartas, la licencia de Windows no lo permite.", ok: true,
            Ui.Row(Ui.Button("Abrir carpeta", () => OpenFolder(Path.GetDirectoryName(output)!), primary: true))));
    }

    // ------------------------------------------------------------------ Utilidades

    private static void OpenFolder(string dir) { Directory.CreateDirectory(dir); OpenUrl(dir); }
    private static void OpenUrl(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}
