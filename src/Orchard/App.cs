using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Orchard;

public sealed class App : Application
{
    /// <summary>Set by <see cref="Program"/> before start-up; the headless screenshot tool leaves it null.</summary>
    public static Func<App, IDisposable>? Startup { get; set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (Startup != null) _shell = Startup(this);
    }

    private IDisposable? _shell;
}
