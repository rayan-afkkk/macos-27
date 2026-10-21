using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Orchard.Platform.Windows;

/// <summary>Grabs a window's pixels (even when partly covered) so the genie animation can morph a picture of it.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowCapture
{
    public static Bitmap? Capture(nint hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var outer)) return null;
        var visible = Native.VisibleBounds(hwnd);
        int w = outer.Width, h = outer.Height;
        if (w <= 0 || h <= 0 || w > 16384 || h > 16384) return null;

        nint screen = Native.GetDC(0);
        nint mem = Native.CreateCompatibleDC(screen);
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
        };
        nint dib = Native.CreateDIBSection(mem, ref bmi, 0, out nint bits, 0, 0);
        nint old = Native.SelectObject(mem, dib);
        try
        {
            // PW_RENDERFULLCONTENT captures DirectComposition / GPU-rendered content (browsers, UWP).
            if (!Native.PrintWindow(hwnd, mem, Native.PW_RENDERFULLCONTENT)) return null;

            // Crop away the invisible resize border so the snapshot lines up with what the user sees.
            int cx = Math.Clamp(visible.Left - outer.Left, 0, w - 1), cy = Math.Clamp(visible.Top - outer.Top, 0, h - 1);
            int cw = Math.Clamp(visible.Width, 1, w - cx), ch = Math.Clamp(visible.Height, 1, h - cy);
            var pixels = new byte[cw * ch * 4];
            for (int y = 0; y < ch; y++)
                Marshal.Copy(bits + ((cy + y) * w + cx) * 4, pixels, y * cw * 4, cw * 4);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // GDI leaves alpha undefined

            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, handle.AddrOfPinnedObject(), new PixelSize(cw, ch), new Vector(96, 96), cw * 4);
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(dib);
            Native.DeleteDC(mem);
            Native.ReleaseDC(0, screen);
        }
    }
}
