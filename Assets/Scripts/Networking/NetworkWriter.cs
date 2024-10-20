using UnityEngine;

namespace Networking
{
    public unsafe struct NetworkWriter
    {
        private readonly uint* _output;
        private byte _fieldMask;
        private int _position;
        private int _nextFieldIndex;
        private readonly int _bufferSize;
        private readonly bool _generateSchema;

        private FieldInfo _currentField;
        private readonly NetworkSchema _schema;
        private static char[] _writeStringBuf = new char[64];
        private static readonly byte[] ByteBuffer = new byte[1024 * 32];

        public NetworkWriter(uint* buffer, int bufferSize, NetworkSchema schema, bool generateSchema = false)
        {
            _output = buffer;
            _bufferSize = bufferSize;
            _position = 0;
            _schema = schema;
            _currentField = null;
            _nextFieldIndex = 0;
            _generateSchema = generateSchema;
            _fieldMask = 0;
        }

        public int GetLength()
        {
            return _position;
        }

        private void ValidateOrGenerateSchema(string name, FieldType type, int bits = 0, bool delta = false,
            int precision = 0,
            int arraySize = 0)
        {
            if (_position + arraySize >= _bufferSize)
            {
                // This is a really hard error to recover from. So we just try to make sure everything stops...
                GameDebug.Assert(false, "Out of buffer space in NetworkWriter.");
            }

            // Precision is amount of digits (10^-3)
            GameDebug.Assert(precision < 4,
                "Precision has to be less than 4 digits. If you need more use unquantized values");
            if (_generateSchema)
            {
                if (type == FieldType.Bool || type == FieldType.ByteArray || type == FieldType.String)
                    GameDebug.Assert(delta == false,
                        "Delta compression of fields of type bool, bytearray and string not supported");
                // TOULF m_Scheme will contain scheme for ALL of the *entity* (not component)
                _schema.AddField(new FieldInfo()
                {
                    Name = name,
                    FieldType = type,
                    Bits = bits,
                    Delta = delta,
                    Precision = precision,
                    ArraySize = arraySize,
                    FieldMask = _fieldMask,
                    StartContext = _schema.NumFields * NetworkConfig.MAXContextsPerField +
                                   _schema.ID * NetworkConfig.MAXContextsPerSchema + NetworkConfig.FirstSchemaContext
                });
            }
            else if (_schema != null)
            {
                _currentField = _schema.Fields[_nextFieldIndex];
                GameDebug.Assert(_currentField.Name == name);
                GameDebug.Assert(_currentField.FieldType == type);
                GameDebug.Assert(_currentField.Bits == bits);
                GameDebug.Assert(_currentField.Delta == delta);
                GameDebug.Assert(_currentField.Precision == precision);
                GameDebug.Assert(_currentField.ArraySize == arraySize);
                GameDebug.Assert(_currentField.FieldMask == _fieldMask);

                ++_nextFieldIndex;
            }

            // TOULF when is it ok that m_Scheme being null?
        }

        public enum FieldSectionType
        {
            OnlyPredicting,
            OnlyNotPredicting
        }

        public void SetFieldSection(FieldSectionType type)
        {
            GameDebug.Assert(_fieldMask == 0, "Field masks cannot be combined.");
            if (type == FieldSectionType.OnlyNotPredicting)
            {
                _fieldMask = 0x1;
            }
            else
            {
                _fieldMask = 0x2;
            }
        }

        public void ClearFieldSection()
        {
            GameDebug.Assert(_fieldMask != 0, "Trying to clear a field mask but none has been set.");
            _fieldMask = 0;
        }

        public void WriteBoolean(string name, bool value)
        {
            ValidateOrGenerateSchema(name, FieldType.Bool, 1);
            _output[_position++] = value ? 1u : 0u;
        }

        public void WriteByte(string name, byte value)
        {
            ValidateOrGenerateSchema(name, FieldType.UInt, 8, true);
            _output[_position++] = value;
        }

        public void WriteInt16(string name, short value)
        {
            ValidateOrGenerateSchema(name, FieldType.Int, 16, true);
            _output[_position++] = (ushort) value;
        }

        public void WriteUInt16(string name, ushort value)
        {
            ValidateOrGenerateSchema(name, FieldType.UInt, 16, true);
            _output[_position++] = value;
        }

        public void WriteInt32(string name, int value)
        {
            ValidateOrGenerateSchema(name, FieldType.Int, 32, true);
            _output[_position++] = (uint) value;
        }

        public void WriteUInt32(string name, uint value)
        {
            ValidateOrGenerateSchema(name, FieldType.UInt, 32, true);
            _output[_position++] = value;
        }

        public void WriteFloat(string name, float value)
        {
            ValidateOrGenerateSchema(name, FieldType.Float, 32);
            _output[_position++] = NetworkUtils.FloatToUInt32(value);
        }

        public void WriteFloatQ(string name, float value, int precision = 3)
        {
            ValidateOrGenerateSchema(name, FieldType.Float, 32, true, precision);
            _output[_position++] = (uint) Mathf.RoundToInt(value * NetworkConfig.EncoderPrecisionScales[precision]);
        }

        public enum OverrunBehaviour
        {
            AssertMaxLength,
            WarnAndTrunc,
            SilentTrunc
        }

        public void WriteString(string name, string value, int maxLength = 64,
            OverrunBehaviour overrunBehaviour = OverrunBehaviour.WarnAndTrunc)
        {
            if (value == null)
            {
                value = "";
            }
            
            if (value.Length > _writeStringBuf.Length)
            {
                _writeStringBuf = new char[_writeStringBuf.Length * 2];
            }
            
            value.CopyTo(0, _writeStringBuf, 0, value.Length);
            WriteString(name, _writeStringBuf, value.Length, maxLength, overrunBehaviour);
        }

        public void WriteString(string name, char[] value, int length, int maxLength, OverrunBehaviour overrunBehaviour)
        {
            ValidateOrGenerateSchema(name, FieldType.String, 0, false, 0, maxLength);

            GameDebug.Assert(maxLength <= ByteBuffer.Length, "NetworkWriter: Max length has to be less than {0}",
                ByteBuffer.Length);
            GameDebug.Assert((maxLength & 0x3) == 0, "MaxLength has to be 32bit aligned");

            var byteCount = 0;
            if (length > 0)
            {
                // Ensure the (utf-8) *encoded* string is not too big. If it is, cut it off,
                // convert back to unicode and then back again to utf-8. This little dance gives
                // a valid utf-8 string within the buffer size.
                byteCount = NetworkConfig.Encoding.GetBytes(value, 0, length, ByteBuffer, 0);
                if (byteCount > maxLength)
                {
                    if (overrunBehaviour == OverrunBehaviour.AssertMaxLength)
                    {
                        GameDebug.Assert(false,
                            "NetworkWriter : string {0} too long. (Using {1}/{2} allowed encoded bytes): ", value,
                            byteCount, maxLength);
                    }

                    // truncate
                    var truncWithBadEnd = NetworkConfig.Encoding.GetString(ByteBuffer, 0, maxLength);
                    var truncOk = truncWithBadEnd.Substring(0, truncWithBadEnd.Length - 1);
                    var newbyteCount = NetworkConfig.Encoding.GetBytes(truncOk, 0, truncOk.Length, ByteBuffer, 0);

                    if (overrunBehaviour == OverrunBehaviour.WarnAndTrunc)
                    {
                        GameDebug.LogWarning(
                            $"NetworkWriter : truncated string with {byteCount - newbyteCount} bytes. (result: {truncOk})");
                    }

                    byteCount = newbyteCount;
                    GameDebug.Assert(byteCount <= maxLength, "String encoding failed");
                }
            }

            _output[_position++] = (uint) byteCount;
            byte* dst = (byte*) (_output + _position);
            int i = 0;
            for (; i < byteCount; ++i)
            {
                *dst++ = ByteBuffer[i];
            }

            for (; i < maxLength; ++i)
            {
                *dst++ = 0;
            }

            GameDebug.Assert(((uint) dst & 0x3) == 0, "Expected to stay aligned!");
            _position += maxLength / 4;
        }

        public void WriteBytes(string name, byte[] value, int srcIndex, int count, int maxCount)
        {
            GameDebug.Assert((maxCount & 0x3) == 0, "MaxCount has to be 32bit aligned");
            ValidateOrGenerateSchema(name, FieldType.ByteArray, 0, false, 0, maxCount);
            if (count > ushort.MaxValue)
            {
                throw new System.ArgumentException("NetworkWriter : Byte buffer too big : " + count);
            }

            _output[_position++] = (uint) count;
            byte* dst = (byte*) (_output + _position);
            int i = 0;
            for (; i < count; ++i)
            {
                *dst++ = value[i];
            }

            for (; i < maxCount; ++i)
            {
                *dst++ = 0;
            }

            _position += maxCount / 4;
        }

        public void WriteVector2(string name, Vector2 value)
        {
            ValidateOrGenerateSchema(name, FieldType.Vector2, 32);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.x);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.y);
        }

        public void WriteVector2Q(string name, Vector2 value, int precision = 3)
        {
            ValidateOrGenerateSchema(name, FieldType.Vector2, 32, true, precision);
            _output[_position++] = (uint) Mathf.RoundToInt(value.x * NetworkConfig.EncoderPrecisionScales[precision]);
            _output[_position++] = (uint) Mathf.RoundToInt(value.y * NetworkConfig.EncoderPrecisionScales[precision]);
        }

        public void WriteVector3(string name, Vector3 value)
        {
            ValidateOrGenerateSchema(name, FieldType.Vector3, 32);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.x);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.y);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.z);
        }

        public void WriteVector3Q(string name, Vector3 value, int precision = 3)
        {
            ValidateOrGenerateSchema(name, FieldType.Vector3, 32, true, precision);
            _output[_position++] = (uint) Mathf.RoundToInt(value.x * NetworkConfig.EncoderPrecisionScales[precision]);
            _output[_position++] = (uint) Mathf.RoundToInt(value.y * NetworkConfig.EncoderPrecisionScales[precision]);
            _output[_position++] = (uint) Mathf.RoundToInt(value.z * NetworkConfig.EncoderPrecisionScales[precision]);
        }

        public void WriteQuaternion(string name, Quaternion value)
        {
            ValidateOrGenerateSchema(name, FieldType.Quaternion, 32);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.x);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.y);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.z);
            _output[_position++] = NetworkUtils.FloatToUInt32(value.w);
        }

        public void WriteQuaternionQ(string name, Quaternion value, int precision = 3)
        {
            ValidateOrGenerateSchema(name, FieldType.Quaternion, 32, true, precision);
            _output[_position++] = (uint) Mathf.RoundToInt(value.x * NetworkConfig.EncoderPrecisionScales[precision]);
            _output[_position++] = (uint) Mathf.RoundToInt(value.y * NetworkConfig.EncoderPrecisionScales[precision]);
            _output[_position++] = (uint) Mathf.RoundToInt(value.z * NetworkConfig.EncoderPrecisionScales[precision]);
            _output[_position++] = (uint) Mathf.RoundToInt(value.w * NetworkConfig.EncoderPrecisionScales[precision]);
        }

        public void Flush()
        {
            if (_generateSchema)
            {
                _schema.ResetPredictPlan();
            }
        }
    }
}