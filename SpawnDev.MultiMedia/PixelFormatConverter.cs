namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// CPU-side pixel format conversion for video frames.
    /// Converts between common camera output formats (NV12, YUY2, RGB24)
    /// and consumer-expected formats (I420 for WebRTC, BGRA for display).
    /// </summary>
    public static class PixelFormatConverter
    {
        /// <summary>
        /// Convert a VideoFrame to the target format. Returns the original if already correct.
        /// </summary>
        public static VideoFrame Convert(VideoFrame source, VideoPixelFormat target)
        {
            if (source.Format == target) return source;

            int w = source.Width, h = source.Height;
            int targetSize = GetFrameSize(target, w, h);
            if (targetSize <= 0)
                throw new NotSupportedException($"Cannot compute frame size for {target} at {w}x{h}");

            var dst = new byte[targetSize];
            Convert(source.Data.Span, source.Format, dst, target, w, h);
            return new VideoFrame(w, h, target, new ReadOnlyMemory<byte>(dst), source.Timestamp);
        }

        /// <summary>
        /// Convert raw pixel data between formats in-place (Span-based, zero alloc for the conversion itself).
        /// </summary>
        public static void Convert(ReadOnlySpan<byte> src, VideoPixelFormat srcFmt,
            Span<byte> dst, VideoPixelFormat dstFmt, int width, int height)
        {
            if (srcFmt == dstFmt)
            {
                src.CopyTo(dst);
                return;
            }

            switch ((srcFmt, dstFmt))
            {
                case (VideoPixelFormat.NV12, VideoPixelFormat.I420):
                    NV12toI420(src, dst, width, height);
                    break;
                case (VideoPixelFormat.NV12, VideoPixelFormat.BGRA):
                    NV12toBGRA(src, dst, width, height);
                    break;
                case (VideoPixelFormat.I420, VideoPixelFormat.BGRA):
                    I420toBGRA(src, dst, width, height);
                    break;
                case (VideoPixelFormat.BGRA, VideoPixelFormat.I420):
                    BGRAtoI420(src, dst, width, height);
                    break;
                case (VideoPixelFormat.YUY2, VideoPixelFormat.I420):
                    YUY2toI420(src, dst, width, height);
                    break;
                case (VideoPixelFormat.YUY2, VideoPixelFormat.BGRA):
                    YUY2toBGRA(src, dst, width, height);
                    break;
                case (VideoPixelFormat.RGB24, VideoPixelFormat.BGRA):
                    RGB24toBGRA(src, dst, width, height);
                    break;
                case (VideoPixelFormat.UYVY, VideoPixelFormat.I420):
                    UYVYtoI420(src, dst, width, height);
                    break;
                case (VideoPixelFormat.MJPG, _):
                    throw new NotSupportedException(
                        "MJPG decode requires an Accelerator. Use GpuMjpgDecoder instead of PixelFormatConverter for MJPG frames.");
                default:
                    throw new NotSupportedException($"No conversion path from {srcFmt} to {dstFmt}");
            }
        }

        /// <summary>
        /// Calculate frame buffer size in bytes for a given format and dimensions.
        /// </summary>
        public static int GetFrameSize(VideoPixelFormat format, int width, int height) => format switch
        {
            VideoPixelFormat.BGRA or VideoPixelFormat.RGBA => width * height * 4,
            VideoPixelFormat.RGB24 => width * height * 3,
            VideoPixelFormat.NV12 or VideoPixelFormat.I420 => width * height * 3 / 2,
            VideoPixelFormat.YUY2 or VideoPixelFormat.UYVY => width * height * 2,
            _ => 0,
        };

        /// <summary>
        /// NV12 -> I420: deinterleave UV plane. Fast - no color math.
        /// NV12: [Y plane] [UV interleaved]
        /// I420: [Y plane] [U plane] [V plane]
        /// </summary>
        public static void NV12toI420(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
        {
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvHeight = height / 2;
            int uvPlaneSize = uvWidth * uvHeight;

            // Copy Y plane as-is
            src.Slice(0, ySize).CopyTo(dst);

            // Deinterleave UV -> separate U and V planes
            var uvSrc = src.Slice(ySize);
            var uDst = dst.Slice(ySize);
            var vDst = dst.Slice(ySize + uvPlaneSize);

            for (int i = 0; i < uvPlaneSize; i++)
            {
                uDst[i] = uvSrc[i * 2];
                vDst[i] = uvSrc[i * 2 + 1];
            }
        }

        /// <summary>
        /// NV12 -> BGRA: YUV to RGB color space conversion.
        /// BT.601 standard coefficients.
        /// </summary>
        public static void NV12toBGRA(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
        {
            int ySize = width * height;
            var uvSrc = src.Slice(ySize);

            for (int j = 0; j < height; j++)
            {
                int uvRow = j / 2;
                for (int i = 0; i < width; i++)
                {
                    int uvCol = i / 2;
                    int y = src[j * width + i];
                    int u = uvSrc[uvRow * width + uvCol * 2] - 128;
                    int v = uvSrc[uvRow * width + uvCol * 2 + 1] - 128;

                    int r = Clamp(y + (int)(1.402 * v));
                    int g = Clamp(y - (int)(0.344 * u) - (int)(0.714 * v));
                    int b = Clamp(y + (int)(1.772 * u));

                    int px = (j * width + i) * 4;
                    dst[px] = (byte)b;
                    dst[px + 1] = (byte)g;
                    dst[px + 2] = (byte)r;
                    dst[px + 3] = 255;
                }
            }
        }

        /// <summary>
        /// I420 -> BGRA: YUV 4:2:0 planar to BGRA.
        /// </summary>
        public static void I420toBGRA(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
        {
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvPlaneSize = uvWidth * (height / 2);
            var uPlane = src.Slice(ySize);
            var vPlane = src.Slice(ySize + uvPlaneSize);

            for (int j = 0; j < height; j++)
            {
                int uvRow = j / 2;
                for (int i = 0; i < width; i++)
                {
                    int uvCol = i / 2;
                    int y = src[j * width + i];
                    int u = uPlane[uvRow * uvWidth + uvCol] - 128;
                    int v = vPlane[uvRow * uvWidth + uvCol] - 128;

                    int r = Clamp(y + (int)(1.402 * v));
                    int g = Clamp(y - (int)(0.344 * u) - (int)(0.714 * v));
                    int b = Clamp(y + (int)(1.772 * u));

                    int px = (j * width + i) * 4;
                    dst[px] = (byte)b;
                    dst[px + 1] = (byte)g;
                    dst[px + 2] = (byte)r;
                    dst[px + 3] = 255;
                }
            }
        }

        /// <summary>
        /// BGRA -> I420: RGB to YUV 4:2:0 planar.
        /// </summary>
        public static void BGRAtoI420(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
        {
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvPlaneSize = uvWidth * (height / 2);
            var yDst = dst;
            var uDst = dst.Slice(ySize);
            var vDst = dst.Slice(ySize + uvPlaneSize);

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    int px = (j * width + i) * 4;
                    int b = src[px], g = src[px + 1], r = src[px + 2];

                    yDst[j * width + i] = (byte)Clamp((int)(0.257 * r + 0.504 * g + 0.098 * b) + 16);

                    if ((j & 1) == 0 && (i & 1) == 0)
                    {
                        int uvIdx = (j / 2) * uvWidth + (i / 2);
                        uDst[uvIdx] = (byte)Clamp((int)(-0.148 * r - 0.291 * g + 0.439 * b) + 128);
                        vDst[uvIdx] = (byte)Clamp((int)(0.439 * r - 0.368 * g - 0.071 * b) + 128);
                    }
                }
            }
        }

        /// <summary>
        /// YUY2 -> I420: packed 4:2:2 to planar 4:2:0.
        /// YUY2: [Y0 U0 Y1 V0] per 2 pixels.
        /// </summary>
        public static void YUY2toI420(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
        {
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvPlaneSize = uvWidth * (height / 2);
            var yDst = dst;
            var uDst = dst.Slice(ySize);
            var vDst = dst.Slice(ySize + uvPlaneSize);

            for (int j = 0; j < height; j++)
            {
                int srcRow = j * width * 2;
                for (int i = 0; i < width; i += 2)
                {
                    int srcIdx = srcRow + i * 2;
                    yDst[j * width + i] = src[srcIdx];       // Y0
                    yDst[j * width + i + 1] = src[srcIdx + 2]; // Y1

                    if ((j & 1) == 0)
                    {
                        int uvIdx = (j / 2) * uvWidth + (i / 2);
                        uDst[uvIdx] = src[srcIdx + 1]; // U
                        vDst[uvIdx] = src[srcIdx + 3]; // V
                    }
                }
            }
        }

        /// <summary>
        /// YUY2 -> BGRA: packed YUV 4:2:2 to BGRA.
        /// </summary>
        public static void YUY2toBGRA(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
        {
            for (int j = 0; j < height; j++)
            {
                int srcRow = j * width * 2;
                for (int i = 0; i < width; i += 2)
                {
                    int srcIdx = srcRow + i * 2;
                    int y0 = src[srcIdx], u = src[srcIdx + 1] - 128;
                    int y1 = src[srcIdx + 2], v = src[srcIdx + 3] - 128;

                    // Pixel 0
                    int px = (j * width + i) * 4;
                    dst[px] = (byte)Clamp(y0 + (int)(1.772 * u));
                    dst[px + 1] = (byte)Clamp(y0 - (int)(0.344 * u) - (int)(0.714 * v));
                    dst[px + 2] = (byte)Clamp(y0 + (int)(1.402 * v));
                    dst[px + 3] = 255;

                    // Pixel 1
                    px += 4;
                    dst[px] = (byte)Clamp(y1 + (int)(1.772 * u));
                    dst[px + 1] = (byte)Clamp(y1 - (int)(0.344 * u) - (int)(0.714 * v));
                    dst[px + 2] = (byte)Clamp(y1 + (int)(1.402 * v));
                    dst[px + 3] = 255;
                }
            }
        }

        /// <summary>
        /// RGB24 -> BGRA: add alpha channel, swap R and B.
        /// </summary>
        public static void RGB24toBGRA(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
        {
            int pixelCount = width * height;
            for (int i = 0; i < pixelCount; i++)
            {
                int s = i * 3, d = i * 4;
                dst[d] = src[s + 2];     // B (from src R position if RGB, or B if BGR)
                dst[d + 1] = src[s + 1]; // G
                dst[d + 2] = src[s];     // R
                dst[d + 3] = 255;        // A
            }
        }

        /// <summary>
        /// UYVY -> I420: packed 4:2:2 (U first) to planar 4:2:0.
        /// UYVY: [U0 Y0 V0 Y1] per 2 pixels.
        /// </summary>
        public static void UYVYtoI420(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height)
        {
            int ySize = width * height;
            int uvWidth = width / 2;
            int uvPlaneSize = uvWidth * (height / 2);
            var yDst = dst;
            var uDst = dst.Slice(ySize);
            var vDst = dst.Slice(ySize + uvPlaneSize);

            for (int j = 0; j < height; j++)
            {
                int srcRow = j * width * 2;
                for (int i = 0; i < width; i += 2)
                {
                    int srcIdx = srcRow + i * 2;
                    yDst[j * width + i] = src[srcIdx + 1];     // Y0
                    yDst[j * width + i + 1] = src[srcIdx + 3]; // Y1

                    if ((j & 1) == 0)
                    {
                        int uvIdx = (j / 2) * uvWidth + (i / 2);
                        uDst[uvIdx] = src[srcIdx];     // U
                        vDst[uvIdx] = src[srcIdx + 2]; // V
                    }
                }
            }
        }

        private static int Clamp(int val) => val < 0 ? 0 : val > 255 ? 255 : val;
    }
}
