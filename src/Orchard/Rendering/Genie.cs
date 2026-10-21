using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Orchard.Services;

namespace Orchard.Rendering;

/// <summary>
/// The genie effect: a window snapshot is cut into thin horizontal strips; the strips nearest the dock pinch toward
/// the icon's width first, then the whole shape pours down into the icon. Played backwards for restore.
/// </summary>
public static class Genie
{
    public const int Strips = 260;

    /// <param name="progress">0 = window at full size, 1 = fully inside the dock icon.</param>
    public static void Draw(DrawingContext ctx, IImage image, Size imageSize, Rect from, Rect to, double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        // Phase A pinches the sides toward the target, phase B slides everything down; they overlap for a fluid motion.
        double pinch = DockMath.EaseInOut(Math.Clamp(progress / 0.55, 0, 1));
        double slide = DockMath.EaseInOut(Math.Clamp((progress - 0.3) / 0.7, 0, 1));

        double top = Lerp(from.Top, to.Top, slide);
        double bottom = Lerp(from.Bottom, to.Bottom, Math.Max(slide, pinch * 0.15));
        double span = Math.Max(bottom - top, 0.5);
        double funnel = Math.Max(1, to.Top - from.Top);

        using var _ = ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality });
        for (int i = 0; i < Strips; i++)
        {
            double v0 = i / (double)Strips, v1 = (i + 1) / (double)Strips;
            double y0 = top + span * v0, y1 = top + span * v1;
            // How far down the funnel this strip sits decides how pinched it is (an S-curve between window and icon).
            double depth = Math.Clamp((y0 - from.Top) / funnel, 0, 1);
            double curve = depth * depth * depth * (depth * (depth * 6 - 15) + 10); // smootherstep: the funnel's S-shaped sides
            double squeeze = Math.Clamp(curve * pinch + slide * (1 - curve), 0, 1);
            double left = Lerp(from.Left, to.Left, squeeze);
            double right = Lerp(from.Right, to.Right, squeeze);
            if (right - left < 0.5) continue;
            var src = new Rect(0, imageSize.Height * v0, imageSize.Width, imageSize.Height * (v1 - v0) + 0.5);
            ctx.DrawImage(image, src, new Rect(left, y0, right - left, y1 - y0 + 0.6));
        }
    }

    /// <summary>The simpler "scale" effect: the window shrinks straight into the icon.</summary>
    public static void DrawScale(DrawingContext ctx, IImage image, Size imageSize, Rect from, Rect to, double progress)
    {
        double t = DockMath.EaseInOut(Math.Clamp(progress, 0, 1));
        var r = new Rect(Lerp(from.X, to.X, t), Lerp(from.Y, to.Y, t), Lerp(from.Width, to.Width, t), Lerp(from.Height, to.Height, t));
        using (ctx.PushOpacity(1 - t * 0.4))
            ctx.DrawImage(image, new Rect(imageSize), r);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>Hosts one genie / scale animation.</summary>
public sealed class GenieView : Control
{
    public IImage? Image { get; set; }
    public Size ImageSize { get; set; }
    public Rect From { get; set; }
    public Rect To { get; set; }
    public double Progress { get; set; }
    public bool UseScale { get; set; }

    public override void Render(DrawingContext ctx)
    {
        if (Image == null) return;
        if (UseScale) Genie.DrawScale(ctx, Image, ImageSize, From, To, Progress);
        else Genie.Draw(ctx, Image, ImageSize, From, To, Progress);
    }
}
