namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Windows desktop implementation of media device access.
    /// Uses MediaFoundation for video capture and WASAPI for audio capture.
    /// Phase 1: basic device enumeration and capture with default settings.
    /// </summary>
    public static class WindowsMediaDevices
    {
        public static Task<IMediaStream> GetUserMedia(MediaStreamConstraints constraints)
        {
            var tracks = new List<IMediaStreamTrack>();

            if (constraints.Audio != null)
            {
                // TODO: Phase 3 - WASAPI audio capture via IAudioClient/IAudioCaptureClient
                tracks.Add(new WindowsMediaStreamTrack(
                    id: Guid.NewGuid().ToString(),
                    kind: "audio",
                    label: "Default Audio Input"));
            }

            if (constraints.Video != null)
            {
                // TODO: Phase 2 - MediaFoundation video capture via IMFSourceReader
                tracks.Add(new WindowsMediaStreamTrack(
                    id: Guid.NewGuid().ToString(),
                    kind: "video",
                    label: "Default Video Input"));
            }

            if (tracks.Count == 0)
                throw new ArgumentException("At least one of audio or video must be requested.");

            IMediaStream stream = new WindowsMediaStream(tracks.ToArray());
            return Task.FromResult(stream);
        }

        public static Task<MediaDeviceInfo[]> EnumerateDevices()
        {
            var devices = new List<MediaDeviceInfo>();

            // TODO: Phase 2 - MFEnumDeviceSources for video devices
            // TODO: Phase 3 - IMMDeviceEnumerator for audio devices

            // Placeholder: report that the platform supports the API
            // Real device enumeration coming in Phase 2/3
            return Task.FromResult(devices.ToArray());
        }
    }
}
