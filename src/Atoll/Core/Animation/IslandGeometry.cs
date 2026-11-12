using System.Windows;
using System.Windows.Media;

namespace Atoll.Core.Animation;

/// <summary>
/// Generates continuous-corner squircle geometries inspired by iOS continuous rounded rectangles.
/// </summary>
public static class IslandGeometry
{
    public static StreamGeometry CreateSquircle(double x, double y, double width, double height, double radius)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        if (width <= 0.5 || height <= 0.5)
        {
            geometry.Freeze();
            return geometry;
        }

        double maxRadius = Math.Min(width, height) * 0.5;
        double r = Math.Clamp(radius, 0.0, maxRadius);

        // Continuous corner smoothing extends tangent handle slightly further than circular arc (0.55228)
        double smoothExtend = Math.Min(r * 0.24, Math.Min(width, height) * 0.5 - r);
        double cornerSpan = r + smoothExtend;
        double handle = r * 0.62;

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x + cornerSpan, y), isFilled: true, isClosed: true);

            // Top edge -> Top-right continuous corner
            ctx.LineTo(new Point(x + width - cornerSpan, y), isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(
                new Point(x + width - cornerSpan + handle, y),
                new Point(x + width, y + cornerSpan - handle),
                new Point(x + width, y + cornerSpan),
                isStroked: true,
                isSmoothJoin: true);

            // Right edge -> Bottom-right continuous corner
            ctx.LineTo(new Point(x + width, y + height - cornerSpan), isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(
                new Point(x + width, y + height - cornerSpan + handle),
                new Point(x + width - cornerSpan + handle, y + height),
                new Point(x + width - cornerSpan, y + height),
                isStroked: true,
                isSmoothJoin: true);

            // Bottom edge -> Bottom-left continuous corner
            ctx.LineTo(new Point(x + cornerSpan, y + height), isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(
                new Point(x + cornerSpan - handle, y + height),
                new Point(x, y + height - cornerSpan + handle),
                new Point(x, y + height - cornerSpan),
                isStroked: true,
                isSmoothJoin: true);

            // Left edge -> Top-left continuous corner
            ctx.LineTo(new Point(x, y + cornerSpan), isStroked: true, isSmoothJoin: true);
            ctx.BezierTo(
                new Point(x, y + cornerSpan - handle),
                new Point(x + cornerSpan - handle, y),
                new Point(x + cornerSpan, y),
                isStroked: true,
                isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    public static bool HitTestRoundedRect(Point p, Rect rect, double radius)
    {
        if (!rect.Contains(p)) return false;

        double r = Math.Clamp(radius, 0.0, Math.Min(rect.Width, rect.Height) * 0.5);
        if (r <= 0.5) return true;

        double dx = 0.0;
        double dy = 0.0;

        if (p.X < rect.Left + r) dx = (rect.Left + r) - p.X;
        else if (p.X > rect.Right - r) dx = p.X - (rect.Right - r);

        if (p.Y < rect.Top + r) dy = (rect.Top + r) - p.Y;
        else if (p.Y > rect.Bottom - r) dy = p.Y - (rect.Bottom - r);

        return (dx * dx + dy * dy) <= (r * r);
    }
}

public sealed class SquircleGeometryCache
{
    private const double Tolerance = 0.05;
    private double _x = double.NaN;
    private double _y = double.NaN;
    private double _width = double.NaN;
    private double _height = double.NaN;
    private double _radius = double.NaN;
    private StreamGeometry? _cached;

    public StreamGeometry GetOrCreate(double x, double y, double width, double height, double radius)
    {
        if (_cached is not null &&
            Math.Abs(_x - x) < Tolerance &&
            Math.Abs(_y - y) < Tolerance &&
            Math.Abs(_width - width) < Tolerance &&
            Math.Abs(_height - height) < Tolerance &&
            Math.Abs(_radius - radius) < Tolerance)
        {
            return _cached;
        }

        _x = x;
        _y = y;
        _width = width;
        _height = height;
        _radius = radius;
        _cached = IslandGeometry.CreateSquircle(x, y, width, height, radius);
        return _cached;
    }
}

