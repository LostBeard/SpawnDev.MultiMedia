using System.Runtime.InteropServices;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// DXGI + D3D11 COM interop for Desktop Duplication screen capture.
    /// Zero external NuGet dependencies - pure P/Invoke.
    /// </summary>
    internal static class DXGI
    {
        [DllImport("d3d11.dll", PreserveSig = true)]
        public static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int DriverType,
            IntPtr Software,
            uint Flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            out IntPtr ppDevice,
            out int pFeatureLevel,
            out IntPtr ppImmediateContext);

        // D3D constants
        public const int D3D_DRIVER_TYPE_HARDWARE = 1;
        public const uint D3D11_SDK_VERSION = 7;
        public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;

        // D3D11_USAGE
        public const uint D3D11_USAGE_STAGING = 3;

        // D3D11_CPU_ACCESS
        public const uint D3D11_CPU_ACCESS_READ = 0x20000;

        // D3D11_MAP
        public const uint D3D11_MAP_READ = 1;

        // DXGI_FORMAT
        public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

        // IIDs
        public static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        public static readonly Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
        public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_OUTDUPL_DESC
    {
        public DXGI_MODE_DESC ModeDesc;
        public int Rotation;
        public int DesktopImageInSystemMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_MODE_DESC
    {
        public uint Width;
        public uint Height;
        public DXGI_RATIONAL RefreshRate;
        public uint Format;
        public uint ScanlineOrdering;
        public uint Scaling;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public DXGI_OUTDUPL_POINTER_SHAPE_INFO PointerShapeInfo;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_OUTDUPL_POINTER_SHAPE_INFO
    {
        public uint Type;
        public uint Width;
        public uint Height;
        public uint Pitch;
        public POINT HotSpot;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    // COM interfaces - minimal vtable wrappers for Desktop Duplication

    [ComImport, Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIDevice
    {
        // IDXGIObject
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);

        // IDXGIDevice
        int GetAdapter([MarshalAs(UnmanagedType.IUnknown)] out object pAdapter);
    }

    [ComImport, Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIAdapter
    {
        // IDXGIObject
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);

        // IDXGIAdapter
        int EnumOutputs(uint Output, [MarshalAs(UnmanagedType.IUnknown)] out object ppOutput);
    }

    [ComImport, Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIOutput
    {
        // IDXGIObject
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);

        // IDXGIOutput
        int GetDesc(out DXGI_OUTPUT_DESC pDesc);
    }

    [ComImport, Guid("00cddea8-939b-4b83-a340-a685226666cc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIOutput1
    {
        // IDXGIObject
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppParent);

        // IDXGIOutput
        int GetDesc(out DXGI_OUTPUT_DESC pDesc);
        int GetDisplayModeList(uint EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
        int FindClosestMatchingMode(IntPtr pModeToMatch, IntPtr pClosestMatch, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice);
        int WaitForVBlank();
        int TakeOwnership([MarshalAs(UnmanagedType.IUnknown)] object pDevice, int Exclusive);
        void ReleaseOwnership();
        int GetGammaControlCapabilities(IntPtr pGammaCaps);
        int SetGammaControl(IntPtr pArray);
        int GetGammaControl(IntPtr pArray);
        int SetDisplaySurface([MarshalAs(UnmanagedType.IUnknown)] object pScanoutSurface);
        int GetDisplaySurfaceData([MarshalAs(UnmanagedType.IUnknown)] object pDestination);
        int GetFrameStatistics(IntPtr pStats);

        // IDXGIOutput1
        int GetDisplayModeList1(uint EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
        int FindClosestMatchingMode1(IntPtr pModeToMatch, IntPtr pClosestMatch, [MarshalAs(UnmanagedType.IUnknown)] object pConcernedDevice);
        int GetDisplaySurfaceData1([MarshalAs(UnmanagedType.IUnknown)] object pDestination);
        int DuplicateOutput(
            [MarshalAs(UnmanagedType.IUnknown)] object pDevice,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppOutputDuplication);
    }

    [ComImport, Guid("191cfac3-a341-470d-b26e-a864f428319c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIOutputDuplication
    {
        void GetDesc(out DXGI_OUTDUPL_DESC pDesc);

        int AcquireNextFrame(
            uint TimeoutInMilliseconds,
            out DXGI_OUTDUPL_FRAME_INFO pFrameInfo,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppDesktopResource);

        int GetFrameDirtyRects(uint DirtyRectsBufferSize, IntPtr pDirtyRectsBuffer, out uint pDirtyRectsBufferSizeRequired);
        int GetFrameMoveRects(uint MoveRectsBufferSize, IntPtr pMoveRectBuffer, out uint pMoveRectsBufferSizeRequired);
        int GetFramePointerShape(uint PointerShapeBufferSize, IntPtr pPointerShapeBuffer, out uint pPointerShapeBufferSizeRequired, IntPtr pPointerShapeInfo);
        int MapDesktopSurface(out D3D11_MAPPED_SUBRESOURCE pLockedRect);
        int UnMapDesktopSurface();

        int ReleaseFrame();
    }

    /// <summary>
    /// ID3D11Device - minimal surface needed for Desktop Duplication.
    /// We use raw vtable calls for CreateTexture2D since the COM interop
    /// for D3D11 structs is fragile via managed interfaces.
    /// </summary>
    internal static class D3D11
    {
        /// <summary>
        /// Create a staging texture for CPU readback of desktop frames.
        /// Uses raw vtable call to avoid COM interop struct marshaling issues.
        /// ID3D11Device::CreateTexture2D is vtable slot 5 (after IUnknown[3] + ID3D11Device starts at slot 3).
        /// Actually: IUnknown has 3, then ID3D11Device methods start. CreateBuffer=slot3, CreateTexture1D=slot4, CreateTexture2D=slot5.
        /// </summary>
        public static unsafe int CreateStagingTexture2D(IntPtr device, uint width, uint height, uint format, out IntPtr ppTexture)
        {
            ppTexture = IntPtr.Zero;
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = DXGI.D3D11_USAGE_STAGING,
                BindFlags = 0,
                CPUAccessFlags = DXGI.D3D11_CPU_ACCESS_READ,
                MiscFlags = 0,
            };

            var vtable = Marshal.ReadIntPtr(device);
            // CreateTexture2D is at vtable index 5: [0]QI [1]AddRef [2]Release [3]CreateBuffer [4]CreateTexture1D [5]CreateTexture2D
            var createTexture2D = Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size);

            fixed (IntPtr* ppTex = &ppTexture)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)createTexture2D)(
                    device, &desc, IntPtr.Zero, ppTex);
            }
        }

        /// <summary>
        /// ID3D11DeviceContext::CopyResource - copies entire texture.
        /// Vtable: IUnknown[3] + ID3D11DeviceContext methods. CopyResource is slot 47.
        /// </summary>
        public static unsafe void CopyResource(IntPtr context, IntPtr dst, IntPtr src)
        {
            var vtable = Marshal.ReadIntPtr(context);
            var copyResource = Marshal.ReadIntPtr(vtable, 47 * IntPtr.Size);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)copyResource)(context, dst, src);
        }

        /// <summary>
        /// ID3D11DeviceContext::Map - maps a subresource for CPU read.
        /// Vtable slot 14.
        /// </summary>
        public static unsafe int Map(IntPtr context, IntPtr resource, uint subresource, uint mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE mapped)
        {
            mapped = default;
            var vtable = Marshal.ReadIntPtr(context);
            var mapFn = Marshal.ReadIntPtr(vtable, 14 * IntPtr.Size);
            fixed (D3D11_MAPPED_SUBRESOURCE* pMapped = &mapped)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)mapFn)(
                    context, resource, subresource, mapType, mapFlags, pMapped);
            }
        }

        /// <summary>
        /// ID3D11DeviceContext::Unmap - unmaps a subresource.
        /// Vtable slot 15.
        /// </summary>
        public static unsafe void Unmap(IntPtr context, IntPtr resource, uint subresource)
        {
            var vtable = Marshal.ReadIntPtr(context);
            var unmapFn = Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)unmapFn)(context, resource, subresource);
        }
    }
}
