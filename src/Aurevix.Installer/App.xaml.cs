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
        new MainWindow().Show();
    }
}
