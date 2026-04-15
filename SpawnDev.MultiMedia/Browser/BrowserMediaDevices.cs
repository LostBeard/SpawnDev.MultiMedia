using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace SpawnDev.MultiMedia.Browser
{
    /// <summary>
    /// Browser implementation of media device access.
    /// Wraps navigator.mediaDevices via SpawnDev.BlazorJS.
    /// </summary>
    public static class BrowserMediaDevices
    {
        public static async Task<IMediaStream> GetUserMedia(MediaStreamConstraints constraints)
        {
            var JS = BlazorJSRuntime.JS;
            using var navigator = JS.Get<Navigator>("navigator");
            using var mediaDevices = navigator.MediaDevices;
            var jsConstraints = ToBlazorJSConstraints(constraints);
            var stream = await mediaDevices.GetUserMedia(jsConstraints);
            if (stream == null) throw new InvalidOperationException("getUserMedia returned null.");
            return new BrowserMediaStream(stream);
        }

        public static async Task<IMediaStream> GetDisplayMedia(MediaStreamConstraints? constraints)
        {
            var JS = BlazorJSRuntime.JS;
            using var navigator = JS.Get<Navigator>("navigator");
            using var mediaDevices = navigator.MediaDevices;
            MediaStream? stream;
            if (constraints != null)
            {
                var jsConstraints = ToBlazorJSConstraints(constraints);
                stream = await mediaDevices.GetDisplayMedia(jsConstraints);
            }
            else
            {
                stream = await mediaDevices.GetDisplayMedia();
            }
            if (stream == null) throw new InvalidOperationException("getDisplayMedia returned null.");
            return new BrowserMediaStream(stream);
        }

        public static async Task<MediaDeviceInfo[]> EnumerateDevices()
        {
            var JS = BlazorJSRuntime.JS;
            using var navigator = JS.Get<Navigator>("navigator");
            using var mediaDevices = navigator.MediaDevices;
            var jsDevices = await mediaDevices.EnumerateDevices();
            var result = new MediaDeviceInfo[jsDevices.Length];
            for (int i = 0; i < jsDevices.Length; i++)
            {
                using var d = jsDevices[i];
                result[i] = new MediaDeviceInfo
                {
                    DeviceId = d.DeviceId,
                    Kind = d.Kind,
                    Label = d.Label,
                    GroupId = d.GroupId,
                };
            }
            return result;
        }

        private static SpawnDev.BlazorJS.JSObjects.MediaStreamConstraints ToBlazorJSConstraints(MediaStreamConstraints constraints)
        {
            // BlazorJS MediaStreamConstraints uses Union<bool, MediaTrackConstraints> for Audio/Video
            // Our simplified version uses object? - pass through as-is since it's JSON-compatible
            var jsc = new SpawnDev.BlazorJS.JSObjects.MediaStreamConstraints();
            if (constraints.Audio is bool audioBool)
                jsc.Audio = audioBool;
            else if (constraints.Audio is MediaTrackConstraints audioConstraints)
                jsc.Audio = ToBlazorJSTrackConstraints(audioConstraints);
            else if (constraints.Audio != null)
                jsc.Audio = true;

            if (constraints.Video is bool videoBool)
                jsc.Video = videoBool;
            else if (constraints.Video is MediaTrackConstraints videoConstraints)
                jsc.Video = ToBlazorJSTrackConstraints(videoConstraints);
            else if (constraints.Video != null)
                jsc.Video = true;

            return jsc;
        }

        private static SpawnDev.BlazorJS.JSObjects.MediaTrackConstraints ToBlazorJSTrackConstraints(MediaTrackConstraints c)
        {
            var jsc = new SpawnDev.BlazorJS.JSObjects.MediaTrackConstraints();
            if (c.Width.HasValue) jsc.Width = (uint)c.Width.Value;
            if (c.Height.HasValue) jsc.Height = (uint)c.Height.Value;
            if (c.FrameRate.HasValue) jsc.FrameRate = c.FrameRate.Value;
            if (c.SampleRate.HasValue) jsc.SampleRate = (uint)c.SampleRate.Value;
            if (c.ChannelCount.HasValue) jsc.ChannelCount = (uint)c.ChannelCount.Value;
            if (c.EchoCancellation.HasValue) jsc.EchoCancellation = c.EchoCancellation.Value;
            if (c.NoiseSuppression.HasValue) jsc.NoiseSuppression = c.NoiseSuppression.Value;
            if (c.AutoGainControl.HasValue) jsc.AutoGainControl = c.AutoGainControl.Value;
            if (c.DeviceId != null) jsc.DeviceId = c.DeviceId;
            return jsc;
        }
    }
}
