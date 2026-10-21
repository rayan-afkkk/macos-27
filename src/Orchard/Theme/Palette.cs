using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Orchard.Theme;

/// <summary>
/// Colour and type tokens shared by every shell surface. The custom-drawn controls read from here
/// directly so the dock, menu bar and popovers stay consistent in light and dark appearance.
/// </summary>
public static class Palette
{
    public static bool Dark { get; set; }

    public static readonly FontFamily Font = new("avares://Orchard/Assets/Fonts#Inter, Segoe UI Variable Text, Segoe UI");

    // Glass: a translucent tint laid over the OS blur (or over the pre-blurred wallpaper in preview).
    public static Color GlassTint => Dark ? Color.FromArgb(0x70, 0x1E, 0x1E, 0x22) : Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF);
    public static Color GlassTintStrong => Dark ? Color.FromArgb(0xC8, 0x26, 0x26, 0x2B) : Color.FromArgb(0xC4, 0xF6, 0xF6, 0xF8);
    public static Color GlassRimTop => Dark ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);
    public static Color GlassRimBottom => Dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF);
    public static Color GlassOutline => Dark ? Color.FromArgb(0x66, 0x00, 0x00, 0x00) : Color.FromArgb(0x26, 0x00, 0x00, 0x00);
    public static Color GlassShadow => Color.FromArgb(Dark ? (byte)0x70 : (byte)0x38, 0, 0, 0);

    public static Color Text => Dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F);
    public static Color TextSecondary => Dark ? Color.FromArgb(0xA6, 0xEB, 0xEB, 0xF5) : Color.FromArgb(0x99, 0x3C, 0x3C, 0x43);
    public static Color Separator => Dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0x00, 0x00, 0x00);
    public static Color Hover => Dark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00);

    public static readonly Color Accent = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static Color MenuBarText => Dark ? Colors.White : Color.FromRgb(0x10, 0x10, 0x12);

    // Window controls ("traffic lights").
    public static readonly Color Close = Color.FromRgb(0xFF, 0x5F, 0x57);
    public static readonly Color CloseRim = Color.FromRgb(0xE0, 0x44, 0x3E);
    public static readonly Color Minimize = Color.FromRgb(0xFE, 0xBC, 0x2E);
    public static readonly Color MinimizeRim = Color.FromRgb(0xDE, 0xA1, 0x23);
    public static readonly Color Zoom = Color.FromRgb(0x28, 0xC8, 0x40);
    public static readonly Color ZoomRim = Color.FromRgb(0x1A, 0xAB, 0x29);
    public static readonly Color Inactive = Color.FromRgb(0xCD, 0xCD, 0xD0);

    public static IBrush Brush(Color c) => new ImmutableSolidColorBrush(c);

    public static IBrush RimBrush() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(GlassRimTop, 0), new GradientStop(GlassRimBottom, 0.45), new GradientStop(GlassRimBottom, 1) }
    };

    public static Typeface Typeface(FontWeight weight = FontWeight.Regular) => new(Font, FontStyle.Normal, weight);
}
