using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orchard.Model;
using Orchard.Rendering;
using Orchard.Services;

namespace Orchard.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform : IShellPlatform
{
    private readonly OrchardSettings _settings;
    private readonly List<AppEntry> _dock = [];
    private readonly Dictionary<nint, string> _windowApp = [];
    private readonly List<nint> _hooks = [];
    private readonly Native.WinEventProc _winEvent; // kept alive: the hook holds only a native pointer to it
    private readonly DispatcherTimer _rescan;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly Lazy<IReadOnlyList<LaunchItem>> _installed;

    /// <summary>URI-launched apps run as a differently named process; this maps one to the other for running dots.</summary>
    private static readonly Dictionary<string, string> UriProcess = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ms-settings:"] = "SystemSettings.exe",
        ["calculator:"] = "CalculatorApp.exe",
        ["ms-photos:"] = "Photos.exe",
        ["ms-windows-store:"] = "WinStore.App.exe",
        ["outlookmail:"] = "olk.exe",
        ["outlookcal:"] = "olk.exe",
        ["ms-clock:"] = "Time.exe",
    };

    public WindowsPlatform(OrchardSettings settings)
    {
        _settings = settings;
        _installed = new(ScanStartMenu);
        _winEvent = OnWinEvent;
        _rescan = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => { _rescan!.Stop(); Rescan(); });

        foreach (var p in settings.Pinned) _dock.Add(FromPinned(p));
        _dock.Insert(Math.Min(1, _dock.Count), new AppEntry { Id = "orchard:launchpad", Name = "Launchpad", Icon = new VectorIcon("launchpad"), Kind = AppKind.Launchpad, IsPinned = true });
        _dock.Add(new AppEntry { Id = "orchard:trash", Name = "Recycle Bin", Icon = new VectorIcon("trash"), Kind = AppKind.Trash, IsPinned = true, Target = "shell:RecycleBinFolder" });

        Hook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND);
        Hook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND);
        Hook(Native.EVENT_OBJECT_CREATE, Native.EVENT_OBJECT_HIDE);
        Hook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_NAMECHANGE);
        Hook(Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED);
        Rescan();
        UpdateForeground();
    }

    public bool IsPreview => false;
    public IReadOnlyList<AppEntry> DockApps => _dock;
    public event Action? DockAppsChanged;
    public event Action? ForegroundChanged;
    public event Action? ForegroundMoved;
    public WindowInfo? Foreground { get; private set; }
    public AppEntry? ForegroundApp { get; private set; }

    public PixelRect PrimaryMonitor => ToPixelRect(Native.PrimaryMonitor().rcMonitor);

    public PixelPoint Pointer => Native.GetCursorPos(out var p) ? new PixelPoint(p.X, p.Y) : default;

    // ---- window tracking -----------------------------------------------------------------------

    private void Hook(uint min, uint max)
    {
        nint h = Native.SetWinEventHook(min, max, 0, _winEvent, 0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        if (h != 0) _hooks.Add(h);
    }

    private void OnWinEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF || hwnd == 0) return;
        switch (ev)
        {
            case Native.EVENT_SYSTEM_FOREGROUND:
                UpdateForeground();
                break;
            case Native.EVENT_OBJECT_LOCATIONCHANGE:
                if (hwnd == Foreground?.Handle) { UpdateForeground(raiseChanged: false); ForegroundMoved?.Invoke(); }
                break;
            case Native.EVENT_OBJECT_NAMECHANGE:
                if (hwnd == Foreground?.Handle) UpdateForeground();
                break;
            case Native.EVENT_SYSTEM_MINIMIZESTART:
            case Native.EVENT_SYSTEM_MINIMIZEEND:
                if (hwnd == Foreground?.Handle) { UpdateForeground(raiseChanged: false); ForegroundMoved?.Invoke(); }
                break;
            default:
                // Create/destroy/show/hide/cloak arrive in bursts; coalesce them into one rescan.
                if (Native.GetAncestor(hwnd, Native.GA_ROOT) == hwnd) { _rescan.Stop(); _rescan.Start(); }
                break;
        }
    }

    /// <summary>The same test the Alt+Tab switcher uses to decide what counts as an app window.</summary>
    private bool IsAppWindow(nint hwnd)
    {
        if (!Native.IsWindowVisible(hwnd) || Native.IsCloaked(hwnd)) return false;
        long ex = Native.ExStyle(hwnd);
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
        if (Native.GetWindow(hwnd, Native.GW_OWNER) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return false;
        if (Native.GetWindowTextLengthW(hwnd) == 0) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid) return false;
        var cls = Native.GetClass(hwnd);
        return cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow");
    }

    /// <summary>Identifies the app behind a window by its executable; UWP windows are unwrapped from ApplicationFrameHost.</summary>
    private static string? AppPathFor(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        var path = Native.ProcessPath(pid);
        if (path != null && path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            string? inner = null;
            Native.EnumChildWindows(hwnd, (child, _) =>
            {
                Native.GetWindowThreadProcessId(child, out uint cpid);
                if (cpid != pid) { inner = Native.ProcessPath(cpid); return false; }
                return true;
            }, 0);
            path = inner ?? path;
        }
        return path;
    }

    private void Rescan()
    {
        var found = new List<nint>();
        Native.EnumWindows((h, _) => { if (IsAppWindow(h)) found.Add(h); return true; }, 0);

        foreach (var app in _dock) app.Windows.Clear();
        _windowApp.Clear();

        foreach (var hwnd in found)
        {
            var path = AppPathFor(hwnd);
            if (path == null) continue;
            var app = _dock.FirstOrDefault(a => Matches(a, path));
            if (app == null)
            {
                app = new AppEntry
                {
                    Id = path.ToLowerInvariant(),
                    Name = FriendlyName(path, hwnd),
                    Icon = IconFor(path),
                    Target = path,
                };
                _dock.Insert(_dock.Count - 1, app); // before the Recycle Bin
            }
            app.Windows.Add(hwnd);
            _windowApp[hwnd] = app.Id;
        }

        // Running-only apps leave the dock when their last window closes.
        _dock.RemoveAll(a => !a.IsPinned && !a.IsRunning && a.Kind == AppKind.Normal);
        DockAppsChanged?.Invoke();
    }

    private static bool Matches(AppEntry app, string processPath)
    {
        if (app.Kind != AppKind.Normal || app.Target == null) return false;
        if (string.Equals(app.Id, processPath, StringComparison.OrdinalIgnoreCase)) return true;
        if (UriProcess.TryGetValue(app.Target, out var exe))
            return processPath.EndsWith("\\" + exe, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private void UpdateForeground(bool raiseChanged = true)
    {
        nint hwnd = Native.GetForegroundWindow();
        // Clicking the dock or menu bar must not make the shell think the app lost focus.
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid) return;

        if (hwnd == 0 || !IsAppWindow(hwnd))
        {
            bool changed = Foreground != null;
            Foreground = null;
            ForegroundApp = null;
            if (changed && raiseChanged) ForegroundChanged?.Invoke();
            return;
        }

        var bounds = ToPixelRect(Native.VisibleBounds(hwnd));
        _windowApp.TryGetValue(hwnd, out var appId);
        if (appId == null) { Rescan(); _windowApp.TryGetValue(hwnd, out appId); }
        var previous = Foreground?.Handle;
        Foreground = new WindowInfo(hwnd, Native.GetText(hwnd), bounds, Native.IsZoomed(hwnd), Native.IsIconic(hwnd), appId ?? "");
        ForegroundApp = _dock.FirstOrDefault(a => a.Id == appId);
        if (raiseChanged || previous != hwnd) ForegroundChanged?.Invoke();
    }

    public ForegroundInfo? ForegroundState(PixelRect monitor)
    {
        if (Foreground is not { } fg || fg.IsMinimized) return null;
        var mon = Native.MonitorFor(fg.Handle);
        var monRect = ToRect(mon.rcMonitor);
        var b = new Rect(fg.Bounds.X, fg.Bounds.Y, fg.Bounds.Width, fg.Bounds.Height);

        // Only windows on the monitor being asked about can hide that monitor's bars.
        if (!monRect.Intersects(new Rect(monitor.X, monitor.Y, monitor.Width, monitor.Height))) return null;

        bool fullscreen = VisibilityRules.CoversMonitor(b, monRect) && (Native.Style(fg.Handle) & Native.WS_CAPTION) != Native.WS_CAPTION;
        if (Native.SHQueryUserNotificationState(out int quns) == 0 &&
            quns is Native.QUNS_BUSY or Native.QUNS_RUNNING_D3D_FULL_SCREEN or Native.QUNS_PRESENTATION_MODE)
            fullscreen = true;
        return new ForegroundInfo(b, fg.IsMaximized, fullscreen, true);
    }

    // ---- apps ----------------------------------------------------------------------------------

    private AppEntry FromPinned(PinnedApp p)
    {
        var target = Environment.ExpandEnvironmentVariables(p.Target);
        var resolved = ResolveExecutable(target);
        IconArt icon = p.Icon != null && VectorIcon.Kinds.Contains(p.Icon)
            ? new VectorIcon(p.Icon)
            : resolved != null ? IconFor(resolved) : new VectorIcon("app");
        return new AppEntry
        {
            Id = (resolved ?? target).ToLowerInvariant(),
            Name = p.Name,
            Icon = icon,
            Target = resolved ?? target,
            Arguments = p.Arguments,
            IsPinned = true,
        };
    }

    private static string? ResolveExecutable(string target)
    {
        if (target.Contains(':') && !Path.IsPathRooted(target)) return null; // URI such as ms-settings:
        if (Path.IsPathRooted(target)) return File.Exists(target) ? target : null;
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.SystemDirectory,
                                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps") })
        {
            var candidate = Path.Combine(dir, target);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string FriendlyName(string path, nint hwnd)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription) && info.FileDescription.Length < 40) return info.FileDescription.Trim();
            if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName.Trim();
        }
        catch (FileNotFoundException) { }
        var title = Native.GetText(hwnd);
        return title.Length is > 0 and < 32 ? title : Path.GetFileNameWithoutExtension(path);
    }

    private static IconArt IconFor(string path) =>
        IconExtractor.ForPath(path) is { } bmp ? new BitmapIcon(bmp) : new VectorIcon("app");

    public void Activate(AppEntry app)
    {
        switch (app.Kind)
        {
            case AppKind.Trash:
                Run(SystemAction.OpenTrash);
                return;
            case AppKind.Launchpad:
                return; // handled by the shell, which owns the Launchpad window
        }

        var windows = app.Windows.Where(Native.IsWindow).ToList();
        if (windows.Count == 0)
        {
            if (app.Target != null) Launch(app.Target, app.Arguments);
            return;
        }
        // Restore minimized windows, then raise them all with the most recently used on top (macOS app activation).
        foreach (var w in windows.AsEnumerable().Reverse())
        {
            if (Native.IsIconic(w)) Native.ShowWindowAsync(w, Native.SW_RESTORE);
            Focus(w);
        }
    }

    /// <summary>
    /// Windows only lets the process that received the last input steal focus. A dock click qualifies, but
    /// a synthetic Alt tap is the documented-in-practice fallback when it doesn't.
    /// </summary>
    private static void Focus(nint hwnd)
    {
        if (Native.SetForegroundWindow(hwnd)) return;
        Native.keybd_event((byte)Native.VK_MENU, 0, 0, 0);
        Native.keybd_event((byte)Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, 0);
        Native.SetForegroundWindow(hwnd);
    }

    public void Launch(string target, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target, arguments ?? "") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Write($"launch failed for {target}: {e.Message}");
        }
    }

    public void Quit(AppEntry app)
    {
        foreach (var w in app.Windows) Native.PostMessageW(w, Native.WM_CLOSE, 0, 0);
    }

    public void SetPinned(AppEntry app, bool pinned)
    {
        app.IsPinned = pinned;
        _settings.Pinned.RemoveAll(p => string.Equals(Environment.ExpandEnvironmentVariables(p.Target), app.Target, StringComparison.OrdinalIgnoreCase));
        if (pinned && app.Target != null) _settings.Pinned.Add(new PinnedApp(app.Name, app.Target, (app.Icon as VectorIcon)?.Kind, app.Arguments));
        SettingsStore.Save(_settings);
        if (!pinned && !app.IsRunning) _dock.Remove(app);
        DockAppsChanged?.Invoke();
    }

    // ---- windows -------------------------------------------------------------------------------

    public void CloseWindow(nint hwnd) => Native.PostMessageW(hwnd, Native.WM_SYSCOMMAND, Native.SC_CLOSE, 0);
    public void MinimizeWindow(nint hwnd) => Native.ShowWindowAsync(hwnd, Native.SW_MINIMIZE);

    public void RestoreWindow(nint hwnd)
    {
        Native.ShowWindowAsync(hwnd, Native.SW_RESTORE);
        Focus(hwnd);
    }

    public void ToggleZoom(nint hwnd) =>
        Native.PostMessageW(hwnd, Native.WM_SYSCOMMAND, Native.IsZoomed(hwnd) ? Native.SC_RESTORE : Native.SC_MAXIMIZE, 0);

    public Bitmap? CaptureWindow(nint hwnd) => WindowCapture.Capture(hwnd);

    // ---- input / system ------------------------------------------------------------------------

    public void SendShortcut(Shortcut s)
    {
        if (Foreground is { } fg) Focus(fg.Handle);
        var keys = new List<uint>();
        if (s.Ctrl) keys.Add(Native.VK_CONTROL);
        if (s.Shift) keys.Add(Native.VK_SHIFT);
        if (s.Alt) keys.Add(Native.VK_MENU);
        keys.Add(s.VirtualKey);
        var inputs = keys.Select(k => Key(k, false)).Concat(keys.AsEnumerable().Reverse().Select(k => Key(k, true))).ToArray();
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());

        static Native.INPUT Key(uint vk, bool up) => new()
        {
            type = Native.INPUT_KEYBOARD,
            ki = new Native.KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? Native.KEYEVENTF_KEYUP : 0 },
        };
    }

    public void Run(SystemAction action)
    {
        switch (action)
        {
            case SystemAction.About: Launch("ms-settings:about"); break;
            case SystemAction.SystemSettings: Launch("ms-settings:"); break;
            case SystemAction.Store: Launch("ms-windows-store:"); break;
            case SystemAction.ForceQuit: Launch("taskmgr.exe"); break;
            case SystemAction.Sleep: Native.SetSuspendState(false, false, false); break;
            case SystemAction.Restart: Launch("shutdown.exe", "/r /t 0"); break;
            case SystemAction.ShutDown: Launch("shutdown.exe", "/s /t 0"); break;
            case SystemAction.LockScreen: Native.LockWorkStation(); break;
            case SystemAction.LogOut: Native.ExitWindowsEx(0 /* EWX_LOGOFF */, 0); break;
            case SystemAction.OpenTrash: Launch("explorer.exe", "shell:RecycleBinFolder"); break;
            case SystemAction.EmptyTrash: Native.SHEmptyRecycleBinW(0, null, 0); break;
        }
    }

    public double? Volume
    {
        get => AudioVolume.Get();
        set { if (value is { } v) AudioVolume.Set(v); }
    }

    private double? _brightness = BrightnessControl.Read();

    public double? Brightness
    {
        get => _brightness;
        set
        {
            if (value is not { } v || _brightness == null) return;
            _brightness = v;
            BrightnessControl.Write(v);
        }
    }

    public bool DarkMode
    {
        get => ThemeSwitch.IsDark();
        set => ThemeSwitch.SetDark(value);
    }

    public BatteryInfo? Battery
    {
        get
        {
            if (!Native.GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryLifePercent > 100) return null; // 128 = no battery
            return new BatteryInfo(s.BatteryLifePercent, s.ACLineStatus == 1);
        }
    }

    // ---- Spotlight / Launchpad index -----------------------------------------------------------

    public IReadOnlyList<LaunchItem> InstalledApps => _installed.Value;

    private static IReadOnlyList<LaunchItem> ScanStartMenu()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        var items = new Dictionary<string, LaunchItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".url", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("readme", StringComparison.OrdinalIgnoreCase)) continue;
                items.TryAdd(name, new LaunchItem(name, file));
            }
        }
        // Store apps don't have shortcut files; expose the common built-ins by URI.
        foreach (var (name, uri) in new[] { ("Settings", "ms-settings:"), ("Calculator", "calculator:"), ("Microsoft Store", "ms-windows-store:"),
                                           ("Photos", "ms-photos:"), ("Clock", "ms-clock:"), ("Camera", "microsoft.windows.camera:") })
            items.TryAdd(name, new LaunchItem(name, uri));
        return items.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IconArt IconFor(LaunchItem item)
    {
        if (item.Icon != null) return item.Icon;
        if (item.Target.EndsWith(":", StringComparison.Ordinal))
        {
            var kind = item.Target switch
            {
                "ms-settings:" => "settings", "calculator:" => "calculator", "ms-windows-store:" => "store", "ms-photos:" => "photos",
                _ => "app",
            };
            return item.Icon = new VectorIcon(kind);
        }
        return item.Icon = IconFor(item.Target);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static PixelRect ToPixelRect(Native.RECT r) => new(r.Left, r.Top, Math.Max(0, r.Width), Math.Max(0, r.Height));
    private static Rect ToRect(Native.RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    public void Dispose()
    {
        foreach (var h in _hooks) Native.UnhookWinEvent(h);
        _hooks.Clear();
        _rescan.Stop();
    }
}
