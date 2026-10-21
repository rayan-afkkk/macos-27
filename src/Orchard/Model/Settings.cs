using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchard.Model;

public enum DockVisibility
{
    /// <summary>The dock is always on screen.</summary>
    AlwaysShow,
    /// <summary>The dock slides away while a maximized or fullscreen window covers it (default).</summary>
    HideWhenOverlapped,
    /// <summary>The dock only appears when the pointer touches the bottom edge.</summary>
    AlwaysAutoHide,
}

public enum MenuBarVisibility
{
    /// <summary>Always visible, and reserves its strip so maximized windows stop below it.</summary>
    AlwaysShow,
    /// <summary>Stays put for maximized windows, slides away for true fullscreen (games, video, F11).</summary>
    HideInFullscreen,
    /// <summary>Slides away while the app in front is maximized or fullscreen; push the pointer to the top edge to bring it back (default).</summary>
    HideWhenMaximized,
    AlwaysAutoHide,
}

public enum MinimizeEffect { Genie, Scale, System }

public enum Appearance
{
    /// <summary>Follow the Windows light/dark setting, live.</summary>
    Auto,
    Light,
    Dark,
}

public enum WidgetKind { Clock, Calendar, Weather, Battery, System }

public sealed record PinnedApp(string Name, string Target, string? Icon = null, string? Arguments = null);

public sealed class OrchardSettings
{
    public const int CurrentSchema = 3;

    /// <summary>Lets <see cref="SettingsStore.Load"/> move older settings files onto new defaults once.</summary>
    public int? SchemaVersion { get; set; }

    public DockVisibility Dock { get; set; } = DockVisibility.HideWhenOverlapped;
    public MenuBarVisibility MenuBar { get; set; } = MenuBarVisibility.HideWhenMaximized;

    public double IconSize { get; set; } = 46;
    public bool Magnification { get; set; } = true;
    /// <summary>Largest scale an icon reaches under the pointer.</summary>
    public double MagnifiedScale { get; set; } = 1.75;

    public MinimizeEffect MinimizeEffect { get; set; } = MinimizeEffect.Genie;
    /// <summary>Overlay red/yellow/green buttons on app windows. Off by default: apps keep their normal Windows buttons.</summary>
    public bool TrafficLights { get; set; }
    /// <summary>Process names (e.g. "explorer.exe") that keep their own window buttons.</summary>
    public List<string> TrafficLightExclusions { get; set; } = [];

    /// <summary>Hide the Windows taskbar while Orchard runs. It is always restored on exit, even after a crash.</summary>
    public bool HideWindowsTaskbar { get; set; } = true;
    public Appearance Appearance { get; set; } = Appearance.Auto;

    public bool ShowWidgets { get; set; } = true;
    public List<WidgetKind> Widgets { get; set; } = [WidgetKind.Clock, WidgetKind.Calendar, WidgetKind.Weather, WidgetKind.Battery, WidgetKind.System];
    /// <summary>City for the Weather widget, e.g. "London". Empty = the widget asks you to set one.</summary>
    public string WeatherCity { get; set; } = "";
    public bool Fahrenheit { get; set; }
    public bool ShowBatteryPercent { get; set; } = true;

    public List<PinnedApp> Pinned { get; set; } =
    [
        new("File Explorer", "explorer.exe", "files"),
        new("Microsoft Edge", @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
        new("Mail", "outlookmail:", "mail"),
        new("Photos", "ms-photos:", "photos"),
        new("Calendar", "outlookcal:", "calendar"),
        new("Notepad", "notepad.exe"),
        new("Terminal", "wt.exe", "terminal"),
        new("Calculator", "calculator:", "calculator"),
        new("Microsoft Store", "ms-windows-store:", "store"),
        new("Settings", "ms-settings:", "settings"),
    ];
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orchard");
    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static OrchardSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<OrchardSettings>(File.ReadAllText(FilePath), Options) is { } loaded)
            {
                if (Migrate(loaded)) Save(loaded);
                return loaded;
            }
        }
        catch (Exception e)
        {
            Log.Write($"settings.json unreadable, using defaults: {e.Message}");
        }
        var defaults = new OrchardSettings { SchemaVersion = OrchardSettings.CurrentSchema };
        Save(defaults);
        return defaults;
    }

    public static void Save(OrchardSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception e)
        {
            Log.Write($"could not save settings: {e.Message}");
        }
    }

    /// <summary>
    /// Schema 2 (v0.3): the menu bar hides for maximized apps and the red/yellow/green overlay is off by default.
    /// Schema 3 (v0.3.4): default dock icon size 54 → 46.
    /// Files written by earlier versions saved the old defaults explicitly, so they are moved over once.
    /// </summary>
    internal static bool Migrate(OrchardSettings s)
    {
        int from = s.SchemaVersion ?? 0;
        if (from >= OrchardSettings.CurrentSchema) return false;
        if (from < 2)
        {
            if (s.MenuBar == MenuBarVisibility.HideInFullscreen) s.MenuBar = MenuBarVisibility.HideWhenMaximized;
            s.TrafficLights = false;
        }
        // Schema 3 (v0.3.4): slimmer dock. Only move people who were still on the old default size.
        if (from < 3 && Math.Abs(s.IconSize - 54) < 0.01) s.IconSize = 46;
        s.SchemaVersion = OrchardSettings.CurrentSchema;
        return true;
    }

    internal static string Serialize(OrchardSettings settings) => JsonSerializer.Serialize(settings, Options);
    internal static OrchardSettings? Deserialize(string json) => JsonSerializer.Deserialize<OrchardSettings>(json, Options);
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(SettingsStore.Directory);
                File.AppendAllText(Path.Combine(SettingsStore.Directory, "orchard.log"), line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the shell down.
        }
    }
}
