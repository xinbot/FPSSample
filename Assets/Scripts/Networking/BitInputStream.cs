namespace Networking
{
    public struct BitInputStream
    {
        private readonly byte[] _buffer;

        private ulong _bitStage;
        private int _currentBitIdx;
        private int _currentByteIdx;

        public BitInputStream(byte[] buffer)
        {
            _buffer = buffer;

            _bitStage = 0;
            _currentBitIdx = 0;
            _currentByteIdx = 0;
        }

        public void Initialize(byte[] buffer)
        {
            this = new BitInputStream(buffer);
        }

        public int GetBitPosition()
        {
            return _currentByteIdx * 8 - _currentBitIdx;
        }

        public long ReadUIntPacked()
        {
            int inputBits = 1;
            long value = 0;
            while (ReadBits(1) == 0)
            {
                value += (1L << inputBits);
                inputBits += 2;
            }

            if (inputBits > 32)
            {
                long low = ReadBits(32);
                long high = ReadBits(inputBits - 32);
                return value + (low | (high << 32));
            }

            return value + ReadBits(inputBits);
        }

        public long ReadIntDelta(long baseline)
        {
            var mapped = ReadUIntPacked();
            if ((mapped & 1) != 0)
            {
                return baseline + ((mapped + 1) >> 1);
            }

            return baseline - (mapped >> 1);
        }

        public uint ReadBits(int numBits)
        {
            GameDebug.Assert(numBits > 0 && numBits <= 32);

            while (_currentBitIdx < 32)
            {
                _bitStage |= (ulong) _buffer[_currentByteIdx++] << _currentBitIdx;
                _currentBitIdx += 8;
            }

            return ReadBitsInternal(numBits);
        }

        public void ReadBytes(byte[] dstBuffer, int dstIndex, int count)
        {
            Align();
            if (dstBuffer != null)
            {
                NetworkUtils.MemCopy(_buffer, _currentByteIdx, dstBuffer, dstIndex, count);
                _currentByteIdx += count;
            }
        }

        public int Align()
        {
            var remainder = _currentBitIdx % 8;
            if (remainder > 0)
            {
                var value = ReadBitsInternal(remainder);
                GameDebug.Assert(value == 0);
            }

            _currentByteIdx -= _currentBitIdx / 8;
            _currentBitIdx = 0;
            _bitStage = 0;
            return _currentByteIdx;
        }

        private uint ReadBitsInternal(int numBits)
        {
            GameDebug.Assert(_currentBitIdx >= numBits);
            var res = _bitStage & (((ulong) 1 << numBits) - 1);
            _bitStage >>= numBits;
            _currentBitIdx -= numBits;
            return (uint) res;
        }
    }
}