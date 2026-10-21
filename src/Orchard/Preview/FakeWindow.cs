using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Orchard.Controls;
using Orchard.Rendering;
using Orchard.Theme;

namespace Orchard.Preview;

public enum FakeContent { Browser, Notes, Files }

/// <summary>
/// A stand-in app window for preview mode and screenshots: rounded corners, deep shadow, title bar with the real
/// <see cref="TrafficLights"/> control. On Windows the lights are overlaid on real app windows instead.
/// </summary>
public sealed class FakeWindow : Panel
{
    public const double TitleHeight = 40;
    public TrafficLights Lights { get; } = new();
    public string Title { get; }
    public FakeContent Content { get; }
    public bool Active { get => Lights.IsActive; set { Lights.IsActive = value; InvalidateVisual(); } }
    public bool Maximized { get; set; }

    public FakeWindow(string title, FakeContent content)
    {
        Title = title;
        Content = content;
        Lights.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        Lights.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        Lights.Margin = new Thickness(14, (TitleHeight - TrafficLights.Diameter) / 2 - 2, 0, 0);
        Children.Add(new Surface(this));
        Children.Add(Lights);
    }

    private sealed class Surface(FakeWindow w) : Control
    {
        public override void Render(DrawingContext ctx)
        {
            var bounds = new Rect(Bounds.Size);
            double radius = w.Maximized ? 0 : 14;
            var shape = new RoundedRect(bounds, radius);
            if (!w.Maximized)
                ctx.DrawRectangle(Brushes.Transparent, null, shape,
                    new BoxShadows(new BoxShadow { OffsetY = w.Active ? 22 : 10, Blur = w.Active ? 60 : 30, Color = Color.FromArgb(w.Active ? (byte)0x60 : (byte)0x35, 0, 0, 0) }));

            using (ctx.PushClip(shape))
            {
                ctx.DrawRectangle(Palette.Brush(Color.FromRgb(0xFB, 0xFB, 0xFD)), null, bounds);
                switch (w.Content)
                {
                    case FakeContent.Browser: Browser(ctx, bounds); break;
                    case FakeContent.Notes: Notes(ctx, bounds); break;
                    default: Files(ctx, bounds); break;
                }
            }
            ctx.DrawRectangle(null, new Pen(Palette.Brush(Color.FromArgb(0x30, 0, 0, 0)), 0.75), shape);
        }

        private void TitleBar(DrawingContext ctx, Rect b, Color bg, bool centredTitle = true)
        {
            ctx.DrawRectangle(Palette.Brush(bg), null, new Rect(b.X, b.Y, b.Width, TitleHeight));
            ctx.DrawLine(new Pen(Palette.Brush(Color.FromArgb(0x18, 0, 0, 0)), 1), new Point(b.X, TitleHeight - 0.5), new Point(b.Right, TitleHeight - 0.5));
            if (!centredTitle) return;
            var t = Shapes.Text(w.Title, 13, w.Active ? Color.FromRgb(0x3A, 0x3A, 0x3C) : Color.FromRgb(0xA0, 0xA0, 0xA4), FontWeight.SemiBold);
            ctx.DrawText(t, new Point(b.Center.X - t.Width / 2, (TitleHeight - t.Height) / 2));
        }

        private void Browser(DrawingContext ctx, Rect b)
        {
            TitleBar(ctx, b, Color.FromRgb(0xF2, 0xF2, 0xF5), centredTitle: false);
            // Address field.
            var field = new Rect(b.Center.X - 220, 7, 440, 26);
            ctx.DrawRectangle(Palette.Brush(Color.FromArgb(0x14, 0, 0, 0)), null, new RoundedRect(field, 8));
            Glyphs.Draw(ctx, Glyph.Lock, new Rect(field.X + 10, field.Y + 7, 12, 12), Color.FromRgb(0x8E, 0x8E, 0x93));
            var url = Shapes.Text("orchard.dev", 13, Color.FromRgb(0x3A, 0x3A, 0x3C));
            ctx.DrawText(url, new Point(field.Center.X - url.Width / 2, field.Y + (field.Height - url.Height) / 2));

            // Hero.
            var hero = new Rect(b.X, TitleHeight, b.Width, Math.Min(300, b.Height * 0.48));
            ctx.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0x1E, 0x1B, 0x4B), 0), new GradientStop(Color.FromRgb(0x31, 0x2E, 0x81), 0.5), new GradientStop(Color.FromRgb(0x0E, 0x74, 0x90), 1) },
            }, null, hero);
            var h1 = Shapes.Text("Your desktop, reimagined.", 38, Colors.White, FontWeight.Bold);
            ctx.DrawText(h1, new Point(hero.Center.X - h1.Width / 2, hero.Y + hero.Height * 0.34));
            var h2 = Shapes.Text("A glassy dock, a real menu bar, and windows that pour into place.", 16, Color.FromArgb(0xCC, 255, 255, 255));
            ctx.DrawText(h2, new Point(hero.Center.X - h2.Width / 2, hero.Y + hero.Height * 0.34 + 54));
            var cta = new Rect(hero.Center.X - 70, hero.Y + hero.Height * 0.34 + 96, 140, 36);
            ctx.DrawRectangle(Palette.Brush(Colors.White), null, new RoundedRect(cta, 18));
            var ctaText = Shapes.Text("Get Orchard", 14, Color.FromRgb(0x1E, 0x1B, 0x4B), FontWeight.SemiBold);
            ctx.DrawText(ctaText, new Point(cta.Center.X - ctaText.Width / 2, cta.Center.Y - ctaText.Height / 2));

            // Feature cards.
            double cardW = (b.Width - 48 - 32) / 3, y = hero.Bottom + 24;
            string[] titles = ["Dock", "Menu bar", "Spotlight"];
            string[] icons = ["launchpad", "settings", "browser"];
            for (int i = 0; i < 3; i++)
            {
                var card = new Rect(24 + i * (cardW + 16), y, cardW, Math.Max(80, b.Bottom - y - 24));
                ctx.DrawRectangle(Palette.Brush(Colors.White), new Pen(Palette.Brush(Color.FromArgb(0x14, 0, 0, 0)), 1), new RoundedRect(card, 14),
                    new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 10, Color = Color.FromArgb(0x10, 0, 0, 0) }));
                new VectorIcon(icons[i]).Draw(ctx, new Rect(card.X + 16, card.Y + 16, 40, 40));
                ctx.DrawText(Shapes.Text(titles[i], 16, Color.FromRgb(0x1D, 0x1D, 0x1F), FontWeight.SemiBold), new Point(card.X + 16, card.Y + 66));
                for (int l = 0; l < 3 && card.Y + 98 + l * 14 < card.Bottom - 8; l++)
                    ctx.DrawRectangle(Palette.Brush(Color.FromRgb(0xE8, 0xE8, 0xED)), null, new RoundedRect(new Rect(card.X + 16, card.Y + 98 + l * 14, card.Width * (l == 2 ? 0.5 : 0.8), 7), 3.5));
            }
        }

        private void Notes(DrawingContext ctx, Rect b)
        {
            double side = 200;
            ctx.DrawRectangle(Palette.Brush(Color.FromRgb(0xEE, 0xEE, 0xF1)), null, new Rect(0, 0, side, b.Height));
            ctx.DrawRectangle(Palette.Brush(Color.FromRgb(0xFF, 0xFD, 0xF6)), null, new Rect(side, 0, b.Width - side, b.Height));
            ctx.DrawLine(new Pen(Palette.Brush(Color.FromArgb(0x16, 0, 0, 0)), 1), new Point(side, 0), new Point(side, b.Height));
            string[] notes = ["Orchard roadmap", "Groceries", "Dock ideas", "Trip to Tahoe", "Reading list"];
            for (int i = 0; i < notes.Length; i++)
            {
                var row = new Rect(10, 52 + i * 52, side - 20, 46);
                if (i == 0) ctx.DrawRectangle(Palette.Brush(Color.FromRgb(0xFF, 0xD5, 0x4A)), null, new RoundedRect(row, 8));
                ctx.DrawText(Shapes.Text(notes[i], 13, Color.FromRgb(0x1D, 0x1D, 0x1F), FontWeight.SemiBold), new Point(row.X + 10, row.Y + 6));
                ctx.DrawText(Shapes.Text(i == 0 ? "Today" : "Yesterday", 11, Color.FromRgb(0x6E, 0x6E, 0x73)), new Point(row.X + 10, row.Y + 25));
            }
            double x = side + 28, y = TitleHeight + 18;
            ctx.DrawText(Shapes.Text("Orchard roadmap", 24, Color.FromRgb(0x1D, 0x1D, 0x1F), FontWeight.Bold), new Point(x, y));
            string[] lines = ["✓  Dock with magnification + bounce", "✓  Menu bar with live app menus", "✓  Traffic-light window buttons",
                              "✓  Genie minimize", "✓  Spotlight + Launchpad", "○  Mission Control", "○  Widgets"];
            for (int i = 0; i < lines.Length; i++)
                ctx.DrawText(Shapes.Text(lines[i], 14, Color.FromRgb(0x3A, 0x3A, 0x3C)), new Point(x, y + 48 + i * 26));
        }

        private void Files(DrawingContext ctx, Rect b)
        {
            TitleBar(ctx, b, Color.FromRgb(0xF6, 0xF6, 0xF8));
            for (int i = 0; i < 8; i++)
            {
                var cell = new Rect(28 + i % 4 * 110, TitleHeight + 24 + i / 4 * 110, 64, 64);
                new VectorIcon("folder").Draw(ctx, cell);
                var t = Shapes.Text(new[] { "Desktop", "Documents", "Downloads", "Music", "Pictures", "Videos", "Projects", "Orchard" }[i], 12, Color.FromRgb(0x1D, 0x1D, 0x1F));
                ctx.DrawText(t, new Point(cell.Center.X - t.Width / 2, cell.Bottom + 6));
            }
        }
    }
}
