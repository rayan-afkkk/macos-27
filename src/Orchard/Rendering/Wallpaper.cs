using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Orchard.Rendering;

public enum WallpaperTheme { Dawn, Night, Sunset, Mint }

/// <summary>An original procedural wallpaper: layered translucent waves over a soft gradient with coloured glows.</summary>
public sealed class WallpaperArt
{
    private sealed record Scheme(Color Top, Color Bottom, (double X, double Y, double R, Color C)[] Glows, Color[] Waves, byte RimAlpha);

    /// <summary>Kept for existing callers: Dark selects <see cref="WallpaperTheme.Night"/>, otherwise <see cref="WallpaperTheme.Dawn"/>.</summary>
    public bool Dark { get => Theme == WallpaperTheme.Night; init => Theme = value ? WallpaperTheme.Night : WallpaperTheme.Dawn; }
    public WallpaperTheme Theme { get; init; } = WallpaperTheme.Dawn;

    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    private Scheme Colors => Theme switch
    {
        WallpaperTheme.Night => new(Color.FromRgb(0x05, 0x0B, 0x24), Color.FromRgb(0x10, 0x2A, 0x5A),
            [(0.18, 0.22, 0.55, C(0x90, 0x5B, 0x3C, 0xE8)), (0.85, 0.30, 0.50, C(0x80, 0x00, 0x8B, 0xC8)), (0.55, 0.95, 0.60, C(0x70, 0x1D, 0x5F, 0xD8))],
            [C(0x60, 0x2E, 0x5B, 0xFF), C(0x50, 0x0F, 0xA3, 0xD8), C(0x58, 0x5B, 0x3C, 0xE8), C(0x40, 0x9F, 0xD8, 0xFF)], 0x40),
        WallpaperTheme.Sunset => new(Color.FromRgb(0x2B, 0x14, 0x5E), Color.FromRgb(0xFF, 0x9A, 0x6B),
            [(0.20, 0.25, 0.55, C(0x90, 0xB0, 0x3C, 0xE0)), (0.85, 0.35, 0.50, C(0x90, 0xFF, 0x5E, 0x8A)), (0.50, 0.95, 0.65, C(0xA0, 0xFF, 0xC2, 0x6B))],
            [C(0x70, 0x8E, 0x2D, 0xD8), C(0x60, 0xFF, 0x4F, 0x87), C(0x60, 0xFF, 0x8A, 0x4F), C(0x80, 0xFF, 0xD9, 0xA8)], 0x50),
        WallpaperTheme.Mint => new(Color.FromRgb(0x0B, 0x5E, 0x5A), Color.FromRgb(0xB8, 0xF5, 0xD8),
            [(0.18, 0.22, 0.55, C(0x90, 0x16, 0xB8, 0x9C)), (0.85, 0.30, 0.50, C(0x80, 0x3C, 0x9E, 0xF0)), (0.55, 0.95, 0.60, C(0x90, 0xFF, 0xFF, 0xFF))],
            [C(0x70, 0x0E, 0x8A, 0x7A), C(0x60, 0x2C, 0xC8, 0xA8), C(0x58, 0x3C, 0x9E, 0xF0), C(0x80, 0xE6, 0xFF, 0xF2)], 0x50),
        _ => new(Color.FromRgb(0x2C, 0x6B, 0xE6), Color.FromRgb(0x8E, 0xD6, 0xF5),
            [(0.18, 0.22, 0.55, C(0xA0, 0x7B, 0x5C, 0xFF)), (0.85, 0.30, 0.50, C(0x90, 0x3D, 0xE0, 0xF2)), (0.55, 0.95, 0.60, C(0x90, 0xFF, 0xFF, 0xFF))],
            [C(0x70, 0x1F, 0x4F, 0xD8), C(0x60, 0x2A, 0xB5, 0xF0), C(0x58, 0x6A, 0x4C, 0xF5), C(0x80, 0xE9, 0xF7, 0xFF)], 0x40),
    };

    public void Draw(DrawingContext ctx, Rect r)
    {
        var scheme = Colors;
        ctx.DrawRectangle(Shapes.Vertical(scheme.Top, scheme.Bottom), null, r);
        foreach (var (x, y, radius, color) in scheme.Glows) Glow(ctx, r, x, y, radius, color);

        var waves = scheme.Waves;
        // Keep the rim of each wave the same visual weight at any resolution.
        var rim = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(scheme.RimAlpha, 255, 255, 255)), Math.Max(1.2, r.Width / 1200));
        for (int i = 0; i < waves.Length; i++)
        {
            double baseY = 0.38 + i * 0.13, amp = 0.09 + i * 0.015, phase = i * 1.3;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(r.X, r.Bottom), true);
                const int n = 8;
                c.LineTo(new Point(r.X, r.Y + r.Height * (baseY + amp * Math.Sin(phase))));
                for (int k = 1; k <= n; k++)
                {
                    double x0 = (k - 1) / (double)n, x1 = k / (double)n;
                    double y0 = baseY + amp * Math.Sin(phase + x0 * Math.PI * 2.2);
                    double y1 = baseY + amp * Math.Sin(phase + x1 * Math.PI * 2.2);
                    double xm = (x0 + x1) / 2;
                    c.CubicBezierTo(new Point(r.X + r.Width * xm, r.Y + r.Height * y0), new Point(r.X + r.Width * xm, r.Y + r.Height * y1),
                        new Point(r.X + r.Width * x1, r.Y + r.Height * y1));
                }
                c.LineTo(new Point(r.Right, r.Bottom));
                c.EndFigure(true);
            }
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(waves[i], 0), new GradientStop(Shapes.WithAlpha(waves[i], waves[i].A / 255.0 * 0.25), 1) },
            };
            ctx.DrawGeometry(fill, rim, g);
        }
    }

    private static void Glow(DrawingContext ctx, Rect r, double cx, double cy, double radius, Color color)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = RelativeScalar.Parse("50%"),
            RadiusY = RelativeScalar.Parse("50%"),
            GradientStops = { new GradientStop(color, 0), new GradientStop(Shapes.WithAlpha(color, 0), 1) },
        };
        double rad = Math.Max(r.Width, r.Height) * radius;
        ctx.DrawEllipse(brush, null, new Point(r.X + r.Width * cx, r.Y + r.Height * cy), rad, rad * 0.8);
    }

    /// <summary>Rasterises the wallpaper; used for the desktop picture and for the preview glass backdrop.</summary>
    public RenderTargetBitmap Render(PixelSize size, double scale = 1)
    {
        var rtb = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        using (var ctx = rtb.CreateDrawingContext())
            Draw(ctx, new Rect(0, 0, size.Width / scale, size.Height / scale));
        return rtb;
    }
}

public static class Blur
{
    /// <summary>Three-pass box blur (≈ gaussian) over a BGRA bitmap. Only used to fake compositor blur in preview mode.</summary>
    public static unsafe WriteableBitmap Apply(Bitmap source, int radius)
    {
        var size = source.PixelSize;
        int w = size.Width, h = size.Height;
        var a = new byte[w * h * 4];
        fixed (byte* p = a) source.CopyPixels(new PixelRect(size), (nint)p, a.Length, w * 4);
        var b = new byte[a.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            Horizontal(a, b, w, h, radius);
            Horizontal(Transpose(b, w, h), a, h, w, radius);
            a = Transpose(a, h, w);
        }
        var result = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = result.Lock();
        for (int y = 0; y < h; y++)
            System.Runtime.InteropServices.Marshal.Copy(a, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        return result;
    }

    private static void Horizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        int window = r * 2 + 1;
        Parallel.For(0, h, y =>
        {
            int row = y * w * 4;
            for (int ch = 0; ch < 4; ch++)
            {
                int sum = 0;
                for (int i = -r; i <= r; i++) sum += src[row + Math.Clamp(i, 0, w - 1) * 4 + ch];
                for (int x = 0; x < w; x++)
                {
                    dst[row + x * 4 + ch] = (byte)(sum / window);
                    sum += src[row + Math.Min(x + r + 1, w - 1) * 4 + ch] - src[row + Math.Max(x - r, 0) * 4 + ch];
                }
            }
        });
    }

    private static byte[] Transpose(byte[] src, int w, int h)
    {
        var dst = new byte[src.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int s = (y * w + x) * 4, d = (x * h + y) * 4;
            dst[d] = src[s]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; dst[d + 3] = src[s + 3];
        }
        return dst;
    }
}
