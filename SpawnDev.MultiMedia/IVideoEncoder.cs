namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Platform-agnostic video encoder interface. Takes raw pixel frames (NV12, I420, or
    /// similar) and produces a compressed-codec elementary stream (H.264 Annex-B, VP8
    /// IVF frames, etc.) suitable for RTP packetization or file muxing.
    ///
    /// Implementations:
    /// - <see cref="SpawnDev.MultiMedia.Windows.WindowsH264Encoder"/> (Windows, MediaFoundation)
    /// - Linux VAAPI + macOS VideoToolbox planned for Phase 5
    /// - Browser uses native WebRTC stack directly; no <c>IVideoEncoder</c> impl there
    ///
    /// Factory: <see cref="VideoEncoderFactory.CreateH264"/> picks the right platform
    /// implementation at runtime.
    /// </summary>
    public interface IVideoEncoder : IDisposable
    {
        /// <summary>Codec identifier: <c>"h264"</c>, <c>"vp8"</c>, <c>"av1"</c>, etc.</summary>
        string Codec { get; }

        /// <summary>Frame width in pixels (immutable after construction).</summary>
        int Width { get; }

        /// <summary>Frame height in pixels (immutable after construction).</summary>
        int Height { get; }

        /// <summary>Target frame rate in Hz (immutable after construction).</summary>
        int FrameRate { get; }

        /// <summary>Target bitrate in bits per second (immutable after construction - restart to change).</summary>
        int BitrateBps { get; }

        /// <summary>
        /// Encode a single frame and return any encoded output the codec produces in
        /// response. Output may be <c>null</c> if the codec is buffering (rare in low-
        /// latency mode). The first non-null output for a fresh encoder session contains
        /// the codec-specific parameter sets (e.g., H.264 SPS + PPS) prepended to the
        /// first IDR frame.
        /// </summary>
        /// <param name="frame">Raw pixel data. Format is encoder-specific; <see cref="PixelFormat"/> names it.</param>
        /// <param name="timestamp100ns">Presentation timestamp in 100-nanosecond units (Media Foundation's native unit; easily converted to any other).</param>
        /// <param name="duration100ns">Frame duration in 100-nanosecond units.</param>
        /// <returns>Encoded output bytes (codec-specific format; H.264 is Annex-B with 00 00 00 01 NAL start codes). Null if no output this call.</returns>
        byte[]? Encode(ReadOnlySpan<byte> frame, long timestamp100ns, long duration100ns);

        /// <summary>
        /// Input pixel format the encoder accepts. Callers must feed frames in this exact
        /// format; use <see cref="PixelFormatConverter"/> upstream if the source frame is
        /// in a different layout.
        /// </summary>
        VideoPixelFormat PixelFormat { get; }

        /// <summary>
        /// Drain any output the encoder is still holding (call before <see cref="IDisposable.Dispose"/>
        /// to flush trailing frames at end of stream). Returns concatenated Annex-B /
        /// codec-native bytes, or null if nothing buffered.
        /// </summary>
        byte[]? Drain();
    }

    /// <summary>
    /// Factory for platform-appropriate <see cref="IVideoEncoder"/> implementations.
    /// Dispatches via <see cref="OperatingSystem"/> checks at runtime.
    /// </summary>
    public static class VideoEncoderFactory
    {
        /// <summary>
        /// Create a platform-default H.264 encoder. Currently Windows-only (MediaFoundation
        /// H.264 Encoder MFT). Throws <see cref="PlatformNotSupportedException"/> on other
        /// platforms until Phase 5 ships Linux / macOS implementations.
        /// </summary>
        public static IVideoEncoder CreateH264(int width, int height, int fps, int bitrateBps)
        {
            if (OperatingSystem.IsWindows())
                return new Windows.WindowsH264Encoder(width, height, fps, bitrateBps);

            throw new PlatformNotSupportedException(
                $"H.264 encoder is not yet implemented for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Windows ships via MediaFoundation MFT today; Linux VAAPI and macOS VideoToolbox are planned as Phase 5.");
        }
    }
}
