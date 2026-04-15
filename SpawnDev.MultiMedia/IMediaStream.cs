namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Cross-platform media stream - a collection of audio and/or video tracks.
    /// Browser: wraps native MediaStream via SpawnDev.BlazorJS.
    /// Desktop: wraps platform-specific media capture.
    /// </summary>
    public interface IMediaStream : IDisposable
    {
        /// <summary>
        /// A unique identifier for this stream.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Whether the stream is currently active (has at least one non-ended track).
        /// </summary>
        bool Active { get; }

        /// <summary>
        /// Returns all tracks in this stream.
        /// </summary>
        IMediaStreamTrack[] GetTracks();

        /// <summary>
        /// Returns all audio tracks in this stream.
        /// </summary>
        IMediaStreamTrack[] GetAudioTracks();

        /// <summary>
        /// Returns all video tracks in this stream.
        /// </summary>
        IMediaStreamTrack[] GetVideoTracks();

        /// <summary>
        /// Returns a track by its ID, or null if not found.
        /// </summary>
        IMediaStreamTrack? GetTrackById(string trackId);

        /// <summary>
        /// Adds a track to this stream.
        /// </summary>
        void AddTrack(IMediaStreamTrack track);

        /// <summary>
        /// Removes a track from this stream.
        /// </summary>
        void RemoveTrack(IMediaStreamTrack track);

        /// <summary>
        /// Clones this stream and all its tracks.
        /// </summary>
        IMediaStream Clone();

        /// <summary>
        /// Fired when a track is added to this stream.
        /// </summary>
        event Action<IMediaStreamTrack>? OnAddTrack;

        /// <summary>
        /// Fired when a track is removed from this stream.
        /// </summary>
        event Action<IMediaStreamTrack>? OnRemoveTrack;
    }
}
