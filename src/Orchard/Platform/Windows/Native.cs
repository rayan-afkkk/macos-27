using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Orchard.Platform.Windows;

/// <summary>Raw Win32 declarations. Everything Windows-specific that Orchard calls lives behind this class.</summary>
[SupportedOSPlatform("windows")]
internal static class Native
{
    // ---- structs -------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ANIMATIONINFO
    {
        public uint cbSize;
        public int iMinAnimate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public nint hbmMask, hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        private readonly long _padding; // INPUT is a union; MOUSEINPUT is the largest member.
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ACCENT_POLICY
    {
        public int AccentState, AccentFlags, GradientColor, AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    // ---- constants -----------------------------------------------------------------------------

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_CAPTION = 0x00C00000, WS_CHILD = 0x40000000, WS_POPUP = 0x80000000;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000,
        WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOPMOST = 0x8;

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_MAXIMIZE = 3, SW_SHOWNOACTIVATE = 4,
        SW_SHOW = 5, SW_MINIMIZE = 6, SW_SHOWNA = 8, SW_RESTORE = 9;

    public const uint WM_SYSCOMMAND = 0x0112, WM_CLOSE = 0x0010, WM_HOTKEY = 0x0312;
    public const int SC_CLOSE = 0xF060, SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030, SC_RESTORE = 0xF120;

    public const uint GW_OWNER = 4;
    public const uint GA_ROOT = 2, GA_ROOTOWNER = 3;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017,
        EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003,
        EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_OBJECT_NAMECHANGE = 0x800C, EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    public const int OBJID_WINDOW = 0, CHILDID_SELF = 0;

    public const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1;

    public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    public const uint ABE_TOP = 1, ABE_BOTTOM = 3;
    public const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    public const uint SPI_GETANIMATION = 0x0048, SPI_SETANIMATION = 0x0049, SPIF_SENDCHANGE = 2;

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14, DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2;

    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20;
    public static readonly nint HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;

    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    public const uint VK_SPACE = 0x20, VK_MENU = 0x12, VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_ESCAPE = 0x1B, VK_LWIN = 0x5B;
    public const uint KEYEVENTF_KEYUP = 0x2, INPUT_KEYBOARD = 1;

    public const int RGN_OR = 2;
    public const uint PW_RENDERFULLCONTENT = 2;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public const uint SHGFI_SYSICONINDEX = 0x4000, SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;
    public const int SHIL_EXTRALARGE = 2, SHIL_JUMBO = 4;
    public const int ILD_TRANSPARENT = 1;

    public const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    // ---- user32 --------------------------------------------------------------------------------

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent, EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(nint hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern int GetWindowTextLengthW(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(nint hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(nint hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hWnd, uint flags);
    [DllImport("user32.dll")] public static extern nint GetShellWindow();
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hWnd, uint flags);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowW(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern int SetWindowRgn(nint hWnd, nint rgn, bool redraw);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfoW(uint action, uint param, ref ANIMATIONINFO info, uint winIni);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, nint extra);
    [DllImport("user32.dll")] public static extern bool PrintWindow(nint hWnd, nint hdc, uint flags);
    [DllImport("user32.dll")] public static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint hWnd, nint hdc);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] public static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] public static extern bool GetIconInfo(nint icon, out ICONINFO info);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(nint hWnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SendNotifyMessageW(nint hWnd, uint msg, nint wParam, string lParam);
    public static readonly nint HWND_BROADCAST = 0xFFFF;
    public const uint WM_SETTINGCHANGE = 0x001A;

    // ---- gdi32 ---------------------------------------------------------------------------------

    [DllImport("gdi32.dll")] public static extern nint CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] public static extern nint CreateEllipticRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] public static extern nint CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] public static extern int CombineRgn(nint dest, nint a, nint b, int mode);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(nint hdc, nint bmp, uint start, uint lines, nint bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] public static extern int GetObject(nint obj, int size, out BITMAP bmp);

    // ---- dwmapi --------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hWnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hWnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint hWnd, int attr, ref int value, int size);

    // ---- shell32 -------------------------------------------------------------------------------

    [DllImport("shell32.dll")] public static extern nint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern nint SHGetFileInfoW(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll", EntryPoint = "SHGetImageList")] public static extern int SHGetImageList(int list, ref Guid iid, out IImageList ppv);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHEmptyRecycleBinW(nint hWnd, string? root, uint flags);

    // ---- kernel32 / powrprof -------------------------------------------------------------------

    [DllImport("kernel32.dll")] public static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);

    // ---- COM: IImageList (only the members we call; vtable order must match commctrl.h) ---------

    public static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IImageList
    {
        [PreserveSig] int Add(nint image, nint mask, ref int index);
        [PreserveSig] int ReplaceIcon(int i, nint icon, ref int index);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int i, nint image, nint mask);
        [PreserveSig] int AddMasked(nint image, int mask, ref int index);
        [PreserveSig] int Draw(nint parameters);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref nint icon);
    }

    // ---- helpers -------------------------------------------------------------------------------

    public static string GetText(nint hWnd)
    {
        int len = GetWindowTextLengthW(hWnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowTextW(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetClass(nint hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassNameW(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static long ExStyle(nint hWnd) => GetWindowLongPtr(hWnd, GWL_EXSTYLE);
    public static long Style(nint hWnd) => GetWindowLongPtr(hWnd, GWL_STYLE);

    public static bool IsCloaked(nint hWnd) =>
        DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>The window's visible bounds, without the invisible resize borders Windows 10/11 add.</summary>
    public static RECT VisibleBounds(nint hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0) return r;
        GetWindowRect(hWnd, out r);
        return r;
    }

    public static MONITORINFO MonitorFor(nint hWnd)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST), ref info);
        return info;
    }

    public static MONITORINFO PrimaryMonitor()
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY), ref info);
        return info;
    }

    public static string? ProcessPath(uint pid)
    {
        nint h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }
}
