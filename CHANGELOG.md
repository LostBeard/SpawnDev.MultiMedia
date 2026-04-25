# Changelog

## 0.2.0 (2026-04-25 stable)

First minor cut since 0.1.0. Bundles Phase 4b (H.264 encoder), Phase 4a WebRTC cross-link polish, partial Linux device enumeration, IAudioPlayer test coverage, and a DI registration helper. Browser + Windows desktop remain feature-complete for capture / playback / conversion; Linux enumeration is live; Linux capture (`GetUserMedia`/`GetDisplayMedia`) and macOS remain documented gaps.

PlaywrightMultiTest verification: **162 / 0 / 0 in 51s** (up from 146/0/0 at 0.1.0; +16 from new H.264 + IAudioPlayer tests).

### DI registration helper (2026-04-25)

- `MultiMediaServiceCollectionExtensions.AddMultiMedia()` — registers `GpuPixelFormatConverter` + `GpuMjpgDecoder` as singletons sharing the consumer's ILGPU `Accelerator`. Stateless types (`MediaDevices`, `PixelFormatConverter`, `MjpgDecoder`) intentionally not registered. README "DI registration (optional)" section walks through Program.cs setup.

### Linux device enumeration (2026-04-25)

- **`SpawnDev.MultiMedia/Linux/LinuxMediaDevices.cs`** (new): `MediaDevices.EnumerateDevices()` returns video inputs from `/dev/video*` and audio inputs from `/proc/asound/cards` (ALSA). No V4L2 ioctl needed for enumeration — pure filesystem reads. `GetUserMedia(...)` still throws `PlatformNotSupportedException`; capture path documented in [`Docs/linux.md`](Docs/linux.md) (V4L2 + PulseAudio / ALSA P/Invoke remaining).

### Phase 4b H.264 encoder SHIPPED (2026-04-23)

- **`SpawnDev.MultiMedia/Windows/H264MFTInterop.cs`** (new): Windows MediaFoundation H.264 Encoder MFT P/Invoke surface. `CLSID_MSH264EncoderMFT` + codec GUIDs + `MF_MT_*` attribute keys + `CODECAPI_AV*` property keys + `PROPVARIANT` for `ICodecAPI.SetValue` + `MFT_OUTPUT_DATA_BUFFER` with `LPArray` marshaling + `IMFTransform` / `ICodecAPI` COM interface declarations. Zero external NuGet media packages — pure Windows SDK P/Invoke.
- **`SpawnDev.MultiMedia/Windows/H264EncoderMFT.cs`** (new): Thin wrapper — `CoCreateInstance` → configure via `ICodecAPI` (`AVLowLatencyMode`, CBR, mean bitrate) → set output type BEFORE input type (MFT requirement) → baseline profile + progressive + square pixels → `BEGIN_STREAMING`. `Encode(nv12, ts, duration)` allocates an `IMFSample`, feeds the MFT, drains output via `ProcessOutput` until `MF_E_TRANSFORM_NEED_MORE_INPUT`. `Drain()` + `Dispose()` for flush + clean teardown.
- **`SpawnDev.MultiMedia/IVideoEncoder.cs`** (new): Platform-agnostic encoder interface + `VideoEncoderFactory.CreateH264` dispatch. Future Linux (VAAPI) / macOS (VideoToolbox) implementations drop in without touching callers.
- **`SpawnDev.MultiMedia/Windows/WindowsH264Encoder.cs`** (new): `IVideoEncoder` impl wrapping `H264EncoderMFT`.
- **Tests**: 4 H.264 unit tests in `SpawnDev.MultiMedia.Demo.Shared/UnitTests/MultiMediaTestBase.H264Encoder.cs`: `H264Encoder_FirstOutput_ContainsSpsPpsIdr` (parses Annex-B start codes, asserts types 7 + 8 + 5 all present), `H264Encoder_MultipleFrames_ProduceIncreasingTimestamps`, `H264Encoder_Dispose_DoesNotThrow`, `VideoEncoderFactory_CreateH264_ReturnsWorkingEncoder`. All pass on DemoConsole; browser path returns early.
- **Consumer integration**: `SpawnDev.RTC 1.1.3-rc.1` ships `DesktopRTCPeerConnection.AddTrack(IVideoTrack)` + a full end-to-end video test. See `SpawnDev.RTC/Docs/video-tracks.md`.
- Total implementation time: ~90 minutes from plan-doc write to end-to-end pass. Original estimate was 2-3 weeks; corrected to reflect that we're interfacing with the OS encoder, not implementing one.

### Phase 4a WebRTC consumption cross-link (2026-04-23)

- `README.md` bullet for WebRTC consumption now deep-links to [`SpawnDev.RTC/Docs/audio-tracks.md`](https://github.com/LostBeard/SpawnDev.RTC/blob/master/SpawnDev.RTC/Docs/audio-tracks.md) — the Phase 4a walkthrough covering `DesktopRTCPeerConnection.AddTrack(IAudioTrack)`, the `MultiMediaAudioSource` bridge, codec negotiation (Opus vs PCMU), and end-to-end test coverage.

## 0.1.0 (2026-04-22 stable)

### First stable release

- Cross-platform media capture and playback. Same C# API on Browser (Blazor WASM via SpawnDev.BlazorJS) and Windows desktop (MediaFoundation + DirectShow + WASAPI via P/Invoke, zero external NuGet media packages).
- **Video capture** via two paths: MediaFoundation for hardware cameras, DirectShow for virtual cameras (OBS, ManyCam, Quest).
- **Audio capture** via WASAPI shared-mode event-driven capture.
- **Audio playback** via WASAPI render.
- **Screen capture** via browser `getDisplayMedia` + desktop DXGI Desktop Duplication.
- **Pixel format conversion** (NV12, I420, YUY2, BGRA, RGB24, UYVY) - CPU + ILGPU GPU paths. All 6 ILGPU backends supported for GPU acceleration (WebGPU, WebGL, Wasm, CUDA, OpenCL, CPU).
- **MJPG decoder** - JPEG parse + Huffman decode (CPU) feeding dequantize + IDCT + color-convert kernels (GPU, all backends).
- **Raw frame access** via `OnFrame` events on `IVideoTrack` / `IAudioTrack` — `ReadOnlyMemory<byte>` frame data for zero-copy downstream processing.
- **Test coverage:** 146 tests across `SpawnDev.MultiMedia.Demo.Shared`. Browser + desktop both verified via PlaywrightMultiTest (port 5580).

### Platform status

- **Browser (Blazor WASM):** ✅ feature-complete
- **Windows:** ✅ feature-complete for capture / playback / conversion
- **Linux / macOS:** 🚧 planned for Phase 5 (V4L2 + PulseAudio / AVFoundation + CoreAudio)
