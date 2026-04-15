namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Windows desktop implementation of IMediaStream.
    /// Manages a collection of WindowsMediaStreamTrack instances.
    /// </summary>
    public class WindowsMediaStream : IMediaStream
    {
        private readonly List<IMediaStreamTrack> _tracks;
        private bool _disposed;

        public string Id { get; }
        public bool Active => _tracks.Any(t => t.ReadyState == "live");

        public event Action<IMediaStreamTrack>? OnAddTrack;
        public event Action<IMediaStreamTrack>? OnRemoveTrack;

        public WindowsMediaStream(IMediaStreamTrack[] tracks)
        {
            Id = Guid.NewGuid().ToString();
            _tracks = new List<IMediaStreamTrack>(tracks);
        }

        public IMediaStreamTrack[] GetTracks() => _tracks.ToArray();

        public IMediaStreamTrack[] GetAudioTracks() =>
            _tracks.Where(t => t.Kind == "audio").ToArray();

        public IMediaStreamTrack[] GetVideoTracks() =>
            _tracks.Where(t => t.Kind == "video").ToArray();

        public IMediaStreamTrack? GetTrackById(string trackId) =>
            _tracks.FirstOrDefault(t => t.Id == trackId);

        public void AddTrack(IMediaStreamTrack track)
        {
            _tracks.Add(track);
            OnAddTrack?.Invoke(track);
        }

        public void RemoveTrack(IMediaStreamTrack track)
        {
            if (_tracks.Remove(track))
                OnRemoveTrack?.Invoke(track);
        }

        public IMediaStream Clone()
        {
            var clonedTracks = _tracks.Select(t => t.Clone()).ToArray();
            return new WindowsMediaStream(clonedTracks);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var track in _tracks)
            {
                track.Stop();
                track.Dispose();
            }
            _tracks.Clear();
        }
    }
}
