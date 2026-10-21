using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Orchard.Platform.Windows;

/// <summary>
/// Registers the menu bar as a desktop "app bar" so Windows shrinks the work area: maximized windows then stop
/// below the menu bar instead of sliding under it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AppBar : IDisposable
{
    private readonly nint _hwnd;
    private bool _registered;

    public AppBar(nint hwnd, Native.RECT monitor, int height)
    {
        _hwnd = hwnd;
        var abd = Data();
        abd.uCallbackMessage = Native.RegisterWindowMessageW("OrchardAppBar");
        if (Native.SHAppBarMessage(Native.ABM_NEW, ref abd) == 0) return;
        _registered = true;

        abd = Data();
        abd.uEdge = Native.ABE_TOP;
        abd.rc = new Native.RECT(monitor.Left, monitor.Top, monitor.Right, monitor.Top + height);
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
        abd.rc.Bottom = abd.rc.Top + height; // QUERYPOS may move the top edge; keep our height
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
    }

    private Native.APPBARDATA Data() => new() { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = _hwnd };

    public void Dispose()
    {
        if (!_registered) return;
        var abd = Data();
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
        _registered = false;
    }
}
