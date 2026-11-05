using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Aurevix.Core.Logging;

namespace Aurevix.ControlCenter.Views;

/// <summary>
/// Pide reiniciar para aplicar cambios (tras una actualización o ajustes del sistema).
/// Nunca reinicia sin consentimiento: "Reiniciar ahora", "En 10 minutos" (cancelable) o "Más tarde".
/// Usa shutdown.exe, que no requiere privilegios para el equipo local.
/// </summary>
public sealed class RestartPrompt : Window
{
    private static bool _scheduled;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _deadline;

    public RestartPrompt(string title, string detail)
    {
        Title = "AUREVIX";
        Width = 520; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Ui.Brush("Bg");

        var countdown = Ui.Muted("");
        var cancel = Ui.Button("Cancelar reinicio programado", () =>
        {
            Run("/a");
            _scheduled = false;
            _tick.Stop();
            countdown.Text = "Reinicio cancelado.";
        });
        cancel.Visibility = _scheduled ? Visibility.Visible : Visibility.Collapsed;

        var later10 = Ui.Button("En 10 minutos", () =>
        {
            Run("/r /t 600 /c \"AUREVIX reiniciará el equipo para aplicar los cambios. Guarda tu trabajo.\"");
            _scheduled = true;
            _deadline = DateTime.Now.AddMinutes(10);
            cancel.Visibility = Visibility.Visible;
            _tick.Start();
        });

        _tick.Tick += (_, _) =>
        {
            var left = _deadline - DateTime.Now;
            countdown.Text = left > TimeSpan.Zero ? $"El equipo se reiniciará en {left:mm\\:ss}. Guarda tu trabajo." : "Reiniciando…";
        };

        var sp = new StackPanel { Margin = new Thickness(28) };
        sp.Children.Add(Ui.Eyebrow("REINICIO NECESARIO"));
        sp.Children.Add(Ui.H1(title));
        var d = Ui.Muted(detail);
        d.Margin = new Thickness(0, 0, 0, 18);
        sp.Children.Add(d);
        sp.Children.Add(Ui.Row(
            Ui.Button("Reiniciar ahora", () =>
            {
                if (MessageBox.Show("Guarda tu trabajo. ¿Reiniciar el equipo ahora?", "AUREVIX", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    Run("/r /t 5");
            }, primary: true),
            later10,
            Ui.Button("Más tarde", Close)));
        sp.Children.Add(cancel);
        countdown.Margin = new Thickness(0, 12, 0, 0);
        sp.Children.Add(countdown);
        Content = sp;
        Closed += (_, _) => _tick.Stop();
        Loaded += (_, _) => Ui.Stagger(sp);
    }

    public static void Ask(string title, string detail)
    {
        var w = new RestartPrompt(title, detail);
        w.Show();
        w.Activate();
    }

    private static void Run(string args)
    {
        try
        {
            AurevixLog.Info($"shutdown {args}");
            Process.Start(new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex) { AurevixLog.Error("No se pudo programar el reinicio", ex); }
    }
}
