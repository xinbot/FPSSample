using UnityEngine;

namespace Networking.Compression
{
    public struct RawOutputStream : IOutputStream
    {
        private readonly NetworkCompressionCapture _capture;

        private byte[] _buffer;
        private int _bufferOffset;
        private int _currentByteIndex;

        public RawOutputStream(byte[] buffer, int bufferOffset, NetworkCompressionCapture capture)
        {
            _buffer = buffer;
            _bufferOffset = bufferOffset;
            _currentByteIndex = bufferOffset;
            _capture = capture;
        }

        public void Initialize(NetworkCompressionModel model, byte[] buffer, int bufferOffset,
            NetworkCompressionCapture capture)
        {
            this = new RawOutputStream(buffer, bufferOffset, capture);
        }

        public void WriteRawBits(uint value, int numBits)
        {
            for (var i = 0; i < numBits; i += 8)
            {
                _buffer[_currentByteIndex++] = (byte) value;
                value >>= 8;
            }
        }

        public unsafe void WriteRawBytes(byte* value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                _buffer[_currentByteIndex + i] = value[i];
            }

            _currentByteIndex += count;
        }

        public void WritePackedNibble(uint value, int context)
        {
            Debug.Assert(value < 16);
            if (_capture != null)
            {
                _capture.AddNibble(context, value);
            }

            _buffer[_currentByteIndex++] = (byte) value;
        }

        public void WritePackedUInt(uint value, int context)
        {
            if (_capture != null)
            {
                _capture.AddUInt(context, value);
            }

            _buffer[_currentByteIndex + 0] = (byte) value;
            _buffer[_currentByteIndex + 1] = (byte) (value >> 8);
            _buffer[_currentByteIndex + 2] = (byte) (value >> 16);
            _buffer[_currentByteIndex + 3] = (byte) (value >> 24);
            _currentByteIndex += 4;
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
            return (_currentByteIndex - _bufferOffset) * 8;
        }

        public NetworkCompressionModel GetModel()
        {
            return null;
        }

        public int Flush()
        {
            return _currentByteIndex - _bufferOffset;
        }
    }
}