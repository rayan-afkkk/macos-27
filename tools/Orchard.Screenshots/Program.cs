using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orchard;
using Orchard.Controls;
using Orchard.Model;
using Orchard.Preview;
using Orchard.Theme;

// Renders Orchard's real UI controls headlessly (Skia, no window system needed) into PNGs under docs/screenshots.
// Usage: dotnet run --project tools/Orchard.Screenshots [output-dir]

if (args.Length > 0 && args[0] == "--icon")
{
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    var root = FindRepoRoot();
    AppIcon.Write(Path.Combine(root, "src", "Orchard", "Assets", "orchard.ico"), Path.Combine(root, "docs", "icon.png"));
    return;
}

if (args.Length > 0 && args[0] == "--video")
{
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    Promo.Render(Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(FindRepoRoot(), "docs", "orchard-promo.mp4")));
    return;
}

var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(FindRepoRoot(), "docs", "screenshots"));
Directory.CreateDirectory(outDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

const double Scale = 1;
var screen = new PixelSize(1440, 900);
var clock = DateTime.Today.AddHours(9).AddMinutes(41);

Shot("01-desktop", scene =>
{
    scene.Dock.SimulatePointer(DockX(scene, "messages"), Find(scene, "messages"));
});

Shot("02-menu-bar-menu", scene =>
{
    scene.OpenMenu(0).HighlightIndex = 2;
});

Shot("03-app-menu", scene =>
{
    scene.OpenMenu(3).HighlightIndex = 4;
});

Shot("04-control-center", scene => scene.OpenControlCenter());

Shot("05-spotlight", scene =>
{
    scene.OpenSpotlightView("c");
});

Shot("06-spotlight-calculator", scene =>
{
    scene.OpenSpotlightView("(1440*900)/2");
});

Shot("07-launchpad", scene => scene.ShowLaunchpad());

Shot("08-genie-minimize", scene =>
{
    var browser = scene.Windows.First(w => w.Title == "orchard.dev");
    scene.ShowGenieFrame(browser, Find(scene, "browser"), 0.5);
});

Shot("09-maximized-dock-hidden", scene =>
{
    var browser = scene.Windows.First(w => w.Title == "orchard.dev");
    scene.SetMaximized(browser, true);
    scene.Dock.HiddenTarget = 1;
    scene.Dock.SnapHidden();
});

Shot("10-maximized-dock-revealed", scene =>
{
    var browser = scene.Windows.First(w => w.Title == "orchard.dev");
    scene.SetMaximized(browser, true);
    scene.Dock.HiddenTarget = 0;
    scene.Dock.SnapHidden();
    scene.Dock.SimulatePointer(DockX(scene, "browser"), Find(scene, "browser"));
});

Shot("11-dark-mode", scene =>
{
    scene.Dock.SimulatePointer(DockX(scene, "photos"), Find(scene, "photos"));
}, dark: true);

// As it looks on a real Windows desktop: solid menu bar, denser glass (no compositor blur behind the dock).
Shot("13-dark-control-center", scene =>
{
    Palette.Solid = true;
    scene.MenuBar.Solid = true;
    scene.OpenControlCenter();
}, dark: true);
Palette.Solid = false;

Shot("14-dark-spotlight", scene => scene.OpenSpotlightView("ca"), dark: true);

TrafficLightCloseup();
SettingsShot();

Console.WriteLine($"Screenshots written to {outDir}");
return;

void Shot(string name, Action<DesktopScene> setup, bool dark = false)
{
    Palette.Dark = dark;
    var settings = new OrchardSettings();
    var scene = new DesktopScene(screen, settings, Scale);
    scene.MenuBar.FixedNow = clock;
    scene.Widgets.FixedNow = clock;
    var window = new Window { Width = screen.Width, Height = screen.Height, Content = scene, SystemDecorations = SystemDecorations.None };
    window.Show();
    Pump();
    setup(scene);
    Pump();
    Save(scene, new Size(screen.Width, screen.Height), name);
    window.Close();
}

void TrafficLightCloseup()
{
    Palette.Dark = false;
    var scene = new DesktopScene(screen, new OrchardSettings(), Scale);
    var window = new Window { Width = screen.Width, Height = screen.Height, Content = scene, SystemDecorations = SystemDecorations.None };
    window.Show();
    Pump();
    var browser = scene.Windows.First(w => w.Title == "orchard.dev");
    var notes = scene.Windows.First(w => w.Title == "Notes");
    // Three states side by side: active, hovered (symbols showing) and inactive.
    var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 28, Margin = new Thickness(24), Background = Avalonia.Media.Brushes.Transparent };
    foreach (var (label, active, hover) in new[] { ("Active", true, false), ("Hover", true, true), ("Inactive window", false, false) })
    {
        var lights = new TrafficLights { IsActive = active, ForceHover = hover };
        var caption = new TextBlock { Text = label, FontFamily = Palette.Font, FontSize = 11, Foreground = Palette.Brush(Palette.TextSecondary), Margin = new Thickness(0, 6, 0, 0) };
        var cell = new StackPanel { Children = { lights, caption } };
        strip.Children.Add(cell);
    }
    var card = new Border
    {
        Background = Palette.Brush(Avalonia.Media.Color.FromRgb(0xF2, 0xF2, 0xF5)),
        CornerRadius = new CornerRadius(14), Child = strip,
    };
    var host = new Window { Width = 360, Height = 80, Content = card, SystemDecorations = SystemDecorations.None };
    host.Show();
    Pump();
    var bmp = new RenderTargetBitmap(new PixelSize(360 * 4, 80 * 4), new Vector(96 * 4, 96 * 4));
    bmp.Render(card);
    bmp.Save(Path.Combine(outDir, "12-traffic-lights.png"));
    host.Close();
    window.Close();
}

void SettingsShot()
{
    Palette.Dark = false;
    var w = new Orchard.Shell.SettingsWindow(new OrchardSettings { WeatherCity = "Lahore" }, canRestart: true);
    w.Show();
    Pump();
    Save(w, new Size(w.Width, w.Height), "15-settings");
    w.Close();
}

void Save(Control c, Size size, string name)
{
    var bmp = new RenderTargetBitmap(new PixelSize((int)(size.Width * Scale), (int)(size.Height * Scale)), new Vector(96 * Scale, 96 * Scale));
    bmp.Render(c);
    bmp.Save(Path.Combine(outDir, name + ".png"));
    Console.WriteLine($"  {name}.png");
}

void Pump()
{
    for (int i = 0; i < 3; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

AppEntry Find(DesktopScene scene, string id) => scene.Platform.DockApps.First(a => a.Id == id);

double DockX(DesktopScene scene, string id)
{
    scene.Dock.SimulatePointer(null);
    return scene.Dock.IconRect(Find(scene, id))!.Value.Center.X;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Orchard.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
