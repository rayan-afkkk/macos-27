using Avalonia;
using Orchard.Model;

namespace Orchard.Services;

/// <summary>What the shell knows about the window the user is working in.</summary>
public readonly record struct ForegroundInfo(Rect Bounds, bool IsMaximized, bool IsFullscreen, bool IsAppWindow);

public static class VisibilityRules
{
    public static bool DockWantsHide(DockVisibility mode, ForegroundInfo? fg, Rect dockArea) => mode switch
    {
        DockVisibility.AlwaysShow => false,
        DockVisibility.AlwaysAutoHide => true,
        _ => fg is { IsAppWindow: true } f && (f.IsFullscreen || f.IsMaximized || f.Bounds.Intersects(dockArea)),
    };

    public static bool MenuBarWantsHide(MenuBarVisibility mode, ForegroundInfo? fg) => mode switch
    {
        MenuBarVisibility.AlwaysShow => false,
        MenuBarVisibility.AlwaysAutoHide => true,
        MenuBarVisibility.HideWhenMaximized => fg is { IsAppWindow: true } f && (f.IsFullscreen || f.IsMaximized),
        _ => fg is { IsAppWindow: true, IsFullscreen: true },
    };

    /// <summary>
    /// A window counts as fullscreen when it covers the whole monitor (borderless games, F11 browsers,
    /// video players) — Windows does not mark these as maximized, so a rect comparison is needed.
    /// </summary>
    public static bool CoversMonitor(Rect window, Rect monitor, double tolerance = 2) =>
        window.X <= monitor.X + tolerance && window.Y <= monitor.Y + tolerance &&
        window.Right >= monitor.Right - tolerance && window.Bottom >= monitor.Bottom - tolerance;
}

/// <summary>
/// Hide/reveal state machine for a bar that lives on a screen edge (dock: bottom, menu bar: top).
/// Revealing needs the pointer to rest on the edge briefly so reaching for a scrollbar doesn't pop the dock;
/// hiding waits a moment after the pointer leaves so the bar doesn't flicker.
/// </summary>
public sealed class EdgeReveal
{
    public TimeSpan RevealDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan HideDelay { get; init; } = TimeSpan.FromMilliseconds(650);

    public bool Visible { get; private set; } = true;

    private DateTime? _edgeSince;
    private DateTime _lastEngaged;
    private bool _wantedHide;

    /// <param name="wantsHide">The visibility rules say this bar should get out of the way.</param>
    /// <param name="pointerAtEdge">The pointer is touching the screen edge the bar lives on.</param>
    /// <param name="engaged">The pointer is over the bar, a menu from it is open, or a drag is hovering it.</param>
    /// <returns>True when <see cref="Visible"/> changed.</returns>
    public bool Update(DateTime now, bool wantsHide, bool pointerAtEdge, bool engaged)
    {
        bool before = Visible;

        if (!wantsHide)
        {
            Visible = true;
            _edgeSince = null;
            _wantedHide = false;
            return before != Visible;
        }

        // The hide countdown starts when hiding becomes wanted, not from whenever the bar was last touched.
        if (!_wantedHide) { _wantedHide = true; _lastEngaged = now; }

        if (engaged || (Visible && pointerAtEdge)) _lastEngaged = now;

        if (Visible)
        {
            if (now - _lastEngaged >= HideDelay) Visible = false;
        }
        else if (pointerAtEdge)
        {
            _edgeSince ??= now;
            if (now - _edgeSince.Value >= RevealDelay)
            {
                Visible = true;
                _lastEngaged = now;
            }
        }

        if (!pointerAtEdge) _edgeSince = null;
        return before != Visible;
    }
}
