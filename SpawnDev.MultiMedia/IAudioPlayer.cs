namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Audio playback interface for desktop platforms.
    /// Plays audio from an IAudioTrack through speakers.
    /// In browser, use HTML audio element instead.
    /// </summary>
    public interface IAudioPlayer : IDisposable
    {
        /// <summary>
        /// Start playing audio from the given track.
        /// </summary>
        void Play(IAudioTrack track);

        /// <summary>
        /// Stop playback.
        /// </summary>
        void Stop();

        /// <summary>
        /// Volume level from 0.0 (silent) to 1.0 (full volume).
        /// </summary>
        float Volume { get; set; }

        /// <summary>
        /// Whether playback is muted.
        /// </summary>
        bool Muted { get; set; }
    }
}
