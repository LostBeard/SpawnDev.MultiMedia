using ILGPU;
using ILGPU.Runtime;
using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    public abstract partial class MultiMediaTestBase
    {
        /// <summary>
        /// Verify GPU NV12->I420 conversion matches CPU reference.
        /// </summary>
        [TestMethod]
        public async Task GpuConvert_NV12toI420_MatchesCPU()
        {
            if (OperatingSystem.IsBrowser()) return; // ILGPU desktop only for this test

            int w = 64, h = 48;
            var nv12 = new byte[w * h * 3 / 2];
            var rng = new Random(42);
            rng.NextBytes(nv12);

            // CPU reference
            var cpuResult = new byte[w * h * 3 / 2];
            PixelFormatConverter.NV12toI420(nv12, cpuResult, w, h);

            // GPU conversion
            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var converter = new GpuPixelFormatConverter(accelerator);

            var source = new VideoFrame(w, h, VideoPixelFormat.NV12, new ReadOnlyMemory<byte>(nv12), 0);
            var gpuFrame = await converter.ConvertAsync(source, VideoPixelFormat.I420);

            // Compare
            var gpuResult = gpuFrame.Data.ToArray();
            if (gpuResult.Length != cpuResult.Length)
                throw new Exception($"Size mismatch: GPU={gpuResult.Length}, CPU={cpuResult.Length}");

            int mismatches = 0;
            for (int i = 0; i < cpuResult.Length; i++)
                if (gpuResult[i] != cpuResult[i]) mismatches++;

            if (mismatches > 0)
                throw new Exception($"{mismatches} byte mismatches between GPU and CPU NV12->I420 conversion");
        }

        /// <summary>
        /// Verify GPU NV12->BGRA conversion produces reasonable output.
        /// </summary>
        [TestMethod]
        public async Task GpuConvert_NV12toBGRA_ProducesValidPixels()
        {
            if (OperatingSystem.IsBrowser()) return;

            int w = 16, h = 16;
            var nv12 = new byte[w * h * 3 / 2];
            // White: Y=235, U=128, V=128
            for (int i = 0; i < w * h; i++) nv12[i] = 235;
            for (int i = w * h; i < nv12.Length; i++) nv12[i] = 128;

            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var converter = new GpuPixelFormatConverter(accelerator);

            var source = new VideoFrame(w, h, VideoPixelFormat.NV12, new ReadOnlyMemory<byte>(nv12), 0);
            var result = await converter.ConvertAsync(source, VideoPixelFormat.BGRA);

            if (result.Format != VideoPixelFormat.BGRA)
                throw new Exception($"Expected BGRA, got {result.Format}");
            if (result.Data.Length != w * h * 4)
                throw new Exception($"Expected {w * h * 4} bytes, got {result.Data.Length}");

            // White pixels should have high R, G, B values
            var d = result.Data.Span;
            int r = d[2], g = d[1], b = d[0]; // BGRA order
            if (r < 200 || g < 200 || b < 200)
                throw new Exception($"Expected near-white, got R={r} G={g} B={b}");
        }

        /// <summary>
        /// Verify GPU BGRA->I420 roundtrip with CPU produces approximately matching results.
        /// </summary>
        [TestMethod]
        public async Task GpuConvert_BGRAtoI420_MatchesCPU()
        {
            if (OperatingSystem.IsBrowser()) return;

            int w = 32, h = 32;
            var bgra = new byte[w * h * 4];
            // Red pixels
            for (int i = 0; i < w * h; i++)
            {
                bgra[i * 4] = 0;       // B
                bgra[i * 4 + 1] = 0;   // G
                bgra[i * 4 + 2] = 255; // R
                bgra[i * 4 + 3] = 255; // A
            }

            // CPU reference
            var cpuResult = new byte[w * h * 3 / 2];
            PixelFormatConverter.BGRAtoI420(bgra, cpuResult, w, h);

            // GPU
            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var converter = new GpuPixelFormatConverter(accelerator);

            var source = new VideoFrame(w, h, VideoPixelFormat.BGRA, new ReadOnlyMemory<byte>(bgra), 0);
            var gpuFrame = await converter.ConvertAsync(source, VideoPixelFormat.I420);

            var gpuResult = gpuFrame.Data.ToArray();
            // Allow small rounding differences between CPU float64 and GPU float32
            int mismatches = 0;
            for (int i = 0; i < cpuResult.Length; i++)
                if (Math.Abs(gpuResult[i] - cpuResult[i]) > 1) mismatches++;

            if (mismatches > 0)
                throw new Exception($"{mismatches} byte mismatches (>1 tolerance) between GPU and CPU BGRA->I420");
        }

        /// <summary>
        /// Verify GPU RGB24->BGRA conversion matches CPU reference.
        /// </summary>
        [TestMethod]
        public async Task GpuConvert_RGB24toBGRA_MatchesCPU()
        {
            if (OperatingSystem.IsBrowser()) return;

            int w = 8, h = 4;
            var rgb24 = new byte[w * h * 3];
            var rng = new Random(123);
            rng.NextBytes(rgb24);

            // CPU reference
            var cpuResult = new byte[w * h * 4];
            PixelFormatConverter.RGB24toBGRA(rgb24, cpuResult, w, h);

            // GPU
            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var converter = new GpuPixelFormatConverter(accelerator);

            var source = new VideoFrame(w, h, VideoPixelFormat.RGB24, new ReadOnlyMemory<byte>(rgb24), 0);
            var gpuFrame = await converter.ConvertAsync(source, VideoPixelFormat.BGRA);

            var gpuResult = gpuFrame.Data.ToArray();
            int mismatches = 0;
            for (int i = 0; i < cpuResult.Length; i++)
                if (gpuResult[i] != cpuResult[i]) mismatches++;

            if (mismatches > 0)
                throw new Exception($"{mismatches} byte mismatches between GPU and CPU RGB24->BGRA");
        }

        /// <summary>
        /// Verify GPU YUY2->I420 conversion matches CPU reference.
        /// </summary>
        [TestMethod]
        public async Task GpuConvert_YUY2toI420_MatchesCPU()
        {
            if (OperatingSystem.IsBrowser()) return;

            int w = 64, h = 48;
            var yuy2 = new byte[w * h * 2];
            var rng = new Random(55);
            rng.NextBytes(yuy2);

            // CPU reference
            var cpuResult = new byte[w * h * 3 / 2];
            PixelFormatConverter.YUY2toI420(yuy2, cpuResult, w, h);

            // GPU conversion
            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var converter = new GpuPixelFormatConverter(accelerator);

            var source = new VideoFrame(w, h, VideoPixelFormat.YUY2, new ReadOnlyMemory<byte>(yuy2), 0);
            var gpuFrame = await converter.ConvertAsync(source, VideoPixelFormat.I420);

            var gpuResult = gpuFrame.Data.ToArray();
            int mismatches = 0;
            for (int i = 0; i < cpuResult.Length; i++)
                if (gpuResult[i] != cpuResult[i]) mismatches++;

            if (mismatches > 0)
                throw new Exception($"{mismatches} byte mismatches between GPU and CPU YUY2->I420 conversion");
        }

        /// <summary>
        /// Verify GPU YUY2->BGRA conversion matches CPU reference.
        /// </summary>
        [TestMethod]
        public async Task GpuConvert_YUY2toBGRA_MatchesCPU()
        {
            if (OperatingSystem.IsBrowser()) return;

            int w = 64, h = 48;
            var yuy2 = new byte[w * h * 2];
            var rng = new Random(66);
            rng.NextBytes(yuy2);

            // CPU reference
            var cpuResult = new byte[w * h * 4];
            PixelFormatConverter.YUY2toBGRA(yuy2, cpuResult, w, h);

            // GPU conversion
            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var converter = new GpuPixelFormatConverter(accelerator);

            var source = new VideoFrame(w, h, VideoPixelFormat.YUY2, new ReadOnlyMemory<byte>(yuy2), 0);
            var gpuFrame = await converter.ConvertAsync(source, VideoPixelFormat.BGRA);

            var gpuResult = gpuFrame.Data.ToArray();
            // Allow tolerance=1 for float32 vs float64 rounding in color matrix
            int mismatches = 0;
            for (int i = 0; i < cpuResult.Length; i++)
                if (Math.Abs(gpuResult[i] - cpuResult[i]) > 1) mismatches++;

            if (mismatches > 0)
                throw new Exception($"{mismatches} byte mismatches (>1 tolerance) between GPU and CPU YUY2->BGRA");
        }

        /// <summary>
        /// Verify GPU UYVY->I420 conversion matches CPU reference.
        /// </summary>
        [TestMethod]
        public async Task GpuConvert_UYVYtoI420_MatchesCPU()
        {
            if (OperatingSystem.IsBrowser()) return;

            int w = 64, h = 48;
            var uyvy = new byte[w * h * 2];
            var rng = new Random(88);
            rng.NextBytes(uyvy);

            // CPU reference
            var cpuResult = new byte[w * h * 3 / 2];
            PixelFormatConverter.UYVYtoI420(uyvy, cpuResult, w, h);

            // GPU conversion
            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var converter = new GpuPixelFormatConverter(accelerator);

            var source = new VideoFrame(w, h, VideoPixelFormat.UYVY, new ReadOnlyMemory<byte>(uyvy), 0);
            var gpuFrame = await converter.ConvertAsync(source, VideoPixelFormat.I420);

            var gpuResult = gpuFrame.Data.ToArray();
            int mismatches = 0;
            for (int i = 0; i < cpuResult.Length; i++)
                if (gpuResult[i] != cpuResult[i]) mismatches++;

            if (mismatches > 0)
                throw new Exception($"{mismatches} byte mismatches between GPU and CPU UYVY->I420 conversion");
        }

        /// <summary>
        /// Verify GPU converter accepts existing Accelerator (pipeline integration).
        /// </summary>
        [TestMethod]
        public async Task GpuConvert_AcceptsExistingAccelerator()
        {
            if (OperatingSystem.IsBrowser()) return;

            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);

            // Create converter with existing accelerator (not owned)
            using var converter = new GpuPixelFormatConverter(accelerator);

            var nv12 = new byte[16 * 16 * 3 / 2];
            var source = new VideoFrame(16, 16, VideoPixelFormat.NV12, new ReadOnlyMemory<byte>(nv12), 0);
            var result = await converter.ConvertAsync(source, VideoPixelFormat.I420);

            if (result.Data.Length != 16 * 16 * 3 / 2)
                throw new Exception($"Wrong output size: {result.Data.Length}");

            // Accelerator should still be alive after converter dispose
            // (converter doesn't own it)
        }

        // ---- MJPG GPU Decode Tests ----

        /// <summary>
        /// Verify GpuMjpgDecoder can decode a minimal valid JPEG to BGRA.
        /// Constructs a minimal baseline JPEG with known pixel data and verifies the output.
        /// </summary>
        [TestMethod]
        public async Task GpuMjpgDecode_MinimalJpeg_ProducesOutput()
        {
            if (OperatingSystem.IsBrowser()) return;

            // Build a minimal valid JPEG: 8x8, single MCU, 4:4:4, all gray (Y=128, Cb=128, Cr=128)
            var jpeg = BuildMinimalGrayJpeg(128);

            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var decoder = new GpuMjpgDecoder(accelerator);

            var frame = decoder.DecodeToBGRA(jpeg, 12345);

            if (frame.Width != 8) throw new Exception($"Expected width 8, got {frame.Width}");
            if (frame.Height != 8) throw new Exception($"Expected height 8, got {frame.Height}");
            if (frame.Format != VideoPixelFormat.BGRA) throw new Exception($"Expected BGRA, got {frame.Format}");
            if (frame.Timestamp != 12345) throw new Exception($"Timestamp not preserved");
            if (frame.Data.Length != 8 * 8 * 4)
                throw new Exception($"Expected {8 * 8 * 4} bytes, got {frame.Data.Length}");

            // Gray pixels: R ~= G ~= B, all near 128
            var d = frame.Data.Span;
            int maxChannelDiff = 0;
            for (int p = 0; p < 64; p++)
            {
                int b = d[p * 4], g = d[p * 4 + 1], r = d[p * 4 + 2], a = d[p * 4 + 3];
                if (a != 255) throw new Exception($"Alpha should be 255 at pixel {p}, got {a}");
                int diff = Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(g - b), Math.Abs(r - b)));
                if (diff > maxChannelDiff) maxChannelDiff = diff;
            }
            if (maxChannelDiff > 3)
                throw new Exception($"Gray image: max channel diff = {maxChannelDiff}, expected <= 3");

            await Task.CompletedTask;
        }

        /// <summary>
        /// Verify GpuMjpgDecoder.DecodeToBGRAOnGpu returns a GPU buffer (zero-copy path).
        /// </summary>
        [TestMethod]
        public async Task GpuMjpgDecode_GpuOutput_StaysOnDevice()
        {
            if (OperatingSystem.IsBrowser()) return;

            var jpeg = BuildMinimalGrayJpeg(200);

            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            using var decoder = new GpuMjpgDecoder(accelerator);

            using var gpuBuffer = decoder.DecodeToBGRAOnGpu(jpeg);

            if (gpuBuffer.Length != 8 * 8 * 4)
                throw new Exception($"GPU buffer size wrong: {gpuBuffer.Length}, expected {8 * 8 * 4}");

            // Read back and verify
            var data = new byte[gpuBuffer.Length];
            gpuBuffer.CopyToCPU(data);

            // Verify valid BGRA output (non-zero, gray-ish since input is uniform)
            // DC=72 after level shift, IDCT scales by 1/8, so pixel = 72/8+128 = 137
            int r = data[2], g = data[1], b = data[0], a = data[3];
            if (a != 255)
                throw new Exception($"Expected alpha=255, got {a}");
            // All channels should be equal (grayscale) and > 128 (brighter than mid-gray)
            int maxDiff = Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(g - b), Math.Abs(r - b)));
            if (maxDiff > 3)
                throw new Exception($"Expected grayscale, got R={r} G={g} B={b} (diff={maxDiff})");
            if (r < 128)
                throw new Exception($"Expected brighter than mid-gray, got R={r} G={g} B={b}");

            await Task.CompletedTask;
        }

        /// <summary>
        /// Verify MJPG parser handles the Huffman decode correctly by checking coefficient count.
        /// </summary>
        [TestMethod]
        public async Task MjpgParse_ProducesCorrectBlockCount()
        {
            if (OperatingSystem.IsBrowser()) return;

            var jpeg = BuildMinimalGrayJpeg(128);
            var result = MjpgDecoder.Parse(jpeg);

            if (result.Width != 8) throw new Exception($"Width: {result.Width}");
            if (result.Height != 8) throw new Exception($"Height: {result.Height}");
            // 4:4:4 with 8x8 image = 1 MCU, 3 blocks (Y + Cb + Cr), 64 coefficients each
            if (result.TotalBlocks != 3) throw new Exception($"TotalBlocks: {result.TotalBlocks}, expected 3");
            if (result.DctCoefficients.Length != 3 * 64)
                throw new Exception($"DctCoefficients length: {result.DctCoefficients.Length}, expected {3 * 64}");

            await Task.CompletedTask;
        }

        /// <summary>
        /// Build a minimal valid baseline JPEG (8x8, 4:4:4, single gray value).
        /// This constructs a real JFIF file with proper markers, Huffman tables, and entropy-coded data.
        /// </summary>
        private static byte[] BuildMinimalGrayJpeg(byte grayLevel)
        {
            // This is a programmatically-built minimal JPEG.
            // 8x8 image, YCbCr 4:4:4 (all sampling factors 1x1), uniform gray.

            using var ms = new System.IO.MemoryStream();
            using var w = new System.IO.BinaryWriter(ms);

            // SOI
            w.Write((byte)0xFF); w.Write((byte)0xD8);

            // DQT - flat quantization table (all 1s for lossless-ish)
            var dqtPayload = new byte[67]; // length=67: 2(len) + 1(info) + 64(table)
            dqtPayload[0] = 0; dqtPayload[1] = 67; // segment length
            dqtPayload[2] = 0; // 8-bit precision, table 0
            for (int i = 0; i < 64; i++) dqtPayload[3 + i] = 1; // all 1s
            w.Write((byte)0xFF); w.Write((byte)0xDB);
            w.Write(dqtPayload);

            // SOF0 - 8x8, 3 components, all 1x1 sampling, quant table 0
            // length = 2 + 1(precision) + 2(height) + 2(width) + 1(nComp) + 3*3(comp info) = 17
            w.Write((byte)0xFF); w.Write((byte)0xC0);
            w.Write((byte)0); w.Write((byte)17); // length = 17
            w.Write((byte)8); // precision
            w.Write((byte)0); w.Write((byte)8); // height = 8
            w.Write((byte)0); w.Write((byte)8); // width = 8
            w.Write((byte)3); // 3 components
            w.Write((byte)1); w.Write((byte)0x11); w.Write((byte)0); // Y: id=1, 1x1, qt=0
            w.Write((byte)2); w.Write((byte)0x11); w.Write((byte)0); // Cb: id=2, 1x1, qt=0
            w.Write((byte)3); w.Write((byte)0x11); w.Write((byte)0); // Cr: id=3, 1x1, qt=0

            // DHT - DC table (class=0, id=0): single symbol for category 0 (zero diff)
            // and one for category range to encode our DC value
            WriteDHT(w, 0, 0, BuildSimpleDcHuffmanTable());
            // DHT - AC table (class=1, id=0): just EOB symbol
            WriteDHT(w, 1, 0, BuildSimpleAcHuffmanTable());

            // SOS
            // length = 2 + 1(nComp) + 3*2(comp table ids) + 3(spectral: Ss, Se, AhAl) = 12
            w.Write((byte)0xFF); w.Write((byte)0xDA);
            w.Write((byte)0); w.Write((byte)12); // length = 12
            w.Write((byte)3); // 3 components
            w.Write((byte)1); w.Write((byte)0x00); // Y: dc=0, ac=0
            w.Write((byte)2); w.Write((byte)0x00); // Cb: dc=0, ac=0
            w.Write((byte)3); w.Write((byte)0x00); // Cr: dc=0, ac=0
            w.Write((byte)0); w.Write((byte)63); w.Write((byte)0); // Ss=0, Se=63, AhAl=0

            // Entropy-coded data: encode 3 blocks of uniform color
            // DC value = grayLevel - 128 (level shift), AC = all zeros (EOB)
            int dcValue = grayLevel - 128;
            var bits = new BitWriter();

            // Block 0 (Y): DC = dcValue, AC = EOB
            EncodeDcValue(bits, dcValue); // DC
            bits.WriteBits(0, 1); // EOB = code 0 (length 1)

            // Block 1 (Cb): DC = 0 (gray = 128, shifted = 0), AC = EOB
            EncodeDcValue(bits, 0);
            bits.WriteBits(0, 1); // EOB

            // Block 2 (Cr): DC = 0, AC = EOB
            EncodeDcValue(bits, 0);
            bits.WriteBits(0, 1); // EOB

            bits.Flush();
            var entropyBytes = bits.ToArray();
            w.Write(entropyBytes);

            // EOI
            w.Write((byte)0xFF); w.Write((byte)0xD9);

            return ms.ToArray();
        }

        private static (int[] bitCounts, byte[] symbols) BuildSimpleDcHuffmanTable()
        {
            // DC Huffman table: encode categories 0-8
            // Category 0: code 00 (2 bits)
            // Category 1: code 010 (3 bits)
            // Category 2: code 011 (3 bits)
            // Category 3: code 100 (3 bits)
            // Category 4: code 101 (3 bits)
            // Category 5: code 110 (3 bits)
            // Category 6: code 1110 (4 bits)
            // Category 7: code 11110 (5 bits)
            // Category 8: code 111110 (6 bits)
            var bitCounts = new int[17];
            bitCounts[2] = 1; // 1 code of length 2
            bitCounts[3] = 5; // 5 codes of length 3
            bitCounts[4] = 1; // 1 code of length 4
            bitCounts[5] = 1; // 1 code of length 5
            bitCounts[6] = 1; // 1 code of length 6
            var symbols = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            return (bitCounts, symbols);
        }

        private static (int[] bitCounts, byte[] symbols) BuildSimpleAcHuffmanTable()
        {
            // AC Huffman table: just EOB (symbol 0x00)
            // Single code of length 1: code = 0, symbol = EOB
            var bitCounts = new int[17];
            bitCounts[1] = 1; // 1 code of length 1
            var symbols = new byte[] { 0x00 }; // EOB
            return (bitCounts, symbols);
        }

        private static void WriteDHT(System.IO.BinaryWriter w, int tableClass, int tableId,
            (int[] bitCounts, byte[] symbols) table)
        {
            int totalSymbols = table.symbols.Length;
            int segLen = 2 + 1 + 16 + totalSymbols;
            w.Write((byte)0xFF); w.Write((byte)0xC4);
            w.Write((byte)(segLen >> 8)); w.Write((byte)(segLen & 0xFF));
            w.Write((byte)((tableClass << 4) | tableId));
            for (int i = 1; i <= 16; i++)
                w.Write((byte)table.bitCounts[i]);
            w.Write(table.symbols);
        }

        private static void EncodeDcValue(BitWriter bits, int value)
        {
            int category = 0;
            int absVal = Math.Abs(value);
            int temp = absVal;
            while (temp > 0) { category++; temp >>= 1; }

            // Encode category using our DC Huffman table
            switch (category)
            {
                case 0: bits.WriteBits(0b00, 2); break;
                case 1: bits.WriteBits(0b010, 3); break;
                case 2: bits.WriteBits(0b011, 3); break;
                case 3: bits.WriteBits(0b100, 3); break;
                case 4: bits.WriteBits(0b101, 3); break;
                case 5: bits.WriteBits(0b110, 3); break;
                case 6: bits.WriteBits(0b1110, 4); break;
                case 7: bits.WriteBits(0b11110, 5); break;
                case 8: bits.WriteBits(0b111110, 6); break;
            }

            // Encode the value magnitude bits
            if (category > 0)
            {
                int magnitude = value >= 0 ? value : value + (1 << category) - 1;
                bits.WriteBits(magnitude, category);
            }
        }

        private class BitWriter
        {
            private readonly System.IO.MemoryStream _ms = new();
            private int _bits;
            private int _bitCount;

            public void WriteBits(int value, int count)
            {
                _bits = (_bits << count) | (value & ((1 << count) - 1));
                _bitCount += count;
                while (_bitCount >= 8)
                {
                    _bitCount -= 8;
                    byte b = (byte)((_bits >> _bitCount) & 0xFF);
                    _ms.WriteByte(b);
                    if (b == 0xFF) _ms.WriteByte(0x00); // Byte stuffing
                }
            }

            public void Flush()
            {
                if (_bitCount > 0)
                {
                    byte b = (byte)((_bits << (8 - _bitCount)) & 0xFF);
                    _ms.WriteByte(b);
                    if (b == 0xFF) _ms.WriteByte(0x00);
                    _bitCount = 0;
                }
            }

            public byte[] ToArray() => _ms.ToArray();
        }
    }
}
