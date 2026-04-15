using SpawnDev.BlazorJS.JSObjects;

namespace SpawnDev.MultiMedia.Browser
{
    /// <summary>
    /// Browser implementation of IMediaStream.
    /// Wraps the native browser MediaStream via SpawnDev.BlazorJS.
    /// </summary>
    public class BrowserMediaStream : IMediaStream
    {
        /// <summary>
        /// Direct access to the underlying BlazorJS MediaStream JSObject.
        /// </summary>
        public MediaStream NativeStream { get; }

        private bool _disposed;

        public string Id => NativeStream.Id;
        public bool Active => NativeStream.Active;

        public event Action<IMediaStreamTrack>? OnAddTrack;
        public event Action<IMediaStreamTrack>? OnRemoveTrack;

        public BrowserMediaStream(MediaStream stream)
        {
            NativeStream = stream;
        }

        public IMediaStreamTrack[] GetTracks()
        {
            using var tracks = NativeStream.GetTracks();
            return tracks.ToArray().Select(t => (IMediaStreamTrack)new BrowserMediaStreamTrack(t)).ToArray();
        }

        public IMediaStreamTrack[] GetAudioTracks()
        {
            using var tracks = NativeStream.GetAudioTracks();
            return tracks.ToArray().Select(t => (IMediaStreamTrack)new BrowserMediaStreamTrack(t)).ToArray();
        }

        public IMediaStreamTrack[] GetVideoTracks()
        {
            using var tracks = NativeStream.GetVideoTracks();
            return tracks.ToArray().Select(t => (IMediaStreamTrack)new BrowserMediaStreamTrack(t)).ToArray();
        }

        public IMediaStreamTrack? GetTrackById(string trackId)
        {
            var track = NativeStream.GetTrackById(trackId);
            return track == null ? null : new BrowserMediaStreamTrack(track);
        }

        public void AddTrack(IMediaStreamTrack track)
        {
            if (track is BrowserMediaStreamTrack browserTrack)
            {
                NativeStream.AddTrack(browserTrack.NativeTrack);
                OnAddTrack?.Invoke(track);
            }
            else
            {
                throw new ArgumentException("Track must be a BrowserMediaStreamTrack in WASM.");
            }
        }

        public void RemoveTrack(IMediaStreamTrack track)
        {
            if (track is BrowserMediaStreamTrack browserTrack)
            {
                NativeStream.RemoveTrack(browserTrack.NativeTrack);
                OnRemoveTrack?.Invoke(track);
            }
        }

        public IMediaStream Clone()
        {
            return new BrowserMediaStream(NativeStream.Clone());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            using var tracks = NativeStream.GetTracks();
            foreach (var track in tracks.ToArray())
            {
                track.Stop();
                track.Dispose();
            }
            NativeStream.Dispose();
        }
    }
}
