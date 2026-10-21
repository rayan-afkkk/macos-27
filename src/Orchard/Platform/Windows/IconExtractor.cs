using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Orchard.Platform.Windows;

/// <summary>Pulls the largest available shell icon for a file (exe, lnk, folder) and converts it to an Avalonia bitmap.</summary>
[SupportedOSPlatform("windows")]
internal static class IconExtractor
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? ForPath(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        Bitmap? result = null;
        try
        {
            var info = new Native.SHFILEINFO();
            if (Native.SHGetFileInfoW(path, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(), Native.SHGFI_SYSICONINDEX) != 0)
            {
                // Jumbo (256 px) first. Apps that only ship small icons come back as a 32 px image in the
                // corner of a 256 px canvas — detect that and fall back to the 48 px list instead.
                result = FromImageList(Native.SHIL_JUMBO, info.iIcon, rejectIfSmallContent: true)
                         ?? FromImageList(Native.SHIL_EXTRALARGE, info.iIcon, rejectIfSmallContent: false);
            }
        }
        catch (Exception e)
        {
            Model.Log.Write($"icon extraction failed for {path}: {e.Message}");
        }
        Cache[path] = result;
        return result;
    }

    private static Bitmap? FromImageList(int list, int index, bool rejectIfSmallContent)
    {
        var iid = Native.IID_IImageList;
        if (Native.SHGetImageList(list, ref iid, out var images) != 0 || images == null) return null;
        nint icon = 0;
        try
        {
            if (images.GetIcon(index, Native.ILD_TRANSPARENT, ref icon) != 0 || icon == 0) return null;
            return FromHIcon(icon, rejectIfSmallContent);
        }
        finally
        {
            if (icon != 0) Native.DestroyIcon(icon);
            Marshal.ReleaseComObject(images);
        }
    }

    public static Bitmap? FromHIcon(nint icon, bool rejectIfSmallContent = false)
    {
        if (!Native.GetIconInfo(icon, out var ii)) return null;
        try
        {
            if (ii.hbmColor == 0 || Native.GetObject(ii.hbmColor, Marshal.SizeOf<Native.BITMAP>(), out var bm) == 0) return null;
            int w = bm.bmWidth, h = bm.bmHeight;
            var pixels = ReadBits(ii.hbmColor, w, h);
            if (pixels == null) return null;

            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha && ii.hbmMask != 0 && ReadBits(ii.hbmMask, w, h) is { } mask)
                for (int i = 0; i < pixels.Length; i += 4) pixels[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;

            if (rejectIfSmallContent)
            {
                int maxX = 0, maxY = 0;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (pixels[(y * w + x) * 4 + 3] > 8) { maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                if (maxX < w / 4 && maxY < h / 4) return null;
            }

            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(), new PixelSize(w, h), new Vector(96, 96), w * 4);
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            if (ii.hbmColor != 0) Native.DeleteObject(ii.hbmColor);
            if (ii.hbmMask != 0) Native.DeleteObject(ii.hbmMask);
        }
    }

    private static byte[]? ReadBits(nint bitmap, int w, int h)
    {
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w, biHeight = -h, // negative = top-down rows
            biPlanes = 1, biBitCount = 32,
        };
        var buffer = new byte[w * h * 4];
        nint dc = Native.GetDC(0);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return Native.GetDIBits(dc, bitmap, 0, (uint)h, handle.AddrOfPinnedObject(), ref bmi, 0) == h ? buffer : null;
        }
        finally
        {
            handle.Free();
            Native.ReleaseDC(0, dc);
        }
    }
}
