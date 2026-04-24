namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Cross-platform media device access.
    /// Browser: wraps navigator.mediaDevices via SpawnDev.BlazorJS.
    /// Windows: MediaFoundation + DirectShow (video), WASAPI (audio) via P/Invoke.
    /// Linux: device enumeration via /dev/video* and /proc/asound/cards; capture pending V4L2/PulseAudio impl.
    /// macOS: not yet implemented.
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
            if (OperatingSystem.IsLinux())
                return Linux.LinuxMediaDevices.GetUserMedia(constraints);
            throw new PlatformNotSupportedException(
                $"GetUserMedia is not yet implemented for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Currently supported: Browser (Blazor WASM), Windows, Linux (enumerate-only).");
        }

        /// <summary>
        /// Requests access to screen capture.
        /// Currently browser + Windows only.
        /// </summary>
        public static Task<IMediaStream> GetDisplayMedia(MediaStreamConstraints? constraints = null)
        {
            if (OperatingSystem.IsBrowser())
                return Browser.BrowserMediaDevices.GetDisplayMedia(constraints);
            if (OperatingSystem.IsWindows())
                return Windows.WindowsMediaDevices.GetDisplayMedia(constraints);
            if (OperatingSystem.IsLinux())
                return Linux.LinuxMediaDevices.GetDisplayMedia(constraints);
            throw new PlatformNotSupportedException(
                $"GetDisplayMedia is not yet implemented for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Currently supported: Browser (Blazor WASM), Windows.");
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
            if (OperatingSystem.IsLinux())
                return Linux.LinuxMediaDevices.EnumerateDevices();
            throw new PlatformNotSupportedException(
                $"EnumerateDevices is not yet implemented for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Currently supported: Browser (Blazor WASM), Windows, Linux.");
        }
    }
}
