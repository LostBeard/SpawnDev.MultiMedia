namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Platform-agnostic video decoder interface: a compressed-codec elementary stream in (H.264 Annex-B access
    /// units), raw pixel frames out. The counterpart of <see cref="IVideoEncoder"/>.
    ///
    /// Implementations:
    /// - <see cref="SpawnDev.MultiMedia.Windows.WindowsH264Decoder"/> (Windows, MediaFoundation H.264 Decoder MFT)
    /// - Linux / macOS planned behind the same <see cref="VideoDecoderFactory"/> facade
    /// - Browser decodes through the native WebRTC / WebCodecs stack; no <c>IVideoDecoder</c> implementation there
    /// </summary>
    public interface IVideoDecoder : IDisposable
    {
        /// <summary>Codec identifier: <c>"h264"</c>.</summary>
        string Codec { get; }

        /// <summary>
        /// Feeds one access unit (for H.264: Annex-B NAL units with 00 00 00 01 start codes; the first call must include the SPS and PPS,
        /// which an encoder emits with each IDR frame) and returns every frame the decoder produced in response: zero while it is still
        /// waiting for a keyframe or buffering, normally one.
        /// </summary>
        /// <param name="accessUnit">The encoded bytes.</param>
        /// <param name="timestamp100ns">Presentation time in 100-nanosecond units; carried through to <see cref="VideoFrame.Timestamp"/>.</param>
        IReadOnlyList<VideoFrame> Decode(ReadOnlySpan<byte> accessUnit, long timestamp100ns);

        /// <summary>Pixel format of the frames returned by <see cref="Decode"/> (NV12: full-resolution Y plane, then interleaved half-resolution UV).</summary>
        VideoPixelFormat PixelFormat { get; }
    }

    /// <summary>Factory for platform-appropriate <see cref="IVideoDecoder"/> implementations.</summary>
    public static class VideoDecoderFactory
    {
        /// <summary>
        /// Creates a platform-default H.264 decoder. Windows only today (the MediaFoundation H.264 Decoder MFT); throws
        /// <see cref="PlatformNotSupportedException"/> elsewhere.
        /// </summary>
        public static IVideoDecoder CreateH264()
        {
            if (OperatingSystem.IsWindows())
                return new Windows.WindowsH264Decoder();

            throw new PlatformNotSupportedException(
                $"H.264 decoder is not yet implemented for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Windows ships via the MediaFoundation H.264 Decoder MFT today; Linux and macOS are planned.");
        }
    }
}
