using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Orchard.Model;
using Orchard.Platform;
using Orchard.Rendering;
using Orchard.Services;
using Orchard.Theme;

namespace Orchard.Controls;

/// <summary>
/// Desktop widgets: glass tiles that sit on the wallpaper beneath your windows. Small widgets are square, medium ones
/// span two columns. They follow the appearance — in dark mode the glass goes black and the faces invert.
/// </summary>
public sealed class WidgetBoard : Control
{
    public const double Small = 164, Gap = 16, Radius = 24;
    public static double BoardWidth => Small * 2 + Gap;

    private readonly IShellPlatform _platform;
    private readonly WeatherService? _weather;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<WidgetKind> _kinds = [];
    private List<(WidgetKind Kind, Rect Rect)> _layout = [];
    private SystemLoad? _load;

    public DateTime? FixedNow { get; set; }
    /// <summary>Weather to show when there is no live service (preview, screenshots).</summary>
    public WeatherInfo? SampleWeather { get; set; }
    public event Action<WidgetKind, Rect>? WidgetMenuRequested;
    public event Action<IReadOnlyList<RoundedRect>>? ShapeChanged;

    public WidgetBoard(IShellPlatform platform, WeatherService? weather = null)
    {
        _platform = platform;
        _weather = weather;
        Width = BoardWidth;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnTick());
        AttachedToVisualTree += (_, _) => { _timer.Start(); OnTick(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        if (weather != null) weather.Updated += () => Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private int _ticks;

    private void OnTick()
    {
        if (_ticks++ % 3 == 0) _load = _platform.Load;
        if (_weather != null && _ticks % 60 == 1) _ = _weather.RefreshAsync();
        InvalidateVisual();
    }

    public IReadOnlyList<WidgetKind> Kinds
    {
        get => _kinds;
        set
        {
            _kinds = value;
            _layout = Layout(value);
            Height = _layout.Count == 0 ? 0 : _layout.Max(l => l.Rect.Bottom);
            ShapeChanged?.Invoke(_layout.Select(l => new RoundedRect(l.Rect, Radius)).ToList());
            InvalidateVisual();
        }
    }

    public IReadOnlyList<RoundedRect> TileShapes => _layout.Select(l => new RoundedRect(l.Rect, Radius)).ToList();

    private static bool IsMedium(WidgetKind k) => k == WidgetKind.Weather;

    /// <summary>Flow layout: small widgets pair up side by side, medium ones take a full row.</summary>
    private static List<(WidgetKind, Rect)> Layout(IReadOnlyList<WidgetKind> kinds)
    {
        var result = new List<(WidgetKind, Rect)>();
        double y = 0;
        int col = 0;
        foreach (var k in kinds)
        {
            if (IsMedium(k))
            {
                if (col == 1) { y += Small + Gap; col = 0; }
                result.Add((k, new Rect(0, y, BoardWidth, Small)));
                y += Small + Gap;
            }
            else
            {
                result.Add((k, new Rect(col * (Small + Gap), y, Small, Small)));
                if (++col == 2) { col = 0; y += Small + Gap; }
            }
        }
        return result;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        var p = e.GetPosition(this);
        foreach (var (kind, rect) in _layout)
            if (rect.Contains(p)) { WidgetMenuRequested?.Invoke(kind, rect); e.Handled = true; return; }
    }

    public override void Render(DrawingContext ctx)
    {
        var now = FixedNow ?? DateTime.Now;
        foreach (var (kind, r) in _layout)
        {
            var shape = new RoundedRect(r, Radius);
            if (kind == WidgetKind.Weather) DrawWeather(ctx, r, shape, now);
            else
            {
                Glass.Draw(ctx, this, shape, strong: false, shadow: false);
                switch (kind)
                {
                    case WidgetKind.Clock: DrawClock(ctx, r, now); break;
                    case WidgetKind.Calendar: DrawCalendar(ctx, r, now); break;
                    case WidgetKind.Battery: DrawBattery(ctx, r); break;
                    case WidgetKind.System: DrawSystem(ctx, r); break;
                }
            }
        }
    }

    // ---- clock ---------------------------------------------------------------------------------

    private static void DrawClock(DrawingContext ctx, Rect r, DateTime now)
    {
        var c = new Point(r.Center.X, r.Center.Y);
        double rad = r.Width / 2 - 14;
        var face = Palette.Dark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Colors.White;
        var ink = Palette.Dark ? Colors.White : Color.FromRgb(0x1D, 0x1D, 0x1F);
        ctx.DrawEllipse(Palette.Brush(face), null, c, rad, rad);

        for (int h = 1; h <= 12; h++)
        {
            double a = h * Math.PI / 6 - Math.PI / 2;
            var t = Shapes.Text(h.ToString(CultureInfo.InvariantCulture), 13, ink, FontWeight.Medium);
            ctx.DrawText(t, new Point(c.X + (rad - 13) * Math.Cos(a) - t.Width / 2, c.Y + (rad - 13) * Math.Sin(a) - t.Height / 2));
        }
        void Hand(double angle, double length, double width, Color color)
        {
            var end = new Point(c.X + length * Math.Cos(angle - Math.PI / 2), c.Y + length * Math.Sin(angle - Math.PI / 2));
            ctx.DrawLine(new Pen(Palette.Brush(color), width, lineCap: PenLineCap.Round), c, end);
        }
        double sec = now.Second, min = now.Minute + sec / 60, hour = now.Hour % 12 + min / 60;
        Hand(hour * Math.PI / 6, rad * 0.5, 4, ink);
        Hand(min * Math.PI / 30, rad * 0.74, 3, ink);
        Hand(sec * Math.PI / 30, rad * 0.8, 1.2, Color.FromRgb(0xFF, 0x9F, 0x0A));
        ctx.DrawEllipse(Palette.Brush(Color.FromRgb(0xFF, 0x9F, 0x0A)), null, c, 3, 3);
        ctx.DrawEllipse(Palette.Brush(face), null, c, 1.2, 1.2);
    }

    // ---- calendar ------------------------------------------------------------------------------

    private static void DrawCalendar(DrawingContext ctx, Rect r, DateTime now)
    {
        var red = Color.FromRgb(0xFF, 0x45, 0x3A);
        ctx.DrawText(Shapes.Text(now.ToString("MMMM", CultureInfo.CurrentCulture).ToUpperInvariant(), 11, red, FontWeight.Bold), new Point(r.X + 16, r.Y + 14));
        var first = new DateTime(now.Year, now.Month, 1);
        int offset = (int)first.DayOfWeek; // Sunday-first grid
        int days = DateTime.DaysInMonth(now.Year, now.Month);
        double cw = (r.Width - 24) / 7, ch = 17, top = r.Y + 34;
        string[] heads = ["S", "M", "T", "W", "T", "F", "S"];
        for (int i = 0; i < 7; i++)
        {
            var h = Shapes.Text(heads[i], 9.5, Palette.TextSecondary, FontWeight.SemiBold);
            ctx.DrawText(h, new Point(r.X + 12 + cw * (i + 0.5) - h.Width / 2, top));
        }
        for (int d = 1; d <= days; d++)
        {
            int cell = offset + d - 1;
            double x = r.X + 12 + cw * (cell % 7 + 0.5), y = top + 16 + cell / 7 * ch;
            bool today = d == now.Day;
            if (today) ctx.DrawEllipse(Palette.Brush(red), null, new Point(x, y + 7), 8.5, 8.5);
            var t = Shapes.Text(d.ToString(CultureInfo.InvariantCulture), 10.5, today ? Colors.White : Palette.Text, today ? FontWeight.Bold : FontWeight.Medium);
            ctx.DrawText(t, new Point(x - t.Width / 2, y + 7 - t.Height / 2));
        }
    }

    // ---- weather -------------------------------------------------------------------------------

    private void DrawWeather(DrawingContext ctx, Rect r, RoundedRect shape, DateTime now)
    {
        var w = _weather?.Current ?? SampleWeather;
        bool day = w?.IsDay ?? (now.Hour is >= 7 and < 19);
        var sky = Palette.Dark || !day
            ? Shapes.Vertical(Color.FromRgb(0x0B, 0x12, 0x2B), Color.FromRgb(0x24, 0x2E, 0x52))
            : Shapes.Vertical(Color.FromRgb(0x2D, 0x7B, 0xE0), Color.FromRgb(0x6C, 0xB4, 0xF2));
        ctx.DrawRectangle(sky, null, shape);
        ctx.DrawRectangle(null, new Pen(Palette.Brush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1), shape.Deflate(0.5, 0.5));

        var white = Colors.White;
        var dim = Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF);
        if (w == null)
        {
            ctx.DrawText(Shapes.Text("Weather", 15, white, FontWeight.SemiBold), new Point(r.X + 18, r.Y + 16));
            ctx.DrawText(Shapes.Text("Set \"WeatherCity\" in Orchard Settings", 12, dim), new Point(r.X + 18, r.Y + 44));
            Glyphs.Draw(ctx, Glyph.CloudSun, new Rect(r.Right - 70, r.Y + 14, 52, 52), white);
            return;
        }

        string deg(double t) => $"{Math.Round(t):0}°";
        ctx.DrawText(Shapes.Text(w.City, 15, white, FontWeight.SemiBold), new Point(r.X + 18, r.Y + 14));
        ctx.DrawText(Shapes.Text(deg(w.Temperature), 40, white, FontWeight.Regular), new Point(r.X + 16, r.Y + 32));
        var cond = Shapes.Text(w.Condition, 12, white, FontWeight.SemiBold);
        ctx.DrawText(cond, new Point(r.Right - 18 - cond.Width, r.Y + 46));
        var hl = Shapes.Text($"H:{deg(w.High)} L:{deg(w.Low)}", 12, dim, FontWeight.Medium);
        ctx.DrawText(hl, new Point(r.Right - 18 - hl.Width, r.Y + 62));
        Glyphs.Draw(ctx, GlyphFor(w.Kind, w.IsDay), new Rect(r.Right - 44, r.Y + 14, 26, 26), white);

        double cell = (r.Width - 36) / Math.Max(1, w.Hourly.Count);
        for (int i = 0; i < w.Hourly.Count; i++)
        {
            var h = w.Hourly[i];
            double cx = r.X + 18 + cell * (i + 0.5);
            var time = Shapes.Text(h.Time.ToString("h tt", CultureInfo.InvariantCulture), 10.5, dim, FontWeight.SemiBold);
            ctx.DrawText(time, new Point(cx - time.Width / 2, r.Y + 96));
            Glyphs.Draw(ctx, GlyphFor(h.Kind, h.Time.Hour is >= 7 and < 19), new Rect(cx - 9, r.Y + 113, 18, 18), white);
            var t = Shapes.Text(deg(h.Temperature), 12, white, FontWeight.SemiBold);
            ctx.DrawText(t, new Point(cx - t.Width / 2, r.Y + 135));
        }
    }

    private static Glyph GlyphFor(WeatherKind k, bool day) => k switch
    {
        WeatherKind.Clear => day ? Glyph.Sun : Glyph.Moon,
        WeatherKind.PartlyCloudy => Glyph.CloudSun,
        WeatherKind.Cloudy => Glyph.Cloud,
        WeatherKind.Fog => Glyph.Fog,
        WeatherKind.Rain => Glyph.Rain,
        WeatherKind.Snow => Glyph.Snow,
        _ => Glyph.Bolt,
    };

    // ---- rings (battery, system) ---------------------------------------------------------------

    private static void Ring(DrawingContext ctx, Point c, double radius, double value, Color color, double thickness = 8)
    {
        ctx.DrawEllipse(null, new Pen(Palette.Brush(Palette.Track), thickness), c, radius, radius);
        value = Math.Clamp(value, 0, 1);
        if (value <= 0) return;
        if (value >= 0.999) { ctx.DrawEllipse(null, new Pen(Palette.Brush(color), thickness), c, radius, radius); return; }
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            double a1 = -Math.PI / 2 + value * Math.PI * 2;
            s.BeginFigure(new Point(c.X, c.Y - radius), false);
            s.ArcTo(new Point(c.X + radius * Math.Cos(a1), c.Y + radius * Math.Sin(a1)), new Size(radius, radius), 0, value > 0.5, SweepDirection.Clockwise);
            s.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(Palette.Brush(color), thickness, lineCap: PenLineCap.Round), g);
    }

    private void DrawBattery(DrawingContext ctx, Rect r)
    {
        var b = _platform.Battery;
        ctx.DrawText(Shapes.Text("Battery", 13, Palette.Text, FontWeight.SemiBold), new Point(r.X + 16, r.Y + 14));
        var c = new Point(r.Center.X, r.Y + 92);
        var green = Color.FromRgb(0x34, 0xC7, 0x59);
        if (b == null)
        {
            Ring(ctx, c, 40, 1, green);
            Glyphs.Draw(ctx, Glyph.Power, new Rect(c.X - 14, c.Y - 14, 28, 28), Palette.Text, 2);
            var t = Shapes.Text("Plugged in", 11, Palette.TextSecondary, FontWeight.Medium);
            ctx.DrawText(t, new Point(c.X - t.Width / 2, r.Bottom - 22));
            return;
        }
        var color = b.Percent <= 20 ? Color.FromRgb(0xFF, 0x45, 0x3A) : green;
        Ring(ctx, c, 40, b.Percent / 100.0, color);
        var pct = Shapes.Text($"{b.Percent}%", 20, Palette.Text, FontWeight.Bold);
        ctx.DrawText(pct, new Point(c.X - pct.Width / 2, c.Y - pct.Height / 2));
        var state = Shapes.Text(b.Charging ? "Charging" : "This PC", 11, Palette.TextSecondary, FontWeight.Medium);
        ctx.DrawText(state, new Point(c.X - state.Width / 2, r.Bottom - 22));
    }

    private void DrawSystem(DrawingContext ctx, Rect r)
    {
        var load = _load ?? _platform.Load;
        ctx.DrawText(Shapes.Text("System", 13, Palette.Text, FontWeight.SemiBold), new Point(r.X + 16, r.Y + 14));
        var blue = Color.FromRgb(0x0A, 0x84, 0xFF);
        var purple = Color.FromRgb(0xBF, 0x5A, 0xF2);
        var c = new Point(r.Center.X, r.Y + 92);
        Ring(ctx, c, 44, load?.Cpu ?? 0, blue, 9);
        Ring(ctx, c, 30, load?.Memory ?? 0, purple, 9);
        Glyphs.Draw(ctx, Glyph.Cpu, new Rect(c.X - 10, c.Y - 10, 20, 20), Palette.Text, 1.6);

        var cpu = Shapes.Text($"CPU {(load?.Cpu ?? 0) * 100:0}%", 10.5, blue, FontWeight.Bold);
        var mem = Shapes.Text($"RAM {(load?.Memory ?? 0) * 100:0}%", 10.5, purple, FontWeight.Bold);
        double total = cpu.Width + 10 + mem.Width;
        ctx.DrawText(cpu, new Point(r.Center.X - total / 2, r.Bottom - 22));
        ctx.DrawText(mem, new Point(r.Center.X - total / 2 + cpu.Width + 10, r.Bottom - 22));
    }
}
