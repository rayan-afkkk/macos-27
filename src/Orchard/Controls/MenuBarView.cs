using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Orchard.Platform;
using Orchard.Rendering;
using Orchard.Theme;

namespace Orchard.Controls;

public enum StatusItem { Battery, Wifi, Search, ControlCenter, Clock }

/// <summary>
/// The top bar. Transparent over the wallpaper with a soft scrim for contrast (the current macOS look):
/// logo menu, bold app name, the app's menus, then status items and the clock on the right.
/// </summary>
public sealed class MenuBarView : Control
{
    public const double BarHeight = 28;

    private readonly IShellPlatform _platform;
    private readonly DispatcherTimer _clock;
    private List<(string Title, Rect Rect, bool Bold)> _titles = [];
    private List<(StatusItem Item, Rect Rect)> _status = [];

    public string AppName { get; set; } = "Finder";
    public IReadOnlyList<string> MenuTitles { get; set; } = ["File", "Edit", "View", "Window", "Help"];

    /// <summary>Index into the left-hand titles (0 = logo, 1 = app name, 2+ = menus) of the menu that is open, or -1.</summary>
    public int OpenIndex { get; set; } = -1;
    public StatusItem? OpenStatus { get; set; }
    public DateTime? FixedNow { get; set; }

    /// <summary>A menu should open under the given title; index -1 means close the open one.</summary>
    public event Action<int, Rect>? MenuRequested;
    public event Action<StatusItem, Rect>? StatusRequested;

    public MenuBarView(IShellPlatform platform)
    {
        _platform = platform;
        Height = BarHeight;
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => InvalidateVisual());
        AttachedToVisualTree += (_, _) => _clock.Start();
        DetachedFromVisualTree += (_, _) => _clock.Stop();
    }

    public Rect TitleRect(int index) => index >= 0 && index < _titles.Count ? _titles[index].Rect : default;
    public Rect StatusRect(StatusItem item) => _status.FirstOrDefault(s => s.Item == item).Rect;

    private void Arrange()
    {
        double x = 12;
        _titles = [("logo", new Rect(x, 3, 30, BarHeight - 6), false)];
        x += 34;
        foreach (var (title, bold) in new[] { (AppName, true) }.Concat(MenuTitles.Select(t => (t, false))))
        {
            double w = Shapes.Text(title, 13, Colors.White, bold ? FontWeight.Bold : FontWeight.Medium).Width + 18;
            _titles.Add((title, new Rect(x, 3, w, BarHeight - 6), bold));
            x += w + 1;
        }

        double right = Bounds.Width - 12;
        var clockText = ClockText();
        double cw = Shapes.Text(clockText, 13, Colors.White, FontWeight.Medium).Width + 16;
        _status = [(StatusItem.Clock, new Rect(right - cw, 3, cw, BarHeight - 6))];
        right -= cw + 2;
        foreach (var item in new[] { StatusItem.ControlCenter, StatusItem.Search, StatusItem.Wifi })
        {
            _status.Add((item, new Rect(right - 32, 3, 32, BarHeight - 6)));
            right -= 34;
        }
        if (_platform.Battery != null)
        {
            double bw = 62;
            _status.Add((StatusItem.Battery, new Rect(right - bw, 3, bw, BarHeight - 6)));
        }
    }

    private string ClockText()
    {
        var now = FixedNow ?? DateTime.Now;
        return $"{now:ddd d MMM}  {now:h:mm tt}";
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        for (int i = 0; i < _titles.Count; i++)
            if (_titles[i].Rect.Contains(p))
            {
                // Clicking the title of the menu that is already open closes it (index -1).
                MenuRequested?.Invoke(i == OpenIndex ? -1 : i, _titles[i].Rect);
                e.Handled = true;
                return;
            }
        foreach (var (item, rect) in _status)
            if (rect.Contains(p)) { StatusRequested?.Invoke(item, rect); e.Handled = true; return; }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        // With a menu already open, sliding along the bar switches menus without clicking — a signature macOS behaviour.
        if (OpenIndex < 0) return;
        var p = e.GetPosition(this);
        for (int i = 0; i < _titles.Count; i++)
            if (i != OpenIndex && _titles[i].Rect.Contains(p)) { MenuRequested?.Invoke(i, _titles[i].Rect); return; }
    }

    public override void Render(DrawingContext ctx)
    {
        Arrange();
        // Scrim: just enough darkening at the top edge to keep white text legible over a bright wallpaper or window.
        ctx.DrawRectangle(Shapes.Vertical(Color.FromArgb(0x40, 0, 0, 0), Color.FromArgb(0x08, 0, 0, 0)), null, new Rect(Bounds.Size));

        var ink = Colors.White;
        var shadow = Color.FromArgb(0x50, 0, 0, 0);

        for (int i = 0; i < _titles.Count; i++)
        {
            var (title, rect, bold) = _titles[i];
            if (i == OpenIndex) ctx.DrawRectangle(Palette.Brush(Color.FromArgb(0x40, 255, 255, 255)), null, new RoundedRect(rect, 6));
            if (i == 0)
            {
                Glyphs.Draw(ctx, Glyph.Logo, rect.Deflate(new Thickness(7, 2)).Translate(new Vector(0, 0.6)), shadow, 1.4);
                Glyphs.Draw(ctx, Glyph.Logo, rect.Deflate(new Thickness(7, 2)), ink, 1.4);
                continue;
            }
            DrawLabel(ctx, title, rect, bold ? FontWeight.Bold : FontWeight.Medium, ink, shadow);
        }

        foreach (var (item, rect) in _status)
        {
            if (item == OpenStatus) ctx.DrawRectangle(Palette.Brush(Color.FromArgb(0x40, 255, 255, 255)), null, new RoundedRect(rect, 6));
            switch (item)
            {
                case StatusItem.Clock:
                    DrawLabel(ctx, ClockText(), rect, FontWeight.Medium, ink, shadow);
                    break;
                case StatusItem.Battery when _platform.Battery is { } b:
                {
                    var pct = Shapes.Text($"{b.Percent}%", 12, ink, FontWeight.Medium);
                    ctx.DrawText(pct, new Point(rect.X + 6, rect.Center.Y - pct.Height / 2));
                    var icon = new Rect(rect.Right - 30, rect.Center.Y - 9, 26, 18);
                    Glyphs.Draw(ctx, Glyph.Battery, icon, ink, 1.4);
                    break;
                }
                default:
                {
                    var glyph = item switch { StatusItem.Wifi => Glyph.Wifi, StatusItem.Search => Glyph.Search, _ => Glyph.ControlCenter };
                    var icon = new Rect(rect.Center.X - 8, rect.Center.Y - 8, 16, 16);
                    Glyphs.Draw(ctx, glyph, icon.Translate(new Vector(0, 0.6)), shadow, 2);
                    Glyphs.Draw(ctx, glyph, icon, ink, 2);
                    break;
                }
            }
        }
    }

    private static void DrawLabel(DrawingContext ctx, string text, Rect rect, FontWeight weight, Color ink, Color shadow)
    {
        var s = Shapes.Text(text, 13, shadow, weight);
        var t = Shapes.Text(text, 13, ink, weight);
        var at = new Point(rect.X + (rect.Width - t.Width) / 2, rect.Center.Y - t.Height / 2);
        ctx.DrawText(s, at + new Point(0, 0.7));
        ctx.DrawText(t, at);
    }
}
