using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Aurevix.Core.Diagnostics;

namespace Aurevix.ControlCenter.Views;

/// <summary>Constructores de UI coherentes con el tema AUREVIX (evita XAML repetitivo).</summary>
public static class Ui
{
    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);
    public static Brush Brush(string key) => Res<Brush>(key);

    public static TextBlock H1(string text) => new() { Text = text, Style = Res<Style>("H1") };
    public static TextBlock H2(string text) => new() { Text = text, Style = Res<Style>("H2") };
    public static TextBlock Muted(string text) => new() { Text = text, Style = Res<Style>("Muted") };
    public static TextBlock Text(string text, double size = 14, FontWeight? weight = null) =>
        new() { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal };

    public static Border Card(params UIElement[] children)
    {
        var sp = new StackPanel();
        foreach (var c in children) sp.Children.Add(c);
        return new Border { Style = Res<Style>("Card"), Child = sp };
    }

    public static Button Button(string text, Action onClick, bool primary = false)
    {
        var b = new Button { Content = text };
        if (primary) b.Style = Res<Style>("Primary");
        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var c in children) sp.Children.Add(c);
        return sp;
    }

    public static WrapPanel Wrap(params UIElement[] children)
    {
        var wp = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var c in children) wp.Children.Add(c);
        return wp;
    }

    /// <summary>Página estándar: título, subtítulo y contenido desplazable.</summary>
    public static (ScrollViewer root, StackPanel body) Page(string title, string subtitle)
    {
        var body = new StackPanel { Margin = new Thickness(32, 28, 32, 28), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(H1(title));
        var sub = Muted(subtitle);
        sub.Margin = new Thickness(0, 0, 0, 20);
        body.Children.Add(sub);
        return (new ScrollViewer { Content = body }, body);
    }

    public static Brush LevelBrush(FindingLevel l) => l switch
    {
        FindingLevel.Ok => Brush("Ok"),
        FindingLevel.Review => Brush("Review"),
        FindingLevel.Attention => Brush("Attention"),
        _ => Brush("Unavailable")
    };

    public static Border Pill(string text, Brush color) => new()
    {
        Background = color, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3),
        Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x16)) }
    };

    /// <summary>Tarjeta de métrica (título + valor grande + detalle).</summary>
    public static Border Metric(string label, out TextBlock value, out TextBlock detail)
    {
        value = new TextBlock { Text = "—", FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) };
        detail = Muted("");
        var card = Card(Muted(label), value, detail);
        card.Width = 250;
        card.Margin = new Thickness(0, 0, 12, 12);
        return card;
    }

    public static Grid Columns(UIElement left, UIElement right, double leftWidth = 1)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 1);
        g.Children.Add(left);
        g.Children.Add(right);
        return g;
    }
}
