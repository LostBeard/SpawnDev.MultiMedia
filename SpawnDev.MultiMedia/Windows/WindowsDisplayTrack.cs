using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Windows screen capture track using DXGI Desktop Duplication.
    /// Captures the primary display as BGRA frames at the monitor's refresh rate.
    /// Available on Windows 8+ (DXGI 1.2).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsDisplayTrack : IVideoTrack
    {
        private IntPtr _d3dDevice;
        private IntPtr _d3dContext;
        private IDXGIOutputDuplication? _duplication;
        private IntPtr _stagingTexture;

        private Thread? _captureThread;
        private volatile bool _capturing;
        private bool _disposed;
        private bool _enabled = true;
        private string _readyState = "live";
        private string _contentHint = "detail";

        public string Id { get; }
        public string Kind => "video";
        public string Label { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public double FrameRate { get; private set; }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public bool Muted => false;
        public string ReadyState => _readyState;

        public string ContentHint
        {
            get => _contentHint;
            set => _contentHint = value;
        }

        public event Action? OnEnded;
        public event Action? OnMute;
        public event Action? OnUnmute;
        public event Action<VideoFrame>? OnFrame;

        private WindowsDisplayTrack(string label)
        {
            Id = Guid.NewGuid().ToString();
            Label = label;
        }

        /// <summary>
        /// Creates a display capture track for the primary monitor.
        /// Sets up DXGI Desktop Duplication and starts the capture thread.
        /// </summary>
        internal static WindowsDisplayTrack Create(MediaTrackConstraints? constraints)
        {
            var track = new WindowsDisplayTrack("Screen Capture");

            // Create D3D11 device
            int hr = DXGI.D3D11CreateDevice(
                IntPtr.Zero,
                DXGI.D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                DXGI.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero,
                0,
                DXGI.D3D11_SDK_VERSION,
                out track._d3dDevice,
                out _,
                out track._d3dContext);
            MF.ThrowOnFailure(hr);

            try
            {
                // Navigate: D3D11 Device -> DXGI Device -> DXGI Adapter -> DXGI Output -> DXGI Output1
                var dxgiDeviceIid = DXGI.IID_IDXGIDevice;
                hr = Marshal.QueryInterface(track._d3dDevice, ref dxgiDeviceIid, out var dxgiDevicePtr);
                MF.ThrowOnFailure(hr);

                var dxgiDevice = (IDXGIDevice)Marshal.GetObjectForIUnknown(dxgiDevicePtr);
                Marshal.Release(dxgiDevicePtr);

                try
                {
                    hr = dxgiDevice.GetAdapter(out var adapterObj);
                    MF.ThrowOnFailure(hr);

                    var adapter = (IDXGIAdapter)adapterObj;
                    try
                    {
                        // Get the first output (primary monitor)
                        hr = adapter.EnumOutputs(0, out var outputObj);
                        MF.ThrowOnFailure(hr);

                        var output = (IDXGIOutput)outputObj;
                        try
                        {
                            // Get output description for dimensions
                            output.GetDesc(out var outputDesc);
                            track.Width = outputDesc.DesktopCoordinates.Width;
                            track.Height = outputDesc.DesktopCoordinates.Height;
                            track.Label = $"Screen: {outputDesc.DeviceName.TrimEnd('\0')}";

                            // QI to IDXGIOutput1 for DuplicateOutput
                            var output1Iid = DXGI.IID_IDXGIOutput1;
                            var outputPtr = Marshal.GetIUnknownForObject(outputObj);
                            try
                            {
                                hr = Marshal.QueryInterface(outputPtr, ref output1Iid, out var output1Ptr);
                                MF.ThrowOnFailure(hr);

                                var output1 = (IDXGIOutput1)Marshal.GetObjectForIUnknown(output1Ptr);
                                Marshal.Release(output1Ptr);

                                try
                                {
                                    // DuplicateOutput needs the D3D11 device as IUnknown.
                                    // Use raw vtable call to avoid managed COM interop issues
                                    // with IntPtr-based D3D11 devices.
                                    hr = DuplicateOutputRaw(output1Ptr, track._d3dDevice, out var duplPtr);
                                    MF.ThrowOnFailure(hr);

                                    track._duplication = (IDXGIOutputDuplication)Marshal.GetObjectForIUnknown(duplPtr);
                                    Marshal.Release(duplPtr);

                                    // Read actual duplication desc
                                    track._duplication.GetDesc(out var duplDesc);
                                    track.Width = (int)duplDesc.ModeDesc.Width;
                                    track.Height = (int)duplDesc.ModeDesc.Height;
                                    if (duplDesc.ModeDesc.RefreshRate.Denominator > 0)
                                        track.FrameRate = (double)duplDesc.ModeDesc.RefreshRate.Numerator / duplDesc.ModeDesc.RefreshRate.Denominator;
                                    else
                                        track.FrameRate = 60;
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(output1);
                                }
                            }
                            finally
                            {
                                Marshal.Release(outputPtr);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(output);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(dxgiDevice);
                }
            }
            catch
            {
                // Cleanup on failure
                if (track._d3dContext != IntPtr.Zero) Marshal.Release(track._d3dContext);
                if (track._d3dDevice != IntPtr.Zero) Marshal.Release(track._d3dDevice);
                throw;
            }

            // Create staging texture for CPU readback
            hr = D3D11.CreateStagingTexture2D(
                track._d3dDevice,
                (uint)track.Width,
                (uint)track.Height,
                DXGI.DXGI_FORMAT_B8G8R8A8_UNORM,
                out track._stagingTexture);
            MF.ThrowOnFailure(hr);

            // Start capture thread
            track._capturing = true;
            track._captureThread = new Thread(track.CaptureLoop)
            {
                IsBackground = true,
                Name = "DXGI_ScreenCapture",
            };
            track._captureThread.Start();

            return track;
        }

        private void CaptureLoop()
        {
            try
            {
                while (_capturing && _duplication != null)
                {
                    // Acquire next frame with 100ms timeout
                    var hr = _duplication.AcquireNextFrame(100, out var frameInfo, out var resourceObj);

                    if (hr == unchecked((int)0x887A0027)) // DXGI_ERROR_WAIT_TIMEOUT
                        continue;

                    if (hr < 0)
                    {
                        // Access lost (display mode change, etc.) - could try to recreate
                        break;
                    }

                    try
                    {
                        // Only process if there's a new frame and we have listeners
                        if (frameInfo.LastPresentTime > 0 && _enabled && OnFrame != null)
                        {
                            // QI to ID3D11Texture2D
                            var texIid = DXGI.IID_ID3D11Texture2D;
                            var resourcePtr = Marshal.GetIUnknownForObject(resourceObj);
                            try
                            {
                                hr = Marshal.QueryInterface(resourcePtr, ref texIid, out var texturePtr);
                                if (hr >= 0)
                                {
                                    try
                                    {
                                        // Copy GPU texture -> staging texture
                                        D3D11.CopyResource(_d3dContext, _stagingTexture, texturePtr);

                                        // Map staging texture for CPU read
                                        hr = D3D11.Map(_d3dContext, _stagingTexture, 0, DXGI.D3D11_MAP_READ, 0, out var mapped);
                                        if (hr >= 0)
                                        {
                                            try
                                            {
                                                var data = new byte[Width * Height * 4];
                                                if (mapped.RowPitch == (uint)(Width * 4))
                                                {
                                                    // Contiguous - single copy
                                                    Marshal.Copy(mapped.pData, data, 0, data.Length);
                                                }
                                                else
                                                {
                                                    // Stride mismatch - copy row by row
                                                    int rowBytes = Width * 4;
                                                    for (int y = 0; y < Height; y++)
                                                    {
                                                        Marshal.Copy(
                                                            mapped.pData + (int)(y * mapped.RowPitch),
                                                            data,
                                                            y * rowBytes,
                                                            rowBytes);
                                                    }
                                                }

                                                var frame = new VideoFrame(
                                                    Width, Height, VideoPixelFormat.BGRA,
                                                    new ReadOnlyMemory<byte>(data),
                                                    frameInfo.LastPresentTime);

                                                OnFrame.Invoke(frame);
                                            }
                                            finally
                                            {
                                                D3D11.Unmap(_d3dContext, _stagingTexture, 0);
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        Marshal.Release(texturePtr);
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.Release(resourcePtr);
                            }
                        }
                    }
                    finally
                    {
                        if (resourceObj != null) Marshal.ReleaseComObject(resourceObj);
                        _duplication.ReleaseFrame();
                    }
                }
            }
            catch (Exception)
            {
                // Capture ended
            }
            finally
            {
                if (_readyState == "live")
                {
                    _readyState = "ended";
                    OnEnded?.Invoke();
                }
            }
        }

        public MediaTrackSettings GetSettings()
        {
            return new MediaTrackSettings
            {
                DeviceId = Id,
                Width = Width,
                Height = Height,
                FrameRate = FrameRate,
                PixelFormat = VideoPixelFormat.BGRA,
            };
        }

        public MediaTrackConstraints GetConstraints() => new MediaTrackConstraints();

        public Task ApplyConstraints(MediaTrackConstraints constraints)
        {
            return Task.CompletedTask;
        }

        public void Stop()
        {
            if (_readyState == "ended") return;
            _capturing = false;
            _readyState = "ended";
            OnEnded?.Invoke();
        }

        public IMediaStreamTrack Clone()
        {
            return new WindowsMediaStreamTrack(
                id: Guid.NewGuid().ToString(),
                kind: "video",
                label: Label);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _capturing = false;
            _captureThread?.Join(2000);

            if (_duplication != null)
            {
                try { Marshal.ReleaseComObject(_duplication); } catch { }
                _duplication = null;
            }

            if (_stagingTexture != IntPtr.Zero)
            {
                Marshal.Release(_stagingTexture);
                _stagingTexture = IntPtr.Zero;
            }

            if (_d3dContext != IntPtr.Zero)
            {
                Marshal.Release(_d3dContext);
                _d3dContext = IntPtr.Zero;
            }

            if (_d3dDevice != IntPtr.Zero)
            {
                Marshal.Release(_d3dDevice);
                _d3dDevice = IntPtr.Zero;
            }

            if (_readyState == "live")
            {
                _readyState = "ended";
                OnEnded?.Invoke();
            }
        }

        /// <summary>
        /// Raw vtable call for IDXGIOutput1::DuplicateOutput.
        /// Avoids managed COM interop issues when passing an IntPtr-based D3D11 device.
        /// Vtable: IUnknown[3] + IDXGIObject[4] + IDXGIOutput[12] + IDXGIOutput1[4]
        /// DuplicateOutput is the last method = slot 3+4+12+3 = 22.
        /// </summary>
        private static unsafe int DuplicateOutputRaw(IntPtr output1, IntPtr d3dDevice, out IntPtr duplication)
        {
            duplication = IntPtr.Zero;
            var vtable = Marshal.ReadIntPtr(output1);
            var duplicateOutputFn = Marshal.ReadIntPtr(vtable, 22 * IntPtr.Size);
            fixed (IntPtr* pDupl = &duplication)
            {
                return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)duplicateOutputFn)(
                    output1, d3dDevice, pDupl);
            }
        }
    }
}
