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

    public static void Draw(DrawingContext ctx, Visual owner, RoundedRect shape, bool strong = false, bool shadow = true)
    {
        // No soft shadow on the real desktop: shell windows clip to their own shapes, so it would barely show, and
        // a large blurred shadow redrawn every animation frame is the single most expensive thing glass can do.
        if (shadow && !Palette.Solid)
        {
            var shadows = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 28, Color = Palette.GlassShadow.WithAlpha(Palette.GlassShadow.A / 255.0 * 0.8) });
            ctx.DrawRectangle(Brushes.Transparent, null, shape, shadows);
        }

        if (PreviewBackdrop is { } backdrop && owner.GetVisualRoot() is Visual root &&
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
