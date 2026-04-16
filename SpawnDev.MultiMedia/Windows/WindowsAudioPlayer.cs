using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Windows audio playback using WASAPI IAudioRenderClient.
    /// Plays audio from an IAudioTrack through the default output device.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsAudioPlayer : IAudioPlayer
    {
        private IAudioClient? _audioClient;
        private IAudioRenderClient? _renderClient;
        private IMMDevice? _device;
        private IntPtr _mixFormatPtr;
        private IAudioTrack? _track;
        private Action<AudioFrame>? _frameHandler;
        private Thread? _playbackThread;
        private volatile bool _playing;
        private bool _disposed;
        private float _volume = 1.0f;
        private bool _muted;
        private int _blockAlign;
        private uint _bufferFrameCount;

        public float Volume
        {
            get => _volume;
            set => _volume = Math.Clamp(value, 0f, 1f);
        }

        public bool Muted
        {
            get => _muted;
            set => _muted = value;
        }

        public WindowsAudioPlayer()
        {
            // Get default render device
            var clsid = WASAPI.CLSID_MMDeviceEnumerator;
            var iid = typeof(IMMDeviceEnumerator).GUID;
            MF.ThrowOnFailure(WASAPI.CoCreateInstance(ref clsid, IntPtr.Zero, WASAPI.CLSCTX_ALL, ref iid, out var enumObj));
            var enumerator = (IMMDeviceEnumerator)enumObj;

            try
            {
                MF.ThrowOnFailure(enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out _device));
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            // Activate IAudioClient
            var iidAudioClient = typeof(IAudioClient).GUID;
            MF.ThrowOnFailure(_device.Activate(ref iidAudioClient, WASAPI.CLSCTX_ALL, IntPtr.Zero, out var clientObj));
            _audioClient = (IAudioClient)clientObj;

            // Get mix format
            MF.ThrowOnFailure(_audioClient.GetMixFormat(out _mixFormatPtr));
            var format = Marshal.PtrToStructure<WAVEFORMATEX>(_mixFormatPtr);
            _blockAlign = format.nBlockAlign;

            // Initialize for playback
            MF.ThrowOnFailure(_audioClient.GetDevicePeriod(out var defaultPeriod, out _));
            MF.ThrowOnFailure(_audioClient.Initialize(
                AUDCLNT_SHAREMODE.Shared, 0,
                defaultPeriod, 0,
                _mixFormatPtr, IntPtr.Zero));

            MF.ThrowOnFailure(_audioClient.GetBufferSize(out _bufferFrameCount));

            // Get render client
            var iidRender = typeof(IAudioRenderClient).GUID;
            MF.ThrowOnFailure(_audioClient.GetService(ref iidRender, out var renderObj));
            _renderClient = (IAudioRenderClient)renderObj;
        }

        public void Play(IAudioTrack track)
        {
            Stop();
            _track = track;
            _playing = true;

            MF.ThrowOnFailure(_audioClient!.Start());

            _playbackThread = new Thread(PlaybackLoop)
            {
                IsBackground = true,
                Name = "WASAPI_Playback",
            };
            _playbackThread.Start();
        }

        public void Stop()
        {
            _playing = false;

            // Unsubscribe from track events to prevent leaks
            if (_track != null && _frameHandler != null)
            {
                _track.OnFrame -= _frameHandler;
                _frameHandler = null;
            }

            _playbackThread?.Join(2000);
            _playbackThread = null;

            try { _audioClient?.Stop(); } catch { }
            try { _audioClient?.Reset(); } catch { }

            _track = null;
        }

        private void PlaybackLoop()
        {
            var pendingFrames = new System.Collections.Concurrent.ConcurrentQueue<AudioFrame>();

            // Subscribe using stored delegate so we can unsubscribe later
            if (_track != null)
            {
                _frameHandler = frame => pendingFrames.Enqueue(frame);
                _track.OnFrame += _frameHandler;
            }

            try
            {
                while (_playing && _renderClient != null && _audioClient != null)
                {
                    Thread.Sleep(5);

                    _audioClient.GetCurrentPadding(out var padding);
                    var availableFrames = _bufferFrameCount - padding;
                    if (availableFrames == 0) continue;

                    if (_muted || pendingFrames.IsEmpty)
                    {
                        var hr = _renderClient.GetBuffer(availableFrames, out _);
                        if (hr >= 0)
                            _renderClient.ReleaseBuffer(availableFrames, 2); // AUDCLNT_BUFFERFLAGS_SILENT
                        continue;
                    }

                    var hr2 = _renderClient.GetBuffer(availableFrames, out var bufferPtr);
                    if (hr2 < 0) continue;

                    int bytesAvailable = (int)availableFrames * _blockAlign;
                    int bytesWritten = 0;

                    while (bytesWritten < bytesAvailable && pendingFrames.TryDequeue(out var frame))
                    {
                        var data = frame.Data.Span;
                        int toCopy = Math.Min(data.Length, bytesAvailable - bytesWritten);
                        Marshal.Copy(data.Slice(0, toCopy).ToArray(), 0,
                            bufferPtr + bytesWritten, toCopy);
                        bytesWritten += toCopy;
                    }

                    uint framesWritten = (uint)(bytesWritten / _blockAlign);
                    _renderClient.ReleaseBuffer(framesWritten, 0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WASAPI playback error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();

            if (_renderClient != null) { Marshal.ReleaseComObject(_renderClient); _renderClient = null; }
            if (_audioClient != null) { Marshal.ReleaseComObject(_audioClient); _audioClient = null; }
            if (_device != null) { Marshal.ReleaseComObject(_device); _device = null; }
            if (_mixFormatPtr != IntPtr.Zero) { Marshal.FreeCoTaskMem(_mixFormatPtr); _mixFormatPtr = IntPtr.Zero; }
        }
    }
}
