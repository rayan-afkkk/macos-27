using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Orchard.Model;
using Orchard.Platform.Windows;
using Orchard.Preview;
using Orchard.Shell;

namespace Orchard;

internal static class Program
{
    /// <summary>
    /// <c>Orchard.exe</c>             run the shell on the Windows desktop
    /// <c>Orchard.exe --preview</c>   run the whole shell inside a single window (any OS) to try it safely
    /// <c>Orchard.exe --restore</c>   put the taskbar and animations back (if something ever went wrong)
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            int w = Array.IndexOf(args, "--watchdog");
            if (w >= 0 && w + 1 < args.Length && int.TryParse(args[w + 1], out int parent))
                return SystemState.RunWatchdog(parent);
            if (args.Contains("--restore"))
            {
                SystemState.RestoreFromDisk();
                return 0;
            }
        }

        bool preview = args.Contains("--preview") || !OperatingSystem.IsWindows();
        using var single = new Mutex(true, preview ? @"Local\OrchardPreview" : @"Local\OrchardShell", out bool first);
        if (!first) return 0;

        var settings = SettingsStore.Load();
        App.Startup = app => preview ? StartPreview(settings) : StartShell(settings);

        if (!preview && OperatingSystem.IsWindows())
        {
            // Whatever happens, never leave the user without their taskbar.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Log.Write($"crash: {e.ExceptionObject}");
                if (OperatingSystem.IsWindows()) SystemState.Restore();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { if (OperatingSystem.IsWindows()) SystemState.Restore(); };
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static IDisposable StartShell(OrchardSettings settings)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var host = new ShellHost(settings, new WindowsPlatform(settings));
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
            life.Exit += (_, _) => { if (OperatingSystem.IsWindows()) host.Dispose(); };
        return host;
    }

    private static IDisposable StartPreview(OrchardSettings settings)
    {
        Theme.Palette.Dark = settings.DarkMode;
        var window = new Window
        {
            Title = "Orchard — preview",
            Width = 1440, Height = 900, CanResize = false,
            Content = new DesktopScene(new PixelSize(1440, 900), settings),
        };
        window.Closed += (_, _) => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        window.Show();
        return window as IDisposable ?? new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
}
