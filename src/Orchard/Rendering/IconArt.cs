using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Orchard.Theme;

namespace Orchard.Rendering;

/// <summary>Something that can paint an app icon into a square. Vector art stays crisp under dock magnification.</summary>
public abstract class IconArt
{
    public abstract void Draw(DrawingContext ctx, Rect r);

    public static IconArt FromBitmap(Bitmap bitmap) => new BitmapIcon(bitmap);
}

/// <summary>A real icon extracted from an executable, shown inside a soft squircle "plate" like modern macOS does for legacy icons.</summary>
public sealed class BitmapIcon(Bitmap bitmap) : IconArt
{
    public Bitmap Bitmap { get; } = bitmap;

    public override void Draw(DrawingContext ctx, Rect r)
    {
        var plate = r.Deflate(r.Width * 0.06);
        ctx.DrawGeometry(Shapes.Vertical(Color.FromRgb(0xFD, 0xFD, 0xFE), Color.FromRgb(0xE4, 0xE6, 0xEA)),
            new Pen(Palette.Brush(Color.FromArgb(0x22, 0, 0, 0)), Math.Max(0.5, r.Width / 160)), Shapes.Squircle(plate));
        var inner = plate.Deflate(plate.Width * 0.17);
        var src = new Rect(0, 0, Bitmap.PixelSize.Width, Bitmap.PixelSize.Height);
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            ctx.DrawImage(Bitmap, src, inner);
    }
}

/// <summary>
/// Orchard's built-in icon set, drawn procedurally. These are original designs in the general style of
/// modern desktop icons (squircle, soft gradient, bold symbol) — no third-party artwork is used.
/// </summary>
public sealed class VectorIcon(string kind) : IconArt
{
    public string Kind { get; } = kind;

    public static readonly string[] Kinds =
        ["files", "launchpad", "browser", "mail", "messages", "music", "photos", "calendar", "notes", "terminal",
         "settings", "calculator", "store", "maps", "code", "trash", "edge", "folder", "app"];

    public override void Draw(DrawingContext ctx, Rect r)
    {
        var plate = r.Deflate(r.Width * 0.06);
        var shape = Shapes.Squircle(plate);
        double u = plate.Width / 100.0; // 100-unit design grid inside the plate
        Point P(double x, double y) => new(plate.X + x * u, plate.Y + y * u);
        Rect R(double x, double y, double w, double h) => new(P(x, y), new Size(w * u, h * u));
        IBrush B(byte rr, byte gg, byte bb) => Palette.Brush(Color.FromRgb(rr, gg, bb));

        if (Kind == "trash") { DrawTrash(ctx, r); return; }

        ctx.DrawGeometry(Background(), null, shape);

        using (ctx.PushGeometryClip(shape))
        {
            switch (Kind)
            {
                case "files":
                {
                    // Two-tone face split down the middle with a friendly smile.
                    ctx.DrawRectangle(B(0xE8, 0xF4, 0xFF), null, R(50, 0, 50, 100));
                    var eye = Palette.Brush(Color.FromRgb(0x10, 0x2A, 0x5C));
                    ctx.DrawRectangle(eye, null, new RoundedRect(R(30, 30, 6, 16), 3 * u));
                    ctx.DrawRectangle(eye, null, new RoundedRect(R(64, 30, 6, 16), 3 * u));
                    var smile = new StreamGeometry();
                    using (var c = smile.Open())
                    {
                        c.BeginFigure(P(24, 64), false);
                        c.QuadraticBezierTo(P(50, 82), P(76, 64));
                        c.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, new Pen(eye, 5 * u, lineCap: PenLineCap.Round), smile);
                    break;
                }
                case "launchpad":
                {
                    Color[] tiles = [Color.FromRgb(0xFF, 0x6B, 0x6B), Color.FromRgb(0xFF, 0xC8, 0x4B), Color.FromRgb(0x5B, 0xD1, 0x7F),
                                     Color.FromRgb(0x4D, 0xA3, 0xFF), Color.FromRgb(0xA7, 0x7B, 0xFF), Color.FromRgb(0xFF, 0x7E, 0xC8),
                                     Color.FromRgb(0x3E, 0xDB, 0xD0), Color.FromRgb(0xFF, 0x9B, 0x52), Color.FromRgb(0x8E, 0xE0, 0x4E)];
                    for (int i = 0; i < 9; i++)
                        ctx.DrawRectangle(Palette.Brush(tiles[i]), null, new RoundedRect(R(20 + i % 3 * 22, 20 + i / 3 * 22, 16, 16), 5 * u));
                    break;
                }
                case "browser":
                case "edge":
                {
                    var face = R(14, 14, 72, 72);
                    ctx.DrawEllipse(Kind == "edge" ? Shapes.Diagonal(Color.FromRgb(0x36, 0xE0, 0xA8), Color.FromRgb(0x0B, 0x6B, 0xD8)) : B(0xFF, 0xFF, 0xFF),
                        null, face.Center, face.Width / 2, face.Height / 2);
                    var tick = new Pen(Palette.Brush(Color.FromArgb(0x70, 0x30, 0x40, 0x60)), 1.3 * u);
                    for (int i = 0; i < 24; i++)
                    {
                        double a = i * Math.PI / 12, r0 = i % 2 == 0 ? 29 : 31;
                        ctx.DrawLine(tick, P(50 + r0 * Math.Cos(a), 50 + r0 * Math.Sin(a)), P(50 + 34 * Math.Cos(a), 50 + 34 * Math.Sin(a)));
                    }
                    var needleA = new StreamGeometry();
                    using (var c = needleA.Open()) { c.BeginFigure(P(66, 30), true); c.LineTo(P(54, 54)); c.LineTo(P(46, 46)); c.EndFigure(true); }
                    var needleB = new StreamGeometry();
                    using (var c = needleB.Open()) { c.BeginFigure(P(34, 70), true); c.LineTo(P(46, 46)); c.LineTo(P(54, 54)); c.EndFigure(true); }
                    ctx.DrawGeometry(B(0xFF, 0x3B, 0x30), null, needleA);
                    ctx.DrawGeometry(B(0xF2, 0xF2, 0xF7), null, needleB);
                    break;
                }
                case "mail":
                {
                    var env = R(18, 30, 64, 42);
                    ctx.DrawRectangle(B(0xFF, 0xFF, 0xFF), null, new RoundedRect(env, 6 * u));
                    var flap = new StreamGeometry();
                    using (var c = flap.Open()) { c.BeginFigure(P(20, 33), false); c.LineTo(P(50, 56)); c.LineTo(P(80, 33)); c.EndFigure(false); }
                    ctx.DrawGeometry(null, new Pen(B(0x2B, 0x7B, 0xF0), 4 * u, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round), flap);
                    break;
                }
                case "messages":
                {
                    ctx.DrawEllipse(B(0xFF, 0xFF, 0xFF), null, P(50, 47), 31 * u, 25 * u);
                    var tail = new StreamGeometry();
                    using (var c = tail.Open()) { c.BeginFigure(P(30, 62), true); c.QuadraticBezierTo(P(30, 76), P(20, 80)); c.QuadraticBezierTo(P(36, 79), P(44, 70)); c.EndFigure(true); }
                    ctx.DrawGeometry(B(0xFF, 0xFF, 0xFF), null, tail);
                    break;
                }
                case "music":
                {
                    var white = B(0xFF, 0xFF, 0xFF);
                    ctx.DrawEllipse(white, null, P(36, 70), 10 * u, 8 * u);
                    ctx.DrawEllipse(white, null, P(68, 63), 10 * u, 8 * u);
                    var stem = new Pen(white, 5 * u, lineCap: PenLineCap.Round);
                    ctx.DrawLine(stem, P(44, 70), P(44, 30));
                    ctx.DrawLine(stem, P(76, 63), P(76, 23));
                    ctx.DrawLine(new Pen(white, 9 * u, lineCap: PenLineCap.Round), P(44, 30), P(76, 23));
                    break;
                }
                case "photos":
                {
                    Color[] petals = [Color.FromRgb(0xFF, 0x95, 0x00), Color.FromRgb(0xFF, 0xCC, 0x00), Color.FromRgb(0x9B, 0xD3, 0x3C), Color.FromRgb(0x34, 0xC7, 0x8A),
                                      Color.FromRgb(0x34, 0xA7, 0xF5), Color.FromRgb(0x64, 0x5C, 0xE8), Color.FromRgb(0xC2, 0x4F, 0xD9), Color.FromRgb(0xFF, 0x47, 0x63)];
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4 - Math.PI / 2;
                        using (ctx.PushOpacity(0.86))
                            ctx.DrawEllipse(Palette.Brush(petals[i]), null, P(50 + 17 * Math.Cos(a), 50 + 17 * Math.Sin(a)), 15 * u, 15 * u);
                    }
                    break;
                }
                case "calendar":
                {
                    ctx.DrawText(Shapes.Text(DateTime.Now.ToString("ddd").ToUpperInvariant(), 13 * u, Color.FromRgb(0xFF, 0x3B, 0x30), FontWeight.SemiBold), Centered(13 * u, 16, "ddd"));
                    var day = Shapes.Text(DateTime.Now.Day.ToString(), 50 * u, Color.FromRgb(0x1D, 0x1D, 0x1F), FontWeight.Regular);
                    ctx.DrawText(day, new Point(plate.Center.X - day.Width / 2, plate.Y + 28 * u));
                    break;
                }
                case "notes":
                {
                    ctx.DrawRectangle(Shapes.Vertical(Color.FromRgb(0xFF, 0xD5, 0x4A), Color.FromRgb(0xFF, 0xC2, 0x1A)), null, R(0, 0, 100, 26));
                    var line = new Pen(B(0xD8, 0xD8, 0xDC), 1.6 * u);
                    for (int i = 0; i < 5; i++) ctx.DrawLine(line, P(14, 42 + i * 11), P(86, 42 + i * 11));
                    break;
                }
                case "terminal":
                {
                    ctx.DrawText(Shapes.Text(">_", 30 * u, Color.FromRgb(0xF2, 0xF2, 0xF2), FontWeight.SemiBold), P(18, 18));
                    break;
                }
                case "settings":
                {
                    var center = P(50, 50);
                    var steel = Shapes.Vertical(Color.FromRgb(0xC9, 0xCC, 0xD1), Color.FromRgb(0x7D, 0x82, 0x8A));
                    for (int i = 0; i < 12; i++)
                    {
                        double a = i * Math.PI / 6;
                        using (ctx.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(a) * Matrix.CreateTranslation(center.X, center.Y)))
                            ctx.DrawRectangle(steel, null, new RoundedRect(R(45, 13, 10, 14), 2 * u));
                    }
                    ctx.DrawEllipse(steel, null, center, 28 * u, 28 * u);
                    ctx.DrawEllipse(B(0x3A, 0x3D, 0x42), null, center, 19 * u, 19 * u);
                    ctx.DrawEllipse(steel, null, center, 9 * u, 9 * u);
                    break;
                }
                case "calculator":
                {
                    Color[] keys = [Color.FromRgb(0xA5, 0xA5, 0xA5), Color.FromRgb(0xA5, 0xA5, 0xA5), Color.FromRgb(0xFF, 0x9F, 0x0A),
                                    Color.FromRgb(0x50, 0x50, 0x52), Color.FromRgb(0x50, 0x50, 0x52), Color.FromRgb(0xFF, 0x9F, 0x0A),
                                    Color.FromRgb(0x50, 0x50, 0x52), Color.FromRgb(0x50, 0x50, 0x52), Color.FromRgb(0xFF, 0x9F, 0x0A)];
                    for (int i = 0; i < 9; i++)
                        ctx.DrawEllipse(Palette.Brush(keys[i]), null, P(28 + i % 3 * 22, 28 + i / 3 * 22), 9 * u, 9 * u);
                    break;
                }
                case "store":
                {
                    var white = new Pen(B(0xFF, 0xFF, 0xFF), 7 * u, lineCap: PenLineCap.Round);
                    ctx.DrawLine(white, P(50, 22), P(30, 70));
                    ctx.DrawLine(white, P(50, 22), P(70, 70));
                    ctx.DrawLine(white, P(24, 58), P(76, 58));
                    break;
                }
                case "maps":
                {
                    ctx.DrawRectangle(B(0x8E, 0xD1, 0x6F), null, R(0, 0, 60, 100));
                    ctx.DrawRectangle(B(0x6C, 0xC4, 0xF5), null, R(60, 0, 40, 100));
                    ctx.DrawLine(new Pen(B(0xFF, 0xD6, 0x4D), 9 * u), P(-5, 80), P(105, 30));
                    ctx.DrawLine(new Pen(B(0xFF, 0xFF, 0xFF), 5 * u), P(30, -5), P(48, 105));
                    var pin = new StreamGeometry();
                    using (var c = pin.Open()) { c.BeginFigure(P(57, 34), true); c.LineTo(P(66, 48)); c.LineTo(P(75, 34)); c.EndFigure(true); }
                    ctx.DrawGeometry(B(0xFF, 0x3B, 0x30), null, pin);
                    ctx.DrawEllipse(B(0xFF, 0x3B, 0x30), null, P(66, 30), 11 * u, 11 * u);
                    ctx.DrawEllipse(B(0xFF, 0xFF, 0xFF), null, P(66, 30), 4 * u, 4 * u);
                    break;
                }
                case "code":
                {
                    var pen = new Pen(B(0xFF, 0xFF, 0xFF), 7 * u, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                    var l = new StreamGeometry();
                    using (var c = l.Open()) { c.BeginFigure(P(38, 30), false); c.LineTo(P(20, 50)); c.LineTo(P(38, 70)); c.EndFigure(false); }
                    var rr = new StreamGeometry();
                    using (var c = rr.Open()) { c.BeginFigure(P(62, 30), false); c.LineTo(P(80, 50)); c.LineTo(P(62, 70)); c.EndFigure(false); }
                    ctx.DrawGeometry(null, pen, l);
                    ctx.DrawGeometry(null, pen, rr);
                    ctx.DrawLine(new Pen(B(0xB8, 0xE2, 0xFF), 6 * u, lineCap: PenLineCap.Round), P(55, 26), P(45, 74));
                    break;
                }
                case "folder":
                {
                    ctx.DrawRectangle(B(0x4F, 0xA8, 0xF0), null, new RoundedRect(R(14, 24, 34, 16), 4 * u));
                    ctx.DrawRectangle(Shapes.Vertical(Color.FromRgb(0x7C, 0xC4, 0xFF), Color.FromRgb(0x4F, 0xA8, 0xF0)), null, new RoundedRect(R(14, 32, 72, 46), 6 * u));
                    break;
                }
                default:
                {
                    var white = new Pen(B(0xFF, 0xFF, 0xFF), 6 * u, lineCap: PenLineCap.Round);
                    ctx.DrawRectangle(null, white, new RoundedRect(R(28, 28, 44, 44), 9 * u));
                    break;
                }
            }

            // Top sheen: a faint highlight across the upper half sells the "glass-coated" icon look.
            ctx.DrawRectangle(Shapes.Vertical(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)), null, R(0, 0, 100, 55));
        }

        ctx.DrawGeometry(null, new Pen(Palette.Brush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), Math.Max(0.6, u * 0.9)), shape);

        Point Centered(double size, double top, string fmt)
        {
            var t = Shapes.Text(DateTime.Now.ToString(fmt).ToUpperInvariant(), size, Colors.Black, FontWeight.SemiBold);
            return new Point(plate.Center.X - t.Width / 2, plate.Y + top * u - size / 2);
        }
    }

    private IBrush Background() => Kind switch
    {
        "files" => Shapes.Vertical(Color.FromRgb(0x6C, 0xC4, 0xFF), Color.FromRgb(0x15, 0x6E, 0xF0)),
        "launchpad" => Shapes.Vertical(Color.FromRgb(0xF4, 0xF4, 0xF7), Color.FromRgb(0xD9, 0xDB, 0xE2)),
        "browser" => Shapes.Vertical(Color.FromRgb(0x5A, 0xC8, 0xFA), Color.FromRgb(0x0A, 0x60, 0xDF)),
        "edge" => Shapes.Vertical(Color.FromRgb(0xF8, 0xFA, 0xFC), Color.FromRgb(0xD8, 0xE2, 0xEC)),
        "mail" => Shapes.Vertical(Color.FromRgb(0x5E, 0xB5, 0xFF), Color.FromRgb(0x1B, 0x6E, 0xF3)),
        "messages" => Shapes.Vertical(Color.FromRgb(0x6B, 0xE3, 0x6E), Color.FromRgb(0x1E, 0xB9, 0x3A)),
        "music" => Shapes.Vertical(Color.FromRgb(0xFF, 0x6B, 0x84), Color.FromRgb(0xF0, 0x24, 0x47)),
        "photos" => Shapes.Vertical(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xEE, 0xEE, 0xF2)),
        "calendar" => Shapes.Vertical(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xF0, 0xF0, 0xF3)),
        "notes" => Shapes.Vertical(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xF3, 0xF1, 0xEA)),
        "terminal" => Shapes.Vertical(Color.FromRgb(0x3A, 0x3A, 0x3E), Color.FromRgb(0x16, 0x16, 0x18)),
        "settings" => Shapes.Vertical(Color.FromRgb(0x9A, 0x9E, 0xA6), Color.FromRgb(0x5E, 0x62, 0x6A)),
        "calculator" => Shapes.Vertical(Color.FromRgb(0x3A, 0x3A, 0x3C), Color.FromRgb(0x1C, 0x1C, 0x1E)),
        "store" => Shapes.Vertical(Color.FromRgb(0x3F, 0xC1, 0xFF), Color.FromRgb(0x1F, 0x5D, 0xF5)),
        "maps" => Palette.Brush(Color.FromRgb(0x8E, 0xD1, 0x6F)),
        "code" => Shapes.Vertical(Color.FromRgb(0x5C, 0x6C, 0xFF), Color.FromRgb(0x2A, 0x2F, 0xC8)),
        "folder" => Shapes.Vertical(Color.FromRgb(0xE9, 0xF4, 0xFF), Color.FromRgb(0xC8, 0xE1, 0xFA)),
        _ => Shapes.Vertical(Color.FromRgb(0x9B, 0x8C, 0xFF), Color.FromRgb(0x5A, 0x48, 0xE0)),
    };

    private static void DrawTrash(DrawingContext ctx, Rect r)
    {
        // A translucent wire-mesh bin, no plate — the way the trash traditionally sits at the end of the dock.
        double u = r.Width / 100.0;
        Point P(double x, double y) => new(r.X + x * u, r.Y + y * u);
        var body = new StreamGeometry();
        using (var c = body.Open())
        {
            c.BeginFigure(P(22, 22), true);
            c.LineTo(P(78, 22));
            c.LineTo(P(72, 92));
            c.QuadraticBezierTo(P(50, 97), P(28, 92));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(Shapes.Vertical(Color.FromArgb(0xC8, 0xEE, 0xF1, 0xF5), Color.FromArgb(0xC8, 0xC2, 0xC8, 0xD0)),
            new Pen(Palette.Brush(Color.FromArgb(0x90, 0x88, 0x90, 0x9C)), 1.2 * u), body);
        var rib = new Pen(Palette.Brush(Color.FromArgb(0x80, 0x8A, 0x92, 0x9E)), 1.4 * u);
        for (int i = 1; i < 6; i++)
        {
            double t = i / 6.0;
            ctx.DrawLine(rib, P(22 + 56 * t, 26), P(28 + 44 * t, 91));
        }
        ctx.DrawEllipse(Shapes.Vertical(Color.FromRgb(0xF6, 0xF7, 0xF9), Color.FromRgb(0xC4, 0xC9, 0xD1)),
            new Pen(Palette.Brush(Color.FromArgb(0x90, 0x88, 0x90, 0x9C)), 1.2 * u), P(50, 22), 29 * u, 6 * u);
    }
}
