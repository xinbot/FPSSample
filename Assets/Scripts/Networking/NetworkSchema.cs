using System.Collections.Generic;
using System.Diagnostics;

namespace Networking
{
    public unsafe class NetworkSchema
    {
        public uint[] predictPlan;
        public FieldInfo[] fields;
        public int numFields;
        public int id;

        private int nextFieldOffset = 0;
        private List<FieldInfo> fieldsInternal = new List<FieldInfo>();

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
        public static unsafe void AddStatsToFieldString(FieldInfo fieldInfo, byte* value, int valueLength, int numBits)
        {
            ((FieldStats<FieldValueString>) fieldInfo.Stats).Add(
                new FieldValueString(value, valueLength), FieldValueString.EmptyStringValue,
                numBits);
        }

        [Conditional("UNITY_EDITOR")]
        public static unsafe void AddStatsToFieldByteArray(FieldInfo fieldInfo, byte* value, int valueLength,
            int numBits)
        {
            ((FieldStats<FieldValueByteArray>) fieldInfo.Stats).Add(
                new FieldValueByteArray(value, valueLength),
                FieldValueByteArray.EmptyByteArrayValue, numBits);
        }

        // 0bAAAAAAAABBBBBBBBCCCCCCCC0000MMDA   ABC: length of array, MM: mask, D: delta, A: array
        public void Finalize()
        {
            GameDebug.Assert(predictPlan == null);

            predictPlan = new uint[fieldsInternal.Count];
            for (int i = 0, c = fieldsInternal.Count; i < c; ++i)
            {
                var f = fieldsInternal[i];
                uint arraycount = 0;
                uint mask = f.FieldMask;
                uint flags = (ushort) (
                    (fieldsInternal[i].Delta ? 2 : 0) |
                    (f.FieldType == FieldType.String || f.FieldType == FieldType.ByteArray ? 1 : 0));
                switch (f.FieldType)
                {
                    case FieldType.Bool:
                    case FieldType.Int:
                    case FieldType.UInt:
                    case FieldType.Float:
                        arraycount = 1;
                        break;
                    case FieldType.Vector2:
                        arraycount = 2;
                        break;
                    case FieldType.Vector3:
                        arraycount = 3;
                        break;
                    case FieldType.Quaternion:
                        arraycount = 4;
                        break;
                    case FieldType.String:
                    case FieldType.ByteArray:
                        arraycount = (ushort) (f.ArraySize / 4 + 1);
                        break;
                }

                predictPlan[i] = (uint) (arraycount << 8) | (uint) (mask << 2) | (uint) flags;
            }

            numFields = fieldsInternal.Count;
            fields = fieldsInternal.ToArray();
        }

        public NetworkSchema(int id)
        {
            GameDebug.Assert(id >= 0 && id < NetworkConfig.maxSchemaIds);
            this.id = id;
        }

        // TODO (peter) Should this be in words?
        public int GetByteSize()
        {
            return nextFieldOffset;
        }

        public void AddField(FieldInfo field)
        {
            GameDebug.Assert(fieldsInternal.Count < NetworkConfig.maxFieldsPerSchema);
            field.ByteOffset = nextFieldOffset;
            field.Stats = FieldStatsBase.CreateFieldStats(field);
            fieldsInternal.Add(field);
            nextFieldOffset += CalculateFieldByteSize(field);
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
            int count = (int) input.ReadPackedUInt(NetworkConfig.miscContext);
            int id = (int) input.ReadPackedUInt(NetworkConfig.miscContext);
            var schema = new NetworkSchema(id);
            for (int i = 0; i < count; ++i)
            {
                var field = new FieldInfo();
                field.FieldType = (FieldType) input.ReadPackedNibble(NetworkConfig.miscContext);
                field.Delta = input.ReadRawBits(1) != 0;
                field.Bits = (int) input.ReadPackedUInt(NetworkConfig.miscContext);
                field.Precision = (int) input.ReadPackedUInt(NetworkConfig.miscContext);
                field.ArraySize = (int) input.ReadPackedUInt(NetworkConfig.miscContext);
                field.StartContext = schema.fieldsInternal.Count * NetworkConfig.maxContextsPerField +
                                     schema.id * NetworkConfig.maxContextsPerSchema + NetworkConfig.firstSchemaContext;
                field.FieldMask = (byte) input.ReadPackedUInt(NetworkConfig.miscContext);
                schema.AddField(field);
            }

            schema.Finalize();
            return schema;
        }

        public static void WriteSchema<TOutputStream>(NetworkSchema schema, ref TOutputStream output)
            where TOutputStream : NetworkCompression.IOutputStream
        {
            output.WritePackedUInt((uint) schema.fieldsInternal.Count, NetworkConfig.miscContext);
            output.WritePackedUInt((uint) schema.id, NetworkConfig.miscContext);
            for (int i = 0; i < schema.numFields; ++i)
            {
                var field = schema.fields[i];
                output.WritePackedNibble((uint) field.FieldType, NetworkConfig.miscContext);
                output.WriteRawBits(field.Delta ? 1U : 0, 1);
                output.WritePackedUInt((uint) field.Bits, NetworkConfig.miscContext);
                output.WritePackedUInt((uint) field.Precision, NetworkConfig.miscContext);
                output.WritePackedUInt((uint) field.ArraySize, NetworkConfig.miscContext);
                output.WritePackedUInt((uint) field.FieldMask, NetworkConfig.miscContext);
            }
        }

        unsafe public static void CopyFieldsFromBuffer<TOutputStream>(NetworkSchema schema, uint* inputBuffer,
            ref TOutputStream output) where TOutputStream : NetworkCompression.IOutputStream
        {
            int index = 0;

            int fieldIndex = 0;
            for (; fieldIndex < schema.fieldsInternal.Count; ++fieldIndex)
            {
                var field = schema.fieldsInternal[fieldIndex];
                switch (field.FieldType)
                {
                    case FieldType.Bool:
                    case FieldType.UInt:
                    case FieldType.Int:
                    case FieldType.Float:
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        break;

                    case FieldType.Vector2:
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        break;

                    case FieldType.Vector3:
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        break;

                    case FieldType.Quaternion:
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        output.WritePackedUInt(inputBuffer[index++], NetworkConfig.miscContext);
                        break;

                    case FieldType.String:
                    case FieldType.ByteArray:
                    {
                        uint dataSize = inputBuffer[index++];

                        output.WritePackedUInt(dataSize, field.StartContext);
                        output.WriteRawBytes((byte*) (inputBuffer + index), (int) dataSize);
                        index += field.ArraySize / 4;
                    }
                        break;

                    default:
                        GameDebug.Assert(false);
                        break;
                }
            }
        }

        unsafe public static void CopyFieldsToBuffer<TInputStream>(NetworkSchema schema, ref TInputStream input,
            uint[] outputBuffer) where TInputStream : NetworkCompression.IInputStream
        {
            var index = 0;
            for (var fieldIndex = 0; fieldIndex < schema.fieldsInternal.Count; ++fieldIndex)
            {
                var field = schema.fieldsInternal[fieldIndex];
                switch (field.FieldType)
                {
                    case FieldType.Bool:
                    case FieldType.UInt:
                    case FieldType.Int:
                    case FieldType.Float:
                        outputBuffer[index++] = input.ReadPackedUInt(NetworkConfig.miscContext);
                        break;

                    case FieldType.Vector2:
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        break;

                    case FieldType.Vector3:
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        break;

                    case FieldType.Quaternion:
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        outputBuffer[index++] = (input.ReadPackedUInt(NetworkConfig.miscContext));
                        break;

                    case FieldType.String:
                    case FieldType.ByteArray:
                        var dataSize = input.ReadPackedUInt(NetworkConfig.miscContext);
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
            for (var fieldIndex = 0; fieldIndex < schema.fieldsInternal.Count; ++fieldIndex)
            {
                var field = schema.fieldsInternal[fieldIndex];
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