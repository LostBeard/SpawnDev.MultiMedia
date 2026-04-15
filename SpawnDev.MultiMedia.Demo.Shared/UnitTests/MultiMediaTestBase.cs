using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    /// <summary>
    /// Cross-platform media test base. Tests run identically on browser and desktop.
    /// Every test hits real production code - no mocks, no identity values.
    /// </summary>
    public abstract partial class MultiMediaTestBase
    {
        protected MultiMediaTestBase()
        {
        }

        [TestMethod]
        public async Task TestInfrastructure_Working()
        {
            if (1 + 1 != 2) throw new Exception("Math is broken");
            await Task.CompletedTask;
        }

        // ---- Device Enumeration ----

        [TestMethod]
        public async Task EnumerateDevices_ReturnsNonNullArray()
        {
            var devices = await MediaDevices.EnumerateDevices();
            if (devices == null) throw new Exception("EnumerateDevices returned null");
        }

        [TestMethod]
        public async Task EnumerateDevices_DevicesHaveKind()
        {
            var devices = await MediaDevices.EnumerateDevices();
            foreach (var d in devices)
            {
                if (string.IsNullOrEmpty(d.Kind))
                    throw new Exception($"Device '{d.Label}' has empty Kind");
                if (d.Kind != "videoinput" && d.Kind != "audioinput" && d.Kind != "audiooutput")
                    throw new Exception($"Device '{d.Label}' has unexpected Kind '{d.Kind}'");
            }
        }

        [TestMethod]
        public async Task EnumerateDevices_DevicesHaveDeviceId()
        {
            var devices = await MediaDevices.EnumerateDevices();
            foreach (var d in devices)
            {
                if (string.IsNullOrEmpty(d.DeviceId))
                    throw new Exception($"Device '{d.Label}' has empty DeviceId");
            }
        }

        [TestMethod]
        public async Task EnumerateDevices_DevicesHaveLabel()
        {
            var devices = await MediaDevices.EnumerateDevices();
            // On desktop, labels should always be populated
            // On browser without permission, labels may be empty (that's spec-correct)
            if (!OperatingSystem.IsBrowser())
            {
                foreach (var d in devices)
                {
                    if (string.IsNullOrEmpty(d.Label))
                        throw new Exception($"Desktop device with ID '{d.DeviceId}' has empty Label");
                }
            }
        }

        [TestMethod]
        public async Task EnumerateDevices_DeviceIdsAreUnique()
        {
            var devices = await MediaDevices.EnumerateDevices();
            if (devices.Length < 2) return; // Need at least 2 to test uniqueness
            var ids = new HashSet<string>();
            foreach (var d in devices)
            {
                if (!ids.Add(d.DeviceId))
                    throw new Exception($"Duplicate DeviceId: {d.DeviceId}");
            }
        }

        // ---- GetUserMedia - Audio ----

        [TestMethod]
        public async Task GetUserMedia_AudioOnly_ReturnsStream()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            if (stream == null) throw new Exception("GetUserMedia returned null");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioOnly_HasAudioTrack()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var tracks = stream.GetAudioTracks();
            if (tracks.Length == 0) throw new Exception("No audio tracks in stream");
            if (tracks[0].Kind != "audio") throw new Exception($"Expected kind 'audio', got '{tracks[0].Kind}'");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioOnly_NoVideoTracks()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var videoTracks = stream.GetVideoTracks();
            if (videoTracks.Length > 0) throw new Exception($"Audio-only stream should have 0 video tracks, got {videoTracks.Length}");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioTrack_HasId()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            if (string.IsNullOrEmpty(track.Id)) throw new Exception("Audio track ID is empty");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioTrack_HasLabel()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            if (string.IsNullOrEmpty(track.Label)) throw new Exception("Audio track Label is empty");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioTrack_IsLive()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            if (track.ReadyState != "live")
                throw new Exception($"Expected readyState 'live', got '{track.ReadyState}'");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioTrack_EnabledByDefault()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            if (!track.Enabled) throw new Exception("Audio track should be enabled by default");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioTrack_ToggleEnabled()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            track.Enabled = false;
            if (track.Enabled) throw new Exception("Track should be disabled");
            track.Enabled = true;
            if (!track.Enabled) throw new Exception("Track should be re-enabled");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioTrack_StopEndsTrack()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            track.Stop();
            if (track.ReadyState != "ended")
                throw new Exception($"Expected 'ended' after Stop, got '{track.ReadyState}'");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioTrack_GetSettings_HasSampleRate()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            var settings = track.GetSettings();
            if (settings.SampleRate == null || settings.SampleRate <= 0)
                throw new Exception($"Expected positive SampleRate, got {settings.SampleRate}");
        }

        [TestMethod]
        public async Task GetUserMedia_AudioTrack_GetSettings_HasChannelCount()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            var settings = track.GetSettings();
            if (settings.ChannelCount == null || settings.ChannelCount <= 0)
                throw new Exception($"Expected positive ChannelCount, got {settings.ChannelCount}");
        }

        // ---- GetUserMedia - Video ----

        [TestMethod]
        public async Task GetUserMedia_VideoOnly_ReturnsStream()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            if (stream == null) throw new Exception("GetUserMedia returned null");
            var tracks = stream.GetVideoTracks();
            if (tracks.Length == 0) throw new Exception("No video tracks in stream");
            if (tracks[0].Kind != "video") throw new Exception($"Expected kind 'video', got '{tracks[0].Kind}'");
        }

        [TestMethod]
        public async Task GetUserMedia_VideoOnly_NoAudioTracks()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var audioTracks = stream.GetAudioTracks();
            if (audioTracks.Length > 0) throw new Exception($"Video-only stream should have 0 audio tracks, got {audioTracks.Length}");
        }

        [TestMethod]
        public async Task GetUserMedia_VideoTrack_GetSettings_HasDimensions()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var track = stream.GetVideoTracks()[0];
            var settings = track.GetSettings();
            if (settings.Width == null || settings.Width <= 0)
                throw new Exception($"Expected positive Width, got {settings.Width}");
            if (settings.Height == null || settings.Height <= 0)
                throw new Exception($"Expected positive Height, got {settings.Height}");
        }

        // ---- GetUserMedia - Combined ----

        [TestMethod]
        public async Task GetUserMedia_AudioAndVideo_HasBothTrackTypes()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true, Video = true });
            var allTracks = stream.GetTracks();
            if (allTracks.Length < 2) throw new Exception($"Expected at least 2 tracks, got {allTracks.Length}");
            if (stream.GetAudioTracks().Length == 0) throw new Exception("No audio tracks");
            if (stream.GetVideoTracks().Length == 0) throw new Exception("No video tracks");
        }

        // ---- Stream Operations ----

        [TestMethod]
        public async Task MediaStream_HasUniqueId()
        {
            using var stream1 = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            using var stream2 = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            if (stream1.Id == stream2.Id) throw new Exception("Two streams should have different IDs");
        }

        [TestMethod]
        public async Task MediaStream_GetTracks_MatchesAudioPlusVideo()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var all = stream.GetTracks();
            var audio = stream.GetAudioTracks();
            var video = stream.GetVideoTracks();
            if (all.Length != audio.Length + video.Length)
                throw new Exception($"GetTracks ({all.Length}) != GetAudioTracks ({audio.Length}) + GetVideoTracks ({video.Length})");
        }

        [TestMethod]
        public async Task MediaStream_GetTrackById_FindsTrack()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            var found = stream.GetTrackById(track.Id);
            if (found == null) throw new Exception($"GetTrackById({track.Id}) returned null");
            if (found.Id != track.Id) throw new Exception("Found track has different ID");
        }

        [TestMethod]
        public async Task MediaStream_GetTrackById_ReturnsNullForBadId()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var found = stream.GetTrackById("nonexistent-id-12345");
            if (found != null) throw new Exception("Expected null for nonexistent track ID");
        }

        [TestMethod]
        public async Task MediaStream_RemoveTrack_RemovesIt()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            var before = stream.GetTracks().Length;
            stream.RemoveTrack(track);
            var after = stream.GetTracks().Length;
            if (after != before - 1)
                throw new Exception($"Expected {before - 1} tracks after remove, got {after}");
        }

        [TestMethod]
        public async Task MediaStream_AddTrack_AddsIt()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            stream.RemoveTrack(track);
            var before = stream.GetTracks().Length;
            stream.AddTrack(track);
            var after = stream.GetTracks().Length;
            if (after != before + 1)
                throw new Exception($"Expected {before + 1} tracks after add, got {after}");
        }

        [TestMethod]
        public async Task MediaStream_Clone_HasDifferentId()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            using var clone = stream.Clone();
            if (clone.Id == stream.Id) throw new Exception("Clone should have different ID");
        }

        [TestMethod]
        public async Task MediaStream_Clone_HasSameTrackCount()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            using var clone = stream.Clone();
            if (clone.GetTracks().Length != stream.GetTracks().Length)
                throw new Exception("Clone should have same number of tracks");
        }

        // ---- Track Operations ----

        [TestMethod]
        public async Task MediaStreamTrack_Clone_HasDifferentId()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            var clone = track.Clone();
            if (clone.Id == track.Id) throw new Exception("Cloned track should have different ID");
            clone.Dispose();
        }

        [TestMethod]
        public async Task MediaStreamTrack_Clone_IsIndependent()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            var clone = track.Clone();
            track.Stop();
            // Clone should remain live even after original is stopped
            if (clone.ReadyState == "ended")
                throw new Exception("Clone should still be live after stopping original");
            clone.Dispose();
        }

        [TestMethod]
        public async Task MediaStreamTrack_ContentHint_DefaultEmpty()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            if (track.ContentHint == null)
                throw new Exception("ContentHint should not be null (empty string is valid)");
        }

        [TestMethod]
        public async Task MediaStreamTrack_NotMutedByDefault()
        {
            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            if (track.Muted) throw new Exception("Track should not be muted by default");
        }

        // ---- Error Handling ----

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
                // Expected on desktop
            }
            catch (Exception ex) when (ex.Message != "Expected exception for empty constraints")
            {
                // Browser may throw different error type
            }
        }

        // ---- Dispose Behavior ----

        [TestMethod]
        public async Task MediaStream_Dispose_StopsTracks()
        {
            var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            stream.Dispose();
            if (track.ReadyState != "ended")
                throw new Exception($"Track should be ended after stream dispose, got '{track.ReadyState}'");
        }

        [TestMethod]
        public async Task MediaStream_DoubleDispose_NoThrow()
        {
            var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            stream.Dispose();
            stream.Dispose(); // Should not throw
        }
    }
}
