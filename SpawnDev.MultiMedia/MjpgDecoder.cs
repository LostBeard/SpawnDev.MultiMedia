namespace SpawnDev.MultiMedia
{
    /// <summary>
    /// Baseline JPEG parser and Huffman decoder for MJPG camera frames.
    /// This class handles ONLY the sequential bitstream parsing (Huffman decode)
    /// which cannot be parallelized. All parallel work (dequantize, IDCT, color convert)
    /// is done via ILGPU kernels in GpuMjpgDecoder.
    ///
    /// Supports: SOF0 (baseline), Huffman coding, 4:4:4 / 4:2:2 / 4:2:0 chroma subsampling.
    /// Does NOT support: progressive (SOF2), arithmetic coding, CMYK, multi-scan.
    /// These limitations are fine for MJPG camera output which is always baseline.
    /// </summary>
    public static class MjpgDecoder
    {
        /// <summary>
        /// Parse a JPEG and Huffman-decode all DCT coefficients.
        /// Returns structured data ready for GPU kernel processing.
        /// The returned coefficients are NOT dequantized - that's a kernel operation.
        /// </summary>
        public static JpegDecodeResult Parse(ReadOnlySpan<byte> jpegData)
        {
            return ParseJpeg(jpegData);
        }

        // ---- JPEG Parsing ----

        private static JpegDecodeResult ParseJpeg(ReadOnlySpan<byte> data)
        {
            var result = new JpegDecodeResult
            {
                QuantTables = new int[4][],
            };
            var dcTables = new HuffmanTable[4];
            var acTables = new HuffmanTable[4];

            int pos = 0;

            // Verify SOI marker
            if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
                throw new FormatException("Not a valid JPEG: missing SOI marker");
            pos = 2;

            while (pos < data.Length - 1)
            {
                if (data[pos] != 0xFF)
                    throw new FormatException($"Expected marker at position {pos}, got 0x{data[pos]:X2}");

                // Skip padding 0xFF bytes
                while (pos < data.Length && data[pos] == 0xFF) pos++;
                if (pos >= data.Length) break;

                byte marker = data[pos++];

                if (marker == 0xD9) break; // EOI
                if (marker == 0x00) continue; // Stuffed byte
                if (marker == 0xD8) continue; // SOI (restart)
                if (marker >= 0xD0 && marker <= 0xD7) continue; // RST markers

                // Read segment length
                if (pos + 2 > data.Length) break;
                int segLen = (data[pos] << 8) | data[pos + 1];
                var segment = data.Slice(pos + 2, segLen - 2);

                switch (marker)
                {
                    case 0xC0: // SOF0 - Baseline DCT
                        ParseSOF(segment, ref result);
                        break;
                    case 0xC4: // DHT - Define Huffman Table
                        ParseDHT(segment, dcTables, acTables);
                        break;
                    case 0xDB: // DQT - Define Quantization Table
                        ParseDQT(segment, ref result);
                        break;
                    case 0xDA: // SOS - Start of Scan
                        ParseSOS(segment, ref result);
                        var entropyStart = pos + segLen;
                        DecodeScan(data.Slice(entropyStart), ref result, dcTables, acTables);
                        return result;
                }

                pos += segLen;
            }

            if (result.Width == 0)
                throw new FormatException("JPEG has no SOF marker - not a valid baseline JPEG");

            return result;
        }

        private static void ParseSOF(ReadOnlySpan<byte> data, ref JpegDecodeResult result)
        {
            if (data[0] != 8)
                throw new FormatException($"Only 8-bit precision supported, got {data[0]}");

            result.Height = (data[1] << 8) | data[2];
            result.Width = (data[3] << 8) | data[4];
            result.ComponentCount = data[5];
            result.Components = new JpegComponentInfo[result.ComponentCount];

            result.MaxHSamp = 1;
            result.MaxVSamp = 1;

            for (int i = 0; i < result.ComponentCount; i++)
            {
                int offset = 6 + i * 3;
                result.Components[i] = new JpegComponentInfo
                {
                    Id = data[offset],
                    HSamp = (data[offset + 1] >> 4) & 0xF,
                    VSamp = data[offset + 1] & 0xF,
                    QuantTableId = data[offset + 2],
                };

                if (result.Components[i].HSamp > result.MaxHSamp)
                    result.MaxHSamp = result.Components[i].HSamp;
                if (result.Components[i].VSamp > result.MaxVSamp)
                    result.MaxVSamp = result.Components[i].VSamp;
            }

            result.McuWidth = result.MaxHSamp * 8;
            result.McuHeight = result.MaxVSamp * 8;
            result.McuCountX = (result.Width + result.McuWidth - 1) / result.McuWidth;
            result.McuCountY = (result.Height + result.McuHeight - 1) / result.McuHeight;
        }

        private static void ParseDQT(ReadOnlySpan<byte> data, ref JpegDecodeResult result)
        {
            int pos = 0;
            while (pos < data.Length)
            {
                int info = data[pos++];
                int precision = (info >> 4) & 0xF;
                int tableId = info & 0xF;
                if (tableId > 3) throw new FormatException($"Invalid quantization table ID: {tableId}");

                result.QuantTables[tableId] = new int[64];
                for (int i = 0; i < 64; i++)
                {
                    if (precision == 0)
                        result.QuantTables[tableId][i] = data[pos++];
                    else
                    {
                        result.QuantTables[tableId][i] = (data[pos] << 8) | data[pos + 1];
                        pos += 2;
                    }
                }
            }
        }

        private static void ParseDHT(ReadOnlySpan<byte> data, HuffmanTable[] dcTables, HuffmanTable[] acTables)
        {
            int pos = 0;
            while (pos < data.Length)
            {
                int info = data[pos++];
                int tableClass = (info >> 4) & 0xF;
                int tableId = info & 0xF;
                if (tableId > 3) throw new FormatException($"Invalid Huffman table ID: {tableId}");

                var bitCounts = new int[17];
                int totalSymbols = 0;
                for (int i = 1; i <= 16; i++)
                {
                    bitCounts[i] = data[pos++];
                    totalSymbols += bitCounts[i];
                }

                var symbols = new byte[totalSymbols];
                for (int i = 0; i < totalSymbols; i++)
                    symbols[i] = data[pos++];

                var table = new HuffmanTable
                {
                    Symbols = symbols,
                    MinCode = new int[17],
                    MaxCode = new int[17],
                    ValOffset = new int[17],
                };

                int code = 0;
                int symbolIndex = 0;
                for (int bits = 1; bits <= 16; bits++)
                {
                    table.MinCode[bits] = code;
                    table.ValOffset[bits] = symbolIndex - code;
                    table.MaxCode[bits] = bitCounts[bits] > 0 ? code + bitCounts[bits] - 1 : -1;
                    symbolIndex += bitCounts[bits];
                    code = (code + bitCounts[bits]) << 1;
                }

                if (tableClass == 0) dcTables[tableId] = table;
                else acTables[tableId] = table;
            }
        }

        private static void ParseSOS(ReadOnlySpan<byte> data, ref JpegDecodeResult result)
        {
            int componentCount = data[0];
            for (int i = 0; i < componentCount; i++)
            {
                int compId = data[1 + i * 2];
                int tableIds = data[2 + i * 2];
                for (int c = 0; c < result.ComponentCount; c++)
                {
                    if (result.Components[c].Id == compId)
                    {
                        result.Components[c].DcTableId = (tableIds >> 4) & 0xF;
                        result.Components[c].AcTableId = tableIds & 0xF;
                        break;
                    }
                }
            }
        }

        // ---- Entropy Decoding (sequential - must be CPU) ----

        private static void DecodeScan(ReadOnlySpan<byte> data, ref JpegDecodeResult result,
            HuffmanTable[] dcTables, HuffmanTable[] acTables)
        {
            int totalMcus = result.McuCountX * result.McuCountY;

            // Count blocks per MCU
            int blocksPerMcu = 0;
            for (int c = 0; c < result.ComponentCount; c++)
                blocksPerMcu += result.Components[c].HSamp * result.Components[c].VSamp;

            result.BlocksPerMcu = blocksPerMcu;
            int totalBlocks = totalMcus * blocksPerMcu;

            // Flat array: [totalBlocks * 64] DCT coefficients (NOT dequantized)
            // Layout: sequential blocks, each block is 64 coefficients in zigzag-reordered spatial order
            result.DctCoefficients = new int[totalBlocks * 64];

            // Block-to-component mapping (which component each block belongs to)
            result.BlockComponentIndex = new int[blocksPerMcu];
            // Block position within component's sampling grid
            result.BlockHIndex = new int[blocksPerMcu];
            result.BlockVIndex = new int[blocksPerMcu];

            int bi = 0;
            for (int c = 0; c < result.ComponentCount; c++)
            {
                for (int v = 0; v < result.Components[c].VSamp; v++)
                {
                    for (int h = 0; h < result.Components[c].HSamp; h++)
                    {
                        result.BlockComponentIndex[bi] = c;
                        result.BlockHIndex[bi] = h;
                        result.BlockVIndex[bi] = v;
                        bi++;
                    }
                }
            }

            // Build flat quant table index per block (for GPU dequantize kernel)
            result.BlockQuantTableId = new int[blocksPerMcu];
            bi = 0;
            for (int c = 0; c < result.ComponentCount; c++)
                for (int v = 0; v < result.Components[c].VSamp; v++)
                    for (int h = 0; h < result.Components[c].HSamp; h++)
                        result.BlockQuantTableId[bi++] = result.Components[c].QuantTableId;

            // Huffman decode all blocks
            var reader = new BitReader(data);
            var dcPred = new int[result.ComponentCount];

            for (int mcuIdx = 0; mcuIdx < totalMcus; mcuIdx++)
            {
                int blockBase = mcuIdx * blocksPerMcu;
                bi = 0;
                for (int c = 0; c < result.ComponentCount; c++)
                {
                    var comp = result.Components[c];
                    for (int v = 0; v < comp.VSamp; v++)
                    {
                        for (int h = 0; h < comp.HSamp; h++)
                        {
                            int blockOffset = (blockBase + bi) * 64;
                            DecodeBlock(ref reader, ref dcTables[comp.DcTableId],
                                ref acTables[comp.AcTableId],
                                ref dcPred[c], result.DctCoefficients, blockOffset);
                            bi++;
                        }
                    }
                }
            }
        }

        private static void DecodeBlock(ref BitReader reader, ref HuffmanTable dcTable,
            ref HuffmanTable acTable, ref int dcPred, int[] coeffs, int offset)
        {
            // DC coefficient
            int dcCategory = reader.DecodeHuffman(ref dcTable);
            int dcValue = 0;
            if (dcCategory > 0)
            {
                dcValue = reader.ReadBits(dcCategory);
                if (dcValue < (1 << (dcCategory - 1)))
                    dcValue -= (1 << dcCategory) - 1;
            }
            dcPred += dcValue;
            coeffs[offset + ZigZag[0]] = dcPred;

            // AC coefficients (zigzag reordered to spatial order)
            int k = 1;
            while (k < 64)
            {
                int symbol = reader.DecodeHuffman(ref acTable);
                if (symbol == 0x00) break; // EOB

                int runLength = (symbol >> 4) & 0xF;
                int acCategory = symbol & 0xF;

                k += runLength;
                if (k >= 64) break;

                if (acCategory > 0)
                {
                    int acValue = reader.ReadBits(acCategory);
                    if (acValue < (1 << (acCategory - 1)))
                        acValue -= (1 << acCategory) - 1;
                    coeffs[offset + ZigZag[k]] = acValue;
                }
                k++;
            }
        }

        // Zigzag scan order -> spatial position
        internal static readonly int[] ZigZag =
        {
             0,  1,  8, 16,  9,  2,  3, 10,
            17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
        };

        private ref struct BitReader
        {
            private readonly ReadOnlySpan<byte> _data;
            private int _pos;
            private int _bits;
            private int _bitCount;

            public BitReader(ReadOnlySpan<byte> data)
            {
                _data = data;
                _pos = 0;
                _bits = 0;
                _bitCount = 0;
            }

            public int ReadBits(int count)
            {
                while (_bitCount < count)
                {
                    byte b = NextByte();
                    _bits = (_bits << 8) | b;
                    _bitCount += 8;
                }
                _bitCount -= count;
                return (_bits >> _bitCount) & ((1 << count) - 1);
            }

            public int DecodeHuffman(ref HuffmanTable table)
            {
                int code = 0;
                for (int bits = 1; bits <= 16; bits++)
                {
                    code = (code << 1) | ReadBits(1);
                    if (table.MaxCode[bits] >= 0 && code <= table.MaxCode[bits])
                        return table.Symbols[code + table.ValOffset[bits]];
                }
                throw new FormatException("Invalid Huffman code");
            }

            private byte NextByte()
            {
                if (_pos >= _data.Length) return 0;
                byte b = _data[_pos++];
                if (b == 0xFF)
                {
                    byte next = _pos < _data.Length ? _data[_pos] : (byte)0;
                    if (next == 0x00) { _pos++; return 0xFF; }
                    _pos = _data.Length;
                    return 0;
                }
                return b;
            }
        }

        private struct HuffmanTable
        {
            public byte[] Symbols;
            public int[] MinCode, MaxCode, ValOffset;
        }
    }

    /// <summary>
    /// Result of JPEG parsing + Huffman decode.
    /// Contains all data needed for GPU kernel processing.
    /// </summary>
    public class JpegDecodeResult
    {
        public int Width, Height;
        public int ComponentCount;
        public JpegComponentInfo[] Components;
        public int MaxHSamp, MaxVSamp;
        public int McuWidth, McuHeight;
        public int McuCountX, McuCountY;
        public int BlocksPerMcu;

        /// <summary>Quantization tables [tableId][64 values]. NOT yet applied to coefficients.</summary>
        public int[][] QuantTables;

        /// <summary>
        /// Flat DCT coefficient array: [totalBlocks * 64].
        /// Layout: blocks are sequential, each block has 64 coefficients in spatial 8x8 order.
        /// These are raw Huffman-decoded values - NOT dequantized.
        /// Dequantization (multiply by quant table) is a kernel operation.
        /// </summary>
        public int[] DctCoefficients;

        /// <summary>Which component index each block within an MCU belongs to.</summary>
        public int[] BlockComponentIndex;
        /// <summary>Horizontal position within component's sampling grid.</summary>
        public int[] BlockHIndex;
        /// <summary>Vertical position within component's sampling grid.</summary>
        public int[] BlockVIndex;
        /// <summary>Which quant table each block uses.</summary>
        public int[] BlockQuantTableId;

        public int TotalBlocks => McuCountX * McuCountY * BlocksPerMcu;
    }

    public struct JpegComponentInfo
    {
        public int Id;
        public int HSamp, VSamp;
        public int QuantTableId;
        public int DcTableId, AcTableId;
    }
}
