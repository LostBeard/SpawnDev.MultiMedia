# SpawnDev.MultiMedia

[![NuGet](https://img.shields.io/nuget/v/SpawnDev.MultiMedia.svg)](https://www.nuget.org/packages/SpawnDev.MultiMedia)
[![License](https://img.shields.io/github/license/LostBeard/SpawnDev.MultiMedia)](https://github.com/LostBeard/SpawnDev.MultiMedia/blob/master/LICENSE.txt)

Cross-platform media capture and playback for .NET — camera, microphone, speakers, video display. One API, browser and desktop.

## Install

```xml
<PackageReference Include="SpawnDev.MultiMedia" Version="0.2.0-rc.1" />
```

Or:

```bash
dotnet add package SpawnDev.MultiMedia --prerelease
```

## Features

- **Cross-platform** — Browser (Blazor WASM) full, Windows full, Linux device enumeration today + capture in progress (see [Docs/linux.md](Docs/linux.md)), macOS planned for Phase 5.
- **Camera capture** — Webcams and virtual cameras (OBS, ManyCam, Quest) with resolution / framerate constraints.
- **Microphone capture** — Audio input with sample-rate / channel-count controls.
- **Audio playback** — WASAPI render for desktop speaker output.
- **Device enumeration** — Cameras, microphones, speakers, plus per-platform device path / id surface.
- **Screen capture** — Browser `getDisplayMedia` + desktop DXGI Desktop Duplication on Windows.
- **Raw frame access** — Desktop video / audio frame callbacks via `OnFrame` events on `IVideoTrack` / `IAudioTrack`. `ReadOnlyMemory<byte>` payloads for zero-copy downstream processing.
- **Pixel format conversion** — NV12, I420, YUY2, BGRA, RGB24, UYVY. CPU paths (`PixelFormatConverter`) AND ILGPU GPU paths (`GpuPixelFormatConverter`); the GPU paths run on all 6 ILGPU backends (WebGPU, WebGL, Wasm, CUDA, OpenCL, CPU).
- **Pixel format selection** — Request NV12 (zero-copy into encoders) or BGRA (display-ready) via `MediaTrackConstraints.PixelFormat`.
- **MJPG decode on GPU** — JPEG parse + Huffman decode (CPU sequential) feeds GPU dequantize + IDCT + color convert kernels.
- **H.264 encoding (Windows)** — `IVideoEncoder` + `VideoEncoderFactory.CreateH264()` wrap the OS MediaFoundation H.264 MFT (Intel Quick Sync / NVIDIA NVENC / AMD VCE where available). Baseline profile, low-latency, CBR; outputs Annex-B NAL units. Linux VAAPI / macOS VideoToolbox are per-OS follow-ups.
- **Zero external media NuGet deps** — Platform APIs via P/Invoke (MediaFoundation, DirectShow, WASAPI on Windows; V4L2 / PulseAudio / ALSA stubs on Linux).
- **WebRTC consumption via [SpawnDev.RTC](https://github.com/LostBeard/SpawnDev.RTC)** — `SpawnDev.RTC.Desktop.DesktopRTCPeerConnection.AddTrack(IAudioTrack)` and `AddTrack(IVideoTrack)` consume MultiMedia microphone + camera tracks directly; the audio bridge encodes Opus / PCMU / PCMA / G722 via SipSorcery's RTP audio path, the video bridge encodes H.264 via the MFT and emits RFC 6184 RTP. One API call to turn a WASAPI mic + webcam into a live audio + video call with a browser peer. See [SpawnDev.RTC `Docs/audio-tracks.md`](https://github.com/LostBeard/SpawnDev.RTC/blob/master/SpawnDev.RTC/Docs/audio-tracks.md) and [`Docs/video-tracks.md`](https://github.com/LostBeard/SpawnDev.RTC/blob/master/SpawnDev.RTC/Docs/video-tracks.md).

## Quick Start

```csharp
using SpawnDev.MultiMedia;

// Get camera + mic - same code on browser, Windows, Linux (enumeration), future macOS
var stream = await MediaDevices.GetUserMedia(new MediaStreamConstraints
{
    Video = true,
    Audio = true
});

var videoTrack = stream.GetVideoTracks()[0];
var settings = videoTrack.GetSettings();
Console.WriteLine($"Camera: {settings.Width}x{settings.Height} @ {settings.FrameRate}fps");

// List available devices
var devices = await MediaDevices.EnumerateDevices();
foreach (var device in devices)
    Console.WriteLine($"[{device.Kind}] {device.Label}");
```

## Architecture

| Platform | Video Capture | Audio Capture | Audio Playback | Notes |
|----------|--------------|---------------|----------------|-------|
| Browser (Blazor WASM) | `navigator.mediaDevices` via [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) | Same | HTML audio element | Feature-complete |
| Windows | MediaFoundation + DirectShow (P/Invoke) | WASAPI (P/Invoke) | WASAPI (P/Invoke) | Feature-complete; H.264 encoding via MediaFoundation MFT |
| Linux | Not yet (V4L2 P/Invoke planned) | Not yet (PulseAudio / ALSA P/Invoke planned) | Not yet | Device enumeration works; see [Docs/linux.md](Docs/linux.md) |
| macOS | Phase 5 (AVFoundation) | Phase 5 (CoreAudio) | Phase 5 | Planned |

## License

Licensed under the MIT License. See [LICENSE.txt](LICENSE.txt) for details.

## Built With

- [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) - Typed C# wrappers for browser APIs
- [SpawnDev.ILGPU](https://github.com/LostBeard/SpawnDev.ILGPU) - GPU-accelerated pixel format conversion

## 🖖 The SpawnDev Crew

SpawnDev.MultiMedia is built by the entire SpawnDev team - a squad of AI agents and one very tired human working together, Star Trek style. Every project we ship is a team effort, and every crew member deserves a line in the credits.

- **LostBeard** (Todd Tanner) - Captain, architect, writer of libraries, keeper of the vision
- **Riker** (Claude CLI #1) - First Officer, implementation lead on consuming projects
- **Data** (Claude CLI #2) - Operations Officer, deep-library work, test rigor, root-cause analysis
- **Tuvok** (Claude CLI #3) - Security/Research Officer, design planning, documentation, code review
- **Geordi** (Claude CLI #4) - Chief Engineer, library internals, GPU kernels, backend work

If you see a commit authored by `Claude Opus 4.7` on a SpawnDev repo, that's one of the crew. Credit where credit is due. Live long and prosper. 🖖

<a href="https://www.browserstack.com"><img src="https://www.browserstack.com/images/layout/browserstack-logo-600x315.png" width="200" alt="BrowserStack"></a>
