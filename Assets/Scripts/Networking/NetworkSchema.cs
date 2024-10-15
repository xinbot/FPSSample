using System.Collections.Generic;
using System.Diagnostics;

namespace Networking
{
    public unsafe class NetworkSchema
    {
        public uint[] PredictPlan;
        public FieldInfo[] Fields;
        public int NumFields;
        public int ID;

        private int _nextFieldOffset;
        private readonly List<FieldInfo> _fieldsInternal = new List<FieldInfo>();

        // Functions for updating stats on a field that can be conditionally excluded from the build
        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldBool(FieldInfo fieldInfo, bool value, bool prediction, int numBits)
        {
            ((FieldStats<FieldValueBool>) fieldInfo.Stats).Add(
                new FieldValueBool(value), new FieldValueBool(prediction), numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldInt(FieldInfo fieldInfo, int value, int prediction, int numBits)
        {
            ((FieldStats<FieldValueInt>) fieldInfo.Stats).Add(
                new FieldValueInt(value), new FieldValueInt(prediction), numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldUInt(FieldInfo fieldInfo, uint value, uint prediction, int numBits)
        {
            ((FieldStats<FieldValueUInt>) fieldInfo.Stats).Add(
                new FieldValueUInt(value), new FieldValueUInt(prediction), numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldFloat(FieldInfo fieldInfo, uint value, uint prediction, int numBits)
        {
            ((FieldStats<FieldValueFloat>) fieldInfo.Stats).Add(
                new FieldValueFloat(value), new FieldValueFloat(prediction), numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldVector2(FieldInfo fieldInfo, uint vx, uint vy, uint px, uint py, int numBits)
        {
            ((FieldStats<FieldValueVector2>) fieldInfo.Stats).Add(
                new FieldValueVector2(vx, vy), new FieldValueVector2(px, py), numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldVector3(FieldInfo fieldInfo, uint vx, uint vy, uint vz, uint px, uint py,
            uint pz, int numBits)
        {
            ((FieldStats<FieldValueVector3>) fieldInfo.Stats).Add(
                new FieldValueVector3(vx, vy, vz), new FieldValueVector3(px, py, pz),
                numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldQuaternion(FieldInfo fieldInfo, uint vx, uint vy, uint vz, uint vw, uint px,
            uint py, uint pz, uint pw, int numBits)
        {
            ((FieldStats<FieldValueQuaternion>) fieldInfo.Stats).Add(
                new FieldValueQuaternion(vx, vy, vz, vw),
                new FieldValueQuaternion(px, py, pz, pw), numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldString(FieldInfo fieldInfo, byte* value, int valueLength, int numBits)
        {
            ((FieldStats<FieldValueString>) fieldInfo.Stats).Add(
                new FieldValueString(value, valueLength), FieldValueString.EmptyStringValue,
                numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static void AddStatsToFieldByteArray(FieldInfo fieldInfo, byte* value, int valueLength,
            int numBits)
        {
            ((FieldStats<FieldValueByteArray>) fieldInfo.Stats).Add(
                new FieldValueByteArray(value, valueLength), FieldValueByteArray.EmptyByteArrayValue,
                numBits);
        }

        // 0bAAAAAAAABBBBBBBBCCCCCCCC0000MMDA
        // ABC: length of array, MM: mask, D: delta, A: array
        public void ResetPredictPlan()
        {
            GameDebug.Assert(PredictPlan == null);

            PredictPlan = new uint[_fieldsInternal.Count];
            for (var i = 0; i < _fieldsInternal.Count; ++i)
            {
                var f = _fieldsInternal[i];
                uint arrayCount = 0;
                uint mask = f.FieldMask;
                uint flags = (ushort) (
                    (_fieldsInternal[i].Delta ? 2 : 0) |
                    (f.FieldType == FieldType.String || f.FieldType == FieldType.ByteArray ? 1 : 0));
                switch (f.FieldType)
                {
                    case FieldType.Bool:
                    case FieldType.Int:
                    case FieldType.UInt:
                    case FieldType.Float:
                        arrayCount = 1;
                        break;
                    case FieldType.Vector2:
                        arrayCount = 2;
                        break;
                    case FieldType.Vector3:
                        arrayCount = 3;
                        break;
                    case FieldType.Quaternion:
                        arrayCount = 4;
                        break;
                    case FieldType.String:
                    case FieldType.ByteArray:
                        arrayCount = (ushort) (f.ArraySize / 4 + 1);
                        break;
                }

                PredictPlan[i] = (arrayCount << 8) | (mask << 2) | flags;
            }

            NumFields = _fieldsInternal.Count;
            Fields = _fieldsInternal.ToArray();
        }

        public NetworkSchema(int id)
        {
            GameDebug.Assert(id >= 0 && id < NetworkConfig.MAXSchemaIds);
            ID = id;
        }

        // TODO (peter) Should this be in words?
        public int GetByteSize()
        {
            return _nextFieldOffset;
        }

        public void AddField(FieldInfo field)
        {
            GameDebug.Assert(_fieldsInternal.Count < NetworkConfig.MAXFieldsPerSchema);
            field.ByteOffset = _nextFieldOffset;
            field.Stats = FieldStatsBase.CreateFieldStats(field);
            _fieldsInternal.Add(field);
            _nextFieldOffset += CalculateFieldByteSize(field);
        }

        public static int CalculateFieldByteSize(FieldInfo field)
        {
            int size = 0;
            switch (field.FieldType)
            {
                case FieldType.Bool:
                    size = 4;
                    break;
                case FieldType.Int:
                case FieldType.UInt:
                case FieldType.Float:
                    size = 4; // (field.bits + 7) / 8;
                    break;
                case FieldType.Vector2:
                    size = 8; // (field.bits + 7) / 8 * 2;
                    break;
                case FieldType.Vector3:
                    size = 12; // (field.bits + 7) / 8 * 3;
                    break;
                case FieldType.Quaternion:
                    size = 16; //(field.bits + 7) / 8 * 4;
                    break;
                case FieldType.String:
                case FieldType.ByteArray:
                    size = 4 + field.ArraySize;
                    break;
                default:
                    GameDebug.Assert(false);
                    break;
            }

            return size;
        }

        public static NetworkSchema ReadSchema<TInputStream>(ref TInputStream input)
            where TInputStream : NetworkCompression.IInputStream
        {
            int count = (int) input.ReadPackedUInt(NetworkConfig.MiscContext);
            int id = (int) input.ReadPackedUInt(NetworkConfig.MiscContext);
            var schema = new NetworkSchema(id);
            for (int i = 0; i < count; ++i)
            {
                var field = new FieldInfo();
                field.FieldType = (FieldType) input.ReadPackedNibble(NetworkConfig.MiscContext);
                field.Delta = input.ReadRawBits(1) != 0;
                field.Bits = (int) input.ReadPackedUInt(NetworkConfig.MiscContext);
                field.Precision = (int) input.ReadPackedUInt(NetworkConfig.MiscContext);
                field.ArraySize = (int) input.ReadPackedUInt(NetworkConfig.MiscContext);
                field.StartContext = schema._fieldsInternal.Count * NetworkConfig.MAXContextsPerField +
                                     schema.ID * NetworkConfig.MAXContextsPerSchema + NetworkConfig.FirstSchemaContext;
                field.FieldMask = (byte) input.ReadPackedUInt(NetworkConfig.MiscContext);
                schema.AddField(field);
            }

            schema.ResetPredictPlan();
            return schema;
        }

        public static void WriteSchema<TOutputStream>(NetworkSchema schema, ref TOutputStream output)
            where TOutputStream : NetworkCompression.IOutputStream
        {
            output.WritePackedUInt((uint) schema._fieldsInternal.Count, NetworkConfig.MiscContext);
            output.WritePackedUInt((uint) schema.ID, NetworkConfig.MiscContext);
            for (int i = 0; i < schema.NumFields; ++i)
            {
                var field = schema.Fields[i];
                output.WritePackedNibble((uint) field.FieldType, NetworkConfig.MiscContext);
                output.WriteRawBits(field.Delta ? 1U : 0, 1);
                output.WritePackedUInt((uint) field.Bits, NetworkConfig.MiscContext);
                output.WritePackedUInt((uint) field.Precision, NetworkConfig.MiscContext);
                output.WritePackedUInt((uint) field.ArraySize, NetworkConfig.MiscContext);
                output.WritePackedUInt(field.FieldMask, NetworkConfig.MiscContext);
            }
        }

        public static void CopyFieldsFromBuffer<TOutputStream>(NetworkSchema schema, uint* inputBuffer,
            ref TOutputStream output) where TOutputStream : NetworkCompression.IOutputStream
        {
            int index = 0;
            int fieldIndex = 0;
            for (; fieldIndex < schema._fieldsInternal.Count; ++fieldIndex)
            {
                var field = schema._fieldsInternal[fieldIndex];
                switch (field.FieldType)
                {
                    case FieldType.Bool:
                    case FieldType.UInt:
                    case FieldType.Int:
                    case FieldType.Float:
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        break;

                    case FieldType.Vector2:
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        break;

                    case FieldType.Vector3:
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        break;

                    case FieldType.Quaternion:
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.MiscContext);
                        break;

                    case FieldType.String:
                    case FieldType.ByteArray:
                        uint dataSize = inputBuffer[index++];
                        output.WritePackedUInt(dataSize, field.StartContext);
                        output.WriteRawBytes((byte*) (inputBuffer + index), (int) dataSize);
                        index += field.ArraySize / 4;
                        break;

                    default:
                        GameDebug.Assert(false);
                        break;
                }
            }
        }

        public static void CopyFieldsToBuffer<TInputStream>(NetworkSchema schema, ref TInputStream input,
            uint[] outputBuffer) where TInputStream : NetworkCompression.IInputStream
        {
            var index = 0;
            for (var fieldIndex = 0; fieldIndex < schema._fieldsInternal.Count; ++fieldIndex)
            {
                var field = schema._fieldsInternal[fieldIndex];
                switch (field.FieldType)
                {
                    case FieldType.Bool:
                    case FieldType.UInt:
                    case FieldType.Int:
                    case FieldType.Float:
                        outputBuffer[index++] = input.ReadPackedUInt(NetworkConfig.MiscContext);
                        break;

                    case FieldType.Vector2:
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        break;

                    case FieldType.Vector3:
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        break;

                    case FieldType.Quaternion:
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.MiscContext));
                        break;

                    case FieldType.String:
                    case FieldType.ByteArray:
                        var dataSize = input.ReadPackedUInt(NetworkConfig.MiscContext);
                        outputBuffer[index++] = dataSize;

                        fixed (uint* buf = outputBuffer)
                        {
                            byte* dst = (byte*) (buf + index);
                            int i = 0;
                            for (; i < dataSize; i++)
                                *dst++ = (byte) input.ReadRawBits(8);
                            for (; i < field.ArraySize; i++)
                                *dst++ = 0;
                        }

                        index += field.ArraySize / 4;
                        break;

                    default:
                        GameDebug.Assert(false);
                        break;
                }
            }
        }

        public static void SkipFields<TInputStream>(NetworkSchema schema, ref TInputStream input)
            where TInputStream : NetworkCompression.IInputStream
        {
            for (var fieldIndex = 0; fieldIndex < schema._fieldsInternal.Count; ++fieldIndex)
            {
                var field = schema._fieldsInternal[fieldIndex];
                switch (field.FieldType)
                {
                    case FieldType.Bool:
                    case FieldType.UInt:
                    case FieldType.Int:
                    case FieldType.Float:
                        input.ReadRawBits(field.Bits);
                        break;

                    case FieldType.Vector2:
                        input.ReadRawBits(field.Bits);
                        input.ReadRawBits(field.Bits);
                        break;

                    case FieldType.Vector3:
                        input.ReadRawBits(field.Bits);
                        input.ReadRawBits(field.Bits);
                        input.ReadRawBits(field.Bits);
                        break;

                    case FieldType.Quaternion:
                        input.ReadRawBits(field.Bits);
                        input.ReadRawBits(field.Bits);
                        input.ReadRawBits(field.Bits);
                        input.ReadRawBits(field.Bits);
                        break;

                    case FieldType.String:
                    case FieldType.ByteArray:
                        input.SkipRawBytes((int) input.ReadPackedUInt(field.StartContext));
                        break;

                    default:
                        GameDebug.Assert(false);
                        break;
                }
            }
        }
    }
}