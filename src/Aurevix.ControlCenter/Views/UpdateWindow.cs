using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core.Logging;
using Aurevix.Update;

namespace Aurevix.ControlCenter.Views;

/// <summary>
/// Pantalla que aparece automáticamente cuando se publica una nueva versión.
/// El usuario decide: actualizar ahora, más tarde u omitir esta versión.
/// </summary>
public sealed class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private readonly ProgressBar _bar = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _state = Ui.Muted("");
    private readonly StackPanel _buttons;
    private CancellationTokenSource? _cts;

    public UpdateWindow(ReleaseInfo release)
    {
        _release = release;
        Title = "AUREVIX Update";
        Width = 560; SizeToContent = SizeToContent.Height; MaxHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Ui.Brush("Bg");

        var notes = new ScrollViewer { MaxHeight = 320, Content = Ui.Text(string.IsNullOrWhiteSpace(release.Notes) ? "Mejoras y correcciones." : release.Notes) };
        _buttons = Ui.Row(
            Ui.Button("Actualizar ahora", () => _ = InstallAsync(), primary: true),
            Ui.Button("Más tarde", Close),
            Ui.Button("Omitir esta versión", Skip));

        var title = Ui.H1("Nueva versión de AUREVIX");
        var sp = new StackPanel { Margin = new Thickness(28) };
        sp.Children.Add(new TextBlock { Text = "ACTUALIZACIÓN DISPONIBLE", Foreground = Ui.Brush("Accent"), FontWeight = FontWeights.SemiBold, FontSize = 12 });
        sp.Children.Add(title);
        sp.Children.Add(Ui.Muted($"Versión instalada {AppServices.CurrentVersion} → nueva {release.Version}"));
        sp.Children.Add(Ui.Card(Ui.H2(release.Title), notes));
        if (release.RequiresRestart)
            sp.Children.Add(Ui.Muted("⚠ Esta actualización requiere reiniciar el equipo. Se te pedirá confirmación antes."));
        sp.Children.Add(Ui.Muted("El paquete se verifica (firma del manifiesto y hash SHA-256) antes de instalarse. Windows pedirá permiso (UAC)."));
        sp.Children.Add(_bar);
        sp.Children.Add(_state);
        sp.Children.Add(_buttons);
        Content = sp;
        Closed += (_, _) => _cts?.Cancel();
    }

    private void Skip()
    {
        AppServices.State.SkippedVersion = _release.Version;
        AppServices.SaveState();
        Close();
    }

    private async Task InstallAsync()
    {
        _buttons.IsEnabled = false;
        _bar.Visibility = Visibility.Visible;
        _state.Text = "Descargando…";
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => { _bar.Value = p * 100; _state.Text = $"Descargando… {p:P0}"; });
            var installer = await AppServices.Updates.DownloadAsync(_release, progress, _cts.Token);
            _state.Text = "Paquete verificado. Iniciando instalación…";
            if (_release.RequiresRestart &&
                MessageBox.Show("La actualización necesitará reiniciar el equipo al terminar. ¿Continuar?", "AUREVIX Update", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                _buttons.IsEnabled = true;
                return;
            }
            AppServices.Updates.LaunchInstaller(installer, _release);
            Close();
        }
        catch (OperationCanceledException) { }
        catch (System.ComponentModel.Win32Exception)
        {
            _state.Text = "Instalación cancelada (no se concedió el permiso). Puedes intentarlo de nuevo.";
            _buttons.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AurevixLog.Error("Fallo al actualizar", ex);
            _state.Text = $"No se pudo actualizar: {ex.Message} Tu versión actual sigue intacta.";
            _buttons.IsEnabled = true;
        }
    }
}
