using System.Runtime.InteropServices;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// DirectShow COM interop for video capture graph pipeline.
    /// Used for virtual cameras (OBS, ManyCam, Quest) that are DirectShow-only
    /// and cannot be opened through MediaFoundation.
    /// Reference: D:\users\tj\Projects\_Usefull Code\DSFiltersNETSrc_v6_1\Sources\BaseClasses\DirectShow.cs
    /// </summary>
    internal static class DSCapture
    {
        // CLSIDs for CoCreateInstance
        public static readonly Guid CLSID_FilterGraph =
            new("E436EBB3-524F-11CE-9F53-0020AF0BA770");

        public static readonly Guid CLSID_CaptureGraphBuilder2 =
            new("BF87B6E1-8C27-11D0-B3F0-00AA003761C5");

        public static readonly Guid CLSID_SampleGrabber =
            new("C1F400A0-3F08-11D3-9F0B-006008039E37");

        public static readonly Guid CLSID_NullRenderer =
            new("C1F400A4-3F08-11D3-9F0B-006008039E37");

        // Pin category GUIDs (for RenderStream)
        public static readonly Guid PIN_CATEGORY_CAPTURE =
            new("FB6C4281-0353-11D1-905F-0000C0CC16BA");

        // Media type GUIDs
        public static readonly Guid MEDIATYPE_Video =
            new("73646976-0000-0010-8000-00AA00389B71");

        public static readonly Guid MEDIASUBTYPE_RGB32 =
            new("E436EB7E-524F-11CE-9F53-0020AF0BA770");

        // Format type GUIDs
        public static readonly Guid FORMAT_VideoInfo =
            new("05589F80-C356-11CE-BF01-00AA0055595A");

        public static readonly Guid FORMAT_VideoInfo2 =
            new("F72A76A0-EB0A-11D0-ACE4-0000C0CC16BA");

        // IBaseFilter IID (for BindToObject)
        public static readonly Guid IID_IBaseFilter =
            new("56A86895-0AD4-11CE-B03A-0020AF0BA770");
    }

    /// <summary>
    /// AM_MEDIA_TYPE structure. Must be a class (not struct) for LPStruct marshaling.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal class DSAMMediaType
    {
        public Guid majorType;
        public Guid subType;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fixedSizeSamples;
        [MarshalAs(UnmanagedType.Bool)]
        public bool temporalCompression;
        public int sampleSize;
        public Guid formatType;
        public IntPtr unkPtr;
        public int formatSize;
        public IntPtr formatPtr;

        public void Free()
        {
            if (formatPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(formatPtr);
                formatPtr = IntPtr.Zero;
            }
            if (unkPtr != IntPtr.Zero)
            {
                Marshal.Release(unkPtr);
                unkPtr = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VIDEOINFOHEADER
    {
        // Source RECT (4 ints = 16 bytes)
        public int rcSourceLeft, rcSourceTop, rcSourceRight, rcSourceBottom;
        // Target RECT (4 ints = 16 bytes)
        public int rcTargetLeft, rcTargetTop, rcTargetRight, rcTargetBottom;
        public int dwBitRate;
        public int dwBitErrorRate;
        public long AvgTimePerFrame;
        public BITMAPINFOHEADER bmiHeader;
    }

    // IFilterGraph (base for IGraphBuilder)
    [ComImport, Guid("56A8689F-0AD4-11CE-B03A-0020AF0BA770"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFilterGraph
    {
        [PreserveSig] int AddFilter(
            [In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter,
            [In, MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int RemoveFilter([In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter);
        [PreserveSig] int EnumFilters(out IntPtr ppEnum);
        [PreserveSig] int FindFilterByName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pName,
            [MarshalAs(UnmanagedType.Interface)] out IBaseFilter ppFilter);
        [PreserveSig] int ConnectDirect(IntPtr ppinOut, IntPtr ppinIn,
            [In, MarshalAs(UnmanagedType.LPStruct)] DSAMMediaType? pmt);
        [PreserveSig] int Reconnect(IntPtr ppin);
        [PreserveSig] int Disconnect(IntPtr ppin);
        [PreserveSig] int SetDefaultSyncSource();
    }

    // IGraphBuilder (extends IFilterGraph)
    [ComImport, Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGraphBuilder : IFilterGraph
    {
        // IFilterGraph (re-declare with new)
        [PreserveSig] new int AddFilter(
            [In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter,
            [In, MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] new int RemoveFilter([In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter);
        [PreserveSig] new int EnumFilters(out IntPtr ppEnum);
        [PreserveSig] new int FindFilterByName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pName,
            [MarshalAs(UnmanagedType.Interface)] out IBaseFilter ppFilter);
        [PreserveSig] new int ConnectDirect(IntPtr ppinOut, IntPtr ppinIn,
            [In, MarshalAs(UnmanagedType.LPStruct)] DSAMMediaType? pmt);
        [PreserveSig] new int Reconnect(IntPtr ppin);
        [PreserveSig] new int Disconnect(IntPtr ppin);
        [PreserveSig] new int SetDefaultSyncSource();
        // IGraphBuilder specific
        [PreserveSig] int Connect(IntPtr ppinOut, IntPtr ppinIn);
        [PreserveSig] int Render(IntPtr ppinOut);
        [PreserveSig] int RenderFile(
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFile,
            [In, MarshalAs(UnmanagedType.LPWStr)] string? lpcwstrPlayList);
        [PreserveSig] int AddSourceFilter(
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFileName,
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFilterName,
            [MarshalAs(UnmanagedType.Interface)] out IBaseFilter ppFilter);
        [PreserveSig] int SetLogFile(IntPtr hFile);
        [PreserveSig] int Abort();
        [PreserveSig] int ShouldOperationContinue();
    }

    // IBaseFilter (minimal - we pass it around but don't call methods directly)
    [ComImport, Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IBaseFilter
    {
        // IPersist
        [PreserveSig] int GetClassID(out Guid pClassID);
        // IMediaFilter
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int filtState);
        [PreserveSig] int SetSyncSource(IntPtr pClock);
        [PreserveSig] int GetSyncSource(out IntPtr pClock);
        // IBaseFilter
        [PreserveSig] int EnumPins(out IntPtr ppEnum);
        [PreserveSig] int FindPin(
            [In, MarshalAs(UnmanagedType.LPWStr)] string Id, out IntPtr ppPin);
        [PreserveSig] int QueryFilterInfo(IntPtr pInfo);
        [PreserveSig] int JoinFilterGraph(IntPtr pGraph,
            [In, MarshalAs(UnmanagedType.LPWStr)] string? pName);
        [PreserveSig] int QueryVendorInfo(
            [MarshalAs(UnmanagedType.LPWStr)] out string pVendorInfo);
    }

    // ICaptureGraphBuilder2
    [ComImport, Guid("93E5A4E0-2D50-11D2-ABFA-00A0C9C6E38D"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICaptureGraphBuilder2
    {
        [PreserveSig] int SetFiltergraph([In] IGraphBuilder pfg);
        [PreserveSig] int GetFiltergraph(out IGraphBuilder ppfg);
        [PreserveSig] int SetOutputFileName(
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid pType,
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpstrFile,
            [MarshalAs(UnmanagedType.Interface)] out IBaseFilter ppbf,
            out IntPtr ppSink);
        [PreserveSig] int FindInterface(
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid pCategory,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid pType,
            [In] IBaseFilter pbf,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppint);
        [PreserveSig] int RenderStream(
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid PinCategory,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid MediaType,
            [In, MarshalAs(UnmanagedType.IUnknown)] object pSource,
            [In, MarshalAs(UnmanagedType.Interface)] IBaseFilter? pfCompressor,
            [In, MarshalAs(UnmanagedType.Interface)] IBaseFilter? pfRenderer);
        [PreserveSig] int ControlStream(
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid PinCategory,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid MediaType,
            [In, MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter,
            long pstart, long pstop, short wStartCookie, short wStopCookie);
        [PreserveSig] int AllocCapFile(
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpstrFile, long dwlSize);
        [PreserveSig] int CopyCaptureFile(
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpwstrOld,
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpwstrNew,
            [MarshalAs(UnmanagedType.Bool)] bool fAllowEscAbort, IntPtr pFilter);
        [PreserveSig] int FindPin(
            [In, MarshalAs(UnmanagedType.IUnknown)] object pSource,
            int pindir,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid PinCategory,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid MediaType,
            [MarshalAs(UnmanagedType.Bool)] bool fUnconnected,
            int ZeroBasedIndex, out IntPtr ppPin);
    }

    // ISampleGrabber
    [ComImport, Guid("6B652FFF-11FE-4FCE-92AD-0266B5D7C78F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISampleGrabber
    {
        [PreserveSig] int SetOneShot([MarshalAs(UnmanagedType.Bool)] bool oneShot);
        [PreserveSig] int SetMediaType([In, MarshalAs(UnmanagedType.LPStruct)] DSAMMediaType pmt);
        [PreserveSig] int GetConnectedMediaType([Out, MarshalAs(UnmanagedType.LPStruct)] DSAMMediaType pmt);
        [PreserveSig] int SetBufferSamples([MarshalAs(UnmanagedType.Bool)] bool bufferThem);
        [PreserveSig] int GetCurrentBuffer(ref int pBufferSize, IntPtr pBuffer);
        [PreserveSig] int GetCurrentSample(IntPtr ppSample);
        [PreserveSig] int SetCallback(ISampleGrabberCB? pCallback, int whichMethodToCallback);
    }

    // ISampleGrabberCB (implement this to receive frames)
    [ComImport, Guid("0579154A-2B53-4994-B0D0-E773148EFF85"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISampleGrabberCB
    {
        [PreserveSig] int SampleCB(double sampleTime, IntPtr pSample);
        [PreserveSig] int BufferCB(double sampleTime, IntPtr pBuffer, int bufferLen);
    }

    // IMediaControl (IDispatch-based - InterfaceIsDual, NOT InterfaceIsIUnknown)
    [ComImport, Guid("56A868B1-0AD4-11CE-B03A-0020AF0BA770"),
     InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IDShowMediaControl
    {
        [PreserveSig] int Run();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
        [PreserveSig] int GetState(int msTimeout, out int pfs);
        [PreserveSig] int RenderFile([MarshalAs(UnmanagedType.BStr)] string strFilename);
        [PreserveSig] int AddSourceFilter(
            [MarshalAs(UnmanagedType.BStr)] string strFilename,
            [MarshalAs(UnmanagedType.IDispatch)] out object ppUnk);
        [PreserveSig] int get_FilterCollection(
            [MarshalAs(UnmanagedType.IDispatch)] out object ppUnk);
        [PreserveSig] int get_RegFilterCollection(
            [MarshalAs(UnmanagedType.IDispatch)] out object ppUnk);
        [PreserveSig] int StopWhenReady();
    }
}
