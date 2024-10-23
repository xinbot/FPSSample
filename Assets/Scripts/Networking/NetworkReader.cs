using UnityEngine;

namespace Networking
{
    public unsafe struct NetworkReader
    {
        private int _position;
        private int _nextFieldIndex;

        private FieldInfo _currentField;
        private readonly uint* _input;
        private readonly NetworkSchema _schema;

        private static readonly char[] CharBuffer = new char[1024 * 32];

        public NetworkReader(uint* buffer, NetworkSchema schema)
        {
            _input = buffer;
            _position = 0;
            _schema = schema;
            _currentField = null;
            _nextFieldIndex = 0;
        }

        public bool ReadBoolean()
        {
            ValidateSchema(FieldType.Bool, 1, false);
            return _input[_position++] == 1;
        }

        public byte ReadByte()
        {
            ValidateSchema(FieldType.UInt, 8, true);
            return (byte) _input[_position++];
        }

        public short ReadInt16()
        {
            ValidateSchema(FieldType.Int, 16, true);
            return (short) _input[_position++];
        }

        public ushort ReadUInt16()
        {
            ValidateSchema(FieldType.UInt, 16, true);
            return (ushort) _input[_position++];
        }

        public int ReadInt32()
        {
            ValidateSchema(FieldType.Int, 32, true);
            return (int) _input[_position++];
        }

        public uint ReadUInt32()
        {
            ValidateSchema(FieldType.UInt, 32, true);
            return _input[_position++];
        }

        public float ReadFloat()
        {
            ValidateSchema(FieldType.Float, 32, false);
            return NetworkUtils.UInt32ToFloat(_input[_position++]);
        }

        public float ReadFloatQ()
        {
            GameDebug.Assert(_schema != null, "Schema required for reading quantized values");
            ValidateSchema(FieldType.Float, 32, true);
            return (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
        }

        public string ReadString(int maxLength = 64)
        {
            ValidateSchema(FieldType.String, 0, false, maxLength);

            uint count = _input[_position++];
            GameDebug.Assert(count <= maxLength);
            byte* data = (byte*) (_input + _position);

            _position += maxLength / 4;

            if (count == 0)
                return "";

            fixed (char* dest = CharBuffer)
            {
                var numChars = NetworkConfig.Encoding.GetChars(data, (int) count, dest, CharBuffer.Length);
                return new string(CharBuffer, 0, numChars);
            }
        }

        public Vector2 ReadVector2()
        {
            ValidateSchema(FieldType.Vector2, 32, false);

            Vector2 result;
            result.x = NetworkUtils.UInt32ToFloat(_input[_position++]);
            result.y = NetworkUtils.UInt32ToFloat(_input[_position++]);
            return result;
        }

        public Vector2 ReadVector2Q()
        {
            GameDebug.Assert(_schema != null, "Schema required for reading quantized values");
            ValidateSchema(FieldType.Vector2, 32, true);

            Vector2 result;
            result.x = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            result.y = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            return result;
        }

        public Vector3 ReadVector3()
        {
            ValidateSchema(FieldType.Vector3, 32, false);

            Vector3 result;
            result.x = NetworkUtils.UInt32ToFloat(_input[_position++]);
            result.y = NetworkUtils.UInt32ToFloat(_input[_position++]);
            result.z = NetworkUtils.UInt32ToFloat(_input[_position++]);
            return result;
        }

        public Vector3 ReadVector3Q()
        {
            GameDebug.Assert(_schema != null, "Schema required for reading quantized values");
            ValidateSchema(FieldType.Vector3, 32, true);

            Vector3 result;
            result.x = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            result.y = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            result.z = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            return result;
        }

        public Quaternion ReadQuaternion()
        {
            ValidateSchema(FieldType.Quaternion, 32, false);

            Quaternion result;
            result.x = NetworkUtils.UInt32ToFloat(_input[_position++]);
            result.y = NetworkUtils.UInt32ToFloat(_input[_position++]);
            result.z = NetworkUtils.UInt32ToFloat(_input[_position++]);
            result.w = NetworkUtils.UInt32ToFloat(_input[_position++]);
            return result;
        }

        public Quaternion ReadQuaternionQ()
        {
            GameDebug.Assert(_schema != null, "Schema required for reading quantized values");
            ValidateSchema(FieldType.Quaternion, 32, true);

            Quaternion result;
            result.x = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            result.y = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            result.z = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            result.w = (int) _input[_position++] * NetworkConfig.DecoderPrecisionScales[_currentField.Precision];
            return result;
        }

        public int ReadBytes(byte[] value, int dstIndex, int maxLength)
        {
            ValidateSchema(FieldType.ByteArray, 0, false, maxLength);

            uint count = _input[_position++];
            byte* src = (byte*) (_input + _position);
            for (int i = 0; i < count; ++i)
            {
                value[i] = *src++;
            }

            _position += maxLength / 4;
            return (int) count;
        }

        private void ValidateSchema(FieldType type, int bits, bool delta, int arraySize = 0)
        {
            if (_schema == null)
            {
                return;
            }

            _currentField = _schema.Fields[_nextFieldIndex];

            var message =
                $"Property:{_currentField.Name} has unexpected field type:{type} Expected:{_currentField.FieldType}";
            GameDebug.Assert(type == _currentField.FieldType, message);
            GameDebug.Assert(bits == _currentField.Bits);
            GameDebug.Assert(arraySize == _currentField.ArraySize);

            ++_nextFieldIndex;
        }
    }
}