namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Extended audio track interface with raw sample access.
    /// On desktop: fires OnFrame with raw PCM samples from the microphone.
    /// In browser: use an audio element instead (OnFrame does not fire).
    /// </summary>
    public interface IAudioTrack : IMediaStreamTrack
    {
        /// <summary>
        /// Audio sample rate in Hz.
        /// </summary>
        int SampleRate { get; }

        /// <summary>
        /// Number of audio channels.
        /// </summary>
        int ChannelCount { get; }

        /// <summary>
        /// Bits per sample (typically 16 or 32).
        /// </summary>
        int BitsPerSample { get; }

        /// <summary>
        /// Raw audio sample callback for desktop playback/processing.
        /// In browser this event does not fire - use HTML audio element instead.
        /// </summary>
        event Action<AudioFrame>? OnFrame;
    }
}
