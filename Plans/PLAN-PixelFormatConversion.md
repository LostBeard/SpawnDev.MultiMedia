# Plan: Pixel Format Conversion Pipeline

## Context

SpawnDev.MultiMedia captures video in whatever format the source provides (NV12, YUY2, MJPG, RGB24, BGRA, I420, UYVY). Different consumers need different formats:

- **SpawnDev.RTC (WebRTC)**: I420 preferred, NV12 acceptable
- **WPF display**: BGRA (WriteableBitmap)
- **ILGPU processing**: NV12 or I420 (GPU compute)
- **Browser**: never convert - native MediaStream handles everything

Without conversion, the consumer must handle every possible input format. With a conversion layer, the consumer requests their preferred format and gets it.

## Design

### Static converter class in the library

```csharp
public static class PixelFormatConverter
{
    // Convert a VideoFrame to the target format, returning a new VideoFrame
    // Returns the original frame if already in target format (zero-copy)
    public static VideoFrame Convert(VideoFrame source, VideoPixelFormat target);

    // Individual conversion methods for hot-path optimization
    public static void NV12toI420(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height);
    public static void NV12toBGRA(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height);
    public static void I420toBGRA(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height);
    public static void BGRAtoI420(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height);
    public static void YUY2toI420(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height);
    public static void YUY2toBGRA(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height);
    public static void RGB24toBGRA(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height);
    public static void UYVYtoI420(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height);
}
```

### Priority conversion paths

1. **NV12 -> I420** (WebRTC path) - Fast, just deinterleave UV plane. Most common.
2. **NV12 -> BGRA** (display path) - YUV->RGB color matrix. Standard.
3. **I420 -> BGRA** (display path) - Same color matrix, different plane layout.
4. **BGRA -> I420** (capture->encode) - Reverse color matrix. For RGB sources.
5. **YUY2 -> I420** (alternate camera format) - Unpack YUYV to planar YUV.
6. **YUY2 -> BGRA** (display) - Combined unpack + color matrix.
7. **RGB24 -> BGRA** (common DirectShow output) - Add alpha channel, swap bytes if needed.
8. **UYVY -> I420** (rare but exists) - Similar to YUY2, different byte order.
9. **MJPG -> BGRA or I420** - JPEG decompress. MF/DirectShow can handle this via decoder.

### Performance considerations

- Use `Span<byte>` for zero-alloc conversions
- Consider SIMD (Vector256/Vector128) for the YUV->RGB matrix multiply
- Buffer pooling for high-framerate paths (ArrayPool<byte>)
- Future: ILGPU GPU kernels for conversion on the GPU pipeline

### Where conversion lives

Two tiers:

1. **SpawnDev.MultiMedia** (base library) - `PixelFormatConverter` static class, CPU-side, Span-based, zero-alloc. Works everywhere without GPU dependency. Good for occasional conversion and low-res paths.

2. **SpawnDev.MultiMedia.GPU** (future package) - ILGPU-accelerated converter. References SpawnDev.ILGPU. Each conversion is a single kernel dispatch where every pixel is an independent work item. 1920x1080 NV12->BGRA is 2M parallel operations - microseconds on GPU vs milliseconds on CPU. Also enables zero-copy when source and destination are already GPU buffers (ILGPU.ML inference, VoxelEngine rendering). This is the production path for 60fps+ video.

The CPU converter is the right foundation. ILGPU converter builds on top of it with the same API surface but GPU execution.

### Implementation order

- [ ] NV12 -> I420 (Riker's primary need for WebRTC)
- [ ] NV12 -> BGRA (WPF display)
- [ ] I420 -> BGRA (received frames display)
- [ ] BGRA -> I420 (RGB camera sources to WebRTC)
- [ ] YUY2 -> I420
- [ ] YUY2 -> BGRA
- [ ] RGB24 -> BGRA
- [ ] UYVY -> I420
- [ ] MJPG decompression (use System.Drawing.Imaging or MF MFT)
- [ ] Unit tests with known test patterns (color bars, gradients)
