using System;
using UnityEngine;

namespace Networking
{
    public struct BitOutputStream
    {
        private readonly byte[] _buffer;

        private ulong _bitStage;
        private int _currentBitIdx;
        private int _currentByteIdx;

        public BitOutputStream(byte[] buffer)
        {
            _buffer = buffer;

            _bitStage = 0;
            _currentBitIdx = 0;
            _currentByteIdx = 0;
        }

        public int GetBitPosition()
        {
            return _currentByteIdx * 8 + _currentBitIdx;
        }

        public void WriteUIntPacked(long value)
        {
            GameDebug.Assert(value >= 0);

            int outputBits = 1;
            int numPrefixBits = 0;
            // RUTODO: Unroll this and merge with bit output. How do we actually verify inlining in C#?
            while (value >= (1L << outputBits))
            {
                value -= (1L << outputBits);
                outputBits += 2;
                numPrefixBits++;
            }

            WriteBits(1u << numPrefixBits, numPrefixBits + 1);

            if (outputBits > 32)
            {
                WriteBits((uint) value, 32);
                WriteBits((uint) (value >> 32), outputBits - 32);
            }
            else
            {
                WriteBits((uint) value, outputBits);
            }
        }

        public void WriteIntDelta(long value, long baseline)
        {
            var diff = baseline - value;
            if (diff < 0)
            {
                diff = (-diff << 1) - 1;
            }
            else
            {
                diff = diff << 1;
            }

            WriteUIntPacked(diff);
        }

        public void WriteIntDeltaNonZero(long value, long baseline)
        {
            var diff = value - baseline;
            GameDebug.Assert(diff != 0);
            if (diff < 0)
            {
                diff = (-diff << 1) - 1;
            }
            else
            {
                diff = (diff << 1) - 2;
            }

            WriteUIntPacked(diff);
        }

        public void WriteBits(uint value, int numBits)
        {
            GameDebug.Assert(numBits > 0 && numBits <= 32);
            GameDebug.Assert((UInt64.MaxValue << numBits & value) == 0);

            _bitStage |= ((ulong) value << _currentBitIdx);
            _currentBitIdx += numBits;

            while (_currentBitIdx >= 8)
            {
                _buffer[_currentByteIdx++] = (byte) _bitStage;
                _currentBitIdx -= 8;
                _bitStage >>= 8;
            }
        }

        public void WriteBytes(byte[] value, int srcIndex, int count)
        {
            Align();
            NetworkUtils.MemCopy(value, srcIndex, _buffer, _currentByteIdx, count);
            _currentByteIdx += count;
        }

        public int Align()
        {
            if (_currentBitIdx > 0)
            {
                WriteBits(0, 8 - _currentBitIdx);
            }

            return _currentByteIdx;
        }

        public int Flush()
        {
            Align();
            return _currentByteIdx;
        }

        public void SkipBytes(int bytes)
        {
            Debug.Assert(_currentBitIdx == 0);
            _currentByteIdx += bytes;
        }
    }
}