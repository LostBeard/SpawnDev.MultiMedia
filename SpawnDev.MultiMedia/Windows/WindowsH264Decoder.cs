using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// <see cref="IVideoDecoder"/> for Windows over the MediaFoundation H.264 Decoder MFT. Thin wrapper around <see cref="H264DecoderMFT"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsH264Decoder : IVideoDecoder
    {
        private readonly H264DecoderMFT _mft = new();

        public string Codec => "h264";
        public VideoPixelFormat PixelFormat => VideoPixelFormat.NV12;

        public IReadOnlyList<VideoFrame> Decode(ReadOnlySpan<byte> accessUnit, long timestamp100ns) => _mft.Decode(accessUnit, timestamp100ns);

        public void Dispose() => _mft.Dispose();
    }
}
