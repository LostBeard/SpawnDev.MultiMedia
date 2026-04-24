using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace SpawnDev.MultiMedia.Linux
{
    /// <summary>
    /// Linux media device access. Device enumeration via <c>/dev/video*</c> (V4L2) +
    /// <c>/proc/asound/cards</c> (ALSA) works today; capture (GetUserMedia) is still
    /// pending a full V4L2 ioctl + PulseAudio / ALSA PCM binding.
    /// </summary>
    /// <remarks>
    /// <para><strong>What works today:</strong> <see cref="EnumerateDevices"/> reads
    /// <c>/dev/video*</c> (each device node is one V4L2 capture interface) and
    /// <c>/proc/asound/cards</c> (each ALSA card is an audio input candidate). Good
    /// enough for enumeration-driven UI, "which devices does the system see" probes,
    /// and validation that the process has the right permissions (video group
    /// membership for /dev/video*).</para>
    ///
    /// <para><strong>What doesn't work yet:</strong> <see cref="GetUserMedia"/> throws
    /// <see cref="PlatformNotSupportedException"/>. A proper impl needs P/Invoke to
    /// libc for V4L2 ioctl (VIDIOC_QUERYCAP, VIDIOC_ENUM_FMT, VIDIOC_S_FMT,
    /// VIDIOC_REQBUFS, VIDIOC_QBUF / DQBUF, VIDIOC_STREAMON) + PulseAudio simple API
    /// (pa_simple_new, pa_simple_read) or ALSA PCM (snd_pcm_open, snd_pcm_readi).
    /// Both are 200-500 lines of P/Invoke each plus format conversion to the
    /// library's common pixel / sample formats. Tracked as future work.</para>
    ///
    /// <para><strong>Testing against WSL2:</strong> USB cameras pass through via
    /// <c>usbipd</c> (Windows 11 feature) with <c>sudo modprobe v4l2loopback</c> on
    /// the WSL side; microphones work via the builtin PulseAudio WSL interop. See
    /// <c>Docs/linux.md</c> for the full setup runbook.</para>
    /// </remarks>
    [SupportedOSPlatform("linux")]
    public static class LinuxMediaDevices
    {
        /// <summary>
        /// Enumerate video inputs (/dev/videoN) + audio inputs (ALSA cards) visible
        /// to the current process. Does not probe formats or capabilities - that
        /// needs V4L2 ioctls not yet plumbed.
        /// </summary>
        public static Task<MediaDeviceInfo[]> EnumerateDevices()
        {
            var devices = new List<MediaDeviceInfo>();

            // Video: /dev/video0 / video1 / ... - each is a V4L2 capture node. We
            // can't read per-device "friendly name" without VIDIOC_QUERYCAP, so the
            // label is the node path itself - consumers can map to pretty names
            // when the capture layer lands.
            try
            {
                foreach (var path in Directory.EnumerateFiles("/dev", "video*", SearchOption.TopDirectoryOnly).OrderBy(p => p))
                {
                    devices.Add(new MediaDeviceInfo
                    {
                        Kind = "videoinput",
                        DeviceId = path,
                        Label = Path.GetFileName(path),
                        GroupId = "v4l2",
                    });
                }
            }
            catch (DirectoryNotFoundException) { /* /dev absent - unusual but survivable */ }
            catch (UnauthorizedAccessException) { /* no perms - enumerate as empty rather than throw */ }

            // Audio: /proc/asound/cards lists ALSA cards (one per sound device). Format:
            //   ` 0 [<id>        ]: <driver> - <name>`
            //   `                  <long name>`
            try
            {
                var cardsPath = "/proc/asound/cards";
                if (File.Exists(cardsPath))
                {
                    var content = File.ReadAllText(cardsPath);
                    // Match lines like " 0 [HDMI           ]: HDA-Intel - HDA Intel HDMI"
                    var cardRegex = new Regex(@"^\s*(\d+)\s+\[([^\]]+)\]:\s*([^\r\n]+)", RegexOptions.Multiline);
                    foreach (Match m in cardRegex.Matches(content))
                    {
                        var cardIndex = m.Groups[1].Value.Trim();
                        var cardId = m.Groups[2].Value.Trim();
                        var cardDesc = m.Groups[3].Value.Trim();
                        devices.Add(new MediaDeviceInfo
                        {
                            Kind = "audioinput",
                            DeviceId = $"hw:{cardIndex}",
                            Label = $"{cardId} ({cardDesc})",
                            GroupId = "alsa",
                        });
                    }
                }
            }
            catch (IOException) { /* proc read failed - enumerate as empty */ }
            catch (UnauthorizedAccessException) { }

            return Task.FromResult(devices.ToArray());
        }

        /// <summary>
        /// V4L2 + PulseAudio capture not yet implemented; enumerate-only today.
        /// Throws <see cref="PlatformNotSupportedException"/> with a pointer to the
        /// doc explaining what's needed to complete it.
        /// </summary>
        public static Task<IMediaStream> GetUserMedia(MediaStreamConstraints constraints)
        {
            throw new PlatformNotSupportedException(
                "Linux V4L2 / PulseAudio capture is not yet implemented. " +
                "EnumerateDevices works today via /dev/video* + /proc/asound/cards; capture needs V4L2 ioctl + PCM bindings (~500 lines of P/Invoke). " +
                "See SpawnDev.MultiMedia/Docs/linux.md for scope + WSL2 test setup.");
        }

        /// <summary>
        /// Linux desktop screen capture via XDG portal / wlroots screencopy is
        /// Phase 5 work and not yet implemented.
        /// </summary>
        public static Task<IMediaStream> GetDisplayMedia(MediaStreamConstraints? constraints = null)
        {
            throw new PlatformNotSupportedException(
                "Linux GetDisplayMedia is Phase 5 work (XDG portal or wlroots screencopy integration). Not yet implemented.");
        }
    }
}
