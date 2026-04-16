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

        // ---- YUY2 Conversion ----

        [TestMethod]
        public async Task PixelConvert_YUY2toI420_KnownPattern()
        {
            // 4x2 YUY2 frame: [Y0 U0 Y1 V0] per macro-pixel pair
            // Row 0: pixel(0,0)=Y:16,U:100 pixel(1,0)=Y:20,V:200 | pixel(2,0)=Y:24,U:110 pixel(3,0)=Y:28,V:210
            // Row 1: pixel(0,1)=Y:32,U:120 pixel(1,1)=Y:36,V:180 | pixel(2,1)=Y:40,U:130 pixel(3,1)=Y:44,V:190
            int w = 4, h = 2;
            var yuy2 = new byte[]
            {
                // Row 0: 2 macro-pixels = 8 bytes
                16, 100, 20, 200,   // Y0=16, U=100, Y1=20, V=200
                24, 110, 28, 210,   // Y0=24, U=110, Y1=28, V=210
                // Row 1: 2 macro-pixels = 8 bytes
                32, 120, 36, 180,   // Y0=32, U=120, Y1=36, V=180
                40, 130, 44, 190,   // Y0=40, U=130, Y1=44, V=190
            };

            var i420 = new byte[w * h * 3 / 2]; // 12 bytes
            PixelFormatConverter.YUY2toI420(yuy2, i420, w, h);

            // Y plane: all 8 luma values in raster order
            byte[] expectedY = { 16, 20, 24, 28, 32, 36, 40, 44 };
            for (int i = 0; i < 8; i++)
                if (i420[i] != expectedY[i])
                    throw new Exception($"Y[{i}] expected {expectedY[i]}, got {i420[i]}");

            // U plane (2 values, only from even rows for 4:2:0 subsampling): row 0 only
            // U comes from row 0 macro-pixels: 100, 110
            if (i420[8] != 100) throw new Exception($"U[0] expected 100, got {i420[8]}");
            if (i420[9] != 110) throw new Exception($"U[1] expected 110, got {i420[9]}");

            // V plane: row 0 macro-pixels: 200, 210
            if (i420[10] != 200) throw new Exception($"V[0] expected 200, got {i420[10]}");
            if (i420[11] != 210) throw new Exception($"V[1] expected 210, got {i420[11]}");

            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_YUY2toBGRA_KnownPattern()
        {
            // Single macro-pixel: white (Y=235, U=128, V=128)
            int w = 2, h = 2;
            var yuy2 = new byte[]
            {
                235, 128, 235, 128, // Row 0: both pixels white
                235, 128, 235, 128, // Row 1: both pixels white
            };

            var bgra = new byte[w * h * 4];
            PixelFormatConverter.YUY2toBGRA(yuy2, bgra, w, h);

            // All 4 pixels should be near-white
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
        public async Task PixelConvert_YUY2toI420_LargerFrame()
        {
            // 320x240 frame with gradient pattern, verify via roundtrip to BGRA
            int w = 320, h = 240;
            var yuy2 = new byte[w * h * 2];
            // Fill with a pattern: Y gradient, U=128, V=128 (grayscale)
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i += 2)
                {
                    int idx = (j * w + i) * 2;
                    yuy2[idx] = (byte)((j + i) % 256);       // Y0
                    yuy2[idx + 1] = 128;                       // U
                    yuy2[idx + 2] = (byte)((j + i + 1) % 256); // Y1
                    yuy2[idx + 3] = 128;                       // V
                }
            }

            var i420 = new byte[w * h * 3 / 2];
            PixelFormatConverter.YUY2toI420(yuy2, i420, w, h);

            // Verify Y values match what we put in
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    byte expectedY = (byte)((j + i) % 256);
                    if (i420[j * w + i] != expectedY)
                        throw new Exception($"Y[{i},{j}] expected {expectedY}, got {i420[j * w + i]}");
                }
            }

            // Verify I420 can convert to BGRA without crashing (pipeline integration)
            var bgra = new byte[w * h * 4];
            PixelFormatConverter.I420toBGRA(i420, bgra, w, h);
            // Grayscale: R ~= G ~= B for each pixel
            int maxDiff = 0;
            for (int p = 0; p < w * h; p++)
            {
                int r = bgra[p * 4 + 2], g = bgra[p * 4 + 1], b = bgra[p * 4];
                int diff = Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(g - b), Math.Abs(r - b)));
                if (diff > maxDiff) maxDiff = diff;
            }
            // BT.601 with U=V=128 should produce near-equal RGB channels
            if (maxDiff > 2)
                throw new Exception($"Grayscale max channel diff = {maxDiff}, expected <= 2");

            await Task.CompletedTask;
        }

        // ---- UYVY Conversion ----

        [TestMethod]
        public async Task PixelConvert_UYVYtoI420_KnownPattern()
        {
            // 4x2 UYVY frame: [U0 Y0 V0 Y1] per macro-pixel pair (opposite byte order from YUY2)
            int w = 4, h = 2;
            var uyvy = new byte[]
            {
                // Row 0: 2 macro-pixels = 8 bytes
                100, 16, 200, 20,   // U=100, Y0=16, V=200, Y1=20
                110, 24, 210, 28,   // U=110, Y0=24, V=210, Y1=28
                // Row 1: 2 macro-pixels = 8 bytes
                120, 32, 180, 36,   // U=120, Y0=32, V=180, Y1=36
                130, 40, 190, 44,   // U=130, Y0=40, V=190, Y1=44
            };

            var i420 = new byte[w * h * 3 / 2]; // 12 bytes
            PixelFormatConverter.UYVYtoI420(uyvy, i420, w, h);

            // Y plane: same luma values as YUY2 test, just different byte positions
            byte[] expectedY = { 16, 20, 24, 28, 32, 36, 40, 44 };
            for (int i = 0; i < 8; i++)
                if (i420[i] != expectedY[i])
                    throw new Exception($"Y[{i}] expected {expectedY[i]}, got {i420[i]}");

            // U plane (from even rows only): 100, 110
            if (i420[8] != 100) throw new Exception($"U[0] expected 100, got {i420[8]}");
            if (i420[9] != 110) throw new Exception($"U[1] expected 110, got {i420[9]}");

            // V plane: 200, 210
            if (i420[10] != 200) throw new Exception($"V[0] expected 200, got {i420[10]}");
            if (i420[11] != 210) throw new Exception($"V[1] expected 210, got {i420[11]}");

            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task PixelConvert_UYVYtoI420_MatchesYUY2()
        {
            // Same logical pixel data in YUY2 and UYVY encoding should produce identical I420 output
            int w = 64, h = 48;
            var yuy2 = new byte[w * h * 2];
            var uyvy = new byte[w * h * 2];

            // Fill both with equivalent data (same Y/U/V, different byte order)
            var rng = new Random(77);
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i += 2)
                {
                    byte y0 = (byte)rng.Next(256), y1 = (byte)rng.Next(256);
                    byte u = (byte)rng.Next(256), v = (byte)rng.Next(256);
                    int idx = (j * w + i) * 2;

                    // YUY2: Y0 U Y1 V
                    yuy2[idx] = y0; yuy2[idx + 1] = u; yuy2[idx + 2] = y1; yuy2[idx + 3] = v;
                    // UYVY: U Y0 V Y1
                    uyvy[idx] = u; uyvy[idx + 1] = y0; uyvy[idx + 2] = v; uyvy[idx + 3] = y1;
                }
            }

            var i420FromYuy2 = new byte[w * h * 3 / 2];
            var i420FromUyvy = new byte[w * h * 3 / 2];
            PixelFormatConverter.YUY2toI420(yuy2, i420FromYuy2, w, h);
            PixelFormatConverter.UYVYtoI420(uyvy, i420FromUyvy, w, h);

            // Must be byte-exact identical
            int mismatches = 0;
            for (int i = 0; i < i420FromYuy2.Length; i++)
                if (i420FromYuy2[i] != i420FromUyvy[i]) mismatches++;

            if (mismatches > 0)
                throw new Exception($"{mismatches} byte mismatches between YUY2->I420 and UYVY->I420 with equivalent input");

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
