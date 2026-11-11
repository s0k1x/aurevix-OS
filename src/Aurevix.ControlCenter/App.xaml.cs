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

        AppServices.Updates.UpdateAvailable += r => Dispatcher.Invoke(() => ShowUpdateWindow(r));

        // Tras una actualización silenciosa: pedir reinicio para aplicarlo todo (sin forzarlo).
        if (args.Contains("--after-update"))
            RestartPrompt.Ask($"AUREVIX {AppServices.CurrentVersion} instalado",
                "La actualización se ha instalado. Reinicia el equipo para que todos los componentes y ajustes se apliquen por completo.");
        AppServices.Updates.StartBackgroundChecks();

        GameBoost.Notify += msg => Dispatcher.Invoke(() => _tray?.ShowBalloon("AUREVIX Game Boost", msg));
        GameBoost.Start();

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
    public void ShowUpdateWindow(UpdateCheckResult result)
    {
        if (_updateWindowOpen || result.Release is null) return;
        _updateWindowOpen = true;
        var w = new UpdateWindow(result.Release);
        w.Closed += (_, _) => _updateWindowOpen = false;
        w.Show();
        w.Activate();
    }

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
