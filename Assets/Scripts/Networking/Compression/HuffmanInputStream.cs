using NetworkCompression;
using UnityEngine;

namespace Networking.Compression
{
    public struct HuffmanInputStream : IInputStream
    {
        private NetworkCompressionModel _model;
        private byte[] _buffer;
        private ulong _bitBuffer;
        private int _currentBitIndex;
        private int _currentByteIndex;

        public HuffmanInputStream(NetworkCompressionModel model, byte[] buffer, int bufferOffset)
        {
            _model = model;
            _buffer = buffer;
            _currentBitIndex = 0;
            _currentByteIndex = bufferOffset;
            _bitBuffer = 0;
        }

        public void Initialize(NetworkCompressionModel model, byte[] buffer, int bufferOffset)
        {
            this = new HuffmanInputStream(model, buffer, bufferOffset);
        }

        public uint ReadRawBits(int numBits)
        {
            FillBitBuffer();
            return ReadRawBitsInternal(numBits);
        }

        public void ReadRawBytes(byte[] dstBuffer, int dstIndex, int count)
        {
            for (var i = 0; i < count; i++)
            {
                dstBuffer[dstIndex + i] = (byte) ReadRawBits(8);
            }
        }

        public void SkipRawBits(int numBits)
        {
            // TODO: implement this properly
            while (numBits >= 32)
            {
                ReadRawBits(32);
                numBits -= 32;
            }

            ReadRawBits(numBits);
        }

        public void SkipRawBytes(int count)
        {
            SkipRawBits(count * 8);
        }

        public uint ReadPackedNibble(int context)
        {
            FillBitBuffer();
            uint peekMask = (1u << NetworkCompressionConstants.k_MaxHuffmanSymbolLength) - 1u;
            uint peekBits = (uint) _bitBuffer & peekMask;
            ushort huffmanEntry = _model.decodeTable[context, peekBits];
            int symbol = huffmanEntry >> 8;
            int length = huffmanEntry & 0xFF;

            // Skip Huffman bits
            _bitBuffer >>= length;
            _currentBitIndex -= length;
            return (uint) symbol;
        }

        public uint ReadPackedUInt(int context)
        {
            FillBitBuffer();
            uint peekMask = (1u << NetworkCompressionConstants.k_MaxHuffmanSymbolLength) - 1u;
            uint peekBits = (uint) _bitBuffer & peekMask;
            ushort huffmanEntry = _model.decodeTable[context, peekBits];
            int symbol = huffmanEntry >> 8;
            int length = huffmanEntry & 0xFF;

            // Skip Huffman bits
            _bitBuffer >>= length;
            _currentBitIndex -= length;

            uint offset = NetworkCompressionConstants.k_BucketOffsets[symbol];
            int bits = NetworkCompressionConstants.k_BucketSizes[symbol];
            return ReadRawBitsInternal(bits) + offset;
        }

        public int ReadPackedIntDelta(int baseline, int context)
        {
            return (int) ReadPackedUIntDelta((uint) baseline, context);
        }

        public uint ReadPackedUIntDelta(uint baseline, int context)
        {
            uint folded = ReadPackedUInt(context);
            uint delta =
                (folded >> 1) ^
                (uint) -(int) (folded &
                               1); // Deinterleave values from [0, -1, 1, -2, 2...] to [..., -2, -1, -0, 1, 2, ...]
            return baseline - delta;
        }

        public int GetBitPosition2()
        {
            return _currentByteIndex * 8 - _currentBitIndex;
        }

        public NetworkCompressionModel GetModel()
        {
            return _model;
        }

        private void FillBitBuffer()
        {
            // fill a ulong (unsigned 64-bit integer) with 8 buffer values.
            while (_currentBitIndex <= 56)
            {
                _bitBuffer |= (ulong) _buffer[_currentByteIndex++] << _currentBitIndex;
                _currentBitIndex += 8;
            }
        }

        private uint ReadRawBitsInternal(int numBits)
        {
            Debug.Assert(numBits >= 0 && numBits <= 32);
            Debug.Assert(_currentBitIndex >= numBits);
            uint res = (uint) (_bitBuffer & ((1UL << numBits) - 1UL));
            _bitBuffer >>= numBits;
            _currentBitIndex -= numBits;
            return res;
        }
    }
}