using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Windows video capture track using MediaFoundation IMFSourceReader.
    /// Captures frames from a real camera device and fires OnFrame events.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsVideoTrack : IVideoTrack
    {
        // MediaFoundation capture (hardware cameras)
        private IMFActivate? _activate;
        private IMFMediaSource? _mediaSource;
        private IMFSourceReader? _sourceReader;
        private Thread? _captureThread;

        // DirectShow capture (virtual cameras - OBS, Quest, etc.)
        private object? _dsGraphBuilder;
        private object? _dsCaptureGraphBuilder;
        private object? _dsGrabberComObject; // SampleGrabber - one RCW, used as ISampleGrabber and IBaseFilter
        private object? _dsSourceFilter;
        private object? _dsNullRenderer;

        // Shared state
        private volatile bool _capturing;
        private bool _disposed;
        private bool _enabled = true;
        private string _readyState = "live";
        private string _contentHint = "";

        public string Id { get; }
        public string Kind => "video";
        public string Label { get; }
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

        private VideoPixelFormat _outputFormat = VideoPixelFormat.BGRA;

        private WindowsVideoTrack(string label)
        {
            Id = Guid.NewGuid().ToString();
            Label = label;
        }

        /// <summary>
        /// Creates a video track from an MF activation object and starts capture.
        /// </summary>
        internal static WindowsVideoTrack CreateFromActivate(IMFActivate activate, string label, MediaTrackConstraints? constraints)
        {
            var track = new WindowsVideoTrack(label);
            track._activate = activate;

            // Activate the media source
            var iidMediaSource = typeof(IMFMediaSource).GUID;
            MF.ThrowOnFailure(activate.ActivateObject(ref iidMediaSource, out var sourceObj));
            track._mediaSource = (IMFMediaSource)sourceObj;

            // Create source reader
            MF.ThrowOnFailure(MF.MFCreateSourceReaderFromMediaSource(
                track._mediaSource, null, out track._sourceReader));

            // Configure output format based on consumer preference
            var requestedFormat = constraints?.PixelFormat;
            var requestedMfSubtype = PixelFormatToMfSubtype(requestedFormat);

            MF.ThrowOnFailure(MF.MFCreateMediaType(out var outputType));
            var majorTypeKey = MF.MF_MT_MAJOR_TYPE;
            var videoType = MF.MFMediaType_Video;
            outputType.SetGUID(ref majorTypeKey, ref videoType);

            var subtypeKey = MF.MF_MT_SUBTYPE;
            outputType.SetGUID(ref subtypeKey, ref requestedMfSubtype);

            var hr = track._sourceReader.SetCurrentMediaType(
                MF.MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, outputType);
            Marshal.ReleaseComObject(outputType);

            if (hr >= 0)
            {
                // MF accepted our requested format
                track._outputFormat = requestedFormat ?? VideoPixelFormat.BGRA;
            }
            else
            {
                // Requested format not supported - read whatever the source provides natively
                track._sourceReader.GetCurrentMediaType(
                    MF.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var currentType);
                var stKey = MF.MF_MT_SUBTYPE;
                currentType.GetGUID(ref stKey, out var nativeSubtype);
                track._outputFormat = MF.SubtypeToPixelFormat(nativeSubtype) ?? VideoPixelFormat.NV12;
                Marshal.ReleaseComObject(currentType);
            }

            // Read actual output dimensions
            track.ReadCurrentFormat();

            // Start capture thread
            track._capturing = true;
            track._captureThread = new Thread(track.CaptureLoop)
            {
                IsBackground = true,
                Name = $"MF_Capture_{label}",
            };
            track._captureThread.Start();

            return track;
        }

        /// <summary>
        /// Creates a video track from a DirectShow moniker using a full capture graph.
        /// For virtual cameras (OBS, ManyCam, Quest) that are DirectShow-only.
        /// Builds: Source -> SampleGrabber -> NullRenderer, with BufferCB callback for frames.
        /// </summary>
        internal static WindowsVideoTrack CreateFromDirectShowMoniker(object monikerObj, string label, MediaTrackConstraints? constraints)
        {
            var track = new WindowsVideoTrack(label);
            var moniker = (IMoniker)monikerObj;

            // Step 1: Bind moniker to IBaseFilter (the DirectShow source filter)
            var iidBaseFilter = DSCapture.IID_IBaseFilter;
            var hr = moniker.BindToObject(IntPtr.Zero, IntPtr.Zero, ref iidBaseFilter, out var sourceObj);
            MF.ThrowOnFailure(hr);
            track._dsSourceFilter = sourceObj;
            var sourceFilter = (IBaseFilter)sourceObj;

            // Step 2: Create FilterGraph -> IGraphBuilder
            var graphObj = Activator.CreateInstance(
                Type.GetTypeFromCLSID(DSCapture.CLSID_FilterGraph)
                ?? throw new COMException("CLSID_FilterGraph not registered"))
                ?? throw new COMException("Failed to create FilterGraph");
            track._dsGraphBuilder = graphObj;
            var graph = (IGraphBuilder)graphObj;

            // Step 3: Create CaptureGraphBuilder2 and connect to graph
            var capObj = Activator.CreateInstance(
                Type.GetTypeFromCLSID(DSCapture.CLSID_CaptureGraphBuilder2)
                ?? throw new COMException("CLSID_CaptureGraphBuilder2 not registered"))
                ?? throw new COMException("Failed to create CaptureGraphBuilder2");
            track._dsCaptureGraphBuilder = capObj;
            var capBuilder = (ICaptureGraphBuilder2)capObj;
            MF.ThrowOnFailure(capBuilder.SetFiltergraph(graph));

            // Step 4: Create SampleGrabber and configure for RGB32 (BGRA in memory)
            var grabObj = Activator.CreateInstance(
                Type.GetTypeFromCLSID(DSCapture.CLSID_SampleGrabber)
                ?? throw new COMException("CLSID_SampleGrabber not registered (qedit.dll missing?)"))
                ?? throw new COMException("Failed to create SampleGrabber");
            track._dsGrabberComObject = grabObj;
            var grabber = (ISampleGrabber)grabObj;
            var grabberFilter = (IBaseFilter)grabObj;

            // Set media type - just Video major type, accept any subtype
            // DirectShow will negotiate the best format between source and grabber
            var mt = new DSAMMediaType
            {
                majorType = DSCapture.MEDIATYPE_Video,
            };
            grabber.SetMediaType(mt);

            // Step 5: Create NullRenderer (sink - discards rendered output)
            var nullObj = Activator.CreateInstance(
                Type.GetTypeFromCLSID(DSCapture.CLSID_NullRenderer)
                ?? throw new COMException("CLSID_NullRenderer not registered"))
                ?? throw new COMException("Failed to create NullRenderer");
            track._dsNullRenderer = nullObj;
            var nullRenderer = (IBaseFilter)nullObj;

            // Step 6: Add all filters to the graph
            MF.ThrowOnFailure(graph.AddFilter(sourceFilter, "Source"));
            MF.ThrowOnFailure(graph.AddFilter(grabberFilter, "SampleGrabber"));
            MF.ThrowOnFailure(graph.AddFilter(nullRenderer, "NullRenderer"));

            // Step 7: RenderStream connects Source -> SampleGrabber -> NullRenderer
            hr = capBuilder.RenderStream(
                DSCapture.PIN_CATEGORY_CAPTURE,
                DSCapture.MEDIATYPE_Video,
                sourceObj,        // source as IUnknown
                grabberFilter,    // intermediate
                nullRenderer);    // renderer
            MF.ThrowOnFailure(hr);

            // Step 8: Read actual dimensions from the connected media type
            var connectedMt = new DSAMMediaType();
            hr = grabber.GetConnectedMediaType(connectedMt);
            if (hr >= 0 && connectedMt.formatPtr != IntPtr.Zero)
            {
                try
                {
                    if (connectedMt.formatType == DSCapture.FORMAT_VideoInfo &&
                        connectedMt.formatSize >= Marshal.SizeOf<VIDEOINFOHEADER>())
                    {
                        var vih = Marshal.PtrToStructure<VIDEOINFOHEADER>(connectedMt.formatPtr);
                        track.Width = vih.bmiHeader.biWidth;
                        track.Height = Math.Abs(vih.bmiHeader.biHeight);
                        if (vih.AvgTimePerFrame > 0)
                            track.FrameRate = 10_000_000.0 / vih.AvgTimePerFrame;
                    }
                    else if (connectedMt.formatSize >= 72 + Marshal.SizeOf<BITMAPINFOHEADER>())
                    {
                        // VideoInfo2 or similar - bmiHeader at offset 72
                        var bmi = Marshal.PtrToStructure<BITMAPINFOHEADER>(connectedMt.formatPtr + 72);
                        track.Width = bmi.biWidth;
                        track.Height = Math.Abs(bmi.biHeight);
                    }
                }
                finally
                {
                    connectedMt.Free();
                }
            }

            if (track.Width == 0) track.Width = 640;
            if (track.Height == 0) track.Height = 480;
            if (track.FrameRate <= 0) track.FrameRate = 30;

            // Detect actual pixel format from connected media type subtype
            var fmtMt = new DSAMMediaType();
            if (grabber.GetConnectedMediaType(fmtMt) >= 0)
            {
                track._outputFormat = MF.SubtypeToPixelFormat(fmtMt.subType) ?? VideoPixelFormat.NV12;
                fmtMt.Free();
            }
            else
            {
                // Guess from frame size: NV12 = w*h*1.5, YUY2 = w*h*2, BGRA = w*h*4
                track._outputFormat = VideoPixelFormat.NV12;
            }

            // Step 9: Enable buffered sampling and configure callback
            // SetBufferSamples AFTER RenderStream (graph must be connected first)
            grabber.SetBufferSamples(true);
            grabber.SetOneShot(false);

            // Step 10: Start the capture graph and wait for Running state
            var mc = (IDShowMediaControl)graphObj;
            MF.ThrowOnFailure(mc.Run());

            // Wait for graph to actually enter Running state (not just transitioning)
            mc.GetState(3000, out _); // Block up to 3s for state transition

            track._capturing = true;

            // Step 11: Start polling thread to read buffered samples
            track._captureThread = new Thread(() => track.DirectShowCaptureLoop(grabber))
            {
                IsBackground = true,
                Name = $"DShow_Capture_{label}",
            };
            track._captureThread.Start();

            return track;
        }

        /// <summary>
        /// Polling loop for DirectShow capture. Reads buffered samples from the SampleGrabber.
        /// Used instead of ISampleGrabberCB callback which has COM marshaling issues in .NET.
        /// </summary>
        private void DirectShowCaptureLoop(ISampleGrabber grabber)
        {
            int targetIntervalMs = FrameRate > 0 ? (int)(1000.0 / FrameRate / 2) : 8; // Poll at 2x frame rate
            if (targetIntervalMs < 1) targetIntervalMs = 1;
            if (targetIntervalMs > 33) targetIntervalMs = 33;

            try
            {
                while (_capturing)
                {
                    Thread.Sleep(targetIntervalMs);

                    // Query the current buffer size
                    int bufferSize = 0;
                    var hr = grabber.GetCurrentBuffer(ref bufferSize, IntPtr.Zero);
                    if (hr < 0 || bufferSize <= 0) continue;
                    if (!_enabled || OnFrame == null) continue;

                    // Allocate and read the buffer
                    var bufferPtr = Marshal.AllocHGlobal(bufferSize);
                    try
                    {
                        hr = grabber.GetCurrentBuffer(ref bufferSize, bufferPtr);
                        if (hr >= 0 && bufferSize > 0)
                        {
                            var data = new byte[bufferSize];
                            Marshal.Copy(bufferPtr, data, 0, bufferSize);

                            var frame = new VideoFrame(
                                Width, Height, _outputFormat,
                                new ReadOnlyMemory<byte>(data),
                                DateTimeOffset.UtcNow.Ticks);

                            OnFrame.Invoke(frame);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(bufferPtr);
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

        private void ReadCurrentFormat()
        {
            if (_sourceReader == null) return;
            var hr = _sourceReader.GetCurrentMediaType(
                MF.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var mediaType);
            if (hr < 0) return;

            try
            {
                // Frame size
                var frameSizeKey = MF.MF_MT_FRAME_SIZE;
                if (mediaType.GetUINT64(ref frameSizeKey, out var packedSize) >= 0)
                {
                    MF.UnpackFrameSize(packedSize, out var w, out var h);
                    Width = w;
                    Height = h;
                }

                // Frame rate
                var frameRateKey = MF.MF_MT_FRAME_RATE;
                if (mediaType.GetUINT64(ref frameRateKey, out var packedRate) >= 0)
                {
                    MF.UnpackFrameRate(packedRate, out var num, out var den);
                    FrameRate = den > 0 ? (double)num / den : 30;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(mediaType);
            }
        }

        private void CaptureLoop()
        {
            try
            {
                while (_capturing && _sourceReader != null)
                {
                    var hr = _sourceReader.ReadSample(
                        MF.MF_SOURCE_READER_FIRST_VIDEO_STREAM,
                        0,
                        out _,
                        out var streamFlags,
                        out var timestamp,
                        out var sample);

                    if (hr < 0 || (streamFlags & MF.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                        break;

                    if (sample == null) continue;

                    try
                    {
                        if (_enabled && OnFrame != null)
                        {
                            hr = sample.ConvertToContiguousBuffer(out var buffer);
                            if (hr >= 0 && buffer != null)
                            {
                                try
                                {
                                    hr = buffer.Lock(out var dataPtr, out _, out var dataLength);
                                    if (hr >= 0 && dataLength > 0)
                                    {
                                        var data = new byte[dataLength];
                                        Marshal.Copy(dataPtr, data, 0, dataLength);
                                        buffer.Unlock();

                                        var frame = new VideoFrame(
                                            Width, Height, _outputFormat,
                                            new ReadOnlyMemory<byte>(data),
                                            timestamp);

                                        OnFrame.Invoke(frame);
                                    }
                                    else
                                    {
                                        buffer.Unlock();
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(buffer);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sample);
                    }
                }
            }
            catch (Exception)
            {
                // Capture ended (device disconnected, etc.)
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
                PixelFormat = _outputFormat,
            };
        }

        public MediaTrackConstraints GetConstraints() => new MediaTrackConstraints();

        public Task ApplyConstraints(MediaTrackConstraints constraints)
        {
            // Changing resolution on a live source reader requires reconfiguration
            // For now, constraints are applied at creation time only
            return Task.CompletedTask;
        }

        public void Stop()
        {
            if (_readyState == "ended") return;
            _capturing = false;

            // Stop DirectShow graph if active
            if (_dsGraphBuilder is IDShowMediaControl mc)
            {
                try { mc.Stop(); } catch { }
            }

            _readyState = "ended";
            OnEnded?.Invoke();
        }

        public IMediaStreamTrack Clone()
        {
            // Clone returns a stub since we can't duplicate the hardware capture
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

            // MediaFoundation cleanup
            if (_sourceReader != null)
            {
                Marshal.ReleaseComObject(_sourceReader);
                _sourceReader = null;
            }
            if (_mediaSource != null)
            {
                try { _mediaSource.Shutdown(); } catch { }
                Marshal.ReleaseComObject(_mediaSource);
                _mediaSource = null;
            }
            if (_activate != null)
            {
                try { _activate.ShutdownObject(); } catch { }
                Marshal.ReleaseComObject(_activate);
                _activate = null;
            }

            // DirectShow cleanup
            if (_dsGraphBuilder is IDShowMediaControl mc)
            {
                try { mc.Stop(); } catch { }
            }
            if (_dsGrabberComObject is ISampleGrabber sg)
            {
                try { sg.SetCallback(null, 1); } catch { }
            }
            if (_dsNullRenderer != null) { try { Marshal.ReleaseComObject(_dsNullRenderer); } catch { } _dsNullRenderer = null; }
            if (_dsGrabberComObject != null) { try { Marshal.ReleaseComObject(_dsGrabberComObject); } catch { } _dsGrabberComObject = null; }
            if (_dsSourceFilter != null) { try { Marshal.ReleaseComObject(_dsSourceFilter); } catch { } _dsSourceFilter = null; }
            if (_dsCaptureGraphBuilder != null) { try { Marshal.ReleaseComObject(_dsCaptureGraphBuilder); } catch { } _dsCaptureGraphBuilder = null; }
            if (_dsGraphBuilder != null) { try { Marshal.ReleaseComObject(_dsGraphBuilder); } catch { } _dsGraphBuilder = null; }

            if (_readyState == "live")
            {
                _readyState = "ended";
                OnEnded?.Invoke();
            }
        }

        /// <summary>
        /// Maps our VideoPixelFormat to an MF subtype GUID for SetCurrentMediaType.
        /// null defaults to RGB32 (BGRA) for display compatibility.
        /// </summary>
        private static Guid PixelFormatToMfSubtype(VideoPixelFormat? format) => format switch
        {
            VideoPixelFormat.NV12 => MF.MFVideoFormat_NV12,
            VideoPixelFormat.I420 => MF.MFVideoFormat_I420,
            VideoPixelFormat.YUY2 => MF.MFVideoFormat_YUY2,
            VideoPixelFormat.BGRA => MF.MFVideoFormat_RGB32,
            VideoPixelFormat.RGBA => MF.MFVideoFormat_ARGB32,
            _ => MF.MFVideoFormat_RGB32, // Default: BGRA for display consumers
        };
    }
}
