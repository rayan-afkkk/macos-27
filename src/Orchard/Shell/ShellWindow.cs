using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Orchard.Platform.Windows;

namespace Orchard.Shell;

/// <summary>
/// Base for every shell surface on the real desktop: borderless, transparent, hidden from Alt+Tab and the taskbar.
/// Non-activating surfaces (dock, menu bar, window buttons) never steal focus from the app you are using.
/// </summary>
public class ShellWindow : Window
{
    private readonly bool _activatable;

    public ShellWindow(bool activatable, bool topmost = true)
    {
        _activatable = activatable;
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = topmost;
        ShowActivated = activatable;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;

        if (OperatingSystem.IsWindows())
        {
            uint add = (uint)Native.WS_EX_TOOLWINDOW | (_activatable ? 0 : (uint)Native.WS_EX_NOACTIVATE);
            uint remove = (uint)Native.WS_EX_APPWINDOW;
            Win32Properties.AddWindowStylesCallback(this, (style, ex) => (style, (ex | add) & ~remove));
        }
    }

    public nint Handle => TryGetPlatformHandle()?.Handle ?? 0;

    /// <summary>DPI scale of the screen this window is (or will be) on.</summary>
    public double Scale => Screens.ScreenFromPoint(Position)?.Scaling ?? Screens.Primary?.Scaling ?? 1;

    /// <summary>Positions the window in physical pixels.</summary>
    public void PlaceAt(PixelRect r)
    {
        Position = r.Position;
        var s = Screens.ScreenFromPoint(r.Center)?.Scaling ?? Scale;
        Width = r.Width / s;
        Height = r.Height / s;
    }

    /// <summary>
    /// Limits the window to the given rounded shapes (in DIPs). Outside them the window is fully click-through and,
    /// when blur is on, unblurred — so the dock's hit area and frosted backdrop follow the magnified icons exactly.
    /// </summary>
    public void SetShape(IEnumerable<RoundedRect> shapes)
    {
        if (!OperatingSystem.IsWindows() || Handle == 0) return;
        double s = RenderScaling;
        nint region = Native.CreateRectRgn(0, 0, 0, 0);
        foreach (var shape in shapes)
        {
            var r = shape.Rect;
            int d = (int)Math.Round(shape.RadiiTopLeft.X * 2 * s);
            nint part = Native.CreateRoundRectRgn((int)Math.Floor(r.X * s), (int)Math.Floor(r.Y * s),
                (int)Math.Ceiling(r.Right * s) + 1, (int)Math.Ceiling(r.Bottom * s) + 1, d, d);
            Native.CombineRgn(region, region, part, Native.RGN_OR);
            Native.DeleteObject(part);
        }
        // The system owns the region after this call; we must not delete it.
        Native.SetWindowRgn(Handle, region, true);
    }

    public void ClearShape()
    {
        if (OperatingSystem.IsWindows() && Handle != 0) Native.SetWindowRgn(Handle, 0, true);
    }

    /// <summary>Compositor blur behind the window (Windows 10 and 11). The glass tint is painted by the controls.</summary>
    public void EnableBlur()
    {
        if (!OperatingSystem.IsWindows() || Handle == 0) return;
        var accent = new Native.ACCENT_POLICY { AccentState = 3 /* ACCENT_ENABLE_BLURBEHIND */ };
        int size = Marshal.SizeOf<Native.ACCENT_POLICY>();
        nint ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new Native.WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
            Native.SetWindowCompositionAttribute(Handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        // Windows 11 would otherwise round and shadow the rectangular window behind our own shapes.
        int pref = Native.DWMWCP_DONOTROUND;
        Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    /// <summary>Makes this window "owned" by another app's window so it stays directly above it in z-order.</summary>
    public void SetOwner(nint owner)
    {
        if (OperatingSystem.IsWindows() && Handle != 0) Native.SetWindowLongPtr(Handle, -8 /* GWLP_HWNDPARENT */, owner);
    }
}
