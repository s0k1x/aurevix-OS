using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aurevix.ControlCenter.Services;
using Aurevix.Performance;

namespace Aurevix.ControlCenter.Views.Pages;

/// <summary>Biblioteca de juegos: Steam, Epic, GOG y los añadidos en Game Boost.</summary>
public sealed class GamesPage : UserControl
{
    private readonly StackPanel _body;
    private readonly TextBlock _count = Ui.Muted("Buscando juegos…");
    private readonly TextBox _search = new() { MinWidth = 260, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(6, 4, 6, 4) };
    private readonly ComboBox _filter = new() { MinWidth = 140 };
    private readonly WrapPanel _grid = new() { Margin = new Thickness(0, 12, 0, 0) };
    private List<GameEntry> _games = [];

    public GamesPage()
    {
        var (root, body) = Ui.Page("Juegos", "Todos tus juegos en un sitio. «Jugar» activa Game Boost y abre el juego.");
        _body = body;
        _filter.ItemsSource = new[] { "Todos", "Steam", "Epic", "GOG", "Añadidos" };
        _filter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => Render();
        _filter.SelectionChanged += (_, _) => Render();

        body.Children.Add(Ui.Card(Ui.H2("Tus juegos"), _count, Ui.Row(_search, _filter)));
        body.Children.Add(_grid);
        Content = root;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try { _games = await GameLibrary.ScanAsync(); }
        catch { _games = []; }
        _loaded = true;
        Render();
    }

    private void Render()
    {
        _grid.Children.Clear();
        if (!_loaded) return;
        _count.Text = _games.Count == 0
            ? "No hemos encontrado juegos de Steam, Epic o GOG. Puedes añadirlos en Game Boost."
            : _games.Count == 1 ? "1 juego" : $"{_games.Count} juegos";

        var q = _search.Text.Trim();
        var source = _filter.SelectedItem as string ?? "Todos";
        foreach (var g in _games)
        {
            if (source == "Añadidos" ? g.Source != "Añadido" : source != "Todos" && g.Source != source) continue;
            if (q.Length > 0 && !g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            _grid.Children.Add(Tile(g));
        }
    }

    private bool _loaded;

    private UIElement Tile(GameEntry g)
    {
        UIElement badge;
        var icon = Icon(g.ExePath);
        if (icon is not null)
            badge = new Image { Source = icon, Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Left };
        else
            badge = new TextBlock
            {
                Text = g.Name.Length > 0 ? g.Name[..1].ToUpperInvariant() : "?", FontSize = 40, FontWeight = FontWeights.SemiBold,
                Foreground = Ui.Brush("Accent"), Height = 48, HorizontalAlignment = HorizontalAlignment.Left
            };

        var name = Ui.Text(g.Name, 14, FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 8, 0, 0);

        var items = new List<UIElement> { badge, name, Ui.Muted(g.Source), Ui.Row(Ui.Button("Jugar", () => Play(g), primary: true)) };
        if (g.ExePath is { } exe && File.Exists(exe))
        {
            var cb = new CheckBox { Content = "Gráfica de alto rendimiento", Margin = new Thickness(0, 10, 0, 0), FontSize = 12 };
            try { cb.IsChecked = PerGameTweaks.HighPerfGpu(exe); } catch { }
            cb.Checked += (_, _) => SetGpu(exe, true);
            cb.Unchecked += (_, _) => SetGpu(exe, false);
            items.Add(cb);
        }
        var card = Ui.Card(items.ToArray());
        card.Width = 270;
        card.Margin = new Thickness(0, 0, 12, 12);
        return card;
    }

    private static void SetGpu(string exe, bool on)
    {
        try { PerGameTweaks.SetHighPerfGpu(exe, on); }
        catch (Exception ex) { MessageBox.Show("No se ha podido cambiar: " + ex.Message, "Juegos"); }
    }

    private static void Play(GameEntry g)
    {
        try
        {
            // Game Boost reconoce el juego por su .exe: si no está en la lista, se añade.
            if (g.ExePath is { } exe)
            {
                var s = GameBoost.Settings;
                var file = Path.GetFileName(exe);
                if (!s.Games.Contains(file, StringComparer.OrdinalIgnoreCase)) s.Games.Add(file);
                s.GamePaths[file] = exe;
                GameBoost.Save();
            }
            var target = g.LaunchUri ?? g.ExePath;
            if (target is null || (g.LaunchUri is null && !File.Exists(target)))
            {
                MessageBox.Show("No encontramos cómo abrir este juego. Ábrelo desde su tienda.", "Juegos");
                return;
            }
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (g.LaunchUri is null && Path.GetDirectoryName(target) is { } wd) psi.WorkingDirectory = wd;
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) { MessageBox.Show("No se ha podido abrir el juego: " + ex.Message, "Juegos"); }
    }

    /// <summary>Icono del .exe como imagen de WPF; null si no se puede sacar.</summary>
    private static BitmapSource? Icon(string? exe)
    {
        if (exe is null || !File.Exists(exe)) return null;
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ico is null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }
}
