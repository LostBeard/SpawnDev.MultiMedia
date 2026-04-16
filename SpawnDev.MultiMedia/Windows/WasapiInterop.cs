using System.Runtime.InteropServices;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// WASAPI COM interop definitions for audio capture and playback.
    /// Zero external NuGet dependencies - pure P/Invoke.
    /// </summary>
    internal static class WASAPI
    {
        public static readonly Guid CLSID_MMDeviceEnumerator =
            new("BCDE0395-E52F-467C-8E3D-C4579291692E");

        public const uint CLSCTX_ALL = 0x17;
        public const uint STGM_READ = 0x00000000;
        public const uint DEVICE_STATE_ACTIVE = 0x00000001;

        // Stream flags
        public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;

        // Buffer flags
        public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

        // Property keys
        public static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
        {
            fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
            pid = 14,
        };

        // Audio subformat GUIDs
        public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT =
            new("00000003-0000-0010-8000-00AA00389B71");

        public static readonly Guid KSDATAFORMAT_SUBTYPE_PCM =
            new("00000001-0000-0010-8000-00AA00389B71");

        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern int CoCreateInstance(
            [In] ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    internal enum EDataFlow : uint
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2,
    }

    internal enum ERole : uint
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2,
    }

    internal enum AUDCLNT_SHAREMODE : uint
    {
        Shared = 0,
        Exclusive = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pointerValue;
        public IntPtr padding;

        public string? GetString()
        {
            if (vt == 31 && pointerValue != IntPtr.Zero) // VT_LPWSTR
                return Marshal.PtrToStringUni(pointerValue);
            return null;
        }

        public void Clear()
        {
            PropVariantClear(ref this);
        }

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    // COM Interface: IMMDeviceEnumerator
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(
            EDataFlow dataFlow,
            uint dwStateMask,
            [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection ppDevices);

        [PreserveSig] int GetDefaultAudioEndpoint(
            EDataFlow dataFlow,
            ERole role,
            [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppEndpoint);

        [PreserveSig] int GetDevice(
            [MarshalAs(UnmanagedType.LPWStr)] string pwstrId,
            [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);

        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    // COM Interface: IMMDeviceCollection
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);
    }

    // COM Interface: IMMDevice
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(
            [In] ref Guid iid,
            uint dwClsCtx,
            IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig] int OpenPropertyStore(
            uint stgmAccess,
            [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ppProperties);

        [PreserveSig] int GetId(
            [MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

        [PreserveSig] int GetState(out uint pdwState);
    }

    // COM Interface: IPropertyStore
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue([In] ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue([In] ref PROPERTYKEY key, [In] ref PROPVARIANT propvar);
        [PreserveSig] int Commit();
    }

    // COM Interface: IAudioClient
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(
            AUDCLNT_SHAREMODE ShareMode,
            uint StreamFlags,
            long hnsBufferDuration,
            long hnsPeriodicity,
            IntPtr pFormat,
            IntPtr AudioSessionGuid);

        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetStreamLatency(out long phnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);

        [PreserveSig] int IsFormatSupported(
            AUDCLNT_SHAREMODE ShareMode,
            IntPtr pFormat,
            out IntPtr ppClosestMatch);

        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);

        [PreserveSig] int GetDevicePeriod(
            out long phnsDefaultDevicePeriod,
            out long phnsMinimumDevicePeriod);

        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);

        [PreserveSig] int GetService(
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    // COM Interface: IAudioCaptureClient
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(
            out IntPtr ppData,
            out uint pNumFramesToRead,
            out uint pdwFlags,
            out ulong pu64DevicePosition,
            out ulong pu64QPCPosition);

        [PreserveSig] int ReleaseBuffer(uint NumFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }

    // COM Interface: IAudioRenderClient (for audio playback)
    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint NumFramesRequested, out IntPtr ppData);
        [PreserveSig] int ReleaseBuffer(uint NumFramesWritten, uint dwFlags);
    }
}
