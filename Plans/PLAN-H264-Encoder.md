# H.264 Video Encoder (Phase 4b)

> **Status (2026-04-23): SHIPPED** via SpawnDev.MultiMedia commits `0c6cb49` (MFT P/Invoke) + `8e86e39` (IVideoEncoder abstraction) and SpawnDev.RTC commit `71275d6` (bridge + E2E test + docs). Steps 1-4 complete; steps 5 (WPF demo integration) + 6 (`Docs/video-tracks.md` - now done on the RTC side) remain as polish. Total implementation time was ~90 min, not the 2-3 weeks originally estimated - interfacing with the OS encoder, not implementing one.


Cross-platform video encoder for SpawnDev.MultiMedia that feeds into SpawnDev.RTC's `DesktopRTCPeerConnection.AddTrack(IVideoTrack)` overload to complete the desktop-side WebRTC video path. Phase 4a (audio) shipped 2026-04-23; this plan scopes Phase 4b (video).

## Goal

Take a MultiMedia `IVideoTrack` (raw YUV / BGRA frames from a camera, screen capture, or synthetic source) and produce encoded H.264 NAL units suitable for RTP packetization over a WebRTC SCTP peer connection, ending at a browser `<video>` element or any WebRTC-compliant receiver.

The browser side already has full H.264 support via the platform WebRTC stack. All work here is desktop-side: encoder P/Invoke + RTP packetization adapter in SpawnDev.RTC.

## Scope per platform (Phase 4b = Windows only)

| Platform | Encoder | Status |
|---|---|---|
| Windows | MediaFoundation H264 Encoder MFT (`CLSID_MSH264EncoderMFT`) via P/Invoke | **Phase 4b** |
| Linux | VAAPI (`libva` + `libva-drm`) or x264 fallback | Phase 5 (post-4b) |
| macOS | VideoToolbox `VTCompressionSession*` | Phase 5 |
| Browser | Native WebRTC stack | Already works |

Ship Phase 4b Windows-only first. Linux + macOS land as Phase 5 with identical `IVideoEncoder` interface.

## Reference implementations to study (do NOT copy)

- **SIPSorceryMedia.Windows** - H.264 MFT usage, RTP H.264 payloader. BSD licensed but we ship zero external NuGet media deps (CLAUDE.md rule).
- **Microsoft SDK sample `H264EncodeTransform`** - MFT wiring reference.
- **WebRTC.org source (libwebrtc) `h264_encoder_impl.cc`** - RTP H.264 payload format (RFC 6184) reference for how browser peers expect NAL units framed.
- **Intel Media SDK docs** - lower-level API but clarifies what GOP/IDR parameters to set for real-time low-latency encoding (browser consumers expect ~every-second IDR + constant-bitrate-ish).

All licensed permissively, but we write our P/Invoke declarations from the Windows SDK headers ourselves per Rule 1 and the CLAUDE.md rule on "no external NuGet media packages".

## Work breakdown

### Step 1: P/Invoke surface for Media Foundation H.264 MFT

**Deliverable:** A `Windows/H264EncoderMFT.cs` class that:
- Instantiates `CLSID_MSH264EncoderMFT` via `MFTEnumEx` / `CoCreateInstance`
- Sets input media type (NV12 preferred for zero-copy; fallback BGRA → NV12 via `PixelFormatConverter` or `GpuPixelFormatConverter` which already exists on all 6 ILGPU backends)
- Sets output media type (H.264 baseline profile, 720p or 1080p default with runtime-settable `VideoBitrate`, `VideoFrameRate`, `VideoKeyFrameInterval`)
- Configures `CODECAPI_AVLowLatencyMode = 1` (required for real-time; MFT buffers ~30 frames by default otherwise)
- Provides `Encode(ReadOnlySpan<byte> nv12Frame, out byte[] encodedNalUnits)` - synchronous, one frame in, zero-or-more NAL units out
- Emits IDR frames on demand (consumers may want to force an IDR on peer join or on packet-loss bursts)
- Releases COM + MFT resources cleanly in `Dispose`

Tests:
- Round-trip known NV12 bit pattern through MFT → bytes-out length > 0, first frame contains SPS + PPS NAL units
- Force-IDR after several P-frames, next output contains IDR NAL (type 5)
- Varying resolutions (320x240, 640x480, 1280x720, 1920x1080) all produce valid output
- Dispose during in-flight Encode call doesn't crash

### Step 2: `IVideoEncoder` interface + `WindowsH264Encoder` wrapper

**Deliverable:** A platform-agnostic interface in `SpawnDev.MultiMedia`:
```csharp
public interface IVideoEncoder : IAsyncDisposable
{
    string Codec { get; }                    // "h264", "vp8", "av1" in future
    int Width { get; set; }
    int Height { get; set; }
    int FrameRate { get; set; }
    int Bitrate { get; set; }                // bits per second
    TimeSpan KeyFrameInterval { get; set; }

    void Encode(ReadOnlySpan<byte> frame, VideoPixelFormat format, out EncodedVideoFrame? output);
    void RequestKeyFrame();
}

public record EncodedVideoFrame(byte[] Data, bool IsKeyFrame, TimeSpan Timestamp);
```

`WindowsH264Encoder : IVideoEncoder` wraps step 1's MFT. On Linux/macOS, future `LinuxH264Encoder` / `MacH264Encoder` implementations swap in.

Tests:
- Factory `VideoEncoderFactory.Create("h264")` dispatches to the right platform impl via `OperatingSystem.IsWindows()` etc.
- Same API shape works for synthetic-pattern → encode → decode round-trip (use upstream Intel quick-sync or bundled libraries to decode for test verification)

### Step 3: RTP H.264 payloader in SpawnDev.RTC

**Deliverable:** `SpawnDev.RTC.Desktop/MultiMediaVideoSource.cs` - symmetric to `MultiMediaAudioSource` but for video:
- Implements SipSorcery's `IVideoSource`
- Subscribes to `IVideoTrack.OnFrame`
- Converts to the format expected by the configured `IVideoEncoder` (NV12 preferred; hits `PixelFormatConverter` / `GpuPixelFormatConverter` when needed)
- Encodes via `IVideoEncoder`
- Packetizes NAL units per RFC 6184 (Single NAL Unit, FU-A fragmentation for NAL > MTU, STAP-A aggregation for tiny units)
- Hands RTP packets to SipSorcery's outbound path

Plus: `DesktopRTCPeerConnection.AddTrack(IVideoTrack)` + `AddTrack(MultiMediaVideoSource)` overloads.

### Step 4: Browser ↔ desktop video call end-to-end test

**Deliverable:** `RTCTestBase.Phase4bVideoTests.cs` mirroring Phase 4a's audio test:
- Two `DesktopRTCPeerConnection` instances exchange a synthetic 640x480 @ 30 fps video pattern (bouncing ball, gradient stripe - something deterministic)
- Assert `OnTrack` fires with video kind, SDP contains `m=video` + `a=rtpmap:*H264/90000`
- pc2 receives ≥ 30 non-empty RTP packets within 10 seconds (proves encoder ran + packetizer produced + SCTP delivered)
- Second test: decode the received RTP stream with a test-only decoder, verify the output matches the synthetic pattern at ≥ 95% pixel similarity (accounting for H.264 lossy compression)

### Step 5: WPF demo integration

**Deliverable:** Add a "Start video call" button to the WPF demo that:
1. Opens a webcam via `MediaDevices.GetUserMedia({video: true})`
2. Displays preview via the existing `WriteableBitmap` renderer
3. On click, opens a new `DesktopRTCPeerConnection`, `AddTrack(videoTrack)`, and displays a browser tab URL for the remote peer to join
4. Uses `TrackerSignalingClient` for SDP exchange

Manual test plan: WPF user opens the tab in a browser, clicks accept, sees WPF camera output in the browser `<video>` element.

### Step 6: Docs/ — SHIPPED

`SpawnDev.RTC/Docs/video-tracks.md` (98 lines) covers:
- `IVideoTrack` / `MultiMediaVideoSource` / `AddTrack(IVideoTrack)` overloads
- H.264 encoder selection (Windows MF MFT via `VideoEncoderFactory.CreateH264`)
- RFC 6184 RTP payload format delegation to SipSorcery's packetizer
- Known limitations / runtime gates

Paired with `audio-tracks.md` (Phase 4a) so the two-track pair has matching
documentation shape.

## Estimated effort (2-3 weeks focused)

| Step | Size |
|---|---|
| 1. MFT P/Invoke | 4-5 days (P/Invoke + encoding params + IDR control + tests) |
| 2. IVideoEncoder + Windows wrapper | 1-2 days |
| 3. RTP packetizer + `MultiMediaVideoSource` | 3-4 days (RFC 6184 is the wildcard — FU-A fragmentation + STAP-A aggregation have subtle RTP timestamp rules) |
| 4. Browser↔desktop E2E test | 2 days (including a minimal test decoder for pixel-similarity check) |
| 5. WPF demo | 1 day |
| 6. Docs | 0.5 day |

**Total:** ~12-15 days focused coding. Real-world calendar: 2-3 weeks with parallel BEP/WebTorrent work.

## Risks + unknowns

- **MFT async mode.** By default the H.264 MFT is synchronous one-in-one-out, but at 1080p30 on some hardware it internally buffers frames. May need `MF_TRANSFORM_ASYNC` + event-driven pull model. Check on a low-end dev box before committing to synchronous API.
- **Hardware vs software encoding.** MFT picks hardware (Intel Quick Sync / NVIDIA NVENC / AMD VCE) automatically when available. On headless servers / VMs this falls back to software and may miss real-time at 1080p. Consider exposing a `preferSoftwareEncoding` knob.
- **Profile compatibility.** Browser peers expect baseline or constrained-baseline by default; main profile works in Chrome but has been flaky in Safari. Ship baseline; document main as advanced.
- **Bandwidth estimation.** libwebrtc runs REMB/TMMBR feedback to dynamically adjust sender bitrate. Phase 4b ships a fixed-bitrate encoder; dynamic BWE is Phase 4c.

## Explicit non-goals for Phase 4b

- No VP8 / VP9 / AV1 encoders. H.264 is sufficient for 95% of browser interop; other codecs come after.
- No GPU-accelerated NV12 conversion on the input path if `PixelFormatConverter` (CPU) is fast enough at target resolutions. `GpuPixelFormatConverter` exists but adds a dispatch boundary; benchmark before assuming it helps.
- No audio+video sync layer. Both tracks ride the same peer connection and SipSorcery handles RTP timestamp alignment at the receiver; we don't need to coordinate sync on the send side beyond passing realistic presentation timestamps.
- No recording / playback of encoded output. MultiMedia's pipelines already have `OnFrame` hooks - a consumer that wants to save video to disk can subscribe there, not in the encoder.

## Rule alignment

- **Rule 1 (last release):** Each of steps 1-4 is individually shippable. Step 1 alone gives consumers a standalone H.264 encoder. Step 2 gives them a cross-platform interface. Etc.
- **Rule 2 (fix libraries first):** SIPSorcery fork already exposes `IVideoSource` cleanly; no fork-level changes expected. If H.264 payloader quirks emerge they land in the fork (same pattern as the SCTP Reset-race fix 2026-04-23).
- **Rule 4 (performance):** Encoder runs on the sender thread; NV12 zero-copy from camera → MFT is the fast path. No extra CPU copies.
- **Rule 5 (real tests):** Synthetic deterministic video pattern through a real MFT → RTP → real SipSorcery → real receiver. No mocks.

## Next session start checklist

1. Read `SpawnDev.RTC/Src/sipsorcery/src/SIPSorcery/net/RTP/MediaStream.cs` - understand `IVideoSource` / RTP send path shape.
2. Read `D:/users/tj/Projects/SpawnDev.RTC/SpawnDev.RTC/Src/sipsorcery/src/SIPSorceryMedia.Abstractions/` for the audio-source pattern we already adapted in Phase 4a.
3. Read RFC 6184 sections 5.6 (Single NAL Unit) + 5.7 (STAP-A) + 5.8 (FU-A) - RTP H.264 payload format.
4. Open `SpawnDev.MultiMedia/SpawnDev.MultiMedia/Windows/` - this is where step-1 MFT code lands next to existing MediaFoundation video-capture code.
5. Start step 1 with a failing test: instantiate MFT, encode one NV12 frame, assert output bytes parsed with a minimal NAL-unit reader produces expected type-7 (SPS) + type-8 (PPS) + type-5 (IDR) in the first output packet.

Captain's delivery order: Phase 4b here; then Phase 5 Linux/macOS.
