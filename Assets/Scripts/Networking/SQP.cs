using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Networking.Transport;
using UnityEngine;

namespace Networking
{
    /// <summary>
    /// An implementation of the ServerInfo part of the Server Query Protocol
    /// </summary>
    [Flags]
    public enum SqpChunkType
    {
        ServerInfo = 1,
        ServerRules = 2,
        PlayerInfo = 4,
        TeamInfo = 8
    }

    public enum SqpMessageType
    {
        ChallengeRequest = 0,
        ChallengeResponse = 0,
        QueryRequest = 1,
        QueryResponse = 1
    }

    public interface ISqpMessage
    {
        void ToStream(ref DataStreamWriter writer);
        void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx);
    }

    public struct SqpHeader : ISqpMessage
    {
        public uint ChallengeId;
        public byte type { get; internal set; }

        public void ToStream(ref DataStreamWriter writer)
        {
            writer.Write(type);
            writer.WriteNetworkByteOrder(ChallengeId);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            type = reader.ReadByte(ref ctx);
            ChallengeId = reader.ReadUIntNetworkByteOrder(ref ctx);
        }
    }

    public struct ChallengeRequest : ISqpMessage
    {
        public SqpHeader Header;

        public void ToStream(ref DataStreamWriter writer)
        {
            Header.type = (byte) SqpMessageType.ChallengeRequest;
            Header.ToStream(ref writer);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Header.FromStream(reader, ref ctx);
        }
    }

    public struct ChallengeResponse
    {
        public SqpHeader Header;

        public void ToStream(ref DataStreamWriter writer)
        {
            Header.type = (byte) SqpMessageType.ChallengeResponse;
            Header.ToStream(ref writer);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Header.FromStream(reader, ref ctx);
        }
    }

    public struct QueryRequest
    {
        public SqpHeader Header;
        public ushort Version;
        public byte RequestedChunks;

        public void ToStream(ref DataStreamWriter writer)
        {
            Header.type = (byte) SqpMessageType.QueryRequest;
            Header.ToStream(ref writer);

            writer.WriteNetworkByteOrder(Version);
            writer.Write(RequestedChunks);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Header.FromStream(reader, ref ctx);
            Version = reader.ReadUShortNetworkByteOrder(ref ctx);
            RequestedChunks = reader.ReadByte(ref ctx);
        }
    }

    public struct QueryResponseHeader
    {
        public SqpHeader Header;
        public ushort Version;
        public byte CurrentPacket;
        public byte LastPacket;
        public ushort Length;

        public DataStreamWriter.DeferredUShortNetworkByteOrder ToStream(ref DataStreamWriter writer)
        {
            Header.type = (byte) SqpMessageType.QueryResponse;
            Header.ToStream(ref writer);

            writer.WriteNetworkByteOrder(Version);
            writer.Write(CurrentPacket);
            writer.Write(LastPacket);
            return writer.WriteNetworkByteOrder(Length);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            Header.FromStream(reader, ref ctx);
            Version = reader.ReadUShortNetworkByteOrder(ref ctx);
            CurrentPacket = reader.ReadByte(ref ctx);
            LastPacket = reader.ReadByte(ref ctx);
            Length = reader.ReadUShortNetworkByteOrder(ref ctx);
        }
    }

    public static class DataStreamExtensions
    {
        private static readonly byte[] Buffer = new byte[byte.MaxValue];

        public static unsafe void WriteString(this DataStreamWriter writer, string value, Encoding encoding)
        {
            var encoder = encoding.GetEncoder();

            var chars = value.ToCharArray();
            int charsUsed, bytesUsed;
            bool completed;

            encoder.Convert(chars, 0, chars.Length, Buffer, 0, byte.MaxValue, true, out charsUsed, out bytesUsed,
                out completed);

            Debug.Assert(bytesUsed <= byte.MaxValue);

            writer.Write((byte) bytesUsed);
            fixed (byte* buf = Buffer)
            {
                writer.WriteBytes(buf, bytesUsed);
            }
        }

        public static unsafe string ReadString(this DataStreamReader reader, ref DataStreamReader.Context ctx,
            Encoding encoding)
        {
            var length = reader.ReadByte(ref ctx);
            fixed (byte* buf = Buffer)
            {
                reader.ReadBytes(ref ctx, buf, length);
            }

            return encoding.GetString(Buffer, 0, length);
        }
    }

    public class ServerInfo
    {
        public QueryResponseHeader QueryHeader;
        public uint ChunkLen;
        public Data ServerInfoData;

        private static readonly Encoding Encoding = new UTF8Encoding();

        public ServerInfo()
        {
            ServerInfoData = new Data();
        }

        public class Data
        {
            public ushort CurrentPlayers;
            public ushort MaxPlayers;

            public string ServerName = "";
            public string GameType = "";
            public string BuildId = "";
            public string Map = "";
            public ushort Port;

            public void ToStream(ref DataStreamWriter writer)
            {
                writer.WriteNetworkByteOrder(CurrentPlayers);
                writer.WriteNetworkByteOrder(MaxPlayers);

                writer.WriteString(ServerName, Encoding);
                writer.WriteString(GameType, Encoding);
                writer.WriteString(BuildId, Encoding);
                writer.WriteString(Map, Encoding);

                writer.WriteNetworkByteOrder(Port);
            }

            public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
            {
                CurrentPlayers = reader.ReadUShortNetworkByteOrder(ref ctx);
                MaxPlayers = reader.ReadUShortNetworkByteOrder(ref ctx);

                ServerName = reader.ReadString(ref ctx, Encoding);
                GameType = reader.ReadString(ref ctx, Encoding);
                BuildId = reader.ReadString(ref ctx, Encoding);
                Map = reader.ReadString(ref ctx, Encoding);

                Port = reader.ReadUShortNetworkByteOrder(ref ctx);
            }
        }

        public void ToStream(ref DataStreamWriter writer)
        {
            var lengthValue = QueryHeader.ToStream(ref writer);

            var start = (ushort) writer.Length;

            var chunkValue = writer.WriteNetworkByteOrder((uint) 0);

            var chunkStart = writer.Length;
            ServerInfoData.ToStream(ref writer);
            ChunkLen = (uint) (writer.Length - chunkStart);
            QueryHeader.Length = (ushort) (writer.Length - start);

            lengthValue.Update(QueryHeader.Length);
            chunkValue.Update(ChunkLen);

            IPAddress.HostToNetworkOrder((short) QueryHeader.Length);
            IPAddress.HostToNetworkOrder((int) ChunkLen);
        }

        public void FromStream(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            QueryHeader.FromStream(reader, ref ctx);
            ChunkLen = reader.ReadUIntNetworkByteOrder(ref ctx);

            ServerInfoData.FromStream(reader, ref ctx);
        }
    }

    public static class UdpExtensions
    {
        public static SocketError SetupAndBind(this System.Net.Sockets.Socket socket, int port = 0)
        {
            SocketError error = SocketError.Success;
            socket.Blocking = false;

            var ep = new IPEndPoint(IPAddress.Any, port);
            try
            {
                socket.Bind(ep);
            }
            catch (SocketException e)
            {
                error = e.SocketErrorCode;
                throw e;
            }

            return error;
        }
    }

    public class SQPClient
    {
        System.Net.Sockets.Socket m_Socket;

        byte[] m_Buffer = new byte[1472];

        System.Net.EndPoint endpoint = new System.Net.IPEndPoint(0, 0);

        public enum SQPClientState
        {
            Idle,
            WaitingForChallange,
            WaitingForResponse,
        }

        public class SQPQuery
        {
            public SQPQuery()
            {
                m_ServerInfo = new ServerInfo();
                m_State = SQPClientState.Idle;
                m_Server = null;
            }

            public void Init(IPEndPoint server)
            {
                GameDebug.Assert(m_State == SQPClientState.Idle);
                GameDebug.Assert(m_Server == null);
                m_Server = server;
                validResult = false;
            }

            public IPEndPoint m_Server;
            public bool validResult;
            public SQPClientState m_State;
            public uint ChallangeId;
            public long RTT;
            public long StartTime;
            public ServerInfo m_ServerInfo;
        }

        List<SQPQuery> m_Queries = new List<SQPQuery>();

        public SQPClient()
        {
            m_Socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_Socket.SetupAndBind(0);
        }

        public SQPQuery GetSQPQuery(IPEndPoint server)
        {
            SQPQuery q = null;
            foreach (var pending in m_Queries)
            {
                if (pending.m_State == SQPClientState.Idle && pending.m_Server == null)
                {
                    q = pending;
                    break;
                }
            }

            if (q == null)
            {
                q = new SQPQuery();
                m_Queries.Add(q);
            }

            q.Init(server);

            return q;
        }

        public void ReleaseSQPQuery(SQPQuery q)
        {
            q.m_Server = null;
        }

        public void StartInfoQuery(SQPQuery q)
        {
            GameDebug.Assert(q.m_State == SQPClientState.Idle);

            q.StartTime = NetworkUtils.Stopwatch.ElapsedMilliseconds;

            var writer = new DataStreamWriter(m_Buffer.Length, Unity.Collections.Allocator.Temp);
            var req = new ChallengeRequest();
            req.ToStream(ref writer);

            writer.CopyTo(0, writer.Length, ref m_Buffer);
            m_Socket.SendTo(m_Buffer, writer.Length, SocketFlags.None, q.m_Server);
            q.m_State = SQPClientState.WaitingForChallange;
        }

        private void SendServerInfoQuery(SQPQuery q)
        {
            q.StartTime = NetworkUtils.Stopwatch.ElapsedMilliseconds;
            var req = new QueryRequest();
            req.Header.ChallengeId = q.ChallangeId;
            req.RequestedChunks = (byte) SqpChunkType.ServerInfo;

            var writer = new DataStreamWriter(m_Buffer.Length, Unity.Collections.Allocator.Temp);
            req.ToStream(ref writer);

            q.m_State = SQPClientState.WaitingForResponse;
            writer.CopyTo(0, writer.Length, ref m_Buffer);
            m_Socket.SendTo(m_Buffer, writer.Length, SocketFlags.None, q.m_Server);
            writer.Dispose();
        }

        public void Update()
        {
            if (m_Socket.Poll(0, SelectMode.SelectRead))
            {
                int read = m_Socket.ReceiveFrom(m_Buffer, m_Buffer.Length, SocketFlags.None, ref endpoint);
                if (read > 0)
                {
                    // Transfer incoming data in m_Buffer into a DataStreamReader
                    var writer = new DataStreamWriter(m_Buffer.Length, Unity.Collections.Allocator.Temp);
                    writer.Write(m_Buffer, read);
                    var reader = new DataStreamReader(writer, 0, read);
                    var ctx = default(DataStreamReader.Context);

                    var header = new SqpHeader();
                    header.FromStream(reader, ref ctx);

                    foreach (var q in m_Queries)
                    {
                        if (q.m_Server == null || !endpoint.Equals(q.m_Server))
                            continue;

                        switch (q.m_State)
                        {
                            case SQPClientState.Idle:
                                // Just ignore if we get extra data
                                break;

                            case SQPClientState.WaitingForChallange:
                                if ((SqpMessageType) header.type == SqpMessageType.ChallengeResponse)
                                {
                                    q.ChallangeId = header.ChallengeId;
                                    q.RTT = NetworkUtils.Stopwatch.ElapsedMilliseconds - q.StartTime;
                                    // We restart timer so we can get an RTT that is an average between two measurements
                                    q.StartTime = NetworkUtils.Stopwatch.ElapsedMilliseconds;
                                    SendServerInfoQuery(q);
                                }

                                break;

                            case SQPClientState.WaitingForResponse:
                                if ((SqpMessageType) header.type == SqpMessageType.QueryResponse)
                                {
                                    ctx = default(DataStreamReader.Context);
                                    q.m_ServerInfo.FromStream(reader, ref ctx);

                                    // We report the average of two measurements
                                    q.RTT = (q.RTT + (NetworkUtils.Stopwatch.ElapsedMilliseconds - q.StartTime)) / 2;

                                    /*
                                    GameDebug.Log(string.Format("ServerName: {0}, BuildId: {1}, Current Players: {2}, Max Players: {3}, GameType: {4}, Map: {5}, Port: {6}",
                                        m_ServerInfo.ServerInfoData.ServerName,
                                        m_ServerInfo.ServerInfoData.BuildId,
                                        (ushort)m_ServerInfo.ServerInfoData.CurrentPlayers,
                                        (ushort)m_ServerInfo.ServerInfoData.MaxPlayers,
                                        m_ServerInfo.ServerInfoData.GameType,
                                        m_ServerInfo.ServerInfoData.Map,
                                        (ushort)m_ServerInfo.ServerInfoData.Port));
                                        */

                                    q.validResult = true;
                                    q.m_State = SQPClientState.Idle;
                                }

                                break;

                            default:
                                break;
                        }
                    }
                }
            }

            foreach (var q in m_Queries)
            {
                // Timeout if stuck in any state but idle for too long
                if (q.m_State != SQPClientState.Idle)
                {
                    var now = NetworkUtils.Stopwatch.ElapsedMilliseconds;
                    if (now - q.StartTime > 3000)
                    {
                        q.m_State = SQPClientState.Idle;
                    }
                }
            }
        }
    }

    public class SQPServer
    {
        System.Net.Sockets.Socket m_Socket;
        System.Random m_Random;

        ServerInfo m_ServerInfo = new ServerInfo();

        public ServerInfo.Data ServerInfoData
        {
            get { return m_ServerInfo.ServerInfoData; }
            set { m_ServerInfo.ServerInfoData = value; }
        }

        byte[] m_Buffer = new byte[1472];

        EndPoint endpoint = new IPEndPoint(0, 0);
        Dictionary<EndPoint, uint> m_OutstandingTokens = new Dictionary<EndPoint, uint>();

        public SQPServer(int port)
        {
            m_Socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_Socket.SetupAndBind(port);
            m_Random = new System.Random();
            GameDebug.Log("SQP Initialized. Listening on port " + port);
        }

        public void Update()
        {
            if (m_Socket.Poll(0, SelectMode.SelectRead))
            {
                int read = m_Socket.ReceiveFrom(m_Buffer, m_Buffer.Length, SocketFlags.None, ref endpoint);
                if (read > 0)
                {
                    var bufferWriter = new DataStreamWriter(m_Buffer.Length, Unity.Collections.Allocator.Temp);
                    bufferWriter.Write(m_Buffer, read);
                    var reader = new DataStreamReader(bufferWriter, 0, read);
                    var ctx = default(DataStreamReader.Context);

                    var header = new SqpHeader();
                    header.FromStream(reader, ref ctx);

                    SqpMessageType type = (SqpMessageType) header.type;

                    switch (type)
                    {
                        case SqpMessageType.ChallengeRequest:
                        {
                            if (!m_OutstandingTokens.ContainsKey(endpoint))
                            {
                                uint token = GetNextToken();
                                //Debug.Log("token generated: " + token);

                                var writer = new DataStreamWriter(m_Buffer.Length, Unity.Collections.Allocator.Temp);
                                var rsp = new ChallengeResponse();
                                rsp.Header.ChallengeId = token;
                                rsp.ToStream(ref writer);

                                writer.CopyTo(0, writer.Length, ref m_Buffer);
                                m_Socket.SendTo(m_Buffer, writer.Length, SocketFlags.None, endpoint);

                                m_OutstandingTokens.Add(endpoint, token);
                            }
                        }
                            break;
                        case SqpMessageType.QueryRequest:
                        {
                            uint token;
                            if (!m_OutstandingTokens.TryGetValue(endpoint, out token))
                            {
                                //Debug.Log("Failed to find token!");
                                return;
                            }

                            m_OutstandingTokens.Remove(endpoint);

                            ctx = default(DataStreamReader.Context);
                            var req = new QueryRequest();
                            req.FromStream(reader, ref ctx);

                            if ((SqpChunkType) req.RequestedChunks == SqpChunkType.ServerInfo)
                            {
                                var rsp = m_ServerInfo;
                                var writer = new DataStreamWriter(m_Buffer.Length, Unity.Collections.Allocator.Temp);
                                rsp.QueryHeader.Header.ChallengeId = token;

                                rsp.ToStream(ref writer);
                                writer.CopyTo(0, writer.Length, ref m_Buffer);
                                m_Socket.SendTo(m_Buffer, writer.Length, SocketFlags.None, endpoint);
                            }
                        }
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        uint GetNextToken()
        {
            uint thirtyBits = (uint) m_Random.Next(1 << 30);
            uint twoBits = (uint) m_Random.Next(1 << 2);
            return (thirtyBits << 2) | twoBits;
        }
    }
}