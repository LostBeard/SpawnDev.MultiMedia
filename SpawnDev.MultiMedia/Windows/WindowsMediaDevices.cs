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

        /// <summary>
        /// Opens the requested devices. Throws <see cref="MediaDeviceException"/> - like the browser's
        /// getUserMedia rejects - when a requested kind has no device (NotFoundError) or no device of that
        /// kind could be opened (NotReadableError, with the real failure as the inner exception). It used to
        /// swallow the failure into Debug output and hand back a stub "No Camera Found" track.
        /// </summary>
        public static Task<IMediaStream> GetUserMedia(MediaStreamConstraints constraints)
        {
            var tracks = new List<IMediaStreamTrack>();
            try
            {
                if (constraints.Video?.IsRequested == true)
                    tracks.Add(OpenVideoTrack(constraints.Video.Constraints));
                if (constraints.Audio?.IsRequested == true)
                    tracks.Add(OpenAudioTrack(constraints.Audio.Constraints));
            }
            catch
            {
                // A track opened before a later one failed must not keep its device.
                foreach (var t in tracks) { try { t.Dispose(); } catch { } }
                throw;
            }

            if (tracks.Count == 0)
                throw new ArgumentException("At least one of audio or video must be requested.");

            IMediaStream stream = new WindowsMediaStream(tracks.ToArray());
            return Task.FromResult(stream);
        }

        private static IMediaStreamTrack OpenVideoTrack(MediaTrackConstraints? videoConstraints)
        {
            EnsureMFInitialized();
            string? requestedDeviceId = videoConstraints?.DeviceId;
            IMediaStreamTrack? videoTrack = null;
            var failures = new List<Exception>();
            int candidates = 0;

            // Try MediaFoundation first (hardware cameras). A device that fails to open is recorded and the
            // next candidate is tried; if none opens, every failure goes into the thrown exception.
            var mfDevices = EnumerateVideoDeviceActivates();
            foreach (var (activate, label, symbolicLink) in mfDevices)
            {
                if (videoTrack != null || (requestedDeviceId != null && symbolicLink != requestedDeviceId))
                {
                    Marshal.ReleaseComObject(activate);
                    continue;
                }
                candidates++;
                try
                {
                    videoTrack = WindowsVideoTrack.CreateFromActivate(activate, label, videoConstraints);
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException($"MediaFoundation could not open '{label}': {ex.Message}", ex));
                    try { Marshal.ReleaseComObject(activate); } catch { }
                }
            }

            // If MF had no match, try DirectShow (virtual cameras like OBS)
            if (videoTrack == null)
            {
                var dshowDevices = EnumerateDirectShowVideoDevices();
                foreach (var (label, devicePath, moniker) in dshowDevices)
                {
                    if (videoTrack != null || (requestedDeviceId != null && devicePath != requestedDeviceId))
                    {
                        if (moniker != null) Marshal.ReleaseComObject(moniker);
                        continue;
                    }
                    if (moniker == null) continue;
                    candidates++;
                    try
                    {
                        videoTrack = WindowsVideoTrack.CreateFromDirectShowMoniker(moniker, label, videoConstraints);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new InvalidOperationException($"DirectShow could not open '{label}': {ex.Message}", ex));
                        try { Marshal.ReleaseComObject(moniker); } catch { }
                    }
                }
            }

            if (videoTrack != null) return videoTrack;
            if (candidates == 0)
                throw new MediaDeviceException(MediaDeviceException.NotFoundError,
                    requestedDeviceId != null ? $"No video input device with DeviceId '{requestedDeviceId}'." : "No video input device found.",
                    "video");
            throw new MediaDeviceException(MediaDeviceException.NotReadableError,
                $"No video input device could be opened ({string.Join("; ", failures.Select(f => f.Message))})",
                "video", failures.Count == 1 ? failures[0] : new AggregateException(failures));
        }

        private static IMediaStreamTrack OpenAudioTrack(MediaTrackConstraints? audioConstraints)
        {
            string? requestedDeviceId = audioConstraints?.DeviceId;
            var audioDevices = EnumerateAudioEndpoints(EDataFlow.eCapture);

            IMMDevice? selectedDevice = null;
            string selectedLabel = "Audio Input";
            foreach (var (device, label, deviceId) in audioDevices)
            {
                if (selectedDevice != null || (requestedDeviceId != null && deviceId != requestedDeviceId)) continue;
                selectedDevice = device;
                selectedLabel = label;
            }
            // Release any we didn't pick
            foreach (var (device, _, _) in audioDevices)
            {
                if (!ReferenceEquals(device, selectedDevice))
                    Marshal.ReleaseComObject(device);
            }

            if (selectedDevice == null)
                throw new MediaDeviceException(MediaDeviceException.NotFoundError,
                    requestedDeviceId != null ? $"No audio input device with DeviceId '{requestedDeviceId}'." : "No audio input device found.",
                    "audio");
            try
            {
                return WindowsAudioTrack.CreateFromDevice(selectedDevice, selectedLabel);
            }
            catch (Exception ex)
            {
                throw new MediaDeviceException(MediaDeviceException.NotReadableError,
                    $"Audio input '{selectedLabel}' could not be opened: {ex.Message}", "audio", ex);
            }
        }

        /// <summary>
        /// Opens the screen capture. Throws <see cref="MediaDeviceException"/> (NotReadableError, real failure
        /// as the inner exception) when desktop duplication cannot be started, like the browser rejects.
        /// </summary>
        public static Task<IMediaStream> GetDisplayMedia(MediaStreamConstraints? constraints)
        {
            var videoConstraints = constraints?.Video?.Constraints;
            IMediaStreamTrack track;
            try
            {
                track = WindowsDisplayTrack.Create(videoConstraints);
            }
            catch (Exception ex) when (ex is not MediaDeviceException)
            {
                throw new MediaDeviceException(MediaDeviceException.NotReadableError,
                    $"Screen capture could not be started: {ex.Message}", "video", ex);
            }
            IMediaStream stream = new WindowsMediaStream(new IMediaStreamTrack[] { track });
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
            foreach (var (label, devicePath, dshowMoniker) in dshowDevices)
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
                if (dshowMoniker != null) Marshal.ReleaseComObject(dshowMoniker);
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
        internal static (string label, string devicePath, object? moniker)[] EnumerateDirectShowVideoDevices()
        {
            var result = new List<(string, string, object?)>();

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

                                    result.Add((label, devicePath, (object)moniker));
                                    moniker = null!; // Don't release - caller owns it
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(bag);
                                }
                            }
                            finally
                            {
                                if (moniker != null) Marshal.ReleaseComObject(moniker);
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
            // The IEnumMoniker INTERFACE pointer (QueryInterface), not GetIUnknownForObject's IUnknown identity
            // pointer: slot 3 of the identity vtable is only Next when IEnumMoniker happens to be the object's
            // primary interface - otherwise this called some other method with Next's arguments.
            var punk = Marshal.GetComInterfaceForObject(enumMoniker, typeof(IEnumMoniker));
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
