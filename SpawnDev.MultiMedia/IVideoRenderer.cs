namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Video rendering interface for desktop platforms.
    /// Displays video frames from an IVideoTrack.
    /// In browser, use HTML video element instead.
    /// </summary>
    public interface IVideoRenderer : IDisposable
    {
        /// <summary>
        /// Attach a video track to render its frames.
        /// </summary>
        void Attach(IVideoTrack track);

        /// <summary>
        /// Detach the current video track.
        /// </summary>
        void Detach();

        /// <summary>
        /// Whether a track is currently attached.
        /// </summary>
        bool IsAttached { get; }
    }
}
