using System.Windows;
using System.Windows.Media;

namespace Atoll.Views.Controls;

public sealed class AudioVisualizerControl : FrameworkElement
{
    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands),
        typeof(float[]),
        typeof(AudioVisualizerControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarColorProperty = DependencyProperty.Register(
        nameof(BarColor),
        typeof(Color),
        typeof(AudioVisualizerControl),
        new FrameworkPropertyMetadata(Color.FromRgb(10, 132, 255), FrameworkPropertyMetadataOptions.AffectsRender, OnColorChanged));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(AudioVisualizerControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private Brush _barBrush;

    public float[]? Bands
    {
        get => (float[]?)GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    public Color BarColor
    {
        get => (Color)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public AudioVisualizerControl()
    {
        _barBrush = CreateFrozenBrush(BarColor);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioVisualizerControl ctrl)
        {
            ctrl._barBrush = CreateFrozenBrush(ctrl.BarColor);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 4 || h <= 4) return;

        float[] bands = Bands ?? [0.2f, 0.35f, 0.25f, 0.4f, 0.2f];
        int count = Math.Min(5, bands.Length);
        double barWidth = Math.Max(2.0, (w - (count - 1) * 2.0) / count);
        double radius = barWidth * 0.5;

        for (int i = 0; i < count; i++)
        {
            double level = IsPlaying ? Math.Clamp(bands[i], 0.16, 1.0) : 0.18;
            double barHeight = Math.Max(3.0, h * level);
            double x = i * (barWidth + 2.0);
            double y = (h - barHeight) * 0.5;
            dc.DrawRoundedRectangle(_barBrush, null, new Rect(x, y, barWidth, barHeight), radius, radius);
        }
    }

    private static Brush CreateFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

