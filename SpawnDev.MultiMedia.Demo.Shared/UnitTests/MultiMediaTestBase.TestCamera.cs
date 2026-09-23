using SpawnDev.MultiMedia;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    /// <summary>
    /// Which camera the desktop video tests open, and making them take turns on it.
    /// <para>
    /// WHICH: the tests were written against OBS Virtual Camera, but <c>Video = true</c> opens the FIRST
    /// video device, and on a machine with Meta Quest Link installed that is a Meta Quest virtual camera
    /// whose DirectShow filter corrupts the process under GC activity (access violation / "Internal CLR
    /// error 0x80131506"; Meta Quest 3/3S/2/Pro crash every stress run, OBS Virtual Camera 0 of 160
    /// sessions, same code). So prefer OBS when it is installed, else fall back to the first camera.
    /// </para>
    /// <para>
    /// TURNS: PlaywrightMultiTest runs each desktop test in its own process, 4 at a time, and a camera
    /// another process is capturing from cannot be opened - DirectShow's RenderStream fails with
    /// E_INVALIDARG, surfaced as <see cref="MediaDeviceException"/> NotReadableError (measured: one
    /// process 10/10 opens, four concurrent processes 3 of 4 fail). GetUserMedia used to hide that behind
    /// a stub track. A machine-wide named semaphore serializes the camera tests; the wait is bounded so a
    /// holder that crashed cannot stall the rest of the sweep (the kernel object dies with the last handle).
    /// The browser needs neither: its tests run on Chrome's fake device.
    /// </para>
    /// </summary>
    public abstract partial class MultiMediaTestBase
    {
        const string PreferredTestCamera = "OBS Virtual Camera";
        const string TestCameraSemaphoreName = @"Global\SpawnDev.MultiMedia.TestCamera";

        /// <summary>Holds the test camera for the lifetime of a test. Declare it before the stream so it is released after.</summary>
        protected sealed class TestCameraLease : IDisposable
        {
            readonly Semaphore? _semaphore;
            public MediaConstraint Video { get; }
            internal TestCameraLease(MediaConstraint video, Semaphore? semaphore)
            {
                Video = video;
                _semaphore = semaphore;
            }
            public void Dispose()
            {
                if (_semaphore == null) return;
                try { _semaphore.Release(); } catch (SemaphoreFullException) { }
                _semaphore.Dispose();
            }
        }

        protected static async Task<TestCameraLease> AcquireTestCamera()
        {
            if (OperatingSystem.IsBrowser()) return new TestCameraLease(true, null);
            Semaphore? semaphore = null;
            if (OperatingSystem.IsWindows())
            {
                semaphore = new Semaphore(1, 1, TestCameraSemaphoreName);
                // Wait on a pool thread; a bounded wait means a crashed holder costs time, not the sweep.
                if (!await Task.Run(() => semaphore.WaitOne(TimeSpan.FromSeconds(90))))
                {
                    semaphore.Dispose();
                    semaphore = null;
                }
            }
            var obs = (await MediaDevices.EnumerateDevices())
                .FirstOrDefault(d => d.Kind == "videoinput" && d.Label.Contains(PreferredTestCamera, StringComparison.OrdinalIgnoreCase));
            MediaConstraint video = obs != null ? new MediaTrackConstraints { DeviceId = obs.DeviceId } : true;
            return new TestCameraLease(video, semaphore);
        }
    }
}
