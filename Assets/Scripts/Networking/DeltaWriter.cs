using Networking.Compression;

namespace Networking
{
    public struct DeltaWriter
    {
        private static readonly byte[] FieldsNotPredicted = new byte[(NetworkConfig.MAXFieldsPerSchema + 7) / 8];

        public static unsafe void Write<TOutputStream>(ref TOutputStream output, NetworkSchema schema, uint* inputData,
            uint* baselineData, byte[] fieldsChangedPrediction, byte fieldMask, ref uint hash)
            where TOutputStream : IOutputStream
        {
            GameDebug.Assert(baselineData != null);

            int numFields = schema.NumFields;
            GameDebug.Assert(fieldsChangedPrediction.Length >= numFields / 8,
                "Not enough bits in fieldsChangedPrediction for all fields");

            for (int i = 0; i < FieldsNotPredicted.Length; ++i)
            {
                FieldsNotPredicted[i] = 0;
            }

            int index = 0;

            // calculate bitmask of fields that need to be encoded
            for (int fieldIndex = 0; fieldIndex < numFields; ++fieldIndex)
            {
                var field = schema.Fields[fieldIndex];

                // Skip fields that are masked out
                bool masked = (field.FieldMask & fieldMask) != 0;

                byte fieldByteOffset = (byte) ((uint) fieldIndex >> 3);
                byte fieldBitOffset = (byte) ((uint) fieldIndex & 0x7); // 0x7 = 0111

                switch (field.FieldType)
                {
                    case FieldType.Bool:
                    {
                        uint value = inputData[index];
                        uint baseline = baselineData[index];
                        index++;

                        if (!masked)
                        {
                            hash = NetworkUtils.SimpleHashStreaming(hash, value);
                            if (value != baseline)
                            {
                                FieldsNotPredicted[fieldByteOffset] |= (byte) (1 << fieldBitOffset);
                            }
                        }

                        break;
                    }

                    case FieldType.Int:
                    {
                        uint value = inputData[index];
                        uint baseline = baselineData[index];
                        index++;

                        if (!masked)
                        {
                            hash = NetworkUtils.SimpleHashStreaming(hash, value);
                            if (value != baseline)
                            {
                                FieldsNotPredicted[fieldByteOffset] |= (byte) (1 << fieldBitOffset);
                            }
                        }

                        break;
                    }

                    case FieldType.UInt:
                    {
                        uint value = inputData[index];
                        uint baseline = baselineData[index];
                        index++;

                        if (!masked)
                        {
                            hash = NetworkUtils.SimpleHashStreaming(hash, value);
                            if (value != baseline)
                            {
                                FieldsNotPredicted[fieldByteOffset] |= (byte) (1 << fieldBitOffset);
                            }
                        }

                        break;
                    }

                    case FieldType.Float:
                    {
                        uint value = inputData[index];
                        uint baseline = baselineData[index];
                        index++;

                        if (!masked)
                        {
                            hash = NetworkUtils.SimpleHashStreaming(hash, value);
                            if (value != baseline)
                            {
                                FieldsNotPredicted[fieldByteOffset] |= (byte) (1 << fieldBitOffset);
                            }
                        }

                        break;
                    }

                    case FieldType.Vector2:
                    {
                        uint vx = inputData[index];
                        uint bx = baselineData[index];
                        index++;

                        uint vy = inputData[index];
                        uint by = baselineData[index];
                        index++;

                        if (!masked)
                        {
                            hash = NetworkUtils.SimpleHashStreaming(hash, vx);
                            hash = NetworkUtils.SimpleHashStreaming(hash, vy);
                            if (vx != bx || vy != by)
                            {
                                FieldsNotPredicted[fieldByteOffset] |= (byte) (1 << fieldBitOffset);
                            }
                        }

                        break;
                    }

                    case FieldType.Vector3:
                    {
                        uint vx = inputData[index];
                        uint bx = baselineData[index];
                        index++;

                        uint vy = inputData[index];
                        uint by = baselineData[index];
                        index++;

                        uint vz = inputData[index];
                        uint bz = baselineData[index];
                        index++;

                        if (!masked)
                        {
                            hash = NetworkUtils.SimpleHashStreaming(hash, vx);
                            hash = NetworkUtils.SimpleHashStreaming(hash, vy);
                            hash = NetworkUtils.SimpleHashStreaming(hash, vz);
                            if (vx != bx || vy != by || vz != bz)
                            {
                                FieldsNotPredicted[fieldByteOffset] |= (byte) (1 << fieldBitOffset);
                            }
                        }

                        break;
                    }

                    case FieldType.Quaternion:
                    {
                        uint vx = inputData[index];
                        uint bx = baselineData[index];
                        index++;

                        uint vy = inputData[index];
                        uint by = baselineData[index];
                        index++;

                        uint vz = inputData[index];
                        uint bz = baselineData[index];
                        index++;

                        uint vw = inputData[index];
                        uint bw = baselineData[index];
                        index++;


                        if (!masked)
                        {
                            hash = NetworkUtils.SimpleHashStreaming(hash, vx);
                            hash = NetworkUtils.SimpleHashStreaming(hash, vy);
                            hash = NetworkUtils.SimpleHashStreaming(hash, vz);
                            hash = NetworkUtils.SimpleHashStreaming(hash, vw);
                            if (vx != bx || vy != by || vz != bz || vw != bw)
                            {
                                FieldsNotPredicted[fieldByteOffset] |= (byte) (1 << fieldBitOffset);
                            }
                        }

                        break;
                    }

                    case FieldType.String:
                    case FieldType.ByteArray:
                    {
                        if (!masked)
                        {
                            // TODO client side has no easy way to hash strings. enable this when possible: NetworkUtils.SimpleHash(valueBuffer, valueLength);
                            hash += 0;
                            bool same = true;
                            for (int i = 0; i < field.ArraySize; i++)
                            {
                                if (inputData[index + i] != baselineData[index + i])
                                {
                                    same = false;
                                    break;
                                }
                            }

                            if (!same)
                            {
                                FieldsNotPredicted[fieldByteOffset] |= (byte) (1 << fieldBitOffset);
                            }
                        }

                        index += field.ArraySize / 4 + 1;

                        break;
                    }
                }
            }

            index = 0;

            int skipContext = schema.ID * NetworkConfig.MAXContextsPerSchema + NetworkConfig.FirstSchemaContext;

            // Client needs fieldsNotPredicted. We send the delta between it and fieldsChangedPrediction
            for (int i = 0; i * 8 < numFields; i++)
            {
                byte deltaFields = (byte) (FieldsNotPredicted[i] ^ fieldsChangedPrediction[i]);
                output.WritePackedNibble((uint) (deltaFields & 0xF), skipContext + i * 2);
                output.WritePackedNibble((uint) ((deltaFields >> 4) & 0xF), skipContext + i * 2 + 1);
            }

            int startBitPosition;
            for (int fieldIndex = 0; fieldIndex < numFields; ++fieldIndex)
            {
                var field = schema.Fields[fieldIndex];
                int fieldStartContext = field.StartContext;
                startBitPosition = output.GetBitPosition2();

                byte fieldByteOffset = (byte) ((uint) fieldIndex >> 3);
                byte fieldBitOffset = (byte) ((uint) fieldIndex & 0x7); // 0x7 = 0111
                var notPredicted = (FieldsNotPredicted[fieldByteOffset] & (1 << fieldBitOffset)) != 0;

                switch (field.FieldType)
                {
                    case FieldType.Bool:
                    {
                        uint value = inputData[index];
                        index++;

                        if (notPredicted)
                        {
                            output.WriteRawBits(value, 1);
                            NetworkSchema.AddStatsToFieldBool(field, (value != 0), false,
                                output.GetBitPosition2() - startBitPosition);
                        }

                        break;
                    }

                    case FieldType.Int:
                    {
                        uint value = inputData[index];
                        uint baseline = baselineData[index];
                        index++;

                        if (notPredicted)
                        {
                            if (field.Delta)
                            {
                                output.WritePackedUIntDelta(value, baseline, fieldStartContext);
                                NetworkSchema.AddStatsToFieldInt(field, (int) value, (int) baseline,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                            else
                            {
                                output.WriteRawBits(value, field.Bits);
                                NetworkSchema.AddStatsToFieldInt(field, (int) value, 0,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                        }

                        break;
                    }

                    case FieldType.UInt:
                    {
                        uint value = inputData[index];
                        uint baseline = baselineData[index];
                        index++;

                        if (notPredicted)
                        {
                            if (field.Delta)
                            {
                                output.WritePackedUIntDelta(value, baseline, fieldStartContext);
                                NetworkSchema.AddStatsToFieldUInt(field, value, baseline,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                            else
                            {
                                output.WriteRawBits(value, field.Bits);
                                NetworkSchema.AddStatsToFieldUInt(field, value, 0,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                        }

                        break;
                    }

                    case FieldType.Float:
                    {
                        uint value = inputData[index];
                        uint baseline = baselineData[index];
                        index++;

                        if (notPredicted)
                        {
                            if (field.Delta)
                            {
                                output.WritePackedUIntDelta(value, baseline, fieldStartContext);
                                NetworkSchema.AddStatsToFieldFloat(field, value, baseline,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                            else
                            {
                                output.WriteRawBits(value, field.Bits);
                                NetworkSchema.AddStatsToFieldFloat(field, value, 0,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                        }

                        break;
                    }

                    case FieldType.Vector2:
                    {
                        uint vx = inputData[index];
                        uint bx = baselineData[index];
                        index++;

                        uint vy = inputData[index];
                        uint by = baselineData[index];
                        index++;

                        if (notPredicted)
                        {
                            if (field.Delta)
                            {
                                output.WritePackedUIntDelta(vx, bx, fieldStartContext + 0);
                                output.WritePackedUIntDelta(vy, by, fieldStartContext + 1);
                                NetworkSchema.AddStatsToFieldVector2(field, vx, vy, bx, by,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                            else
                            {
                                output.WriteRawBits(vx, field.Bits);
                                output.WriteRawBits(vy, field.Bits);
                                NetworkSchema.AddStatsToFieldVector2(field, vx, vy, 0, 0,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                        }

                        break;
                    }

                    case FieldType.Vector3:
                    {
                        uint vx = inputData[index];
                        uint bx = baselineData[index];
                        index++;

                        uint vy = inputData[index];
                        uint by = baselineData[index];
                        index++;

                        uint vz = inputData[index];
                        uint bz = baselineData[index];
                        index++;

                        if (notPredicted)
                        {
                            if (field.Delta)
                            {
                                output.WritePackedUIntDelta(vx, bx, fieldStartContext + 0);
                                output.WritePackedUIntDelta(vy, by, fieldStartContext + 1);
                                output.WritePackedUIntDelta(vz, bz, fieldStartContext + 2);
                                NetworkSchema.AddStatsToFieldVector3(field, vx, vy, vz, bx, by, bz,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                            else
                            {
                                output.WriteRawBits(vx, field.Bits);
                                output.WriteRawBits(vy, field.Bits);
                                output.WriteRawBits(vz, field.Bits);
                                NetworkSchema.AddStatsToFieldVector3(field, vx, vy, vz, 0, 0, 0,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                        }

                        break;
                    }


                    case FieldType.Quaternion:
                    {
                        // TODO : Figure out what to do with quaternions
                        uint vx = inputData[index];
                        uint bx = baselineData[index];
                        index++;

                        uint vy = inputData[index];
                        uint by = baselineData[index];
                        index++;

                        uint vz = inputData[index];
                        uint bz = baselineData[index];
                        index++;

                        uint vw = inputData[index];
                        uint bw = baselineData[index];
                        index++;

                        if (notPredicted)
                        {
                            if (field.Delta)
                            {
                                output.WritePackedUIntDelta(vx, bx, fieldStartContext + 0);
                                output.WritePackedUIntDelta(vy, by, fieldStartContext + 1);
                                output.WritePackedUIntDelta(vz, bz, fieldStartContext + 2);
                                output.WritePackedUIntDelta(vw, bw, fieldStartContext + 3);
                                NetworkSchema.AddStatsToFieldQuaternion(field, vx, vy, vz, vw, bx, by, bz, bw,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                            else
                            {
                                output.WriteRawBits(vx, field.Bits);
                                output.WriteRawBits(vy, field.Bits);
                                output.WriteRawBits(vz, field.Bits);
                                output.WriteRawBits(vw, field.Bits);
                                NetworkSchema.AddStatsToFieldQuaternion(field, vx, vy, vz, vw, 0, 0, 0, 0,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                        }

                        break;
                    }

                    case FieldType.String:
                    case FieldType.ByteArray:
                    {
                        uint valueLength = inputData[index];
                        index++;

                        if (notPredicted)
                        {
                            output.WritePackedUInt(valueLength, fieldStartContext);
                            byte* bytes = (byte*) (inputData + index);
                            output.WriteRawBytes(bytes, (int) valueLength);

                            if (field.FieldType == FieldType.String)
                            {
                                NetworkSchema.AddStatsToFieldString(field, bytes, (int) valueLength,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                            else
                            {
                                NetworkSchema.AddStatsToFieldByteArray(field, bytes, (int) valueLength,
                                    output.GetBitPosition2() - startBitPosition);
                            }
                        }

                        index += field.ArraySize / 4;

                        break;
                    }
                }
            }
        }
    }
}