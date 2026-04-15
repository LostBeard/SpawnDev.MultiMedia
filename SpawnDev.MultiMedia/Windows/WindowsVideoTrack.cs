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
        private IMFActivate? _activate;
        private IMFMediaSource? _mediaSource;
        private IMFSourceReader? _sourceReader;
        private Thread? _captureThread;
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

            // Configure output format - request RGB32 (BGRA) for easy consumption
            MF.ThrowOnFailure(MF.MFCreateMediaType(out var outputType));
            var majorTypeKey = MF.MF_MT_MAJOR_TYPE;
            var videoType = MF.MFMediaType_Video;
            outputType.SetGUID(ref majorTypeKey, ref videoType);

            var subtypeKey = MF.MF_MT_SUBTYPE;
            var rgb32 = MF.MFVideoFormat_RGB32;
            outputType.SetGUID(ref subtypeKey, ref rgb32);

            var hr = track._sourceReader.SetCurrentMediaType(
                MF.MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, outputType);
            Marshal.ReleaseComObject(outputType);

            if (hr < 0)
            {
                // RGB32 not supported by decoder chain - fall back to reading native format
                track._sourceReader.GetCurrentMediaType(
                    MF.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out var currentType);
                var stKey = MF.MF_MT_SUBTYPE;
                currentType.GetGUID(ref stKey, out var subtype);
                track._outputFormat = MF.SubtypeToPixelFormat(subtype) ?? VideoPixelFormat.BGRA;
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

            if (_readyState == "live")
            {
                _readyState = "ended";
                OnEnded?.Invoke();
            }
        }
    }
}
