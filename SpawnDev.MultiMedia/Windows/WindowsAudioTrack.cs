using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Windows audio capture track using WASAPI IAudioCaptureClient.
    /// Captures PCM samples from a real microphone and fires OnFrame events.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsAudioTrack : IAudioTrack
    {
        private IMMDevice? _device;
        private IAudioClient? _audioClient;
        private IAudioCaptureClient? _captureClient;
        private IntPtr _mixFormatPtr;
        private Thread? _captureThread;
        private EventWaitHandle? _captureEvent;
        private volatile bool _capturing;
        private bool _disposed;
        private bool _enabled = true;
        private string _readyState = "live";
        private string _contentHint = "";

        public string Id { get; }
        public string Kind => "audio";
        public string Label { get; }
        public int SampleRate { get; private set; }
        public int ChannelCount { get; private set; }
        public int BitsPerSample { get; private set; }

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
        public event Action<AudioFrame>? OnFrame;

        private int _blockAlign;

        private WindowsAudioTrack(string label)
        {
            Id = Guid.NewGuid().ToString();
            Label = label;
        }

        /// <summary>
        /// Creates an audio track from an IMMDevice and starts capture.
        /// </summary>
        internal static WindowsAudioTrack CreateFromDevice(IMMDevice device, string label)
        {
            var track = new WindowsAudioTrack(label);
            track._device = device;

            // Activate IAudioClient
            var iidAudioClient = typeof(IAudioClient).GUID;
            MF.ThrowOnFailure(device.Activate(ref iidAudioClient, WASAPI.CLSCTX_ALL, IntPtr.Zero, out var clientObj));
            track._audioClient = (IAudioClient)clientObj;

            // Get the mix format (device's native format in shared mode)
            MF.ThrowOnFailure(track._audioClient.GetMixFormat(out track._mixFormatPtr));
            var format = Marshal.PtrToStructure<WAVEFORMATEX>(track._mixFormatPtr);
            track.SampleRate = (int)format.nSamplesPerSec;
            track.ChannelCount = format.nChannels;
            track.BitsPerSample = format.wBitsPerSample;
            track._blockAlign = format.nBlockAlign;

            // Get device period for buffer sizing
            MF.ThrowOnFailure(track._audioClient.GetDevicePeriod(out var defaultPeriod, out _));

            // Initialize in shared mode with event callback
            track._captureEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            MF.ThrowOnFailure(track._audioClient.Initialize(
                AUDCLNT_SHAREMODE.Shared,
                WASAPI.AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                defaultPeriod,
                0,
                track._mixFormatPtr,
                IntPtr.Zero));

            track._audioClient.SetEventHandle(track._captureEvent.SafeWaitHandle.DangerousGetHandle());

            // Get capture client
            var iidCapture = typeof(IAudioCaptureClient).GUID;
            MF.ThrowOnFailure(track._audioClient.GetService(ref iidCapture, out var captureObj));
            track._captureClient = (IAudioCaptureClient)captureObj;

            // Start capture
            MF.ThrowOnFailure(track._audioClient.Start());

            track._capturing = true;
            track._captureThread = new Thread(track.CaptureLoop)
            {
                IsBackground = true,
                Name = $"WASAPI_Capture_{label}",
            };
            track._captureThread.Start();

            return track;
        }

        private void CaptureLoop()
        {
            try
            {
                while (_capturing && _captureClient != null && _captureEvent != null)
                {
                    // Wait for audio data (up to 2 seconds timeout)
                    if (!_captureEvent.WaitOne(2000))
                        continue;

                    _captureClient.GetNextPacketSize(out var packetSize);
                    while (packetSize > 0 && _capturing)
                    {
                        var hr = _captureClient.GetBuffer(
                            out var dataPtr,
                            out var numFrames,
                            out var flags,
                            out var devicePos,
                            out _);

                        if (hr < 0) break;

                        if (_enabled && OnFrame != null && numFrames > 0)
                        {
                            int byteCount = (int)numFrames * _blockAlign;
                            var data = new byte[byteCount];

                            if ((flags & WASAPI.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                            {
                                // Silent buffer - data is already zeroed
                            }
                            else
                            {
                                Marshal.Copy(dataPtr, data, 0, byteCount);
                            }

                            var frame = new AudioFrame(
                                SampleRate,
                                ChannelCount,
                                (int)numFrames,
                                new ReadOnlyMemory<byte>(data),
                                (long)devicePos);

                            OnFrame.Invoke(frame);
                        }

                        _captureClient.ReleaseBuffer(numFrames);
                        _captureClient.GetNextPacketSize(out packetSize);
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
                SampleRate = SampleRate,
                ChannelCount = ChannelCount,
                SampleSize = BitsPerSample,
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

            try { _audioClient?.Stop(); } catch { }

            OnEnded?.Invoke();
        }

        public IMediaStreamTrack Clone()
        {
            return new WindowsMediaStreamTrack(
                id: Guid.NewGuid().ToString(),
                kind: "audio",
                label: Label);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _capturing = false;
            _captureEvent?.Set(); // Wake up the capture thread
            _captureThread?.Join(2000);

            try { _audioClient?.Stop(); } catch { }
            try { _audioClient?.Reset(); } catch { }

            if (_captureClient != null)
            {
                Marshal.ReleaseComObject(_captureClient);
                _captureClient = null;
            }
            if (_audioClient != null)
            {
                Marshal.ReleaseComObject(_audioClient);
                _audioClient = null;
            }
            if (_device != null)
            {
                Marshal.ReleaseComObject(_device);
                _device = null;
            }
            if (_mixFormatPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_mixFormatPtr);
                _mixFormatPtr = IntPtr.Zero;
            }

            _captureEvent?.Dispose();
            _captureEvent = null;

            if (_readyState == "live")
            {
                _readyState = "ended";
                OnEnded?.Invoke();
            }
        }
    }
}
