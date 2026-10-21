using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Orchard.Platform.Windows;

/// <summary>"Open at sign-in" via the per-user Run key — no admin rights, and the installer/uninstaller use the same value.</summary>
[SupportedOSPlatform("windows")]
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Orchard";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe) key.SetValue(Name, $"\"{exe}\"");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
