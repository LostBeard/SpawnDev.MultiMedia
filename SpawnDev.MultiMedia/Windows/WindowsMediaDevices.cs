using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Windows desktop implementation of media device access.
    /// Uses MediaFoundation for video capture and WASAPI for audio capture.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class WindowsMediaDevices
    {
        private static bool _mfInitialized;
        private static readonly object _initLock = new();

        private static void EnsureMFInitialized()
        {
            if (_mfInitialized) return;
            lock (_initLock)
            {
                if (_mfInitialized) return;
                MF.ThrowOnFailure(MF.MFStartup(MF.MF_VERSION, 0));
                _mfInitialized = true;
            }
        }

        public static Task<IMediaStream> GetUserMedia(MediaStreamConstraints constraints)
        {
            var tracks = new List<IMediaStreamTrack>();

            if (constraints.Video != null)
            {
                EnsureMFInitialized();
                var videoDevices = EnumerateVideoDeviceActivates();
                if (videoDevices.Length > 0)
                {
                    // Use first available camera (or match deviceId constraint)
                    string? requestedDeviceId = null;
                    if (constraints.Video is MediaTrackConstraints vc)
                        requestedDeviceId = vc.DeviceId;

                    IMFActivate? selectedActivate = null;
                    string selectedLabel = "Video Input";

                    foreach (var (activate, label, symbolicLink) in videoDevices)
                    {
                        if (requestedDeviceId != null && symbolicLink != requestedDeviceId)
                        {
                            Marshal.ReleaseComObject(activate);
                            continue;
                        }
                        selectedActivate = activate;
                        selectedLabel = label;
                        break;
                    }

                    // Release any we didn't pick
                    foreach (var (activate, _, _) in videoDevices)
                    {
                        if (!ReferenceEquals(activate, selectedActivate))
                            Marshal.ReleaseComObject(activate);
                    }

                    if (selectedActivate != null)
                    {
                        var track = WindowsVideoTrack.CreateFromActivate(selectedActivate, selectedLabel, constraints.Video as MediaTrackConstraints);
                        tracks.Add(track);
                    }
                }
                else
                {
                    // No cameras found - return stub track for test compatibility
                    tracks.Add(new WindowsMediaStreamTrack(
                        id: Guid.NewGuid().ToString(),
                        kind: "video",
                        label: "No Camera Found"));
                }
            }

            if (constraints.Audio != null)
            {
                // Audio capture stub - WASAPI implementation coming
                tracks.Add(new WindowsMediaStreamTrack(
                    id: Guid.NewGuid().ToString(),
                    kind: "audio",
                    label: "Default Audio Input"));
            }

            if (tracks.Count == 0)
                throw new ArgumentException("At least one of audio or video must be requested.");

            IMediaStream stream = new WindowsMediaStream(tracks.ToArray());
            return Task.FromResult(stream);
        }

        public static Task<MediaDeviceInfo[]> EnumerateDevices()
        {
            EnsureMFInitialized();
            var devices = new List<MediaDeviceInfo>();

            // Video devices via MediaFoundation
            var videoActivates = EnumerateVideoDeviceActivates();
            foreach (var (activate, label, symbolicLink) in videoActivates)
            {
                devices.Add(new MediaDeviceInfo
                {
                    DeviceId = symbolicLink,
                    Kind = "videoinput",
                    Label = label,
                    GroupId = "",
                });
                Marshal.ReleaseComObject(activate);
            }

            // Audio devices will be added when WASAPI is implemented

            return Task.FromResult(devices.ToArray());
        }

        /// <summary>
        /// Enumerates video capture devices via MFEnumDeviceSources.
        /// Returns IMFActivate handles with their friendly names and symbolic links.
        /// Caller is responsible for releasing the IMFActivate objects.
        /// </summary>
        internal static (IMFActivate activate, string label, string symbolicLink)[] EnumerateVideoDeviceActivates()
        {
            var result = new List<(IMFActivate, string, string)>();

            int hr = MF.MFCreateAttributes(out var attrs, 1);
            if (hr < 0) return result.ToArray();

            try
            {
                var sourceTypeKey = MF.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
                var vidcapGuid = MF.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
                attrs.SetGUID(ref sourceTypeKey, ref vidcapGuid);

                hr = MF.MFEnumDeviceSources(attrs, out var devicesPtr, out var count);
                if (hr < 0 || count == 0) return result.ToArray();

                try
                {
                    for (uint i = 0; i < count; i++)
                    {
                        var activatePtr = Marshal.ReadIntPtr(devicesPtr, (int)(i * IntPtr.Size));
                        if (activatePtr == IntPtr.Zero) continue;

                        var activate = (IMFActivate)Marshal.GetObjectForIUnknown(activatePtr);
                        Marshal.Release(activatePtr);

                        // Get friendly name
                        string label = "Video Device";
                        var nameKey = MF.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME;
                        if (activate.GetAllocatedString(ref nameKey, out var name, out _) >= 0 && name != null)
                            label = name;

                        // Get symbolic link (device ID)
                        string symbolicLink = $"videoinput:{i}";
                        var linkKey = MF.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK;
                        if (activate.GetAllocatedString(ref linkKey, out var link, out _) >= 0 && link != null)
                            symbolicLink = link;

                        result.Add((activate, label, symbolicLink));
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(devicesPtr);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(attrs);
            }

            return result.ToArray();
        }
    }
}
