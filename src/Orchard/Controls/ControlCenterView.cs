using System.Net.NetworkInformation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Orchard.Platform;
using Orchard.Rendering;
using Orchard.Theme;

namespace Orchard.Controls;

/// <summary>
/// Control Center: glass modules for connectivity, Focus, display and sound. Sliders and toggles act on the real
/// system where Windows exposes it (volume, brightness, dark mode, lock); connectivity modules open the matching
/// Settings page, since Windows doesn't let a normal app flip the Wi-Fi or Bluetooth radios.
/// </summary>
public sealed class ControlCenterView : Control
{
    public const double PanelWidth = 324, Pad = 10, TileGap = 10;

    private readonly IShellPlatform _platform;
    private readonly List<(Rect Rect, Action Action)> _buttons = [];
    private readonly Dictionary<string, Rect> _sliders = [];
    private string? _dragging;
    private Point? _hover;

    public event Action? Dismissed;

    public ControlCenterView(IShellPlatform platform)
    {
        _platform = platform;
        Width = PanelWidth;
        Height = 372;
        Focusable = true;
    }

    private static (bool Up, string Name) Wifi()
    {
        try
        {
            var wifi = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up);
            return wifi != null ? (true, "Connected") : (false, "Not connected");
        }
        catch (NetworkInformationException)
        {
            return (false, "Unavailable");
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _hover = e.GetPosition(this);
        if (_dragging != null) SetSlider(_dragging, _hover.Value.X);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e) { _hover = null; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        foreach (var (name, rect) in _sliders)
            if (rect.Inflate(4).Contains(p))
            {
                _dragging = name;
                e.Pointer.Capture(this);
                SetSlider(name, p.X);
                return;
            }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging != null) { _dragging = null; e.Pointer.Capture(null); return; }
        var p = e.GetPosition(this);
        foreach (var (rect, action) in _buttons.ToList())
            if (rect.Contains(p)) { action(); InvalidateVisual(); return; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismissed?.Invoke(); e.Handled = true; }
    }

    private void SetSlider(string name, double x)
    {
        var r = _sliders[name];
        double v = Math.Clamp((x - r.X) / r.Width, 0, 1);
        if (name == "sound") _platform.Volume = v;
        else if (_platform.Brightness != null) _platform.Brightness = v;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        _buttons.Clear();
        _sliders.Clear();
        var panel = new RoundedRect(new Rect(Bounds.Size), 22);
        Glass.Draw(ctx, this, panel, strong: true, shadow: false);

        double w = Bounds.Width - Pad * 2, half = (w - TileGap) / 2;
        double y = Pad;

        // --- Connectivity (tall tile, left) ---------------------------------------------------
        var conn = new Rect(Pad, y, half, 150);
        Tile(ctx, conn);
        var (wifiUp, wifiText) = Wifi();
        Toggle(ctx, new Point(conn.X + 12, conn.Y + 12), Glyph.Wifi, "Wi-Fi", wifiText, wifiUp, () => _platform.Launch("ms-settings:network-wifi"));
        Toggle(ctx, new Point(conn.X + 12, conn.Y + 58), Glyph.Bluetooth, "Bluetooth", "Settings", true, () => _platform.Launch("ms-settings:bluetooth"));
        Toggle(ctx, new Point(conn.X + 12, conn.Y + 104), Glyph.Airplane, "Airplane", "Settings", false, () => _platform.Launch("ms-settings:network-airplanemode"));

        // --- Focus + Mirroring (right) ----------------------------------------------------------
        var focus = new Rect(Pad + half + TileGap, y, half, 70);
        Tile(ctx, focus);
        Toggle(ctx, new Point(focus.X + 12, focus.Y + 17), Glyph.Moon, "Focus", "Do Not Disturb", false, () => _platform.Launch("ms-settings:quiethours"),
            accent: Color.FromRgb(0x5E, 0x5C, 0xE6));

        var mirror = new Rect(focus.X, y + 80, half, 70);
        Tile(ctx, mirror);
        Toggle(ctx, new Point(mirror.X + 12, mirror.Y + 17), Glyph.Mirror, "Cast", "Screen", false, () => _platform.Launch("ms-settings-connectabledevices:devicediscovery"));
        y += 160;

        // --- Display -----------------------------------------------------------------------------
        var display = new Rect(Pad, y, w, 64);
        Tile(ctx, display);
        Label(ctx, "Display", display.X + 14, display.Y + 9);
        Slider(ctx, "display", new Rect(display.X + 14, display.Y + 32, display.Width - 28, 22), _platform.Brightness, Glyph.Sun);
        y += 74;

        // --- Sound -------------------------------------------------------------------------------
        var sound = new Rect(Pad, y, w, 64);
        Tile(ctx, sound);
        Label(ctx, "Sound", sound.X + 14, sound.Y + 9);
        Slider(ctx, "sound", new Rect(sound.X + 14, sound.Y + 32, sound.Width - 28, 22), _platform.Volume, Glyph.Speaker);
        y += 74;

        // --- Small modules -----------------------------------------------------------------------
        double third = (w - TileGap * 2) / 3;
        SmallTile(ctx, new Rect(Pad, y, third, 44), Glyph.Moon, "Dark Mode", _platform.DarkMode, () => _platform.DarkMode = !_platform.DarkMode);
        SmallTile(ctx, new Rect(Pad + third + TileGap, y, third, 44), Glyph.Lock, "Lock", false, () => { Dismissed?.Invoke(); _platform.Run(SystemAction.LockScreen); });
        SmallTile(ctx, new Rect(Pad + (third + TileGap) * 2, y, third, 44), Glyph.Gear, "Settings", false, () => { Dismissed?.Invoke(); _platform.Run(SystemAction.SystemSettings); });
    }

    private void Tile(DrawingContext ctx, Rect r) =>
        ctx.DrawRectangle(Palette.Brush(Palette.Dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            new Pen(Palette.Brush(Color.FromArgb(0x18, 0, 0, 0)), 0.5), new RoundedRect(r, 16));

    private static void Label(DrawingContext ctx, string text, double x, double y) =>
        ctx.DrawText(Shapes.Text(text, 13, Palette.Text, FontWeight.SemiBold), new Point(x, y));

    private void Toggle(DrawingContext ctx, Point at, Glyph glyph, string title, string subtitle, bool on, Action action, Color? accent = null)
    {
        var circle = new Rect(at.X, at.Y, 34, 34);
        var fill = on ? (accent ?? Palette.Accent) : (Palette.Dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0, 0, 0));
        ctx.DrawEllipse(Palette.Brush(fill), null, circle.Center, 17, 17);
        Glyphs.Draw(ctx, glyph, circle.Deflate(9), on ? Colors.White : Palette.Text, 2);
        ctx.DrawText(Shapes.Text(title, 13, Palette.Text, FontWeight.SemiBold), new Point(at.X + 42, at.Y + 1));
        ctx.DrawText(Shapes.Text(subtitle, 11, Palette.TextSecondary), new Point(at.X + 42, at.Y + 18));
        _buttons.Add((new Rect(at.X, at.Y, 140, 34), action));
    }

    private void SmallTile(DrawingContext ctx, Rect r, Glyph glyph, string title, bool on, Action action)
    {
        bool hot = _hover is { } h && r.Contains(h);
        Tile(ctx, r);
        if (hot) ctx.DrawRectangle(Palette.Brush(Palette.Hover), null, new RoundedRect(r, 16));
        var dot = new Rect(r.X + 8, r.Center.Y - 13, 26, 26);
        ctx.DrawEllipse(Palette.Brush(on ? Palette.Accent : (Palette.Dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0, 0, 0))), null, dot.Center, 13, 13);
        Glyphs.Draw(ctx, glyph, dot.Deflate(6.5), on ? Colors.White : Palette.Text, 2);
        ctx.DrawText(Shapes.Text(title, 11, Palette.Text, FontWeight.Medium), new Point(dot.Right + 6, r.Center.Y - 7));
        _buttons.Add((r, action));
    }

    private void Slider(DrawingContext ctx, string name, Rect r, double? value, Glyph glyph)
    {
        var track = new RoundedRect(r, r.Height / 2);
        ctx.DrawRectangle(Palette.Brush(Palette.Dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0, 0, 0)), null, track);
        if (value is not { } v)
        {
            var na = Shapes.Text("Not available on this display", 11, Palette.TextSecondary);
            ctx.DrawText(na, new Point(r.X + 30, r.Center.Y - na.Height / 2));
            Glyphs.Draw(ctx, glyph, new Rect(r.X + 6, r.Y + 4, r.Height - 8, r.Height - 8), Palette.TextSecondary, 2);
            return;
        }
        double fillW = Math.Max(r.Height, r.Width * v);
        using (ctx.PushClip(track))
            ctx.DrawRectangle(Palette.Brush(Colors.White), null, new RoundedRect(new Rect(r.X, r.Y, fillW, r.Height), r.Height / 2));
        ctx.DrawEllipse(Palette.Brush(Colors.White), new Pen(Palette.Brush(Color.FromArgb(0x30, 0, 0, 0)), 0.5),
            new Point(r.X + fillW - r.Height / 2, r.Center.Y), r.Height / 2, r.Height / 2);
        Glyphs.Draw(ctx, glyph, new Rect(r.X + 5, r.Y + 4, r.Height - 8, r.Height - 8), Color.FromArgb(0x99, 0x3C, 0x3C, 0x43), 2);
        _sliders[name] = r;
    }
}
