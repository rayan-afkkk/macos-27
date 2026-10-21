using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orchard.Controls;
using Orchard.Model;
using Orchard.Platform;
using Orchard.Platform.Windows;
using Orchard.Rendering;
using Orchard.Services;
using Orchard.Theme;

namespace Orchard.Shell;

/// <summary>
/// The real shell on a Windows desktop. Each surface is its own top-level window (menu bar, dock, window buttons,
/// popovers, genie layer); this class creates them, keeps them in sync with the foreground window, and runs the
/// auto-hide logic.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellHost : IShellCommands, IDisposable
{
    private const int HotkeySpotlight = 1, HotkeyQuit = 2;

    private readonly OrchardSettings _settings;
    private readonly WindowsPlatform _platform;
    private readonly PixelRect _monitor;

    private readonly ShellWindow _menuWindow;
    private readonly MenuBarView _menuBar;
    private readonly ShellWindow _dockWindow;
    private readonly DockControl _dock;
    private readonly ShellWindow _lightsWindow;
    private readonly TrafficLights _lights;
    private AppBar? _appBar;

    private ShellWindow? _popup;
    private bool _popupIsDockMenu;
    private ShellWindow? _genieWindow;

    private readonly EdgeReveal _dockReveal = new();
    private readonly EdgeReveal _menuReveal = new() { RevealDelay = TimeSpan.FromMilliseconds(150) };
    private double _menuOffset; // 0 = shown, 1 = slid above the screen
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _taskbarGuard;
    private DateTime _lastTick = DateTime.UtcNow;

    /// <summary>Snapshots taken at minimize time, replayed in reverse when the window is restored from the dock.</summary>
    private readonly Dictionary<nint, (Bitmap Image, PixelRect Bounds)> _minimized = [];

    public ShellHost(OrchardSettings settings, WindowsPlatform platform)
    {
        _settings = settings;
        _platform = platform;
        _monitor = platform.PrimaryMonitor;
        Palette.Dark = settings.DarkMode || platform.DarkMode;
        MenuCatalog.WindowTitle = Native.GetText;

        SystemState.Capture();
        SystemState.StartWatchdog();
        if (settings.HideWindowsTaskbar) SystemState.HideTaskbar();
        if (settings.MinimizeEffect != MinimizeEffect.System) SystemState.SetMinimizeAnimation(false);

        // ---- menu bar ----
        _menuBar = new MenuBarView(platform) { MenuTitles = MenuCatalog.Titles };
        _menuWindow = new ShellWindow(activatable: false) { Content = _menuBar, Title = "Orchard Menu Bar" };
        _menuWindow.PlaceAt(new PixelRect(_monitor.X, _monitor.Y, _monitor.Width, (int)Math.Ceiling(MenuBarView.BarHeight * _menuWindow.Scale)));
        _menuBar.MenuRequested += (i, _) => { if (i < 0) ClosePopup(); else OpenMenu(i); };
        _menuBar.StatusRequested += (item, _) =>
        {
            if (item == StatusItem.ControlCenter) ToggleControlCenter();
            else if (item == StatusItem.Search) OpenSpotlight();
            else if (item == StatusItem.Wifi) platform.Launch("ms-settings:network");
            else if (item == StatusItem.Battery) platform.Launch("ms-settings:batterysaver");
            else if (item == StatusItem.Clock) platform.Launch("ms-settings:dateandtime");
        };
        _menuWindow.Opened += (_, _) =>
        {
            if (settings.MenuBar != MenuBarVisibility.AlwaysAutoHide)
                _appBar = new AppBar(_menuWindow.Handle, ToRect(_monitor), (int)Math.Ceiling(MenuBarView.BarHeight * _menuWindow.Scale));
            RegisterHotkeys();
        };

        // ---- dock ----
        _dock = new DockControl { IconSize = settings.IconSize, MaxScale = settings.MagnifiedScale, Magnify = settings.Magnification };
        _dockWindow = new ShellWindow(activatable: false) { Content = _dock, Title = "Orchard Dock" };
        int dockHeight = (int)Math.Ceiling(_dock.RequiredHeight * _dockWindow.Scale);
        _dockWindow.PlaceAt(new PixelRect(_monitor.X, _monitor.Bottom - dockHeight, _monitor.Width, dockHeight));
        _dock.Items = platform.DockApps;
        _dock.ItemClicked += OnDockClick;
        _dock.ItemContextRequested += OnDockContext;
        _dock.ShapeChanged += shapes => Dispatcher.UIThread.Post(() => _dockWindow.SetShape(shapes));
        _dockWindow.Opened += (_, _) => { _dockWindow.SetShape([]); _dockWindow.EnableBlur(); };
        platform.DockAppsChanged += () => _dock.Items = platform.DockApps.ToList();

        // ---- traffic lights (follow the foreground window) ----
        _lights = new TrafficLights();
        _lightsWindow = new ShellWindow(activatable: false, topmost: false) { Content = _lights, SizeToContent = SizeToContent.WidthAndHeight, Title = "Orchard Window Buttons" };
        _lights.Clicked += OnLightClicked;
        platform.ForegroundChanged += () => Dispatcher.UIThread.Post(OnForegroundChanged);
        platform.ForegroundMoved += () => Dispatcher.UIThread.Post(PositionLights);

        _menuWindow.Show();
        _dockWindow.Show();
        OnForegroundChanged();

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Normal, (_, _) => Tick());
        _tick.Start();

        // Explorer re-shows the taskbar after restarts, display changes and some Win-key shortcuts; quietly undo that.
        _taskbarGuard = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) =>
        {
            if (_settings.HideWindowsTaskbar && SystemState.TaskbarVisible()) SystemState.HideTaskbar();
        });
        _taskbarGuard.Start();
    }

    private static Native.RECT ToRect(PixelRect r) => new(r.X, r.Y, r.Right, r.Bottom);

    // ---- hotkeys -------------------------------------------------------------------------------

    private void RegisterHotkeys()
    {
        var hwnd = _menuWindow.Handle;
        if (!Native.RegisterHotKey(hwnd, HotkeySpotlight, Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_SPACE))
            Log.Write("Alt+Space is taken by another app (PowerToys Run?) — open Spotlight from the menu bar instead");
        // Emergency exit: Ctrl+Alt+Shift+O always quits Orchard and gives the normal Windows desktop back.
        Native.RegisterHotKey(hwnd, HotkeyQuit, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT | Native.MOD_NOREPEAT, 'O');
        Win32Properties.AddWndProcHookCallback(_menuWindow, (nint h, uint msg, nint wParam, nint lParam, ref bool handled) =>
        {
            if (msg != Native.WM_HOTKEY) return 0;
            handled = true;
            if (wParam == HotkeySpotlight) OpenSpotlight();
            else if (wParam == HotkeyQuit) QuitOrchard();
            return 0;
        });
    }

    // ---- per-frame state -----------------------------------------------------------------------

    private void Tick()
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastTick).TotalSeconds, 0, 0.1);
        _lastTick = now;

        var fg = _platform.ForegroundState(_monitor);
        var pointer = _platform.Pointer;
        bool onMonitor = pointer.X >= _monitor.X && pointer.X < _monitor.Right;
        double s = _dockWindow.Scale;

        // Dock: the area it occupies when shown, so "does a window overlap it" is stable while it is hidden.
        double restingHeight = (_dock.IconSize + DockControl.Padding * 2 + DockControl.BottomMargin) * s;
        var shelf = _dock.ShelfRect;
        var dockArea = new Rect(_monitor.X + shelf.X * s, _monitor.Bottom - restingHeight, shelf.Width * s, restingHeight);
        bool launchpadOpen = _popup?.Content is LaunchpadView;
        bool dockWantsHide = !launchpadOpen && VisibilityRules.DockWantsHide(_settings.Dock, fg, dockArea);
        _dockReveal.Update(now, dockWantsHide, onMonitor && pointer.Y >= _monitor.Bottom - 2, _dock.IsEngaged || _popupIsDockMenu);
        double dockTarget = _dockReveal.Visible ? 0 : 1;
        if (_dock.HiddenTarget != dockTarget)
        {
            _dock.HiddenTarget = dockTarget;
            _dock.Items = _dock.Items; // restart the animation loop
        }

        // Menu bar: slides up out of the way in fullscreen, drops back when the pointer touches the top edge.
        bool menuWantsHide = VisibilityRules.MenuBarWantsHide(_settings.MenuBar, fg);
        bool menuEngaged = _popup != null && !_popupIsDockMenu || pointer.Y < _monitor.Y + MenuBarView.BarHeight * s && onMonitor && _menuOffset < 0.5;
        _menuReveal.Update(now, menuWantsHide, onMonitor && pointer.Y <= _monitor.Y + 1, menuEngaged);
        double menuTarget = _menuReveal.Visible ? 0 : 1;
        if (_menuOffset != menuTarget)
        {
            _menuOffset = DockMath.Approach(_menuOffset, menuTarget, dt, 16);
            _menuWindow.Position = new PixelPoint(_monitor.X, _monitor.Y - (int)Math.Round(_menuOffset * (MenuBarView.BarHeight + 4) * s));
        }
        _menuWindow.Topmost = true;
    }

    // ---- foreground / window buttons -----------------------------------------------------------

    private void OnForegroundChanged()
    {
        var app = _platform.ForegroundApp;
        _menuBar.AppName = app?.Name ?? "File Explorer";
        _menuBar.InvalidateVisual();
        PositionLights();
    }

    private bool LightsWanted(out WindowInfo fg)
    {
        fg = _platform.Foreground!;
        if (!_settings.TrafficLights || _platform.Foreground == null || fg.IsMinimized) return false;
        if (_platform.ForegroundState(_monitor) is { IsFullscreen: true }) return false;
        Native.GetWindowThreadProcessId(fg.Handle, out uint pid);
        var exe = Path.GetFileName(Native.ProcessPath(pid) ?? "");
        return !_settings.TrafficLightExclusions.Contains(exe, StringComparer.OrdinalIgnoreCase);
    }

    private void PositionLights()
    {
        if (!LightsWanted(out var fg))
        {
            if (_lightsWindow.IsVisible) _lightsWindow.Hide();
            return;
        }
        double s = Native.GetDpiForWindow(fg.Handle) / 96.0;
        if (s <= 0) s = 1;
        var at = new PixelPoint(fg.Bounds.X + (int)(10 * s), fg.Bounds.Y + (int)(9 * s));
        _lightsWindow.Position = at;
        if (!_lightsWindow.IsVisible) _lightsWindow.Show();
        _lightsWindow.SetOwner(fg.Handle);
        _lights.IsActive = true;
        _lights.InvalidateVisual();
    }

    private void OnLightClicked(WindowButton button)
    {
        if (_platform.Foreground is not { } fg) return;
        switch (button)
        {
            case WindowButton.Close: _platform.CloseWindow(fg.Handle); break;
            case WindowButton.Minimize: MinimizeWithEffect(fg.Handle, fg.Bounds, _platform.ForegroundApp); break;
            case WindowButton.Zoom: _platform.ToggleZoom(fg.Handle); break;
        }
    }

    // ---- minimize / restore animation ----------------------------------------------------------

    private PixelRect DockTargetFor(AppEntry? app)
    {
        double s = _dockWindow.Scale;
        var r = app != null ? _dock.IconRect(app) : null;
        if (r is not { } icon || _dock.Hidden > 0.5)
            return new PixelRect(_monitor.Center.X - 24, _monitor.Bottom - 8, 48, 8);
        return new PixelRect(_dockWindow.Position.X + (int)(icon.X * s), _dockWindow.Position.Y + (int)(icon.Y * s), (int)(icon.Width * s), (int)(icon.Height * s));
    }

    private void MinimizeWithEffect(nint hwnd, PixelRect bounds, AppEntry? app)
    {
        if (_settings.MinimizeEffect == MinimizeEffect.System || _platform.CaptureWindow(hwnd) is not { } snapshot)
        {
            _platform.MinimizeWindow(hwnd);
            return;
        }
        _minimized[hwnd] = (snapshot, bounds);
        PlayGenie(snapshot, bounds, DockTargetFor(app), reverse: false, onFirstFrame: () => _platform.MinimizeWindow(hwnd), onDone: null);
    }

    private bool TryRestoreWithEffect(AppEntry app)
    {
        var hwnd = app.Windows.FirstOrDefault(w => Native.IsIconic(w) && _minimized.ContainsKey(w));
        if (hwnd == 0) return false;
        var (image, bounds) = _minimized[hwnd];
        _minimized.Remove(hwnd);
        PlayGenie(image, bounds, DockTargetFor(app), reverse: true, onFirstFrame: null, onDone: () => _platform.RestoreWindow(hwnd));
        return true;
    }

    private void PlayGenie(Bitmap image, PixelRect from, PixelRect to, bool reverse, Action? onFirstFrame, Action? onDone)
    {
        _genieWindow ??= new ShellWindow(activatable: false) { Title = "Orchard Genie", IsHitTestVisible = false };
        var view = new GenieView { Image = image, ImageSize = new Size(image.PixelSize.Width, image.PixelSize.Height), UseScale = _settings.MinimizeEffect == MinimizeEffect.Scale };
        _genieWindow.Content = view;
        _genieWindow.PlaceAt(_monitor);
        double s = _genieWindow.Scale;
        Rect Local(PixelRect r) => new((r.X - _monitor.X) / s, (r.Y - _monitor.Y) / s, r.Width / s, r.Height / s);
        view.From = Local(from);
        view.To = Local(to);
        view.Progress = reverse ? 1 : 0;
        _genieWindow.Show();

        var start = DateTime.UtcNow;
        const double duration = 0.5;
        bool first = true;
        void Frame(TimeSpan _)
        {
            if (first) { first = false; onFirstFrame?.Invoke(); }
            double t = Math.Min(1, (DateTime.UtcNow - start).TotalSeconds / duration);
            view.Progress = reverse ? 1 - t : t;
            view.InvalidateVisual();
            if (t < 1) _genieWindow.RequestAnimationFrame(Frame);
            else
            {
                onDone?.Invoke();
                // Let the restored window paint before the snapshot disappears, so there is no flash.
                DispatcherTimer.RunOnce(() => { _genieWindow.Hide(); _genieWindow.Content = null; }, TimeSpan.FromMilliseconds(reverse ? 60 : 0));
            }
        }
        _genieWindow.RequestAnimationFrame(Frame);
    }

    // ---- dock ----------------------------------------------------------------------------------

    private void OnDockClick(AppEntry app)
    {
        switch (app.Kind)
        {
            case AppKind.Launchpad: ToggleLaunchpad(); return;
            case AppKind.Trash: _platform.Activate(app); return;
        }
        if (app.IsRunning && TryRestoreWithEffect(app)) return;
        _platform.Activate(app);
    }

    private void OnDockContext(AppEntry app, Rect iconRect)
    {
        var panel = new MenuPanel(MenuCatalog.ForDockItem(app, _platform, w => { Native.ShowWindowAsync(w, Native.SW_RESTORE); Native.SetForegroundWindow(w); }));
        double s = _dockWindow.Scale;
        var size = MenuPanel.Measure(panel.Items);
        var at = new PixelPoint(
            _dockWindow.Position.X + (int)((iconRect.Center.X - size.Width / 2) * s),
            _dockWindow.Position.Y + (int)((iconRect.Y - size.Height - 10) * s));
        ShowPopup(panel, at, dockMenu: true);
        panel.Dismissed += ClosePopup;
    }

    // ---- popovers ------------------------------------------------------------------------------

    private void ShowPopup(Control content, PixelPoint at, bool dockMenu = false, PixelSize? size = null)
    {
        ClosePopup();
        var window = new ShellWindow(activatable: true) { Content = content, Title = "Orchard" };
        if (size is { } sz) window.PlaceAt(new PixelRect(at, sz));
        else
        {
            window.SizeToContent = SizeToContent.WidthAndHeight;
            // Keep the popover on screen.
            var desired = MenuPanel.Measure(content is MenuPanel mp ? mp.Items : []);
            double s = window.Scale;
            int w = (int)((double.IsNaN(content.Width) ? desired.Width : content.Width) * s);
            window.Position = new PixelPoint(Math.Clamp(at.X, _monitor.X + 6, Math.Max(_monitor.X + 6, _monitor.Right - w - 6)), at.Y);
        }
        window.Deactivated += (_, _) => { if (_popup == window) ClosePopup(); };
        window.Opened += (_, _) =>
        {
            window.EnableBlur();
            window.Activate();
            if (content is SpotlightView sv) sv.FocusInput();
            else if (content is LaunchpadView lv) lv.FocusSearch();
            else content.Focus();
        };
        _popup = window;
        _popupIsDockMenu = dockMenu;
        window.Show();
        if (content is not LaunchpadView) ApplyRoundedShape(window, content);
    }

    private static void ApplyRoundedShape(ShellWindow window, Control content)
    {
        double radius = content switch { ControlCenterView => 22, SpotlightView => 26, _ => 10 };
        void Apply()
        {
            var b = content.Bounds;
            if (b.Width > 0) window.SetShape([new RoundedRect(new Rect(b.Size), radius)]);
        }
        content.LayoutUpdated += (_, _) => Apply();
        Apply();
    }

    private void ClosePopup()
    {
        var popup = _popup;
        _popup = null;
        _popupIsDockMenu = false;
        _menuBar.OpenIndex = -1;
        _menuBar.OpenStatus = null;
        _menuBar.InvalidateVisual();
        popup?.Close();
    }

    private PixelPoint BelowMenuBar(double x) =>
        new(_menuWindow.Position.X + (int)(x * _menuWindow.Scale), _menuWindow.Position.Y + (int)((MenuBarView.BarHeight + 3) * _menuWindow.Scale));

    private void OpenMenu(int index)
    {
        var appName = _menuBar.AppName;
        var panel = new MenuPanel(MenuCatalog.For(index, appName, _platform, this));
        panel.Dismissed += ClosePopup;
        ShowPopup(panel, BelowMenuBar(_menuBar.TitleRect(index).X));
        _menuBar.OpenIndex = index;
        _menuBar.InvalidateVisual();
    }

    private void ToggleControlCenter()
    {
        if (_popup?.Content is ControlCenterView) { ClosePopup(); return; }
        var cc = new ControlCenterView(_platform);
        cc.Dismissed += ClosePopup;
        ShowPopup(cc, BelowMenuBar(_menuBar.Bounds.Width - ControlCenterView.PanelWidth - 10));
        _menuBar.OpenStatus = StatusItem.ControlCenter;
        _menuBar.InvalidateVisual();
    }

    // ---- IShellCommands ------------------------------------------------------------------------

    public void OpenSpotlight()
    {
        if (_popup?.Content is SpotlightView) { ClosePopup(); return; }
        var s = new SpotlightView(_platform);
        s.Dismissed += ClosePopup;
        double scale = _menuWindow.Scale;
        ShowPopup(s, new PixelPoint(_monitor.Center.X - (int)(SpotlightView.PanelWidth / 2 * scale), _monitor.Y + (int)(_monitor.Height * 0.2)));
    }

    public void ToggleLaunchpad()
    {
        if (_popup?.Content is LaunchpadView) { ClosePopup(); return; }
        var lp = new LaunchpadView(_platform);
        lp.Dismissed += ClosePopup;
        ShowPopup(lp, _monitor.Position, size: _monitor.Size);
    }

    public void MinimizeFront()
    {
        if (_platform.Foreground is { } fg) MinimizeWithEffect(fg.Handle, fg.Bounds, _platform.ForegroundApp);
    }

    public void ZoomFront()
    {
        if (_platform.Foreground is { } fg) _platform.ToggleZoom(fg.Handle);
    }

    public void OpenSettingsFile() => _platform.Launch("notepad.exe", $"\"{SettingsStore.FilePath}\"");

    public void QuitOrchard() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    public void Dispose()
    {
        _tick.Stop();
        _taskbarGuard.Stop();
        _appBar?.Dispose();
        _platform.Dispose();
        SystemState.Restore();
    }
}
