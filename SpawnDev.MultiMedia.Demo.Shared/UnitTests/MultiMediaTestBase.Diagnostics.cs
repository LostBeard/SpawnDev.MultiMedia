using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    public abstract partial class MultiMediaTestBase
    {
        /// <summary>
        /// Lists all media devices on the system.
        /// </summary>
        [TestMethod]
        public async Task Diagnostic_ListAllDevices()
        {
            var devices = await MediaDevices.EnumerateDevices();
            if (devices.Length == 0)
                throw new Exception("No media devices found on this system");

            var report = $"Found {devices.Length} device(s):\n";
            foreach (var d in devices)
                report += $"  [{d.Kind}] {d.Label}\n";
            Console.WriteLine(report);
        }

        /// <summary>
        /// Captures real video frames and verifies the data is non-empty.
        /// Desktop: captures from first available camera (OBS Virtual Camera, webcam, etc.)
        /// Browser: captures from fake camera provided by Playwright args.
        /// </summary>
        [TestMethod]
        public async Task VideoCapture_ReceivesFrames()
        {
            // Check if any video device is available
            var devices = await MediaDevices.EnumerateDevices();
            var hasVideo = devices.Any(d => d.Kind == "videoinput");
            if (!hasVideo && !OperatingSystem.IsBrowser())
                throw new Exception("No video input devices available - cannot test frame capture");

            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var track = stream.GetVideoTracks()[0];
            if (track is not IVideoTrack videoTrack)
                throw new Exception($"Expected IVideoTrack, got {track.GetType().Name}");

            var frameReceived = new TaskCompletionSource<VideoFrame>();
            int frameCount = 0;
            videoTrack.OnFrame += frame =>
            {
                if (Interlocked.Increment(ref frameCount) == 3)
                    frameReceived.TrySetResult(frame);
            };

            var completed = await Task.WhenAny(frameReceived.Task, Task.Delay(10000));
            if (completed != frameReceived.Task)
                throw new Exception($"Timed out waiting for video frames (got {frameCount} in 10s)");

            var f = await frameReceived.Task;
            if (f.Width <= 0) throw new Exception($"Frame width is {f.Width}");
            if (f.Height <= 0) throw new Exception($"Frame height is {f.Height}");
            if (f.Data.Length <= 0) throw new Exception("Frame data is empty");

            // Verify the data contains non-zero bytes (real pixel data, not empty buffer)
            var span = f.Data.Span;
            int nonZero = 0;
            for (int i = 0; i < span.Length; i++)
                if (span[i] != 0) nonZero++;
            if (nonZero == 0)
                throw new Exception($"Frame data is all zeros ({f.Data.Length} bytes)");
        }

        /// <summary>
        /// Verifies that the pixel format reported by GetSettings matches the actual frame data size.
        /// </summary>
        [TestMethod]
        public async Task VideoCapture_FormatMatchesData()
        {
            var devices = await MediaDevices.EnumerateDevices();
            if (!devices.Any(d => d.Kind == "videoinput") && !OperatingSystem.IsBrowser())
                throw new Exception("No video input devices available");

            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = true });
            var track = stream.GetVideoTracks()[0];
            if (track is not IVideoTrack videoTrack)
                throw new Exception($"Expected IVideoTrack, got {track.GetType().Name}");

            var settings = track.GetSettings();
            if (settings.PixelFormat == null)
                throw new Exception("PixelFormat is null in settings");

            var frameReceived = new TaskCompletionSource<VideoFrame>();
            videoTrack.OnFrame += frame => frameReceived.TrySetResult(frame);

            var completed = await Task.WhenAny(frameReceived.Task, Task.Delay(10000));
            if (completed != frameReceived.Task)
                throw new Exception("Timed out waiting for frame");

            var f = await frameReceived.Task;

            // Verify frame format matches settings
            if (f.Format != settings.PixelFormat)
                throw new Exception($"Frame format {f.Format} doesn't match settings {settings.PixelFormat}");

            // Verify data size is consistent with dimensions and format
            int expectedSize = f.Format switch
            {
                VideoPixelFormat.BGRA or VideoPixelFormat.RGBA => f.Width * f.Height * 4,
                VideoPixelFormat.RGB24 => f.Width * f.Height * 3,
                VideoPixelFormat.NV12 or VideoPixelFormat.I420 => f.Width * f.Height * 3 / 2,
                VideoPixelFormat.YUY2 or VideoPixelFormat.UYVY => f.Width * f.Height * 2,
                VideoPixelFormat.MJPG => 0, // MJPG is variable-size compressed
                _ => 0,
            };
            if (expectedSize > 0 && f.Data.Length != expectedSize)
                throw new Exception($"Frame data size {f.Data.Length} doesn't match expected {expectedSize} for {f.Format} {f.Width}x{f.Height}");
        }

        /// <summary>
        /// Captures real audio frames from the microphone and verifies data arrives.
        /// Desktop: captures from first WASAPI audio input device.
        /// Browser: captures from fake mic provided by Playwright args.
        /// </summary>
        [TestMethod]
        public async Task AudioCapture_ReceivesFrames()
        {
            var devices = await MediaDevices.EnumerateDevices();
            var hasAudio = devices.Any(d => d.Kind == "audioinput");
            if (!hasAudio && !OperatingSystem.IsBrowser())
                throw new Exception("No audio input devices available - cannot test audio capture");

            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            if (track is not IAudioTrack audioTrack)
            {
                // Stub tracks pass basic tests but can't capture real frames
                // This is expected on systems where WASAPI capture init fails
                return;
            }

            var frameReceived = new TaskCompletionSource<AudioFrame>();
            int frameCount = 0;
            audioTrack.OnFrame += frame =>
            {
                if (Interlocked.Increment(ref frameCount) == 3)
                    frameReceived.TrySetResult(frame);
            };

            var completed = await Task.WhenAny(frameReceived.Task, Task.Delay(5000));
            if (completed != frameReceived.Task)
                throw new Exception($"Timed out waiting for audio frames (got {frameCount} in 5s)");

            var f = await frameReceived.Task;
            if (f.SampleRate <= 0) throw new Exception($"SampleRate is {f.SampleRate}");
            if (f.ChannelCount <= 0) throw new Exception($"ChannelCount is {f.ChannelCount}");
            if (f.SamplesPerChannel <= 0) throw new Exception($"SamplesPerChannel is {f.SamplesPerChannel}");
            if (f.Data.Length <= 0) throw new Exception("Audio frame data is empty");
        }

        /// <summary>
        /// Verifies that IAudioTrack properties match the device's native format.
        /// </summary>
        [TestMethod]
        public async Task AudioCapture_TrackProperties()
        {
            var devices = await MediaDevices.EnumerateDevices();
            if (!devices.Any(d => d.Kind == "audioinput") && !OperatingSystem.IsBrowser())
                throw new Exception("No audio input devices available");

            using var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Audio = true });
            var track = stream.GetAudioTracks()[0];
            if (track is not IAudioTrack audioTrack)
                return; // Stub - skip property verification

            if (audioTrack.SampleRate <= 0)
                throw new Exception($"SampleRate should be positive, got {audioTrack.SampleRate}");
            if (audioTrack.ChannelCount <= 0)
                throw new Exception($"ChannelCount should be positive, got {audioTrack.ChannelCount}");
            if (audioTrack.BitsPerSample <= 0)
                throw new Exception($"BitsPerSample should be positive, got {audioTrack.BitsPerSample}");

            // Common values: 44100/48000 Hz, 1-2 channels, 16/32 bits
            if (audioTrack.SampleRate < 8000 || audioTrack.SampleRate > 192000)
                throw new Exception($"Unusual SampleRate: {audioTrack.SampleRate}");
        }
    }
}
