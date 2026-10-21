using Orchard.Controls;
using Orchard.Model;
using Orchard.Platform;

namespace Orchard.Shell;

/// <summary>Shell-level commands the menus can trigger (implemented by the real shell and by preview).</summary>
public interface IShellCommands
{
    void MinimizeFront();
    void ZoomFront();
    void ToggleLaunchpad();
    void OpenSpotlight();
    void OpenSettingsFile();
    void QuitOrchard();
}

/// <summary>
/// The menus behind each menu bar title. App menus forward standard shortcuts to whatever app is in front,
/// which works for nearly every Windows program without needing per-app integration.
/// </summary>
public static class MenuCatalog
{
    public static IReadOnlyList<string> Titles => ["File", "Edit", "View", "Window", "Help"];

    public static IReadOnlyList<MenuItem> For(int index, string appName, IShellPlatform p, IShellCommands shell)
    {
        MenuItem Key(string title, Shortcut s) => new(title, s.Label, () => p.SendShortcut(s));
        var sep = MenuItem.Separator;

        return index switch
        {
            0 =>
            [
                new("About This PC", null, () => p.Run(SystemAction.About)),
                sep,
                new("System Settings…", null, () => p.Run(SystemAction.SystemSettings)),
                new("App Store…", null, () => p.Run(SystemAction.Store)),
                sep,
                new("Orchard Settings…", null, shell.OpenSettingsFile),
                sep,
                new("Force Quit…", "Ctrl+Shift+Esc", () => p.Run(SystemAction.ForceQuit)),
                sep,
                new("Sleep", null, () => p.Run(SystemAction.Sleep)),
                new("Restart…", null, () => p.Run(SystemAction.Restart)),
                new("Shut Down…", null, () => p.Run(SystemAction.ShutDown)),
                sep,
                new("Lock Screen", "Win+L", () => p.Run(SystemAction.LockScreen)),
                new("Log Out", null, () => p.Run(SystemAction.LogOut)),
                sep,
                new("Quit Orchard", null, shell.QuitOrchard),
            ],
            1 =>
            [
                new($"About {appName}", null, () => p.SendShortcut(Shortcut.Key(0x70, "F1"))),
                sep,
                new("Settings…", "Ctrl+,", () => p.SendShortcut(Shortcut.WithCtrl(','))),
                sep,
                new($"Hide {appName}", null, shell.MinimizeFront),
                sep,
                new($"Quit {appName}", "Alt+F4", () => p.SendShortcut(Shortcut.Key(0x73, "Alt+F4", alt: true))),
            ],
            2 =>
            [
                Key("New Window", Shortcut.WithCtrl('N')),
                Key("New Tab", Shortcut.WithCtrl('T')),
                Key("Open…", Shortcut.WithCtrl('O')),
                sep,
                Key("Close Window", Shortcut.WithCtrl('W')),
                Key("Save", Shortcut.WithCtrl('S')),
                Key("Save As…", Shortcut.WithCtrl('S', shift: true)),
                sep,
                Key("Print…", Shortcut.WithCtrl('P')),
            ],
            3 =>
            [
                Key("Undo", Shortcut.WithCtrl('Z')),
                Key("Redo", Shortcut.WithCtrl('Y')),
                sep,
                Key("Cut", Shortcut.WithCtrl('X')),
                Key("Copy", Shortcut.WithCtrl('C')),
                Key("Paste", Shortcut.WithCtrl('V')),
                Key("Select All", Shortcut.WithCtrl('A')),
                sep,
                Key("Find…", Shortcut.WithCtrl('F')),
            ],
            4 =>
            [
                Key("Zoom In", new Shortcut(true, false, false, 0xBB, "Ctrl+=")),
                Key("Zoom Out", new Shortcut(true, false, false, 0xBD, "Ctrl+-")),
                Key("Actual Size", Shortcut.WithCtrl('0')),
                sep,
                Key("Reload", Shortcut.Key(0x74, "F5")),
                Key("Enter Full Screen", Shortcut.Key(0x7A, "F11")),
            ],
            5 =>
            [
                new("Minimize", null, shell.MinimizeFront),
                new("Zoom", null, shell.ZoomFront),
                sep,
                new("Launchpad", null, shell.ToggleLaunchpad),
                new("Spotlight", "Alt+Space", shell.OpenSpotlight),
            ],
            _ =>
            [
                new($"{appName} Help", "F1", () => p.SendShortcut(Shortcut.Key(0x70, "F1"))),
                new("Get Help with Windows", null, () => p.Launch("ms-contact-support:")),
            ],
        };
    }

    /// <summary>Right-click menu for a dock icon.</summary>
    public static IReadOnlyList<MenuItem> ForDockItem(AppEntry app, IShellPlatform p, Action<nint> focusWindow)
    {
        var items = new List<MenuItem>();
        if (app.Kind == AppKind.Trash)
        {
            items.Add(new("Open", null, () => p.Run(SystemAction.OpenTrash)));
            items.Add(MenuItem.Separator);
            items.Add(new("Empty Recycle Bin", null, () => p.Run(SystemAction.EmptyTrash)));
            return items;
        }
        if (app.Kind == AppKind.Launchpad)
        {
            items.Add(new("Open", null, () => p.Activate(app)));
            return items;
        }

        foreach (var w in app.Windows.Take(8))
        {
            var title = p.Foreground?.Handle == w ? p.Foreground.Title : WindowTitle(w);
            items.Add(new(Truncate(title), null, () => focusWindow(w)));
        }
        if (items.Count > 0) items.Add(MenuItem.Separator);

        items.Add(new(app.IsPinned ? "Remove from Dock" : "Keep in Dock", null, () => p.SetPinned(app, !app.IsPinned)));
        if (app.Target != null && Path.IsPathRooted(app.Target))
            items.Add(new("Show in File Explorer", null, () => p.Launch("explorer.exe", $"/select,\"{app.Target}\"")));
        if (!app.IsRunning)
            items.Add(new("Open", null, () => p.Activate(app)));
        else
        {
            items.Add(MenuItem.Separator);
            items.Add(new("Quit", null, () => p.Quit(app)));
        }
        return items;

        static string Truncate(string s) => s.Length > 48 ? s[..47] + "…" : s;
    }

    /// <summary>Right-click menu for a desktop widget.</summary>
    public static IReadOnlyList<MenuItem> ForWidget(Action remove, Action edit) =>
    [
        new("Remove Widget", null, remove),
        MenuItem.Separator,
        new("Edit Widgets…", null, edit),
    ];

    /// <summary>Overridden by the Windows shell to read real window titles.</summary>
    public static Func<nint, string> WindowTitle { get; set; } = _ => "Window";
}
