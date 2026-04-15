using System.Text.Json.Serialization;

namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Pixel format for video frames.
    /// </summary>
    public enum VideoPixelFormat
    {
        RGBA,
        BGRA,
        NV12,
        I420,
        YUY2,
    }

    /// <summary>
    /// A single video frame with raw pixel data.
    /// </summary>
    public class VideoFrame : IDisposable
    {
        public int Width { get; }
        public int Height { get; }
        public VideoPixelFormat Format { get; }
        public ReadOnlyMemory<byte> Data { get; }
        public long Timestamp { get; }

        public VideoFrame(int width, int height, VideoPixelFormat format, ReadOnlyMemory<byte> data, long timestamp)
        {
            Width = width;
            Height = height;
            Format = format;
            Data = data;
            Timestamp = timestamp;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// A single audio frame with raw PCM sample data.
    /// </summary>
    public class AudioFrame
    {
        public int SampleRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }
        public ReadOnlyMemory<byte> Data { get; }
        public long Timestamp { get; }

        public AudioFrame(int sampleRate, int channelCount, int samplesPerChannel, ReadOnlyMemory<byte> data, long timestamp)
        {
            SampleRate = sampleRate;
            ChannelCount = channelCount;
            SamplesPerChannel = samplesPerChannel;
            Data = data;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Information about a media input/output device.
    /// </summary>
    public class MediaDeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Label { get; set; } = "";
        public string GroupId { get; set; } = "";
    }

    /// <summary>
    /// Current settings of a media track (read-only snapshot).
    /// </summary>
    public class MediaTrackSettings
    {
        // Common
        public string? DeviceId { get; set; }
        public string? GroupId { get; set; }

        // Video
        public int? Width { get; set; }
        public int? Height { get; set; }
        public double? FrameRate { get; set; }
        public double? AspectRatio { get; set; }
        public string? FacingMode { get; set; }
        public string? ResizeMode { get; set; }

        // Audio
        public int? SampleRate { get; set; }
        public int? SampleSize { get; set; }
        public int? ChannelCount { get; set; }
        public bool? EchoCancellation { get; set; }
        public bool? AutoGainControl { get; set; }
        public bool? NoiseSuppression { get; set; }
        public double? Latency { get; set; }
    }

    /// <summary>
    /// Constraints for requesting specific media track properties.
    /// </summary>
    public class MediaTrackConstraints
    {
        [JsonPropertyName("width")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Height { get; set; }

        [JsonPropertyName("frameRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? FrameRate { get; set; }

        [JsonPropertyName("facingMode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FacingMode { get; set; }

        [JsonPropertyName("sampleRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SampleRate { get; set; }

        [JsonPropertyName("channelCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ChannelCount { get; set; }

        [JsonPropertyName("echoCancellation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EchoCancellation { get; set; }

        [JsonPropertyName("noiseSuppression")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? NoiseSuppression { get; set; }

        [JsonPropertyName("autoGainControl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? AutoGainControl { get; set; }

        [JsonPropertyName("deviceId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DeviceId { get; set; }
    }

    /// <summary>
    /// Constraints for GetUserMedia / GetDisplayMedia.
    /// </summary>
    public class MediaStreamConstraints
    {
        /// <summary>
        /// Audio constraint. Set to true for default audio, or a MediaTrackConstraints for specific settings.
        /// Null means no audio requested.
        /// </summary>
        [JsonPropertyName("audio")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Audio { get; set; }

        /// <summary>
        /// Video constraint. Set to true for default video, or a MediaTrackConstraints for specific settings.
        /// Null means no video requested.
        /// </summary>
        [JsonPropertyName("video")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Video { get; set; }
    }
}
