# Changelog

## 2.6.0 (2026-11-18)

### Added
- **H.264 rate control**: `H264EncoderOptions` (`RateControl`: `ConstantBitrate`, `Quality` 1..100, `ConstantQp` 0..51) and
  `VideoEncoderFactory.CreateH264(H264EncoderOptions)`; the old `CreateH264(width, height, fps, bitrateBps)` is unchanged (constant bitrate).
  Constant QP is the closest Media Foundation gets to x264's CRF. Measured (320 x 240 detailed picture, one I-frame): QP 12 / 28 / 44 give
  49.2 / 33.7 / 29.9 dB luma PSNR in 43550 / 11947 / 766 bytes; quality 20 / 90 give 30.0 / 42.8 dB.
- `PixelFormatConverter.BGRAtoNV12`, `RGB24toNV12`, `I420toNV12` (the encoder's input from RGB pictures); `Bt601`, the conversion maths shared
  by the CPU converter and the ILGPU kernels.

### Fixed
- **YUV to RGB decoded video-range YUV as full range.** NV12 / I420 / YUY2 to BGRA (CPU and GPU) used Y + 1.402 V etc. without the 16 offset and
  the 255 / 219 gain, while BGRA to I420 wrote BT.601 limited range: camera and H.264-decoded pictures came out washed out (white 235 instead of
  255) and BGRA -> YUV -> BGRA did not return its colours. All paths are BT.601 limited range now, rounded instead of truncated, and BGRA to
  I420's chroma is the 2 x 2 block's average instead of its top-left pixel. (MJPEG keeps full range: JFIF is full range.)
- `eAVEncCommonRateControlMode_Quality` was 2 (UnconstrainedVBR in codecapi.h); it is 3. It was not used before 2.6.0.

### Changed
- SpawnDev.SpawnJS 3.0.7 -> 3.0.10, SpawnDev.ILGPU 5.4.15 -> 5.4.36 (demo: SpawnDev.SpawnJS.Blazor 3.0.7 -> 3.0.8).

### Tests
- PlaywrightMultiTest (NV1080: Radeon RX 590, installed Chrome): 173 passed, 7 failed of 180 (2.5.0: 172 on Yoda). The 7 are the desktop
  video-capture tests: OBS Virtual Camera is installed on NV1080 but not running, and the unmodified 2.5.0 fails them the same way here.
  Every browser test passed (the browser's WebGPU adapter was not logged by the run).
- PlaywrightMultiTest now ignores certificate errors for its own https://localhost test server: on a machine without a trusted .NET dev
  certificate Chrome stopped at ERR_CERT_AUTHORITY_INVALID and no browser test ran. New: limited-range black / white exact, BGRA -> NV12 round trips (<= 2 levels on 2 x 2
  constant colours, <= 4 on a gradient), constant QP and quality monotonic.

## 2.5.0 (2026-11-03)

### Added
- **H.264 decoding (desktop): `IVideoDecoder`** over the Media Foundation H.264 Decoder MFT, NV12 out. Round-trip tests encode with
  the library's own H.264 encoder and decode back. SpawnDev.RTC 2.4.0's `IRTCVideoReceiver` decodes received video with it.

### Changed
- SpawnDev.SpawnJS 3.0.0 -> 3.0.7, SpawnDev.ILGPU 5.3.0 -> 5.4.15 (demo: SpawnDev.SpawnJS.Blazor 3.0.1 -> 3.0.7).

### Tests
- PlaywrightMultiTest (RTX 4070, Chrome 156): 172 passed, 0 failed, 0 skipped (2.4.0: 168; the 4 new are the H.264 round trips).

## 2.4.0 (2026-10-01)

### Changed - SpawnDev.SpawnJS 3.0.0 + SpawnDev.ILGPU 5.3.0

- **SpawnDev.SpawnJS 2.1.18 -> 3.0.0** (one-crossing marshaller) and **SpawnDev.ILGPU 5.2.16 -> 5.3.0** (built on
  SpawnJS 3). Minor bump: a consumer pinning SpawnJS 2.x now gets NU1605.
- **The browser test app no longer runs on SpawnDev.BlazorJS.** `SpawnDev.MultiMedia.Demo` booted with
  `AddBlazorJSRuntime()` and the tests used `SpawnDev.UnitTesting.Blazor` (which depends on BlazorJS), so the Wasm
  lane ran two interop runtimes side by side. Now `AddSpawnJSRuntime()` + `SpawnDev.UnitTesting.Browser`, the same
  as SpawnDev.RTC. CameraPreview sets `<video>.srcObject` through `ElementReference.As<HTMLVideoElement>()`
  (SpawnDev.SpawnJS.Blazor **3.0.1** - porting this page found that `As<T>()` threw NullReferenceException in every
  published app; fixed there). Verified in Chromium with a fake camera: Start attaches a live stream, Stop clears it.
- **The library builds with 0 warnings.** `Browser*` classes are `[SupportedOSPlatform("browser")]` (as in SpawnDev.RTC);
  `JpegDecodeResult` arrays initialized; `in` for `Marshal.QueryInterface`; the desktop tracks' `OnMute` / `OnUnmute`
  are explicit no-op events (desktop capture has no mute signal, they never fired).

### Tests

- PlaywrightMultiTest 168/168 (desktop 83 + Wasm 83 + 2), the Wasm lane on SpawnJS 3.0.0 + ILGPU 5.3.0.

## 2.3.0 (2026-09-23)

### Changed - GetUserMedia failures now THROW on desktop and in the browser

- **New `MediaDeviceException`** (`Name`, `Kind`, `InnerException`). `Name` follows the web's DOMException
  names: `NotFoundError` (no such device / no device of that kind), `NotReadableError` (the device exists
  but could not be opened), plus whatever the browser reports (`NotAllowedError`, `OverconstrainedError`).
- **Desktop used to swallow the failure.** A capture that failed to open was written to Debug output and
  `GetUserMedia` returned a stub "No Camera Found" / "No Audio Input Found" track, so a caller could not
  tell a missing camera from a working one. It now throws, keeping the real cause as `InnerException`
  (an `AggregateException` when several devices were tried). Video tries Media Foundation then
  DirectShow per device and falls through to the next device before giving up; tracks already opened by
  the same call are disposed when a later one fails. `GetDisplayMedia` failures are `NotReadableError`.
- **The browser now throws the same type.** A rejected `getUserMedia` / `getDisplayMedia` is rethrown as
  `MediaDeviceException` with the JS error name (needs SpawnDev.SpawnJS 2.1.18, which keeps the name).
- **`DeviceId` is exact on both platforms.** The browser passed it as a preference and silently opened a
  different device; it is now `{ exact: id }`, matching desktop.
- **A half-built video track is released.** `WindowsVideoTrack.CreateFromActivate` /
  `CreateFromDirectShowMoniker` dispose the track (graph, reader, capture thread) if setup fails part way.

### Tests

- `GetUserMedia_UnknownVideoDeviceId_ThrowsMediaDeviceException`,
  `GetUserMedia_UnknownAudioDeviceId_ThrowsMediaDeviceException` - pass on desktop and in the Wasm lane.
- The desktop video tests open **OBS Virtual Camera** when it is installed (else the first camera) and take
  turns on it through a machine-wide semaphore. PlaywrightMultiTest runs 4 desktop test processes at once,
  and a camera another process is capturing from cannot be opened (DirectShow RenderStream E_INVALIDARG,
  now `NotReadableError`; measured 3 of 4 concurrent processes fail, 1 process 10/10). The old stub track
  had been hiding those failures. Full sweep 168/168, three runs in a row.
- PlaywrightMultiTest: a desktop test process that dies without reporting now fails with its exit code and
  last output (it said only "Test run failed"), and a `PMT_FILTER` / `PMT_LANES` scope that selects no
  tests fails instead of reporting "Passed!". `PMT_LANES` matches test class names
  (`DesktopMultiMediaTests`, `WasmMultiMediaTests`).

### Known issue

- Capturing from a Meta Quest Link virtual camera (Meta Quest 3/3S/2/Pro) through DirectShow can crash the
  process (access violation, "Internal CLR error 0x80131506") under GC activity: it did on every stress run,
  while OBS Virtual Camera ran 0 crashes in 160 sessions with the same code. Not yet proven to be inside
  Meta's filter rather than our graph usage. The tests avoid it by preferring OBS.

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
