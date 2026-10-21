using Avalonia;
using Avalonia.Media.Imaging;
using Orchard.Model;
using Orchard.Rendering;
using Orchard.Services;

namespace Orchard.Platform;

/// <summary>
/// A fake desktop with a fixed set of apps. Used on non-Windows machines, by <c>--preview</c>, and by the
/// screenshot tool so the real UI controls can be rendered without a Windows session.
/// </summary>
public sealed class PreviewPlatform : IShellPlatform
{
    private readonly List<AppEntry> _apps;

    public PreviewPlatform(PixelSize screen)
    {
        PrimaryMonitor = new PixelRect(screen);
        AppEntry App(string id, string name, string icon, bool running = false, bool pinned = true) => new()
        {
            Id = id, Name = name, Icon = new VectorIcon(icon), IsPinned = pinned, PreviewRunning = running, Target = id,
        };
        _apps =
        [
            App("files", "Files", "files", running: true),
            new AppEntry { Id = "orchard:launchpad", Name = "Launchpad", Icon = new VectorIcon("launchpad"), Kind = AppKind.Launchpad, IsPinned = true },
            App("browser", "Browser", "browser", running: true),
            App("mail", "Mail", "mail"),
            App("messages", "Messages", "messages", running: true),
            App("maps", "Maps", "maps"),
            App("photos", "Photos", "photos"),
            App("calendar", "Calendar", "calendar"),
            App("notes", "Notes", "notes", running: true),
            App("music", "Music", "music", running: true),
            App("store", "Store", "store"),
            App("settings", "System Settings", "settings"),
            App("code", "Code", "code", running: true, pinned: false),
            App("terminal", "Terminal", "terminal", running: true, pinned: false),
            new AppEntry { Id = "orchard:trash", Name = "Recycle Bin", Icon = new VectorIcon("trash"), Kind = AppKind.Trash, IsPinned = true },
        ];
        ForegroundApp = _apps[2];
        InstalledApps = new[]
        {
            "Files", "Browser", "Mail", "Messages", "Maps", "Photos", "Calendar", "Notes", "Music", "Store", "System Settings",
            "Code", "Terminal", "Calculator", "Clock", "Weather", "Camera", "Paint", "Snipping Tool", "Task Manager",
            "Control Panel", "Command Prompt", "PowerShell", "Notepad", "Sound Recorder", "Character Map",
        }.Select(n => new LaunchItem(n, n.ToLowerInvariant())).ToList();
    }

    public bool IsPreview => true;
    public IReadOnlyList<AppEntry> DockApps => _apps;
    public event Action? DockAppsChanged;
    public event Action? ForegroundChanged;
    public event Action? ForegroundMoved { add { } remove { } }

    public WindowInfo? Foreground { get; set; }
    public AppEntry? ForegroundApp { get; set; }

    /// <summary>Lets the screenshot tool pretend a window is maximized / fullscreen.</summary>
    public ForegroundInfo? SimulatedForeground { get; set; }
    public ForegroundInfo? ForegroundState(PixelRect monitor) => SimulatedForeground;

    public PixelRect PrimaryMonitor { get; }
    public PixelPoint Pointer { get; set; }

    public void Activate(AppEntry app)
    {
        if (app.Kind != AppKind.Normal) return;
        app.PreviewRunning = true;
        ForegroundApp = app;
        ForegroundChanged?.Invoke();
        DockAppsChanged?.Invoke();
    }

    public void Launch(string target, string? arguments = null) { }

    public void Quit(AppEntry app)
    {
        app.PreviewRunning = false;
        if (!app.IsPinned) _apps.Remove(app);
        DockAppsChanged?.Invoke();
    }

    public void SetPinned(AppEntry app, bool pinned)
    {
        app.IsPinned = pinned;
        if (!pinned && !app.IsRunning) _apps.Remove(app);
        DockAppsChanged?.Invoke();
    }

    public void CloseWindow(nint hwnd) { }
    public void MinimizeWindow(nint hwnd) { }
    public void RestoreWindow(nint hwnd) { }
    public void ToggleZoom(nint hwnd) { }
    public Bitmap? CaptureWindow(nint hwnd) => null;
    public void SendShortcut(Shortcut shortcut) { }
    public void Run(SystemAction action) { }

    public double? Volume { get; set; } = 0.62;
    public double? Brightness { get; set; } = 0.8;
    public bool DarkMode { get; set; }
    public BatteryInfo? Battery => new(86, false);

    public IReadOnlyList<LaunchItem> InstalledApps { get; }

    public IconArt IconFor(LaunchItem item)
    {
        if (item.Icon != null) return item.Icon;
        var kind = item.Name.ToLowerInvariant() switch
        {
            "system settings" or "control panel" => "settings",
            "command prompt" or "powershell" or "terminal" => "terminal",
            var n when VectorIcon.Kinds.Contains(n) => n,
            "files" => "files",
            _ => "app",
        };
        return item.Icon = new VectorIcon(kind);
    }

    public void Dispose() { }
}
