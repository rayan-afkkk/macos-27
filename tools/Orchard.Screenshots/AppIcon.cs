using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Orchard.Rendering;

/// <summary>Renders Orchard's app icon (squircle + tree mark) at every Windows icon size and packs them into an .ico.</summary>
internal static class AppIcon
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    public static void Write(string icoPath, string pngPath)
    {
        var images = Sizes.Select(size => (size, png: Render(size))).ToList();
        File.WriteAllBytes(pngPath, images[^1].png);

        // ICO container with PNG-compressed entries (supported since Windows Vista).
        using var fs = File.Create(icoPath);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(png.Length); w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);
        Console.WriteLine($"Icon written to {icoPath} and {pngPath}");
    }

    private static byte[] Render(int size)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            // Small sizes use the whole canvas; large ones leave the usual margin.
            double inset = size <= 32 ? 0 : size * 0.06;
            var r = new Rect(inset, inset, size - inset * 2, size - inset * 2);
            var shape = Shapes.Squircle(r);
            ctx.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x4F, 0x7C, 0xFF), 0),
                    new GradientStop(Color.FromRgb(0x6A, 0x4C, 0xF5), 0.55),
                    new GradientStop(Color.FromRgb(0x2A, 0xB5, 0xF0), 1),
                },
            }, null, shape);
            using (ctx.PushGeometryClip(shape))
                ctx.DrawRectangle(Shapes.Vertical(Color.FromArgb(0x40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)), null, new Rect(r.X, r.Y, r.Width, r.Height * 0.55));
            if (size >= 48)
                ctx.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), Math.Max(1, size / 128.0)), shape);
            var mark = r.Deflate(r.Width * (size <= 24 ? 0.14 : 0.2));
            Glyphs.Draw(ctx, Glyph.Logo, mark, Colors.White);
        }
        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }
}
