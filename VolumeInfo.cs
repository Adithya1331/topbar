using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TopBar;

internal static partial class VolumeInfo
{
    private const int CLSCTX_ALL = 23;
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly StrategyBasedComWrappers s_comWrappers = new();
    private static IMMDeviceEnumerator? s_enumerator;
    private static IAudioEndpointVolume? s_endpoint;
    private static readonly VolumeCallback s_callback = new();
    private static nint s_hwnd;

    public static string Text { get; private set; } = "";
    public static bool IsMuted { get; private set; }

    public static void Start(nint hwnd)
    {
        if (s_endpoint is not null) return;

        s_hwnd = hwnd;
        try
        {
            Guid clsid = CLSID_MMDeviceEnumerator;
            Guid iidEnum = typeof(IMMDeviceEnumerator).GUID;
            Marshal.ThrowExceptionForHR(Native.CoCreateInstance(ref clsid, default, CLSCTX_ALL, ref iidEnum, out nint pEnum));
            s_enumerator = (IMMDeviceEnumerator)s_comWrappers.GetOrCreateObjectForComInstance(pEnum, CreateObjectFlags.UniqueInstance);
            _ = Marshal.Release(pEnum);

            Marshal.ThrowExceptionForHR(s_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice device));
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(in iid, CLSCTX_ALL, default, out nint pEndpoint));
            s_endpoint = (IAudioEndpointVolume)s_comWrappers.GetOrCreateObjectForComInstance(pEndpoint, CreateObjectFlags.UniqueInstance);
            _ = Marshal.Release(pEndpoint);

            Marshal.ThrowExceptionForHR(s_endpoint.RegisterControlChangeNotify(s_callback));
            Refresh();
        }
        catch
        {
            Stop();
        }
    }

    public static void Stop()
    {
        if (s_endpoint is not null)
        {
            try { _ = s_endpoint.UnregisterControlChangeNotify(s_callback); } catch { }
            ((object)s_endpoint as ComObject)?.FinalRelease();
            s_endpoint = null;
        }
        if (s_enumerator is not null)
        {
            ((object)s_enumerator as ComObject)?.FinalRelease();
            s_enumerator = null;
        }
        s_hwnd = default;
        Text = "";
        IsMuted = false;
    }

    public static void Refresh()
    {
        try
        {
            if (s_endpoint is null) return;
            Marshal.ThrowExceptionForHR(s_endpoint.GetMute(out bool muted));
            Marshal.ThrowExceptionForHR(s_endpoint.GetMasterVolumeLevelScalar(out float level));
            IsMuted = muted;
            Text = muted ? "Mute" : $"{Math.Clamp((int)Math.Round(level * 100), 0, 100)}%";
        }
        catch
        {
            Text = "";
            IsMuted = false;
        }
    }

    public static void ToggleMute()
    {
        try
        {
            if (s_endpoint is null) return;
            Marshal.ThrowExceptionForHR(s_endpoint.GetMute(out bool muted));
            Guid eventContext = Guid.Empty;
            Marshal.ThrowExceptionForHR(s_endpoint.SetMute(!muted, in eventContext));
            Refresh();
        }
        catch { }
    }

    public static void Adjust(int wheelDelta)
    {
        try
        {
            if (s_endpoint is null) return;
            int steps = Math.Max(1, Math.Abs(wheelDelta) / 120);
            Guid eventContext = Guid.Empty;
            for (int i = 0; i < steps; i++)
            {
                int hr = wheelDelta > 0 ? s_endpoint.VolumeStepUp(in eventContext) : s_endpoint.VolumeStepDown(in eventContext);
                Marshal.ThrowExceptionForHR(hr);
            }
            Refresh();
        }
        catch { }
    }

    [GeneratedComClass]
    private sealed partial class VolumeCallback : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint notifyData)
        {
            if (s_hwnd != default) _ = Native.PostMessageW(s_hwnd, Program.WM_APP_VOLUME, 0, 0);
            return 0;
        }
    }
}

internal enum EDataFlow
{
    eRender,
    eCapture,
    eAll,
}

internal enum ERole
{
    eConsole,
    eMultimedia,
    eCommunications,
}

[GeneratedComInterface, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out nint devices);
    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    [PreserveSig]
    int GetDevice(nint id, out IMMDevice device);
    [PreserveSig]
    int RegisterEndpointNotificationCallback(nint client);
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(nint client);
}

[GeneratedComInterface, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig]
    int Activate(in Guid iid, int clsCtx, nint activationParams, out nint interfacePointer);
    [PreserveSig]
    int OpenPropertyStore(int access, out nint properties);
    [PreserveSig]
    int GetId(out nint id);
    [PreserveSig]
    int GetState(out uint state);
}

[GeneratedComInterface, Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig]
    int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig]
    int GetChannelCount(out uint channelCount);
    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, in Guid eventContext);
    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, in Guid eventContext);
    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig]
    int SetChannelVolumeLevel(uint channel, float levelDb, in Guid eventContext);
    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channel, float level, in Guid eventContext);
    [PreserveSig]
    int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, in Guid eventContext);
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    [PreserveSig]
    int GetVolumeStepInfo(out uint step, out uint stepCount);
    [PreserveSig]
    int VolumeStepUp(in Guid eventContext);
    [PreserveSig]
    int VolumeStepDown(in Guid eventContext);
    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);
    [PreserveSig]
    int GetVolumeRange(out float volumeMinDb, out float volumeMaxDb, out float volumeIncrementDb);
}

[GeneratedComInterface, Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
internal partial interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(nint notifyData);
}
