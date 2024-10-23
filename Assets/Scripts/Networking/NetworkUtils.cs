using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Networking
{
    public static class NetworkUtils
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct UIntFloat
        {
            [FieldOffset(0)] public float floatValue;
            [FieldOffset(0)] public uint intValue;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct ULongDouble
        {
            [FieldOffset(0)] public double doubleValue;
            [FieldOffset(0)] public ulong longValue;
        }

        public static readonly System.Diagnostics.Stopwatch Stopwatch = new System.Diagnostics.Stopwatch();

        private const string HexDigits = "0123456789ABCDEF";

        static NetworkUtils()
        {
            Stopwatch.Start();
        }

        public static float UInt32ToFloat(uint value)
        {
            return new UIntFloat {intValue = value}.floatValue;
        }

        public static uint FloatToUInt32(float value)
        {
            return new UIntFloat {floatValue = value}.intValue;
        }

        public static Color32 Uint32ToColor32(uint value)
        {
            return new Color32((byte) (value & 0xff), (byte) ((value >> 8) & 0xff), (byte) ((value >> 16) & 0xff),
                (byte) ((value >> 24) & 0xff));
        }

        public static UInt32 Color32ToUInt32(Color32 value)
        {
            return value.r | (uint) (value.g << 8) | (uint) (value.b << 16) | (uint) (value.a << 24);
        }

        public static double DoubleToUInt64(ulong value)
        {
            return new ULongDouble {longValue = value}.doubleValue;
        }

        public static ulong UInt64ToDouble(double value)
        {
            return new ULongDouble {doubleValue = value}.longValue;
        }

        public static string HexString(byte[] values, int count)
        {
            var d = new char[count * 2];
            for (int i = 0; i < count; i++)
            {
                d[i * 2 + 0] = HexDigits[values[i] >> 4];
                d[i * 2 + 1] = HexDigits[values[i] & 0xf];
            }

            return new string(d) + " (" + count + ")";
        }

        public static int CalculateRequiredBits(long min, long max)
        {
            return min == max ? 0 : (int) Math.Log(max - min, 2) + 1;
        }

        public static void MemCopy(byte[] src, int srcIndex, byte[] dst, int dstIndex, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                dst[dstIndex++] = src[srcIndex++];
            }
        }

        public static int MemCmp(byte[] a, int aIndex, byte[] b, int bIndex, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                var diff = b[bIndex++] - a[aIndex++];
                if (diff != 0)
                {
                    return diff;
                }
            }

            return 0;
        }

        public static int MemCmp(uint[] a, int aIndex, uint[] b, int bIndex, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                var diff = b[bIndex++] - a[aIndex++];
                if (diff != 0)
                {
                    return (int) diff;
                }
            }

            return 0;
        }

        public static uint SimpleHash(uint[] array, int count)
        {
            uint hash = 0;
            for (int i = 0; i < count; i++)
            {
                hash = hash * 179 + array[i] + 1;
            }

            return hash;
        }

        public static uint SimpleHashStreaming(uint oldHash, uint value)
        {
            return oldHash * 179 + value + 1;
        }

        public static bool EndpointParse(string endpoint, out IPAddress address, out int port, int defaultPort)
        {
            string addressPart;
            address = null;
            port = 0;

            if (endpoint.Contains(":"))
            {
                int.TryParse(endpoint.AfterLast(":"), out port);
                addressPart = endpoint.BeforeFirst(":");
            }
            else
            {
                addressPart = endpoint;
            }

            if (port == 0)
            {
                port = defaultPort;
            }

            // Resolve in case we got a hostname
            var resolvedAddress = Dns.GetHostAddresses(addressPart);
            foreach (var r in resolvedAddress)
            {
                if (r.AddressFamily == AddressFamily.InterNetwork)
                {
                    // Pick first ipv4
                    address = r;
                    return true;
                }
            }

            return false;
        }

        public static List<string> GetLocalInterfaceAddresses()
        {
            // Useful to print 'best guess' for local ip, so...
            List<NetworkInterface> interfaces = new List<NetworkInterface>();
            List<string> addresses = new List<string>();
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var type = item.NetworkInterfaceType;
                if (type != NetworkInterfaceType.Ethernet && type != NetworkInterfaceType.Wireless80211)
                {
                    continue;
                }

                interfaces.Add(item);
            }

            // Sort interfaces so those with most gateways are first. Attempting to guess what is the 'main' ip address
            interfaces.Sort((a, b) =>
            {
                return b.GetIPProperties().GatewayAddresses.Count
                    .CompareTo(a.GetIPProperties().GatewayAddresses.Count);
            });

            foreach (NetworkInterface item in interfaces)
            {
                try
                {
                    foreach (UnicastIPAddressInformation addr in item.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        addresses.Add(addr.Address.ToString());
                    }
                }
                catch (Exception e)
                {
                    // NOTE : For some reason this can throw marshal exception in the interop 
                    // to native network code on some computers (when running player but not in editor)?
                    GameDebug.Log("Error " + e.Message + " while getting IP properties for " + item.Description);
                }
            }

            return addresses;
        }
    }

    internal class ByteArrayComp : IEqualityComparer<byte[]>, IComparer<byte[]>
    {
        public static readonly ByteArrayComp Instance = new ByteArrayComp();

        public int Compare(byte[] x, byte[] y)
        {
            if (x == null || y == null)
            {
                const string message = "Trying to compare array with null";
                throw new ArgumentNullException(message);
            }

            var xl = x.Length;
            var yl = y.Length;
            if (xl != yl)
            {
                return yl - xl;
            }

            for (int i = 0; i < xl; i++)
            {
                var d = y[i] - x[i];
                if (d != 0)
                {
                    return d;
                }
            }

            return 0;
        }

        public bool Equals(byte[] x, byte[] y)
        {
            return Compare(x, y) == 0;
        }

        public int GetHashCode(byte[] x)
        {
            if (x == null)
            {
                const string message = "Trying to compare array with null";
                throw new ArgumentNullException(message);
            }

            var xl = x.Length;
            if (xl >= 4)
            {
                return (x[0] + (x[1] << 8) + (x[2] << 16) + (x[3] << 24));
            }

            return 0;
        }
    }

    public class Aggregator
    {
        private const int WindowSize = 120;

        public readonly FloatRollingAverage Graph = new FloatRollingAverage(WindowSize);

        private float _previousValue;

        public void Update(float value)
        {
            Graph.Update(value - _previousValue);
            _previousValue = value;
        }
    }
}