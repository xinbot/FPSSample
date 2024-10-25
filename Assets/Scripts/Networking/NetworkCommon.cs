using System;
using Networking.Compression;
using Unity.Networking.Transport;
using UnityEngine;

namespace Networking
{
    public static class NetworkSectionColor
    {
        public static readonly Color MapInfoColor = new Color(0.65f, 0.16f, 0.16f);

        public static readonly Color EventColor = new Color(1.0f, 0.84f, 0.0f);

        public static readonly Color UnknownColor = Color.black;

        public static readonly Color ClientInfoColor = Color.green;

        public static readonly Color HeaderColor = Color.white;

        public static readonly Color SnapshotHeaderColor = new Color(0.5f, 0.5f, 0.5f);

        public static readonly Color SnapShotSpawnsColor = new Color(0, 0.58f, 0);

        public static readonly Color SnapShotDeSpawnsColor = new Color(0.49f, 0, 0);

        public static readonly Color SnapShotChecksumColor = new Color(0.2f, 0.2f, 0.2f);
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public struct TransportEvent
    {
        public enum Type
        {
            Data,
            Connect,
            Disconnect
        }

        public Type EventType;
        public int ConnectionId;
        public byte[] Data;
        public int DataSize;
    }

    public interface INetworkTransport
    {
        int Connect(string ip, int port);

        void Disconnect(int connectionId);

        bool NextEvent(ref TransportEvent e);

        void SendData(int connectionId, byte[] data, int sendSize);

        string GetConnectionDescription(int connectionId);

        void Update();

        void Shutdown();
    }

    public interface INetworkCallbacks
    {
        void OnConnect(int clientId);

        void OnDisconnect(int clientId);

        void OnEvent(int clientId, NetworkEvent info);
    }

    [Flags]
    public enum NetworkMessage
    {
        // Shared messages
        Events = 1 << 0,

        // Server -> Client messages
        ClientInfo = 1 << 1,

        MapInfo = 1 << 2,

        Snapshot = 1 << 3,

        // Client -> Server messages
        ClientConfig = 1 << 1,

        Commands = 1 << 2,

        // Special flag used when package has been fragmented into several packages and needs reassembly
        Fragment = 1 << 7,
    }

    public static class NetworkConfig
    {
        [ConfigVar(Name = "net.stats", DefaultValue = "0", Description = "Show net statistics")]
        public static ConfigVar NetStats;

        [ConfigVar(Name = "net.printstats", DefaultValue = "0", Description = "Print stats to console every N frame")]
        public static ConfigVar NetPrintStats;

        [ConfigVar(Name = "net.debug", DefaultValue = "0", Description = "Dump lots of debug info about network")]
        public static ConfigVar NetDebug;

        [ConfigVar(Name = "server.port", DefaultValue = "7913", Description = "Port listened to by server")]
        public static ConfigVar ServerPort;

        [ConfigVar(Name = "server.sqp_port", DefaultValue = "0",
            Description = "Port used for server query protocol. server.port + 1 if not set")]
        public static ConfigVar ServerSqpPort;

        [ConfigVar(Name = "net.chokesendinterval", DefaultValue = "0.3",
            Description = "If connection is choked, send tiny keep alive packs at this interval")]
        public static ConfigVar NetChokeSendInterval;

        // Increase this when you make a change to the protocol
        public const uint ProtocolVersion = 4;

        public const int DefaultServerPort = 7913;

        // By default (if not specified) the SQP be at the server port + this number
        public static int SqpPortOffset = 10;

        public const int CommandServerQueueSize = 32;

        // Number of commands the client stores - also maximum number of predictive steps the client can take
        public const int CommandClientBufferSize = 32;

        public const int MAXFragments = 16;

        // 128 is just a random safety distance to MTU
        public const int PackageFragmentSize = NetworkParameterConstants.MTU - 128;

        public const int MAXPackageSize = MAXFragments * PackageFragmentSize;

        // Number of serialized snapshots kept on server. Each server tick generate a snapshot. 
        public const int SnapshotDeltaCacheSize = 128; // Number of snapshots to cache for deltas

        // Size of client ack buffers. These buffers are used to keep track of ack'ed baselines
        // from clients. Theoretically the 'right' size is snapshotDeltaCacheSize / (server.tickrate / client.updaterate)
        // e.g. 128 / (60 / 20) = 128 / 3, but since client.updaterate <= server.tickrate we use
        public const int ClientAckCacheSize = SnapshotDeltaCacheSize;

        public const int MAXEventDataSize = 512;
        public const int MAXCommandDataSize = 128;
        public const int MAXEntitySnapshotDataSize = 512;

        // The entire world snapshot has to fit in this number of bytes
        public const int MAXWorldSnapshotDataSize = 64 * 1024;

        public static readonly System.Text.UTF8Encoding Encoding = new System.Text.UTF8Encoding();

        public static readonly float[] EncoderPrecisionScales = new float[] {1.0f, 10.0f, 100.0f, 1000.0f};
        public static readonly float[] DecoderPrecisionScales = new float[] {1.0f, 0.1f, 0.01f, 0.001f};

        // compression //TODO: make this dynamic
        public const IOStreamType IOStreamType = Compression.IOStreamType.Huffman;

        public const int MAXFixedSchemaIds = 2;
        public const int MAXEventTypeSchemaIds = 8;
        public const int MAXEntityTypeSchemaIds = 40;

        public const int NetworkClientQueueCommandSchemaId = 0;
        public const int MapSchemaId = 1;
        public const int FirstEventTypeSchemaId = MAXFixedSchemaIds;
        public const int FirstEntitySchemaId = MAXFixedSchemaIds + MAXEventTypeSchemaIds;

        public const int MAXSchemaIds = MAXFixedSchemaIds + MAXEventTypeSchemaIds + MAXEntityTypeSchemaIds;

        public const int MAXFieldsPerSchema = 128;
        public const int MAXContextsPerField = 4;
        public const int MAXSkipContextsPerSchema = MAXFieldsPerSchema / 4;
        public const int MAXContextsPerSchema = MAXSkipContextsPerSchema + MAXFieldsPerSchema * MAXContextsPerField;

        public const int MiscContext = 0;
        public const int BaseSequenceContext = 1;
        public const int BaseSequence1Context = 2;
        public const int BaseSequence2Context = 3;
        public const int ServerTimeContext = 4;
        public const int SchemaCountContext = 5;
        public const int SchemaTypeIdContext = 6;
        public const int SpawnCountContext = 7;
        public const int IDContext = 8;
        public const int SpawnTypeIdContext = 9;
        public const int DeSpawnCountContext = 10;
        public const int UpdateCountContext = 11;
        public const int CommandTimeContext = 12;
        public const int EventCountContext = 13;
        public const int EventTypeIdContext = 14;
        public const int SkipContext = 15;

        public const int FirstSchemaContext = 16;

        public const int MAXContexts = FirstSchemaContext + MAXSchemaIds * MAXContextsPerSchema;
    }
}