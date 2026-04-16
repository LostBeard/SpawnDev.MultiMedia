using ILGPU;
using ILGPU.Runtime;

namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// ILGPU-accelerated MJPG/JPEG decoder. All parallel work runs as GPU kernels:
    /// dequantize, IDCT, and YCbCr-to-BGRA color conversion.
    ///
    /// Works on ALL ILGPU backends: CUDA, OpenCL, WebGPU, WebGL, Wasm, CPU.
    /// Acts as a cross-backend polyfill for NvJpeg (which is CUDA-only).
    ///
    /// Pipeline: CPU Huffman decode -> upload coefficients -> GPU dequantize -> GPU IDCT -> GPU color convert
    /// Output stays on GPU (ArrayView) or can be read back to CPU.
    /// </summary>
    public class GpuMjpgDecoder : IDisposable
    {
        private readonly Accelerator _accelerator;
        private readonly bool _ownsAccelerator;

        // Cached kernels
        private Action<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, int>? _dequantizeKernel;
        private Action<Index1D, ArrayView<int>, ArrayView<float>>? _idctKernel;
        private Action<Index1D, ArrayView<float>, ArrayView<byte>, int, int, int, int, int, int, int, int>? _ycbcrToBgraKernel;

        public GpuMjpgDecoder(Accelerator accelerator)
        {
            _accelerator = accelerator;
            _ownsAccelerator = false;
        }

        public GpuMjpgDecoder(Context context)
        {
            _accelerator = context.GetPreferredDevice(false).CreateAccelerator(context);
            _ownsAccelerator = true;
        }

        /// <summary>
        /// Decode JPEG data to BGRA. Full pipeline: CPU parse -> GPU decode -> CPU readback.
        /// </summary>
        public VideoFrame DecodeToBGRA(ReadOnlySpan<byte> jpegData, long timestamp = 0)
        {
            var parsed = MjpgDecoder.Parse(jpegData);
            var bgra = DecodeToBGRA(parsed);
            return new VideoFrame(parsed.Width, parsed.Height, VideoPixelFormat.BGRA,
                new ReadOnlyMemory<byte>(bgra), timestamp);
        }

        /// <summary>
        /// Decode a parsed JPEG to BGRA using GPU kernels. Returns CPU byte array.
        /// </summary>
        public byte[] DecodeToBGRA(JpegDecodeResult parsed)
        {
            int totalBlocks = parsed.TotalBlocks;
            int w = parsed.Width, h = parsed.Height;

            // Build flat quant table for all blocks: [totalBlocks * 64]
            var quantFlat = BuildFlatQuantTable(parsed);

            // Upload DCT coefficients and quant table to GPU
            using var dctGpu = _accelerator.Allocate1D<int>(totalBlocks * 64);
            using var quantGpu = _accelerator.Allocate1D<int>(totalBlocks * 64);
            dctGpu.CopyFromCPU(parsed.DctCoefficients);
            quantGpu.CopyFromCPU(quantFlat);

            // Step 1: Dequantize (per coefficient: coeff * quant)
            _dequantizeKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<int>, ArrayView<int>, ArrayView<int>, int>(DequantizeKernel);
            _dequantizeKernel(totalBlocks * 64, dctGpu.View, quantGpu.View, dctGpu.View, totalBlocks * 64);

            // Step 2: IDCT per block (64 coefficients -> 64 pixel values)
            using var pixelsGpu = _accelerator.Allocate1D<float>(totalBlocks * 64);
            _idctKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<int>, ArrayView<float>>(IDCTBlockKernel);
            _idctKernel(totalBlocks, dctGpu.View, pixelsGpu.View);

            // Step 3: Color convert YCbCr -> BGRA
            // Build component block map for the kernel
            int pw = parsed.McuCountX * parsed.McuWidth;
            int ph = parsed.McuCountY * parsed.McuHeight;

            using var bgraGpu = _accelerator.Allocate1D<byte>(w * h * 4);
            _ycbcrToBgraKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<float>, ArrayView<byte>,
                int, int, int, int, int, int, int, int>(YCbCrToBGRAKernel);
            _ycbcrToBgraKernel(
                w * h,
                pixelsGpu.View, bgraGpu.View,
                w, h, pw, ph,
                parsed.McuWidth, parsed.McuHeight,
                parsed.McuCountX, parsed.BlocksPerMcu);

            _accelerator.Synchronize();

            // Readback
            var result = new byte[w * h * 4];
            bgraGpu.CopyToCPU(result);
            return result;
        }

        /// <summary>
        /// Decode JPEG to BGRA on GPU and return the GPU buffer (zero-copy for GPU consumers).
        /// Caller owns the returned buffer and must dispose it.
        /// </summary>
        public MemoryBuffer1D<byte, Stride1D.Dense> DecodeToBGRAOnGpu(ReadOnlySpan<byte> jpegData)
        {
            var parsed = MjpgDecoder.Parse(jpegData);
            return DecodeToBGRAOnGpu(parsed);
        }

        /// <summary>
        /// Decode to BGRA, output stays on GPU. Caller owns the returned buffer.
        /// </summary>
        public MemoryBuffer1D<byte, Stride1D.Dense> DecodeToBGRAOnGpu(JpegDecodeResult parsed)
        {
            int totalBlocks = parsed.TotalBlocks;
            int w = parsed.Width, h = parsed.Height;

            var quantFlat = BuildFlatQuantTable(parsed);

            using var dctGpu = _accelerator.Allocate1D<int>(totalBlocks * 64);
            using var quantGpu = _accelerator.Allocate1D<int>(totalBlocks * 64);
            dctGpu.CopyFromCPU(parsed.DctCoefficients);
            quantGpu.CopyFromCPU(quantFlat);

            _dequantizeKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<int>, ArrayView<int>, ArrayView<int>, int>(DequantizeKernel);
            _dequantizeKernel(totalBlocks * 64, dctGpu.View, quantGpu.View, dctGpu.View, totalBlocks * 64);

            using var pixelsGpu = _accelerator.Allocate1D<float>(totalBlocks * 64);
            _idctKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<int>, ArrayView<float>>(IDCTBlockKernel);
            _idctKernel(totalBlocks, dctGpu.View, pixelsGpu.View);

            int pw = parsed.McuCountX * parsed.McuWidth;
            int ph = parsed.McuCountY * parsed.McuHeight;

            var bgraGpu = _accelerator.Allocate1D<byte>(w * h * 4);
            _ycbcrToBgraKernel ??= _accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<float>, ArrayView<byte>,
                int, int, int, int, int, int, int, int>(YCbCrToBGRAKernel);
            _ycbcrToBgraKernel(
                w * h,
                pixelsGpu.View, bgraGpu.View,
                w, h, pw, ph,
                parsed.McuWidth, parsed.McuHeight,
                parsed.McuCountX, parsed.BlocksPerMcu);

            _accelerator.Synchronize();
            return bgraGpu;
        }

        // ---- Helper: build flat quant table matching coefficient layout ----

        private static int[] BuildFlatQuantTable(JpegDecodeResult parsed)
        {
            int totalBlocks = parsed.TotalBlocks;
            var quantFlat = new int[totalBlocks * 64];

            int totalMcus = parsed.McuCountX * parsed.McuCountY;
            for (int mcu = 0; mcu < totalMcus; mcu++)
            {
                for (int bi = 0; bi < parsed.BlocksPerMcu; bi++)
                {
                    int blockIdx = mcu * parsed.BlocksPerMcu + bi;
                    int tableId = parsed.BlockQuantTableId[bi];
                    var qt = parsed.QuantTables[tableId];
                    int offset = blockIdx * 64;
                    for (int i = 0; i < 64; i++)
                        quantFlat[offset + i] = qt[i];
                }
            }
            return quantFlat;
        }

        // ---- ILGPU Kernels ----

        /// <summary>
        /// Dequantize: multiply each DCT coefficient by its quantization table value.
        /// One thread per coefficient.
        /// </summary>
        static void DequantizeKernel(Index1D index,
            ArrayView<int> coeffs, ArrayView<int> quantTable, ArrayView<int> output, int totalCoeffs)
        {
            if (index >= totalCoeffs) return;
            output[index] = coeffs[index] * quantTable[index];
        }

        /// <summary>
        /// IDCT kernel: one thread per 8x8 block.
        /// Computes the full 2D IDCT using row-column decomposition.
        /// Input: 64 dequantized DCT coefficients (spatial order).
        /// Output: 64 pixel values (float, range approximately 0-255 after +128 level shift).
        /// </summary>
        static void IDCTBlockKernel(Index1D blockIndex,
            ArrayView<int> dctCoeffs, ArrayView<float> pixels)
        {
            int baseIdx = blockIndex * 64;

            // IDCT cosine basis values: cos(pi * (2n+1) * k / 16) for n,k = 0..7
            // Precomputed as constants to avoid trig in kernel

            // Row-column decomposition: do 1D IDCT on each column, then each row

            // Step 1: Column IDCT (8 columns, each column has 8 values)
            // Using direct matrix multiply with the 8x8 IDCT matrix
            // C[k] = (k==0 ? 1/sqrt(8) : 1/2) * cos(pi*(2n+1)*k/16)

            // We use the fact that the 1D IDCT of X[k] is:
            // x[n] = sum_{k=0}^{7} C[k] * X[k] * cos(pi*(2n+1)*k/16)
            // where C[0] = 1/sqrt(2), C[k>0] = 1

            // Work with temp storage in the output array (column pass writes transposed)
            // Column pass: for each column c, compute IDCT down the column
            for (int c = 0; c < 8; c++)
            {
                float s0 = dctCoeffs[baseIdx + 0 * 8 + c];
                float s1 = dctCoeffs[baseIdx + 1 * 8 + c];
                float s2 = dctCoeffs[baseIdx + 2 * 8 + c];
                float s3 = dctCoeffs[baseIdx + 3 * 8 + c];
                float s4 = dctCoeffs[baseIdx + 4 * 8 + c];
                float s5 = dctCoeffs[baseIdx + 5 * 8 + c];
                float s6 = dctCoeffs[baseIdx + 6 * 8 + c];
                float s7 = dctCoeffs[baseIdx + 7 * 8 + c];

                // Even part
                float a0 = s0 + s4;
                float a1 = s0 - s4;
                float a2 = s2 * 0.541196100f - s6 * 1.306562965f;
                float a3 = s2 * 1.306562965f + s6 * 0.541196100f;

                float b0 = a0 + a3;
                float b1 = a1 + a2;
                float b2 = a1 - a2;
                float b3 = a0 - a3;

                // Odd part (using scaled Chen/LLM factorization)
                float c0 = s1 * 1.175875602f + s3 * 0.785694958f + s5 * -1.961570560f + s7 * -0.390180644f;
                float c1 = s1 * 0.785694958f + s3 * -1.961570560f + s5 * 0.275899379f + s7 * 1.175875602f;
                float c2 = s1 * -1.961570560f + s3 * 0.275899379f + s5 * 1.175875602f + s7 * -0.785694958f;
                float c3 = s1 * -0.390180644f + s3 * 1.175875602f + s5 * -0.785694958f + s7 * -1.961570560f;

                // Store column pass results (still in column-major within the block)
                pixels[baseIdx + 0 * 8 + c] = b0 + c0;
                pixels[baseIdx + 7 * 8 + c] = b0 - c0;
                pixels[baseIdx + 1 * 8 + c] = b1 + c1;
                pixels[baseIdx + 6 * 8 + c] = b1 - c1;
                pixels[baseIdx + 2 * 8 + c] = b2 + c2;
                pixels[baseIdx + 5 * 8 + c] = b2 - c2;
                pixels[baseIdx + 3 * 8 + c] = b3 + c3;
                pixels[baseIdx + 4 * 8 + c] = b3 - c3;
            }

            // Row pass: for each row, compute IDCT across the row
            for (int r = 0; r < 8; r++)
            {
                int ri = baseIdx + r * 8;
                float s0 = pixels[ri], s1 = pixels[ri + 1], s2 = pixels[ri + 2], s3 = pixels[ri + 3];
                float s4 = pixels[ri + 4], s5 = pixels[ri + 5], s6 = pixels[ri + 6], s7 = pixels[ri + 7];

                float a0 = s0 + s4;
                float a1 = s0 - s4;
                float a2 = s2 * 0.541196100f - s6 * 1.306562965f;
                float a3 = s2 * 1.306562965f + s6 * 0.541196100f;

                float b0 = a0 + a3;
                float b1 = a1 + a2;
                float b2 = a1 - a2;
                float b3 = a0 - a3;

                float c0 = s1 * 1.175875602f + s3 * 0.785694958f + s5 * -1.961570560f + s7 * -0.390180644f;
                float c1 = s1 * 0.785694958f + s3 * -1.961570560f + s5 * 0.275899379f + s7 * 1.175875602f;
                float c2 = s1 * -1.961570560f + s3 * 0.275899379f + s5 * 1.175875602f + s7 * -0.785694958f;
                float c3 = s1 * -0.390180644f + s3 * 1.175875602f + s5 * -0.785694958f + s7 * -1.961570560f;

                // Scale by 1/8 and add 128 (DC level shift)
                float scale = 0.125f;
                pixels[ri + 0] = (b0 + c0) * scale + 128;
                pixels[ri + 7] = (b0 - c0) * scale + 128;
                pixels[ri + 1] = (b1 + c1) * scale + 128;
                pixels[ri + 6] = (b1 - c1) * scale + 128;
                pixels[ri + 2] = (b2 + c2) * scale + 128;
                pixels[ri + 5] = (b2 - c2) * scale + 128;
                pixels[ri + 3] = (b3 + c3) * scale + 128;
                pixels[ri + 4] = (b3 - c3) * scale + 128;
            }
        }

        /// <summary>
        /// YCbCr to BGRA color conversion with chroma upsampling.
        /// One thread per output pixel. Handles 4:4:4, 4:2:2, 4:2:0 subsampling.
        ///
        /// The pixel data is stored as blocks in MCU order. This kernel maps each output
        /// pixel (x,y) to the correct block and sample within the YCbCr block structure.
        /// </summary>
        static void YCbCrToBGRAKernel(Index1D pixelIndex,
            ArrayView<float> blockPixels, ArrayView<byte> bgra,
            int imgWidth, int imgHeight, int paddedWidth, int paddedHeight,
            int mcuPixelWidth, int mcuPixelHeight,
            int mcuCountX, int blocksPerMcu)
        {
            if (pixelIndex >= imgWidth * imgHeight) return;

            int px = pixelIndex % imgWidth;
            int py = pixelIndex / imgWidth;

            // Which MCU does this pixel belong to?
            int mcuX = px / mcuPixelWidth;
            int mcuY = py / mcuPixelHeight;
            int mcuIdx = mcuY * mcuCountX + mcuX;

            // Position within the MCU (in pixels)
            int inMcuX = px - mcuX * mcuPixelWidth;
            int inMcuY = py - mcuY * mcuPixelHeight;

            // For a 4:2:0 image (maxH=2, maxV=2): MCU has 4 Y blocks + 1 Cb + 1 Cr = 6 blocks
            // Block layout within MCU: Y blocks first (H*V blocks), then Cb (1 block), then Cr (1 block)
            // For 4:2:2 (maxH=2, maxV=1): 2 Y + 1 Cb + 1 Cr = 4 blocks
            // For 4:4:4 (maxH=1, maxV=1): 1 Y + 1 Cb + 1 Cr = 3 blocks

            // Y component (component 0): full resolution
            // Which Y block within the MCU?
            int yBlockH = inMcuX / 8; // horizontal block index
            int yBlockV = inMcuY / 8; // vertical block index
            int maxHSamp = mcuPixelWidth / 8;
            int yBlockIdx = yBlockV * maxHSamp + yBlockH; // block index within Y component
            int yInBlockX = inMcuX % 8;
            int yInBlockY = inMcuY % 8;

            int yGlobalBlock = mcuIdx * blocksPerMcu + yBlockIdx;
            float yVal = blockPixels[yGlobalBlock * 64 + yInBlockY * 8 + yInBlockX];

            // Cb component: subsampled - find its block(s)
            // Cb blocks start after all Y blocks in the MCU
            int yBlocksInMcu = maxHSamp * (mcuPixelHeight / 8);
            int cbBlockIdx = yBlocksInMcu; // First Cb block

            // For subsampled components, map pixel position to the subsampled coordinate
            // Cb/Cr have HSamp=1, VSamp=1 typically (for 4:2:0 and 4:2:2)
            int cbInBlockX = (inMcuX * 8) / mcuPixelWidth;
            int cbInBlockY = (inMcuY * 8) / mcuPixelHeight;

            int cbGlobalBlock = mcuIdx * blocksPerMcu + cbBlockIdx;
            float cbVal = blockPixels[cbGlobalBlock * 64 + cbInBlockY * 8 + cbInBlockX];

            // Cr component: one block after Cb
            int crBlockIdx = cbBlockIdx + 1;
            int crGlobalBlock = mcuIdx * blocksPerMcu + crBlockIdx;
            float crVal = blockPixels[crGlobalBlock * 64 + cbInBlockY * 8 + cbInBlockX];

            // BT.601 YCbCr -> RGB
            float cb = cbVal - 128;
            float cr = crVal - 128;
            int r = Clamp((int)(yVal + 1.402f * cr));
            int g = Clamp((int)(yVal - 0.344136f * cb - 0.714136f * cr));
            int b = Clamp((int)(yVal + 1.772f * cb));

            int outIdx = pixelIndex * 4;
            bgra[outIdx] = (byte)b;
            bgra[outIdx + 1] = (byte)g;
            bgra[outIdx + 2] = (byte)r;
            bgra[outIdx + 3] = 255;
        }

        static int Clamp(int val) => val < 0 ? 0 : val > 255 ? 255 : val;

        public void Dispose()
        {
            if (_ownsAccelerator)
                _accelerator.Dispose();
        }
    }
}
