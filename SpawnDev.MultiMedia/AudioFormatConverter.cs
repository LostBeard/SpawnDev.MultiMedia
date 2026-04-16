namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Audio format conversion utilities.
    /// Handles conversion between WASAPI native format (typically 32-bit float)
    /// and common consumer formats (16-bit PCM for WebRTC/codecs).
    /// </summary>
    public static class AudioFormatConverter
    {
        /// <summary>
        /// Convert 32-bit IEEE float audio samples to 16-bit signed PCM (little-endian).
        /// This is the standard conversion for WASAPI shared mode -> WebRTC (G.711/Opus).
        /// </summary>
        public static byte[] Float32ToPcm16(ReadOnlySpan<byte> float32Data)
        {
            int sampleCount = float32Data.Length / 4;
            var pcm16 = new byte[sampleCount * 2];
            Float32ToPcm16(float32Data, pcm16);
            return pcm16;
        }

        /// <summary>
        /// Convert 32-bit IEEE float to 16-bit PCM in-place (Span-based).
        /// </summary>
        public static void Float32ToPcm16(ReadOnlySpan<byte> src, Span<byte> dst)
        {
            int sampleCount = src.Length / 4;
            var floatSrc = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(src);

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = floatSrc[i];
                // Clamp to [-1.0, 1.0] then scale to int16 range
                sample = sample < -1.0f ? -1.0f : sample > 1.0f ? 1.0f : sample;
                short pcmSample = (short)(sample * 32767f);
                dst[i * 2] = (byte)(pcmSample & 0xFF);
                dst[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
            }
        }

        /// <summary>
        /// Convert 16-bit signed PCM (little-endian) to 32-bit IEEE float.
        /// </summary>
        public static byte[] Pcm16ToFloat32(ReadOnlySpan<byte> pcm16Data)
        {
            int sampleCount = pcm16Data.Length / 2;
            var float32 = new byte[sampleCount * 4];
            Pcm16ToFloat32(pcm16Data, float32);
            return float32;
        }

        /// <summary>
        /// Convert 16-bit PCM to 32-bit float in-place (Span-based).
        /// </summary>
        public static void Pcm16ToFloat32(ReadOnlySpan<byte> src, Span<byte> dst)
        {
            int sampleCount = src.Length / 2;
            var floatDst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(dst);

            for (int i = 0; i < sampleCount; i++)
            {
                short pcmSample = (short)(src[i * 2] | (src[i * 2 + 1] << 8));
                floatDst[i] = pcmSample / 32768f;
            }
        }

        /// <summary>
        /// Convert an AudioFrame from float32 to 16-bit PCM.
        /// Returns the original if BitsPerSample is already 16.
        /// </summary>
        public static AudioFrame ConvertTopcm16(AudioFrame source)
        {
            if (source.Data.Length == 0) return source;

            // Assume float32 if 4 bytes per sample, int16 if 2 bytes
            int bytesPerSample = source.Data.Length / (source.SamplesPerChannel * source.ChannelCount);
            if (bytesPerSample <= 2) return source; // Already 16-bit or smaller

            var pcm16Data = Float32ToPcm16(source.Data.Span);
            return new AudioFrame(
                source.SampleRate,
                source.ChannelCount,
                source.SamplesPerChannel,
                new ReadOnlyMemory<byte>(pcm16Data),
                source.Timestamp);
        }
    }
}
