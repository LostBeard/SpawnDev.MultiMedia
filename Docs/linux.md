# Linux Support

Linux is a partial platform today. Device enumeration works; capture (getUserMedia) does not yet.

## What works

### `MediaDevices.EnumerateDevices()`

Returns `MediaDeviceInfo[]` for:

- **Video inputs:** one per `/dev/video*` node. `DeviceId` is the device path, `Label` is the node filename, `Kind = "videoinput"`, `GroupId = "v4l2"`.
- **Audio inputs:** one per ALSA card in `/proc/asound/cards`. `DeviceId` is `hw:N` (ALSA hardware reference), `Label` is `"<cardId> (<cardDescription>)"`, `Kind = "audioinput"`, `GroupId = "alsa"`.

No V4L2 ioctl required for enumeration — just filesystem reads. Works in any process with read access to `/dev` and `/proc/asound`.

## What doesn't work yet

### `GetUserMedia(...)` — throws `PlatformNotSupportedException`

Actual frame / sample capture needs:

1. **V4L2 ioctl** via P/Invoke to `libc` — roughly: `VIDIOC_QUERYCAP`, `VIDIOC_ENUM_FMT`, `VIDIOC_S_FMT`, `VIDIOC_REQBUFS`, `VIDIOC_QBUF` / `VIDIOC_DQBUF`, `VIDIOC_STREAMON`. Plus memory-mapping the driver's frame buffers.
2. **PulseAudio simple API** (`libpulse-simple`) via P/Invoke — `pa_simple_new`, `pa_simple_read`, `pa_simple_free`. ALSA PCM (`libasound`) is the fallback when PulseAudio isn't running.
3. **Format conversion** from V4L2 pixel formats (YUYV, NV12, MJPEG) into the library's common `VideoPixelFormat` enum, and PCM16/Float32 audio frames.

Estimated scope: ~500 lines of P/Invoke + buffer management + format conversion per platform component. Landed as a separate phase.

### `GetDisplayMedia(...)` — throws `PlatformNotSupportedException`

Screen capture on Linux needs either:

- **XDG portal** (`org.freedesktop.portal.ScreenCast`) — the modern / Wayland-compatible path. D-Bus method calls.
- **wlroots screencopy** — Wayland-native on wlroots-based compositors.
- **X11 XShm + XDamage** — X11-session fallback.

Phase 5 work.

## Testing against WSL2

Development on a Windows host can exercise the Linux impl via WSL2. Two things to wire up:

### USB cameras via usbipd

```powershell
# On Windows (PowerShell, admin). Install once:
winget install usbipd
# List USB devices, find your camera:
usbipd list
# Bind + attach the device to WSL:
usbipd bind --busid 1-4
usbipd attach --wsl --busid 1-4
```

Inside the WSL2 distro:

```bash
# Verify the device is visible:
ls -l /dev/video*
# Should show at least /dev/video0 for the attached camera.
# Current user needs to be in the 'video' group:
sudo usermod -aG video $USER  # logout/login for effect
```

### Microphone via PulseAudio WSL interop

WSL2 ships a PulseAudio server that proxies to the Windows host's audio stack. ALSA enumeration through `/proc/asound/cards` should already work without additional setup.

```bash
# Verify ALSA sees at least one card:
cat /proc/asound/cards
# Typical output when PulseAudio WSL interop is active:
#  0 [UltraSpeakers ]: HDA-Intel - HDA Intel PCH
#                      HDA Intel PCH at 0xa3240000 irq 172
```

### Running the enumeration test

From inside the WSL distro with the repo checked out at the same path (via `/mnt/d/users/tj/Projects/SpawnDev.MultiMedia`):

```bash
cd /mnt/d/users/tj/Projects/SpawnDev.MultiMedia/SpawnDev.MultiMedia/SpawnDev.MultiMedia.DemoConsole
dotnet run --no-build -c Release -- "DesktopTests.EnumerateDevices_ReturnsNonNullArray"
dotnet run --no-build -c Release -- "DesktopTests.EnumerateDevices_DevicesHaveKind"
```

These two existing tests exercise the enumeration path platform-agnostically — the Linux impl hooks in via `MediaDevices.EnumerateDevices()` without any test-side changes.

## Alternative: Docker

A minimal Dockerfile for CI Linux enumeration checks:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /app
COPY . .
# Mount a loopback V4L2 device into the container for enumeration tests (host needs v4l2loopback):
# docker run --device /dev/video0:/dev/video0 ...
RUN dotnet build -c Release
CMD dotnet test -c Release --no-build
```

Docker can't access host hardware by default; for capture testing you need `--device /dev/video0` + `--group-add video` at run time. For enumeration-only tests the `/proc/asound/cards` file is synthesized from the container's view and works out of the box.

## macOS

Not supported today. Phase 5 items are AVFoundation (video capture) + CoreAudio (audio capture/playback). No dev environment for this yet (no macOS hardware on TJ's setup); contributions welcome.
