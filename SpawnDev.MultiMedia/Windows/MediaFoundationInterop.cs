using System.Runtime.InteropServices;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// MediaFoundation COM interop definitions for camera capture.
    /// Zero external NuGet dependencies - pure P/Invoke.
    /// </summary>
    internal static class MF
    {
        // MF version constant
        public const uint MF_VERSION = 0x00020070;

        // Stream index constants
        public const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
        public const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD;

        // ReadSample stream flags
        public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x00000010;
        public const uint MF_SOURCE_READERF_STREAMTICK = 0x00000100;

        // Attribute GUIDs
        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE =
            new("C60AC5FE-252A-478F-A0EF-BC8FA5F7CAD3");

        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID =
            new("8AC3587A-4AE7-42D8-99E0-0A6013EEF90F");

        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID =
            new("14DD9A1C-7CFF-41BE-B1B9-BA1AC6ECB571");

        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME =
            new("60D0E559-52F8-4FA2-BBCE-ACDB34A8EC01");

        public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK =
            new("58F0AAD8-22BF-4F8A-BB3D-D2C4978C6E2F");

        // Media type attribute GUIDs
        public static readonly Guid MF_MT_MAJOR_TYPE =
            new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");

        public static readonly Guid MF_MT_SUBTYPE =
            new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");

        public static readonly Guid MF_MT_FRAME_SIZE =
            new("1652C33D-D6B2-4012-B834-72030849A37D");

        public static readonly Guid MF_MT_FRAME_RATE =
            new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");

        public static readonly Guid MF_MT_DEFAULT_STRIDE =
            new("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");

        // Media type major type GUIDs
        public static readonly Guid MFMediaType_Video =
            new("73646976-0000-0010-8000-00AA00389B71");

        public static readonly Guid MFMediaType_Audio =
            new("73647561-0000-0010-8000-00AA00389B71");

        // Video format GUIDs (FOURCC-based)
        public static readonly Guid MFVideoFormat_NV12 =
            new("3231564E-0000-0010-8000-00AA00389B71");

        public static readonly Guid MFVideoFormat_YUY2 =
            new("32595559-0000-0010-8000-00AA00389B71");

        public static readonly Guid MFVideoFormat_I420 =
            new("30323449-0000-0010-8000-00AA00389B71");

        public static readonly Guid MFVideoFormat_MJPG =
            new("47504A4D-0000-0010-8000-00AA00389B71");

        // RGB32 = BGRA in memory (D3DFMT_A8R8G8B8 = 21, but MF defines it as 22)
        public static readonly Guid MFVideoFormat_RGB32 =
            new("00000016-0000-0010-8000-00AA00389B71");

        public static readonly Guid MFVideoFormat_ARGB32 =
            new("00000015-0000-0010-8000-00AA00389B71");

        // P/Invoke - mfplat.dll
        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFStartup(uint version, uint dwFlags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateAttributes(
            [MarshalAs(UnmanagedType.Interface)] out IMFAttributes ppMFAttributes,
            uint cInitialSize);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMediaType(
            [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppMFType);

        // P/Invoke - mf.dll
        [DllImport("mf.dll", ExactSpelling = true)]
        public static extern int MFEnumDeviceSources(
            [MarshalAs(UnmanagedType.Interface)] IMFAttributes pAttributes,
            out IntPtr pppSourceActivate,
            out uint pcSourceActivate);

        // P/Invoke - mfreadwrite.dll
        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        public static extern int MFCreateSourceReaderFromMediaSource(
            [MarshalAs(UnmanagedType.Interface)] IMFMediaSource pMediaSource,
            [MarshalAs(UnmanagedType.Interface)] IMFAttributes? pAttributes,
            [MarshalAs(UnmanagedType.Interface)] out IMFSourceReader ppSourceReader);

        // Helper: HRESULT check
        public static void ThrowOnFailure(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        // Helper: unpack MF_MT_FRAME_SIZE (width in high 32, height in low 32)
        public static void UnpackFrameSize(long packed, out int width, out int height)
        {
            width = (int)(packed >> 32);
            height = (int)(packed & 0xFFFFFFFF);
        }

        // Helper: unpack MF_MT_FRAME_RATE (numerator in high 32, denominator in low 32)
        public static void UnpackFrameRate(long packed, out int numerator, out int denominator)
        {
            numerator = (int)(packed >> 32);
            denominator = (int)(packed & 0xFFFFFFFF);
        }

        // DirectShow MEDIASUBTYPE_RGB32 (different GUID from MF's MFVideoFormat_RGB32)
        public static readonly Guid MEDIASUBTYPE_RGB32_DS =
            new("E436EB7E-524F-11CE-9F53-0020AF0BA770");

        // Helper: map MF or DirectShow subtype GUID to our VideoPixelFormat
        public static VideoPixelFormat? SubtypeToPixelFormat(Guid subtype)
        {
            if (subtype == MFVideoFormat_NV12) return VideoPixelFormat.NV12;
            if (subtype == MFVideoFormat_YUY2) return VideoPixelFormat.YUY2;
            if (subtype == MFVideoFormat_I420) return VideoPixelFormat.I420;
            if (subtype == MFVideoFormat_RGB32) return VideoPixelFormat.BGRA;
            if (subtype == MFVideoFormat_ARGB32) return VideoPixelFormat.BGRA;
            if (subtype == MEDIASUBTYPE_RGB32_DS) return VideoPixelFormat.BGRA;
            return null;
        }
    }

    // COM Interface: IMFAttributes
    [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
    {
        // We only declare the methods we use. COM vtable slots must be in order.
        // Methods 0-2 (IUnknown) are implicit.
        [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType([In] ref Guid guidKey, out uint pType);
        [PreserveSig] int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, uint MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64([In] ref Guid guidKey, out long punValue);
        [PreserveSig] int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int SetItem([In] ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64([In] ref Guid guidKey, long unValue);
        [PreserveSig] int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint pcItems);
        [PreserveSig] int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
    }

    // COM Interface: IMFMediaType (extends IMFAttributes)
    [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType : IMFAttributes
    {
        // IMFAttributes methods are inherited (slots 0-30)
        // IMFMediaType-specific methods (slots 31+):
        [PreserveSig] new int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig] new int GetItemType([In] ref Guid guidKey, out uint pType);
        [PreserveSig] new int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] new int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, uint MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] new int GetUINT64([In] ref Guid guidKey, out long punValue);
        [PreserveSig] new int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] new int GetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] new int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig] new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] new int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] new int SetItem([In] ref Guid guidKey, IntPtr Value);
        [PreserveSig] new int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig] new int SetUINT64([In] ref Guid guidKey, long unValue);
        [PreserveSig] new int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig] new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] new int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] new int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] new int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out uint pcItems);
        [PreserveSig] new int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] new int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
        // IMFMediaType specific
        [PreserveSig] int GetMajorType(out Guid pguidMajorType);
        [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
        [PreserveSig] int IsEqual([MarshalAs(UnmanagedType.Interface)] IMFMediaType pIMediaType, out uint pdwFlags);
        [PreserveSig] int GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        [PreserveSig] int FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    // COM Interface: IMFActivate (extends IMFAttributes)
    [ComImport, Guid("7FEE9E9A-4A89-47A6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFActivate : IMFAttributes
    {
        // IMFAttributes inherited (re-declare with new)
        [PreserveSig] new int GetItem([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig] new int GetItemType([In] ref Guid guidKey, out uint pType);
        [PreserveSig] new int CompareItem([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] new int Compare([MarshalAs(UnmanagedType.Interface)] IMFAttributes pTheirs, uint MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] new int GetUINT32([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] new int GetUINT64([In] ref Guid guidKey, out long punValue);
        [PreserveSig] new int GetDouble([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] new int GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] new int GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] new int GetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] new int GetAllocatedString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out uint pcchLength);
        [PreserveSig] new int GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] new int GetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] new int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] new int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] new int SetItem([In] ref Guid guidKey, IntPtr Value);
        [PreserveSig] new int DeleteItem([In] ref Guid guidKey);
        [PreserveSig] new int DeleteAllItems();
        [PreserveSig] new int SetUINT32([In] ref Guid guidKey, uint unValue);
        [PreserveSig] new int SetUINT64([In] ref Guid guidKey, long unValue);
        [PreserveSig] new int SetDouble([In] ref Guid guidKey, double fValue);
        [PreserveSig] new int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] new int SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] new int SetBlob([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] new int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] new int LockStore();
        [PreserveSig] new int UnlockStore();
        [PreserveSig] new int GetCount(out uint pcItems);
        [PreserveSig] new int GetItemByIndex(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] new int CopyAllItems([MarshalAs(UnmanagedType.Interface)] IMFAttributes pDest);
        // IMFActivate specific
        [PreserveSig] int ActivateObject([In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    // COM Interface: IMFMediaSource
    [ComImport, Guid("279A808D-AEC7-40C8-9C6B-A6B492C78A66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaSource
    {
        // IMFMediaEventGenerator (4 methods) - we don't use these directly
        [PreserveSig] int placeholder_GetEvent();
        [PreserveSig] int placeholder_BeginGetEvent();
        [PreserveSig] int placeholder_EndGetEvent();
        [PreserveSig] int placeholder_QueueEvent();
        // IMFMediaSource specific
        [PreserveSig] int GetCharacteristics(out uint pdwCharacteristics);
        [PreserveSig] int CreatePresentationDescriptor([MarshalAs(UnmanagedType.Interface)] out object ppPresentationDescriptor);
        [PreserveSig] int Start(IntPtr pPresentationDescriptor, IntPtr pguidTimeFormat, IntPtr pvarStartPosition);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Shutdown();
    }

    // COM Interface: IMFSourceReader
    [ComImport, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] out bool pfSelected);
        [PreserveSig] int SetStreamSelection(uint dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
        [PreserveSig] int GetNativeMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppMediaType);
        [PreserveSig] int GetCurrentMediaType(uint dwStreamIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppMediaType);
        [PreserveSig] int SetCurrentMediaType(uint dwStreamIndex, IntPtr pdwReserved, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pMediaType);
        [PreserveSig] int SetCurrentPosition([In] ref Guid guidTimeFormat, IntPtr varPosition);
        [PreserveSig] int ReadSample(uint dwStreamIndex, uint dwControlFlags, out uint pdwActualStreamIndex, out uint pdwStreamFlags, out long pllTimestamp, [MarshalAs(UnmanagedType.Interface)] out IMFSample? ppSample);
        [PreserveSig] int Flush(uint dwStreamIndex);
        [PreserveSig] int GetServiceForStream(uint dwStreamIndex, [In] ref Guid guidService, [In] ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetPresentationAttribute(uint dwStreamIndex, [In] ref Guid guidAttribute, IntPtr pvarAttribute);
    }

    // COM Interface: IMFSample (extends IMFAttributes)
    [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample
    {
        // IMFAttributes (31 methods) - we skip the redeclarations for brevity
        // and only use ConvertToContiguousBuffer which is at a known vtable offset.
        // For IMFSample, the vtable is:
        // [0-2] IUnknown
        // [3-33] IMFAttributes (31 methods)
        // [34-47] IMFSample methods

        // We need to pad the vtable. Declare stubs for IMFAttributes slots.
        [PreserveSig] int GetItem_([In] ref Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType_([In] ref Guid guidKey, out uint pType);
        [PreserveSig] int CompareItem_([In] ref Guid guidKey, IntPtr Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int Compare_([MarshalAs(UnmanagedType.IUnknown)] object pTheirs, uint MatchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        [PreserveSig] int GetUINT32_([In] ref Guid guidKey, out uint punValue);
        [PreserveSig] int GetUINT64_([In] ref Guid guidKey, out long punValue);
        [PreserveSig] int GetDouble_([In] ref Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID_([In] ref Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength_([In] ref Guid guidKey, out uint pcchLength);
        [PreserveSig] int GetString_([In] ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        [PreserveSig] int GetAllocatedString_([In] ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        [PreserveSig] int GetBlobSize_([In] ref Guid guidKey, out uint pcbBlobSize);
        [PreserveSig] int GetBlob_([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob_([In] ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        [PreserveSig] int GetUnknown_([In] ref Guid guidKey, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        [PreserveSig] int SetItem_([In] ref Guid guidKey, IntPtr Value);
        [PreserveSig] int DeleteItem_([In] ref Guid guidKey);
        [PreserveSig] int DeleteAllItems_();
        [PreserveSig] int SetUINT32_([In] ref Guid guidKey, uint unValue);
        [PreserveSig] int SetUINT64_([In] ref Guid guidKey, long unValue);
        [PreserveSig] int SetDouble_([In] ref Guid guidKey, double fValue);
        [PreserveSig] int SetGUID_([In] ref Guid guidKey, [In] ref Guid guidValue);
        [PreserveSig] int SetString_([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob_([In] ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        [PreserveSig] int SetUnknown_([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        [PreserveSig] int LockStore_();
        [PreserveSig] int UnlockStore_();
        [PreserveSig] int GetCount_(out uint pcItems);
        [PreserveSig] int GetItemByIndex_(uint unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems_([MarshalAs(UnmanagedType.IUnknown)] object pDest);
        // IMFSample-specific methods (after 31 IMFAttributes methods)
        [PreserveSig] int GetSampleFlags(out uint pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(uint dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);
        [PreserveSig] int SetSampleTime(long hnsSampleTime);
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig] int GetBufferCount(out uint pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(uint dwIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer([MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);
        [PreserveSig] int AddBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);
        [PreserveSig] int RemoveBufferByIndex(uint dwIndex);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint pcbTotalLength);
        [PreserveSig] int CopyToBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);
    }

    // COM Interface: IMFMediaBuffer
    [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(int cbCurrentLength);
        [PreserveSig] int GetMaxLength(out int pcbMaxLength);
    }
}
