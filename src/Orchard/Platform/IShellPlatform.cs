using Avalonia;
using Avalonia.Media.Imaging;
using Orchard.Model;
using Orchard.Rendering;
using Orchard.Services;

namespace Orchard.Platform;

public enum SystemAction { About, SystemSettings, Store, ForceQuit, Sleep, Restart, ShutDown, LockScreen, LogOut, OpenTrash, EmptyTrash }

/// <summary>A keyboard shortcut the menu bar forwards to the app in front (Edit ▸ Copy → Ctrl+C).</summary>
public sealed record Shortcut(bool Ctrl, bool Shift, bool Alt, ushort VirtualKey, string Label)
{
    public static Shortcut WithCtrl(char key, bool shift = false) =>
        new(true, shift, false, char.ToUpperInvariant(key), "Ctrl+" + (shift ? "Shift+" : "") + char.ToUpperInvariant(key));

    public static Shortcut Key(ushort vk, string label, bool alt = false) => new(false, false, alt, vk, label);

    public override string ToString() => Label;
}

public sealed record LaunchItem(string Name, string Target, string? Arguments = null)
{
    /// <summary>Loaded lazily — extracting hundreds of shell icons up front would stall start-up.</summary>
    public IconArt? Icon { get; set; }
}

public sealed record BatteryInfo(int Percent, bool Charging);

/// <summary>A top-level window as the shell sees it, in physical screen pixels.</summary>
public sealed record WindowInfo(nint Handle, string Title, PixelRect Bounds, bool IsMaximized, bool IsMinimized, string AppId);

/// <summary>
/// Everything the shell UI needs from the operating system. <see cref="Windows.WindowsPlatform"/> talks to Win32;
/// <see cref="PreviewPlatform"/> fakes a desktop so the UI can be developed and screenshotted anywhere.
/// </summary>
public interface IShellPlatform : IDisposable
{
    bool IsPreview { get; }

    /// <summary>Dock contents in display order: pinned apps, then running unpinned apps.</summary>
    IReadOnlyList<AppEntry> DockApps { get; }
    event Action? DockAppsChanged;

    /// <summary>The window the user is working in, or null when the desktop / shell has focus.</summary>
    WindowInfo? Foreground { get; }
    AppEntry? ForegroundApp { get; }
    event Action? ForegroundChanged;
    /// <summary>The foreground window moved, resized, maximized or restored.</summary>
    event Action? ForegroundMoved;

    ForegroundInfo? ForegroundState(PixelRect monitor);

    PixelRect PrimaryMonitor { get; }
    PixelPoint Pointer { get; }

    /// <summary>Launch the app, or bring it forward (restoring minimized windows) if it is already running.</summary>
    void Activate(AppEntry app);
    void Launch(string target, string? arguments = null);
    void Quit(AppEntry app);
    void SetPinned(AppEntry app, bool pinned);

    void CloseWindow(nint hwnd);
    void MinimizeWindow(nint hwnd);
    void RestoreWindow(nint hwnd);
    void ToggleZoom(nint hwnd);
    /// <summary>Snapshot of a window's pixels for the minimize animation.</summary>
    Bitmap? CaptureWindow(nint hwnd);

    void SendShortcut(Shortcut shortcut);
    void Run(SystemAction action);

    double? Volume { get; set; }
    /// <summary>Built-in display brightness 0–1, or null when the monitor doesn't expose it (most desktop screens).</summary>
    double? Brightness { get; set; }
    /// <summary>The Windows app/system dark mode switch.</summary>
    bool DarkMode { get; set; }
    BatteryInfo? Battery { get; }

    IReadOnlyList<LaunchItem> InstalledApps { get; }
    IconArt IconFor(LaunchItem item);
}
