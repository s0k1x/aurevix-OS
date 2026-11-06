using System.IO;
using System.Windows;
using Aurevix.Core.Logging;

namespace Aurevix.Installer;

public partial class App : Application
{
    private System.Threading.Mutex? _mutex;
    public static string LogDir { get; } = Path.Combine(Engine.MediaEngine.WorkRoot, "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Una sola ventana a la vez: dos instaladores a la vez podrían pisarse al escribir el USB.
        var selftest = e.Args.Any(a => a.Equals("--selftest-iso", StringComparison.OrdinalIgnoreCase));
        if (!selftest)
        {
            _mutex = new System.Threading.Mutex(true, @"Local\Aurevix.Installer.SingleInstance", out var first);
            if (!first)
            {
                MessageBox.Show("AUREVIX Installer ya está abierto.", "AUREVIX Installer");
                Shutdown();
                return;
            }
        }
        AurevixLog.Initialize(LogDir);
        AurevixLog.Info("AUREVIX Installer iniciado.");
        DispatcherUnhandledException += (_, ex) =>
        {
            AurevixLog.Error("Error no controlado", ex.Exception);
            MessageBox.Show($"Error inesperado: {ex.Exception.Message}\n\nRegistro: {LogDir}", "AUREVIX Installer");
            ex.Handled = true;
        };
        // Autoprueba del motor de ISOs (solo la usa el CI en un Windows real).
        var st = Array.FindIndex(e.Args, a => a.Equals("--selftest-iso", StringComparison.OrdinalIgnoreCase));
        if (st >= 0 && st + 1 < e.Args.Length)
        {
            var outDir = e.Args[st + 1];
            Dispatcher.InvokeAsync(async () =>
            {
                try { await Engine.SelfTest.IsoAsync(outDir); File.WriteAllText(Path.Combine(outDir, "selftest-iso.txt"), "OK"); }
                catch (Exception ex) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "selftest-iso.txt"), "FAIL\n" + ex); }
                Shutdown();
            });
            return;
        }

        var w = new MainWindow();
        w.Show();
        var idx = Array.FindIndex(e.Args, a => a.Equals("--render-screenshots", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < e.Args.Length)
        {
            var dir = e.Args[idx + 1];
            Dispatcher.InvokeAsync(async () =>
            {
                try { await w.RenderScreenshotsAsync(dir); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error-installer.txt"), ex.ToString()); }
                Shutdown();
            });
        }
    }
}
