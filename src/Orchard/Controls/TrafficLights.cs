using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Orchard.Rendering;
using Orchard.Theme;

namespace Orchard.Controls;

public enum WindowButton { Close, Minimize, Zoom }

/// <summary>
/// The close / minimize / zoom buttons. Coloured when the window is active, grey otherwise; hovering anywhere over
/// the group reveals all three symbols at once, exactly like the real thing.
/// </summary>
public sealed class TrafficLights : Control
{
    public const double Diameter = 12, Spacing = 8;
    public static readonly double GroupWidth = Diameter * 3 + Spacing * 2;

    public bool IsActive { get; set; } = true;
    public bool ForceHover { get; set; }
    public event Action<WindowButton>? Clicked;

    private bool _hover;
    private WindowButton? _pressed;

    public TrafficLights()
    {
        Width = GroupWidth + 4;
        Height = Diameter + 4;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private static Point CenterOf(int i) => new(2 + Diameter / 2 + i * (Diameter + Spacing), 2 + Diameter / 2);

    private static WindowButton? Hit(Point p)
    {
        for (int i = 0; i < 3; i++)
            if (Math.Abs(p.X - CenterOf(i).X) <= Diameter / 2 + Spacing / 2 && Math.Abs(p.Y - CenterOf(i).Y) <= Diameter / 2 + 2)
                return (WindowButton)i;
        return null;
    }

    protected override void OnPointerEntered(PointerEventArgs e) { _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { _hover = false; _pressed = null; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressed = Hit(e.GetPosition(this));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var hit = Hit(e.GetPosition(this));
        if (hit != null && hit == _pressed) Clicked?.Invoke(hit.Value);
        _pressed = null;
        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        bool hover = _hover || ForceHover;
        (Color fill, Color rim, Glyph glyph)[] buttons =
        [
            (Palette.Close, Palette.CloseRim, Glyph.Close),
            (Palette.Minimize, Palette.MinimizeRim, Glyph.Minus),
            (Palette.Zoom, Palette.ZoomRim, Glyph.ZoomArrows),
        ];
        for (int i = 0; i < 3; i++)
        {
            var c = CenterOf(i);
            bool colored = IsActive || hover;
            var fill = colored ? buttons[i].fill : (Palette.Dark ? Color.FromRgb(0x4A, 0x4A, 0x4E) : Palette.Inactive);
            var rim = colored ? buttons[i].rim : (Palette.Dark ? Color.FromRgb(0x3A, 0x3A, 0x3E) : Color.FromRgb(0xB8, 0xB8, 0xBC));
            if (_pressed == (WindowButton)i) fill = Shapes.Lerp(fill, Colors.Black, 0.22);

            ctx.DrawEllipse(Shapes.Vertical(Shapes.Lerp(fill, Colors.White, 0.12), fill), new Pen(Palette.Brush(rim), 0.6), c, Diameter / 2 - 0.3, Diameter / 2 - 0.3);
            if (hover)
            {
                var ink = Color.FromArgb(0x99, 0x3A, 0x0A, 0x05);
                double g = i == 2 ? 7.4 : 7;
                Glyphs.Draw(ctx, buttons[i].glyph, new Rect(c.X - g / 2, c.Y - g / 2, g, g), ink, i == 1 ? 4.6 : 4);
            }
        }
    }
}
