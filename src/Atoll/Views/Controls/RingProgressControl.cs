using System.Windows;
using System.Windows.Media;

namespace Atoll.Views.Controls;

public sealed class RingProgressControl : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(RingProgressControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingColorProperty = DependencyProperty.Register(
        nameof(RingColor),
        typeof(Color),
        typeof(RingProgressControl),
        new FrameworkPropertyMetadata(Color.FromRgb(48, 209, 88), FrameworkPropertyMetadataOptions.AffectsRender, OnBrushPropertyChanged));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(RingProgressControl),
        new FrameworkPropertyMetadata(2.5, FrameworkPropertyMetadataOptions.AffectsRender, OnBrushPropertyChanged));

    private Pen _trackPen;
    private Pen _accentPen;

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Color RingColor
    {
        get => (Color)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public RingProgressControl()
    {
        _trackPen = CreateFrozenPen(Color.FromArgb(48, 255, 255, 255), StrokeThickness);
        _accentPen = CreateFrozenPen(RingColor, StrokeThickness);
    }

    private static void OnBrushPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RingProgressControl ctrl)
        {
            ctrl._trackPen = CreateFrozenPen(Color.FromArgb(48, 255, 255, 255), ctrl.StrokeThickness);
            ctrl._accentPen = CreateFrozenPen(ctrl.RingColor, ctrl.StrokeThickness);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        double radius = (Math.Min(w, h) - StrokeThickness) * 0.5;
        if (radius <= 1) return;

        var center = new Point(w * 0.5, h * 0.5);
        dc.DrawEllipse(null, _trackPen, center, radius, radius);

        double p = Math.Clamp(Progress, 0.0, 1.0);
        if (p <= 0.005) return;

        if (p >= 0.995)
        {
            dc.DrawEllipse(null, _accentPen, center, radius, radius);
            return;
        }

        double startAngle = -Math.PI * 0.5;
        double sweepAngle = p * Math.PI * 2.0;
        double endAngle = startAngle + sweepAngle;

        var startPt = new Point(
            center.X + radius * Math.Cos(startAngle),
            center.Y + radius * Math.Sin(startAngle));
        var endPt = new Point(
            center.X + radius * Math.Cos(endAngle),
            center.Y + radius * Math.Sin(endAngle));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(startPt, isFilled: false, isClosed: false);
            ctx.ArcTo(
                endPt,
                new Size(radius, radius),
                0,
                sweepAngle > Math.PI,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, _accentPen, geo);
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }
}

