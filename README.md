# SpawnDev.MultiMedia

[![NuGet](https://img.shields.io/nuget/v/SpawnDev.MultiMedia.svg)](https://www.nuget.org/packages/SpawnDev.MultiMedia)
[![License](https://img.shields.io/github/license/LostBeard/SpawnDev.MultiMedia)](https://github.com/LostBeard/SpawnDev.MultiMedia/blob/master/LICENSE.txt)

Cross-platform media capture and playback for .NET - camera, microphone, speakers, video display. One API, every platform.

## Features

- **True cross-platform** - Browser (Blazor WASM), Windows, Linux, macOS
- **Camera capture** - Access webcam video with resolution/framerate constraints
- **Microphone capture** - Access audio input with sample rate/channel controls
- **Device enumeration** - List all available media input/output devices
- **Screen capture** - Browser getDisplayMedia support
- **Raw frame access** - Desktop video/audio frame callbacks for custom processing
- **No external media NuGet dependencies** - Platform APIs via P/Invoke on desktop
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
| Windows | MediaFoundation (P/Invoke) | WASAPI (P/Invoke) | WASAPI (P/Invoke) |
| Linux | V4L2 (P/Invoke) | PulseAudio (P/Invoke) | PulseAudio (P/Invoke) |
| macOS | AVFoundation (P/Invoke) | CoreAudio (P/Invoke) | CoreAudio (P/Invoke) |

## License

Licensed under the MIT License. See [LICENSE.txt](LICENSE.txt) for details.

## Built With

- [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) - Typed C# wrappers for browser APIs

<a href="https://www.browserstack.com"><img src="https://www.browserstack.com/images/layout/browserstack-logo-600x315.png" width="200" alt="BrowserStack"></a>
