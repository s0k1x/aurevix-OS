using System.Windows;
using System.Windows.Controls;
using Aurevix.ControlCenter.Services;
using Aurevix.Core;
using Aurevix.Core.Logging;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Ajustes: inicio automático, notificaciones, privacidad, idioma, registros y permisos.</summary>
public sealed class SettingsPage : UserControl
{
    public SettingsPage()
    {
        var (root, body) = Ui.Page("Ajustes", "Preferencias de AUREVIX Control Center.");
        var s = AppServices.Settings;

        body.Children.Add(Ui.Card(Ui.H2("General"),
            Check("Iniciar AUREVIX con Windows (en la bandeja del sistema)", s.StartWithWindows, v => s.StartWithWindows = v),
            Check("Buscar actualizaciones automáticamente", s.CheckUpdatesAutomatically, v => s.CheckUpdatesAutomatically = v),
            Check("Mostrar notificaciones", s.Notifications, v => s.Notifications = v),
            Check("Reducir movimiento (sin animaciones)", s.ReduceMotion, v => { s.ReduceMotion = v; Ui.ReduceMotion = v; })));

        body.Children.Add(Ui.Card(Ui.H2("Idioma"),
            Ui.Muted("Idioma de la interfaz. En esta versión solo está disponible el español."),
            new ComboBox { ItemsSource = new[] { "Español (es-ES)" }, SelectedIndex = 0, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) }));

        body.Children.Add(Ui.Card(Ui.H2("Privacidad"),
            Ui.Muted("El diagnóstico se procesa en tu equipo. AUREVIX solo descarga el manifiesto de versiones; no envía datos personales, " +
                     "nombres de usuario, documentos ni historial. El identificador de instalación es aleatorio y solo se usa localmente para el despliegue gradual."),
            Check("Compartir estadísticas anónimas de diagnóstico (próximamente; se mostrará exactamente qué se envía)", s.ShareAnonymousDiagnostics, v => s.ShareAnonymousDiagnostics = v, enabled: false)));

        body.Children.Add(Ui.Card(Ui.H2("Registros"),
            Ui.Muted($"Los registros técnicos se guardan {s.LogRetentionDays} días en {AurevixPaths.Logs}."),
            Ui.Row(
                Ui.Button("Abrir registros", () => SystemTools.OpenFolder(AurevixPaths.Logs)),
                Ui.Button("Borrar registros", () =>
                {
                    if (MessageBox.Show("¿Borrar todos los registros de AUREVIX?", "Registros", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        AurevixLog.DeleteAll();
                }))));

        body.Children.Add(Ui.Card(Ui.H2("Permisos"),
            Ui.Muted(AppServices.IsAdmin
                ? "⚠ AUREVIX se está ejecutando como administrador. No es necesario: ciérralo y ábrelo normalmente."
                : "AUREVIX se ejecuta sin privilegios de administrador. Las acciones que los necesitan (punto de restauración, actualizaciones) los piden puntualmente con el aviso de Windows (UAC).")));

        Content = root;
    }

    private static CheckBox Check(string text, bool value, Action<bool> set, bool enabled = true)
    {
        var cb = new CheckBox { Content = text, IsChecked = value, IsEnabled = enabled };
        cb.Checked += (_, _) => { set(true); AppServices.SaveSettings(); };
        cb.Unchecked += (_, _) => { set(false); AppServices.SaveSettings(); };
        return cb;
    }
}
