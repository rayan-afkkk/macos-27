using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Orchard.Rendering;

/// <summary>
/// Line-art symbols for the menu bar, Control Center and window controls, drawn in a 24×24 box
/// and scaled to whatever rect they are given. Our own drawings, not a copy of any system font.
/// </summary>
public enum Glyph
{
    Logo, Wifi, Battery, Search, ControlCenter, Bluetooth, Moon, Sun, Speaker, Airplane, Mirror,
    Close, Minus, ZoomArrows, Plus, Gear, Chevron, Lock, Power, Play, Grid, Bell
}

public static class Glyphs
{
    public static void Draw(DrawingContext ctx, Glyph glyph, Rect r, Color color, double weight = 1.6)
    {
        double s = Math.Min(r.Width, r.Height) / 24.0;
        var origin = new Point(r.X + (r.Width - 24 * s) / 2, r.Y + (r.Height - 24 * s) / 2);
        Point P(double x, double y) => new(origin.X + x * s, origin.Y + y * s);
        var brush = new ImmutableSolidColorBrush(color);
        var pen = new Pen(brush, weight * s, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        void Line(double x1, double y1, double x2, double y2) => ctx.DrawLine(pen, P(x1, y1), P(x2, y2));
        void Dot(double x, double y, double rad) => ctx.DrawEllipse(brush, null, P(x, y), rad * s, rad * s);
        void Ring(double x, double y, double rad) => ctx.DrawEllipse(null, pen, P(x, y), rad * s, rad * s);
        void Arc(double cx, double cy, double rad, double fromDeg, double toDeg)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double a0 = fromDeg * Math.PI / 180, a1 = toDeg * Math.PI / 180;
                c.BeginFigure(P(cx + rad * Math.Cos(a0), cy + rad * Math.Sin(a0)), false);
                c.ArcTo(P(cx + rad * Math.Cos(a1), cy + rad * Math.Sin(a1)), new Size(rad * s, rad * s), 0,
                    Math.Abs(toDeg - fromDeg) > 180, SweepDirection.Clockwise);
                c.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, g);
        }
        void Poly(bool fill, bool closed, params double[] xy)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(P(xy[0], xy[1]), fill);
                for (int i = 2; i < xy.Length; i += 2) c.LineTo(P(xy[i], xy[i + 1]));
                c.EndFigure(closed);
            }
            ctx.DrawGeometry(fill ? brush : null, fill ? null : pen, g);
        }
        void RoundRect(double x, double y, double w, double h, double rad, bool fill)
            => ctx.DrawRectangle(fill ? brush : null, fill ? null : pen, new RoundedRect(new Rect(P(x, y), new Size(w * s, h * s)), rad * s));

        switch (glyph)
        {
            case Glyph.Logo:
                // Orchard's own mark: a little tree (three-lobed canopy on a trunk) — deliberately nothing like Apple's logo.
                Dot(8, 10.5, 4.6);
                Dot(16, 10.5, 4.6);
                Dot(12, 6.8, 4.9);
                Dot(12, 12, 4.2);
                RoundRect(10.6, 13, 2.8, 8.5, 1.2, true);
                break;
            case Glyph.Wifi:
                Arc(12, 19, 13, 225, 315);
                Arc(12, 19, 8.6, 225, 315);
                Arc(12, 19, 4.4, 225, 315);
                Dot(12, 19, 1.5);
                break;
            case Glyph.Battery:
                RoundRect(2, 7, 18, 10, 3, false);
                RoundRect(4, 9, 11, 6, 1.4, true);
                RoundRect(21, 10.2, 1.6, 3.6, 0.8, true);
                break;
            case Glyph.Search:
                Ring(10.5, 10.5, 6);
                Line(15, 15, 20.5, 20.5);
                break;
            case Glyph.ControlCenter:
                RoundRect(3, 5, 18, 6, 3, false);
                Dot(18, 8, 1.7);
                RoundRect(3, 13, 18, 6, 3, false);
                Dot(6, 16, 1.7);
                break;
            case Glyph.Bluetooth:
                Poly(false, false, 7, 7.5, 17, 16.5, 12, 21, 12, 3, 17, 7.5, 7, 16.5);
                break;
            case Glyph.Moon:
                ctx.DrawGeometry(brush, null, Crescent(P(12, 12), 8 * s));
                break;
            case Glyph.Sun:
                Dot(12, 12, 4);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    Line(12 + 6.5 * Math.Cos(a), 12 + 6.5 * Math.Sin(a), 12 + 9 * Math.Cos(a), 12 + 9 * Math.Sin(a));
                }
                break;
            case Glyph.Speaker:
                Poly(true, true, 3, 9, 7, 9, 12, 4.5, 12, 19.5, 7, 15, 3, 15);
                Arc(12, 12, 4, -50, 50);
                Arc(12, 12, 8, -50, 50);
                break;
            case Glyph.Airplane:
                Poly(true, true, 11, 2.5, 13, 2.5, 13.5, 9.5, 21.5, 14, 21.5, 16, 13.5, 13.5, 13.2, 18.5, 15.8, 20.5, 15.8, 21.8,
                    12, 20.8, 8.2, 21.8, 8.2, 20.5, 10.8, 18.5, 10.5, 13.5, 2.5, 16, 2.5, 14, 10.5, 9.5);
                break;
            case Glyph.Mirror:
                RoundRect(2.5, 4, 19, 13, 2, false);
                Poly(true, true, 8, 21, 16, 21, 12, 15.5);
                break;
            case Glyph.Close:
                Line(7, 7, 17, 17);
                Line(17, 7, 7, 17);
                break;
            case Glyph.Minus:
                Line(5.5, 12, 18.5, 12);
                break;
            case Glyph.ZoomArrows:
                // Two solid triangles pointing to opposite corners — the "zoom" hover glyph.
                Poly(true, true, 3.5, 3.5, 16, 3.5, 3.5, 16);
                Poly(true, true, 20.5, 20.5, 8, 20.5, 20.5, 8);
                break;
            case Glyph.Plus:
                Line(12, 5, 12, 19);
                Line(5, 12, 19, 12);
                break;
            case Glyph.Gear:
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    Line(12 + 6 * Math.Cos(a), 12 + 6 * Math.Sin(a), 12 + 9.5 * Math.Cos(a), 12 + 9.5 * Math.Sin(a));
                }
                Ring(12, 12, 6.2);
                Ring(12, 12, 2.4);
                break;
            case Glyph.Chevron:
                Poly(false, false, 9, 5, 16, 12, 9, 19);
                break;
            case Glyph.Lock:
                RoundRect(5, 11, 14, 10, 2.5, true);
                Arc(12, 11, 4.5, 180, 360);
                Line(7.5, 11, 7.5, 9.5);
                Line(16.5, 11, 16.5, 9.5);
                break;
            case Glyph.Power:
                Arc(12, 13, 7.5, -50, 230);
                Line(12, 3, 12, 11);
                break;
            case Glyph.Play:
                Poly(true, true, 7, 4.5, 19.5, 12, 7, 19.5);
                break;
            case Glyph.Grid:
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    RoundRect(3.5 + x * 6, 3.5 + y * 6, 4.5, 4.5, 1.2, true);
                break;
            case Glyph.Bell:
                Poly(true, true, 12, 3, 16.5, 5.5, 17.5, 11, 19.5, 16.5, 4.5, 16.5, 6.5, 11, 7.5, 5.5);
                Dot(12, 19.5, 2);
                break;
        }
    }

    private static Geometry Crescent(Point c, double r)
    {
        var outer = new EllipseGeometry(new Rect(c.X - r, c.Y - r, r * 2, r * 2));
        var inner = new EllipseGeometry(new Rect(c.X - r * 0.35, c.Y - r * 1.25, r * 2, r * 2));
        return new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
    }
}
