﻿using NetworkCompression;
using UnityEngine;

namespace Networking.Compression
{
    public struct HuffmanOutputStream : IOutputStream
    {
        private NetworkCompressionCapture _capture;
        private NetworkCompressionModel _model;
        private byte[] _buffer;
        private int _bufferOffset;
        private ulong _bitBuffer;
        private int _currentBitIndex;
        private int _currentByteIndex;

        public HuffmanOutputStream(NetworkCompressionModel model, byte[] buffer, int bufferOffset,
            NetworkCompressionCapture capture)
        {
            _capture = capture;
            _model = model;
            _buffer = buffer;
            _bufferOffset = bufferOffset;
            _bitBuffer = 0;
            _currentBitIndex = 0;
            _currentByteIndex = bufferOffset;
        }

        public void Initialize(NetworkCompressionModel model, byte[] buffer, int bufferOffset,
            NetworkCompressionCapture capture)
        {
            this = new HuffmanOutputStream(model, buffer, bufferOffset, capture);
        }

        public void WriteRawBits(uint value, int numBits)
        {
            WriteRawBitsInternal(value, numBits);
            FlushBits();
        }

        public unsafe void WriteRawBytes(byte* value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                //TODO: only flush every n bytes
                WriteRawBits(value[i], 8);
            }
        }

        public void WritePackedNibble(uint value, int context)
        {
            if (value >= 16)
            {
                Debug.Assert(false, "Nibble bigger than 15");
            }

            if (_capture != null)
            {
                _capture.AddNibble(context, value);
            }

            ushort encodeEntry = _model.encodeTable[context, value];
            WriteRawBitsInternal((uint) (encodeEntry >> 8), encodeEntry & 0xFF);
            FlushBits();
        }

        public void WritePackedUInt(uint value, int context)
        {
            if (_capture != null)
            {
                _capture.AddUInt(context, value);
            }

            int bucket = 0;
            while (bucket + 1 < NetworkCompressionConstants.k_NumBuckets &&
                   value >= NetworkCompressionConstants.k_BucketOffsets[bucket + 1])
            {
                bucket++;
            }

            uint offset = NetworkCompressionConstants.k_BucketOffsets[bucket];
            int bits = NetworkCompressionConstants.k_BucketSizes[bucket];
            ushort encodeEntry = _model.encodeTable[context, bucket];
            WriteRawBitsInternal((uint) (encodeEntry >> 8), encodeEntry & 0xFF);
            WriteRawBitsInternal(value - offset, bits);
            FlushBits();
        }

        public void WritePackedIntDelta(int value, int baseline, int context)
        {
            WritePackedUIntDelta((uint) value, (uint) baseline, context);
        }

        public void WritePackedUIntDelta(uint value, uint baseline, int context)
        {
            int diff = (int) (baseline - value);
            // interleave negative values between positive values: 0, -1, 1, -2, 2
            uint interleaved = (uint) ((diff >> 31) ^ (diff << 1)); 
            WritePackedUInt(interleaved, context);
        }

        public int GetBitPosition2()
        {
            return (_currentByteIndex - _bufferOffset) * 8 - _currentBitIndex;
        }

        public NetworkCompressionModel GetModel()
        {
            return _model;
        }

        public int Flush()
        {
            while (_currentBitIndex > 0)
            {
                _buffer[_currentByteIndex++] = (byte) _bitBuffer;
                _currentBitIndex -= 8;
                _bitBuffer >>= 8;
            }

            _currentBitIndex = 0;
            return _currentByteIndex - _bufferOffset;
        }

        private void WriteRawBitsInternal(uint value, int numBits)
        {
#if UNITY_EDITOR
            Debug.Assert(numBits >= 0 && numBits <= 32);
            Debug.Assert(value < (1UL << numBits));
#endif

            _bitBuffer |= ((ulong) value << _currentBitIndex);
            _currentBitIndex += numBits;
        }

        private void FlushBits()
        {
            while (_currentBitIndex >= 8)
            {
                _buffer[_currentByteIndex++] = (byte) _bitBuffer;
                _currentBitIndex -= 8;
                _bitBuffer >>= 8;
            }
        }
    }
}