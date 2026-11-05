using System.IO;
using WinForms = System.Windows.Forms;

namespace Aurevix.ControlCenter.Services;

/// <summary>Icono de la bandeja del sistema (área de notificación).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayIcon(Action open, Action checkUpdates, Action exit)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Abrir AUREVIX Control Center", null, (_, _) => open());
        menu.Items.Add("Buscar actualizaciones", null, (_, _) => checkUpdates());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Text = "AUREVIX Control Center",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => open();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/aurevix.ico"));
            if (info is not null) return new System.Drawing.Icon(info.Stream);
        }
        catch (IOException) { }
        return System.Drawing.SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
