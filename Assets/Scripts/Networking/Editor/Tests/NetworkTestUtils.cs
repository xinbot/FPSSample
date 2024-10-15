using System;
using System.Collections.Generic;
using Networking;
using NUnit.Framework;
using UnityEngine;

namespace NetcodeTests
{
    public class NetworkTestUtils
    {
        public static NetworkSchema GenerateRandomSchema(int length, int seed)
        {
            var random = new System.Random(seed);
            var values = Enum.GetValues(typeof(FieldType));

            var schema = new NetworkSchema(0);

            for (int i = 0; i < length; ++i)
            {
                string fieldName = "field_" + i;
                FieldType type = (FieldType) random.Next(0, values.Length);
                switch (type)
                {
                    case FieldType.Bool:
                        schema.AddField(new FieldInfo()
                            {Name = fieldName, FieldType = type, Bits = 1, Delta = false});
                        break;

                    case FieldType.UInt:
                    {
                        var bits = 32;
                        switch (random.Next(0, 3))
                        {
                            case 0:
                                bits = 8;
                                break;
                            case 1:
                                bits = 16;
                                break;
                            case 2:
                                bits = 32;
                                break;
                            default:
                                Assert.Fail();
                                break;
                        }

                        schema.AddField(new FieldInfo()
                            {Name = fieldName, FieldType = type, Bits = bits, Delta = true});
                        break;
                    }

                    case FieldType.Int:
                    {
                        var bits = 32;
                        switch (random.Next(0, 2))
                        {
                            case 0:
                                bits = 16;
                                break;
                            case 1:
                                bits = 32;
                                break;
                            default:
                                Assert.Fail();
                                break;
                        }

                        schema.AddField(new FieldInfo()
                            {Name = fieldName, FieldType = type, Bits = bits, Delta = true});
                        break;
                    }

                    case FieldType.Float:
                    case FieldType.Vector2:
                    case FieldType.Vector3:
                    {
                        var delta = random.Next(2) == 1;
                        var field =
                            new FieldInfo()
                            {
                                Name = fieldName,
                                FieldType = type,
                                Bits = 32,
                                Delta = delta,
                                Precision = delta ? random.Next(4) : 0
                            };

                        schema.AddField(field);
                        break;
                    }

                    case FieldType.Quaternion:
                    {
                        var delta = random.Next(2) == 1;
                        schema.AddField(new FieldInfo()
                        {
                            Name = fieldName, FieldType = type, Bits = 32, Delta = delta,
                            Precision = delta ? random.Next(4) : 0
                        });
                        break;
                    }

                    case FieldType.String:
                    case FieldType.ByteArray:
                        schema.AddField(new FieldInfo()
                            {Name = fieldName, FieldType = type, ArraySize = 1024});
                        break;

                    default:
                        Assert.Fail();
                        break;
                }
            }

            schema.ResetPredictPlan();
            return schema;
        }

        public static List<object> GenerateRandomValues(NetworkSchema schema, int seed)
        {
            var random = new System.Random(seed);
            var values = new List<object>();
            for (var i = 0; i < schema.NumFields; ++i)
            {
                var field = schema.Fields[i];
                switch (field.FieldType)
                {
                    case FieldType.Bool:
                        values.Add(random.Next(2) == 1 ? true : false);
                        break;
                    case FieldType.UInt:
                        switch (field.Bits)
                        {
                            case 8:
                                values.Add((byte) random.Next(0, byte.MaxValue));
                                break;
                            case 16:
                                values.Add((ushort) random.Next(0, ushort.MaxValue));
                                break;
                            case 32:
                                values.Add((uint) random.Next(int.MinValue, int.MaxValue));
                                break;
                            default:
                                Assert.Fail();
                                break;
                        }

                        break;
                    case FieldType.Int:
                        switch (field.Bits)
                        {
                            case 16:
                                values.Add((short) random.Next(ushort.MinValue, ushort.MaxValue));
                                break;
                            case 32:
                                values.Add((int) random.Next(int.MinValue, int.MaxValue));
                                break;
                            default:
                                Assert.Fail();
                                break;
                        }

                        break;
                    case FieldType.Float:
                        values.Add((float) random.NextDouble());
                        break;
                    case FieldType.Vector2:
                        values.Add(new Vector2((float) random.NextDouble(), (float) random.NextDouble()));
                        break;
                    case FieldType.Vector3:
                        values.Add(new Vector3((float) random.NextDouble(), (float) random.NextDouble(),
                            (float) random.NextDouble()));
                        break;
                    case FieldType.Quaternion:
                        values.Add(new Quaternion((float) random.NextDouble(), (float) random.NextDouble(),
                            (float) random.NextDouble(), (float) random.NextDouble()));
                        break;
                    case FieldType.String:
                        if (random.Next(10) == 0)
                            values.Add(null);
                        else if (random.Next(10) == 0)
                            values.Add("");
                        else
                        {
                            var start = random.Next(s_RandomText.Length - 2);
                            var length = Math.Min(random.Next(1, 50), s_RandomText.Length - start);
                            values.Add(s_RandomText.Substring(start, length));
                        }

                        break;
                    case FieldType.ByteArray:
                        byte[] bytes = new byte[10];
                        random.NextBytes(bytes);
                        values.Add(bytes);
                        break;
                }
            }

            return values;
        }

        unsafe public static void WriteValues(List<object> values, uint[] buffer, NetworkSchema schema)
        {
            fixed (uint* buf = buffer)
            {
                NetworkWriter writer = new NetworkWriter(buf, buffer.Length, schema);
                for (int j = 0; j < values.Count; ++j)
                {
                    var value = values[j];
                    var field = schema.Fields[j];
                    string fieldName = "field_" + j;
                    if (value is bool)
                        writer.WriteBoolean(fieldName, (bool) value);
                    else if (value is byte)
                        writer.WriteByte(fieldName, (byte) value);
                    else if (value is ushort)
                        writer.WriteUInt16(fieldName, (ushort) value);
                    else if (value is short)
                        writer.WriteInt16(fieldName, (short) value);
                    else if (value is uint)
                        writer.WriteUInt32(fieldName, (uint) value);
                    else if (value is int)
                        writer.WriteInt32(fieldName, (int) value);
                    else if (value is float)
                    {
                        if (field.Delta)
                            writer.WriteFloatQ(fieldName, (float) value, field.Precision);
                        else
                            writer.WriteFloat(fieldName, (float) value);
                    }
                    else if (value is Vector2)
                    {
                        if (field.Delta)
                            writer.WriteVector2Q(fieldName, (Vector2) value, field.Precision);
                        else
                            writer.WriteVector2(fieldName, (Vector2) value);
                    }
                    else if (value is Vector3)
                    {
                        if (field.Delta)
                            writer.WriteVector3Q(fieldName, (Vector3) value, field.Precision);
                        else
                            writer.WriteVector3(fieldName, (Vector3) value);
                    }
                    else if (value is Quaternion)
                        if (field.Delta)
                            writer.WriteQuaternionQ(fieldName, (Quaternion) value, field.Precision);
                        else
                            writer.WriteQuaternion(fieldName, (Quaternion) value);
                    else if (value is string)
                        writer.WriteString(fieldName, (string) value, 1024);
                    else if (value is byte[])
                        writer.WriteBytes(fieldName, (byte[]) value, 0, 10, 1024);
                    else if (value == null && field.FieldType == FieldType.String)
                        writer.WriteString(fieldName, null, 1024);
                    else
                        Assert.Fail();
                }

                writer.Flush();
            }
        }

        unsafe public static void ReadAndAssertValues(List<object> values, uint[] buffer, NetworkSchema schema)
        {
            fixed (uint* buf = buffer)
            {
                NetworkReader reader = new NetworkReader(buf, schema);
                for (int j = 0; j < schema.NumFields; ++j)
                {
                    var value = values[j];
                    var field = schema.Fields[j];
                    if (value is bool)
                        Assert.AreEqual(value, reader.ReadBoolean());
                    else if (value is byte)
                        Assert.AreEqual(value, reader.ReadByte());
                    else if (value is ushort)
                        Assert.AreEqual(value, reader.ReadUInt16());
                    else if (value is short)
                        Assert.AreEqual(value, reader.ReadInt16());
                    else if (value is uint)
                        Assert.AreEqual(value, reader.ReadUInt32());
                    else if (value is int)
                        Assert.AreEqual(value, reader.ReadInt32());
                    else if (value is float)
                    {
                        var expected = (float) value;
                        if (field.Delta)
                        {
                            var actual = reader.ReadFloatQ();
                            Assert.IsTrue(Math.Abs(actual - expected) < Math.Pow(10, -field.Precision));
                        }
                        else
                        {
                            var actual = reader.ReadFloat();
                            Assert.AreEqual(expected, actual);
                        }
                    }
                    else if (value is Vector2)
                    {
                        var expected = (Vector2) value;
                        if (field.Delta)
                        {
                            var actual = reader.ReadVector2Q();
                            Assert.IsTrue(Math.Abs(actual.x - expected.x) < Math.Pow(10, -field.Precision));
                            Assert.IsTrue(Math.Abs(actual.y - expected.y) < Math.Pow(10, -field.Precision));
                        }
                        else
                        {
                            var actual = reader.ReadVector2();
                            Assert.AreEqual(expected.x, actual.x);
                            Assert.AreEqual(expected.y, actual.y);
                        }
                    }
                    else if (value is Vector3)
                    {
                        var expected = (Vector3) value;
                        if (field.Delta)
                        {
                            var actual = reader.ReadVector3Q();
                            Assert.IsTrue(Math.Abs(actual.x - expected.x) < Math.Pow(10, -field.Precision));
                            Assert.IsTrue(Math.Abs(actual.y - expected.y) < Math.Pow(10, -field.Precision));
                            Assert.IsTrue(Math.Abs(actual.z - expected.z) < Math.Pow(10, -field.Precision));
                        }
                        else
                        {
                            var actual = reader.ReadVector3();
                            Assert.AreEqual(expected.x, actual.x);
                            Assert.AreEqual(expected.y, actual.y);
                            Assert.AreEqual(expected.z, actual.z);
                        }
                    }
                    else if (value is Quaternion)
                    {
                        var expected = (Quaternion) value;

                        if (field.Delta)
                        {
                            var actual = reader.ReadQuaternionQ();
                            Assert.IsTrue(Math.Abs(actual.x - expected.x) < Math.Pow(10, -field.Precision));
                            Assert.IsTrue(Math.Abs(actual.y - expected.y) < Math.Pow(10, -field.Precision));
                            Assert.IsTrue(Math.Abs(actual.z - expected.z) < Math.Pow(10, -field.Precision));
                            Assert.IsTrue(Math.Abs(actual.w - expected.w) < Math.Pow(10, -field.Precision));
                        }
                        else
                        {
                            var actual = reader.ReadQuaternion();
                            Assert.AreEqual(expected.x, actual.x);
                            Assert.AreEqual(expected.y, actual.y);
                            Assert.AreEqual(expected.z, actual.z);
                            Assert.AreEqual(expected.w, actual.w);
                        }
                    }
                    else if (value is string)
                    {
                        var actual = reader.ReadString(1024);
                        if (value != null)
                            Assert.AreEqual(value, actual);
                        else
                            Assert.AreEqual("", actual);
                    }
                    else if (value is byte[])
                    {
                        var expected = (byte[]) value;
                        var actual = new byte[10];
                        var length = reader.ReadBytes(actual, 0, 1024);
                        Assert.AreEqual(10, length);
                        for (int b = 0; b < 10; ++b)
                            Assert.AreEqual(expected[b], actual[b]);
                    }
                    else if (value == null && field.FieldType == FieldType.String)
                    {
                        var actual = reader.ReadString(1024);
                        Assert.AreEqual("", actual);
                    }
                    else
                        Assert.Fail();
                }
            }
        }


        static string s_RandomText =
            @"Lorem ipsum dolor sit amet, consectetur adipiscing elit. Phasellus scelerisque egestas nibh, tristique hendrerit felis porta ut. Donec rhoncus eleifend venenatis. Donec viverra, magna a tristique vulputate, mauris nibh semper sapien, non eleifend quam lacus ut nisl. Phasellus neque est, placerat a consectetur nec, tempor a nisl. Aenean lacinia cursus risus nec molestie. Curabitur laoreet justo at pretium rutrum. Nunc tincidunt mauris vel est suscipit, feugiat tempor ligula bibendum. Pellentesque aliquam felis nec efficitur aliquam. Vivamus convallis justo quis leo pellentesque venenatis a sit amet sem. Nullam ac varius nisi. Nunc vel tincidunt elit, non aliquet lectus. Nullam rutrum dictum turpis, ac pulvinar urna ultrices a. Vestibulum nec nulla odio.\
        Quisque ut sollicitudin leo, non vehicula quam. Mauris sit amet facilisis arcu. Curabitur quis venenatis nunc. Nunc ultrices erat posuere justo hendrerit, nec convallis metus rhoncus. Aenean a interdum odio, in facilisis diam. Pellentesque consectetur, risus nec consequat dapibus, enim neque porttitor turpis, at dignissim urna velit ut justo. Nulla consectetur euismod leo sit amet placerat. Integer vel lorem dignissim, blandit massa vel, pulvinar odio. Quisque pretium ut tortor nec consectetur. Nunc in venenatis tellus, a consectetur est. Integer nec accumsan neque, eget feugiat ex.\
        Nulla tincidunt auctor neque, at vehicula arcu vestibulum at. Aenean pellentesque tempor ipsum, quis finibus nisl tincidunt vel. Integer lacinia elit odio, non malesuada est finibus luctus. Ut lectus odio, congue condimentum ultricies et, convallis vel leo. Mauris vehicula enim et fermentum sagittis. Interdum et malesuada fames ac ante ipsum primis in faucibus. Integer vel augue justo.\
        Vestibulum blandit posuere dui non porttitor. Etiam lobortis erat sed leo sollicitudin, nec accumsan augue lacinia. Maecenas eu nisi tellus. Mauris malesuada consequat dui quis vulputate. Sed quis tristique tellus. Fusce ac nisl ut tellus porta fermentum. Quisque magna libero, accumsan pharetra libero non, luctus convallis nisl. Morbi varius neque vitae tortor maximus, ut accumsan justo fringilla. Sed mi urna, pellentesque ut nibh ut, ultricies lacinia mi. Morbi placerat urna libero, ut iaculis ligula varius et. In commodo dolor ac tortor varius cursus. In tincidunt justo id odio dignissim, a bibendum elit feugiat.\
        Sed facilisis enim felis, non pellentesque tellus ultrices nec. Vestibulum sed lacus vel dui luctus malesuada interdum sit amet ligula. Vivamus accumsan tincidunt mattis. Cras lobortis sodales libero, vel varius mauris rutrum quis. Nunc eros nunc, volutpat a lacinia id, faucibus in enim. Donec non faucibus massa. Ut congue erat nec vehicula accumsan. Donec placerat, urna non vehicula venenatis, nulla quam efficitur libero, vel tincidunt eros diam quis nibh. Sed commodo, libero sed efficitur molestie, erat nunc varius diam, eu ornare risus arcu quis neque. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; In aliquet laoreet nisl, ut malesuada magna hendrerit at. Duis facilisis interdum blandit. Proin arcu nulla, gravida in odio nec, facilisis condimentum tortor. Curabitur sollicitudin ac ante ac placerat. Mauris sed arcu quis neque posuere iaculis id at nulla. Duis laoreet faucibus mi, eu laoreet ligula dictum a.";
    }
}