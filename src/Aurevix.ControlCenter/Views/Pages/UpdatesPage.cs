using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Update;

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
        channel.SelectionChanged += (_, _) =>
        {
            AppServices.Settings.UpdateChannel = (string)channel.SelectedItem;
            AppServices.SaveSettings();
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
            Ui.Row(Ui.Button("Abrir Windows Update", SystemTools.OpenWindowsUpdate))));

        var prev = UpdateService.FindPreviousInstaller();
        if (prev is not null)
            body.Children.Add(Ui.Card(Ui.H2("Volver a la versión anterior"),
                Ui.Muted("Reinstala el instalador verificado de la versión anterior que se guardó durante la última actualización."),
                Ui.Row(Ui.Button("Reinstalar versión anterior", () =>
                {
                    if (MessageBox.Show("¿Reinstalar la versión anterior de AUREVIX?", "Rollback", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        SystemTools.Start(prev);
                }))));

        if (AppServices.Updates.LastResult is { } last) Show(last);
        Content = root;
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
