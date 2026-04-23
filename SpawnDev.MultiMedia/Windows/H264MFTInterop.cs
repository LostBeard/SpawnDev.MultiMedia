using System.Runtime.InteropServices;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Media Foundation H.264 Encoder MFT (Microsoft's stock encoder, CLSID_MSH264EncoderMFT)
    /// P/Invoke + COM interop declarations. Zero external NuGet dependencies - pure Windows
    /// SDK constants + interface-pointer marshaling. The encoder itself is OS-provided: we
    /// just plumb NV12 frames in and NAL units out.
    ///
    /// Spec references:
    /// - H.264 MFT: https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder
    /// - IMFTransform: https://learn.microsoft.com/en-us/windows/win32/api/mftransform/nn-mftransform-imftransform
    /// - ICodecAPI: https://learn.microsoft.com/en-us/windows/win32/api/strmif/nn-strmif-icodecapi
    /// </summary>
    internal static class H264MFT
    {
        // COM class ID for Microsoft's H.264 Encoder MFT (software or hardware, OS selects).
        public static readonly Guid CLSID_MSH264EncoderMFT =
            new("6CA50344-051A-4DED-9779-A43305165E35");

        // Output format: H.264 ("H264" FOURCC in MF's format GUID scheme).
        public static readonly Guid MFVideoFormat_H264 =
            new("34363248-0000-0010-8000-00AA00389B71");

        // Media-type attribute GUIDs specific to video encoding.
        public static readonly Guid MF_MT_AVG_BITRATE =
            new("20332624-FB0D-4D9E-BD0D-CBF6786C102E");

        public static readonly Guid MF_MT_INTERLACE_MODE =
            new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");

        public static readonly Guid MF_MT_MPEG2_PROFILE =
            new("AD76A80B-2D5C-4E0B-B375-64E520137036");

        public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO =
            new("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");

        // ICodecAPI property keys. See codecapi.h in the Windows SDK.
        public static readonly Guid CODECAPI_AVLowLatencyMode =
            new("9C27891A-ED7A-40E1-88E8-B22727A024EE");

        public static readonly Guid CODECAPI_AVEncCommonRateControlMode =
            new("1C0608E9-370C-4710-8A58-CB6181C42423");

        public static readonly Guid CODECAPI_AVEncCommonMeanBitRate =
            new("F7222374-2144-4815-B550-A37F8E12EE52");

        // MPEG-2 profile (also used for H.264 via MF_MT_MPEG2_PROFILE).
        public const uint eAVEncH264VProfile_Base = 66;  // Baseline - widest browser interop
        public const uint eAVEncH264VProfile_Main = 77;
        public const uint eAVEncH264VProfile_High = 100;

        // Interlace modes.
        public const uint MFVideoInterlace_Progressive = 2;

        // Rate control modes.
        public const uint eAVEncCommonRateControlMode_CBR = 0;
        public const uint eAVEncCommonRateControlMode_Quality = 2;

        // IMFTransform.ProcessOutput flags.
        public const uint MFT_PROCESS_OUTPUT_DISCARD_WHEN_NO_BUFFER = 1;

        // IMFTransform.ProcessOutput status codes (in MFT_OUTPUT_DATA_BUFFER.dwStatus).
        public const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
        public const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);

        // IMFTransform message types (ProcessMessage).
        public const uint MFT_MESSAGE_NOTIFY_START_OF_STREAM = 0x10000002;
        public const uint MFT_MESSAGE_NOTIFY_END_OF_STREAM = 0x10000003;
        public const uint MFT_MESSAGE_NOTIFY_BEGIN_STREAMING = 0x10000000;
        public const uint MFT_MESSAGE_NOTIFY_END_STREAMING = 0x10000001;
        public const uint MFT_MESSAGE_COMMAND_DRAIN = 0x00000001;
        public const uint MFT_MESSAGE_COMMAND_FLUSH = 0x00000000;

        // PROPVARIANT helper constants for ICodecAPI.SetValue.
        public const ushort VT_BOOL = 11;
        public const ushort VT_UI4 = 19;
        public const ushort VT_UI8 = 21;

        // Packed PROPVARIANT for SetValue - we only need VT_BOOL and VT_UI4.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public int iVal;       // VT_BOOL: VARIANT_TRUE=-1 / VARIANT_FALSE=0
            [FieldOffset(8)] public uint ulVal;     // VT_UI4
            [FieldOffset(8)] public ulong uhVal;    // VT_UI8

            public static PROPVARIANT FromBool(bool value)
                => new PROPVARIANT { vt = VT_BOOL, iVal = value ? -1 : 0 };

            public static PROPVARIANT FromUInt32(uint value)
                => new PROPVARIANT { vt = VT_UI4, ulVal = value };

            public static PROPVARIANT FromUInt64(ulong value)
                => new PROPVARIANT { vt = VT_UI8, uhVal = value };
        }

        // MFT output buffer struct passed to ProcessOutput.
        [StructLayout(LayoutKind.Sequential)]
        public struct MFT_OUTPUT_DATA_BUFFER
        {
            public uint dwStreamID;
            public IntPtr pSample;          // IMFSample* (we marshal manually)
            public uint dwStatus;
            public IntPtr pEvents;          // IMFCollection*, we don't use
        }

        // Ole32.dll - COM activation.
        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern int CoCreateInstance(
            [In] ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        public const uint CLSCTX_INPROC_SERVER = 0x1;

        // mfplat.dll - sample + buffer creation + copying.
        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateSample(
            [MarshalAs(UnmanagedType.Interface)] out IMFSample ppIMFSample);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMemoryBuffer(
            uint cbMaxLength,
            [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);

        // Helper: pack width/height into a single UINT64 for MF_MT_FRAME_SIZE etc.
        public static long PackLong(uint high, uint low) => ((long)high << 32) | low;
    }

    // COM Interface: IMFTransform - the MFT itself (encoder/decoder/processor).
    // Guid from mftransform.h. Only the methods we use are declared fully; others are
    // padded as placeholders to keep vtable slots correct.
    [ComImport, Guid("BF94C121-5B05-4E6F-8000-BA598961414D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFTransform
    {
        // Slot 3-8: stream enumeration + counts. We don't need to configure stream counts
        // (encoder is 1-in / 1-out which is the default).
        [PreserveSig] int GetStreamLimits(out uint pdwInputMinimum, out uint pdwInputMaximum, out uint pdwOutputMinimum, out uint pdwOutputMaximum);
        [PreserveSig] int GetStreamCount(out uint pcInputStreams, out uint pcOutputStreams);
        [PreserveSig] int GetStreamIDs(uint dwInputIDArraySize, [Out] uint[] pdwInputIDs, uint dwOutputIDArraySize, [Out] uint[] pdwOutputIDs);
        [PreserveSig] int GetInputStreamInfo(uint dwInputStreamID, out MFT_INPUT_STREAM_INFO pStreamInfo);
        [PreserveSig] int GetOutputStreamInfo(uint dwOutputStreamID, out MFT_OUTPUT_STREAM_INFO pStreamInfo);
        [PreserveSig] int GetAttributes([MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);

        // Slot 9-10: per-stream attributes (not used).
        [PreserveSig] int GetInputStreamAttributes(uint dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);
        [PreserveSig] int GetOutputStreamAttributes(uint dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);

        // Slot 11-12: stream add/remove (not used).
        [PreserveSig] int DeleteInputStream(uint dwStreamID);
        [PreserveSig] int AddInputStreams(uint cStreams, [In] uint[] adwStreamIDs);

        // Slot 13-14: enumerate available types.
        [PreserveSig] int GetInputAvailableType(uint dwInputStreamID, uint dwTypeIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
        [PreserveSig] int GetOutputAvailableType(uint dwOutputStreamID, uint dwTypeIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);

        // Slot 15-16: set the active input/output media type.
        [PreserveSig] int SetInputType(uint dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pType, uint dwFlags);
        [PreserveSig] int SetOutputType(uint dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pType, uint dwFlags);

        // Slot 17-18: get the active type.
        [PreserveSig] int GetInputCurrentType(uint dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
        [PreserveSig] int GetOutputCurrentType(uint dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);

        // Slot 19-20: input/output status.
        [PreserveSig] int GetInputStatus(uint dwInputStreamID, out uint pdwFlags);
        [PreserveSig] int GetOutputStatus(out uint pdwFlags);

        // Slot 21: advance stream time range (not used for encoder).
        [PreserveSig] int SetOutputBounds(long hnsLowerBound, long hnsUpperBound);

        // Slot 22: process event (not used).
        [PreserveSig] int ProcessEvent(uint dwInputStreamID, IntPtr pEvent);

        // Slot 23: control messages (start/end of stream, flush, drain).
        [PreserveSig] int ProcessMessage(uint eMessage, UIntPtr ulParam);

        // Slot 24-25: the actual encode path.
        [PreserveSig] int ProcessInput(uint dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFSample pSample, uint dwFlags);
        [PreserveSig] int ProcessOutput(uint dwFlags, uint cOutputBufferCount,
            [In, Out, MarshalAs(UnmanagedType.LPArray)] H264MFT.MFT_OUTPUT_DATA_BUFFER[] pOutputSamples, out uint pdwStatus);
    }

    // COM Interface: ICodecAPI - runtime codec property configuration (low-latency mode,
    // bitrate, rate-control mode, etc.). Same instance as the IMFTransform, obtained via
    // QueryInterface.
    [ComImport, Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICodecAPI
    {
        [PreserveSig] int IsSupported([In] ref Guid Api);
        [PreserveSig] int IsModifiable([In] ref Guid Api);
        [PreserveSig] int GetParameterRange([In] ref Guid Api, IntPtr ValueMin, IntPtr ValueMax, IntPtr SteppingDelta);
        [PreserveSig] int GetParameterValues([In] ref Guid Api, IntPtr Values, out uint ValuesCount);
        [PreserveSig] int GetDefaultValue([In] ref Guid Api, IntPtr Value);
        [PreserveSig] int GetValue([In] ref Guid Api, IntPtr Value);
        [PreserveSig] int SetValue([In] ref Guid Api, [In] ref H264MFT.PROPVARIANT Value);
        [PreserveSig] int RegisterForEvent([In] ref Guid Api, IntPtr userData);
        [PreserveSig] int UnregisterForEvent([In] ref Guid Api);
        [PreserveSig] int SetAllDefaults();
        [PreserveSig] int SetValueWithNotify([In] ref Guid Api, IntPtr Value, out IntPtr ChangedParam, out uint ChangedParamCount);
        [PreserveSig] int SetAllDefaultsWithNotify(IntPtr ChangedParam, out uint ChangedParamCount);
        [PreserveSig] int GetAllSettings(IntPtr store);
        [PreserveSig] int SetAllSettings(IntPtr store);
        [PreserveSig] int SetAllSettingsWithNotify(IntPtr store, out IntPtr ChangedParam, out uint ChangedParamCount);
    }

    // MFT_INPUT_STREAM_INFO - tells us buffer-alignment + sizing requirements for input.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_INPUT_STREAM_INFO
    {
        public long hnsMaxLatency;
        public uint dwFlags;
        public uint cbSize;
        public uint cbMaxLookahead;
        public uint cbAlignment;
    }

    // MFT_OUTPUT_STREAM_INFO - same for output. The encoder may or may not allocate output
    // samples; we check dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MFT_OUTPUT_STREAM_INFO
    {
        public uint dwFlags;
        public uint cbSize;
        public uint cbAlignment;
    }
}
