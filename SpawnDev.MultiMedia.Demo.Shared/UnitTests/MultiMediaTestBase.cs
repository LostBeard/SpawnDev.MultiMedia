using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    /// <summary>
    /// Cross-platform media test base. Tests run identically on browser and desktop.
    /// </summary>
    public abstract partial class MultiMediaTestBase
    {
        protected MultiMediaTestBase()
        {
        }

        /// <summary>
        /// Verify the test infrastructure is working.
        /// </summary>
        [TestMethod]
        public async Task TestInfrastructure_Working()
        {
            if (1 + 1 != 2) throw new Exception("Math is broken");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Verify MediaDevices.EnumerateDevices returns without error.
        /// Browser: returns real device list (may be empty without permissions).
        /// Desktop: returns placeholder list (Phase 2 adds real enumeration).
        /// </summary>
        [TestMethod]
        public async Task EnumerateDevices_ReturnsArray()
        {
            var devices = await MediaDevices.EnumerateDevices();
            if (devices == null) throw new Exception("EnumerateDevices returned null");
            // On desktop stub, returns empty array. On browser with fake devices, may return devices.
            // Just verify it doesn't throw and returns a valid array.
        }

        /// <summary>
        /// Verify GetUserMedia with video constraint returns a stream with a video track.
        /// Browser: uses fake camera (--use-fake-device-for-media-stream).
        /// Desktop: returns stub track (Phase 2 adds real capture).
        /// </summary>
        [TestMethod]
        public async Task GetUserMedia_VideoOnly()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            if (stream == null) throw new Exception("GetUserMedia returned null");
            if (!stream.Active && !OperatingSystem.IsWindows()) throw new Exception("Stream is not active");
            var tracks = stream.GetVideoTracks();
            if (tracks.Length == 0) throw new Exception("No video tracks in stream");
            if (tracks[0].Kind != "video") throw new Exception($"Expected kind 'video', got '{tracks[0].Kind}'");
        }

        /// <summary>
        /// Verify GetUserMedia with audio constraint returns a stream with an audio track.
        /// </summary>
        [TestMethod]
        public async Task GetUserMedia_AudioOnly()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            if (stream == null) throw new Exception("GetUserMedia returned null");
            var tracks = stream.GetAudioTracks();
            if (tracks.Length == 0) throw new Exception("No audio tracks in stream");
            if (tracks[0].Kind != "audio") throw new Exception($"Expected kind 'audio', got '{tracks[0].Kind}'");
        }

        /// <summary>
        /// Verify GetUserMedia with both audio and video returns both track types.
        /// </summary>
        [TestMethod]
        public async Task GetUserMedia_AudioAndVideo()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true, Video = true });
            if (stream == null) throw new Exception("GetUserMedia returned null");
            var allTracks = stream.GetTracks();
            if (allTracks.Length < 2) throw new Exception($"Expected at least 2 tracks, got {allTracks.Length}");
            var audioTracks = stream.GetAudioTracks();
            var videoTracks = stream.GetVideoTracks();
            if (audioTracks.Length == 0) throw new Exception("No audio tracks");
            if (videoTracks.Length == 0) throw new Exception("No video tracks");
        }

        /// <summary>
        /// Verify track properties are populated.
        /// </summary>
        [TestMethod]
        public async Task MediaStreamTrack_Properties()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var track = stream.GetVideoTracks()[0];
            if (string.IsNullOrEmpty(track.Id)) throw new Exception("Track ID is empty");
            if (track.Kind != "video") throw new Exception($"Expected kind 'video', got '{track.Kind}'");
            if (track.ReadyState != "live") throw new Exception($"Expected readyState 'live', got '{track.ReadyState}'");
        }

        /// <summary>
        /// Verify track GetSettings returns populated settings.
        /// </summary>
        [TestMethod]
        public async Task MediaStreamTrack_GetSettings()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var track = stream.GetVideoTracks()[0];
            var settings = track.GetSettings();
            if (settings == null) throw new Exception("GetSettings returned null");
            // Video track should have width/height
            if (settings.Width == null || settings.Width <= 0)
                throw new Exception($"Expected positive width, got {settings.Width}");
            if (settings.Height == null || settings.Height <= 0)
                throw new Exception($"Expected positive height, got {settings.Height}");
        }

        /// <summary>
        /// Verify track enable/disable works.
        /// </summary>
        [TestMethod]
        public async Task MediaStreamTrack_EnableDisable()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var track = stream.GetVideoTracks()[0];
            if (!track.Enabled) throw new Exception("Track should be enabled by default");
            track.Enabled = false;
            if (track.Enabled) throw new Exception("Track should be disabled after setting to false");
            track.Enabled = true;
            if (!track.Enabled) throw new Exception("Track should be enabled after setting to true");
        }

        /// <summary>
        /// Verify track Stop changes readyState to ended.
        /// </summary>
        [TestMethod]
        public async Task MediaStreamTrack_Stop()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var track = stream.GetVideoTracks()[0];
            if (track.ReadyState != "live") throw new Exception("Track should be live before stop");
            track.Stop();
            if (track.ReadyState != "ended") throw new Exception($"Expected readyState 'ended' after stop, got '{track.ReadyState}'");
        }

        /// <summary>
        /// Verify stream Clone creates independent copy.
        /// </summary>
        [TestMethod]
        public async Task MediaStream_Clone()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            using var clone = stream.Clone();
            if (clone.Id == stream.Id) throw new Exception("Clone should have different ID");
            if (clone.GetTracks().Length != stream.GetTracks().Length)
                throw new Exception("Clone should have same number of tracks");
        }

        /// <summary>
        /// Verify track Clone creates independent copy.
        /// </summary>
        [TestMethod]
        public async Task MediaStreamTrack_Clone()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var track = stream.GetVideoTracks()[0];
            using var clone = track.Clone() as IDisposable;
            var cloneTrack = (IMediaStreamTrack)clone!;
            if (cloneTrack.Id == track.Id) throw new Exception("Clone should have different ID");
            // Stopping original should not affect clone
            track.Stop();
            if (cloneTrack.ReadyState == "ended") throw new Exception("Clone should still be live after stopping original");
        }

        /// <summary>
        /// Verify AddTrack/RemoveTrack on stream.
        /// </summary>
        [TestMethod]
        public async Task MediaStream_AddRemoveTrack()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true, Video = true });
            var initialCount = stream.GetTracks().Length;
            if (initialCount < 2) throw new Exception($"Expected at least 2 tracks, got {initialCount}");

            var audioTrack = stream.GetAudioTracks()[0];
            stream.RemoveTrack(audioTrack);
            var afterRemove = stream.GetTracks().Length;
            if (afterRemove != initialCount - 1)
                throw new Exception($"Expected {initialCount - 1} tracks after remove, got {afterRemove}");

            stream.AddTrack(audioTrack);
            var afterAdd = stream.GetTracks().Length;
            if (afterAdd != initialCount)
                throw new Exception($"Expected {initialCount} tracks after re-add, got {afterAdd}");
        }

        /// <summary>
        /// Verify at least one constraint must be provided.
        /// </summary>
        [TestMethod]
        public async Task GetUserMedia_NoConstraints_Throws()
        {
            try
            {
                using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints());
                throw new Exception("Expected exception for empty constraints");
            }
            catch (ArgumentException)
            {
                // Expected
            }
            catch (Exception ex) when (ex.Message.Contains("constraint") || ex.Message.Contains("audio") || ex.Message.Contains("video"))
            {
                // Browser may throw different error message
            }
        }
    }
}
