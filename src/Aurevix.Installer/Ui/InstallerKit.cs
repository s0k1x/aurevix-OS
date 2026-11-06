using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Aurevix.ControlCenter.Views;
using Aurevix.Installer.Engine;

namespace Aurevix.Installer.Controls;

/// <summary>Barra de progreso fina y redondeada, con animación suave y modo indeterminado.</summary>
public sealed class ThinProgress : Grid
{
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3), Background = Ui.Brush("Accent"), Width = 0 };
    private readonly TranslateTransform _slide = new();
    private double _value;
    private bool _indeterminate;

    public ThinProgress()
    {
        Height = 6;
        ClipToBounds = true;
        Children.Add(new Border { Background = Ui.Brush("SurfaceAlt"), CornerRadius = new CornerRadius(3) });
        Children.Add(_fill);
        SizeChanged += (_, _) => { if (_indeterminate) StartSlide(); else Apply(false); };
    }

    public void SetValue(double v)
    {
        _value = Math.Clamp(v, 0, 1);
        if (!_indeterminate) Apply(true);
    }

    public void SetIndeterminate(bool on)
    {
        if (_indeterminate == on) return;
        _indeterminate = on;
        if (on) StartSlide();
        else { _fill.RenderTransform = null; _slide.BeginAnimation(TranslateTransform.XProperty, null); Apply(false); }
    }

    private void Apply(bool animate)
    {
        var target = ActualWidth * _value;
        if (animate && Ui.AnimationsEnabled)
            _fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { _fill.BeginAnimation(WidthProperty, null); _fill.Width = target; }
    }

    private void StartSlide()
    {
        if (ActualWidth <= 0) return;
        _fill.BeginAnimation(WidthProperty, null);
        _fill.Width = ActualWidth * 0.28;
        _fill.RenderTransform = _slide;
        if (!Ui.AnimationsEnabled) return;
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-ActualWidth * 0.3, ActualWidth, TimeSpan.FromSeconds(1.5))
        { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }
}

public sealed record StageSpec(string Label, double DefaultSeconds);

/// <summary>
/// Pantalla de progreso: porcentaje grande, tiempo restante aproximado, barra fina y lista de pasos
/// (hecho · en curso · pendiente). El tiempo se calcula con la velocidad real de los últimos segundos.
/// </summary>
public sealed class ProgressView
{
    private readonly StageSpec[] _stages;
    private readonly TextBlock _percent = new() { FontFamily = Ui.Res<FontFamily>("DisplayFont"), FontSize = 76, FontWeight = FontWeights.Light, Text = "0 %" };
    private readonly TextBlock _eta = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, Text = "Calculando el tiempo restante…", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = Ui.Muted("Preparando…");
    private readonly ThinProgress _bar = new();
    private readonly ContentControl[] _icons;
    private readonly TextBlock[] _labels;
    private readonly TextBlock[] _details;
    private readonly ProgressMeter _meter = new();
    private int _stage = -1;
    private double _stageFraction;
    private bool _hasFraction;

    public FrameworkElement Root { get; }
    public IProgress<Step> Reporter { get; }

    public ProgressView(IReadOnlyList<StageSpec> stages)
    {
        _stages = stages.ToArray();
        _icons = new ContentControl[_stages.Length];
        _labels = new TextBlock[_stages.Length];
        _details = new TextBlock[_stages.Length];
        Reporter = new Progress<Step>(OnStep);

        _percent.Margin = new Thickness(0, 0, 0, 0);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 14), Children = { _eta, _status } };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        right.Margin = new Thickness(28, 0, 0, 14);
        Grid.SetColumn(right, 1);
        top.Children.Add(_percent);
        top.Children.Add(right);
        _bar.Margin = new Thickness(0, 10, 0, 30);
        _bar.SetIndeterminate(true);

        var list = new StackPanel();
        for (var i = 0; i < _stages.Length; i++)
        {
            _icons[i] = new ContentControl { Width = 26, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            _labels[i] = new TextBlock { Text = _stages[i].Label, FontSize = 15 };
            _details[i] = Ui.Muted("");
            _details[i].FontSize = 12;
            _details[i].Visibility = Visibility.Collapsed;
            var texts = new StackPanel { Children = { _labels[i], _details[i] } };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(_icons[i], Dock.Left);
            row.Children.Add(_icons[i]);
            row.Children.Add(texts);
            list.Children.Add(row);
            Style(i, Phase.Pending);
        }
        Root = new StackPanel { Children = { top, _bar, list } };
    }

    private enum Phase { Pending, Active, Done }

    private void Style(int i, Phase phase)
    {
        switch (phase)
        {
            case Phase.Done:
                _icons[i].Content = new TextBlock { Text = "", FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 13, Foreground = Ui.Brush("Accent") };
                _labels[i].Foreground = Ui.Brush("TextMuted");
                _labels[i].FontWeight = FontWeights.Normal;
                _details[i].Visibility = Visibility.Collapsed;
                break;
            case Phase.Active:
                var dot = new Ellipse { Width = 9, Height = 9, Fill = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 5, 0, 0) };
                if (Ui.AnimationsEnabled)
                    dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                _icons[i].Content = dot;
                _labels[i].Foreground = Ui.Brush("Text");
                _labels[i].FontWeight = FontWeights.SemiBold;
                _details[i].Visibility = Visibility.Visible;
                break;
            default:
                _icons[i].Content = new Ellipse { Width = 9, Height = 9, Stroke = Ui.Brush("Unavailable"), StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 5, 0, 0) };
                _labels[i].Foreground = Ui.Brush("Unavailable");
                _labels[i].FontWeight = FontWeights.Normal;
                _details[i].Visibility = Visibility.Collapsed;
                break;
        }
    }

    /// <summary>Pasa al paso <paramref name="index"/> (0 = primero). Los anteriores quedan marcados como hechos.</summary>
    public void SetStage(int index)
    {
        for (var i = 0; i < _stages.Length; i++) Style(i, i < index ? Phase.Done : i == index ? Phase.Active : Phase.Pending);
        _stage = index;
        _stageFraction = 0;
        _hasFraction = false;
        _meter.Reset();
        _details[Math.Clamp(index, 0, _stages.Length - 1)].Text = "";
        Refresh();
    }

    /// <summary>Marca todo como terminado.</summary>
    public void Complete()
    {
        for (var i = 0; i < _stages.Length; i++) Style(i, Phase.Done);
        _stage = _stages.Length;
        _percent.Text = "100 %";
        _bar.SetIndeterminate(false);
        _bar.SetValue(1);
    }

    private void OnStep(Step s)
    {
        if (_stage < 0 || _stage >= _stages.Length) return;
        if (s.Fraction is { } f)
        {
            _stageFraction = Math.Clamp(f, 0, 1);
            _hasFraction = true;
            _meter.Add(_stageFraction);
        }
        var line = string.IsNullOrWhiteSpace(s.Detail) ? s.Text : s.Detail;
        _details[_stage].Text = line;
        _status.Text = s.Text.TrimEnd('…', '.');
        Refresh();
    }

    private double Overall()
    {
        double total = _stages.Sum(x => x.DefaultSeconds), done = 0;
        for (var i = 0; i < _stages.Length; i++)
        {
            if (i < _stage) done += _stages[i].DefaultSeconds;
            else if (i == _stage) done += _stages[i].DefaultSeconds * _stageFraction;
        }
        return total <= 0 ? 0 : done / total;
    }

    private void Refresh()
    {
        var overall = Overall();
        _percent.Text = $"{(int)Math.Round(overall * 100)} %";
        _bar.SetIndeterminate(!_hasFraction && _stage >= 0 && _stageFraction == 0 && overall < 0.001);
        if (_hasFraction || overall > 0) _bar.SetValue(overall);

        if (_stage >= _stages.Length - 1 && _stageFraction >= 0.995) { _eta.Text = "Terminando…"; return; }
        var rem = _meter.Remaining();
        if (rem is null) { _eta.Text = _hasFraction ? "Calculando el tiempo restante…" : "Un momento…"; return; }
        // Resto del paso actual (medido) + duración típica de los pasos que quedan.
        var later = _stages.Skip(Math.Max(0, _stage + 1)).Sum(x => x.DefaultSeconds);
        _eta.Text = "Quedan " + Eta.Format(rem.Value + TimeSpan.FromSeconds(later));
    }
}

/// <summary>Grupo de opciones tipo «tarjeta» (una sola elegible), más claras y modernas que los botones de radio.</summary>
public sealed class ChoiceGroup
{
    private readonly List<Border> _tiles = [];
    public int Selected { get; private set; } = -1;
    public event Action<int>? Changed;

    public Border Add(string title, string? text = null, string? badgeText = null)
    {
        var index = _tiles.Count;
        var texts = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold } } };
        if (!string.IsNullOrEmpty(text)) { var m = Ui.Muted(text); m.Margin = new Thickness(0, 3, 0, 0); texts.Children.Add(m); }
        if (badgeText is not null)
        {
            var pill = Ui.Pill(badgeText, Ui.Brush("Accent"));
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            pill.Margin = new Thickness(0, 8, 0, 0);
            texts.Children.Add(pill);
        }
        var outer = new Ellipse { Width = 18, Height = 18, StrokeThickness = 1.5, Stroke = Ui.Brush("Unavailable") };
        var inner = new Ellipse { Width = 8, Height = 8, Fill = Ui.Brush("Accent"), Visibility = Visibility.Collapsed };
        var radio = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 14, 0), Children = { outer, inner } };
        var row = new DockPanel();
        DockPanel.SetDock(radio, Dock.Left);
        row.Children.Add(radio);
        row.Children.Add(texts);
        var tile = new Border
        {
            Child = row, Padding = new Thickness(18, 15, 18, 15), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            BorderBrush = Ui.Brush("BorderBrushAx"), Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0E, 0x0E, 0x11)),
            Cursor = Cursors.Hand, Focusable = true, Margin = new Thickness(0, 0, 0, 10), Tag = (outer, inner), FocusVisualStyle = null
        };
        tile.MouseLeftButtonUp += (_, _) => Select(index);
        tile.KeyUp += (_, e) => { if (e.Key is Key.Enter or Key.Space) Select(index); };
        tile.MouseEnter += (_, _) => { if (Selected != index) tile.BorderBrush = Ui.Brush("Unavailable"); };
        tile.MouseLeave += (_, _) => { if (Selected != index) tile.BorderBrush = Ui.Brush("BorderBrushAx"); };
        _tiles.Add(tile);
        return tile;
    }

    public void Clear() { _tiles.Clear(); Selected = -1; }

    public void Select(int index, bool raise = true)
    {
        if (index < 0 || index >= _tiles.Count) return;
        Selected = index;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var on = i == index;
            var (outer, inner) = ((Ellipse, Ellipse))_tiles[i].Tag;
            _tiles[i].BorderBrush = on ? Ui.Brush("Accent") : Ui.Brush("BorderBrushAx");
            _tiles[i].Background = new SolidColorBrush(on ? Color.FromArgb(0x18, 0x8D, 0xE0, 0x3C) : Color.FromArgb(0xE6, 0x0E, 0x0E, 0x11));
            outer.Stroke = on ? Ui.Brush("Accent") : Ui.Brush("Unavailable");
            inner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
        if (raise) Changed?.Invoke(index);
    }
}

public static class Kit
{
    /// <summary>Cambia el texto secundario de una tarjeta creada con <see cref="ChoiceGroup.Add"/>.</summary>
    public static void SetTileText(Border tile, string text)
    {
        if (tile.Child is DockPanel { Children.Count: > 1 } dp && dp.Children[1] is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
            tb.Text = text;
    }

    public static FrameworkElement Glyph(string glyph, double size = 18, Brush? color = null) => new TextBlock
    {
        Text = glyph, FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = size, Foreground = color ?? Ui.Brush("Accent"),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>Tarjeta de acción principal de la pantalla de inicio: icono, textos y flecha que se desliza al pasar el ratón.</summary>
    public static Border OptionCard(string glyph, string eyebrow, string title, string text, Action onClick)
    {
        var icon = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(14), Background = Ui.Brush("SurfaceAlt"), BorderBrush = Ui.Brush("BorderBrushAx"), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 20, 0), Child = Glyph(glyph, 20)
        };
        var eb = Ui.Eyebrow(eyebrow); eb.Margin = new Thickness(0, 0, 0, 3);
        var h = Ui.H2(title); h.FontSize = 19; h.Margin = new Thickness(0, 0, 0, 4);
        var muted = Ui.Muted(text);
        var arrow = new TextBlock { Text = "", FontFamily = Ui.Res<FontFamily>("IconFont"), FontSize = 14, Foreground = Ui.Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), RenderTransform = new TranslateTransform() };
        var texts = new StackPanel { Children = { eb, h, muted } };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 1); Grid.SetColumn(arrow, 2);
        grid.Children.Add(icon); grid.Children.Add(texts); grid.Children.Add(arrow);

        var card = new Border
        {
            Child = grid, Padding = new Thickness(24, 22, 24, 22), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            BorderBrush = Ui.Brush("BorderBrushAx"), Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0E, 0x0E, 0x11)),
            Cursor = Cursors.Hand, Focusable = true, Margin = new Thickness(0, 0, 0, 14), FocusVisualStyle = null
        };
        card.MouseLeftButtonUp += (_, _) => onClick();
        card.KeyUp += (_, e) => { if (e.Key is Key.Enter or Key.Space) onClick(); };
        void Hover(bool on)
        {
            card.BorderBrush = on ? Ui.Brush("Accent") : Ui.Brush("BorderBrushAx");
            arrow.Foreground = on ? Ui.Brush("Accent") : Ui.Brush("TextMuted");
            var tt = (TranslateTransform)arrow.RenderTransform;
            if (Ui.AnimationsEnabled) tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 6 : 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else tt.X = on ? 6 : 0;
        }
        card.MouseEnter += (_, _) => Hover(true);
        card.MouseLeave += (_, _) => Hover(false);
        card.GotKeyboardFocus += (_, _) => Hover(true);
        card.LostKeyboardFocus += (_, _) => Hover(false);
        return card;
    }

    /// <summary>Círculo grande con icono (resultado correcto o aviso).</summary>
    public static FrameworkElement ResultBadge(bool ok)
    {
        var brush = ok ? Ui.Brush("Accent") : Ui.Brush("Review");
        var ring = new Border
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(36), BorderThickness = new Thickness(2), BorderBrush = brush,
            Background = new SolidColorBrush(ok ? Color.FromArgb(0x1C, 0x8D, 0xE0, 0x3C) : Color.FromArgb(0x1C, 0xE8, 0xA3, 0x3D)),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 22),
            Child = Glyph(ok ? "" : "", 28, brush), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1)
        };
        if (Ui.AnimationsEnabled)
        {
            var st = (ScaleTransform)ring.RenderTransform;
            var a = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        return ring;
    }
}
