using ILGPU;
using ILGPU.Runtime;

namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// ILGPU-accelerated pixel format conversion for video frames.
    /// Runs on any backend: CUDA, OpenCL, WebGPU, Wasm, CPU.
    /// Accepts an existing Accelerator for pipeline integration.
    /// </summary>
    public class GpuPixelFormatConverter : IDisposable
    {
        private readonly Accelerator _accelerator;
        private readonly bool _ownsAccelerator;

        // Cached kernels (loaded on first use)
        private Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int>? _nv12ToI420Kernel;
        private Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int>? _nv12ToBGRAKernel;
        private Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int>? _i420ToBGRAKernel;
        private Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int>? _bgraToI420Kernel;
        private Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int>? _yuy2ToI420Kernel;
        private Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int>? _yuy2ToBGRAKernel;

        /// <summary>
        /// Create a converter with an existing ILGPU Accelerator.
        /// Use this when integrating into an existing ILGPU pipeline.
        /// </summary>
        public GpuPixelFormatConverter(Accelerator accelerator)
        {
            _accelerator = accelerator;
            _ownsAccelerator = false;
        }

        /// <summary>
        /// Create a converter that owns its own Accelerator.
        /// Creates the best available accelerator (CUDA > OpenCL > CPU).
        /// </summary>
        public GpuPixelFormatConverter(Context context)
        {
            _accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            _ownsAccelerator = true;
        }

        /// <summary>
        /// Convert a VideoFrame to the target format using the GPU.
        /// Source data is uploaded, kernel runs, result is downloaded.
        /// Returns original frame if already in target format.
        /// </summary>
        public async Task<VideoFrame> ConvertAsync(VideoFrame source, VideoPixelFormat target)
        {
            if (source.Format == target) return source;

            int w = source.Width, h = source.Height;
            int dstSize = PixelFormatConverter.GetFrameSize(target, w, h);

            using var srcBuffer = _accelerator.Allocate1D<byte>(source.Data.Length);
            using var dstBuffer = _accelerator.Allocate1D<byte>(dstSize);

            srcBuffer.CopyFromCPU(source.Data.ToArray());

            DispatchConversion(srcBuffer.View, source.Format, dstBuffer.View, target, w, h);

            _accelerator.Synchronize();

            var result = new byte[dstSize];
            dstBuffer.CopyToCPU(result);

            return new VideoFrame(w, h, target, new ReadOnlyMemory<byte>(result), source.Timestamp);
        }

        /// <summary>
        /// Convert between GPU buffers directly (zero-copy for GPU pipelines).
        /// Both buffers must already be allocated on this accelerator.
        /// </summary>
        public void Convert(ArrayView<byte> src, VideoPixelFormat srcFmt,
            ArrayView<byte> dst, VideoPixelFormat dstFmt, int width, int height)
        {
            DispatchConversion(src, srcFmt, dst, dstFmt, width, height);
        }

        private void DispatchConversion(ArrayView<byte> src, VideoPixelFormat srcFmt,
            ArrayView<byte> dst, VideoPixelFormat dstFmt, int width, int height)
        {
            int pixelCount = width * height;

            switch ((srcFmt, dstFmt))
            {
                case (VideoPixelFormat.NV12, VideoPixelFormat.I420):
                    _nv12ToI420Kernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                        ArrayView<byte>, ArrayView<byte>, int, int>(NV12toI420Kernel);
                    _nv12ToI420Kernel(pixelCount, src, dst, width, height);
                    break;

                case (VideoPixelFormat.NV12, VideoPixelFormat.BGRA):
                    _nv12ToBGRAKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                        ArrayView<byte>, ArrayView<byte>, int, int>(NV12toBGRAKernel);
                    _nv12ToBGRAKernel(pixelCount, src, dst, width, height);
                    break;

                case (VideoPixelFormat.I420, VideoPixelFormat.BGRA):
                    _i420ToBGRAKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                        ArrayView<byte>, ArrayView<byte>, int, int>(I420toBGRAKernel);
                    _i420ToBGRAKernel(pixelCount, src, dst, width, height);
                    break;

                case (VideoPixelFormat.BGRA, VideoPixelFormat.I420):
                    _bgraToI420Kernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                        ArrayView<byte>, ArrayView<byte>, int, int>(BGRAtoI420Kernel);
                    _bgraToI420Kernel(pixelCount, src, dst, width, height);
                    break;

                case (VideoPixelFormat.YUY2, VideoPixelFormat.I420):
                    _yuy2ToI420Kernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                        ArrayView<byte>, ArrayView<byte>, int, int>(YUY2toI420Kernel);
                    _yuy2ToI420Kernel(pixelCount / 2, src, dst, width, height); // 2 pixels per YUY2 macro-pixel
                    break;

                case (VideoPixelFormat.YUY2, VideoPixelFormat.BGRA):
                    _yuy2ToBGRAKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                        ArrayView<byte>, ArrayView<byte>, int, int>(YUY2toBGRAKernel);
                    _yuy2ToBGRAKernel(pixelCount / 2, src, dst, width, height);
                    break;

                default:
                    throw new NotSupportedException($"No GPU conversion path from {srcFmt} to {dstFmt}");
            }
        }

        // ---- ILGPU Kernels ----
        // Each thread processes one pixel (or one macro-pixel for packed formats)

        static void NV12toI420Kernel(Index1D index, ArrayView<byte> src, ArrayView<byte> dst, int width, int height)
        {
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvSize = uvWidth * (height / 2);

            if (index < ySize)
            {
                // Copy Y as-is
                dst[index] = src[index];
            }

            // Each UV pair: deinterleave
            int uvIndex = index - ySize;
            if (uvIndex >= 0 && uvIndex < uvSize)
            {
                dst[ySize + uvIndex] = src[ySize + uvIndex * 2];           // U
                dst[ySize + uvSize + uvIndex] = src[ySize + uvIndex * 2 + 1]; // V
            }
        }

        static void NV12toBGRAKernel(Index1D index, ArrayView<byte> src, ArrayView<byte> dst, int width, int height)
        {
            if (index >= width * height) return;
            int x = index % width;
            int y = index / width;
            int ySize = width * height;

            int yVal = src[y * width + x];
            int uvRow = y / 2;
            int uvCol = x / 2;
            int u = src[ySize + uvRow * width + uvCol * 2] - 128;
            int v = src[ySize + uvRow * width + uvCol * 2 + 1] - 128;

            int r = Clamp(yVal + (int)(1.402f * v));
            int g = Clamp(yVal - (int)(0.344f * u) - (int)(0.714f * v));
            int b = Clamp(yVal + (int)(1.772f * u));

            int px = index * 4;
            dst[px] = (byte)b;
            dst[px + 1] = (byte)g;
            dst[px + 2] = (byte)r;
            dst[px + 3] = 255;
        }

        static void I420toBGRAKernel(Index1D index, ArrayView<byte> src, ArrayView<byte> dst, int width, int height)
        {
            if (index >= width * height) return;
            int x = index % width;
            int y = index / width;
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvSize = uvWidth * (height / 2);

            int yVal = src[y * width + x];
            int u = src[ySize + (y / 2) * uvWidth + (x / 2)] - 128;
            int v = src[ySize + uvSize + (y / 2) * uvWidth + (x / 2)] - 128;

            int r = Clamp(yVal + (int)(1.402f * v));
            int g = Clamp(yVal - (int)(0.344f * u) - (int)(0.714f * v));
            int b = Clamp(yVal + (int)(1.772f * u));

            int px = index * 4;
            dst[px] = (byte)b;
            dst[px + 1] = (byte)g;
            dst[px + 2] = (byte)r;
            dst[px + 3] = 255;
        }

        static void BGRAtoI420Kernel(Index1D index, ArrayView<byte> src, ArrayView<byte> dst, int width, int height)
        {
            if (index >= width * height) return;
            int x = index % width;
            int y = index / width;
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvSize = uvWidth * (height / 2);

            int px = index * 4;
            int b = src[px], g = src[px + 1], r = src[px + 2];

            dst[y * width + x] = (byte)Clamp((int)(0.257f * r + 0.504f * g + 0.098f * b) + 16);

            // Only one thread per 2x2 block writes U/V
            if ((x & 1) == 0 && (y & 1) == 0)
            {
                int uvIdx = (y / 2) * uvWidth + (x / 2);
                dst[ySize + uvIdx] = (byte)Clamp((int)(-0.148f * r - 0.291f * g + 0.439f * b) + 128);
                dst[ySize + uvSize + uvIdx] = (byte)Clamp((int)(0.439f * r - 0.368f * g - 0.071f * b) + 128);
            }
        }

        static void YUY2toI420Kernel(Index1D index, ArrayView<byte> src, ArrayView<byte> dst, int width, int height)
        {
            // index = macro-pixel index (2 horizontal pixels)
            int macroPixelsPerRow = width / 2;
            int macroY = index / macroPixelsPerRow;
            int macroX = index % macroPixelsPerRow;
            if (macroY >= height) return;

            int srcIdx = (macroY * macroPixelsPerRow + macroX) * 4;
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvSize = uvWidth * (height / 2);

            int x = macroX * 2;
            dst[macroY * width + x] = src[srcIdx];         // Y0
            dst[macroY * width + x + 1] = src[srcIdx + 2]; // Y1

            if ((macroY & 1) == 0)
            {
                int uvIdx = (macroY / 2) * uvWidth + macroX;
                dst[ySize + uvIdx] = src[srcIdx + 1];           // U
                dst[ySize + uvSize + uvIdx] = src[srcIdx + 3];  // V
            }
        }

        static void YUY2toBGRAKernel(Index1D index, ArrayView<byte> src, ArrayView<byte> dst, int width, int height)
        {
            int macroPixelsPerRow = width / 2;
            int macroY = index / macroPixelsPerRow;
            int macroX = index % macroPixelsPerRow;
            if (macroY >= height) return;

            int srcIdx = (macroY * macroPixelsPerRow + macroX) * 4;
            int y0 = src[srcIdx], u = src[srcIdx + 1] - 128;
            int y1 = src[srcIdx + 2], v = src[srcIdx + 3] - 128;

            int x = macroX * 2;
            int px0 = (macroY * width + x) * 4;
            dst[px0] = (byte)Clamp(y0 + (int)(1.772f * u));
            dst[px0 + 1] = (byte)Clamp(y0 - (int)(0.344f * u) - (int)(0.714f * v));
            dst[px0 + 2] = (byte)Clamp(y0 + (int)(1.402f * v));
            dst[px0 + 3] = 255;

            int px1 = px0 + 4;
            dst[px1] = (byte)Clamp(y1 + (int)(1.772f * u));
            dst[px1 + 1] = (byte)Clamp(y1 - (int)(0.344f * u) - (int)(0.714f * v));
            dst[px1 + 2] = (byte)Clamp(y1 + (int)(1.402f * v));
            dst[px1 + 3] = 255;
        }

        static int Clamp(int val) => val < 0 ? 0 : val > 255 ? 255 : val;

        public void Dispose()
        {
            if (_ownsAccelerator)
                _accelerator.Dispose();
        }
    }
}
