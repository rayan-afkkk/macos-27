using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Orchard.Theme;

namespace Orchard.Rendering;

/// <summary>
/// The translucent "glass" material every shell surface is made of.
/// On Windows the blur comes from the compositor (see <c>ShellWindow.EnableBlur</c>) and this only paints the tint,
/// rim light and outline. In preview mode there is no compositor, so a pre-blurred copy of the wallpaper is
/// painted underneath instead — which is why screenshots look the same as the real thing.
/// </summary>
public static class Glass
{
    /// <summary>Blurred wallpaper in scene coordinates (preview only). Null on a real desktop.</summary>
    public static Bitmap? PreviewBackdrop { get; set; }
    public static double PreviewBackdropScale { get; set; } = 1;

    /// <summary>
    /// Real desktop: a blurred copy of the user's wallpaper covering <see cref="DesktopBackdropMonitor"/> (screen pixels).
    /// Each surface paints the piece of it that lies behind it, so glass looks frosted even though Windows can't blur
    /// behind our shaped windows. Null when the wallpaper can't be read (then the tint is painted opaque instead).
    /// </summary>
    public static Bitmap? DesktopBackdrop { get; set; }

    /// <summary>
    /// "Clear" glass for the dock and menu bar: no blur and almost no tint, so whatever is behind shows through
    /// sharply; the edge highlights alone make it read as a sheet of glass (macOS's clear Liquid Glass look).
    /// </summary>
    public static bool ClearChrome { get; set; } = true;

    /// <summary>The tint the dock / menu bar actually use, for contrast decisions.</summary>
    public static Color ChromeTint => ClearChrome ? Palette.ClearTint : Palette.GlassTint;
    public static PixelRect DesktopBackdropMonitor { get; set; }

    public static void Draw(DrawingContext ctx, Visual owner, RoundedRect shape, bool strong = false, bool shadow = true, bool panel = false, bool chrome = false)
    {
        if (chrome && ClearChrome)
        {
            DrawClear(ctx, shape);
            return;
        }

        // No soft shadow on the real desktop: shell windows clip to their own shapes, so it would barely show, and
        // a large blurred shadow redrawn every animation frame is the single most expensive thing glass can do.
        if (shadow && !Palette.Solid)
        {
            var shadows = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 28, Color = Palette.GlassShadow.WithAlpha(Palette.GlassShadow.A / 255.0 * 0.8) });
            ctx.DrawRectangle(Brushes.Transparent, null, shape, shadows);
        }

        if (DesktopBackdrop is { } wallpaper)
            DrawDesktopBackdrop(ctx, owner, shape, wallpaper);
        else if (PreviewBackdrop is { } backdrop && owner.GetVisualRoot() is Visual root &&
            owner.TranslatePoint(shape.Rect.TopLeft, root) is { } origin)
        {
            using (ctx.PushClip(shape))
            {
                double s = PreviewBackdropScale;
                var src = new Rect(origin.X * s, origin.Y * s, shape.Rect.Width * s, shape.Rect.Height * s)
                    .Intersect(new Rect(0, 0, backdrop.PixelSize.Width, backdrop.PixelSize.Height));
                if (src.Width > 0 && src.Height > 0)
                {
                    var dst = new Rect(shape.Rect.X + (src.X / s - origin.X), shape.Rect.Y + (src.Y / s - origin.Y), src.Width / s, src.Height / s);
                    ctx.DrawImage(backdrop, src, dst);
                }
            }
        }

        var tint = strong ? Palette.GlassTintStrong : panel ? Palette.GlassTintPanel : Palette.GlassTint;
        DrawSurface(ctx, shape, tint);
    }

    private static void DrawDesktopBackdrop(DrawingContext ctx, Visual owner, RoundedRect shape, Bitmap wallpaper)
    {
        if (Avalonia.Controls.TopLevel.GetTopLevel(owner) == null) return;
        var tl = owner.PointToScreen(shape.Rect.TopLeft);
        var br = owner.PointToScreen(shape.Rect.BottomRight);
        var mon = DesktopBackdropMonitor;
        if (br.X <= tl.X || br.Y <= tl.Y || mon.Width <= 0) return;
        double kx = wallpaper.PixelSize.Width / (double)mon.Width, ky = wallpaper.PixelSize.Height / (double)mon.Height;
        var full = new Rect((tl.X - mon.X) * kx, (tl.Y - mon.Y) * ky, (br.X - tl.X) * kx, (br.Y - tl.Y) * ky);
        var src = full.Intersect(new Rect(0, 0, wallpaper.PixelSize.Width, wallpaper.PixelSize.Height));
        if (src.Width <= 0 || src.Height <= 0) return;
        // Map the visible part of the source back onto the shape (surfaces sliding off-screen are partly outside).
        double sx = shape.Rect.Width / full.Width, sy = shape.Rect.Height / full.Height;
        var dst = new Rect(shape.Rect.X + (src.X - full.X) * sx, shape.Rect.Y + (src.Y - full.Y) * sy, src.Width * sx, src.Height * sy);
        using (ctx.PushClip(shape))
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality }))
            ctx.DrawImage(wallpaper, src, dst);
    }

    /// <summary>
    /// How bright the glass at <paramref name="rect"/> (in <paramref name="owner"/>'s coordinates) will look: the blurred
    /// backdrop's average luminance with the tint on top, 0–1. Null when there is no backdrop to sample. Lets the
    /// translucent menu bar pick dark or white text the way macOS does over light or dark wallpapers.
    /// </summary>
    public static unsafe double? PerceivedLuminance(Visual owner, Rect rect, Color tint)
    {
        Bitmap? bitmap = null;
        Rect src = default;
        if (DesktopBackdrop is { } wallpaper && Avalonia.Controls.TopLevel.GetTopLevel(owner) != null)
        {
            var tl = owner.PointToScreen(rect.TopLeft);
            var br = owner.PointToScreen(rect.BottomRight);
            var mon = DesktopBackdropMonitor;
            if (mon.Width <= 0) return null;
            double kx = wallpaper.PixelSize.Width / (double)mon.Width, ky = wallpaper.PixelSize.Height / (double)mon.Height;
            src = new Rect((tl.X - mon.X) * kx, (tl.Y - mon.Y) * ky, (br.X - tl.X) * kx, (br.Y - tl.Y) * ky);
            bitmap = wallpaper;
        }
        else if (PreviewBackdrop is { } preview && owner.GetVisualRoot() is Visual root && owner.TranslatePoint(rect.TopLeft, root) is { } o)
        {
            double s = PreviewBackdropScale;
            src = new Rect(o.X * s, o.Y * s, rect.Width * s, rect.Height * s);
            bitmap = preview;
        }
        if (bitmap == null) return null;

        var px = new PixelRect((int)src.X, (int)src.Y, Math.Max(1, (int)src.Width), Math.Max(1, (int)src.Height))
            .Intersect(new PixelRect(bitmap.PixelSize));
        if (px.Width <= 0 || px.Height <= 0) return null;
        var buffer = new byte[px.Width * px.Height * 4];
        fixed (byte* p = buffer) bitmap.CopyPixels(px, (nint)p, buffer.Length, px.Width * 4);
        double sum = 0;
        for (int i = 0; i < buffer.Length; i += 4)
            sum += (0.0722 * buffer[i] + 0.7152 * buffer[i + 1] + 0.2126 * buffer[i + 2]) / 255; // BGRA
        double wall = sum / (buffer.Length / 4);
        double a = tint.A / 255.0, tintLum = (0.2126 * tint.R + 0.7152 * tint.G + 0.0722 * tint.B) / 255;
        return wall * (1 - a) + tintLum * a;
    }

    private static void DrawClear(DrawingContext ctx, RoundedRect shape)
    {
        ctx.DrawRectangle(Palette.Brush(Palette.ClearTint), null, shape);
        var r = shape.Rect;
        using (ctx.PushClip(shape))
        {
            // A light sheen across the top third, like light catching the upper surface.
            ctx.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(Palette.Dark ? (byte)0x18 : (byte)0x2A, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.4),
                },
            }, null, r);
            // Soft brightening just inside the edge — the "thick glass" refraction cue.
            ctx.DrawRectangle(null, new Pen(Palette.Brush(Color.FromArgb(Palette.Dark ? (byte)0x10 : (byte)0x1C, 255, 255, 255)), 5), shape.Deflate(2.5, 2.5));
        }
        // Thin dark outline for definition on bright backgrounds, then a specular rim: bright at the top-left and
        // bottom-right, fading in between, as light wraps around a rounded glass edge.
        ctx.DrawRectangle(null, new Pen(Palette.Brush(Color.FromArgb(Palette.Dark ? (byte)0x70 : (byte)0x26, 0, 0, 0)), 0.75), shape.Inflate(0.4, 0.4));
        var rim = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(Palette.Dark ? (byte)0x90 : (byte)0xF0, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(Palette.Dark ? (byte)0x18 : (byte)0x40, 255, 255, 255), 0.45),
                new GradientStop(Color.FromArgb(Palette.Dark ? (byte)0x18 : (byte)0x40, 255, 255, 255), 0.7),
                new GradientStop(Color.FromArgb(Palette.Dark ? (byte)0x60 : (byte)0xB0, 255, 255, 255), 1),
            },
        };
        ctx.DrawRectangle(null, new Pen(rim, 1.2), shape.Deflate(0.6, 0.6));
    }

    private static void DrawSurface(DrawingContext ctx, RoundedRect shape, Color tint)
    {
        ctx.DrawRectangle(Palette.Brush(tint), null, shape);

        // Specular sheen: brighter along the top, the cue that makes it read as glass rather than frosted plastic.
        using (ctx.PushClip(shape))
        {
            var sheen = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(Palette.Dark ? (byte)0x14 : (byte)0x30, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.5),
                },
            };
            ctx.DrawRectangle(sheen, null, shape.Rect);
        }

        ctx.DrawRectangle(null, new Pen(Palette.Brush(Palette.GlassOutline), 0.75), shape.Inflate(0.4, 0.4));
        ctx.DrawRectangle(null, new Pen(Palette.RimBrush(), 1), shape.Deflate(0.6, 0.6));
    }
}
