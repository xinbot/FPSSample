using System.Net;
using Unity.Collections;
using Unity.Networking.Transport;
using UdpNetworkDriver = Unity.Networking.Transport.BasicNetworkDriver<Unity.Networking.Transport.IPv4UDPSocket>;
using EventType = Unity.Networking.Transport.NetworkEvent.Type;

namespace Networking.Socket
{
    public class SocketTransport : INetworkTransport
    {
        private byte[] _buffer = new byte[1024 * 8];
        private BasicNetworkDriver<IPv4UDPSocket> _socket;
        private NativeArray<NetworkConnection> _idToConnection;

        public SocketTransport(int port = 0, int maxConnections = 16)
        {
            var networkDataStreamParameter = new NetworkDataStreamParameter {size = 10 * NetworkConfig.MAXPackageSize};
            var networkConfigParameter = new NetworkConfigParameter
                {disconnectTimeout = ServerGameLoop.serverDisconnectTimeout.IntValue};
            _socket = new UdpNetworkDriver(networkDataStreamParameter, networkConfigParameter);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));

            _idToConnection = new NativeArray<NetworkConnection>(maxConnections, Allocator.Persistent);

            if (port != 0)
            {
                _socket.Listen();
            }
        }

        public int Connect(string ip, int port)
        {
            var connection = _socket.Connect(new IPEndPoint(IPAddress.Parse(ip), port));
            _idToConnection[connection.InternalId] = connection;
            return connection.InternalId;
        }

        public void Disconnect(int connection)
        {
            _socket.Disconnect(_idToConnection[connection]);
            _idToConnection[connection] = default(NetworkConnection);
        }

        public void Update()
        {
            _socket.ScheduleUpdate().Complete();
        }

        public bool NextEvent(ref TransportEvent e)
        {
            NetworkConnection connection = _socket.Accept();
            if (connection.IsCreated)
            {
                e.EventType = TransportEvent.Type.Connect;
                e.ConnectionId = connection.InternalId;
                _idToConnection[connection.InternalId] = connection;
                return true;
            }

            DataStreamReader reader;
            var context = default(DataStreamReader.Context);
            var eventType = _socket.PopEvent(out connection, out reader);
            if (eventType == EventType.Empty)
            {
                return false;
            }

            int size = 0;
            if (reader.IsCreated)
            {
                GameDebug.Assert(_buffer.Length >= reader.Length);
                reader.ReadBytesIntoArray(ref context, ref _buffer, reader.Length);
                size = reader.Length;
            }

            switch (eventType)
            {
                case EventType.Data:
                    e.EventType = TransportEvent.Type.Data;
                    e.Data = _buffer;
                    e.DataSize = size;
                    e.ConnectionId = connection.InternalId;
                    break;
                case EventType.Connect:
                    e.EventType = TransportEvent.Type.Connect;
                    e.ConnectionId = connection.InternalId;
                    _idToConnection[connection.InternalId] = connection;
                    break;
                case EventType.Disconnect:
                    e.EventType = TransportEvent.Type.Disconnect;
                    e.ConnectionId = connection.InternalId;
                    break;
                default:
                    return false;
            }

            return true;
        }

        public void SendData(int connectionId, byte[] data, int sendSize)
        {
            using (var sendStream = new DataStreamWriter(sendSize, Allocator.Persistent))
            {
                sendStream.Write(data, sendSize);
                _socket.Send(_idToConnection[connectionId], sendStream);
            }
        }

        public string GetConnectionDescription(int connectionId)
        {
            // TODO enable this once RemoteEndPoint is implemented m_Socket.RemoteEndPoint(m_IdToConnection[connectionId]).GetIp();
            return "";
        }

        public void Shutdown()
        {
            _socket.Dispose();
            _idToConnection.Dispose();
        }
    }
}