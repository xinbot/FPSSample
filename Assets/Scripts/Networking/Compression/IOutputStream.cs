﻿namespace Networking.Compression
{
    public interface IOutputStream
    {
        void Initialize(NetworkCompressionModel model, byte[] buffer, int bufferOffset,
            NetworkCompressionCapture capture);

        void WriteRawBits(uint value, int numBits);

        unsafe void WriteRawBytes(byte* value, int count);

        void WritePackedNibble(uint value, int context);

        void WritePackedUInt(uint value, int context);

        void WritePackedIntDelta(int value, int baseline, int context);

        void WritePackedUIntDelta(uint value, uint baseline, int context);

        int GetBitPosition2();

        int Flush();

        NetworkCompressionModel GetModel();
    }
}