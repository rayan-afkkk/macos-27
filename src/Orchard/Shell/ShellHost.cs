using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private readonly ShellWindow? _widgetsWindow;
    private readonly WidgetBoard? _widgets;
    private readonly WeatherService _weather;
    private int _appearancePoll;
    private PixelRect _dockRect, _widgetsRect;
    private bool _dockExpanded;
    private DateTime _dockExpandedUntil;
    private int _placementPoll;

    private ShellWindow? _popup;
    private bool _popupIsDockMenu;
    private ShellWindow? _genieWindow;

    // Like the Windows 11 auto-hide taskbar: touch the bottom edge and the dock is there.
    private readonly EdgeReveal _dockReveal = new() { RevealDelay = TimeSpan.FromMilliseconds(40) };
    private readonly EdgeReveal _menuReveal = new() { RevealDelay = TimeSpan.FromMilliseconds(220) };
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
        Palette.Solid = true;
        Palette.Dark = settings.Appearance switch { Appearance.Dark => true, Appearance.Light => false, _ => platform.DarkMode };
        MenuCatalog.WindowTitle = Native.GetText;

        SystemState.Capture();
        SystemState.StartWatchdog();
        if (settings.HideWindowsTaskbar) SystemState.HideTaskbar();
        if (settings.MinimizeEffect != MinimizeEffect.System) SystemState.SetMinimizeAnimation(false);

        // ---- menu bar ----
        _menuBar = new MenuBarView(platform) { MenuTitles = MenuCatalog.Titles, Solid = true };
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
            // Only reserve the strip when the bar stays put; otherwise maximized apps should get the full screen
            // and the bar slides over them on demand.
            if (settings.MenuBar is MenuBarVisibility.AlwaysShow or MenuBarVisibility.HideInFullscreen)
            {
                _appBar = new AppBar(_menuWindow.Handle, ToRect(_monitor), (int)Math.Ceiling(MenuBarView.BarHeight * _menuWindow.Scale));
                // Windows that were already maximized still cover the new strip; give the work area a moment, then refit them.
                DispatcherTimer.RunOnce(platform.FitAllMaximized, TimeSpan.FromMilliseconds(600));
            }
            RegisterHotkeys();
        };

        // ---- dock ----
        _dock = new DockControl { IconSize = settings.IconSize, MaxScale = settings.MagnifiedScale, Magnify = settings.Magnification };
        _dockWindow = new ShellWindow(activatable: false) { Content = _dock, Title = "Orchard Dock" };
        _dock.Items = platform.DockApps;
        // The dock window hugs the shelf and only grows (upwards and sideways) while you hover it, so it never
        // blocks clicks on the strip of screen around it. See UpdateDockWindow.
        _dockRect = DockRect(expanded: false);
        _dockWindow.PlaceAt(_dockRect);
        _dock.ItemClicked += OnDockClick;
        _dock.ItemContextRequested += OnDockContext;
        _dock.ShapeChanged += shapes => Dispatcher.UIThread.Post(() => _dockWindow.SetShape(shapes));
        _dockWindow.Opened += (_, _) => _dockWindow.EnsurePlacement(_dockRect);
        platform.DockAppsChanged += () => _dock.Items = platform.DockApps.ToList();

        // ---- traffic lights (follow the foreground window) ----
        _lights = new TrafficLights();
        _lightsWindow = new ShellWindow(activatable: false, topmost: false) { Content = _lights, SizeToContent = SizeToContent.WidthAndHeight, Title = "Orchard Window Buttons" };
        _lights.Clicked += OnLightClicked;
        platform.ForegroundChanged += () => Dispatcher.UIThread.Post(OnForegroundChanged);
        platform.ForegroundMoved += () => Dispatcher.UIThread.Post(PositionLights);

        // ---- desktop widgets (bottom of the z-order, under every window) ----
        _weather = new WeatherService(settings);
        if (settings.ShowWidgets && settings.Widgets.Count > 0)
        {
            var widgets = _widgets = new WidgetBoard(platform, _weather) { Kinds = settings.Widgets };
            var window = _widgetsWindow = new ShellWindow(activatable: false, topmost: false) { Content = widgets, Title = "Orchard Widgets" };
            double ws = window.Scale;
            _widgetsRect = new PixelRect(_monitor.X + (int)(24 * ws), _monitor.Y + (int)((MenuBarView.BarHeight + 18) * ws),
                (int)Math.Ceiling(WidgetBoard.BoardWidth * ws) + 2, (int)Math.Ceiling(widgets.Height * ws) + 2);
            window.PlaceAt(_widgetsRect);
            // No compositor blur here: on some PCs it ignores the window's shape and frosts the whole rectangle.
            window.Opened += (_, _) => { window.EnsurePlacement(_widgetsRect); window.SetShape(widgets.TileShapes); SendWidgetsToBottom(); };
            widgets.ShapeChanged += shapes => Dispatcher.UIThread.Post(() => window.SetShape(shapes));
            widgets.WidgetMenuRequested += (kind, rect) =>
            {
                var panel = new MenuPanel(MenuCatalog.ForWidget(() =>
                {
                    settings.Widgets.Remove(kind);
                    SettingsStore.Save(settings);
                    widgets.Kinds = settings.Widgets.ToList();
                }, OpenSettings));
                panel.Dismissed += ClosePopup;
                double s = window.Scale;
                ShowPopup(panel, new PixelPoint(window.Position.X + (int)((rect.X + 24) * s), window.Position.Y + (int)((rect.Y + 24) * s)));
            };
        }

        Palette.Changed += () => Dispatcher.UIThread.Post(RepaintAll);

        _menuWindow.Show();
        _dockWindow.Show();
        _widgetsWindow?.Show();
        OnForegroundChanged();

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Normal, (_, _) => Tick());
        _tick.Start();

        // Explorer re-shows the taskbar when the pointer touches the bottom edge (auto-hide), after restarts and on some
        // Win-key shortcuts. Hide it the instant it appears (event hook), with a fast poll as a safety net.
        platform.TaskbarShown += () =>
        {
            if (_settings.HideWindowsTaskbar && SystemState.TaskbarVisible()) SystemState.HideTaskbar();
        };
        _taskbarGuard = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            if (_settings.HideWindowsTaskbar && SystemState.TaskbarVisible()) SystemState.HideTaskbar();
        });
        _taskbarGuard.Start();
    }

    private static Native.RECT ToRect(PixelRect r) => new(r.X, r.Y, r.Right, r.Bottom);

    /// <summary>
    /// The dock window's rectangle in pixels, centred at the bottom of the monitor. At rest it is exactly the shelf;
    /// expanded, it adds headroom for magnified icons and the hover label.
    /// </summary>
    private PixelRect DockRect(bool expanded)
    {
        double s = _dockWindow.Scale;
        double w = _dock.RestingShelfWidth + (expanded ? 2 * _dock.IconSize * _dock.MaxScale : 0);
        double h = expanded ? _dock.RequiredHeight : _dock.ShelfHeightAtRest + DockControl.BottomMargin;
        int pw = Math.Min(_monitor.Width, (int)Math.Ceiling(w * s)), ph = (int)Math.Ceiling(h * s);
        return new PixelRect(_monitor.X + (_monitor.Width - pw) / 2, _monitor.Bottom - ph, pw, ph);
    }

    private void UpdateDockWindow(PixelRect rest, PixelPoint pointer)
    {
        // Fully slid away: hide the window outright so nothing at the bottom edge is blocked.
        if (_dock.HiddenTarget == 1 && _dock.Hidden > 0.98)
        {
            if (_dockWindow.IsVisible) _dockWindow.Hide();
            return;
        }
        var expandedRect = DockRect(expanded: true);
        bool wantExpand = _dock.Hidden < 0.5 &&
                          (rest.Contains(pointer) || (_dockExpanded && expandedRect.Contains(pointer)) || _dock.IsMagnified || _popupIsDockMenu);
        // Grow immediately, shrink only after things have been still for a moment, so the window doesn't flap.
        if (wantExpand) _dockExpandedUntil = DateTime.UtcNow.AddMilliseconds(400);
        bool expand = wantExpand || (_dockExpanded && DateTime.UtcNow < _dockExpandedUntil && _dock.Hidden < 0.5);
        var want = expand ? expandedRect : rest;
        if (want == _dockRect) return;
        _dockRect = want;
        _dockExpanded = expand;
        _dockWindow.EnsurePlacement(want);
    }

    /// <summary>Widgets live on the desktop layer: keep them beneath every app window.</summary>
    private void SendWidgetsToBottom()
    {
        if (_widgetsWindow is { Handle: not 0 } w)
            Native.SetWindowPos(w.Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Light ↔ dark: every surface reads the palette when it paints, so a repaint is all it takes.</summary>
    private void RepaintAll()
    {
        foreach (var window in new[] { _menuWindow, _dockWindow, _lightsWindow, _widgetsWindow, _popup })
            if (window?.Content is Visual root)
            {
                root.InvalidateVisual();
                foreach (var v in root.GetVisualDescendants()) v.InvalidateVisual();
            }
    }

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

        // Appearance "Auto" follows the Windows light/dark switch (checked every ~2 s; it's a registry read).
        if (_settings.Appearance == Appearance.Auto && ++_appearancePoll % 60 == 0) Palette.Dark = _platform.DarkMode;

        var fg = _platform.ForegroundState(_monitor);
        var pointer = _platform.Pointer;
        bool onMonitor = pointer.X >= _monitor.X && pointer.X < _monitor.Right;
        double s = _dockWindow.Scale;

        // Dock: the area it occupies when shown, so "does a window overlap it" is stable while it is hidden.
        var rest = DockRect(expanded: false);
        var dockArea = new Rect(rest.X, rest.Y, rest.Width, rest.Height);
        bool launchpadOpen = _popup?.Content is LaunchpadView;
        bool dockWantsHide = !launchpadOpen && VisibilityRules.DockWantsHide(_settings.Dock, fg, dockArea);
        _dockReveal.Update(now, dockWantsHide, onMonitor && pointer.Y >= _monitor.Bottom - 2,
            _dock.IsEngaged || _popupIsDockMenu || rest.Contains(pointer));
        double dockTarget = _dockReveal.Visible ? 0 : 1;
        if (_dock.HiddenTarget != dockTarget)
        {
            _dock.HiddenTarget = dockTarget;
            if (dockTarget == 0 && !_dockWindow.IsVisible) _dockWindow.Show();
            _dock.Items = _dock.Items; // restart the animation loop
        }
        UpdateDockWindow(rest, pointer);

        // Windows sometimes nudges borderless windows (e.g. when the menu bar's app-bar changes the work area).
        if (++_placementPoll % 30 == 0)
        {
            _dockWindow.EnsurePlacement(_dockRect);
            _widgetsWindow?.EnsurePlacement(_widgetsRect);
            if (_menuOffset == 0 && _menuReveal.Visible)
                _menuWindow.EnsurePlacement(new PixelRect(_monitor.X, _monitor.Y, _monitor.Width, (int)Math.Ceiling(MenuBarView.BarHeight * _menuWindow.Scale)));
        }

        // Menu bar: slides up out of the way in fullscreen, drops back when the pointer touches the top edge.
        bool menuWantsHide = VisibilityRules.MenuBarWantsHide(_settings.MenuBar, fg);
        bool menuEngaged = _popup != null && !_popupIsDockMenu || pointer.Y < _monitor.Y + MenuBarView.BarHeight * s && onMonitor && _menuOffset < 0.5;
        // The top-right corner is where a maximized app's own close/maximize buttons live — never pop the bar over them.
        bool atTop = onMonitor && pointer.Y <= _monitor.Y + 1 && pointer.X < _monitor.Right - 170 * s;
        _menuReveal.Update(now, menuWantsHide, atTop, menuEngaged);
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
        SendWidgetsToBottom();
        if (_platform.Foreground is { IsMaximized: true } fg && _appBar != null) _platform.FitMaximizedToWorkArea(fg.Handle);
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
            window.EnableBlur(roundCorners: true);
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

    private SettingsWindow? _settingsWindow;

    /// <summary>Opens Orchard Settings on a copy of the settings, so Cancel really discards the edits.</summary>
    public void OpenSettings()
    {
        ClosePopup();
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        var copy = SettingsStore.Deserialize(SettingsStore.Serialize(_settings)) ?? new OrchardSettings();
        _settingsWindow = new SettingsWindow(copy, canRestart: true);
        _settingsWindow.Show();
    }

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
