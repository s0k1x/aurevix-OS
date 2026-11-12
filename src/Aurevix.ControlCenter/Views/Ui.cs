using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Aurevix.Core.Diagnostics;

namespace Aurevix.ControlCenter.Views;

/// <summary>Constructores de UI coherentes con el tema AUREVIX (evita XAML repetitivo).</summary>
public static class Ui
{
    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// Barra de título oscura, del color del fondo de AUREVIX, en todas las ventanas de la app (API oficial de DWM).
    /// En Windows 10 solo se oscurece; el color exacto y el borde necesitan Windows 11.
    /// </summary>
    /// <summary>El Control Center sigue el tema de Windows (claro por defecto en AUREVIX).</summary>
    public static bool IsLight { get; private set; }

    public static void ApplyTheme()
    {
        IsLight = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is not int v || v != 0;
        if (IsLight)
        {
            var dicts = Application.Current.Resources.MergedDictionaries;
            dicts.Clear();
            dicts.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/AurevixLight.xaml") });
        }
        ApplyAccentResources();
    }

    private const string DefaultAccent = "#8DE03C";

    /// <summary>De dónde sale el acento elegido. El Control Center lo enlaza a sus ajustes; el Installer (que comparte este archivo) no.</summary>
    public static Func<string?> AccentSource { get; set; } = () => null;
    private static readonly Color DarkHover = Color.FromRgb(0xA6, 0xEE, 0x5E); // AccentHoverColor del tema oscuro

    /// <summary>
    /// Sustituye el acento del tema por el elegido en Ajustes (hover 12 % más claro). En tema claro, un acento muy claro
    /// se oscurece para que el texto y los botones se lean bien. El verde AUREVIX conserva el tono propio del tema.
    /// </summary>
    public static void ApplyAccentResources()
    {
        try
        {
            var hex = AccentSource();
            if (string.IsNullOrWhiteSpace(hex) || ColorConverter.ConvertFromString(hex) is not Color c) return;
            var isDefault = string.Equals(hex, DefaultAccent, StringComparison.OrdinalIgnoreCase);
            // Verde AUREVIX: se restablece al tono propio de cada tema (por si antes había otro acento).
            if (isDefault) c = IsLight ? Color.FromRgb(0x4E, 0x9A, 0x1E) : Color.FromRgb(0x8D, 0xE0, 0x3C);

            if (!isDefault && IsLight && (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.6)
                c = Color.FromRgb((byte)(c.R * 0.65), (byte)(c.G * 0.65), (byte)(c.B * 0.65));
            var hover = isDefault ? (IsLight ? Color.FromRgb(0x5D, 0xB0, 0x2A) : DarkHover) : Color.FromRgb(Lighten(c.R), Lighten(c.G), Lighten(c.B));

            var dicts = new List<ResourceDictionary> { Application.Current.Resources };
            dicts.AddRange(Application.Current.Resources.MergedDictionaries);
            foreach (var d in dicts)
            {
                if (d.Contains("AccentColor")) d["AccentColor"] = c;
                if (d.Contains("AccentHoverColor")) d["AccentHoverColor"] = hover;
                SetBrush(d, "Accent", c);
                SetBrush(d, "AccentHover", hover);
            }
        }
        catch (Exception ex) { Aurevix.Core.Logging.AurevixLog.Warn($"Acento del Control Center: {ex.Message}"); }

        static byte Lighten(byte v) => (byte)Math.Min(255, v + (255 - v) * 0.12);
    }

    /// <summary>Cambia el color del pincel en su sitio (así los estilos que ya lo usan se actualizan); si está congelado, lo reemplaza.</summary>
    private static void SetBrush(ResourceDictionary d, string key, Color c)
    {
        if (!d.Contains(key)) return;
        if (d[key] is SolidColorBrush b && !b.IsFrozen) b.Color = c;
        else d[key] = new SolidColorBrush(c);
    }

    public static void EnableDarkTitleBars()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is not Window w) return;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int on = IsLight ? 0 : 1;
            int caption = IsLight ? 0x00F0F5F3 : 0x00060505, border = IsLight ? 0x00D4E2DC : 0x00282222; // COLORREF 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));       // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));  // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));   // DWMWA_BORDER_COLOR
        }));
    }

    /// <summary>Lo fija la app según Ajustes ("Reducir movimiento").</summary>
    public static bool ReduceMotion { get; set; }

    /// <summary>Animaciones activas solo si Windows las tiene activadas y el usuario no pidió reducir movimiento.</summary>
    public static bool AnimationsEnabled => !ReduceMotion && SystemParameters.ClientAreaAnimation;

    /// <summary>Entrada suave: fundido + deslizamiento corto hacia arriba.</summary>
    public static void Enter(UIElement el, int delayMs = 0, double offset = 14)
    {
        if (!AnimationsEnabled) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var tt = new TranslateTransform(0, offset);
        el.RenderTransform = tt;
        el.Opacity = 0;
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var dur = new Duration(TimeSpan.FromMilliseconds(380));
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { BeginTime = begin, EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, dur) { BeginTime = begin, EasingFunction = ease });
    }

    /// <summary>Entrada escalonada de los hijos de un panel (máx. 10 elementos animados).</summary>
    public static void Stagger(Panel panel, int stepMs = 45)
    {
        if (!AnimationsEnabled) return;
        for (var i = 0; i < panel.Children.Count; i++)
            Enter(panel.Children[i], Math.Min(i, 10) * stepMs);
    }

    public static TextBlock Eyebrow(string text) => new() { Text = text, Style = Res<Style>("Eyebrow") };
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
        var body = new StackPanel { Margin = new Thickness(48, 44, 48, 40), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(H1(title));
        var sub = Muted(subtitle);
        sub.FontSize = 14;
        sub.Margin = new Thickness(0, 0, 0, 28);
        body.Children.Add(sub);
        // Animación de entrada al terminar de construir la página.
        body.Loaded += (_, _) => Stagger(body);
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
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = IsLight ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x16)) }
    };

    /// <summary>Tarjeta de métrica (título + valor grande + detalle).</summary>
    public static Border Metric(string label, out TextBlock value, out TextBlock detail)
    {
        value = new TextBlock { Text = "—", FontSize = 30, FontWeight = FontWeights.SemiBold, FontFamily = Res<FontFamily>("DisplayFont"), Margin = new Thickness(0, 6, 0, 2) };
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
