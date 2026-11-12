using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core.Logging;
using Aurevix.Update;
using Microsoft.Win32;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Actualizaciones de AUREVIX: versión instalada, novedades, canal y estado de descarga.</summary>
public sealed class UpdatesPage : UserControl
{
    private readonly TextBlock _status = Ui.Text("");
    private readonly StackPanel _release = new();
    private readonly Button _check;

    public UpdatesPage()
    {
        var (root, body) = Ui.Page("Actualizaciones", "Actualizaciones firmadas de la aplicación AUREVIX, sus módulos y configuraciones.");

        var channel = new ComboBox { ItemsSource = UpdateChannels.All, SelectedItem = AppServices.Settings.UpdateChannel, Margin = new Thickness(0, 0, 12, 0) };
        var revertingChannel = false;
        channel.SelectionChanged += (_, _) =>
        {
            if (revertingChannel || channel.SelectedItem is not string selected) return;
            if (selected == AppServices.Settings.UpdateChannel) return;
            if (selected is "beta" or "developer" &&
                MessageBox.Show("Las versiones beta pueden tener fallos. ¿Cambiar?", "Canal de actualizaciones", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                revertingChannel = true;
                channel.SelectedItem = AppServices.Settings.UpdateChannel;
                revertingChannel = false;
                return;
            }
            AppServices.Settings.UpdateChannel = selected;
            AppServices.SaveSettings();
            AppServices.Updates.RestartChecks();
            // Al cambiar de canal se comprueba al momento si hay algo nuevo.
            _ = CheckAsync();
        };

        _check = Ui.Button("Buscar actualizaciones", () => _ = CheckAsync(), primary: true);
        body.Children.Add(Ui.Card(
            Ui.H2($"Versión instalada: {AppServices.CurrentVersion}"),
            Ui.Muted(AppServices.State.LastUpdateCheckUtc is { } t ? $"Última comprobación: {t.ToLocalTime():g}" : "Aún no se ha comprobado."),
            Ui.Row(Ui.Muted("Canal: "), channel, _check),
            Ui.Muted("Stable: recomendado. Beta: novedades antes, puede contener errores. Developer: versiones de pruebas internas."),
            _status));
        body.Children.Add(_release);

        body.Children.Add(Ui.Card(Ui.H2("Actualizaciones de Windows"),
            Ui.Muted("Las actualizaciones del propio Windows (seguridad, controladores, nuevas versiones) siguen llegando por Windows Update. AUREVIX no las sustituye."),
            Ui.Muted(WindowsUpdateStatus()),
            Ui.Row(
                Ui.Button("Abrir Windows Update", SystemTools.OpenWindowsUpdate),
                Ui.Button("Buscar actualizaciones de Windows", () => SystemTools.Start("ms-settings:windowsupdate-action")))));

        var prev = UpdateService.FindPreviousInstaller();
        if (prev is not null)
            body.Children.Add(Ui.Card(Ui.H2("Volver a la versión anterior"),
                Ui.Muted("Reinstala el instalador verificado de la versión anterior que se guardó durante la última actualización."),
                Ui.Row(Ui.Button("Reinstalar versión anterior", () =>
                {
                    if (MessageBox.Show("Se reinstalará la versión anterior de AUREVIX. Tus ajustes y perfiles se mantienen. ¿Continuar?", "Volver a la versión anterior", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        SystemTools.Start(prev);
                }))));

        if (AppServices.Updates.LastResult is { } last) Show(last);
        Content = root;
    }

    /// <summary>Estado de Windows Update leído del registro: reinicio pendiente y última instalación correcta.</summary>
    private static string WindowsUpdateStatus()
    {
        var parts = new List<string>();
        try
        {
            const string baseKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update";
            using (var reboot = Registry.LocalMachine.OpenSubKey(baseKey + @"\RebootRequired"))
                if (reboot is not null) parts.Add("Reinicio pendiente.");
            using var install = Registry.LocalMachine.OpenSubKey(baseKey + @"\Results\Install");
            if (install?.GetValue("LastSuccessTime") is string s &&
                DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var utc))
                parts.Add($"Última instalación: {utc.ToLocalTime():g}.");
        }
        catch (Exception ex)
        {
            AurevixLog.Warn($"No se pudo leer el estado de Windows Update: {ex.Message}");
        }
        return parts.Count > 0 ? string.Join(" ", parts) : "Sin información de la última instalación.";
    }

    private async Task CheckAsync()
    {
        _check.IsEnabled = false;
        _status.Text = "Comprobando…";
        try { Show(await AppServices.Updates.CheckAsync(notify: false)); }
        finally { _check.IsEnabled = true; }
    }

    private void Show(UpdateCheckResult r)
    {
        _status.Text = r.Message;
        _status.Margin = new Thickness(0, 10, 0, 0);
        _release.Children.Clear();
        if (r.Status == UpdateStatus.Available && r.Release is { } rel)
            _release.Children.Add(Ui.Card(Ui.H2(rel.Title), Ui.Text(rel.Notes),
                Ui.Row(Ui.Button("Instalar ahora", () => ((App)Application.Current).ShowUpdateWindow(r), primary: true))));
    }
}
