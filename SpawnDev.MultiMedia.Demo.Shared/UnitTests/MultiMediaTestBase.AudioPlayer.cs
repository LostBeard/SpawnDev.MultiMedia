using SpawnDev.MultiMedia;
using SpawnDev.UnitTesting;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Demo.Shared.UnitTests
{
    /// <summary>
    /// Tests for <see cref="SpawnDev.MultiMedia.IAudioPlayer"/> and its Windows
    /// implementation <see cref="SpawnDev.MultiMedia.Windows.WindowsAudioPlayer"/>.
    /// Property + lifecycle tests only - actual audio playback requires a hardware output
    /// device and is verified by ear via the WpfDemo. These tests lock in the contract so
    /// a future refactor of WASAPI interop can't silently break the interface shape.
    /// </summary>
    public abstract partial class MultiMediaTestBase
    {
        [TestMethod]
        public async Task AudioPlayer_Volume_Clamps()
        {
            if (OperatingSystem.IsBrowser()) return;  // Windows-only impl today

            RunVolumeClampTest();
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task AudioPlayer_Muted_GetSet()
        {
            if (OperatingSystem.IsBrowser()) return;

            RunMutedGetSetTest();
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task AudioPlayer_Dispose_IsSafe()
        {
            if (OperatingSystem.IsBrowser()) return;

            RunDisposeSafetyTest();
            await Task.CompletedTask;
        }

        [TestMethod]
        public async Task AudioPlayer_Stop_WithoutPlay_DoesNotThrow()
        {
            if (OperatingSystem.IsBrowser()) return;

            RunStopWithoutPlayTest();
            await Task.CompletedTask;
        }

        [SupportedOSPlatform("windows")]
        private static void RunVolumeClampTest()
        {
            using var player = new SpawnDev.MultiMedia.Windows.WindowsAudioPlayer();

            player.Volume = 0.5f;
            if (Math.Abs(player.Volume - 0.5f) > 0.001f) throw new Exception($"Volume 0.5 round-trip: {player.Volume}");

            player.Volume = -1.0f;
            if (player.Volume != 0f) throw new Exception($"Volume -1.0 must clamp to 0, got {player.Volume}");

            player.Volume = 10.0f;
            if (player.Volume != 1f) throw new Exception($"Volume 10.0 must clamp to 1, got {player.Volume}");

            player.Volume = 1.0f;
            if (player.Volume != 1f) throw new Exception($"Volume 1.0 round-trip: {player.Volume}");

            player.Volume = 0f;
            if (player.Volume != 0f) throw new Exception($"Volume 0.0 round-trip: {player.Volume}");
        }

        [SupportedOSPlatform("windows")]
        private static void RunMutedGetSetTest()
        {
            using var player = new SpawnDev.MultiMedia.Windows.WindowsAudioPlayer();

            if (player.Muted) throw new Exception("Default Muted must be false");

            player.Muted = true;
            if (!player.Muted) throw new Exception("Muted setter true didn't apply");

            player.Muted = false;
            if (player.Muted) throw new Exception("Muted setter false didn't apply");
        }

        [SupportedOSPlatform("windows")]
        private static void RunDisposeSafetyTest()
        {
            var player = new SpawnDev.MultiMedia.Windows.WindowsAudioPlayer();
            player.Dispose();
            player.Dispose(); // must be idempotent
        }

        [SupportedOSPlatform("windows")]
        private static void RunStopWithoutPlayTest()
        {
            using var player = new SpawnDev.MultiMedia.Windows.WindowsAudioPlayer();
            // Stop before any Play should be a safe no-op, not a NRE on the internal audio
            // client handle.
            player.Stop();
        }
    }
}
