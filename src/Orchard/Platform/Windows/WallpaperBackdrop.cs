using System.Runtime.Versioning;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Win32;
using Orchard.Model;
using Orchard.Rendering;

namespace Orchard.Platform.Windows;

/// <summary>
/// Builds the frosted backdrop for every shell surface from the user's actual wallpaper: read the file Windows is
/// showing, lay it out the way Windows does (fill / fit / stretch / span), shrink it and blur it. Solid-colour
/// desktops work too. Cheap enough to redo whenever the wallpaper changes.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WallpaperBackdrop
{
    /// <summary>Identifies the current wallpaper, so the shell can tell when it changes.</summary>
    public static string Signature()
    {
        var path = CurrentPath();
        string style = Desktop("WallpaperStyle") + "/" + Desktop("TileWallpaper");
        string stamp = path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : BackgroundColor().ToString();
        return $"{path}|{style}|{stamp}";
    }

    public static Bitmap? Load(PixelRect monitor)
    {
        try
        {
            // A quarter of the screen is plenty: it is going to be blurred heavily anyway.
            int w = Math.Max(64, monitor.Width / 4), h = Math.Max(36, monitor.Height / 4);
            using var canvas = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            using (var ctx = canvas.CreateDrawingContext())
            {
                ctx.DrawRectangle(new SolidColorBrush(BackgroundColor()), null, new Rect(0, 0, w, h));
                var path = CurrentPath();
                if (path != null && File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    using var image = Bitmap.DecodeToWidth(stream, Math.Max(w * 2, 256), BitmapInterpolationMode.MediumQuality);
                    var src = new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height);
                    ctx.DrawImage(image, Layout(src, new Rect(0, 0, w, h)), new Rect(0, 0, w, h));
                }
            }
            return Blur.Apply(canvas, Math.Max(4, w / 90));
        }
        catch (Exception e)
        {
            Log.Write($"wallpaper backdrop unavailable: {e.Message}");
            return null;
        }
    }

    /// <summary>Which part of the image ends up on screen, following the Personalize ▸ Background "fit" choice.</summary>
    private static Rect Layout(Rect image, Rect screen)
    {
        double imageAspect = image.Width / image.Height, screenAspect = screen.Width / screen.Height;
        switch (Desktop("WallpaperStyle"))
        {
            case "2": // stretch
                return image;
            case "6": // fit: the whole image is visible; approximate by using all of it
                return image;
            default: // fill, span, centre, tile: crop to the screen's shape around the centre
                if (imageAspect > screenAspect)
                {
                    double cw = image.Height * screenAspect;
                    return new Rect((image.Width - cw) / 2, 0, cw, image.Height);
                }
                double ch = image.Width / screenAspect;
                return new Rect(0, (image.Height - ch) / 2, image.Width, ch);
        }
    }

    private static string? CurrentPath()
    {
        var sb = new StringBuilder(1024);
        return Native.SystemParametersInfoString(Native.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0) && sb.Length > 0 ? sb.ToString() : null;
    }

    private static string? Desktop(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return key?.GetValue(name)?.ToString();
    }

    private static Color BackgroundColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
        var parts = (key?.GetValue("Background") as string)?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: 3 } && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b)
            ? Color.FromRgb(r, g, b)
            : Color.FromRgb(0x10, 0x18, 0x30);
    }
}
