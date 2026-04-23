# Changelog

## Unreleased

### Phase 4b H.264 encoder plan (2026-04-23)

- Added `Plans/PLAN-H264-Encoder.md` — 6-step execution plan for Phase 4b Windows MediaFoundation H.264 encoder + RTP payloader + `IVideoEncoder` interface + end-to-end browser↔desktop video call test. Scopes each step as individually shippable per Rule 1. Estimated 2-3 weeks focused effort. Includes next-session start checklist.

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
