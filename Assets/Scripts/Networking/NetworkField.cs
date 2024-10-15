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
            return m_NumWrites;
        }

        public int GetNumBitsWritten()
        {
            return m_NumBitsWritten;
        }

        protected int m_NumWrites;
        protected int m_NumBitsWritten;
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
        public T value;
        public T valueMin;
        public T valueMax;
        public T prediction;
        public T predictionMin;
        public T predictionMax;

        public T delta;
        public T deltaMin;
        public T deltaMax;

        FieldInfo m_FieldInfo;

        public FieldStats(FieldInfo fieldInfo)
        {
            m_FieldInfo = fieldInfo;
        }

        public void Add(T value, T prediction, int bitsWritten)
        {
            this.value = value;
            this.prediction = prediction;
            this.delta = (T) value.Sub(prediction);

            if (m_NumWrites > 0)
            {
                valueMin = (T) valueMin.Min(value);
                valueMax = (T) valueMin.Max(value);
                predictionMin = (T) predictionMin.Min(prediction);
                predictionMax = (T) predictionMax.Max(prediction);
                deltaMin = (T) deltaMin.Min(delta);
                deltaMax = (T) deltaMax.Max(delta);
            }
            else
            {
                valueMin = value;
                valueMax = value;
                predictionMin = prediction;
                predictionMax = prediction;
            }

            this.m_NumBitsWritten += bitsWritten;
            this.m_NumWrites++;
        }

        public override string GetValue(bool showRaw)
        {
            return ((T) value).ToString(m_FieldInfo, showRaw);
        }

        public override string GetValueMin(bool showRaw)
        {
            return ((T) valueMin).ToString(m_FieldInfo, showRaw);
        }

        public override string GetValueMax(bool showRaw)
        {
            return ((T) valueMax).ToString(m_FieldInfo, showRaw);
        }

        public override string GetPrediction(bool showRaw)
        {
            return ((T) prediction).ToString(m_FieldInfo, showRaw);
        }

        public override string GetPredictionMin(bool showRaw)
        {
            return ((T) predictionMin).ToString(m_FieldInfo, showRaw);
        }

        public override string GetPredictionMax(bool showRaw)
        {
            return ((T) predictionMax).ToString(m_FieldInfo, showRaw);
        }

        public override string GetDelta(bool showRaw)
        {
            return ((T) delta).ToString(m_FieldInfo, showRaw);
        }

        public override string GetDeltaMin(bool showRaw)
        {
            return ((T) deltaMin).ToString(m_FieldInfo, showRaw);
        }

        public override string GetDeltaMax(bool showRaw)
        {
            return ((T) deltaMax).ToString(m_FieldInfo, showRaw);
        }
    }


    public struct FieldValueBool : IFieldValue<FieldValueBool>
    {
        public FieldValueBool(bool value)
        {
            m_Value = value;
        }

        public FieldValueBool Min(FieldValueBool other)
        {
            bool otherValue = ((FieldValueBool) other).m_Value;
            return new FieldValueBool(m_Value && otherValue);
        }

        public FieldValueBool Max(FieldValueBool other)
        {
            bool otherValue = ((FieldValueBool) other).m_Value;
            return new FieldValueBool(m_Value || otherValue);
        }

        public FieldValueBool Sub(FieldValueBool other)
        {
            bool otherValue = ((FieldValueBool) other).m_Value;
            return new FieldValueBool(m_Value != otherValue);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            return "" + m_Value;
        }

        bool m_Value;
    }

    public struct FieldValueInt : IFieldValue<FieldValueInt>
    {
        public FieldValueInt(int value)
        {
            m_Value = value;
        }

        public FieldValueInt Min(FieldValueInt other)
        {
            int otherValue = ((FieldValueInt) other).m_Value;
            return new FieldValueInt(m_Value < otherValue ? m_Value : otherValue);
        }

        public FieldValueInt Max(FieldValueInt other)
        {
            int otherValue = ((FieldValueInt) other).m_Value;
            return new FieldValueInt(m_Value > otherValue ? m_Value : otherValue);
        }

        public FieldValueInt Sub(FieldValueInt other)
        {
            int otherValue = ((FieldValueInt) other).m_Value;
            return new FieldValueInt(m_Value - otherValue);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            return "" + m_Value;
        }

        public int m_Value;
    }

    public struct FieldValueUInt : IFieldValue<FieldValueUInt>
    {
        public FieldValueUInt(uint value)
        {
            m_Value = value;
        }

        public FieldValueUInt Min(FieldValueUInt other)
        {
            uint otherValue = ((FieldValueUInt) other).m_Value;
            return new FieldValueUInt(m_Value < otherValue ? m_Value : otherValue);
        }

        public FieldValueUInt Max(FieldValueUInt other)
        {
            uint otherValue = ((FieldValueUInt) other).m_Value;
            return new FieldValueUInt(m_Value > otherValue ? m_Value : otherValue);
        }

        public FieldValueUInt Sub(FieldValueUInt other)
        {
            uint otherValue = ((FieldValueUInt) other).m_Value;
            return new FieldValueUInt(m_Value - otherValue);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            return "" + m_Value;
        }

        public uint m_Value;
    }

    public struct FieldValueFloat : IFieldValue<FieldValueFloat>
    {
        public FieldValueFloat(uint value)
        {
            m_Value = value;
        }

        public FieldValueFloat Min(FieldValueFloat other)
        {
            uint otherValue = ((FieldValueFloat) other).m_Value;
            return new FieldValueFloat(m_Value < otherValue ? m_Value : otherValue);
        }

        public FieldValueFloat Max(FieldValueFloat other)
        {
            uint otherValue = ((FieldValueFloat) other).m_Value;
            return new FieldValueFloat(m_Value > otherValue ? m_Value : otherValue);
        }

        public FieldValueFloat Sub(FieldValueFloat other)
        {
            uint otherValue = ((FieldValueFloat) other).m_Value;
            return new FieldValueFloat(m_Value - otherValue);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            if (showRaw)
            {
                return "" + m_Value;
            }
            else
            {
                if (fieldInfo.Delta)
                    return "" + ((int) m_Value * NetworkConfig.decoderPrecisionScales[fieldInfo.Precision]);
                else
                    return "" + ConversionUtility.UInt32ToFloat(m_Value);
            }
        }

        public uint m_Value;
    }

    public struct FieldValueVector2 : IFieldValue<FieldValueVector2>
    {
        public FieldValueVector2(uint x, uint y)
        {
            m_ValueX = x;
            m_ValueY = y;
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
            var o = (FieldValueVector2) other;
            return new FieldValueVector2(m_ValueX - o.m_ValueX, m_ValueX - o.m_ValueY);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            if (fieldInfo.Delta)
            {
                float scale = NetworkConfig.decoderPrecisionScales[fieldInfo.Precision];
                return "(" + ((int) m_ValueX * scale) + ", " + ((int) m_ValueY * scale) + ")";
            }

            else
                return "(" + ConversionUtility.UInt32ToFloat(m_ValueX) + ", " +
                       ConversionUtility.UInt32ToFloat(m_ValueY) + ")";
        }

        uint m_ValueX, m_ValueY;
    }

    public struct FieldValueVector3 : IFieldValue<FieldValueVector3>
    {
        public FieldValueVector3(uint x, uint y, uint z)
        {
            m_ValueX = x;
            m_ValueY = y;
            m_ValueZ = z;
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
            var o = (FieldValueVector3) other;
            return new FieldValueVector3(m_ValueX - o.m_ValueX, m_ValueY - o.m_ValueY, m_ValueZ - o.m_ValueZ);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            if (fieldInfo.Delta)
            {
                float scale = NetworkConfig.decoderPrecisionScales[fieldInfo.Precision];
                return "(" + ((int) m_ValueX * scale) + ", " + ((int) m_ValueY * scale) + ", " +
                       ((int) m_ValueZ * scale) + ")";
            }

            else
                return "(" + ConversionUtility.UInt32ToFloat(m_ValueX) + ", " +
                       ConversionUtility.UInt32ToFloat(m_ValueY) + ", " +
                       ConversionUtility.UInt32ToFloat(m_ValueZ) + ")";
        }

        public uint m_ValueX, m_ValueY, m_ValueZ;
    }

    public struct FieldValueQuaternion : IFieldValue<FieldValueQuaternion>
    {
        public FieldValueQuaternion(uint x, uint y, uint z, uint w)
        {
            m_ValueX = x;
            m_ValueY = y;
            m_ValueZ = z;
            m_ValueW = w;
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
            var o = (FieldValueQuaternion) other;
            return new FieldValueQuaternion(m_ValueX - o.m_ValueX, m_ValueY - o.m_ValueY, m_ValueZ - o.m_ValueZ,
                m_ValueW - o.m_ValueW);
        }

        public string ToString(FieldInfo fieldInfo, bool showRaw)
        {
            if (fieldInfo.Delta)
            {
                float scale = NetworkConfig.decoderPrecisionScales[fieldInfo.Precision];
                return "(" + ((int) m_ValueX * scale) + ", " + ((int) m_ValueY * scale) + ", " +
                       ((int) m_ValueZ * scale) + ", " + ((int) m_ValueW * scale) + ")";
            }

            else
                return "(" + ConversionUtility.UInt32ToFloat(m_ValueX) + ", " +
                       ConversionUtility.UInt32ToFloat(m_ValueY) + ", " +
                       ConversionUtility.UInt32ToFloat(m_ValueZ) + ", " +
                       ConversionUtility.UInt32ToFloat(m_ValueW) + ")";
        }

        uint m_ValueX, m_ValueY, m_ValueZ, m_ValueW;
    }

    public struct FieldValueString : IFieldValue<FieldValueString>
    {
        public FieldValueString(string value)
        {
            m_Value = value;
        }

        unsafe public FieldValueString(byte* valueBuffer, int valueLength)
        {
            if (valueBuffer != null)
            {
                fixed (char* dest = s_CharBuffer)
                {
                    int numChars =
                        NetworkConfig.encoding.GetChars(valueBuffer, valueLength, dest, s_CharBuffer.Length);
                    m_Value = new string(s_CharBuffer, 0, numChars);
                }
            }
            else
            {
                m_Value = "";
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
            return m_Value;
        }


        public readonly static FieldValueString EmptyStringValue = new FieldValueString("");

        string m_Value;
        static char[] s_CharBuffer = new char[1024 * 32];
    }

    public struct FieldValueByteArray : IFieldValue<FieldValueByteArray>
    {
        unsafe public FieldValueByteArray(byte* value, int valueLength)
        {
            if (value != null)
            {
                m_Value = new byte[valueLength];
                for (int i = 0; i < valueLength; i++)
                    m_Value[i] = value[i];
            }
            else
            {
                m_Value = null;
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
            return "";
        }


        unsafe public readonly static FieldValueByteArray EmptyByteArrayValue = new FieldValueByteArray(null, 0);

        byte[] m_Value;
    }
}