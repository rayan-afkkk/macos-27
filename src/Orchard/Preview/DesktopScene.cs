using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Orchard.Controls;
using Orchard.Model;
using Orchard.Platform;
using Orchard.Rendering;
using Orchard.Services;
using Orchard.Shell;

namespace Orchard.Preview;

/// <summary>
/// The whole shell composed inside one window, over fake app windows: used by <c>Orchard --preview</c> and by the
/// screenshot tool. Every control here is the same class the real Windows shell uses.
/// </summary>
public sealed class DesktopScene : Canvas, IShellCommands
{
    public PreviewPlatform Platform { get; }
    public MenuBarView MenuBar { get; }
    public DockControl Dock { get; }
    public List<FakeWindow> Windows { get; } = [];
    private readonly Canvas _overlay = new();
    private readonly OrchardSettings _settings;
    private readonly EdgeReveal _dockReveal = new();
    private readonly DispatcherTimer _poll;
    private Point _pointer;
    private Control? _popup;

    public double ScreenWidth { get; }
    public double ScreenHeight { get; }

    public DesktopScene(PixelSize screen, OrchardSettings settings, double renderScale = 1)
    {
        _settings = settings;
        ScreenWidth = screen.Width;
        ScreenHeight = screen.Height;
        Width = ScreenWidth;
        Height = ScreenHeight;
        ClipToBounds = true;
        Platform = new PreviewPlatform(screen);

        var wallpaper = new WallpaperArt { Dark = Orchard.Theme.Palette.Dark }.Render(new PixelSize((int)(screen.Width * renderScale), (int)(screen.Height * renderScale)), renderScale);
        Glass.PreviewBackdrop = Rendering.Blur.Apply(wallpaper, (int)(22 * renderScale));
        Glass.PreviewBackdropScale = renderScale;
        Children.Add(new Image { Source = wallpaper, Width = ScreenWidth, Height = ScreenHeight });

        AddWindow(new FakeWindow("Notes", FakeContent.Notes) { Width = 640, Height = 430, Active = false }, 120, 110);
        AddWindow(new FakeWindow("orchard.dev", FakeContent.Browser) { Width = 940, Height = 600 }, ScreenWidth / 2 - 380, 80);

        MenuBar = new MenuBarView(Platform) { Width = ScreenWidth, AppName = "Browser", MenuTitles = MenuCatalog.Titles };
        MenuBar.MenuRequested += (i, r) => { if (i < 0) ClosePopup(); else OpenMenu(i); };
        MenuBar.StatusRequested += (s, r) =>
        {
            if (s == StatusItem.ControlCenter) OpenControlCenter();
            else if (s == StatusItem.Search) OpenSpotlight();
        };
        Children.Add(MenuBar);

        Dock = new DockControl
        {
            Items = Platform.DockApps, Width = ScreenWidth,
            IconSize = settings.IconSize, MaxScale = settings.MagnifiedScale, Magnify = settings.Magnification,
        };
        Dock.Height = Dock.RequiredHeight;
        SetTop(Dock, ScreenHeight - Dock.Height);
        Dock.ItemClicked += app =>
        {
            if (app.Kind == AppKind.Launchpad) ToggleLaunchpad();
            else Platform.Activate(app);
        };
        Dock.ItemContextRequested += (app, rect) =>
            ShowPopup(new MenuPanel(MenuCatalog.ForDockItem(app, Platform, _ => { })), rect.Center.X - 100, ScreenHeight - Dock.Height + rect.Y - 8, anchorBottom: true);
        Children.Add(Dock);

        Children.Add(_overlay);
        _overlay.Width = ScreenWidth;
        _overlay.Height = ScreenHeight;
        _overlay.IsHitTestVisible = true;

        // Auto-hide: same rules and state machine as the real shell, fed with the fake desktop's state.
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, (_, _) => UpdateAutoHide());
        AttachedToVisualTree += (_, _) => _poll.Start();
        DetachedFromVisualTree += (_, _) => _poll.Stop();
        AddHandler(PointerMovedEvent, (_, e) => _pointer = e.GetPosition(this), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_popup != null && !_popup.Bounds.Contains(e.GetPosition(_overlay)) && e.Source is Visual v && !IsInside(v, MenuBar)) ClosePopup();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private static bool IsInside(Visual v, Visual container)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent()) if (x == container) return true;
        return false;
    }

    private void AddWindow(FakeWindow w, double x, double y)
    {
        SetLeft(w, x);
        SetTop(w, y);
        w.Lights.Clicked += b =>
        {
            switch (b)
            {
                case WindowButton.Close: w.IsVisible = false; break;
                case WindowButton.Minimize: _ = AnimateMinimize(w); break;
                case WindowButton.Zoom: SetMaximized(w, !w.Maximized); break;
            }
        };
        w.PointerPressed += (_, _) => Activate(w);
        Windows.Add(w);
        Children.Add(w);
    }

    private void Activate(FakeWindow w)
    {
        foreach (var other in Windows) other.Active = other == w;
        Children.Remove(w);
        Children.Insert(1 + Windows.Count(x => x != w && Children.Contains(x)), w);
        MenuBar.AppName = w.Content == FakeContent.Notes ? "Notes" : "Browser";
        MenuBar.InvalidateVisual();
    }

    public FakeWindow? ActiveWindow => Windows.FirstOrDefault(w => w.Active && w.IsVisible);

    private (double X, double Y, double W, double H)? _restoreBounds;

    public void SetMaximized(FakeWindow w, bool maximized)
    {
        if (maximized)
        {
            _restoreBounds = (GetLeft(w), GetTop(w), w.Width, w.Height);
            SetLeft(w, 0);
            SetTop(w, MenuBarView.BarHeight);
            w.Width = ScreenWidth;
            w.Height = ScreenHeight - MenuBarView.BarHeight;
        }
        else if (_restoreBounds is { } r)
        {
            SetLeft(w, r.X); SetTop(w, r.Y); w.Width = r.W; w.Height = r.H;
        }
        w.Maximized = maximized;
        w.InvalidateVisual();
        UpdateAutoHide();
    }

    private void UpdateAutoHide()
    {
        var w = ActiveWindow;
        ForegroundInfo? fg = w == null ? null : new ForegroundInfo(new Rect(GetLeft(w), GetTop(w), w.Width, w.Height), w.Maximized, false, true);
        Platform.SimulatedForeground = fg;
        var shelf = Dock.ShelfRect.Translate(new Vector(0, ScreenHeight - Dock.Height));
        bool wantsHide = VisibilityRules.DockWantsHide(_settings.Dock, fg, new Rect(shelf.X, shelf.Y - Dock.Hidden * 80, shelf.Width, shelf.Height + 80));
        _dockReveal.Update(DateTime.UtcNow, wantsHide, _pointer.Y >= ScreenHeight - 2, Dock.IsEngaged);
        double target = _dockReveal.Visible ? 0 : 1;
        if (Math.Abs(Dock.HiddenTarget - target) > 0.001)
        {
            Dock.HiddenTarget = target;
            Dock.Items = Dock.Items; // kicks the animation loop
        }
    }

    // ---- popovers ------------------------------------------------------------------------------

    private void ShowPopup(Control popup, double x, double y, bool anchorBottom = false)
    {
        ClosePopup();
        _popup = popup;
        popup.Measure(Size.Infinity);
        double h = double.IsNaN(popup.Height) ? popup.DesiredSize.Height : popup.Height;
        double w = double.IsNaN(popup.Width) ? popup.DesiredSize.Width : popup.Width;
        SetLeft(popup, w >= ScreenWidth - 12 ? 0 : Math.Clamp(x, 6, ScreenWidth - w - 6));
        SetTop(popup, anchorBottom ? y - h : y);
        _overlay.Children.Add(popup);
        if (popup.Focusable) popup.Focus();
    }

    public void ClosePopup()
    {
        if (_popup != null) _overlay.Children.Remove(_popup);
        _popup = null;
        MenuBar.OpenIndex = -1;
        MenuBar.OpenStatus = null;
        MenuBar.InvalidateVisual();
    }

    public MenuPanel OpenMenu(int index)
    {
        var panel = new MenuPanel(MenuCatalog.For(index, MenuBar.AppName, Platform, this));
        panel.Dismissed += ClosePopup;
        var rect = MenuBar.TitleRect(index);
        ShowPopup(panel, rect.X, MenuBarView.BarHeight + 3);
        MenuBar.OpenIndex = index;
        MenuBar.InvalidateVisual();
        return panel;
    }

    public ControlCenterView OpenControlCenter()
    {
        var cc = new ControlCenterView(Platform);
        cc.Dismissed += ClosePopup;
        ShowPopup(cc, ScreenWidth - ControlCenterView.PanelWidth - 10, MenuBarView.BarHeight + 4);
        MenuBar.OpenStatus = StatusItem.ControlCenter;
        MenuBar.InvalidateVisual();
        return cc;
    }

    public SpotlightView OpenSpotlightView(string query = "")
    {
        var s = new SpotlightView(Platform) { Query = query };
        s.Dismissed += ClosePopup;
        ShowPopup(s, (ScreenWidth - SpotlightView.PanelWidth) / 2, ScreenHeight * 0.2);
        s.FocusInput();
        return s;
    }

    public LaunchpadView ShowLaunchpad()
    {
        var lp = new LaunchpadView(Platform) { Width = ScreenWidth, Height = ScreenHeight };
        lp.Dismissed += ClosePopup;
        ShowPopup(lp, 0, 0);
        return lp;
    }

    // ---- minimize ------------------------------------------------------------------------------

    public RenderTargetBitmap Snapshot(Control c)
    {
        var size = new PixelSize((int)c.Bounds.Width, (int)c.Bounds.Height);
        var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
        rtb.Render(c);
        return rtb;
    }

    /// <summary>Adds a frozen genie frame to the scene (screenshots).</summary>
    public GenieView ShowGenieFrame(FakeWindow w, AppEntry target, double progress)
    {
        var snap = Snapshot(w);
        var to = Dock.IconRect(target) ?? new Rect(ScreenWidth / 2, ScreenHeight - 40, 50, 50);
        var genie = new GenieView
        {
            Image = snap, ImageSize = new Size(snap.PixelSize.Width, snap.PixelSize.Height),
            From = new Rect(GetLeft(w), GetTop(w), w.Width, w.Height),
            To = to.Translate(new Vector(0, ScreenHeight - Dock.Height)),
            Progress = progress, Width = ScreenWidth, Height = ScreenHeight, IsHitTestVisible = false,
        };
        w.IsVisible = false;
        _overlay.Children.Add(genie);
        return genie;
    }

    private async Task AnimateMinimize(FakeWindow w)
    {
        var target = Platform.DockApps.FirstOrDefault(a => a.Id == (w.Content == FakeContent.Notes ? "notes" : "browser")) ?? Platform.DockApps[0];
        var genie = ShowGenieFrame(w, target, 0);
        var start = DateTime.UtcNow;
        const double duration = 0.5;
        while (true)
        {
            double t = (DateTime.UtcNow - start).TotalSeconds / duration;
            genie.Progress = Math.Min(1, t);
            genie.InvalidateVisual();
            if (t >= 1) break;
            await Task.Delay(16);
        }
        _overlay.Children.Remove(genie);
    }

    // ---- IShellCommands ------------------------------------------------------------------------

    public void MinimizeFront() { if (ActiveWindow is { } w) _ = AnimateMinimize(w); }
    public void ZoomFront() { if (ActiveWindow is { } w) SetMaximized(w, !w.Maximized); }
    public void ToggleLaunchpad() { if (_popup is LaunchpadView) ClosePopup(); else ShowLaunchpad(); }
    public void OpenSpotlight() => OpenSpotlightView();
    public void OpenSettingsFile() { }
    public void QuitOrchard() => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
