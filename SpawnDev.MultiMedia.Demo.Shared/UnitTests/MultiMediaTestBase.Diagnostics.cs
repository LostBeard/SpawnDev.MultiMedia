using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    public abstract partial class MultiMediaTestBase
    {
        /// <summary>
        /// Lists all media devices on the system - cameras, microphones, speakers.
        /// This is a diagnostic test that prints device info to the console.
        /// </summary>
        [TestMethod]
        public async Task Diagnostic_ListAllDevices()
        {
            var devices = await MediaDevices.EnumerateDevices();
            if (devices.Length == 0)
                throw new Exception("No media devices found on this system");

            var report = $"Found {devices.Length} device(s):\n";
            foreach (var d in devices)
                report += $"  [{d.Kind}] {d.Label} (ID: {d.DeviceId.Substring(0, Math.Min(50, d.DeviceId.Length))}...)\n";

            // Pass the report as the exception message so the test runner captures it
            // The test passes - this is purely diagnostic
            Console.WriteLine(report);
        }
    }
}
