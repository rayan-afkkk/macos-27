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
    AlwaysShow,
    /// <summary>Stays put for maximized windows, slides away for true fullscreen (games, video, F11).</summary>
    HideInFullscreen,
    AlwaysAutoHide,
}

public enum MinimizeEffect { Genie, Scale, System }

public sealed record PinnedApp(string Name, string Target, string? Icon = null, string? Arguments = null);

public sealed class OrchardSettings
{
    public DockVisibility Dock { get; set; } = DockVisibility.HideWhenOverlapped;
    public MenuBarVisibility MenuBar { get; set; } = MenuBarVisibility.HideInFullscreen;

    public double IconSize { get; set; } = 54;
    public bool Magnification { get; set; } = true;
    /// <summary>Largest scale an icon reaches under the pointer.</summary>
    public double MagnifiedScale { get; set; } = 1.75;

    public MinimizeEffect MinimizeEffect { get; set; } = MinimizeEffect.Genie;
    public bool TrafficLights { get; set; } = true;
    /// <summary>Process names (e.g. "explorer.exe") that keep their own window buttons.</summary>
    public List<string> TrafficLightExclusions { get; set; } = [];

    /// <summary>Hide the Windows taskbar while Orchard runs. It is always restored on exit, even after a crash.</summary>
    public bool HideWindowsTaskbar { get; set; } = true;
    public bool DarkMode { get; set; }
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
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<OrchardSettings>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch (Exception e)
        {
            Log.Write($"settings.json unreadable, using defaults: {e.Message}");
        }
        var defaults = new OrchardSettings();
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
