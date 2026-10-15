using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpawnDev.MultiMedia.Windows
{
    /// <summary>
    /// Thin wrapper over the Microsoft H.264 Decoder Media Foundation Transform (<c>CLSID_CMSH264DecoderMFT</c>): Annex-B access units in,
    /// NV12 frames out. The decoder is the OS's (hardware accelerated when the GPU offers it); this plumbs bytes in and pixels out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decoder learns the picture size from the SPS in the stream, so the output type cannot be fixed up front: the first output attempt after the
    /// SPS returns <c>MF_E_TRANSFORM_STREAM_CHANGE</c>, at which point the NV12 output type is chosen and decoding goes on (the standard decoder dance).
    /// </para>
    /// <para>
    /// Frames come out packed (no row padding) whatever stride the MFT used: <see cref="VideoFrame.Data"/> is <c>Width * Height * 3 / 2</c> bytes.
    /// Low-latency mode is on, so a frame is returned as soon as it is decoded instead of after a reordering delay.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class H264DecoderMFT : IDisposable
    {
        // CLSID_CMSH264DecoderMFT
        private static readonly Guid ClsidDecoder = new("62CE7E72-4C71-4D20-B15D-452831A87D9D");
        // MF_LOW_LATENCY (the same value as CODECAPI_AVLowLatencyMode)
        private static readonly Guid MfLowLatency = new("9C27891A-ED7A-40E1-88E8-B22727A024EE");
        private const uint MFT_OUTPUT_STREAM_PROVIDES_SAMPLES = 0x100;
        private const int MF_E_TRANSFORM_TYPE_NOT_SET = unchecked((int)0xC00D6D60);

        private IMFTransform? _transform;
        private bool _disposed;
        private bool _outputTypeSet;
        private int _width, _height, _stride;
        private uint _outputBufferSize;
        private bool _providesSamples;

        /// <summary>Width of the decoded pictures (0 until the first frame).</summary>
        public int Width => _width;

        /// <summary>Height of the decoded pictures (0 until the first frame).</summary>
        public int Height => _height;

        public H264DecoderMFT()
        {
            MediaFoundationRuntime.EnsureStarted();
            var clsid = ClsidDecoder;
            var iid = typeof(IMFTransform).GUID;
            MF.ThrowOnFailure(H264MFT.CoCreateInstance(ref clsid, IntPtr.Zero, H264MFT.CLSCTX_INPROC_SERVER, ref iid, out var unk));
            _transform = (IMFTransform)unk;

            // Low latency: no reordering buffer, a frame out as soon as it is decoded.
            if (_transform.GetAttributes(out var attributes) >= 0 && attributes != null)
            {
                var key = MfLowLatency;
                attributes.SetUINT32(ref key, 1);
                Marshal.ReleaseComObject(attributes);
            }

            // The input type is what the decoder needs first: H.264. The output type follows once the SPS has been seen.
            MF.ThrowOnFailure(MF.MFCreateMediaType(out var inType));
            var major = MF.MF_MT_MAJOR_TYPE; var video = MF.MFMediaType_Video;
            MF.ThrowOnFailure(inType.SetGUID(ref major, ref video));
            var sub = MF.MF_MT_SUBTYPE; var h264 = H264MFT.MFVideoFormat_H264;
            MF.ThrowOnFailure(inType.SetGUID(ref sub, ref h264));
            var interlace = H264MFT.MF_MT_INTERLACE_MODE;
            MF.ThrowOnFailure(inType.SetUINT32(ref interlace, H264MFT.MFVideoInterlace_Progressive));
            MF.ThrowOnFailure(_transform.SetInputType(0, inType, 0));
            Marshal.ReleaseComObject(inType);

            MF.ThrowOnFailure(_transform.ProcessMessage(H264MFT.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, UIntPtr.Zero));
            MF.ThrowOnFailure(_transform.ProcessMessage(H264MFT.MFT_MESSAGE_NOTIFY_START_OF_STREAM, UIntPtr.Zero));
        }

        /// <summary>Feeds one Annex-B access unit and returns the frames the decoder produced.</summary>
        public IReadOnlyList<VideoFrame> Decode(ReadOnlySpan<byte> accessUnit, long timestamp100ns)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(H264DecoderMFT));
            var frames = new List<VideoFrame>();
            if (accessUnit.IsEmpty) return frames;

            MF.ThrowOnFailure(H264MFT.MFCreateMemoryBuffer((uint)accessUnit.Length, out var buffer));
            MF.ThrowOnFailure(buffer.Lock(out var pBuf, out _, out _));
            try
            {
                unsafe
                {
                    fixed (byte* src = accessUnit) Buffer.MemoryCopy(src, (void*)pBuf, accessUnit.Length, accessUnit.Length);
                }
            }
            finally { buffer.Unlock(); }
            MF.ThrowOnFailure(buffer.SetCurrentLength(accessUnit.Length));
            MF.ThrowOnFailure(H264MFT.MFCreateSample(out var sample));
            MF.ThrowOnFailure(sample.AddBuffer(buffer));
            MF.ThrowOnFailure(sample.SetSampleTime(timestamp100ns));

            var hr = _transform!.ProcessInput(0, sample, 0);
            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buffer);
            // A decoder that is full asks to be drained first; draining below makes room, then the unit is offered again by the caller's next call.
            if (hr != unchecked((int)0xC00D6D75) /* MF_E_NOTACCEPTING */) MF.ThrowOnFailure(hr);

            DrainOutput(frames, timestamp100ns);
            return frames;
        }

        private void DrainOutput(List<VideoFrame> frames, long timestamp100ns)
        {
            while (true)
            {
                IMFSample? outSample = null;
                IMFMediaBuffer? outBuffer = null;
                if (_outputTypeSet && !_providesSamples)
                {
                    MF.ThrowOnFailure(H264MFT.MFCreateSample(out outSample));
                    MF.ThrowOnFailure(H264MFT.MFCreateMemoryBuffer(Math.Max(_outputBufferSize, 1u), out outBuffer));
                    MF.ThrowOnFailure(outSample.AddBuffer(outBuffer));
                }

                var output = new H264MFT.MFT_OUTPUT_DATA_BUFFER[1];
                output[0].dwStreamID = 0;
                output[0].pSample = outSample != null ? Marshal.GetIUnknownForObject(outSample) : IntPtr.Zero;
                var hr = _transform!.ProcessOutput(0, 1, output, out _);
                var returned = output[0].pSample;

                if (hr == H264MFT.MF_E_TRANSFORM_NEED_MORE_INPUT)
                {
                    if (returned != IntPtr.Zero) Marshal.Release(returned);
                    if (outSample != null) Marshal.ReleaseComObject(outSample);
                    if (outBuffer != null) Marshal.ReleaseComObject(outBuffer);
                    return;
                }
                if (hr == H264MFT.MF_E_TRANSFORM_STREAM_CHANGE || hr == MF_E_TRANSFORM_TYPE_NOT_SET)
                {
                    // The SPS has been parsed (or the stream changed): choose NV12 as the output type, learn the size, and ask again.
                    if (returned != IntPtr.Zero) Marshal.Release(returned);
                    if (outSample != null) Marshal.ReleaseComObject(outSample);
                    if (outBuffer != null) Marshal.ReleaseComObject(outBuffer);
                    // No output type yet means the decoder has not seen a parameter set: it needs more input, not an error.
                    if (!ChooseOutputType()) return;
                    continue;
                }
                MF.ThrowOnFailure(hr);

                // The sample the MFT filled: ours, or one it provided itself.
                IMFSample done = outSample ?? (IMFSample)Marshal.GetObjectForIUnknown(returned);
                try
                {
                    frames.Add(ReadFrame(done, timestamp100ns));
                }
                finally
                {
                    if (returned != IntPtr.Zero) Marshal.Release(returned);
                    Marshal.ReleaseComObject(done);
                    if (outBuffer != null) Marshal.ReleaseComObject(outBuffer);
                }
            }
        }

        /// <summary>Picks NV12 from the types the decoder now offers. False when it offers none yet (no SPS seen).</summary>
        private bool ChooseOutputType()
        {
            for (uint i = 0; ; i++)
            {
                var hr = _transform!.GetOutputAvailableType(0, i, out var type);
                if (hr < 0) return false;
                var sub = MF.MF_MT_SUBTYPE;
                type.GetGUID(ref sub, out var subtype);
                if (subtype != MF.MFVideoFormat_NV12) { Marshal.ReleaseComObject(type); continue; }

                MF.ThrowOnFailure(_transform.SetOutputType(0, type, 0));
                var sizeKey = MF.MF_MT_FRAME_SIZE;
                MF.ThrowOnFailure(type.GetUINT64(ref sizeKey, out var packed));
                _width = (int)(packed >> 32);
                _height = (int)(packed & 0xFFFFFFFF);
                var strideKey = MF.MF_MT_DEFAULT_STRIDE;
                _stride = type.GetUINT32(ref strideKey, out var stride) >= 0 && (int)stride > 0 ? (int)stride : _width;
                Marshal.ReleaseComObject(type);

                MF.ThrowOnFailure(_transform.GetOutputStreamInfo(0, out var info));
                _outputBufferSize = info.cbSize;
                _providesSamples = (info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) != 0;
                _outputTypeSet = true;
                return true;
            }
        }

        /// <summary>Copies the decoded picture out of the sample as packed NV12 (the MFT's rows may be padded to its stride).</summary>
        private VideoFrame ReadFrame(IMFSample sample, long timestamp100ns)
        {
            MF.ThrowOnFailure(sample.ConvertToContiguousBuffer(out var contiguous));
            MF.ThrowOnFailure(contiguous.Lock(out var pData, out _, out var length));
            try
            {
                var packed = new byte[_width * _height * 3 / 2];
                // Layout in the buffer: Y rows at _stride bytes, then the interleaved UV rows at the same stride. The decoder's surface may be taller than the
                // picture (a 1080-line picture is decoded into 1088 lines), so the UV plane starts after the PADDED Y plane; its height follows from the length.
                var surfaceRows = Math.Max(_height, length * 2 / (3 * _stride));
                if (_stride == _width && surfaceRows == _height)
                {
                    Marshal.Copy(pData, packed, 0, Math.Min(packed.Length, length));
                }
                else
                {
                    var row = new byte[_width];
                    for (var y = 0; y < _height; y++)
                    {
                        Marshal.Copy(pData + y * _stride, row, 0, _width);
                        Buffer.BlockCopy(row, 0, packed, y * _width, _width);
                    }
                    var uvBase = _stride * surfaceRows;
                    for (var y = 0; y < _height / 2; y++)
                    {
                        Marshal.Copy(pData + uvBase + y * _stride, row, 0, _width);
                        Buffer.BlockCopy(row, 0, packed, _width * _height + y * _width, _width);
                    }
                }
                sample.GetSampleTime(out var time);
                return new VideoFrame(_width, _height, VideoPixelFormat.NV12, packed, time != 0 ? time : timestamp100ns);
            }
            finally
            {
                contiguous.Unlock();
                Marshal.ReleaseComObject(contiguous);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_transform != null)
            {
                try
                {
                    _transform.ProcessMessage(H264MFT.MFT_MESSAGE_NOTIFY_END_OF_STREAM, UIntPtr.Zero);
                    _transform.ProcessMessage(H264MFT.MFT_MESSAGE_NOTIFY_END_STREAMING, UIntPtr.Zero);
                }
                catch { /* best-effort teardown */ }
                Marshal.ReleaseComObject(_transform);
                _transform = null;
            }
        }
    }

    /// <summary>Starts Media Foundation once per process (the encoder, the decoder and the capture code share it).</summary>
    internal static class MediaFoundationRuntime
    {
        private static readonly object Gate = new();
        private static bool _started;

        public static void EnsureStarted()
        {
            lock (Gate)
            {
                if (_started) return;
                MF.ThrowOnFailure(MF.MFStartup(MF.MF_VERSION, 0));
                _started = true;
            }
        }
    }
}
