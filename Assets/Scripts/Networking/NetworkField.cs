namespace Networking
{
    public enum FieldType
    {
        Bool,
        Int,
        UInt,
        Float,
        Vector2,
        Vector3,
        Quaternion,
        String,
        ByteArray
    }

    public class FieldInfo
    {
        public string Name;

        public int Bits;
        public int Precision;
        public int ArraySize;
        public int ByteOffset;
        public int StartContext;
        public byte FieldMask;
        public bool Delta;

        public FieldType FieldType;

        public FieldStatsBase Stats;
    }

    public abstract class FieldStatsBase
    {
        public abstract string GetValue(bool showRaw);

        public abstract string GetValueMin(bool showRaw);

        public abstract string GetValueMax(bool showRaw);

        public abstract string GetPrediction(bool showRaw);

        public abstract string GetPredictionMin(bool showRaw);

        public abstract string GetPredictionMax(bool showRaw);

        public abstract string GetDelta(bool showRaw);

        public abstract string GetDeltaMin(bool showRaw);

        public abstract string GetDeltaMax(bool showRaw);

        public static FieldStatsBase CreateFieldStats(FieldInfo fieldInfo)
        {
            switch (fieldInfo.FieldType)
            {
                case FieldType.Bool:
                    return new FieldStats<FieldValueBool>(fieldInfo);
                case FieldType.Int:
                    return new FieldStats<FieldValueInt>(fieldInfo);
                case FieldType.UInt:
                    return new FieldStats<FieldValueUInt>(fieldInfo);
                case FieldType.Float:
                    return new FieldStats<FieldValueFloat>(fieldInfo);
                case FieldType.Vector2:
                    return new FieldStats<FieldValueVector2>(fieldInfo);
                case FieldType.Vector3:
                    return new FieldStats<FieldValueVector3>(fieldInfo);
                case FieldType.Quaternion:
                    return new FieldStats<FieldValueQuaternion>(fieldInfo);
                case FieldType.String:
                    return new FieldStats<FieldValueString>(fieldInfo);
                case FieldType.ByteArray:
                    return new FieldStats<FieldValueByteArray>(fieldInfo);
                default:
                    GameDebug.Assert(false);
                    return null;
            }
        }

        public int GetNumWrites()
        {
            return NumWrites;
        }

        public int GetNumBitsWritten()
        {
            return NumBitsWritten;
        }

        protected int NumWrites;
        protected int NumBitsWritten;
    }

    public interface IFieldValue<T>
    {
        T Min(T other);
        T Max(T other);
        T Sub(T other);

        string ToString(FieldInfo fieldInfo, bool showRaw);
    }

    public class FieldStats<T> : FieldStatsBase where T : IFieldValue<T>
    {
        private T _value;
        private T _valueMin;
        private T _valueMax;

        private T _prediction;
        private T _predictionMin;
        private T _predictionMax;

        private T _delta;
        private T _deltaMin;
        private T _deltaMax;

        private readonly FieldInfo _fieldInfo;

        public FieldStats(FieldInfo fieldInfo)
        {
            _fieldInfo = fieldInfo;
        }

        public void Add(T value, T prediction, int bitsWritten)
        {
            _value = value;
            _prediction = prediction;
            _delta = value.Sub(prediction);

            if (NumWrites > 0)
            {
                _valueMin = _valueMin.Min(value);
                _valueMax = _valueMin.Max(value);
                _predictionMin = _predictionMin.Min(prediction);
                _predictionMax = _predictionMax.Max(prediction);
                _deltaMin = _deltaMin.Min(_delta);
                _deltaMax = _deltaMax.Max(_delta);
            }
            else
            {
                _valueMin = value;
                _valueMax = value;
                _predictionMin = prediction;
                _predictionMax = prediction;
            }

            NumBitsWritten += bitsWritten;
            NumWrites++;
        }

        public override string GetValue(bool showRaw)
        {
            return _value.ToString(_fieldInfo, showRaw);
        }

        public override string GetValueMin(bool showRaw)
        {
            return _valueMin.ToString(_fieldInfo, showRaw);
        }

        public override string GetValueMax(bool showRaw)
        {
            return _valueMax.ToString(_fieldInfo, showRaw);
        }

        public override string GetPrediction(bool showRaw)
        {
            return _prediction.ToString(_fieldInfo, showRaw);
        }

        public override string GetPredictionMin(bool showRaw)
        {
            return _predictionMin.ToString(_fieldInfo, showRaw);
        }

        public override string GetPredictionMax(bool showRaw)
        {
            return _predictionMax.ToString(_fieldInfo, showRaw);
        }

        public override string GetDelta(bool showRaw)
        {
            return _delta.ToString(_fieldInfo, showRaw);
        }

        public override string GetDeltaMin(bool showRaw)
        {
            return _deltaMin.ToString(_fieldInfo, showRaw);
        }

        public override string GetDeltaMax(bool showRaw)
        {
            return _deltaMax.ToString(_fieldInfo, showRaw);
        }
    }

    public struct FieldValueBool : IFieldValue<FieldValueBool>
    {
        private bool _value;

        public FieldValueBool(bool value)
        {
            _value = value;
        }

        public FieldValueBool Min(FieldValueBool other)
        {
            bool otherValue = other._value;
            return new FieldValueBool(_value && otherValue);
        }

        public FieldValueBool Max(FieldValueBool other)
        {
            bool otherValue = other._value;
            return new FieldValueBool(_value || otherValue);
        }

        public FieldValueBool Sub(FieldValueBool other)
        {
            bool otherValue = other._value;
            return new FieldValueBool(_value != otherValue);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            return "" + _value;
        }
    }

    public struct FieldValueInt : IFieldValue<FieldValueInt>
    {
        private int _value;

        public FieldValueInt(int value)
        {
            _value = value;
        }

        public FieldValueInt Min(FieldValueInt other)
        {
            int otherValue = other._value;
            return new FieldValueInt(_value < otherValue ? _value : otherValue);
        }

        public FieldValueInt Max(FieldValueInt other)
        {
            int otherValue = other._value;
            return new FieldValueInt(_value > otherValue ? _value : otherValue);
        }

        public FieldValueInt Sub(FieldValueInt other)
        {
            int otherValue = other._value;
            return new FieldValueInt(_value - otherValue);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            return "" + _value;
        }
    }

    public struct FieldValueUInt : IFieldValue<FieldValueUInt>
    {
        private uint _value;

        public FieldValueUInt(uint value)
        {
            _value = value;
        }

        public FieldValueUInt Min(FieldValueUInt other)
        {
            uint otherValue = other._value;
            return new FieldValueUInt(_value < otherValue ? _value : otherValue);
        }

        public FieldValueUInt Max(FieldValueUInt other)
        {
            uint otherValue = other._value;
            return new FieldValueUInt(_value > otherValue ? _value : otherValue);
        }

        public FieldValueUInt Sub(FieldValueUInt other)
        {
            uint otherValue = other._value;
            return new FieldValueUInt(_value - otherValue);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            return "" + _value;
        }
    }

    public struct FieldValueFloat : IFieldValue<FieldValueFloat>
    {
        private uint _value;

        public FieldValueFloat(uint value)
        {
            _value = value;
        }

        public FieldValueFloat Min(FieldValueFloat other)
        {
            uint otherValue = other._value;
            return new FieldValueFloat(_value < otherValue ? _value : otherValue);
        }

        public FieldValueFloat Max(FieldValueFloat other)
        {
            uint otherValue = other._value;
            return new FieldValueFloat(_value > otherValue ? _value : otherValue);
        }

        public FieldValueFloat Sub(FieldValueFloat other)
        {
            uint otherValue = other._value;
            return new FieldValueFloat(_value - otherValue);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            if (showRaw)
            {
                return "" + _value;
            }

            if (fieldInfo.Delta)
            {
                return "" + ((int) _value * NetworkConfig.DecoderPrecisionScales[fieldInfo.Precision]);
            }

            return "" + ConversionUtility.UInt32ToFloat(_value);
        }
    }

    public struct FieldValueVector2 : IFieldValue<FieldValueVector2>
    {
        private uint _valueX, _valueY;

        public FieldValueVector2(uint x, uint y)
        {
            _valueX = x;
            _valueY = y;
        }

        public FieldValueVector2 Min(FieldValueVector2 other)
        {
            return new FieldValueVector2(0, 0);
        }

        public FieldValueVector2 Max(FieldValueVector2 other)
        {
            return new FieldValueVector2(0, 0);
        }

        public FieldValueVector2 Sub(FieldValueVector2 other)
        {
            var o = other;
            return new FieldValueVector2(_valueX - o._valueX, _valueX - o._valueY);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            if (fieldInfo.Delta)
            {
                float scale = NetworkConfig.DecoderPrecisionScales[fieldInfo.Precision];
                return "(" + ((int) _valueX * scale) + ", " + ((int) _valueY * scale) + ")";
            }

            return "(" + ConversionUtility.UInt32ToFloat(_valueX) + ", " + ConversionUtility.UInt32ToFloat(_valueY) +
                   ")";
        }
    }

    public struct FieldValueVector3 : IFieldValue<FieldValueVector3>
    {
        private uint _valueX, _valueY, _valueZ;

        public FieldValueVector3(uint x, uint y, uint z)
        {
            _valueX = x;
            _valueY = y;
            _valueZ = z;
        }

        public FieldValueVector3 Min(FieldValueVector3 other)
        {
            return new FieldValueVector3(0, 0, 0);
        }

        public FieldValueVector3 Max(FieldValueVector3 other)
        {
            return new FieldValueVector3(0, 0, 0);
        }

        public FieldValueVector3 Sub(FieldValueVector3 other)
        {
            var o = other;
            return new FieldValueVector3(_valueX - o._valueX, _valueY - o._valueY, _valueZ - o._valueZ);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            if (fieldInfo.Delta)
            {
                float scale = NetworkConfig.DecoderPrecisionScales[fieldInfo.Precision];
                return "(" + ((int) _valueX * scale) + ", " + ((int) _valueY * scale) + ", " +
                       ((int) _valueZ * scale) + ")";
            }

            return "(" + ConversionUtility.UInt32ToFloat(_valueX) + ", " +
                   ConversionUtility.UInt32ToFloat(_valueY) + ", " +
                   ConversionUtility.UInt32ToFloat(_valueZ) + ")";
        }
    }

    public struct FieldValueQuaternion : IFieldValue<FieldValueQuaternion>
    {
        private uint _valueX, _valueY, _valueZ, _valueW;

        public FieldValueQuaternion(uint x, uint y, uint z, uint w)
        {
            _valueX = x;
            _valueY = y;
            _valueZ = z;
            _valueW = w;
        }

        public FieldValueQuaternion Min(FieldValueQuaternion other)
        {
            return new FieldValueQuaternion(0, 0, 0, 0);
        }

        public FieldValueQuaternion Max(FieldValueQuaternion other)
        {
            return new FieldValueQuaternion(0, 0, 0, 0);
        }

        public FieldValueQuaternion Sub(FieldValueQuaternion other)
        {
            var o = other;
            return new FieldValueQuaternion(_valueX - o._valueX, _valueY - o._valueY, _valueZ - o._valueZ,
                _valueW - o._valueW);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            if (fieldInfo.Delta)
            {
                float scale = NetworkConfig.DecoderPrecisionScales[fieldInfo.Precision];
                return "(" + ((int) _valueX * scale) + ", " + ((int) _valueY * scale) + ", " +
                       ((int) _valueZ * scale) + ", " + ((int) _valueW * scale) + ")";
            }

            return "(" + ConversionUtility.UInt32ToFloat(_valueX) + ", " +
                   ConversionUtility.UInt32ToFloat(_valueY) + ", " +
                   ConversionUtility.UInt32ToFloat(_valueZ) + ", " +
                   ConversionUtility.UInt32ToFloat(_valueW) + ")";
        }
    }

    public struct FieldValueString : IFieldValue<FieldValueString>
    {
        public static readonly FieldValueString EmptyStringValue = new FieldValueString("");

        private string _value;
        private static char[] _charBuffer = new char[1024 * 32];

        public FieldValueString(string value)
        {
            _value = value;
        }

        public unsafe FieldValueString(byte* valueBuffer, int valueLength)
        {
            if (valueBuffer != null)
            {
                fixed (char* dest = _charBuffer)
                {
                    int numChars =
                        NetworkConfig.Encoding.GetChars(valueBuffer, valueLength, dest, _charBuffer.Length);
                    _value = new string(_charBuffer, 0, numChars);
                }
            }
            else
            {
                _value = "";
            }
        }

        public FieldValueString Min(FieldValueString other)
        {
            return EmptyStringValue;
        }

        public FieldValueString Max(FieldValueString other)
        {
            return EmptyStringValue;
        }

        public FieldValueString Sub(FieldValueString other)
        {
            return EmptyStringValue;
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            return _value;
        }
    }

    public struct FieldValueByteArray : IFieldValue<FieldValueByteArray>
    {
        public static readonly unsafe FieldValueByteArray EmptyByteArrayValue = new FieldValueByteArray(null, 0);

        private readonly byte[] _value;

        public unsafe FieldValueByteArray(byte* value, int valueLength)
        {
            if (value != null)
            {
                _value = new byte[valueLength];
                for (var i = 0; i < valueLength; i++)
                {
                    _value[i] = value[i];
                }
            }
            else
            {
                _value = null;
            }
        }

        public FieldValueByteArray Min(FieldValueByteArray other)
        {
            return EmptyByteArrayValue;
        }

        public FieldValueByteArray Max(FieldValueByteArray other)
        {
            return EmptyByteArrayValue;
        }

        public FieldValueByteArray Sub(FieldValueByteArray other)
        {
            return EmptyByteArrayValue;
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            return _value != null ? _value.ToString() : "";
        }
    }
}