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
    }
}
