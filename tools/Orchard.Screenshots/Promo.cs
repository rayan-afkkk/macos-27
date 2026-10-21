using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orchard.Controls;
using Orchard.Model;
using Orchard.Preview;
using Orchard.Rendering;
using Orchard.Services;
using Orchard.Theme;

/// <summary>
/// Renders the promo video frame by frame from Orchard's real controls (the same DesktopScene as the screenshots),
/// with a scripted cursor and captions on top, and pipes raw frames into ffmpeg.
/// </summary>
internal static class Promo
{
    private const int W = 1440, H = 900, Fps = 30;
    private const double Duration = 36;

    public static void Render(string output)
    {
        Palette.Dark = false;
        var settings = new OrchardSettings();
        var scene = new DesktopScene(new PixelSize(W, H), settings) { AutoHide = false };
        var clock = DateTime.Today.AddHours(9).AddMinutes(41);
        scene.MenuBar.FixedNow = clock;
        var overlay = new Overlay();
        var root = new Panel { Width = W, Height = H, Children = { scene, overlay } };
        var window = new Window { Width = W, Height = H, Content = root, SystemDecorations = SystemDecorations.None };
        window.Show();
        Pump();

        var browser = scene.Windows.First(w => w.Title == "orchard.dev");
        var browserApp = scene.Platform.DockApps.First(a => a.Id == "browser");
        double dockTop = H - scene.Dock.Height;
        Point DockIcon(string id)
        {
            scene.Dock.SetPointer(null);
            for (int i = 0; i < 40; i++) scene.Dock.Advance(0.05);
            var r = scene.Dock.IconRect(scene.Platform.DockApps.First(a => a.Id == id))!.Value;
            return new Point(r.Center.X, dockTop + r.Center.Y);
        }
        var dockLeft = DockIcon("files");
        var dockRight = DockIcon("terminal");
        // FakeWindow puts its lights 14 px in and 12 px down; circle centres sit at 8, 28, 48 within the control.
        double bx = Canvas.GetLeft(browser), by = Canvas.GetTop(browser);
        var lightsYellow = new Point(bx + 14 + 28, by + 12 + 8);
        var lightsGreen = new Point(bx + 14 + 48, by + 12 + 8);
        // Control Center opens at x = 1440 - 324 - 10, y = 32 (see DesktopScene.OpenControlCenter).
        const double ccX = W - ControlCenterView.PanelWidth - 10, ccY = 32;
        double sliderLeft = ccX + ControlCenterView.Pad + 14, sliderWidth = ControlCenterView.PanelWidth - ControlCenterView.Pad * 2 - 28;
        double soundY = ccY + ControlCenterView.Pad + 160 + 74 + 32 + 11;
        var darkTile = new Point(ccX + ControlCenterView.Pad + 46, ccY + ControlCenterView.Pad + 160 + 74 + 74 + 22);

        // ---- cursor path (time, position) ----
        var path = new List<(double T, Point P)>
        {
            (0, new Point(900, 520)), (3.2, new Point(900, 520)),
            (4.0, new Point(dockLeft.X - 20, dockLeft.Y + 4)), (7.0, new Point(dockRight.X, dockRight.Y + 4)),
            (7.9, lightsYellow), (10.6, lightsYellow), (11.1, lightsGreen),
            (12.4, new Point(840, 600)), (13.0, new Point(700, H - 1)), (13.8, new Point(760, H - 30)), (14.6, new Point(820, H - 30)),
            (15.2, new Point(900, 420)), (19.2, new Point(900, 420)),
            (19.9, new Point(1268, 14)), (20.4, new Point(sliderLeft + 150, soundY)), (21.3, new Point(sliderLeft + 250, soundY)),
            (22.0, darkTile), (24.6, darkTile),
            (25.3, new Point(dockLeft.X, dockLeft.Y + 4)), (27.6, new Point(dockRight.X - 80, dockRight.Y + 4)),
            (28.4, new Point(dockLeft.X + 60, dockLeft.Y)), (31.5, new Point(dockLeft.X + 60, dockLeft.Y)),
            (Duration, new Point(dockLeft.X + 60, dockLeft.Y)),
        };
        Point Cursor(double t)
        {
            for (int i = 1; i < path.Count; i++)
                if (t <= path[i].T)
                {
                    var (t0, p0) = path[i - 1];
                    var (t1, p1) = path[i];
                    double k = DockMath.EaseInOut(Math.Clamp((t - t0) / Math.Max(0.001, t1 - t0), 0, 1));
                    return new Point(p0.X + (p1.X - p0.X) * k, p0.Y + (p1.Y - p0.Y) * k);
                }
            return path[^1].P;
        }

        string[] captions =
        [
            "", "A dock that comes alive", "Genie minimize", "Gets out of the way", "Spotlight", "Control Center", "Dark mode — widgets turn black", "Launchpad", "",
        ];
        double[] captionAt = [0, 3.2, 7.4, 10.8, 15.0, 19.4, 22.2, 28.2, 31.8];

        var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt bgra -s {W}x{H} -r {Fps} -i - " +
            "-vf scale=1280:800:flags=lanczos -c:v libx264 -preset slow -crf 24 -pix_fmt yuv420p -movflags +faststart " +
            $"\"{output}\"") { RedirectStandardInput = true, UseShellExecute = false })!;
        var stdin = ffmpeg.StandardInput.BaseStream;

        GenieView? genie = null;
        SpotlightView? spotlight = null;
        ControlCenterView? cc = null;
        byte[]? fadeFrom = null;
        double fadeStart = 0;
        var buffer = new byte[W * H * 4];
        int frames = (int)(Duration * Fps);
        var fired = new HashSet<string>();
        bool Once(string key, double t, double at) => t >= at && fired.Add(key);

        for (int f = 0; f < frames; f++)
        {
            double t = f / (double)Fps;
            var now = clock.AddSeconds(t);
            scene.Widgets.FixedNow = now;
            scene.Dock.FixedNow = DateTime.UtcNow.Date.AddSeconds(t);

            // ---- scripted events ----
            if (Once("genie", t, 8.2)) genie = scene.ShowGenieFrame(browser, browserApp, 0);
            if (genie != null)
            {
                double p = t < 9.0 ? (t - 8.2) / 0.6 : t < 9.6 ? 1 : 1 - (t - 9.6) / 0.6;
                genie.Progress = Math.Clamp(p, 0, 1);
                genie.InvalidateVisual();
                if (t >= 10.2) { scene.RemoveGenie(genie); genie = null; browser.IsVisible = true; }
            }
            if (Once("max", t, 11.2)) { scene.SetMaximized(browser, true); scene.Dock.HiddenTarget = 1; }
            if (Once("reveal", t, 13.2)) scene.Dock.HiddenTarget = 0;
            if (Once("rehide", t, 14.7)) scene.Dock.HiddenTarget = 1;
            if (Once("unmax", t, 15.1)) { scene.SetMaximized(browser, false); scene.Dock.HiddenTarget = 0; }
            if (Once("spot", t, 15.4)) spotlight = scene.OpenSpotlightView();
            if (spotlight != null)
            {
                const string word = "calc", math = "128*4+1";
                if (t < 17.4) spotlight.Query = word[..Math.Clamp((int)((t - 15.8) / 0.22) + 1, 0, word.Length)];
                else spotlight.Query = math[..Math.Clamp((int)((t - 17.6) / 0.12) + 1, 0, math.Length)];
                if (t >= 19.2) { scene.ClosePopup(); spotlight = null; }
            }
            if (Once("cc", t, 19.9)) cc = scene.OpenControlCenter();
            if (cc != null && t is > 20.4 and < 21.3)
            {
                scene.Platform.Volume = Math.Clamp((Cursor(t).X - sliderLeft) / sliderWidth, 0, 1);
                cc.InvalidateVisual();
            }
            if (Once("dark", t, 22.1))
            {
                fadeFrom = (byte[])buffer.Clone();
                fadeStart = t;
                scene.Platform.DarkMode = true;
            }
            if (Once("ccclose", t, 24.6)) { scene.ClosePopup(); cc = null; }
            if (Once("launchpad", t, 28.6)) scene.ShowLaunchpad();
            if (Once("lpclose", t, 31.6)) scene.ClosePopup();

            // ---- continuous state ----
            var cursor = Cursor(t);
            bool overDock = cursor.Y > dockTop && spotlight == null && cc == null;
            scene.Dock.SetPointer(overDock ? new Point(cursor.X, cursor.Y - dockTop) : null);
            scene.Dock.Advance(1.0 / Fps);

            overlay.Pointer = cursor;
            overlay.Pressed = new[] { 8.2, 11.15, 19.9, 22.1, 28.6 }.Any(c => t >= c - 0.1 && t < c + 0.08);
            overlay.ShowCursor = t is > 3.0 and < 31.8;
            int ci = Array.FindLastIndex(captionAt, c => t >= c);
            overlay.Caption = captions[ci];
            double sinceCaption = t - captionAt[ci];
            double untilNext = (ci + 1 < captionAt.Length ? captionAt[ci + 1] : Duration) - t;
            overlay.CaptionOpacity = Math.Clamp(Math.Min(sinceCaption, untilNext) / 0.3, 0, 1);
            overlay.Card = t < 3.2 ? Card.Title : t >= 31.8 ? Card.End : Card.None;
            overlay.CardOpacity = t < 3.2 ? Math.Clamp((3.2 - t) / 0.5, 0, 1) : t >= 31.8 ? Math.Clamp((t - 31.8) / 0.5, 0, 1) : 0;
            overlay.CardTime = t < 3.2 ? t : t - 31.8;
            overlay.InvalidateVisual();

            Pump();
            Capture(root, buffer);
            if (fadeFrom != null)
            {
                double k = (t - fadeStart) / 0.5;
                if (k >= 1) fadeFrom = null;
                else Blend(buffer, fadeFrom, 1 - DockMath.EaseInOut(k));
            }
            stdin.Write(buffer);
            if (f % 60 == 0) Console.WriteLine($"  frame {f}/{frames}");
        }

        stdin.Close();
        ffmpeg.WaitForExit();
        window.Close();
        Palette.Dark = false;
        Console.WriteLine($"Promo video written to {output} (exit {ffmpeg.ExitCode})");
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static unsafe void Capture(Control root, byte[] buffer)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96));
        rtb.Render(root);
        fixed (byte* p = buffer) rtb.CopyPixels(new PixelRect(0, 0, W, H), (nint)p, buffer.Length, W * 4);
    }

    /// <summary>Cross-fade: mixes <paramref name="from"/> over the new frame with the given weight.</summary>
    private static void Blend(byte[] frame, byte[] from, double weight)
    {
        int a = (int)(weight * 256);
        for (int i = 0; i < frame.Length; i++) frame[i] = (byte)((from[i] * a + frame[i] * (256 - a)) >> 8);
    }

    private enum Card { None, Title, End }

    /// <summary>Cursor, captions and title cards drawn over the scene.</summary>
    private sealed class Overlay : Control
    {
        public Point Pointer { get; set; }
        public bool Pressed { get; set; }
        public bool ShowCursor { get; set; }
        public string Caption { get; set; } = "";
        public double CaptionOpacity { get; set; }
        public Card Card { get; set; }
        public double CardOpacity { get; set; }
        public double CardTime { get; set; }

        public override void Render(DrawingContext ctx)
        {
            if (Caption.Length > 0 && CaptionOpacity > 0)
            {
                using (ctx.PushOpacity(CaptionOpacity))
                {
                    var text = Shapes.Text(Caption, 22, Colors.White, FontWeight.SemiBold);
                    var rect = new Rect(W / 2.0 - text.Width / 2 - 26, 42, text.Width + 52, text.Height + 22);
                    ctx.DrawRectangle(Palette.Brush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14)), new Pen(Palette.Brush(Color.FromArgb(0x50, 255, 255, 255)), 1),
                        new RoundedRect(rect, rect.Height / 2), new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 24, Color = Color.FromArgb(0x50, 0, 0, 0) }));
                    ctx.DrawText(text, new Point(rect.X + 26, rect.Y + 11));
                }
            }

            if (ShowCursor && Card == Card.None) DrawCursor(ctx, Pointer, Pressed ? 0.85 : 1);

            if (Card != Card.None && CardOpacity > 0)
            {
                using (ctx.PushOpacity(CardOpacity))
                {
                    var bg = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.FromRgb(0x0B, 0x10, 0x2E), 0), new GradientStop(Color.FromRgb(0x2B, 0x1F, 0x6B), 0.55), new GradientStop(Color.FromRgb(0x0E, 0x5E, 0x8C), 1) },
                    };
                    ctx.DrawRectangle(bg, null, new Rect(0, 0, W, H));
                    double rise = (1 - DockMath.EaseOut(CardTime / 0.9)) * 24;
                    Glyphs.Draw(ctx, Glyph.Logo, new Rect(W / 2.0 - 48, 250 + rise, 96, 96), Colors.White);
                    var title = Shapes.Text("Orchard", 84, Colors.White, FontWeight.Bold);
                    ctx.DrawText(title, new Point(W / 2.0 - title.Width / 2, 370 + rise));
                    string sub = Card == Card.Title ? "The Mac feel, on Windows 10 & 11." : "Free & open source  ·  github.com/rayan-afkkk/macos-27";
                    var subtitle = Shapes.Text(sub, 26, Color.FromArgb(0xD0, 255, 255, 255), FontWeight.Medium);
                    ctx.DrawText(subtitle, new Point(W / 2.0 - subtitle.Width / 2, 490 + rise * 1.4));
                    if (Card == Card.End)
                    {
                        var tags = Shapes.Text("Dock · Menu bar · Genie · Spotlight · Launchpad · Control Center · Widgets · Dark mode", 17,
                            Color.FromArgb(0x99, 255, 255, 255), FontWeight.Medium);
                        ctx.DrawText(tags, new Point(W / 2.0 - tags.Width / 2, 560 + rise * 1.6));
                    }
                }
            }
        }

        private static void DrawCursor(DrawingContext ctx, Point p, double scale)
        {
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                Point P(double x, double y) => new(p.X + x * scale, p.Y + y * scale);
                s.BeginFigure(P(0, 0), true);
                s.LineTo(P(0, 22));
                s.LineTo(P(5.5, 17));
                s.LineTo(P(9.5, 26));
                s.LineTo(P(13, 24.5));
                s.LineTo(P(9, 15.8));
                s.LineTo(P(16, 15.8));
                s.EndFigure(true);
            }
            using (ctx.PushTransform(Matrix.CreateTranslation(1, 2)))
                ctx.DrawGeometry(Palette.Brush(Color.FromArgb(0x40, 0, 0, 0)), null, g);
            ctx.DrawGeometry(Palette.Brush(Colors.Black), new Pen(Palette.Brush(Colors.White), 1.6, lineJoin: PenLineJoin.Round), g);
        }
    }
}
