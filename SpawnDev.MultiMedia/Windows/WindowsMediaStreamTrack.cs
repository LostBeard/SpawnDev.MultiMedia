namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Windows desktop implementation of IMediaStreamTrack.
    /// Phase 1: stub implementation with correct state management.
    /// Phase 2/3: will integrate MediaFoundation (video) and WASAPI (audio) capture.
    /// </summary>
    public class WindowsMediaStreamTrack : IMediaStreamTrack
    {
        private bool _disposed;
        private bool _enabled = true;
        private string _readyState = "live";
        private string _contentHint = "";

        public string Id { get; }
        public string Kind { get; }
        public string Label { get; }

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

        public WindowsMediaStreamTrack(string id, string kind, string label)
        {
            Id = id;
            Kind = kind;
            Label = label;
        }

        public MediaTrackSettings GetSettings()
        {
            // TODO: Return actual device settings when capture is implemented
            var settings = new MediaTrackSettings { DeviceId = Id };
            if (Kind == "video")
            {
                settings.Width = 640;
                settings.Height = 480;
                settings.FrameRate = 30;
            }
            else if (Kind == "audio")
            {
                settings.SampleRate = 48000;
                settings.ChannelCount = 1;
                settings.SampleSize = 16;
            }
            return settings;
        }

        public MediaTrackConstraints GetConstraints() => new MediaTrackConstraints();

        public Task ApplyConstraints(MediaTrackConstraints constraints)
        {
            // TODO: Apply constraints to the active capture session
            return Task.CompletedTask;
        }

        public void Stop()
        {
            if (_readyState == "ended") return;
            _readyState = "ended";
            // TODO: Stop MediaFoundation/WASAPI capture
            OnEnded?.Invoke();
        }

        public IMediaStreamTrack Clone()
        {
            return new WindowsMediaStreamTrack(
                id: Guid.NewGuid().ToString(),
                kind: Kind,
                label: Label);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            // TODO: Release MediaFoundation/WASAPI resources
        }
    }
}
