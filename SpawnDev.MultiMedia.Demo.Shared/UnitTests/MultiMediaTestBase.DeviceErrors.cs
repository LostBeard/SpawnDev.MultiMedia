using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    /// <summary>
    /// GetUserMedia failures throw <see cref="MediaDeviceException"/> on desktop AND in the browser, with the real
    /// cause kept. Desktop used to swallow a capture failure into Debug output and hand back a stub
    /// "No Camera Found" / "No Audio Input Found" track; the browser treated a DeviceId as a mere preference and
    /// silently opened a different device. Both now fail the same way (DeviceId is exact on both).
    /// </summary>
    public abstract partial class MultiMediaTestBase
    {
        const string NoSuchDevice = "spawndev-no-such-device-0000";

        static void AssertDeviceNotFound(MediaDeviceException ex, string kind)
        {
            // Desktop names the missing device NotFoundError; the browser reports an unsatisfiable exact
            // deviceId as OverconstrainedError.
            if (ex.Name != MediaDeviceException.NotFoundError && ex.Name != "OverconstrainedError")
                throw new Exception($"Unexpected MediaDeviceException.Name '{ex.Name}' for an unknown {kind} DeviceId: {ex.Message}");
            if (!OperatingSystem.IsBrowser() && ex.Kind != kind)
                throw new Exception($"MediaDeviceException.Kind is '{ex.Kind}', expected '{kind}'");
        }

        [TestMethod]
        public async Task GetUserMedia_UnknownVideoDeviceId_ThrowsMediaDeviceException()
        {
            try
            {
                using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints
                {
                    Video = new MediaTrackConstraints { DeviceId = NoSuchDevice },
                });
                throw new Exception($"GetUserMedia returned a stream ({stream.GetVideoTracks().FirstOrDefault()?.Label}) for an unknown video DeviceId instead of throwing");
            }
            catch (MediaDeviceException ex)
            {
                AssertDeviceNotFound(ex, "video");
            }
        }

        [TestMethod]
        public async Task GetUserMedia_UnknownAudioDeviceId_ThrowsMediaDeviceException()
        {
            try
            {
                using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints
                {
                    Audio = new MediaTrackConstraints { DeviceId = NoSuchDevice },
                });
                throw new Exception($"GetUserMedia returned a stream ({stream.GetAudioTracks().FirstOrDefault()?.Label}) for an unknown audio DeviceId instead of throwing");
            }
            catch (MediaDeviceException ex)
            {
                AssertDeviceNotFound(ex, "audio");
            }
        }
    }
}
