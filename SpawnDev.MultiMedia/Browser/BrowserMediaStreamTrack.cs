using SpawnDev.BlazorJS.JSObjects;

namespace SpawnDev.MultiMedia.Browser
{
    /// <summary>
    /// Browser implementation of IMediaStreamTrack.
    /// Wraps the native browser MediaStreamTrack via SpawnDev.BlazorJS.
    /// </summary>
    public class BrowserMediaStreamTrack : IMediaStreamTrack
    {
        /// <summary>
        /// Direct access to the underlying BlazorJS MediaStreamTrack JSObject.
        /// </summary>
        public MediaStreamTrack NativeTrack { get; }

        private bool _disposed;

        public string Id => NativeTrack.Id;
        public string Kind => NativeTrack.Kind;
        public string Label => NativeTrack.Label;
        public bool Enabled { get => NativeTrack.Enabled; set => NativeTrack.Enabled = value; }
        public bool Muted => NativeTrack.Muted;
        public string ReadyState => NativeTrack.ReadyState;

        public event Action? OnEnded;
        public event Action? OnMute;
        public event Action? OnUnmute;

        public BrowserMediaStreamTrack(MediaStreamTrack track)
        {
            NativeTrack = track;
            NativeTrack.OnEnded += HandleEnded;
            NativeTrack.OnMute += HandleMute;
            NativeTrack.OnUnMute += HandleUnmute;
        }

        public string ContentHint
        {
            get => NativeTrack.ContentHint ?? "";
            set { /* BlazorJS ContentHint is read-only from C# side */ }
        }

        public MediaTrackSettings GetSettings()
        {
            var settings = NativeTrack.GetSettings();
            return new MediaTrackSettings
            {
                Width = (int?)settings.Width,
                Height = (int?)settings.Height,
                FrameRate = settings.FrameRate,
                AspectRatio = settings.AspectRatio,
                FacingMode = settings.FacingMode,
                ResizeMode = settings.ResizeMode,
                SampleRate = (int?)settings.SampleRate,
                SampleSize = (int?)settings.SampleSize,
                ChannelCount = (int?)settings.ChannelCount,
                EchoCancellation = settings.EchoCancellation,
                AutoGainControl = settings.AutoGainControl,
                NoiseSuppression = settings.NoiseSuppression,
                Latency = settings.Latency,
                DeviceId = settings.DeviceId,
                GroupId = settings.GroupId,
            };
        }

        public MediaTrackConstraints GetConstraints()
        {
            // Return empty constraints - browser constraints are managed via the native API
            return new MediaTrackConstraints();
        }

        public Task ApplyConstraints(MediaTrackConstraints constraints)
        {
            var jsc = new SpawnDev.BlazorJS.JSObjects.MediaTrackConstraints();
            if (constraints.Width.HasValue) jsc.Width = (uint)constraints.Width.Value;
            if (constraints.Height.HasValue) jsc.Height = (uint)constraints.Height.Value;
            if (constraints.FrameRate.HasValue) jsc.FrameRate = constraints.FrameRate.Value;
            return NativeTrack.ApplyConstraints(jsc);
        }

        public void Stop() => NativeTrack.Stop();

        public IMediaStreamTrack Clone()
        {
            return new BrowserMediaStreamTrack(NativeTrack.Clone());
        }

        private void HandleEnded(Event e) => OnEnded?.Invoke();
        private void HandleMute(Event e) => OnMute?.Invoke();
        private void HandleUnmute(Event e) => OnUnmute?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NativeTrack.OnEnded -= HandleEnded;
            NativeTrack.OnMute -= HandleMute;
            NativeTrack.OnUnMute -= HandleUnmute;
            NativeTrack.Dispose();
        }
    }
}
