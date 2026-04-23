using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// <see cref="IVideoEncoder"/> implementation for Windows using the MediaFoundation
    /// H.264 Encoder MFT. Thin wrapper around <see cref="H264EncoderMFT"/> that promotes
    /// its method surface to the platform-agnostic interface shape.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsH264Encoder : IVideoEncoder
    {
        private readonly H264EncoderMFT _mft;

        public WindowsH264Encoder(int width, int height, int fps, int bitrateBps)
        {
            _mft = new H264EncoderMFT(width, height, fps, bitrateBps);
        }

        public string Codec => "h264";
        public int Width => _mft.Width;
        public int Height => _mft.Height;
        public int FrameRate => _mft.FrameRate;
        public int BitrateBps => _mft.BitrateBps;
        public VideoPixelFormat PixelFormat => VideoPixelFormat.NV12;

        public byte[]? Encode(ReadOnlySpan<byte> frame, long timestamp100ns, long duration100ns)
        {
            _mft.Encode(frame, timestamp100ns, duration100ns, out var output);
            return output;
        }

        public byte[]? Drain() => _mft.Drain();

        public void Dispose() => _mft.Dispose();
    }
}
