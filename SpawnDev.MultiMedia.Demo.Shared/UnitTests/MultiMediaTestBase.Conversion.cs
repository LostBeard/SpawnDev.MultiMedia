using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    public abstract partial class MultiMediaTestBase
    {
        [TestMethod]
        public async Task PixelConvert_GetFrameSize_BGRA()
        {
            var size = PixelFormatConverter.GetFrameSize(VideoPixelFormat.BGRA, 1920, 1080);
            if (size != 1920 * 1080 * 4) throw new Exception($"Expected {1920 * 1080 * 4}, got {size}");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_GetFrameSize_NV12()
        {
            var size = PixelFormatConverter.GetFrameSize(VideoPixelFormat.NV12, 1920, 1080);
            if (size != 1920 * 1080 * 3 / 2) throw new Exception($"Expected {1920 * 1080 * 3 / 2}, got {size}");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_NV12toI420_PreservesY()
        {
            // 4x2 test frame: Y plane is [16..23], UV is interleaved
            int w = 4, h = 2;
            var nv12 = new byte[w * h * 3 / 2]; // 12 bytes
            // Y plane: fill with distinct values
            for (int i = 0; i < w * h; i++) nv12[i] = (byte)(16 + i);
            // UV interleaved: U=128 V=128 (gray) for 2 pairs
            nv12[8] = 100; nv12[9] = 200; // U0,V0
            nv12[10] = 110; nv12[11] = 210; // U1,V1

            var i420 = new byte[w * h * 3 / 2];
            PixelFormatConverter.NV12toI420(nv12, i420, w, h);

            // Y plane should be identical
            for (int i = 0; i < w * h; i++)
                if (i420[i] != nv12[i])
                    throw new Exception($"Y plane mismatch at {i}: expected {nv12[i]}, got {i420[i]}");

            // U plane (after Y): should be deinterleaved
            if (i420[8] != 100 || i420[9] != 110)
                throw new Exception($"U plane wrong: [{i420[8]}, {i420[9]}], expected [100, 110]");
            // V plane (after U): should be deinterleaved
            if (i420[10] != 200 || i420[11] != 210)
                throw new Exception($"V plane wrong: [{i420[10]}, {i420[11]}], expected [200, 210]");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_NV12toI420_Roundtrip()
        {
            // Create NV12, convert to I420, verify sizes and Y preservation
            int w = 320, h = 240;
            var nv12 = new byte[w * h * 3 / 2];
            // Fill Y with gradient
            for (int i = 0; i < w * h; i++) nv12[i] = (byte)(i % 256);
            // Fill UV with test pattern
            for (int i = w * h; i < nv12.Length; i++) nv12[i] = (byte)((i * 7) % 256);

            var i420 = new byte[w * h * 3 / 2];
            PixelFormatConverter.NV12toI420(nv12, i420, w, h);

            // Same total size
            if (i420.Length != nv12.Length)
                throw new Exception($"Size mismatch: NV12={nv12.Length}, I420={i420.Length}");

            // Y plane preserved exactly
            for (int i = 0; i < w * h; i++)
                if (i420[i] != nv12[i])
                    throw new Exception($"Y mismatch at {i}");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_NV12toBGRA_WhitePixel()
        {
            // White in YUV: Y=235, U=128, V=128
            int w = 2, h = 2;
            var nv12 = new byte[w * h * 3 / 2]; // 6 bytes
            nv12[0] = nv12[1] = nv12[2] = nv12[3] = 235; // Y
            nv12[4] = 128; nv12[5] = 128; // U, V (one pair for 2x2 block)

            var bgra = new byte[w * h * 4]; // 16 bytes
            PixelFormatConverter.NV12toBGRA(nv12, bgra, w, h);

            // Should be near-white: R,G,B all close to 235
            for (int p = 0; p < w * h; p++)
            {
                int b = bgra[p * 4], g = bgra[p * 4 + 1], r = bgra[p * 4 + 2], a = bgra[p * 4 + 3];
                if (r < 220 || g < 220 || b < 220)
                    throw new Exception($"Pixel {p} not white enough: R={r} G={g} B={b}");
                if (a != 255) throw new Exception($"Alpha should be 255, got {a}");
            }
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_BGRAtoI420_Roundtrip()
        {
            // BGRA -> I420 -> BGRA should preserve approximate colors
            int w = 4, h = 4;
            var bgra = new byte[w * h * 4];
            // Red pixel pattern
            for (int i = 0; i < w * h; i++)
            {
                bgra[i * 4] = 0;       // B
                bgra[i * 4 + 1] = 0;   // G
                bgra[i * 4 + 2] = 255; // R
                bgra[i * 4 + 3] = 255; // A
            }

            var i420 = new byte[w * h * 3 / 2];
            PixelFormatConverter.BGRAtoI420(bgra, i420, w, h);

            var bgra2 = new byte[w * h * 4];
            PixelFormatConverter.I420toBGRA(i420, bgra2, w, h);

            // Check that red is still dominant (YUV round-trip has some loss)
            for (int i = 0; i < w * h; i++)
            {
                int r = bgra2[i * 4 + 2], g = bgra2[i * 4 + 1], b = bgra2[i * 4];
                if (r < 200) throw new Exception($"Red channel too low after roundtrip: {r}");
                if (g > 50) throw new Exception($"Green channel too high after roundtrip: {g}");
                if (b > 50) throw new Exception($"Blue channel too high after roundtrip: {b}");
            }
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_SameFormatReturnsOriginal()
        {
            var frame = new VideoFrame(4, 4, VideoPixelFormat.NV12,
                new byte[4 * 4 * 3 / 2], 0);
            var result = PixelFormatConverter.Convert(frame, VideoPixelFormat.NV12);
            // Should return the same frame object (no conversion needed)
            if (!ReferenceEquals(result, frame))
                throw new Exception("Same-format conversion should return the original frame");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_RGB24toBGRA()
        {
            // RGB24: [R, G, B] per pixel -> BGRA: [B, G, R, A]
            int w = 2, h = 1;
            var rgb24 = new byte[] { 255, 0, 0, 0, 255, 0 }; // red, green
            var bgra = new byte[w * h * 4];
            PixelFormatConverter.RGB24toBGRA(rgb24, bgra, w, h);

            // Pixel 0: R=255,G=0,B=0 -> BGRA=[0,0,255,255]
            if (bgra[0] != 0 || bgra[1] != 0 || bgra[2] != 255 || bgra[3] != 255)
                throw new Exception($"Pixel 0 wrong: [{bgra[0]},{bgra[1]},{bgra[2]},{bgra[3]}]");
            // Pixel 1: R=0,G=255,B=0 -> BGRA=[0,255,0,255]
            if (bgra[4] != 0 || bgra[5] != 255 || bgra[6] != 0 || bgra[7] != 255)
                throw new Exception($"Pixel 1 wrong: [{bgra[4]},{bgra[5]},{bgra[6]},{bgra[7]}]");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_VideoFrame_NV12toBGRA()
        {
            // Test the high-level VideoFrame conversion
            int w = 4, h = 4;
            var nv12Data = new byte[w * h * 3 / 2];
            for (int i = 0; i < nv12Data.Length; i++) nv12Data[i] = 128; // mid-gray

            var source = new VideoFrame(w, h, VideoPixelFormat.NV12,
                new ReadOnlyMemory<byte>(nv12Data), 12345);

            var converted = PixelFormatConverter.Convert(source, VideoPixelFormat.BGRA);

            if (converted.Width != w) throw new Exception($"Width mismatch");
            if (converted.Height != h) throw new Exception($"Height mismatch");
            if (converted.Format != VideoPixelFormat.BGRA) throw new Exception($"Format should be BGRA");
            if (converted.Timestamp != 12345) throw new Exception($"Timestamp not preserved");
            if (converted.Data.Length != w * h * 4)
                throw new Exception($"Data size should be {w * h * 4}, got {converted.Data.Length}");
            await Task.CompletedTask;
        }

        // ---- Audio Format Conversion ----

        [TestMethod]
        public async Task AudioConvert_Float32ToPcm16_Silence()
        {
            // Silence in float32 = all zeros -> silence in PCM16 = all zeros
            var float32 = new byte[16]; // 4 samples of silence
            var pcm16 = AudioFormatConverter.Float32ToPcm16(float32);
            if (pcm16.Length != 8) throw new Exception($"Expected 8 bytes, got {pcm16.Length}");
            for (int i = 0; i < pcm16.Length; i++)
                if (pcm16[i] != 0) throw new Exception($"Expected silence, byte {i} = {pcm16[i]}");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task AudioConvert_Float32ToPcm16_FullScale()
        {
            // 1.0f in float32 -> 32767 in int16 (0xFF7F in LE)
            var float32 = new byte[4];
            BitConverter.TryWriteBytes(float32, 1.0f);
            var pcm16 = AudioFormatConverter.Float32ToPcm16(float32);
            short sample = BitConverter.ToInt16(pcm16);
            if (sample != 32767) throw new Exception($"Expected 32767, got {sample}");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task AudioConvert_Float32ToPcm16_NegativeFullScale()
        {
            // -1.0f -> -32767
            var float32 = new byte[4];
            BitConverter.TryWriteBytes(float32, -1.0f);
            var pcm16 = AudioFormatConverter.Float32ToPcm16(float32);
            short sample = BitConverter.ToInt16(pcm16);
            if (sample != -32767) throw new Exception($"Expected -32767, got {sample}");
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task AudioConvert_Pcm16ToFloat32_Roundtrip()
        {
            // PCM16 -> Float32 -> PCM16 should preserve values (within rounding)
            var pcm16 = new byte[] { 0x00, 0x40 }; // 16384 in LE
            var float32 = AudioFormatConverter.Pcm16ToFloat32(pcm16);
            var back = AudioFormatConverter.Float32ToPcm16(float32);
            short original = BitConverter.ToInt16(pcm16);
            short roundtrip = BitConverter.ToInt16(back);
            if (Math.Abs(original - roundtrip) > 1)
                throw new Exception($"Roundtrip mismatch: {original} -> {roundtrip}");
            await Task.CompletedTask;
        }
    }
}
