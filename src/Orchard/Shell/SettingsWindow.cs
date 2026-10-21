using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Orchard.Model;
using Orchard.Platform.Windows;
using Orchard.Theme;

namespace Orchard.Shell;

/// <summary>Orchard Settings: every option in settings.json, as a normal app window.</summary>
public sealed class SettingsWindow : Window
{
    private readonly OrchardSettings _s;

    public SettingsWindow(OrchardSettings settings, bool canRestart)
    {
        _s = settings;
        Title = "Orchard Settings";
        Width = 560;
        Height = 720;
        MinWidth = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Palette.Font;
        RequestedThemeVariant = Palette.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Background = Palette.Brush(Palette.Dark ? Color.FromRgb(0x1E, 0x1E, 0x20) : Color.FromRgb(0xF2, 0xF2, 0xF5));
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Orchard/Assets/orchard.ico"))); } catch (Exception) { /* icon is cosmetic */ }

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "Orchard Settings", FontSize = 26, FontWeight = FontWeight.Bold });

        body.Children.Add(Section("Appearance",
            Row("Theme", Combo(_s.Appearance, v => _s.Appearance = v, ("Match Windows", Appearance.Auto), ("Light", Appearance.Light), ("Dark", Appearance.Dark))),
            Row("Dock & widget glass", Combo(_s.Glass, v => _s.Glass = v, ("Clear (see-through)", GlassStyle.Clear), ("Frosted", GlassStyle.Frosted)))));

        body.Children.Add(Section("Dock",
            Row("Show the dock", Combo(_s.Dock, v => _s.Dock = v,
                ("Always", DockVisibility.AlwaysShow), ("Hide when a window covers it", DockVisibility.HideWhenOverlapped), ("Auto-hide", DockVisibility.AlwaysAutoHide))),
            Row("Icon size", SliderFor(_s.IconSize, 36, 80, v => _s.IconSize = v)),
            Row("Magnification", Toggle(_s.Magnification, v => _s.Magnification = v)),
            Row("Magnified size", SliderFor(_s.MagnifiedScale, 1.2, 2.2, v => _s.MagnifiedScale = v)),
            Row("Minimize effect", Combo(_s.MinimizeEffect, v => _s.MinimizeEffect = v,
                ("Genie", MinimizeEffect.Genie), ("Scale", MinimizeEffect.Scale), ("Windows default", MinimizeEffect.System)))));

        body.Children.Add(Section("Menu bar & windows",
            Row("Show the menu bar", Combo(_s.MenuBar, v => _s.MenuBar = v,
                ("Always", MenuBarVisibility.AlwaysShow), ("Hide when an app is maximized", MenuBarVisibility.HideWhenMaximized),
                ("Hide in fullscreen", MenuBarVisibility.HideInFullscreen), ("Auto-hide", MenuBarVisibility.AlwaysAutoHide))),
            Row("Red / yellow / green window buttons", Toggle(_s.TrafficLights, v => _s.TrafficLights = v)),
            Row("Hide the Windows taskbar", Toggle(_s.HideWindowsTaskbar, v => _s.HideWindowsTaskbar = v))));

        var widgetToggles = new WrapPanel();
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var box = new CheckBox { Content = kind.ToString(), IsChecked = _s.Widgets.Contains(kind), Margin = new Thickness(0, 0, 16, 4) };
            box.IsCheckedChanged += (_, _) =>
            {
                _s.Widgets.Remove(kind);
                if (box.IsChecked == true) _s.Widgets.Add(kind);
                _s.Widgets.Sort();
            };
            widgetToggles.Children.Add(box);
        }
        var city = new TextBox { Text = _s.WeatherCity, Watermark = "e.g. London", Width = 200 };
        city.TextChanged += (_, _) => _s.WeatherCity = city.Text ?? "";
        body.Children.Add(Section("Widgets",
            Row("Show widgets on the desktop", Toggle(_s.ShowWidgets, v => _s.ShowWidgets = v)),
            widgetToggles,
            Row("Weather city", city),
            Row("Use °F", Toggle(_s.Fahrenheit, v => _s.Fahrenheit = v))));

        if (OperatingSystem.IsWindows())
            body.Children.Add(Section("Startup",
                Row("Open Orchard when I sign in", Toggle(StartupRegistration.IsEnabled(), StartupRegistration.Set))));

        var save = new Button { Content = canRestart ? "Save & Restart Orchard" : "Save", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            SettingsStore.Save(_s);
            if (canRestart) Restart();
            Close();
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        var note = new TextBlock
        {
            Text = "Changes apply when Orchard restarts.", Opacity = 0.6, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
        };
        var buttons = new DockPanel { Margin = new Thickness(24, 12) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, save } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(note);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
    }

    /// <summary>Starts a fresh Orchard that waits for this one to exit, then quits this one.</summary>
    private static void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        Process.Start(new ProcessStartInfo(exe, $"--after {Environment.ProcessId}") { UseShellExecute = false });
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private Control Section(string title, params Control[] rows)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Palette.Brush(Palette.Dark ? Color.FromRgb(0x2A, 0x2A, 0x2D) : Colors.White),
            Padding = new Thickness(16, 10),
            Child = new StackPanel { Spacing = 6, Children = { } },
        };
        var stack = (StackPanel)card.Child;
        foreach (var r in rows) stack.Children.Add(r);
        return new StackPanel
        {
            Spacing = 6,
            Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Opacity = 0.7, Margin = new Thickness(4, 0) }, card },
        };
    }

    private static Control Row(string label, Control editor)
    {
        var row = new DockPanel { MinHeight = 36 };
        editor.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(editor, Dock.Right);
        row.Children.Add(editor);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
        return row;
    }

    private static ComboBox Combo<T>(T current, Action<T> set, params (string Label, T Value)[] options)
    {
        var combo = new ComboBox { MinWidth = 220, ItemsSource = options.Select(o => o.Label).ToList() };
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(options, o => EqualityComparer<T>.Default.Equals(o.Value, current)));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) set(options[combo.SelectedIndex].Value); };
        return combo;
    }

    private static ToggleSwitch Toggle(bool current, Action<bool> set)
    {
        var t = new ToggleSwitch { IsChecked = current, OnContent = "", OffContent = "" };
        t.IsCheckedChanged += (_, _) => set(t.IsChecked == true);
        return t;
    }

    private static Control SliderFor(double current, double min, double max, Action<double> set)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = current, Width = 220 };
        slider.ValueChanged += (_, e) => set(Math.Round(e.NewValue, 2));
        return slider;
    }
}
