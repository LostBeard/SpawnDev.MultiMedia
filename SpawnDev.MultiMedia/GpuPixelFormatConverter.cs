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
        private Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int>? _uyvyToI420Kernel;
        private Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int>? _rgb24ToBGRAKernel;

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
        public Task<VideoFrame> ConvertAsync(VideoFrame source, VideoPixelFormat target)
        {
            if (source.Format == target) return Task.FromResult(source);

            int w = source.Width, h = source.Height;
            int dstSize = PixelFormatConverter.GetFrameSize(target, w, h);

            using var srcBuffer = _accelerator.Allocate1D<byte>(source.Data.Length);
            using var dstBuffer = _accelerator.Allocate1D<byte>(dstSize);

            srcBuffer.CopyFromCPU(source.Data.ToArray());

            DispatchConversion(srcBuffer.View, source.Format, dstBuffer.View, target, w, h);

            _accelerator.Synchronize();

            var result = new byte[dstSize];
            dstBuffer.CopyToCPU(result);

            return Task.FromResult(new VideoFrame(w, h, target, new ReadOnlyMemory<byte>(result), source.Timestamp));
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
                    // Dispatch enough for Y plane + UV plane (total = ySize + uvSize = w*h*3/2)
                    _nv12ToI420Kernel(width * height * 3 / 2, src, dst, width, height);
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

                case (VideoPixelFormat.UYVY, VideoPixelFormat.I420):
                    _uyvyToI420Kernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                        ArrayView<byte>, ArrayView<byte>, int, int>(UYVYtoI420Kernel);
                    _uyvyToI420Kernel(pixelCount / 2, src, dst, width, height);
                    break;

                case (VideoPixelFormat.RGB24, VideoPixelFormat.BGRA):
                    _rgb24ToBGRAKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                        ArrayView<byte>, ArrayView<byte>, int, int>(RGB24toBGRAKernel);
                    _rgb24ToBGRAKernel(pixelCount, src, dst, width, height);
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
            int uvSize = (width / 2) * (height / 2);

            if (index < ySize)
            {
                // Copy Y plane as-is
                dst[index] = src[index];
            }
            else
            {
                // Deinterleave UV: NV12 has [U0,V0,U1,V1,...], I420 has separate U and V planes
                int uvIndex = index - ySize;
                if (uvIndex < uvSize)
                {
                    dst[ySize + uvIndex] = src[ySize + uvIndex * 2];           // U
                    dst[ySize + uvSize + uvIndex] = src[ySize + uvIndex * 2 + 1]; // V
                }
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

            int r = Bt601.R(yVal, v);
            int g = Bt601.G(yVal, u, v);
            int b = Bt601.B(yVal, u);

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

            int r = Bt601.R(yVal, v);
            int g = Bt601.G(yVal, u, v);
            int b = Bt601.B(yVal, u);

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
            dst[y * width + x] = (byte)Bt601.Y(src[px + 2], src[px + 1], src[px]);

            // One thread per 2x2 block writes U/V, from the block's average colour (as the CPU converter)
            if ((x & 1) == 0 && (y & 1) == 0)
            {
                float r = 0, g = 0, b = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int q = ((y + dy) * width + x + dx) * 4;
                        r += src[q + 2]; g += src[q + 1]; b += src[q];
                    }
                r *= 0.25f; g *= 0.25f; b *= 0.25f;
                int uvIdx = (y / 2) * uvWidth + (x / 2);
                dst[ySize + uvIdx] = (byte)Bt601.U(r, g, b);
                dst[ySize + uvSize + uvIdx] = (byte)Bt601.V(r, g, b);
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
            dst[px0] = (byte)Bt601.B(y0, u);
            dst[px0 + 1] = (byte)Bt601.G(y0, u, v);
            dst[px0 + 2] = (byte)Bt601.R(y0, v);
            dst[px0 + 3] = 255;

            int px1 = px0 + 4;
            dst[px1] = (byte)Bt601.B(y1, u);
            dst[px1 + 1] = (byte)Bt601.G(y1, u, v);
            dst[px1 + 2] = (byte)Bt601.R(y1, v);
            dst[px1 + 3] = 255;
        }

        static void UYVYtoI420Kernel(Index1D index, ArrayView<byte> src, ArrayView<byte> dst, int width, int height)
        {
            int macroPixelsPerRow = width / 2;
            int macroY = index / macroPixelsPerRow;
            int macroX = index % macroPixelsPerRow;
            if (macroY >= height) return;

            int srcIdx = (macroY * macroPixelsPerRow + macroX) * 4;
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvSize = uvWidth * (height / 2);

            int x = macroX * 2;
            dst[macroY * width + x] = src[srcIdx + 1];     // Y0
            dst[macroY * width + x + 1] = src[srcIdx + 3]; // Y1

            if ((macroY & 1) == 0)
            {
                int uvIdx = (macroY / 2) * uvWidth + macroX;
                dst[ySize + uvIdx] = src[srcIdx];           // U
                dst[ySize + uvSize + uvIdx] = src[srcIdx + 2]; // V
            }
        }

        static void RGB24toBGRAKernel(Index1D index, ArrayView<byte> src, ArrayView<byte> dst, int width, int height)
        {
            if (index >= width * height) return;
            int s = index * 3;
            int d = index * 4;
            dst[d] = src[s + 2];     // B
            dst[d + 1] = src[s + 1]; // G
            dst[d + 2] = src[s];     // R
            dst[d + 3] = 255;        // A
        }

        static int Clamp(int val) => val < 0 ? 0 : val > 255 ? 255 : val;

        public void Dispose()
        {
            if (_ownsAccelerator)
                _accelerator.Dispose();
        }
    }
}
