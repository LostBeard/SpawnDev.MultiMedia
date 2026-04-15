namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Extended video track interface with raw frame access.
    /// On desktop: fires OnFrame with raw pixel data from the camera.
    /// In browser: use a video element instead (OnFrame does not fire).
    /// </summary>
    public interface IVideoTrack : IMediaStreamTrack
    {
        /// <summary>
        /// Video width in pixels.
        /// </summary>
        int Width { get; }

        /// <summary>
        /// Video height in pixels.
        /// </summary>
        int Height { get; }

        /// <summary>
        /// Current frame rate.
        /// </summary>
        double FrameRate { get; }

        /// <summary>
        /// Raw frame callback for desktop rendering/processing.
        /// In browser this event does not fire - use HTML video element instead.
        /// </summary>
        event Action<VideoFrame>? OnFrame;
    }
}
