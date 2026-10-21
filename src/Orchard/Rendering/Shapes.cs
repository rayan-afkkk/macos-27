using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Orchard.Theme;

namespace Orchard.Rendering;

public static class Shapes
{
    /// <summary>
    /// A superellipse ("squircle") — the continuous-curvature rounded square used for app icons.
    /// </summary>
    public static Geometry Squircle(Rect r, double exponent = 5)
    {
        const int steps = 96;
        var g = new StreamGeometry();
        using var ctx = g.Open();
        double cx = r.Center.X, cy = r.Center.Y, a = r.Width / 2, b = r.Height / 2;
        for (int i = 0; i <= steps; i++)
        {
            double t = i * Math.PI * 2 / steps;
            double c = Math.Cos(t), s = Math.Sin(t);
            double x = cx + a * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / exponent);
            double y = cy + b * Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / exponent);
            if (i == 0) ctx.BeginFigure(new Point(x, y), true);
            else ctx.LineTo(new Point(x, y));
        }
        ctx.EndFigure(true);
        return g;
    }

    public static Geometry Circle(Point c, double radius) => new EllipseGeometry(new Rect(c.X - radius, c.Y - radius, radius * 2, radius * 2));

    public static FormattedText Text(string text, double size, Color color, FontWeight weight = FontWeight.Regular)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Palette.Typeface(weight), size, Palette.Brush(color));

    public static LinearGradientBrush Vertical(Color top, Color bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) }
    };

    public static LinearGradientBrush Diagonal(Color from, Color to) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
    };

    public static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static Color WithAlpha(this Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
}
