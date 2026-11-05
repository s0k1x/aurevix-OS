using System.IO;
using System.Windows;
using Aurevix.Core.Logging;

namespace Aurevix.Installer;

public partial class App : Application
{
    public static string LogDir { get; } = Path.Combine(Engine.MediaEngine.WorkRoot, "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AurevixLog.Initialize(LogDir);
        AurevixLog.Info("AUREVIX Installer iniciado.");
        DispatcherUnhandledException += (_, ex) =>
        {
            AurevixLog.Error("Error no controlado", ex.Exception);
            MessageBox.Show($"Error inesperado: {ex.Exception.Message}\n\nRegistro: {LogDir}", "AUREVIX Installer");
            ex.Handled = true;
        };
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
