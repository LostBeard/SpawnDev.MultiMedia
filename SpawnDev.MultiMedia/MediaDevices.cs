namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Cross-platform media device access.
    /// Browser: wraps navigator.mediaDevices via SpawnDev.BlazorJS.
    /// Windows: MediaFoundation + DirectShow (video), WASAPI (audio) via P/Invoke.
    /// Linux/macOS: not yet implemented.
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
                return Browser.BrowserMediaDevices.GetUserMedia(constraints);
            if (OperatingSystem.IsWindows())
                return Windows.WindowsMediaDevices.GetUserMedia(constraints);
            throw new PlatformNotSupportedException(
                $"GetUserMedia is not yet implemented for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Currently supported: Browser (Blazor WASM) and Windows.");
        }

        /// <summary>
        /// Requests access to screen capture.
        /// Currently browser-only.
        /// </summary>
        public static Task<IMediaStream> GetDisplayMedia(MediaStreamConstraints? constraints = null)
        {
            if (OperatingSystem.IsBrowser())
                return Browser.BrowserMediaDevices.GetDisplayMedia(constraints);
            if (OperatingSystem.IsWindows())
                return Windows.WindowsMediaDevices.GetDisplayMedia(constraints);
            throw new PlatformNotSupportedException(
                $"GetDisplayMedia is not yet implemented for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Currently supported: Browser (Blazor WASM) and Windows.");
        }

        /// <summary>
        /// Enumerates available media input and output devices.
        /// </summary>
        public static Task<MediaDeviceInfo[]> EnumerateDevices()
        {
            if (OperatingSystem.IsBrowser())
                return Browser.BrowserMediaDevices.EnumerateDevices();
            if (OperatingSystem.IsWindows())
                return Windows.WindowsMediaDevices.EnumerateDevices();
            throw new PlatformNotSupportedException(
                $"EnumerateDevices is not yet implemented for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Currently supported: Browser (Blazor WASM) and Windows.");
        }
    }
}
