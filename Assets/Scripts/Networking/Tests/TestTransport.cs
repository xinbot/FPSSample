using System.Collections.Generic;
using UnityEngine;

namespace Networking.Tests
{
    public class TestTransport : INetworkTransport
    {
        private class Package
        {
            public int From;
            public int Size;
            public readonly byte[] Data = new byte[2048];
        }

        private readonly int _id;
        private readonly string _name;

        private readonly Queue<int> _connects = new Queue<int>();
        private readonly Queue<int> _disconnects = new Queue<int>();
        private readonly Queue<Package> _incomingPackages = new Queue<Package>();

        private static readonly List<TestTransport> EndPoints = new List<TestTransport>();

        public TestTransport(string ip, int port)
        {
            _id = EndPoints.Count;
            _name = $"{ip}:{port}";

            EndPoints.Add(this);
        }

        public static void Reset()
        {
            EndPoints.Clear();
        }

        public void Update()
        {
        }

        public void Shutdown()
        {
        }

        public bool NextEvent(ref TransportEvent e)
        {
            // Pass back connects, disconnects and data
            if (_connects.Count > 0)
            {
                e.type = TransportEvent.Type.Connect;
                e.connectionId = _connects.Dequeue();
            }
            else if (_disconnects.Count > 0)
            {
                e.type = TransportEvent.Type.Disconnect;
                e.connectionId = _disconnects.Dequeue();
            }
            else if (_incomingPackages.Count > 0)
            {
                var p = _incomingPackages.Dequeue();
                e.type = TransportEvent.Type.Data;
                e.connectionId = p.From;
                e.data = p.Data;
                e.dataSize = p.Size;
            }
            else
            {
                return false;
            }

            return true;
        }

        public int Connect(string ip, int port)
        {
            var name = $"{ip}:{port}";
            var ep = EndPoints.Find((x) => x._name == name);
            if (ep != null)
            {
                _connects.Enqueue(ep._id);
                ep._connects.Enqueue(_id);
                return ep._id;
            }

            return -1;
        }

        public void Disconnect(int connectionId)
        {
            var remote = EndPoints[_id];
            if (remote != null)
            {
                remote._disconnects.Enqueue(_id);
            }
        }

        public void SendData(int connectionId, byte[] data, int sendSize)
        {
            var remote = EndPoints[connectionId];
            Debug.Assert(remote != null);

            var package = new Package();
            package.From = _id;
            package.Size = sendSize;
            NetworkUtils.MemCopy(data, 0, package.Data, 0, sendSize);
            remote._incomingPackages.Enqueue(package);
        }

        public string GetConnectionDescription(int connectionId)
        {
            return "" + connectionId;
        }

        public void DropPackages()
        {
            _incomingPackages.Clear();
        }
    }
}