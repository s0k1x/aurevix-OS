using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core.Logging;
using Aurevix.Performance;
using Aurevix.Update;

namespace Aurevix.ControlCenter.Views;

/// <summary>
/// Pantalla que aparece cuando el usuario abre el aviso de una nueva versión.
/// El usuario decide: actualizar ahora, más tarde u omitir esta versión.
/// </summary>
public sealed class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private readonly ProgressBar _bar = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _state = Ui.Muted("");
    private readonly TextBlock _mode = Ui.Muted("");
    private readonly StackPanel _buttons;
    private readonly Button _install;
    private readonly Button _later;
    private readonly Button _skip;
    private readonly Button _cancel;
    private CancellationTokenSource? _cts;
    private bool _closed;
    private bool? _useService;

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
        _install = Ui.Button("Actualizar ahora", () => _ = InstallAsync(), primary: true);
        _later = Ui.Button("Más tarde", Close);
        _skip = Ui.Button("Omitir esta versión", Skip);
        _cancel = Ui.Button("Cancelar", () => _cts?.Cancel());
        _cancel.Visibility = Visibility.Collapsed;
        _buttons = Ui.Row(_install, _later, _skip, _cancel);

        var title = Ui.H1("Nueva versión de AUREVIX");
        var sp = new StackPanel { Margin = new Thickness(28) };
        sp.Children.Add(new TextBlock { Text = "ACTUALIZACIÓN DISPONIBLE", Foreground = Ui.Brush("Accent"), FontWeight = FontWeights.SemiBold, FontSize = 12 });
        sp.Children.Add(title);
        sp.Children.Add(Ui.Muted($"Versión instalada {AppServices.CurrentVersion} → nueva {release.Version}"));
        sp.Children.Add(Ui.Card(Ui.H2(release.Title), notes));
        if (release.RequiresRestart)
            sp.Children.Add(Ui.Muted("⚠ Esta actualización requiere reiniciar el equipo. Se te pedirá confirmación antes."));
        sp.Children.Add(Ui.Muted("Antes de instalarse se comprueba que el paquete es auténtico y no está dañado."));
        sp.Children.Add(_mode);
        sp.Children.Add(_bar);
        sp.Children.Add(_state);
        sp.Children.Add(_buttons);
        Content = sp;

        // Cerrar la ventana NO cancela la descarga: solo el botón «Cancelar» lo hace.
        Closed += (_, _) => _closed = true;
        Loaded += async (_, _) => await ShowModeAsync();
    }

    /// <summary>Explica con honestidad si Windows pedirá permiso o no, según cómo se vaya a instalar.</summary>
    private async Task ShowModeAsync()
    {
        try
        {
            _useService ??= await CoreServiceClient.IsAvailableAsync();
            _mode.Text = _useService == true
                ? "Se instala sola, sin avisos de permisos, y AUREVIX se vuelve a abrir al terminar."
                : "Windows te pedirá permiso para instalarla. AUREVIX se vuelve a abrir al terminar.";
        }
        catch (Exception ex)
        {
            AurevixLog.Warn($"No se pudo comprobar el servicio de AUREVIX: {ex.Message}");
            _useService = false;
            _mode.Text = "Windows te pedirá permiso para instalarla. AUREVIX se vuelve a abrir al terminar.";
        }
    }

    private void Skip()
    {
        AppServices.State.SkippedVersion = _release.Version;
        AppServices.SaveState();
        Close();
    }

    private void SetBusy(bool busy)
    {
        _install.IsEnabled = !busy;
        _skip.IsEnabled = !busy;
        _later.Content = busy ? "Seguir en segundo plano" : "Más tarde";
    }

    /// <summary>Avisa al terminar si la ventana ya se cerró (la descarga siguió en segundo plano).</summary>
    private void NotifyIfBackground(string text)
    {
        if (_closed && Application.Current is App app) app.Notify("AUREVIX Update", text);
    }

    private async Task InstallAsync()
    {
        SetBusy(true);
        _state.Text = "Preparando…";
        _cts = new CancellationTokenSource();
        try
        {
            if (_release.RequiresRestart &&
                MessageBox.Show("La actualización necesitará reiniciar el equipo al terminar. ¿Continuar?", "AUREVIX Update", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                SetBusy(false);
                _state.Text = "";
                return;
            }

            // Como parte del sistema, AUREVIX Core Service descarga, comprueba la firma e instala: sin avisos de UAC.
            _useService ??= await CoreServiceClient.IsAvailableAsync();
            if (_useService == true)
            {
                // El servicio no informa de porcentajes: se muestran fases en texto, sin inventar progreso.
                _state.Text = "Descargando y comprobando la firma…";
                var r = await CoreServiceClient.SendAsync(new CoreRequest { Op = "update-install", Value = AppServices.Settings.UpdateChannel, Label = UpdateService.InstallId }, TimeSpan.FromMinutes(30));
                if (!r.Ok) throw new InvalidOperationException(r.Error);
                AurevixLog.Info($"Actualización {_release.Version} en manos de AUREVIX Core Service");
                _state.Text = "Instalando… AUREVIX se cerrará y volverá a abrirse solo.";
                if (_release.RequiresRestart) AskRestart();
                else NotifyIfBackground($"AUREVIX {_release.Version} se está instalando.");
                return;
            }

            _bar.Visibility = Visibility.Visible;
            _cancel.Visibility = Visibility.Visible;
            var progress = new Progress<double>(p => { _bar.Value = p * 100; _state.Text = $"Descargando… {p:P0}"; });
            _state.Text = "Descargando…";
            var installer = await AppServices.Updates.DownloadAsync(_release, progress, _cts.Token);
            _cancel.Visibility = Visibility.Collapsed;
            _state.Text = "Paquete comprobado. Iniciando la instalación…";
            NotifyIfBackground($"La descarga de AUREVIX {_release.Version} ha terminado. Se va a iniciar la instalación.");
            AppServices.Updates.LaunchInstaller(installer, _release);
            if (_release.RequiresRestart) AskRestart();
            Close();
        }
        catch (OperationCanceledException)
        {
            _state.Text = "Descarga cancelada. Puedes volver a intentarlo cuando quieras.";
            Reset();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AurevixLog.Warn($"Instalación no iniciada (permiso denegado): {ex.Message}");
            _state.Text = "Instalación cancelada (no se concedió el permiso). Puedes intentarlo de nuevo.";
            Reset();
            NotifyIfBackground("La instalación de AUREVIX no se ha iniciado porque no se concedió el permiso.");
        }
        catch (Exception ex)
        {
            AurevixLog.Error("Fallo al actualizar", ex);
            _state.Text = ex is HttpRequestException or IOException
                ? "No se pudo descargar la actualización. Comprueba tu conexión e inténtalo de nuevo. Tu versión actual sigue intacta."
                : "No se pudo completar la actualización. Tu versión actual sigue intacta; puedes intentarlo más tarde.";
            Reset();
            NotifyIfBackground("No se pudo completar la actualización de AUREVIX. Tu versión actual sigue intacta.");
        }
    }

    private void Reset()
    {
        SetBusy(false);
        _bar.Visibility = Visibility.Collapsed;
        _cancel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Ofrece «Reiniciar ahora» o «Reiniciar más tarde» (RestartPrompt).</summary>
    private void AskRestart() =>
        RestartPrompt.Ask($"AUREVIX {_release.Version} instalado",
            "La actualización necesita reiniciar el equipo para aplicarse por completo. Puedes reiniciar ahora o más tarde.");
}
