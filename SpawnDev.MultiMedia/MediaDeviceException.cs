namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Thrown by <see cref="MediaDevices.GetUserMedia"/> and <see cref="MediaDevices.GetDisplayMedia"/> when a
    /// device cannot be opened - the same on desktop and in the browser. <see cref="Name"/> uses the web's
    /// getUserMedia error names, and <see cref="Exception.InnerException"/> keeps the original error (the
    /// browser's DOMException, or the Media Foundation / DirectShow / WASAPI / DXGI failure on desktop).
    /// </summary>
    public class MediaDeviceException : Exception
    {
        /// <summary>No device of the requested kind exists, or none matches the requested DeviceId.</summary>
        public const string NotFoundError = "NotFoundError";
        /// <summary>A device exists but could not be opened (in use, driver or capture-graph failure).</summary>
        public const string NotReadableError = "NotReadableError";
        /// <summary>The user or the platform denied access (browser permission prompt).</summary>
        public const string NotAllowedError = "NotAllowedError";

        /// <summary>
        /// The error name: <see cref="NotFoundError"/>, <see cref="NotReadableError"/>, <see cref="NotAllowedError"/>,
        /// or any other name the browser reports (OverconstrainedError, AbortError, SecurityError, TypeError ...).
        /// </summary>
        public string Name { get; }

        /// <summary>The track kind that failed ("video" or "audio"), when known.</summary>
        public string? Kind { get; }

        /// <summary>Creates the exception.</summary>
        public MediaDeviceException(string name, string message, string? kind = null, Exception? innerException = null)
            : base($"{name}: {message}", innerException)
        {
            Name = name;
            Kind = kind;
        }
    }
}
