using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    /// <summary>
    /// Tests for the H.264 decoder (<see cref="SpawnDev.MultiMedia.Windows.H264DecoderMFT"/> behind <see cref="IVideoDecoder"/>): frames encoded by the library's own encoder come back
    /// out as pictures that look like what went in. Windows only (the decoder is the OS's Media Foundation MFT).
    /// </summary>
    public abstract partial class MultiMediaTestBase
    {
        /// <summary>A picture with structure a wrong decode would destroy: a horizontal luma ramp, a bright square, and chroma that differs by quadrant.</summary>
        private static byte[] BuildPatternNV12(int width, int height, int shift)
        {
            var nv12 = new byte[width * height * 3 / 2];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var luma = (x + shift) * 200 / width + 20;
                    if (x > width / 2 && x < width * 3 / 4 && y > height / 4 && y < height / 2) luma = 235;
                    nv12[y * width + x] = (byte)Math.Clamp(luma, 16, 235);
                }
            var uv = width * height;
            for (var y = 0; y < height / 2; y++)
                for (var x = 0; x < width / 2; x++)
                {
                    nv12[uv + y * width + x * 2] = (byte)(x < width / 4 ? 90 : 170);       // U
                    nv12[uv + y * width + x * 2 + 1] = (byte)(y < height / 4 ? 100 : 160); // V
                }
            return nv12;
        }

        [TestMethod]
        public async Task H264Decoder_RoundTrip_ReturnsThePictureThatWentIn()
        {
            if (OperatingSystem.IsBrowser()) return; // the MFT is Windows-only
            RunH264DecoderRoundTrip();
            await Task.CompletedTask;
        }

        [SupportedOSPlatform("windows")]
        private static void RunH264DecoderRoundTrip()
        {
            const int width = 320, height = 240, fps = 30;
            using var encoder = VideoEncoderFactory.CreateH264(width, height, fps, 2_000_000);
            using var decoder = VideoDecoderFactory.CreateH264();
            if (decoder.Codec != "h264" || decoder.PixelFormat != VideoPixelFormat.NV12) throw new Exception($"decoder reports {decoder.Codec} / {decoder.PixelFormat}");

            var source = BuildPatternNV12(width, height, 0);
            var decoded = new List<VideoFrame>();
            for (var i = 0; i < 20; i++)
            {
                var au = encoder.Encode(source, i * 333_333L, 333_333L);
                if (au == null) continue;
                decoded.AddRange(decoder.Decode(au, i * 333_333L));
            }
            var drained = encoder.Drain();
            if (drained != null) decoded.AddRange(decoder.Decode(drained, 20 * 333_333L));

            if (decoded.Count == 0) throw new Exception("the decoder returned no frames for 20 encoded frames");
            var last = decoded[^1];
            if (last.Width != width || last.Height != height) throw new Exception($"decoded {last.Width}x{last.Height}, expected {width}x{height}");
            if (last.Format != VideoPixelFormat.NV12) throw new Exception($"decoded format {last.Format}");
            if (last.Data.Length != width * height * 3 / 2) throw new Exception($"decoded {last.Data.Length} bytes, expected {width * height * 3 / 2}");

            // Lossy codec, 2 Mbit/s on a flat picture: the luma should be within a few levels of the source on average, and the chroma should keep its quadrants.
            var got = last.Data.Span;
            double lumaError = 0;
            for (var i = 0; i < width * height; i++) lumaError += Math.Abs(got[i] - source[i]);
            lumaError /= width * height;
            if (lumaError > 6) throw new Exception($"mean luma error {lumaError:0.0} levels after the round trip (expected under 6)");

            var uvBase = width * height;
            var topLeftU = got[uvBase + 10 * width + 10 * 2];                   // quadrant U = 90
            var topRightU = got[uvBase + 10 * width + (width / 2 - 10) * 2];    // quadrant U = 170
            if (!(topRightU - topLeftU > 40)) throw new Exception($"chroma quadrants lost: U {topLeftU} vs {topRightU} (expected about 90 vs 170)");
            Console.WriteLine($"[H264Decoder] {decoded.Count} frames of {width}x{height}, mean luma error {lumaError:0.00}, chroma U {topLeftU}/{topRightU}");
        }

        [TestMethod]
        public async Task H264Decoder_RecoversWhenAValidStreamFollowsGarbage()
        {
            if (OperatingSystem.IsBrowser()) return;
            RunH264DecoderNeedsKeyframe();
            await Task.CompletedTask;
        }

        [SupportedOSPlatform("windows")]
        private static void RunH264DecoderNeedsKeyframe()
        {
            const int width = 320, height = 240;
            using var encoder = VideoEncoderFactory.CreateH264(width, height, 30, 1_000_000);
            using var decoder = VideoDecoderFactory.CreateH264();
            var units = new List<byte[]>();
            for (var i = 0; i < 12; i++)
            {
                var au = encoder.Encode(BuildPatternNV12(width, height, i * 4), i * 333_333L, 333_333L);
                if (au != null) units.Add(au);
            }
            if (units.Count < 3) throw new Exception($"the encoder produced only {units.Count} access units");

            // Garbage, then the real stream from its first unit (keyframe with parameter sets): the decoder must not throw and must recover.
            var garbage = new byte[] { 0, 0, 0, 1, 0x41, 0x9A, 0x24, 0x6C, 0x41, 0xAF };
            decoder.Decode(garbage, 0);
            var decoded = new List<VideoFrame>();
            for (var i = 0; i < units.Count; i++) decoded.AddRange(decoder.Decode(units[i], i * 333_333L));
            if (decoded.Count == 0) throw new Exception("no frames decoded after garbage followed by a valid stream");
        }
    }
}
