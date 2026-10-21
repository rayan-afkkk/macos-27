using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Orchard.Model;
using Orchard.Theme;

namespace Orchard.Platform.Windows;

/// <summary>
/// Laptop panel brightness through WMI (WmiMonitorBrightness). External monitors usually don't support it,
/// in which case <see cref="Read"/> returns null and the Control Center slider says so.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BrightnessControl
{
    private static CancellationTokenSource? _pending;

    public static double? Read()
    {
        var output = PowerShell("(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -ErrorAction Stop | Select-Object -First 1).CurrentBrightness", 4000);
        return int.TryParse(output?.Trim(), out int pct) ? pct / 100.0 : null;
    }

    /// <summary>Debounced: dragging the slider fires many values, only the last one is applied.</summary>
    public static void Write(double level)
    {
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        int pct = (int)Math.Round(Math.Clamp(level, 0, 1) * 100);
        Task.Delay(150, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            PowerShell($"Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods | Invoke-CimMethod -MethodName WmiSetBrightness -Arguments @{{Timeout=1;Brightness={pct}}}", 5000);
        }, TaskScheduler.Default);
    }

    private static string? PowerShell(string command, int timeoutMs)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{command}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { p.Kill(); return null; }
            return p.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception e)
        {
            Log.Write($"powershell failed: {e.Message}");
            return null;
        }
    }
}

/// <summary>Flips Windows between light and dark mode (the same registry values the Settings app writes).</summary>
[SupportedOSPlatform("windows")]
internal static class ThemeSwitch
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    public static void SetDark(bool dark)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        k.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        k.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        // Tell running apps to re-read the theme.
        Native.SendNotifyMessageW(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE, 0, "ImmersiveColorSet");
        Palette.Dark = dark;
    }
}
