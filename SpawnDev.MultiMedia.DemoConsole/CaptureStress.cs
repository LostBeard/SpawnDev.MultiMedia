using SpawnDev.MultiMedia;

/// <summary>
/// Stress harness for the intermittent desktop video-capture process crash ("Internal CLR error
/// 0x80131506" / AccessViolation on the DirectShow capture thread). Runs many capture sessions with ONE
/// ingredient varied per mode, so the ingredient that corrupts memory can be isolated. Runs before the
/// unit-test types (and ILGPU) are touched. Usage:
///   SpawnDev.MultiMedia.DemoConsole.exe capture-stress &lt;mode&gt; &lt;rounds&gt; [camera label substring]
/// Modes:
///   open-close      GetUserMedia, wait 300 ms, Dispose - no OnFrame subscription
///   frames          subscribe OnFrame, wait for 3 frames, Dispose from this thread
///   frames-nodata   like frames, but the handler never touches the frame
///   dispose-inframe the handler disposes the stream on the capture thread
/// Prints "STRESS OK" after the last round.
/// </summary>
internal static class CaptureStress
{
    public static async Task<int> Run(string[] args)
    {
        string mode = args.Length > 1 ? args[1] : "frames";
        int rounds = args.Length > 2 ? int.Parse(args[2]) : 10;
        // Optional 4th argument: a substring of the camera label to open (default: the first camera).
        MediaConstraint videoRequest = true;
        if (args.Length > 3)
        {
            var dev = (await MediaDevices.EnumerateDevices())
                .First(d => d.Kind == "videoinput" && d.Label.Contains(args[3], StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"device: {dev.Label} ({dev.DeviceId})");
            videoRequest = new MediaTrackConstraints { DeviceId = dev.DeviceId };
        }
        for (int round = 0; round < rounds; round++)
        {
            var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints { Video = videoRequest });
            var track = stream.GetVideoTracks()[0];
            var video = (IVideoTrack)track;
            switch (mode)
            {
                case "open-close":
                    await Task.Delay(300);
                    stream.Dispose();
                    break;
                case "open-gc":
                {
                    // No OnFrame subscription, but the same allocation pressure the frame path creates
                    // (3.1 MB arrays at ~60/s) from another thread: isolates GC activity from the frame copy.
                    var until = DateTime.UtcNow.AddMilliseconds(1000);
                    await Task.Run(() =>
                    {
                        long keep = 0;
                        while (DateTime.UtcNow < until) { var a = new byte[3_110_400]; keep += a.Length; Thread.Sleep(16); }
                        return keep;
                    });
                    stream.Dispose();
                    break;
                }
                case "frames":
                case "frames-nodata":
                {
                    var got = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    int n = 0;
                    long sum = 0;
                    video.OnFrame += f =>
                    {
                        if (mode == "frames") sum += f.Data.Length;
                        if (Interlocked.Increment(ref n) == 3) got.TrySetResult();
                    };
                    if (await Task.WhenAny(got.Task, Task.Delay(10000)) != got.Task) throw new Exception($"round {round}: no frames");
                    stream.Dispose();
                    break;
                }
                case "dispose-inframe":
                {
                    var got = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    int handled = 0;
                    video.OnFrame += f =>
                    {
                        if (Interlocked.Exchange(ref handled, 1) != 0) return;
                        stream.Dispose();
                        got.TrySetResult();
                    };
                    if (await Task.WhenAny(got.Task, Task.Delay(10000)) != got.Task) throw new Exception($"round {round}: no frames");
                    break;
                }
                default:
                    throw new ArgumentException($"unknown mode {mode}");
            }
            for (int i = 0; i < 100 && track.ReadyState != "ended"; i++) await Task.Delay(20);
            Console.WriteLine($"round {round} ok ({track.ReadyState})");
        }
        Console.WriteLine("STRESS OK");
        return 0;
    }
}
