using System.IO;
using System.Threading;
using System.Windows;
using Aurevix.ControlCenter.Services;
using Aurevix.ControlCenter.Views;
using Aurevix.Core.Logging;
using Aurevix.Update;

namespace Aurevix.ControlCenter;

/// <summary>
/// Modos de arranque:
///   (sin argumentos)  abre el Control Center.
///   --background      se queda en la bandeja; comprueba actualizaciones y muestra el asistente del primer inicio.
///   --first-run       fuerza el asistente de primer inicio.
///   --page &lt;id&gt;       abre una sección concreta (p. ej. updates).
///   --after-update    tras una actualización silenciosa: ofrece reiniciar.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\Aurevix.ControlCenter.SingleInstance";
    private const string ShowEventName = @"Local\Aurevix.ControlCenter.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private bool _updateWindowOpen;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToArray();
        var background = args.Contains("--background");

        // Instancia única: si ya está abierta, se le pide que muestre la ventana y se sale.
        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            if (!background && EventWaitHandle.TryOpenExisting(ShowEventName, out var existing)) existing.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            AurevixLog.Error("Excepción no controlada en la interfaz", ex.Exception);
            MessageBox.Show($"Se ha producido un error inesperado:\n{ex.Exception.Message}\n\nEl detalle está en los registros de AUREVIX.",
                "AUREVIX", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };

        Views.Ui.AccentSource = () => AppServices.Settings.AccentColor;
        Views.Ui.ApplyTheme();
        Views.Ui.EnableDarkTitleBars();
        AppServices.Initialize();
        Views.Ui.ReduceMotion = AppServices.Settings.ReduceMotion;

        // Modo capturas (solo lo usa el workflow de GitHub Actions).
        var deskIdx = Array.FindIndex(e.Args, a => a.Equals("--render-desktop", StringComparison.OrdinalIgnoreCase));
        if (deskIdx >= 0 && deskIdx + 1 < e.Args.Length)
        {
            var ddir = e.Args[deskIdx + 1];
            Dispatcher.InvokeAsync(async () =>
            {
                try { await Screenshotter.RenderDesktopAsync(ddir); }
                catch (Exception ex) { Directory.CreateDirectory(ddir); File.WriteAllText(System.IO.Path.Combine(ddir, "error-desktop.txt"), ex.ToString()); }
                Shutdown();
            });
            return;
        }

        var allIdx = Array.FindIndex(e.Args, a => a.Equals("--render-installed", StringComparison.OrdinalIgnoreCase));
        if (allIdx >= 0 && allIdx + 1 < e.Args.Length)
        {
            var adir = e.Args[allIdx + 1];
            Dispatcher.InvokeAsync(async () =>
            {
                try { await Screenshotter.RenderInstalledAsync(adir); }
                catch (Exception ex) { Directory.CreateDirectory(adir); File.WriteAllText(System.IO.Path.Combine(adir, "error-todo.txt"), ex.ToString()); }
                Shutdown();
            });
            return;
        }

        // Modo prueba de AUREVIX Shell (solo lo usa el workflow de GitHub Actions).
        var shellIdx = Array.FindIndex(e.Args, a => a.Equals("--shell-test", StringComparison.OrdinalIgnoreCase));
        if (shellIdx >= 0 && shellIdx + 1 < e.Args.Length)
        {
            var sfile = e.Args[shellIdx + 1];
            Dispatcher.InvokeAsync(async () =>
            {
                string r;
                try { r = await AurevixShell.ApplyOnceAsync(); } catch (Exception ex) { r = "FAIL " + ex; }
                File.WriteAllText(sfile, r);
                await Task.Delay(8000);
                Shutdown();
            });
            return;
        }

        var shotIdx = Array.FindIndex(e.Args, a => a.Equals("--render-screenshots", StringComparison.OrdinalIgnoreCase));
        if (shotIdx >= 0 && shotIdx + 1 < e.Args.Length)
        {
            var dir = e.Args[shotIdx + 1];
            Dispatcher.InvokeAsync(async () =>
            {
                try { await Screenshotter.RunAsync(dir); }
                catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "error.txt"), ex.ToString()); }
                Shutdown();
            });
            return;
        }
        AurevixLog.Info($"AUREVIX Control Center {AppServices.CurrentVersion} iniciado (args: {string.Join(' ', args)})");

        if (background && !AppServices.Settings.StartWithWindows && AppServices.State.FirstRunCompleted)
        {
            AurevixLog.Info("Inicio automático desactivado por el usuario; se cierra.");
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.Invoke(() => ShowMain());
        }) { IsBackground = true };
        listener.Start();

        _tray = new TrayIcon(
            open: () => ShowMain(),
            checkUpdates: () => _ = CheckUpdatesInteractiveAsync(),
            exit: ExitApp);

        // En segundo plano no se abre una ventana de golpe: se avisa desde la bandeja y se abre al pulsar.
        AppServices.Updates.UpdateAvailable += r => Dispatcher.Invoke(() =>
        {
            if (r.Release is null) return;
            if (_tray is { } tray)
                tray.ShowBalloon("AUREVIX Update",
                    $"Hay una actualización de AUREVIX (versión {r.Release.Version}). Pulsa para verla.",
                    () => Dispatcher.Invoke(() => ShowUpdateWindow(r)));
            else
                ShowUpdateWindow(r, topmost: false);
        });

        // Tras una actualización silenciosa: pedir reinicio para aplicarlo todo (sin forzarlo).
        if (args.Contains("--after-update"))
            RestartPrompt.Ask($"AUREVIX {AppServices.CurrentVersion} instalado",
                "La actualización se ha instalado. Reinicia el equipo para que todos los componentes y ajustes se apliquen por completo.");
        AppServices.Updates.StartBackgroundChecks();

        AurevixShell.Notify += msg => Dispatcher.Invoke(() => _tray?.ShowBalloon("AUREVIX Shell", msg));
        AurevixShell.Start();

        GameBoost.Notify += msg => Dispatcher.Invoke(() => _tray?.ShowBalloon("AUREVIX Game Boost", msg));
        GameBoost.Start();

        // Tema claro/oscuro automático, perfiles automáticos y avisos de salud.
        ThemeScheduler.Start();
        AutoProfiles.Notify = msg => Dispatcher.Invoke(() => _tray?.ShowBalloon("AUREVIX · Perfiles", msg));
        AutoProfiles.Start();
        HealthMonitor.Notify = (title, text, page) => Dispatcher.Invoke(() =>
            _tray?.ShowBalloon(title, text, page is null ? null : () => Dispatcher.Invoke(() => ShowMain(page))));
        HealthMonitor.Start();
        AutoMaintenance.Notify = msg => Dispatcher.Invoke(() => _tray?.ShowBalloon("AUREVIX · Mantenimiento", msg));
        AutoMaintenance.Start();

        if (!AppServices.State.FirstRunCompleted || args.Contains("--first-run"))
        {
            var wizard = new FirstRunWindow();
            wizard.Closed += (_, _) => { if (!background) ShowMain(); };
            wizard.Show();
            return;
        }

        if (!background)
        {
            var pageIdx = Array.IndexOf(args, "--page");
            ShowMain(pageIdx >= 0 && pageIdx + 1 < args.Length ? args[pageIdx + 1] : null);
        }
    }

    public void ShowMain(string? page = null)
    {
        if (_main is null || !_main.IsLoaded)
        {
            _main = new MainWindow();
            _main.Closed += (_, _) => _main = null;
        }
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        if (page is not null) _main.Navigate(page);
    }

    /// <summary>La "pantalla automática" que aparece al publicar una nueva versión.</summary>
    public void ShowUpdateWindow(UpdateCheckResult result, bool topmost = true)
    {
        if (_updateWindowOpen || result.Release is null) return;
        _updateWindowOpen = true;
        var w = new UpdateWindow(result.Release) { Topmost = topmost };
        w.Closed += (_, _) => _updateWindowOpen = false;
        w.Show();
        w.Activate();
    }

    /// <summary>Notificación en la bandeja, usable desde cualquier hilo.</summary>
    public void Notify(string title, string text) => Dispatcher.Invoke(() => _tray?.ShowBalloon(title, text));

    private async Task CheckUpdatesInteractiveAsync()
    {
        var r = await AppServices.Updates.CheckAsync(notify: false);
        if (r.Status == UpdateStatus.Available) ShowUpdateWindow(r);
        else _tray?.ShowBalloon("AUREVIX Update", r.Message);
    }

    private void ExitApp()
    {
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (GameBoost.ActiveGame is not null) GameBoost.Restore();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
