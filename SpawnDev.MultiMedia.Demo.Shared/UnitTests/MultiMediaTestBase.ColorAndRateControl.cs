using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    public abstract partial class MultiMediaTestBase
    {
        // ---- BT.601 limited range (2.6.0): what cameras and H.264 decoders deliver, and what the H.264 encoder expects ----

        [TestMethod]
        public async Task PixelConvert_LimitedRange_BlackAndWhiteAreExact()
        {
            // Y 16 is black and Y 235 white in video range; U = V = 128 is grey: every YUV -> BGRA path must give 0 and 255 exactly.
            const int w = 2, h = 2;
            foreach (var (luma, expected) in new[] { (16, 0), (235, 255), (126, 128) })
            {
                var nv12 = new byte[w * h * 3 / 2];
                for (int i = 0; i < 4; i++) nv12[i] = (byte)luma;
                nv12[4] = nv12[5] = 128;
                var bgra = new byte[w * h * 4];
                PixelFormatConverter.NV12toBGRA(nv12, bgra, w, h);
                for (int p = 0; p < w * h; p++)
                    for (int c = 0; c < 3; c++)
                        if (bgra[p * 4 + c] != expected) throw new Exception($"Y={luma}: pixel {p} channel {c} = {bgra[p * 4 + c]}, expected {expected}");
            }
            await Task.CompletedTask;
        }

        /// <summary>A smooth test picture (gradients in every channel), BGRA.</summary>
        private static byte[] BuildSmoothBGRA(int w, int h)
        {
            var bgra = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = (y * w + x) * 4;
                    bgra[p] = (byte)(40 + 160 * x / w);                    // B
                    bgra[p + 1] = (byte)(30 + 180 * y / h);                // G
                    bgra[p + 2] = (byte)(200 - 120 * (x + y) / (w + h));   // R
                    bgra[p + 3] = 255;
                }
            return bgra;
        }

        [TestMethod]
        public async Task PixelConvert_BGRAtoNV12_RoundTripsSmoothColours()
        {
            // Colours constant over each 2 x 2 block (so 4:2:0 loses nothing) survive BGRA -> NV12 -> BGRA within 2 levels (8-bit rounding of Y
            // and U / V, amplified by the decode's gains: B = 1.164 Y + 2.017 U, so up to ~1.6 levels);
            // the old full-range decode was off by up to ~20 (no 16 offset, no 255/219 gain). A smooth gradient stays within 4 (4:2:0 shares one
            // chroma sample per 2 x 2 block while the colour changes inside it).
            const int w = 64, h = 48;
            static int Worst(byte[] a, byte[] b) { int m = 0; for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i])); return m; }
            byte[] RoundTrip(byte[] src)
            {
                var yuv = new byte[w * h * 3 / 2];
                PixelFormatConverter.BGRAtoNV12(src, yuv, w, h);
                var back = new byte[w * h * 4];
                PixelFormatConverter.NV12toBGRA(yuv, back, w, h);
                return back;
            }
            var blocks = BuildSmoothBGRA(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < 4; c++) blocks[(y * w + x) * 4 + c] = blocks[((y & ~1) * w + (x & ~1)) * 4 + c];
            int blockWorst = Worst(blocks, RoundTrip(blocks));
            if (blockWorst > 2) throw new Exception($"worst channel error {blockWorst} after BGRA -> NV12 -> BGRA on 2 x 2 constant colours (expected <= 2)");
            var bgra = BuildSmoothBGRA(w, h);
            int gradientWorst = Worst(bgra, RoundTrip(bgra));
            if (gradientWorst > 4) throw new Exception($"worst channel error {gradientWorst} after BGRA -> NV12 -> BGRA on a gradient (expected <= 4)");
            var nv12 = new byte[w * h * 3 / 2];
            PixelFormatConverter.BGRAtoNV12(bgra, nv12, w, h);

            // NV12 is I420 with interleaved chroma: both paths give the same bytes
            var i420 = new byte[w * h * 3 / 2];
            PixelFormatConverter.BGRAtoI420(bgra, i420, w, h);
            var viaI420 = new byte[w * h * 3 / 2];
            PixelFormatConverter.I420toNV12(i420, viaI420, w, h);
            if (!nv12.AsSpan().SequenceEqual(viaI420)) throw new Exception("BGRAtoNV12 differs from BGRAtoI420 + I420toNV12");

            // RGB24 -> NV12 equals BGRA -> NV12 for the same colours
            var rgb = new byte[w * h * 3];
            for (int i = 0; i < w * h; i++) { rgb[i * 3] = bgra[i * 4 + 2]; rgb[i * 3 + 1] = bgra[i * 4 + 1]; rgb[i * 3 + 2] = bgra[i * 4]; }
            var fromRgb = new byte[w * h * 3 / 2];
            PixelFormatConverter.RGB24toNV12(rgb, fromRgb, w, h);
            if (!nv12.AsSpan().SequenceEqual(fromRgb)) throw new Exception("RGB24toNV12 differs from BGRAtoNV12");
            await Task.CompletedTask;
        }

        // ---- H.264 rate control (2.6.0): constant QP and quality, besides the constant bitrate ----

        /// <summary>A detailed picture (gradient + deterministic noise) as NV12: enough detail that the quantizer matters.</summary>
        private static byte[] BuildDetailedNV12(int w, int h)
        {
            var bgra = new byte[w * h * 4];
            uint state = 12345;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    state = state * 1664525 + 1013904223;
                    int n = (int)(state >> 27) - 16;   // -16..15
                    int p = (y * w + x) * 4;
                    bgra[p] = (byte)Math.Clamp(60 + 140 * x / w + n, 0, 255);
                    bgra[p + 1] = (byte)Math.Clamp(50 + 150 * y / h + n, 0, 255);
                    bgra[p + 2] = (byte)Math.Clamp(180 - 100 * (x + y) / (w + h) + n, 0, 255);
                    bgra[p + 3] = 255;
                }
            var nv12 = new byte[w * h * 3 / 2];
            PixelFormatConverter.BGRAtoNV12(bgra, nv12, w, h);
            return nv12;
        }

        [SupportedOSPlatform("windows")]
        private static (int Bytes, double LumaPsnr) EncodeOneFrame(H264EncoderOptions options, byte[] nv12)
        {
            using var encoder = VideoEncoderFactory.CreateH264(options);
            using var decoder = VideoDecoderFactory.CreateH264();
            var bytes = new List<byte>();
            var au = encoder.Encode(nv12, 0, 10_000_000 / options.FrameRate);
            if (au != null) bytes.AddRange(au);
            var drained = encoder.Drain();
            if (drained != null) bytes.AddRange(drained);
            if (bytes.Count == 0) throw new Exception($"no output for {options}");
            var frames = decoder.Decode(bytes.ToArray(), 0);
            if (frames.Count == 0) throw new Exception($"the decoder returned nothing for {options}");
            var got = frames[^1].Data.Span;
            int n = options.Width * options.Height;
            double se = 0;
            for (int i = 0; i < n; i++) { double d = got[i] - nv12[i]; se += d * d; }
            return (bytes.Count, se == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / (se / n)));
        }

        [TestMethod]
        public async Task H264Encoder_ConstantQp_QualityFallsAsQpRises()
        {
            if (OperatingSystem.IsBrowser()) return; // the MFT is Windows-only
            RunConstantQp();
            await Task.CompletedTask;
        }

        [SupportedOSPlatform("windows")]
        private static void RunConstantQp()
        {
            const int w = 320, h = 240;
            var nv12 = BuildDetailedNV12(w, h);
            var results = new[] { 12, 28, 44 }.Select(qp =>
                (Qp: qp, R: EncodeOneFrame(new H264EncoderOptions(w, h, 30, 1_000_000) { RateControl = H264RateControl.ConstantQp, Qp = qp }, nv12))).ToArray();
            var text = string.Join("; ", results.Select(r => $"QP {r.Qp}: {r.R.Bytes} bytes, {r.R.LumaPsnr:0.0} dB"));
            for (int i = 1; i < results.Length; i++)
                if (results[i].R.Bytes >= results[i - 1].R.Bytes || results[i].R.LumaPsnr >= results[i - 1].R.LumaPsnr)
                    throw new Exception($"a higher QP must give fewer bytes and a lower PSNR: {text}");
            if (results[0].R.LumaPsnr < 38) throw new Exception($"QP 12 should be near lossless (over 38 dB): {text}");
            Console.WriteLine(text);
        }

        [TestMethod]
        public async Task H264Encoder_Quality_BytesRiseWithQuality()
        {
            if (OperatingSystem.IsBrowser()) return;
            RunQuality();
            await Task.CompletedTask;
        }

        [SupportedOSPlatform("windows")]
        private static void RunQuality()
        {
            const int w = 320, h = 240;
            var nv12 = BuildDetailedNV12(w, h);
            var low = EncodeOneFrame(new H264EncoderOptions(w, h, 30, 1_000_000) { RateControl = H264RateControl.Quality, Quality = 20 }, nv12);
            var high = EncodeOneFrame(new H264EncoderOptions(w, h, 30, 1_000_000) { RateControl = H264RateControl.Quality, Quality = 90 }, nv12);
            if (high.Bytes <= low.Bytes || high.LumaPsnr <= low.LumaPsnr)
                throw new Exception($"quality 90 must give more bytes and a higher PSNR than 20: 20 -> {low.Bytes} bytes {low.LumaPsnr:0.0} dB, 90 -> {high.Bytes} bytes {high.LumaPsnr:0.0} dB");
            Console.WriteLine($"quality 20: {low.Bytes} bytes, {low.LumaPsnr:0.0} dB; quality 90: {high.Bytes} bytes, {high.LumaPsnr:0.0} dB");
        }
    }
}
