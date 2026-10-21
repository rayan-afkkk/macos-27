using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Orchard.Model;

namespace Orchard.Platform.Windows;

/// <summary>
/// Owns every system-wide change Orchard makes — hiding the taskbar and turning off the built-in minimize
/// animation — and guarantees they are undone. The original values are written to disk before anything is
/// changed so the watchdog process (or the next launch) can restore them if Orchard dies unexpectedly.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SystemState
{
    private sealed record Saved(int TaskbarState, bool MinimizeAnimation);

    private static string StatePath => Path.Combine(SettingsStore.Directory, "restore.json");
    private static Saved? _saved;

    public static void Capture()
    {
        // A leftover file means the last session crashed before restoring; put things back first.
        if (File.Exists(StatePath)) RestoreFromDisk();

        var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Native.FindWindowW("Shell_TrayWnd", null) };
        int state = (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd);
        _saved = new Saved(state, GetMinimizeAnimation());
        Directory.CreateDirectory(SettingsStore.Directory);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(_saved));
    }

    // ---- taskbar -------------------------------------------------------------------------------

    public static IEnumerable<nint> Taskbars()
    {
        nint primary = Native.FindWindowW("Shell_TrayWnd", null);
        if (primary != 0) yield return primary;
        nint next = 0;
        while ((next = Native.FindWindowExW(0, next, "Shell_SecondaryTrayWnd", null)) != 0) yield return next;
    }

    /// <summary>
    /// Auto-hide first (so Windows gives the reserved strip back to the work area), then hide the window itself
    /// so its reveal zone at the bottom edge can't fight with the dock's.
    /// </summary>
    public static void HideTaskbar()
    {
        SetTaskbarState(Native.ABS_AUTOHIDE);
        foreach (var tb in Taskbars())
            if (Native.IsWindowVisible(tb)) Native.ShowWindow(tb, Native.SW_HIDE);
    }

    public static bool TaskbarVisible() => Taskbars().Any(Native.IsWindowVisible);

    private static void SetTaskbarState(int state)
    {
        var abd = new Native.APPBARDATA
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(),
            hWnd = Native.FindWindowW("Shell_TrayWnd", null),
            lParam = state,
        };
        Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
    }

    // ---- minimize animation ------------------------------------------------------------------

    public static bool GetMinimizeAnimation()
    {
        var ai = new Native.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<Native.ANIMATIONINFO>() };
        Native.SystemParametersInfoW(Native.SPI_GETANIMATION, ai.cbSize, ref ai, 0);
        return ai.iMinAnimate != 0;
    }

    /// <summary>Not persisted to the user profile (no SPIF_UPDATEINIFILE), so a reboot always resets it too.</summary>
    public static void SetMinimizeAnimation(bool enabled)
    {
        var ai = new Native.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<Native.ANIMATIONINFO>(), iMinAnimate = enabled ? 1 : 0 };
        Native.SystemParametersInfoW(Native.SPI_SETANIMATION, ai.cbSize, ref ai, Native.SPIF_SENDCHANGE);
    }

    // ---- restore -------------------------------------------------------------------------------

    public static void Restore()
    {
        if (_saved == null) { RestoreFromDisk(); return; }
        Apply(_saved);
        TryDelete();
    }

    public static void RestoreFromDisk()
    {
        try
        {
            var saved = File.Exists(StatePath) ? JsonSerializer.Deserialize<Saved>(File.ReadAllText(StatePath)) : null;
            Apply(saved ?? new Saved(Native.ABS_ALWAYSONTOP, true));
        }
        catch (Exception e)
        {
            Log.Write($"restore from disk failed: {e.Message}");
            Apply(new Saved(Native.ABS_ALWAYSONTOP, true));
        }
        TryDelete();
    }

    private static void Apply(Saved saved)
    {
        SetTaskbarState(saved.TaskbarState);
        foreach (var tb in Taskbars()) Native.ShowWindow(tb, Native.SW_SHOWNA);
        SetMinimizeAnimation(saved.MinimizeAnimation);
    }

    private static void TryDelete()
    {
        try { File.Delete(StatePath); } catch { /* best effort */ }
    }

    // ---- watchdog ------------------------------------------------------------------------------

    /// <summary>
    /// Starts a second copy of Orchard in watchdog mode. It does nothing but wait for this process to exit,
    /// then restores the taskbar — so even a hard crash or a Task Manager kill never leaves you without one.
    /// </summary>
    public static void StartWatchdog()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            Process.Start(new ProcessStartInfo(exe, $"--watchdog {Environment.ProcessId}") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception e)
        {
            Log.Write($"watchdog failed to start: {e.Message}");
        }
    }

    public static int RunWatchdog(int parentPid)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            parent.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Parent already gone.
        }
        if (File.Exists(StatePath))
        {
            Log.Write("watchdog: Orchard exited without restoring — restoring taskbar and animations");
            RestoreFromDisk();
        }
        return 0;
    }
}
