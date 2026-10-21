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
    public static PixelRect DesktopBackdropMonitor { get; set; }

    public static void Draw(DrawingContext ctx, Visual owner, RoundedRect shape, bool strong = false, bool shadow = true)
    {
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

        var tint = strong ? Palette.GlassTintStrong : Palette.GlassTint;
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
