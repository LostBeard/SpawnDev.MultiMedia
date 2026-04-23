using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Thin wrapper over the Microsoft H.264 Encoder Media Foundation Transform
    /// (<c>CLSID_MSH264EncoderMFT</c>). Accepts NV12 frames and produces H.264 Annex-B NAL
    /// units suitable for RTP packetization (RFC 6184) or file muxing.
    ///
    /// Usage shape:
    /// <code>
    /// using var enc = new H264EncoderMFT(width: 640, height: 480, fps: 30, bitrateBps: 1_500_000);
    /// foreach (var nv12Frame in frames)
    /// {
    ///     enc.Encode(nv12Frame, timestamp100ns, durationNanos, out var nalUnits);
    ///     if (nalUnits != null) consumer.Send(nalUnits);
    /// }
    /// </code>
    ///
    /// The encoder is synchronous (non-async MFT). ProcessInput blocks briefly; the caller
    /// can drive encoding on the capture thread directly. On hardware where MFT picks a
    /// GPU encoder (Intel Quick Sync / NVIDIA NVENC / AMD VCE), per-frame latency is
    /// typically under 5 ms at 1080p30.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class H264EncoderMFT : IDisposable
    {
        private IMFTransform? _transform;
        private ICodecAPI? _codecApi;
        private bool _streaming;
        private bool _disposed;

        /// <summary>Frame width in pixels. Set at construction.</summary>
        public int Width { get; }

        /// <summary>Frame height in pixels. Set at construction.</summary>
        public int Height { get; }

        /// <summary>Target frame rate in Hz. Set at construction.</summary>
        public int FrameRate { get; }

        /// <summary>Target bitrate in bits per second. Set at construction.</summary>
        public int BitrateBps { get; }

        /// <summary>
        /// Construct + configure the MFT. Output type is set BEFORE input type per the
        /// MFT docs (the encoder needs to know its output format before it can accept
        /// input). Low-latency mode is enabled via <c>CODECAPI_AVLowLatencyMode</c> so the
        /// encoder does not buffer frames for look-ahead - required for real-time WebRTC.
        /// </summary>
        public H264EncoderMFT(int width, int height, int fps, int bitrateBps)
        {
            if (width <= 0 || (width & 1) != 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive + even (NV12 requires even dimensions).");
            if (height <= 0 || (height & 1) != 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive + even.");
            if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
            if (bitrateBps <= 0) throw new ArgumentOutOfRangeException(nameof(bitrateBps));

            Width = width;
            Height = height;
            FrameRate = fps;
            BitrateBps = bitrateBps;

            CreateTransform();
            ConfigureCodecApi();
            ConfigureOutputType();
            ConfigureInputType();
            BeginStreaming();
        }

        private void CreateTransform()
        {
            var clsid = H264MFT.CLSID_MSH264EncoderMFT;
            var iidTransform = typeof(IMFTransform).GUID;
            int hr = H264MFT.CoCreateInstance(ref clsid, IntPtr.Zero, H264MFT.CLSCTX_INPROC_SERVER, ref iidTransform, out var unk);
            MF.ThrowOnFailure(hr);
            _transform = (IMFTransform)unk;

            // Same object exposes ICodecAPI via QueryInterface.
            _codecApi = (ICodecAPI)unk;
        }

        private void ConfigureCodecApi()
        {
            // Low-latency mode: encoder emits NALs as soon as each frame is processed
            // instead of buffering for look-ahead / reordering. Required for real-time.
            var lowLatencyKey = H264MFT.CODECAPI_AVLowLatencyMode;
            var lowLatencyVal = H264MFT.PROPVARIANT.FromBool(true);
            int hr = _codecApi!.SetValue(ref lowLatencyKey, ref lowLatencyVal);
            MF.ThrowOnFailure(hr);

            // Constant bitrate rate-control. Default is VBR; CBR is easier for RTP.
            var rcKey = H264MFT.CODECAPI_AVEncCommonRateControlMode;
            var rcVal = H264MFT.PROPVARIANT.FromUInt32(H264MFT.eAVEncCommonRateControlMode_CBR);
            hr = _codecApi.SetValue(ref rcKey, ref rcVal);
            MF.ThrowOnFailure(hr);

            // Mean bitrate (used when rate-control is CBR).
            var brKey = H264MFT.CODECAPI_AVEncCommonMeanBitRate;
            var brVal = H264MFT.PROPVARIANT.FromUInt32((uint)BitrateBps);
            hr = _codecApi.SetValue(ref brKey, ref brVal);
            MF.ThrowOnFailure(hr);
        }

        private void ConfigureOutputType()
        {
            int hr = MF.MFCreateMediaType(out var outType);
            MF.ThrowOnFailure(hr);

            var majorKey = MF.MF_MT_MAJOR_TYPE;
            var majorVal = MF.MFMediaType_Video;
            hr = outType.SetGUID(ref majorKey, ref majorVal);
            MF.ThrowOnFailure(hr);

            var subKey = MF.MF_MT_SUBTYPE;
            var subVal = H264MFT.MFVideoFormat_H264;
            hr = outType.SetGUID(ref subKey, ref subVal);
            MF.ThrowOnFailure(hr);

            var brKey = H264MFT.MF_MT_AVG_BITRATE;
            hr = outType.SetUINT32(ref brKey, (uint)BitrateBps);
            MF.ThrowOnFailure(hr);

            var sizeKey = MF.MF_MT_FRAME_SIZE;
            hr = outType.SetUINT64(ref sizeKey, H264MFT.PackLong((uint)Width, (uint)Height));
            MF.ThrowOnFailure(hr);

            var rateKey = MF.MF_MT_FRAME_RATE;
            hr = outType.SetUINT64(ref rateKey, H264MFT.PackLong((uint)FrameRate, 1));
            MF.ThrowOnFailure(hr);

            var interlaceKey = H264MFT.MF_MT_INTERLACE_MODE;
            hr = outType.SetUINT32(ref interlaceKey, H264MFT.MFVideoInterlace_Progressive);
            MF.ThrowOnFailure(hr);

            var aspectKey = H264MFT.MF_MT_PIXEL_ASPECT_RATIO;
            hr = outType.SetUINT64(ref aspectKey, H264MFT.PackLong(1, 1));
            MF.ThrowOnFailure(hr);

            var profileKey = H264MFT.MF_MT_MPEG2_PROFILE;
            hr = outType.SetUINT32(ref profileKey, H264MFT.eAVEncH264VProfile_Base);
            MF.ThrowOnFailure(hr);

            hr = _transform!.SetOutputType(0, outType, 0);
            MF.ThrowOnFailure(hr);
        }

        private void ConfigureInputType()
        {
            int hr = MF.MFCreateMediaType(out var inType);
            MF.ThrowOnFailure(hr);

            var majorKey = MF.MF_MT_MAJOR_TYPE;
            var majorVal = MF.MFMediaType_Video;
            hr = inType.SetGUID(ref majorKey, ref majorVal);
            MF.ThrowOnFailure(hr);

            var subKey = MF.MF_MT_SUBTYPE;
            var subVal = MF.MFVideoFormat_NV12;
            hr = inType.SetGUID(ref subKey, ref subVal);
            MF.ThrowOnFailure(hr);

            var sizeKey = MF.MF_MT_FRAME_SIZE;
            hr = inType.SetUINT64(ref sizeKey, H264MFT.PackLong((uint)Width, (uint)Height));
            MF.ThrowOnFailure(hr);

            var rateKey = MF.MF_MT_FRAME_RATE;
            hr = inType.SetUINT64(ref rateKey, H264MFT.PackLong((uint)FrameRate, 1));
            MF.ThrowOnFailure(hr);

            var interlaceKey = H264MFT.MF_MT_INTERLACE_MODE;
            hr = inType.SetUINT32(ref interlaceKey, H264MFT.MFVideoInterlace_Progressive);
            MF.ThrowOnFailure(hr);

            var aspectKey = H264MFT.MF_MT_PIXEL_ASPECT_RATIO;
            hr = inType.SetUINT64(ref aspectKey, H264MFT.PackLong(1, 1));
            MF.ThrowOnFailure(hr);

            hr = _transform!.SetInputType(0, inType, 0);
            MF.ThrowOnFailure(hr);
        }

        private void BeginStreaming()
        {
            int hr = _transform!.ProcessMessage(H264MFT.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, UIntPtr.Zero);
            MF.ThrowOnFailure(hr);
            hr = _transform!.ProcessMessage(H264MFT.MFT_MESSAGE_NOTIFY_START_OF_STREAM, UIntPtr.Zero);
            MF.ThrowOnFailure(hr);
            _streaming = true;
        }

        /// <summary>
        /// Push one NV12 frame into the encoder and collect any NAL units the encoder
        /// produces in response. Output is a byte array of zero-or-more Annex-B-delimited
        /// NAL units; null indicates the encoder swallowed the frame without emitting
        /// anything this time (low-latency mode typically emits every frame, but the
        /// first frame is delayed until after SPS/PPS are synthesized).
        /// </summary>
        /// <param name="nv12Frame">Raw NV12 bytes. Length must be <c>Width * Height * 3 / 2</c>.</param>
        /// <param name="timestamp100ns">Presentation time in 100-nanosecond units (MF's native unit).</param>
        /// <param name="duration100ns">Frame duration in 100-nanosecond units. For 30 fps this is 333333.</param>
        /// <param name="output">Encoded NAL units with Annex-B start codes (00 00 00 01 ...) - ready for RTP H.264 payloader. <c>null</c> if no output this call.</param>
        public void Encode(ReadOnlySpan<byte> nv12Frame, long timestamp100ns, long duration100ns, out byte[]? output)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(H264EncoderMFT));
            int expectedSize = Width * Height * 3 / 2;
            if (nv12Frame.Length != expectedSize)
                throw new ArgumentException($"NV12 frame must be exactly {expectedSize} bytes for {Width}x{Height} (Y plane {Width * Height} + UV plane {Width * Height / 2}); got {nv12Frame.Length}.", nameof(nv12Frame));

            // Build input sample from frame bytes.
            int hr = H264MFT.MFCreateMemoryBuffer((uint)nv12Frame.Length, out var buffer);
            MF.ThrowOnFailure(hr);

            hr = buffer.Lock(out var pBuf, out _, out _);
            MF.ThrowOnFailure(hr);
            try
            {
                unsafe
                {
                    fixed (byte* src = nv12Frame)
                    {
                        Buffer.MemoryCopy(src, (void*)pBuf, nv12Frame.Length, nv12Frame.Length);
                    }
                }
            }
            finally
            {
                buffer.Unlock();
            }
            hr = buffer.SetCurrentLength(nv12Frame.Length);
            MF.ThrowOnFailure(hr);

            hr = H264MFT.MFCreateSample(out var sample);
            MF.ThrowOnFailure(hr);
            hr = sample.AddBuffer(buffer);
            MF.ThrowOnFailure(hr);
            hr = sample.SetSampleTime(timestamp100ns);
            MF.ThrowOnFailure(hr);
            hr = sample.SetSampleDuration(duration100ns);
            MF.ThrowOnFailure(hr);

            hr = _transform!.ProcessInput(0, sample, 0);
            MF.ThrowOnFailure(hr);

            output = DrainOutput();
        }

        /// <summary>
        /// Pull all currently-available encoded NAL units out of the MFT. Typically called
        /// automatically after <see cref="Encode"/>, but also usable after
        /// <see cref="Drain"/> to flush buffered output at end of stream.
        /// </summary>
        private byte[]? DrainOutput()
        {
            if (_transform == null) return null;

            int hr = _transform.GetOutputStreamInfo(0, out var outInfo);
            MF.ThrowOnFailure(hr);
            bool providesSamples = (outInfo.dwFlags & 0x3) != 0;  // _PROVIDES_SAMPLES | _CAN_PROVIDE_SAMPLES

            var collected = new List<byte>();
            while (true)
            {
                IMFSample? outSample = null;
                IMFMediaBuffer? outBuffer = null;
                if (!providesSamples)
                {
                    hr = H264MFT.MFCreateSample(out outSample);
                    MF.ThrowOnFailure(hr);
                    hr = H264MFT.MFCreateMemoryBuffer(Math.Max(outInfo.cbSize, 1u << 20), out outBuffer);
                    MF.ThrowOnFailure(hr);
                    hr = outSample.AddBuffer(outBuffer);
                    MF.ThrowOnFailure(hr);
                }

                var outArr = new H264MFT.MFT_OUTPUT_DATA_BUFFER[1];
                outArr[0].dwStreamID = 0;
                outArr[0].pSample = outSample != null
                    ? Marshal.GetIUnknownForObject(outSample)
                    : IntPtr.Zero;

                hr = _transform.ProcessOutput(0, 1, outArr, out _);

                // Release the IUnknown ref we added (ProcessOutput holds its own ref on
                // success; on failure our ref is the only one). Do this before any branch.
                if (outArr[0].pSample != IntPtr.Zero)
                    Marshal.Release(outArr[0].pSample);

                if (hr == H264MFT.MF_E_TRANSFORM_NEED_MORE_INPUT)
                {
                    // Normal - no output this round. Stop draining.
                    break;
                }
                if (hr == H264MFT.MF_E_TRANSFORM_STREAM_CHANGE)
                {
                    // Encoder renegotiated output type (shouldn't happen for fixed-res/-fps,
                    // but handle by reading the new type and continuing). Skip for now and
                    // break - will re-read on next call.
                    break;
                }
                MF.ThrowOnFailure(hr);

                // Pull encoded bytes out of the sample's buffer.
                if (outSample != null)
                {
                    hr = outSample.ConvertToContiguousBuffer(out var contig);
                    MF.ThrowOnFailure(hr);
                    hr = contig.Lock(out var pOut, out _, out var curLen);
                    MF.ThrowOnFailure(hr);
                    try
                    {
                        var chunk = new byte[curLen];
                        Marshal.Copy(pOut, chunk, 0, curLen);
                        collected.AddRange(chunk);
                    }
                    finally
                    {
                        contig.Unlock();
                    }
                    Marshal.ReleaseComObject(contig);
                    Marshal.ReleaseComObject(outSample);
                    if (outBuffer != null) Marshal.ReleaseComObject(outBuffer);
                }
            }

            return collected.Count > 0 ? collected.ToArray() : null;
        }

        /// <summary>
        /// Drain any remaining encoded output (call before Dispose if the application
        /// needs to flush trailing frames from the MFT's internal queue).
        /// </summary>
        public byte[]? Drain()
        {
            if (_disposed || _transform == null) return null;
            int hr = _transform.ProcessMessage(H264MFT.MFT_MESSAGE_COMMAND_DRAIN, UIntPtr.Zero);
            MF.ThrowOnFailure(hr);
            return DrainOutput();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_transform != null)
            {
                try
                {
                    if (_streaming)
                    {
                        _transform.ProcessMessage(H264MFT.MFT_MESSAGE_NOTIFY_END_OF_STREAM, UIntPtr.Zero);
                        _transform.ProcessMessage(H264MFT.MFT_MESSAGE_NOTIFY_END_STREAMING, UIntPtr.Zero);
                    }
                }
                catch { /* best-effort teardown */ }

                Marshal.ReleaseComObject(_transform);
                _transform = null;
            }

            if (_codecApi != null)
            {
                // codec API is the same COM object as _transform - release already called
                // above covers it. Reset the reference so we don't double-release.
                _codecApi = null;
            }
        }
    }
}
