using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Aurevix.Core.Logging;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>
/// Instalador de aplicaciones con winget (gestor de paquetes oficial de Microsoft). Cada app se descarga de su
/// fuente oficial a través del repositorio de winget. Solo software gratuito y legal.
/// </summary>
public sealed class AppsPage : UserControl
{
    private sealed record App(string Id, string Name, string Category, string Note = "");

    private static readonly App[] Catalog =
    [
        new("Valve.Steam", "Steam", "Juegos"),
        new("EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Juegos"),
        new("Discord.Discord", "Discord", "Juegos"),
        new("ElectronicArts.EADesktop", "EA app", "Juegos"),
        new("Ubisoft.Connect", "Ubisoft Connect", "Juegos"),
        new("Mozilla.Firefox", "Firefox", "Navegadores"),
        new("Google.Chrome", "Google Chrome", "Navegadores"),
        new("Brave.Brave", "Brave", "Navegadores"),
        new("VideoLAN.VLC", "VLC", "Multimedia"),
        new("CodecGuide.K-LiteCodecPack.Standard", "K-Lite Codec Pack", "Multimedia", "códecs de audio y vídeo"),
        new("OBSProject.OBSStudio", "OBS Studio", "Multimedia", "grabar y hacer streaming"),
        new("Spotify.Spotify", "Spotify", "Multimedia"),
        new("7zip.7zip", "7-Zip", "Utilidades"),
        new("Notepad++.Notepad++", "Notepad++", "Utilidades"),
        new("Microsoft.PowerToys", "PowerToys", "Utilidades", "utilidades oficiales de Microsoft"),
        new("voidtools.Everything", "Everything", "Utilidades", "búsqueda de archivos instantánea"),
        new("Microsoft.VCRedist.2015+.x64", "Visual C++ 2015-2022 (x64)", "Componentes para juegos", "muchos juegos los necesitan"),
        new("Microsoft.VCRedist.2015+.x86", "Visual C++ 2015-2022 (x86)", "Componentes para juegos"),
        new("Microsoft.DirectX", "DirectX (runtime de usuario final)", "Componentes para juegos", "juegos antiguos"),
        new("Microsoft.DotNet.DesktopRuntime.8", ".NET Desktop Runtime 8", "Componentes para juegos"),
    ];

    private readonly Dictionary<string, CheckBox> _checks = new();
    private readonly TextBlock _status = Ui.Muted("");
    private readonly ProgressBar _bar = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };

    public AppsPage()
    {
        var (root, body) = Ui.Page("Apps", "Instala tus programas de una vez con winget, el gestor de paquetes oficial de Microsoft.");
        foreach (var group in Catalog.GroupBy(a => a.Category))
        {
            var wrap = Ui.Wrap();
            foreach (var a in group)
            {
                var cb = new CheckBox { Content = a.Note.Length > 0 ? $"{a.Name}  ·  {a.Note}" : a.Name, Width = 340 };
                _checks[a.Id] = cb;
                wrap.Children.Add(cb);
            }
            body.Children.Add(Ui.Card(Ui.H2(group.Key), wrap));
        }
        body.Children.Add(Ui.Card(
            Ui.Muted("Las apps se descargan de la fuente oficial de cada fabricante a través de winget. Algunos instaladores de terceros pueden pedir permiso a Windows. " +
                     "AUREVIX no incluye ni instala software de pago, activadores ni programas de origen dudoso."),
            Ui.Row(Ui.Button("Instalar seleccionadas", () => _ = InstallAsync(), primary: true), Ui.Button("Actualizar todas mis apps", () => _ = UpgradeAllAsync())),
            _bar, _status));
        Content = root;
    }

    private async Task InstallAsync()
    {
        var sel = Catalog.Where(a => _checks[a.Id].IsChecked == true).ToList();
        if (sel.Count == 0) return;
        _bar.Visibility = Visibility.Visible;
        _bar.Maximum = sel.Count;
        var ok = 0;
        for (var i = 0; i < sel.Count; i++)
        {
            _status.Text = $"Instalando {sel[i].Name} ({i + 1} de {sel.Count})…";
            _bar.Value = i;
            var code = await WingetAsync($"install --id {sel[i].Id} --exact --source winget --silent --accept-package-agreements --accept-source-agreements");
            // 0 = instalado; -1978335189 (0x8A15002B) = ya instalado / sin actualización
            if (code is 0 or -1978335189) ok++;
            _checks[sel[i].Id].IsChecked = false;
        }
        _bar.Value = sel.Count;
        _status.Text = $"Listo: {ok} de {sel.Count} apps instaladas o ya presentes.";
    }

    private async Task UpgradeAllAsync()
    {
        _bar.Visibility = Visibility.Visible;
        _bar.IsIndeterminate = true;
        _status.Text = "Actualizando todas las apps instaladas con winget…";
        await WingetAsync("upgrade --all --silent --accept-package-agreements --accept-source-agreements");
        _bar.IsIndeterminate = false;
        _status.Text = "Actualización terminada.";
    }

    private static async Task<int> WingetAsync(string args)
    {
        try
        {
            AurevixLog.Info($"winget {args}");
            using var p = Process.Start(new ProcessStartInfo("winget", args) { UseShellExecute = false, CreateNoWindow = true })!;
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo ejecutar winget: {ex.Message}\nInstala «App Installer» desde Microsoft Store.", "Apps");
            return -1;
        }
    }
}
