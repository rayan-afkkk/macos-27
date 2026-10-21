using Avalonia;
using Orchard.Model;
using Orchard.Services;

namespace Orchard.Tests;

public class DockMathTests
{
    [Fact]
    public void Scale_is_max_under_pointer_and_one_outside_range()
    {
        Assert.Equal(1.75, DockMath.Scale(0, 100, 1.75), 6);
        Assert.Equal(1, DockMath.Scale(100, 100, 1.75));
        Assert.Equal(1, DockMath.Scale(-250, 100, 1.75));
    }

    [Fact]
    public void Scale_falls_off_monotonically_and_symmetrically()
    {
        double prev = double.MaxValue;
        for (double d = 0; d <= 100; d += 5)
        {
            double s = DockMath.Scale(d, 100, 2);
            Assert.True(s <= prev);
            Assert.Equal(s, DockMath.Scale(-d, 100, 2), 9);
            prev = s;
        }
    }

    [Fact]
    public void Row_adds_gaps_and_separators()
    {
        var (lefts, width) = DockMath.Row([1.0, 1.0, 1.0], 50, 6, new HashSet<int> { 2 }, 1);
        Assert.Equal([0, 56, 56 + 50 + 6 + 1 + 6], lefts);
        Assert.Equal(lefts[2] + 50, width);
    }

    [Fact]
    public void Row_width_grows_with_magnification()
    {
        var (_, rest) = DockMath.Row([1.0, 1.0, 1.0], 50, 6);
        var (_, magnified) = DockMath.Row([1.2, 1.75, 1.2], 50, 6);
        Assert.Equal(rest + 50 * (0.2 + 0.75 + 0.2), magnified, 6);
    }

    [Fact]
    public void Bounce_hops_twice_then_settles()
    {
        Assert.Equal(0, DockMath.Bounce(0), 6);
        Assert.True(DockMath.Bounce(0.225) > DockMath.Bounce(0.675)); // second hop is lower
        Assert.True(DockMath.Bounce(0.675) > 0);
        Assert.Equal(0, DockMath.Bounce(1.0));
    }

    [Fact]
    public void Approach_converges_to_target()
    {
        double v = 0;
        for (int i = 0; i < 120; i++) v = DockMath.Approach(v, 1, 1 / 60.0);
        Assert.Equal(1, v);
    }
}

public class VisibilityTests
{
    private static readonly Rect Monitor = new(0, 0, 1920, 1080);
    private static readonly Rect Dock = new(500, 1000, 920, 80);

    [Fact]
    public void Maximized_window_hides_dock_in_default_mode()
    {
        var fg = new ForegroundInfo(new Rect(0, 28, 1920, 1052), IsMaximized: true, IsFullscreen: false, IsAppWindow: true);
        Assert.True(VisibilityRules.DockWantsHide(DockVisibility.HideWhenOverlapped, fg, Dock));
    }

    [Fact]
    public void Small_window_away_from_dock_keeps_it()
    {
        var fg = new ForegroundInfo(new Rect(100, 100, 800, 600), false, false, true);
        Assert.False(VisibilityRules.DockWantsHide(DockVisibility.HideWhenOverlapped, fg, Dock));
    }

    [Fact]
    public void Window_dragged_over_dock_hides_it()
    {
        var fg = new ForegroundInfo(new Rect(600, 500, 800, 560), false, false, true);
        Assert.True(VisibilityRules.DockWantsHide(DockVisibility.HideWhenOverlapped, fg, Dock));
    }

    [Fact]
    public void Desktop_focus_never_hides_dock()
    {
        Assert.False(VisibilityRules.DockWantsHide(DockVisibility.HideWhenOverlapped, null, Dock));
    }

    [Fact]
    public void Always_modes_ignore_windows()
    {
        var fg = new ForegroundInfo(Monitor, true, true, true);
        Assert.False(VisibilityRules.DockWantsHide(DockVisibility.AlwaysShow, fg, Dock));
        Assert.True(VisibilityRules.DockWantsHide(DockVisibility.AlwaysAutoHide, null, Dock));
    }

    [Fact]
    public void Menu_bar_hides_only_for_fullscreen_by_default()
    {
        var maximized = new ForegroundInfo(new Rect(0, 28, 1920, 1052), true, false, true);
        var fullscreen = new ForegroundInfo(Monitor, false, true, true);
        Assert.False(VisibilityRules.MenuBarWantsHide(MenuBarVisibility.HideInFullscreen, maximized));
        Assert.True(VisibilityRules.MenuBarWantsHide(MenuBarVisibility.HideInFullscreen, fullscreen));
    }

    [Fact]
    public void Default_menu_bar_mode_hides_for_maximized_and_fullscreen_only()
    {
        var maximized = new ForegroundInfo(new Rect(0, 0, 1920, 1080), true, false, true);
        var normal = new ForegroundInfo(new Rect(100, 100, 800, 600), false, false, true);
        Assert.True(VisibilityRules.MenuBarWantsHide(MenuBarVisibility.HideWhenMaximized, maximized));
        Assert.False(VisibilityRules.MenuBarWantsHide(MenuBarVisibility.HideWhenMaximized, normal));
        Assert.False(VisibilityRules.MenuBarWantsHide(MenuBarVisibility.HideWhenMaximized, null));
    }

    [Fact]
    public void Covers_monitor_detects_borderless_fullscreen()
    {
        Assert.True(VisibilityRules.CoversMonitor(new Rect(-1, -1, 1922, 1082), Monitor));
        Assert.False(VisibilityRules.CoversMonitor(new Rect(0, 28, 1920, 1052), Monitor));
    }
}

public class EdgeRevealTests
{
    private static readonly DateTime T0 = new(2026, 1, 1);
    private static DateTime At(int ms) => T0.AddMilliseconds(ms);

    [Fact]
    public void Hides_after_delay_when_asked()
    {
        var r = new EdgeReveal();
        r.Update(At(0), wantsHide: true, pointerAtEdge: false, engaged: false);
        Assert.True(r.Visible); // not instantly
        r.Update(At(700), true, false, false);
        Assert.False(r.Visible);
    }

    [Fact]
    public void Brief_touch_of_edge_does_not_reveal()
    {
        var r = new EdgeReveal();
        r.Update(At(0), true, false, false);
        r.Update(At(700), true, false, false);
        r.Update(At(800), true, pointerAtEdge: true, false);
        r.Update(At(900), true, pointerAtEdge: false, false);
        r.Update(At(1100), true, pointerAtEdge: true, false);
        Assert.False(r.Visible);
    }

    [Fact]
    public void Resting_on_edge_reveals_and_stays_while_engaged()
    {
        var r = new EdgeReveal();
        r.Update(At(0), true, false, false);
        r.Update(At(700), true, false, false);
        r.Update(At(800), true, true, false);
        r.Update(At(1060), true, true, false);
        Assert.True(r.Visible);
        r.Update(At(3000), true, false, engaged: true);
        Assert.True(r.Visible);
        r.Update(At(3100), true, false, false);
        r.Update(At(3800), true, false, false);
        Assert.False(r.Visible);
    }

    [Fact]
    public void Shows_immediately_when_no_longer_wanted_hidden()
    {
        var r = new EdgeReveal();
        r.Update(At(0), true, false, false);
        r.Update(At(700), true, false, false);
        Assert.False(r.Visible);
        Assert.True(r.Update(At(710), false, false, false));
        Assert.True(r.Visible);
    }
}

public class CalculatorTests
{
    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2^3^2", 512)]
    [InlineData("-4+10", 6)]
    [InlineData("10/4", 2.5)]
    [InlineData("sqrt(16)+1", 5)]
    [InlineData("3 x 4", 12)]
    public void Evaluates(string expr, double expected) => Assert.Equal(expected, Calculator.TryEvaluate(expr)!.Value, 9);

    [Theory]
    [InlineData("1+")]
    [InlineData("(2")]
    [InlineData("hello")]
    [InlineData("1/0")]
    public void Rejects(string expr) => Assert.Null(Calculator.TryEvaluate(expr));

    [Fact]
    public void Only_math_looking_input_is_treated_as_math()
    {
        Assert.True(Calculator.LooksLikeMath("12*(3+4)"));
        Assert.False(Calculator.LooksLikeMath("chrome"));
        Assert.False(Calculator.LooksLikeMath("42"));
    }
}

public class AppSearchTests
{
    private static readonly string[] Apps = ["Google Chrome", "Calculator", "Visual Studio Code", "Calendar", "Command Prompt", "Camera"];

    [Fact]
    public void Prefix_beats_word_match_beats_substring()
    {
        Assert.True(AppSearch.Score("cal", "Calculator") > AppSearch.Score("cal", "Google Calendar"));
        Assert.True(AppSearch.Score("chrome", "Google Chrome") > AppSearch.Score("rom", "Command Prompt"));
    }

    [Fact]
    public void Acronyms_and_subsequences_match()
    {
        Assert.Equal("Visual Studio Code", AppSearch.Rank(Apps, "vsc", a => a).First());
        Assert.Contains("Google Chrome", AppSearch.Rank(Apps, "chrm", a => a));
    }

    [Fact]
    public void Nonsense_matches_nothing()
    {
        Assert.Empty(AppSearch.Rank(Apps, "zzzq", a => a));
    }
}

public class SettingsTests
{
    [Fact]
    public void Custom_dock_size_survives_migration()
    {
        var s = SettingsStore.Deserialize("""{ "SchemaVersion": 2, "IconSize": 62 }""")!;
        Assert.True(SettingsStore.Migrate(s));
        Assert.Equal(62, s.IconSize);
    }

    [Fact]
    public void Old_settings_files_move_to_new_defaults_once()
    {
        // A v0.2 file: no SchemaVersion, old defaults written out explicitly.
        var old = SettingsStore.Deserialize("""{ "MenuBar": "HideInFullscreen", "TrafficLights": true, "Dock": "AlwaysShow" }""")!;
        Assert.True(SettingsStore.Migrate(old));
        Assert.Equal(MenuBarVisibility.HideWhenMaximized, old.MenuBar);
        Assert.False(old.TrafficLights);
        Assert.Equal(DockVisibility.AlwaysShow, old.Dock); // unrelated choices are kept

        // Schema 3: the old default dock size moves to the new, slimmer default...
        Assert.Equal(46, old.IconSize);

        // Once migrated, the user's own choices stick.
        old.TrafficLights = true;
        Assert.False(SettingsStore.Migrate(old));
        Assert.True(old.TrafficLights);
    }

    [Fact]
    public void Round_trips_through_json_with_readable_enums()
    {
        var s = new OrchardSettings { Dock = DockVisibility.AlwaysAutoHide, MinimizeEffect = MinimizeEffect.Scale, IconSize = 60 };
        var json = SettingsStore.Serialize(s);
        Assert.Contains("\"AlwaysAutoHide\"", json);
        var back = SettingsStore.Deserialize(json)!;
        Assert.Equal(DockVisibility.AlwaysAutoHide, back.Dock);
        Assert.Equal(MinimizeEffect.Scale, back.MinimizeEffect);
        Assert.Equal(60, back.IconSize);
        Assert.Equal(s.Pinned.Count, back.Pinned.Count);
    }

    [Fact]
    public void Defaults_hide_dock_and_menu_bar_for_maximized_windows()
    {
        var s = new OrchardSettings();
        Assert.Equal(DockVisibility.HideWhenOverlapped, s.Dock);
        Assert.Equal(MenuBarVisibility.HideWhenMaximized, s.MenuBar);
        Assert.False(s.TrafficLights);
        Assert.True(s.HideWindowsTaskbar);
    }
}

public class WeatherTests
{
    [Theory]
    [InlineData(0, WeatherKind.Clear)]
    [InlineData(2, WeatherKind.PartlyCloudy)]
    [InlineData(3, WeatherKind.Cloudy)]
    [InlineData(45, WeatherKind.Fog)]
    [InlineData(63, WeatherKind.Rain)]
    [InlineData(81, WeatherKind.Rain)]
    [InlineData(73, WeatherKind.Snow)]
    [InlineData(95, WeatherKind.Storm)]
    public void Maps_wmo_codes(int code, WeatherKind kind) => Assert.Equal(kind, WeatherInfo.FromWmo(code));

    [Fact]
    public void Parses_open_meteo_forecast()
    {
        const string json = """
        {
          "current": { "time": "2026-10-21T09:45", "temperature_2m": 17.6, "weather_code": 2, "is_day": 1 },
          "hourly": {
            "time": ["2026-10-21T09:00","2026-10-21T10:00","2026-10-21T11:00","2026-10-21T12:00"],
            "temperature_2m": [17.0, 18.2, 19.9, 21.0],
            "weather_code": [2, 0, 61, 95]
          },
          "daily": { "temperature_2m_max": [23.4], "temperature_2m_min": [12.1] }
        }
        """;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var w = WeatherService.Parse(doc.RootElement, "London", fahrenheit: false);
        Assert.Equal("London", w.City);
        Assert.Equal(17.6, w.Temperature);
        Assert.Equal("Partly Cloudy", w.Condition);
        Assert.Equal(23.4, w.High);
        Assert.Equal(12.1, w.Low);
        Assert.Equal(3, w.Hourly.Count); // only hours after "now"
        Assert.Equal(WeatherKind.Rain, w.Hourly[1].Kind);
        Assert.Equal(WeatherKind.Storm, w.Hourly[2].Kind);
    }
}
