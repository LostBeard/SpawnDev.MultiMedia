# SpawnDev.MultiMedia

[![NuGet](https://img.shields.io/nuget/v/SpawnDev.MultiMedia.svg)](https://www.nuget.org/packages/SpawnDev.MultiMedia)
[![License](https://img.shields.io/github/license/LostBeard/SpawnDev.MultiMedia)](https://github.com/LostBeard/SpawnDev.MultiMedia/blob/master/LICENSE.txt)

Cross-platform media capture and playback for .NET - camera, microphone, speakers, video display. One API, browser and desktop.

## Features

- **Cross-platform** - Browser (Blazor WASM) and Windows (Linux/macOS planned)
- **Camera capture** - Webcams and virtual cameras (OBS, ManyCam, Quest) with resolution/framerate constraints
- **Microphone capture** - Audio input with sample rate/channel controls
- **Audio playback** - WASAPI render for desktop speaker output
- **Device enumeration** - List all cameras, microphones, and speakers
- **Screen capture** - Browser getDisplayMedia support
- **Raw frame access** - Desktop video/audio frame callbacks via OnFrame events
- **Pixel format conversion** - NV12, I420, YUY2, BGRA, RGB24, UYVY (CPU + ILGPU GPU)
- **Pixel format selection** - Request NV12 (zero-copy) or BGRA (display-ready) via constraints
- **Zero external media NuGet deps** - Platform APIs via P/Invoke (MediaFoundation, DirectShow, WASAPI)
- **Zero-copy where possible** - `ReadOnlyMemory<byte>` frame data

## Quick Start

```csharp
using SpawnDev.MultiMedia;

// Get camera + mic - same code on browser and desktop
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

| Platform | Video Capture | Audio Capture | Audio Playback |
|----------|--------------|---------------|----------------|
| Browser | `navigator.mediaDevices` via [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) | Same | HTML audio element |
| Windows | MediaFoundation + DirectShow (P/Invoke) | WASAPI (P/Invoke) | WASAPI (P/Invoke) |

## License

Licensed under the MIT License. See [LICENSE.txt](LICENSE.txt) for details.

## Built With

- [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) - Typed C# wrappers for browser APIs
- [SpawnDev.ILGPU](https://github.com/LostBeard/SpawnDev.ILGPU) - GPU-accelerated pixel format conversion

<a href="https://www.browserstack.com"><img src="https://www.browserstack.com/images/layout/browserstack-logo-600x315.png" width="200" alt="BrowserStack"></a>
