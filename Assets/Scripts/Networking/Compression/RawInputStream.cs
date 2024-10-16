namespace Networking.Compression
{
    public struct RawInputStream : IInputStream
    {
        private byte[] _buffer;
        private int _bufferOffset;
        private int _currentByteIndex;

        public RawInputStream(byte[] buffer, int bufferOffset)
        {
            _buffer = buffer;
            _bufferOffset = bufferOffset;
            _currentByteIndex = bufferOffset;
        }

        public void Initialize(NetworkCompressionModel model, byte[] buffer, int bufferOffset)
        {
            this = new RawInputStream(buffer, bufferOffset);
        }

        public uint ReadRawBits(int numBits)
        {
            uint value = 0;
            for (var i = 0; i < numBits; i += 8)
            {
                value |= (uint) _buffer[_currentByteIndex++] << i;
            }

            return value;
        }

        public void ReadRawBytes(byte[] dstBuffer, int dstIndex, int count)
        {
            for (var i = 0; i < count; i++)
            {
                dstBuffer[dstIndex + i] = _buffer[_currentByteIndex + i];
            }

            _currentByteIndex += count;
        }

        public void SkipRawBits(int numBits)
        {
            _currentByteIndex += (numBits + 7) >> 3;
        }

        public void SkipRawBytes(int count)
        {
            _currentByteIndex += count;
        }

        public uint ReadPackedNibble(int context)
        {
            return _buffer[_currentByteIndex++];
        }

        public uint ReadPackedUInt(int context)
        {
            uint value = _buffer[_currentByteIndex + 0] | ((uint) _buffer[_currentByteIndex + 1] << 8) |
                         ((uint) _buffer[_currentByteIndex + 2] << 16) |
                         ((uint) _buffer[_currentByteIndex + 3] << 24);
            _currentByteIndex += 4;
            return value;
        }

        public int ReadPackedIntDelta(int baseline, int context)
        {
            return (int) ReadPackedUIntDelta((uint) baseline, context);
        }

        public uint ReadPackedUIntDelta(uint baseline, int context)
        {
            uint folded = ReadPackedUInt(context);
            // DeInterleave values from [0, -1, 1, -2, 2...] to [..., -2, -1, -0, 1, 2, ...]
            uint delta = (folded >> 1) ^ (uint) -(int) (folded & 1);
            return baseline - delta;
        }

        public int GetBitPosition2()
        {
            return (_currentByteIndex - _bufferOffset) * 8;
        }

        public NetworkCompressionModel GetModel()
        {
            return null;
        }
    }
}