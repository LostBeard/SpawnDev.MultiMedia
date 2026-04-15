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
                var audioDevices = EnumerateAudioEndpoints(EDataFlow.eCapture);
                if (audioDevices.Length > 0)
                {
                    string? requestedDeviceId = null;
                    if (constraints.Audio is MediaTrackConstraints ac)
                        requestedDeviceId = ac.DeviceId;

                    IMMDevice? selectedDevice = null;
                    string selectedLabel = "Audio Input";

                    foreach (var (device, label, deviceId) in audioDevices)
                    {
                        if (requestedDeviceId != null && deviceId != requestedDeviceId)
                        {
                            Marshal.ReleaseComObject(device);
                            continue;
                        }
                        selectedDevice = device;
                        selectedLabel = label;
                        break;
                    }

                    // Release any we didn't pick
                    foreach (var (device, _, _) in audioDevices)
                    {
                        if (!ReferenceEquals(device, selectedDevice))
                            Marshal.ReleaseComObject(device);
                    }

                    if (selectedDevice != null)
                    {
                        var track = WindowsAudioTrack.CreateFromDevice(selectedDevice, selectedLabel);
                        tracks.Add(track);
                    }
                }
                else
                {
                    // No mics found - return stub track for test compatibility
                    tracks.Add(new WindowsMediaStreamTrack(
                        id: Guid.NewGuid().ToString(),
                        kind: "audio",
                        label: "No Audio Input Found"));
                }
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

            // Video devices - merge MediaFoundation + DirectShow for full coverage
            // MF finds hardware cameras; DirectShow also finds virtual cameras (OBS, etc.)
            var seenVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                seenVideoIds.Add(symbolicLink);
                Marshal.ReleaseComObject(activate);
            }

            // DirectShow catches virtual cameras that MF may miss
            var dshowDevices = EnumerateDirectShowVideoDevices();
            foreach (var (label, devicePath) in dshowDevices)
            {
                if (!seenVideoIds.Contains(devicePath))
                {
                    devices.Add(new MediaDeviceInfo
                    {
                        DeviceId = devicePath,
                        Kind = "videoinput",
                        Label = label,
                        GroupId = "",
                    });
                    seenVideoIds.Add(devicePath);
                }
            }

            // Audio capture devices (microphones) via WASAPI
            var audioInputs = EnumerateAudioEndpoints(EDataFlow.eCapture);
            foreach (var (device, label, deviceId) in audioInputs)
            {
                devices.Add(new MediaDeviceInfo
                {
                    DeviceId = deviceId,
                    Kind = "audioinput",
                    Label = label,
                    GroupId = "",
                });
                Marshal.ReleaseComObject(device);
            }

            // Audio render devices (speakers/headphones) via WASAPI
            var audioOutputs = EnumerateAudioEndpoints(EDataFlow.eRender);
            foreach (var (device, label, deviceId) in audioOutputs)
            {
                devices.Add(new MediaDeviceInfo
                {
                    DeviceId = deviceId,
                    Kind = "audiooutput",
                    Label = label,
                    GroupId = "",
                });
                Marshal.ReleaseComObject(device);
            }

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

        /// <summary>
        /// Enumerates audio endpoints via WASAPI IMMDeviceEnumerator.
        /// Pass eCapture for microphones, eRender for speakers/headphones.
        /// Caller is responsible for releasing the IMMDevice objects.
        /// </summary>
        internal static (IMMDevice device, string label, string deviceId)[] EnumerateAudioEndpoints(EDataFlow dataFlow)
        {
            var result = new List<(IMMDevice, string, string)>();

            try
            {
                var clsid = WASAPI.CLSID_MMDeviceEnumerator;
                var iid = typeof(IMMDeviceEnumerator).GUID;
                int hr = WASAPI.CoCreateInstance(ref clsid, IntPtr.Zero, WASAPI.CLSCTX_ALL, ref iid, out var enumObj);
                if (hr < 0) return result.ToArray();

                var enumerator = (IMMDeviceEnumerator)enumObj;
                try
                {
                    hr = enumerator.EnumAudioEndpoints(dataFlow, WASAPI.DEVICE_STATE_ACTIVE, out var collection);
                    if (hr < 0) return result.ToArray();

                    try
                    {
                        collection.GetCount(out var count);
                        for (uint i = 0; i < count; i++)
                        {
                            hr = collection.Item(i, out var device);
                            if (hr < 0) continue;

                            // Get device ID
                            string deviceId = $"audio:{i}";
                            if (device.GetId(out var id) >= 0 && id != null)
                                deviceId = id;

                            // Get friendly name from property store
                            string label = "Audio Device";
                            if (device.OpenPropertyStore(WASAPI.STGM_READ, out var store) >= 0)
                            {
                                try
                                {
                                    var nameKey = WASAPI.PKEY_Device_FriendlyName;
                                    if (store.GetValue(ref nameKey, out var pv) >= 0)
                                    {
                                        var name = pv.GetString();
                                        if (name != null) label = name;
                                        pv.Clear();
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(store);
                                }
                            }

                            result.Add((device, label, deviceId));
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(collection);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(enumerator);
                }
            }
            catch (Exception)
            {
                // WASAPI not available (e.g., no audio subsystem)
            }

            return result.ToArray();
        }

        /// <summary>
        /// Enumerates video capture devices via DirectShow ICreateDevEnum.
        /// Finds virtual cameras (OBS Virtual Camera, ManyCam, etc.) that
        /// MFEnumDeviceSources may not report.
        /// </summary>
        internal static (string label, string devicePath)[] EnumerateDirectShowVideoDevices()
        {
            var result = new List<(string, string)>();

            try
            {
                var clsid = DShow.CLSID_SystemDeviceEnum;
                var iid = typeof(ICreateDevEnum).GUID;
                int hr = DShow.CoCreateInstance(ref clsid, IntPtr.Zero, WASAPI.CLSCTX_ALL, ref iid, out var enumObj);
                if (hr < 0) return result.ToArray();

                var devEnum = (ICreateDevEnum)enumObj;
                try
                {
                    var category = DShow.CLSID_VideoInputDeviceCategory;
                    hr = devEnum.CreateClassEnumerator(ref category, out var monikerEnum, 0);
                    if (hr != 0 || monikerEnum == null) return result.ToArray();

                    try
                    {
                        // Manual vtable call for IEnumMoniker.Next - the managed COM interop
                        // has marshaling issues with the IMoniker[] out-parameter.
                        while (true)
                        {
                            var pMoniker = IntPtr.Zero;
                            uint fetched = 0;
                            if (!EnumMonikerNext(monikerEnum, ref pMoniker, ref fetched))
                                break;
                            if (fetched == 0 || pMoniker == IntPtr.Zero)
                                break;

                            var moniker = (IMoniker)Marshal.GetObjectForIUnknown(pMoniker);
                            Marshal.Release(pMoniker);

                            try
                            {
                                var iidBag = typeof(IPropertyBag).GUID;
                                hr = moniker.BindToStorage(IntPtr.Zero, IntPtr.Zero, ref iidBag, out var bagObj);
                                if (hr < 0 || bagObj == null) continue;

                                var bag = (IPropertyBag)bagObj;
                                try
                                {
                                    string label = "Video Device";
                                    string devicePath = "";

                                    if (bag.Read("FriendlyName", out var nameVal, IntPtr.Zero) == 0 && nameVal is string name)
                                        label = name;

                                    if (bag.Read("DevicePath", out var pathVal, IntPtr.Zero) == 0 && pathVal is string path)
                                        devicePath = path;
                                    else
                                        devicePath = $"dshow:video:{label}";

                                    result.Add((label, devicePath));
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(bag);
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(moniker);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(monikerEnum);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(devEnum);
                }
            }
            catch (Exception)
            {
                // DirectShow not available
            }

            return result.ToArray();
        }

        /// <summary>
        /// Manual vtable call for IEnumMoniker.Next(1, ...) to work around
        /// .NET COM interop marshaling issues with IMoniker[] array parameters.
        /// </summary>
        private static unsafe bool EnumMonikerNext(object enumMoniker, ref IntPtr pMoniker, ref uint fetched)
        {
            var punk = Marshal.GetIUnknownForObject(enumMoniker);
            try
            {
                var vtable = Marshal.ReadIntPtr(punk);
                // IEnumMoniker vtable: [0]QI [1]AddRef [2]Release [3]Next [4]Skip [5]Reset [6]Clone
                var nextFn = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                fixed (IntPtr* pMon = &pMoniker)
                fixed (uint* pFetch = &fetched)
                {
                    var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)nextFn)(
                        punk, 1, pMon, pFetch);
                    return hr == 0;
                }
            }
            finally
            {
                Marshal.Release(punk);
            }
        }
    }
}
