using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Orchard.Platform.Windows;

/// <summary>Master volume of the default playback device via Core Audio, for the Control Center slider.</summary>
[SupportedOSPlatform("windows")]
internal static class AudioVolume
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }

    private static IAudioEndpointVolume? Endpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return device.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out var o) == 0 ? (IAudioEndpointVolume)o : null;
        }
        catch (COMException)
        {
            return null; // no audio device
        }
    }

    public static double? Get() => Endpoint() is { } ep && ep.GetMasterVolumeLevelScalar(out float v) == 0 ? v : null;

    public static void Set(double level)
    {
        if (Endpoint() is not { } ep) return;
        var ctx = Guid.Empty;
        ep.SetMasterVolumeLevelScalar((float)Math.Clamp(level, 0, 1), ref ctx);
    }
}
