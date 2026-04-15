namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Cross-platform media device access.
    /// Browser: wraps navigator.mediaDevices via SpawnDev.BlazorJS.
    /// Desktop: wraps platform-specific media APIs (MediaFoundation, WASAPI, etc.).
    /// </summary>
    public static class MediaDevices
    {
        /// <summary>
        /// Requests access to media devices (camera, microphone).
        /// Returns a media stream with the requested tracks.
        /// </summary>
        public static Task<IMediaStream> GetUserMedia(MediaStreamConstraints constraints)
        {
            if (OperatingSystem.IsBrowser())
            {
                return Browser.BrowserMediaDevices.GetUserMedia(constraints);
            }
            else
            {
                return Windows.WindowsMediaDevices.GetUserMedia(constraints);
            }
        }

        /// <summary>
        /// Requests access to screen capture.
        /// Currently browser-only - throws PlatformNotSupportedException on desktop.
        /// </summary>
        public static Task<IMediaStream> GetDisplayMedia(MediaStreamConstraints? constraints = null)
        {
            if (OperatingSystem.IsBrowser())
            {
                return Browser.BrowserMediaDevices.GetDisplayMedia(constraints);
            }
            else
            {
                throw new PlatformNotSupportedException("GetDisplayMedia is not yet available on desktop.");
            }
        }

        /// <summary>
        /// Enumerates available media input and output devices.
        /// </summary>
        public static Task<MediaDeviceInfo[]> EnumerateDevices()
        {
            if (OperatingSystem.IsBrowser())
            {
                return Browser.BrowserMediaDevices.EnumerateDevices();
            }
            else
            {
                return Windows.WindowsMediaDevices.EnumerateDevices();
            }
        }
    }
}
