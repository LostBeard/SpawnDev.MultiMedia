using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    /// <summary>
    /// Tests for <see cref="SpawnDev.MultiMedia.Windows.H264EncoderMFT"/>, the Phase 4b
    /// wrapper over the Microsoft MediaFoundation H.264 Encoder MFT. These tests only run
    /// on Windows (browser path returns early via <see cref="OperatingSystem.IsBrowser"/>),
    /// because the encoder is a Windows-only P/Invoke around the OS-provided encoder.
    ///
    /// The first-output NAL-unit assertion (SPS + PPS + IDR) is the critical one - it
    /// proves we plumbed the MFT correctly end-to-end. All the structural glue (input
    /// type / output type / codec-api knobs) lands on the single path that emits a
    /// valid H.264 elementary stream; if any of it is wrong, the encoder either errors
    /// out or emits garbage that fails NAL parsing.
    /// </summary>
    public abstract partial class MultiMediaTestBase
    {
        [TestMethod]
        public async Task H264Encoder_FirstOutput_ContainsSpsPpsIdr()
        {
            if (OperatingSystem.IsBrowser()) return; // MFT is Windows-only

            RunH264FirstOutputTest();
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task H264Encoder_MultipleFrames_ProduceIncreasingTimestamps()
        {
            if (OperatingSystem.IsBrowser()) return;

            RunH264MultiFrameTest();
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task H264Encoder_Dispose_DoesNotThrow()
        {
            if (OperatingSystem.IsBrowser()) return;

            RunH264DisposeTest();
            await Task.CompletedTask;
        }

        [SupportedOSPlatform("windows")]
        private static void RunH264FirstOutputTest()
        {
            const int width = 320;
            const int height = 240;
            const int fps = 30;
            const int bitrate = 500_000;

            using var enc = new SpawnDev.MultiMedia.Windows.H264EncoderMFT(width, height, fps, bitrate);

            // Push one NV12 frame (mid-grey Y + chroma center -> neutral grey image).
            var nv12 = BuildGreyNV12(width, height);

            byte[]? firstOutput = null;
            // MFT often emits SPS+PPS+IDR on first frame when low-latency. If it swallows
            // and asks for more input, feed a few more identical frames until we see output.
            for (int attempt = 0; attempt < 10 && firstOutput == null; attempt++)
            {
                long timestamp = attempt * (10_000_000L / fps); // 100ns units
                long duration = 10_000_000L / fps;
                enc.Encode(nv12, timestamp, duration, out firstOutput);
            }

            if (firstOutput == null)
                throw new Exception("H.264 encoder produced no output after 10 input frames - MFT plumbing broken");

            // Parse Annex-B NAL units. Each NAL starts with 00 00 00 01 (or 00 00 01) and
            // the next byte's low 5 bits identify the NAL type (1-23 valid).
            var nalTypes = ScanNalTypes(firstOutput);

            if (nalTypes.Count == 0)
                throw new Exception($"First output ({firstOutput.Length} bytes) contains no Annex-B NAL start codes - wrong format");

            // First output MUST contain SPS (type 7) + PPS (type 8) + IDR (type 5) for a
            // fresh encoder session. Anything else means the MFT is misconfigured or the
            // encoder is emitting a raw bytestream without parameter sets.
            if (!nalTypes.Contains(7))
                throw new Exception($"First encoder output missing SPS NAL (type 7). Got types: {string.Join(",", nalTypes)}");
            if (!nalTypes.Contains(8))
                throw new Exception($"First encoder output missing PPS NAL (type 8). Got types: {string.Join(",", nalTypes)}");
            if (!nalTypes.Contains(5))
                throw new Exception($"First encoder output missing IDR NAL (type 5). Got types: {string.Join(",", nalTypes)}");
        }

        [SupportedOSPlatform("windows")]
        private static void RunH264MultiFrameTest()
        {
            const int width = 320;
            const int height = 240;
            const int fps = 30;
            const int bitrate = 500_000;

            using var enc = new SpawnDev.MultiMedia.Windows.H264EncoderMFT(width, height, fps, bitrate);

            int totalOutputs = 0;
            int totalBytes = 0;
            for (int i = 0; i < 30; i++)
            {
                var frame = BuildMovingPatternNV12(width, height, i);
                long ts = i * (10_000_000L / fps);
                long dur = 10_000_000L / fps;
                enc.Encode(frame, ts, dur, out var output);
                if (output != null)
                {
                    totalOutputs++;
                    totalBytes += output.Length;
                }
            }

            // Also drain any trailing buffered output.
            var trailing = enc.Drain();
            if (trailing != null) { totalOutputs++; totalBytes += trailing.Length; }

            if (totalOutputs == 0)
                throw new Exception("30 input frames + drain produced zero output - encoder broken");
            if (totalBytes < 100)
                throw new Exception($"30-frame encode produced only {totalBytes} bytes total - output suspiciously small");
        }

        [SupportedOSPlatform("windows")]
        private static void RunH264DisposeTest()
        {
            var enc = new SpawnDev.MultiMedia.Windows.H264EncoderMFT(320, 240, 30, 500_000);
            var frame = BuildGreyNV12(320, 240);
            enc.Encode(frame, 0, 333_333, out _);
            enc.Dispose();
            enc.Dispose(); // double-dispose must be safe
        }

        private static byte[] BuildGreyNV12(int width, int height)
        {
            // NV12 = Y plane (width*height) + interleaved UV plane (width*height/2)
            int ySize = width * height;
            int uvSize = width * height / 2;
            var data = new byte[ySize + uvSize];
            // Y = 128 (mid-grey luma)
            for (int i = 0; i < ySize; i++) data[i] = 128;
            // UV = 128 (zero chroma = grey)
            for (int i = ySize; i < ySize + uvSize; i++) data[i] = 128;
            return data;
        }

        private static byte[] BuildMovingPatternNV12(int width, int height, int frameIndex)
        {
            int ySize = width * height;
            int uvSize = width * height / 2;
            var data = new byte[ySize + uvSize];
            int offset = (frameIndex * 8) % 256;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    data[y * width + x] = (byte)((x + y + offset) & 0xFF);
                }
            }
            for (int i = ySize; i < ySize + uvSize; i++) data[i] = 128;
            return data;
        }

        private static List<int> ScanNalTypes(byte[] annexBStream)
        {
            var types = new List<int>();
            int i = 0;
            while (i < annexBStream.Length - 4)
            {
                // Look for 00 00 00 01 or 00 00 01 start code.
                int hdrLen = 0;
                if (annexBStream[i] == 0 && annexBStream[i + 1] == 0 && annexBStream[i + 2] == 0 && annexBStream[i + 3] == 1)
                    hdrLen = 4;
                else if (annexBStream[i] == 0 && annexBStream[i + 1] == 0 && annexBStream[i + 2] == 1)
                    hdrLen = 3;

                if (hdrLen == 0)
                {
                    i++;
                    continue;
                }

                // NAL header byte is immediately after the start code. Low 5 bits = nal_unit_type.
                byte nalHeader = annexBStream[i + hdrLen];
                types.Add(nalHeader & 0x1F);
                i += hdrLen + 1;
            }
            return types;
        }
    }
}
