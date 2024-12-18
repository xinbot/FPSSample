using System.Collections.Generic;
using System.Net;
using Networking.Compression;
using UnityEngine;
using UnityEngine.Profiling;

namespace Networking
{
    public interface ISnapshotConsumer
    {
        void ProcessEntityDeSpawn(int serverTime, List<int> deSpawn);
        void ProcessEntitySpawn(int serverTick, int id, ushort typeId);
        void ProcessEntityUpdate(int serverTime, int id, ref NetworkReader reader);
    }

    public interface INetworkClientCallbacks : INetworkCallbacks
    {
        void OnMapUpdate(ref NetworkReader data);
    }

    // Sent from client to server when changed
    public class ClientConfig
    {
        // max bytes / sec
        public int ServerUpdateRate;

        // requested tick / update
        public int ServerUpdateInterval;
    }

    public struct PackageSectionStats
    {
        public string SectionName;
        public int SectionStart;
        public int SectionLength;
        public Color Color;
    }

    public class Counters : NetworkConnectionCounters
    {
        // Total number of snapshots received
        public int SnapshotsIn;

        // Number of snapshots without a baseline
        public int FullSnapshotsIn;

        // Number of command messages sent
        public int CommandsOut;

        // incrementing when packageContentStats is filled
        public int PackageContentStatsPackageSequence;

        // Breakdown of bits spent in package
        public readonly List<PackageSectionStats>[] PackageContentStats = new List<PackageSectionStats>[64];

        private int _lastOffset;

        public void ClearSectionStats()
        {
            var idx = PackagesIn % PackageContentStats.Length;
            if (PackageContentStats[idx] == null)
            {
                PackageContentStats[idx] = new List<PackageSectionStats>();
            }
            else
            {
                PackageContentStats[idx].Clear();
            }

            _lastOffset = 0;
        }

        public void AddSectionStats(string name, int offset, Color color)
        {
            var idx = PackagesIn % PackageContentStats.Length;
            PackageContentStats[idx].Add(new PackageSectionStats
                {SectionName = name, SectionStart = _lastOffset, SectionLength = offset - _lastOffset, Color = color});
            _lastOffset = offset;
        }
    }

    public class NetworkClient
    {
        // Hack to allow thin clients to drop snapshots after having read initial bit
        public static bool DropSnapshots = false;

        [ConfigVar(Name = "client.debug", DefaultValue = "0",
            Description = "Enable debug printing of client handshake etc.", Flags = ConfigVar.Flags.None)]
        public static ConfigVar ClientDebug;

        [ConfigVar(Name = "client.blockin", DefaultValue = "0",
            Description = "Cut next N incoming network packges. -1 means forever.", Flags = ConfigVar.Flags.None)]
        public static ConfigVar ClientBlockIn;

        [ConfigVar(Name = "client.blockout", DefaultValue = "0", Description = "Cut all outgoing network traffic.",
            Flags = ConfigVar.Flags.None)]
        public static ConfigVar ClientBlockOut;

        [ConfigVar(Name = "client.verifyprotocol", DefaultValue = "1",
            Description = "Verify protocol match when connecting to server.", Flags = ConfigVar.Flags.None)]
        public static ConfigVar ClientVerifyProtocol;

        public readonly ClientConfig ClientConfig;

        public Counters counters
        {
            get { return _clientConnection != null ? _clientConnection.counters : null; }
        }

        public bool isConnected
        {
            get { return _clientConnection != null && _clientConnection.ConnectionState == ConnectionState.Connected; }
        }

        public ConnectionState connectionState
        {
            get { return _clientConnection != null ? _clientConnection.ConnectionState : ConnectionState.Disconnected; }
        }

        public int clientId
        {
            get { return _clientConnection != null ? _clientConnection.ClientId : -1; }
        }

        public int serverTime
        {
            get { return _clientConnection != null ? _clientConnection.ServerTime : -1; }
        }

        public int serverTickRate
        {
            get { return _clientConnection != null ? _clientConnection.ServerTickRate : 60; }
        }

        public int lastAcknowledgedCommandTime
        {
            get { return _clientConnection != null ? _clientConnection.LastAcknowledgedCommandTime : -1; }
        }

        public float serverSimTime
        {
            get { return _clientConnection != null ? _clientConnection.ServerSimTime : 0.0f; }
        }

        public int rtt
        {
            get { return _clientConnection != null ? _clientConnection.RTT : 0; }
        }

        public float timeSinceSnapshot
        {
            get
            {
                var deltaTime = NetworkUtils.Stopwatch.ElapsedMilliseconds - _clientConnection.SnapshotReceivedTime;
                return _clientConnection != null ? deltaTime : -1;
            }
        }

        public delegate void DataGenerator(ref NetworkWriter data);

        public delegate void MapUpdateProcessor(ref NetworkReader data);

        public delegate void EntitySpawnProcessor(int id, ushort typeId);

        public delegate void EntityDeSpawnProcessor(int id);

        public delegate void EntityUpdateProcessor(int id, ref NetworkReader data);

        private readonly Dictionary<ushort, NetworkEventType> _eventTypesOut =
            new Dictionary<ushort, NetworkEventType>();

        private readonly INetworkTransport _transport;
        private ClientConnection _clientConnection;

        public NetworkClient(INetworkTransport transport)
        {
            _transport = transport;
            ClientConfig = new ClientConfig();
        }

        public void Shutdown()
        {
            Disconnect();
        }

        internal void UpdateClientConfig()
        {
            Profiler.BeginSample("NetworkClient.UpdateClientConfig");

            ClientConfig.ServerUpdateRate = ClientGameLoop.ClientUpdateRate.IntValue;
            ClientConfig.ServerUpdateInterval = ClientGameLoop.ClientUpdateInterval.IntValue;
            if (_clientConnection != null)
            {
                _clientConnection.ClientConfigChanged();
            }

            Profiler.EndSample();
        }

        public bool Connect(string endpoint)
        {
            if (_clientConnection != null)
            {
                GameDebug.Log("Must be disconnected before reconnecting");
                return false;
            }

            IPAddress ipAddress;
            int port;
            if (!NetworkUtils.EndpointParse(endpoint, out ipAddress, out port, NetworkConfig.DefaultServerPort))
            {
                GameDebug.Log("Invalid endpoint: " + endpoint);
                return false;
            }

            var connectionId = _transport.Connect(ipAddress.ToString(), port);
            if (connectionId == -1)
            {
                GameDebug.Log("Connect failed");
                return false;
            }

            _clientConnection = new ClientConnection(connectionId, _transport, ClientConfig);

            return true;
        }

        public void Disconnect()
        {
            if (_clientConnection == null)
            {
                return;
            }

            // Force transport layer to disconnect
            _transport.Disconnect(_clientConnection.ConnectionId);

            // Note, we have to call OnDisconnect manually as disconnecting forcefully like this does not
            // generate an disconnect event from the transport layer
            OnDisconnect(_clientConnection.ConnectionId);
        }

        public void QueueCommand(int time, DataGenerator generator)
        {
            if (_clientConnection == null)
            {
                return;
            }

            _clientConnection.QueueCommand(time, generator);
        }

        public void QueueEvent(ushort typeId, bool reliable, NetworkEventGenerator generator)
        {
            if (_clientConnection == null)
            {
                return;
            }

            var e = NetworkEvent.Serialize(typeId, reliable, _eventTypesOut, generator);
            _clientConnection.QueueEvent(e);
            e.Release();
        }

        public void ProcessMapUpdate(INetworkClientCallbacks processor)
        {
            if (_clientConnection == null)
            {
                return;
            }

            _clientConnection.ProcessMapUpdate(processor);
        }

        public void Update(INetworkClientCallbacks clientNetworkConsumer, ISnapshotConsumer snapshotConsumer)
        {
            Profiler.BeginSample("NetworkClient.Update");

            _transport.Update();

            // Debug tracking of outstanding events
            if (NetworkConfig.NetDebug.IntValue > 1 && _clientConnection != null)
            {
                var outstandingPackages = _clientConnection.OutstandingPackages;
                for (var i = 0; i < outstandingPackages.Elements.Length; i++)
                {
                    if (outstandingPackages.Sequences[i] != -1 && outstandingPackages.Elements[i].Events.Count > 0)
                    {
                        GameDebug.Log("Outstanding Package: " + i + " (idx), " + outstandingPackages.Sequences[i] +
                                      " (seq), " + outstandingPackages.Elements[i].Events.Count + " (numevs), " +
                                      ((GameNetworkEvents.EventType) outstandingPackages.Elements[i].Events[0].Type
                                          .TypeId) + " (ev0)");
                    }
                }
            }

            var e = new TransportEvent();
            while (_transport.NextEvent(ref e))
            {
                switch (e.EventType)
                {
                    case TransportEvent.Type.Connect:
                        OnConnect(e.ConnectionId);
                        break;
                    case TransportEvent.Type.Disconnect:
                        OnDisconnect(e.ConnectionId);
                        break;
                    case TransportEvent.Type.Data:
                        OnData(e.ConnectionId, e.Data, e.DataSize, clientNetworkConsumer, snapshotConsumer);
                        break;
                }
            }

            if (_clientConnection != null)
            {
                _clientConnection.ProcessMapUpdate(clientNetworkConsumer);
            }

            Profiler.EndSample();
        }

        public void SendData()
        {
            if (_clientConnection == null || _clientConnection.ConnectionState == ConnectionState.Disconnected ||
                ClientBlockOut.IntValue > 0)
            {
                return;
            }

            Profiler.BeginSample("NetworkClient.SendData");

// unreachable code
#pragma warning disable 0162
            switch (NetworkConfig.IOStreamType)
            {
                case IOStreamType.Raw:
                    _clientConnection.SendPackage<RawOutputStream>();
                    break;
                case IOStreamType.Huffman:
                    _clientConnection.SendPackage<HuffmanOutputStream>();
                    break;
                default:
                    GameDebug.Assert(false);
            }
#pragma warning restore

            Profiler.EndSample();
        }

        private void OnConnect(int connectionId)
        {
            if (_clientConnection != null && _clientConnection.ConnectionId == connectionId)
            {
                GameDebug.Assert(connectionState == ConnectionState.Connecting);
            }
        }

        private void OnDisconnect(int connectionId)
        {
            if (_clientConnection == null || _clientConnection.ConnectionId != connectionId)
            {
                return;
            }

            if (_clientConnection.ConnectionState == ConnectionState.Connected)
            {
                GameDebug.Log("Disconnected from server");

                var outSequence = _clientConnection.OutSequence;
                var inSequence = _clientConnection.InSequence;
                var deltaTime = NetworkUtils.Stopwatch.ElapsedMilliseconds - _clientConnection.InSequenceTime;
                GameDebug.Log(
                    $"Last package sent : {outSequence}. Last package received {inSequence} {deltaTime} ms ago");
            }
            else if (_clientConnection.ConnectionState == ConnectionState.Connecting)
            {
                GameDebug.Log("Server never replied when trying to connect ... disconnecting");
            }

            _clientConnection.Reset();
            _clientConnection = null;
        }

        private void OnData(int connectionId, byte[] data, int size, INetworkClientCallbacks networkClientConsumer,
            ISnapshotConsumer snapshotConsumer)
        {
            // Block A number of incoming packets. -1 for all 
            if (ClientBlockIn.IntValue > 0)
            {
                ClientBlockIn.Value = (ClientBlockIn.IntValue - 1).ToString();
            }

            if (ClientBlockIn.IntValue != 0)
            {
                return;
            }

            // Should these be asserts?
            if (_clientConnection == null || _clientConnection.ConnectionId != connectionId)
            {
                return;
            }

// unreached code
#pragma warning disable 0162
            switch (NetworkConfig.IOStreamType)
            {
                case IOStreamType.Raw:
                {
                    _clientConnection.ReadPackage<RawInputStream>(data, size, _clientConnection.CompressionModel,
                        networkClientConsumer, snapshotConsumer);
                    break;
                }
                case IOStreamType.Huffman:
                {
                    _clientConnection.ReadPackage<HuffmanInputStream>(data, size, _clientConnection.CompressionModel,
                        networkClientConsumer, snapshotConsumer);
                    break;
                }
                default:
                    GameDebug.Assert(false);
                    break;
            }
#pragma warning restore
        }

        private class ClientConnection : NetworkConnection<Counters, ClientPackageInfo>
        {
            // Time we received the last snapshot
            public long SnapshotReceivedTime;

            // Server simulation time (actual time spent doing simulation regardless of tick rate)
            public float ServerSimTime;

            private readonly ClientConfig _clientConfig;

            private byte[] _modelData = new byte[32];

            private readonly uint[] _tempSnapshotBuffer = new uint[NetworkConfig.MAXEntitySnapshotDataSize];

            private readonly byte[] _zeroFieldsChanged = new byte[(NetworkConfig.MAXFieldsPerSchema + 7) / 8];

            public ClientConnection(int connectionId, INetworkTransport transport, ClientConfig clientConfig) : base(
                connectionId, transport)
            {
                _clientConfig = clientConfig;
            }

            public override void Reset()
            {
                base.Reset();
                ServerTime = 0;
                _entities.Clear();
                _spawns.Clear();
                _deSpawns.Clear();
                _updates.Clear();
            }

            public void ClientConfigChanged()
            {
                _sendClientConfig = true;
            }

            public unsafe void QueueCommand(int time, DataGenerator generator)
            {
                var generateSchema = _commandSchema == null;
                if (generateSchema)
                {
                    _commandSchema = new NetworkSchema(NetworkConfig.NetworkClientQueueCommandSchemaId);
                }

                var info = _commandsOut.Acquire(++_commandSequence);
                info.Time = time;
                fixed (uint* buf = info.Data)
                {
                    var writer = new NetworkWriter(buf, info.Data.Length, _commandSchema, generateSchema);
                    generator(ref writer);
                    writer.Flush();
                }
            }

            public unsafe void ProcessMapUpdate(INetworkClientCallbacks loop)
            {
                if (_mapInfo.MapSequence > 0 && !_mapInfo.Processed)
                {
                    fixed (uint* data = _mapInfo.Data)
                    {
                        var reader = new NetworkReader(data, _mapInfo.Schema);
                        loop.OnMapUpdate(ref reader);
                        _mapInfo.Processed = true;
                    }
                }
            }

            public void ReadPackage<TInputStream>(byte[] packageData, int packageSize,
                NetworkCompressionModel model, INetworkClientCallbacks networkClientConsumer,
                ISnapshotConsumer snapshotConsumer) where TInputStream : struct, IInputStream
            {
                counters.BytesIn += packageSize;

                var packageSequence = ProcessPackageHeader(packageData, packageSize, out var content,
                    out var assembledData,
                    out var assembledSize, out var headerSize);

                // Reset stats
                counters.ClearSectionStats();
                counters.AddSectionStats("header", headerSize * 8, NetworkSectionColor.HeaderColor);

                // The package was dropped (duplicate or too old) or if it was a fragment not yet assembled, bail out here
                if (packageSequence == 0)
                {
                    return;
                }

                var input = default(TInputStream);
                input.Initialize(model, assembledData, headerSize);

                if ((content & NetworkMessage.ClientInfo) != 0)
                {
                    ReadClientInfo(ref input);
                }

                counters.AddSectionStats("clientInfo", input.GetBitPosition2(), NetworkSectionColor.ClientInfoColor);

                if ((content & NetworkMessage.MapInfo) != 0)
                {
                    ReadMapInfo(ref input);
                }

                counters.AddSectionStats("mapInfo", input.GetBitPosition2(), NetworkSectionColor.MapInfoColor);

                /*
                 * The package was received out of order but older than the last map reset, 
                 * so we ignore the remaining data
                 */
                if (_mapInfo.AckSequence == 0 || packageSequence < _mapInfo.AckSequence)
                {
                    return;
                }

                if ((content & NetworkMessage.Snapshot) != 0)
                {
                    ReadSnapshot(packageSequence, ref input, snapshotConsumer);

                    /*
                     * Make sure the callback actually picked up the snapshot data. It is important that
                     * every snapshot gets processed by the game so that the spawns, deSpawns and updates lists
                     * does not end up containing stuff from different snapshots
                     */
                    GameDebug.Assert(_spawns.Count == 0 && _deSpawns.Count == 0 && _updates.Count == 0,
                        "Game did not consume snapshots");
                }

                // We have to skip this if we dropped snapshot as we will then be in the middle of the input stream
                if ((content & NetworkMessage.Events) != 0 && !DropSnapshots)
                {
                    ReadEvents(ref input, networkClientConsumer);
                }

                counters.AddSectionStats("events", input.GetBitPosition2(), NetworkSectionColor.EventColor);

                counters.AddSectionStats("unknown", assembledSize * 8, NetworkSectionColor.UnknownColor);

                counters.PackageContentStatsPackageSequence = packageSequence;
            }

            public void SendPackage<TOutputStream>() where TOutputStream : struct, IOutputStream
            {
                /*
                 * We don't start sending updates before we have received at
                 * least one content package from the server
                 */
                var rawOutputStream = new BitOutputStream(PackageBuffer);
                if (InSequence == 0 || !CanSendPackage(ref rawOutputStream))
                {
                    return;
                }

                /*
                 * Only if there is anything to send
                 * TODO (petera) should we send empty packages at a low frequency?
                 */
                if (_sendClientConfig == false && _commandSequence > 0 && _commandSequence <= _lastSentCommandSeq &&
                    EventsOut.Count == 0)
                {
                    return;
                }

                ClientPackageInfo info;
                BeginSendPackage(ref rawOutputStream, out info);

                var endOfHeaderPos = rawOutputStream.Align();
                var output = default(TOutputStream);
                output.Initialize(NetworkCompressionModel.DefaultModel, PackageBuffer, endOfHeaderPos, null);

                if (_sendClientConfig)
                {
                    WriteClientConfig(ref output);
                }

                if (_commandSequence > 0)
                {
                    _lastSentCommandSeq = _commandSequence;
                    WriteCommands(info, ref output);
                }

                WriteEvents(info, ref output);
                var compressedSize = output.Flush();
                rawOutputStream.SkipBytes(compressedSize);

                CompleteSendPackage(info, ref rawOutputStream);
            }

            private void ReadClientInfo<TInputStream>(ref TInputStream input) where TInputStream : IInputStream
            {
                var newClientId = (int) input.ReadRawBits(8);
                // TODO (petera) remove this from here. This should only be handshake code. tickrate updated by other means like configvar
                ServerTickRate = (int) input.ReadRawBits(8);
                uint serverProtocol = input.ReadRawBits(8);

                int modelSize = (int) input.ReadRawBits(16);
                if (modelSize > _modelData.Length)
                {
                    _modelData = new byte[modelSize];
                }

                for (int i = 0; i < modelSize; i++)
                {
                    _modelData[i] = (byte) input.ReadRawBits(8);
                }

                // Server sends client info our way repeatedly until we have ack'ed it.
                // We ignore it if we already got it. We have to read the data in order
                // to skip it as the packet may contain other things after this
                if (ConnectionState == ConnectionState.Connected)
                {
                    return;
                }

                uint ourProtocol = NetworkConfig.ProtocolVersion;
                GameDebug.Log($"Client protocol id: {ourProtocol}");
                GameDebug.Log($"Server protocol id: {serverProtocol}");
                if (ourProtocol != serverProtocol)
                {
                    if (ClientVerifyProtocol.IntValue > 0)
                    {
                        GameDebug.LogError($"Protocol mismatch. Server is: {serverProtocol} and we are: {ourProtocol}");
                        ConnectionState = ConnectionState.Disconnected;
                        return;
                    }

                    GameDebug.Log("Ignoring protocol difference client.verify protocol is 0");
                }

                GameDebug.Assert(ClientId == -1 || newClientId == ClientId,
                    "Repeated client info didn't match existing client id");

                CompressionModel = new NetworkCompressionModel(_modelData);

                ClientId = newClientId;

                ConnectionState = ConnectionState.Connected;

                if (ClientDebug.IntValue > 0)
                {
                    GameDebug.Log($"ReadClientInfo: clientId {newClientId} serverTickRate {ServerTickRate}");
                }
            }

            private void ReadMapInfo<TInputStream>(ref TInputStream input) where TInputStream : IInputStream
            {
                var mapSequence = (ushort) input.ReadRawBits(16);
                var schemaIncluded = input.ReadRawBits(1) != 0;
                if (schemaIncluded)
                {
                    _mapInfo.Schema = NetworkSchema.ReadSchema(ref input); // might override previous definition
                }

                if (mapSequence > _mapInfo.MapSequence)
                {
                    _mapInfo.MapSequence = mapSequence;
                    _mapInfo.AckSequence = InSequence;
                    _mapInfo.Processed = false;
                    NetworkSchema.CopyFieldsToBuffer(_mapInfo.Schema, ref input, _mapInfo.Data);
                    Reset();
                }
                else
                {
                    NetworkSchema.SkipFields(_mapInfo.Schema, ref input);
                }
            }

            private unsafe void ReadSnapshot<TInputStream>(int sequence, ref TInputStream input,
                ISnapshotConsumer consumer)
                where TInputStream : IInputStream
            {
                counters.SnapshotsIn++;

                // Snapshot may be delta compressed against one or more baselines
                // Baselines are indicated by sequence number of the package it was in
                var haveBaseline = input.ReadRawBits(1) == 1;
                var baseSequence = input.ReadPackedIntDelta(sequence - 1, NetworkConfig.BaseSequenceContext);

                bool enableNetworkPrediction = input.ReadRawBits(1) != 0;
                bool enableHashing = input.ReadRawBits(1) != 0;

                int baseSequence1 = 0;
                int baseSequence2 = 0;
                if (enableNetworkPrediction)
                {
                    baseSequence1 = input.ReadPackedIntDelta(baseSequence - 1, NetworkConfig.BaseSequence1Context);
                    baseSequence2 = input.ReadPackedIntDelta(baseSequence1 - 1, NetworkConfig.BaseSequence2Context);
                }

                if (ClientDebug.IntValue > 2)
                {
                    string networkPredictionMsg;
                    if (enableNetworkPrediction)
                    {
                        networkPredictionMsg = haveBaseline
                            ? $"Snap [BL] ({sequence}) {baseSequence} - {baseSequence1} - {baseSequence2}"
                            : $"Snap [  ] ({sequence}) {baseSequence} - {baseSequence1} - {baseSequence2}";
                    }
                    else
                    {
                        networkPredictionMsg = haveBaseline
                            ? $"Snap [BL] ({sequence}) {baseSequence}"
                            : $"Snap [  ] ({sequence}) {baseSequence}";
                    }

                    GameDebug.Log(networkPredictionMsg);
                }

                if (!haveBaseline)
                {
                    counters.FullSnapshotsIn++;
                }

                GameDebug.Assert(!haveBaseline ||
                                 (sequence > baseSequence &&
                                  sequence - baseSequence < NetworkConfig.SnapshotDeltaCacheSize),
                    "Attempting snapshot encoding with invalid baseline: {0}:{1}", sequence, baseSequence);

                var snapshotInfo = _snapshots.Acquire(sequence);
                snapshotInfo.ServerTime = input.ReadPackedIntDelta(
                    haveBaseline ? _snapshots[baseSequence].ServerTime : 0,
                    NetworkConfig.ServerTimeContext);

                var temp = (int) input.ReadRawBits(8);
                ServerSimTime = temp * 0.1f;

                // Only update time if received in-order.. 
                // TODO consider dropping out of order snapshots
                // TODO detecting out-of-order on pack sequences
                if (snapshotInfo.ServerTime > ServerTime)
                {
                    ServerTime = snapshotInfo.ServerTime;
                    SnapshotReceivedTime = NetworkUtils.Stopwatch.ElapsedMilliseconds;
                }
                else
                {
                    GameDebug.Log(
                        $"NetworkClient. Dropping out of order snapshot. Server time:{ServerTime} snapshot time:{snapshotInfo.ServerTime}");
                }

                counters.AddSectionStats("snapShotHeader", input.GetBitPosition2(),
                    NetworkSectionColor.SnapshotHeaderColor);

                // Used by thin client that wants to very cheaply just do minimal handling of snapshots
                if (DropSnapshots)
                {
                    return;
                }

                // Read schemas
                var schemaCount = input.ReadPackedUInt(NetworkConfig.SchemaCountContext);
                for (var schemaIndex = 0; schemaIndex < schemaCount; ++schemaIndex)
                {
                    var typeId = (ushort) input.ReadPackedUInt(NetworkConfig.SchemaTypeIdContext);

                    var entityType = new EntityTypeInfo {TypeId = typeId, Schema = NetworkSchema.ReadSchema(ref input)};
                    counters.AddSectionStats("snapShotSchemas", input.GetBitPosition2(),
                        new Color(0.0f, (schemaIndex & 1) == 1 ? 0.5f : 1.0f, 1.0f));

                    entityType.Baseline = new uint[NetworkConfig.MAXEntitySnapshotDataSize];
                    NetworkSchema.CopyFieldsToBuffer(entityType.Schema, ref input, entityType.Baseline);

                    if (!_entityTypes.ContainsKey(typeId))
                    {
                        _entityTypes.Add(typeId, entityType);
                    }

                    counters.AddSectionStats("snapShotSchemas", input.GetBitPosition2(),
                        new Color(1.0f, (schemaIndex & 1) == 1 ? 0.5f : 1.0f, 1.0f));
                }

                // Remove any deSpawning entities that belong to older base sequences
                for (var i = 0; i < _entities.Count; i++)
                {
                    var entity = _entities[i];
                    if (entity.Type == null)
                    {
                        continue;
                    }

                    if (entity.DeSpawnSequence > 0 && entity.DeSpawnSequence <= baseSequence)
                    {
                        entity.Reset();
                    }
                }

                // Read new spawns
                _tempSpawnList.Clear();
                var previousId = 1;
                var spawnCount = input.ReadPackedUInt(NetworkConfig.SpawnCountContext);
                for (var spawnIndex = 0; spawnIndex < spawnCount; ++spawnIndex)
                {
                    var id = input.ReadPackedIntDelta(previousId, NetworkConfig.IDContext);
                    previousId = id;

                    // Register the entity
                    // TODO: use another encoding
                    var typeId = (ushort) input.ReadPackedUInt(NetworkConfig.SpawnTypeIdContext);
                    GameDebug.Assert(_entityTypes.ContainsKey(typeId), $"Spawn request with unknown type id {typeId}");

                    var fieldMask = (byte) input.ReadRawBits(8);

                    // TODO (petera) need an max entity id for safety
                    while (id >= _entities.Count)
                    {
                        _entities.Add(new EntityInfo());
                    }

                    // Incoming spawn of different type than what we have for this id, so immediately nuke
                    // the one we have to make room for the incoming
                    if (_entities[id].Type != null && _entities[id].Type.TypeId != typeId)
                    {
                        // This should only ever happen in case of no baseline as normally the server will
                        // not reuse an id before all clients have acknowledged its deSpawn.
                        GameDebug.Assert(haveBaseline == false,
                            "Spawning entity but we already have with different type?");
                        GameDebug.Log("REPLACING old entity: " + id + " because snapshot gave us new type for this id");
                        _deSpawns.Add(id);
                        _entities[id].Reset();
                    }

                    // We can receive spawn information in several snapshots before our ack
                    // has reached the server. Only pass on spawn to game layer once
                    if (_entities[id].Type == null)
                    {
                        var entity = _entities[id];
                        entity.Type = _entityTypes[typeId];
                        entity.FieldMask = fieldMask;
                        _spawns.Add(id);
                    }

                    _tempSpawnList.Add(id);
                }

                counters.AddSectionStats("snapShotSpawns", input.GetBitPosition2(),
                    NetworkSectionColor.SnapShotSpawnsColor);

                // Read deSpawns
                var deSpawnCount = input.ReadPackedUInt(NetworkConfig.DeSpawnCountContext);

                // If we have no baseline, we need to clear all entities that are not being spawned
                if (!haveBaseline)
                {
                    GameDebug.Assert(deSpawnCount == 0, "There should not be any deSpawns in a non-baseline snapshot");
                    for (var i = 0; i < _entities.Count; ++i)
                    {
                        var entity = _entities[i];
                        if (entity.Type == null)
                        {
                            continue;
                        }

                        if (_tempSpawnList.Contains(i))
                        {
                            continue;
                        }

                        GameDebug.Log("NO BL SO PRUNING Stale entity: " + i);
                        _deSpawns.Add(i);
                        entity.Reset();
                    }
                }

                for (var deSpawnIndex = 0; deSpawnIndex < deSpawnCount; ++deSpawnIndex)
                {
                    var id = input.ReadPackedIntDelta(previousId, NetworkConfig.IDContext);
                    previousId = id;

                    // we may see deSpawns many times, only handle if we still have the entity
                    GameDebug.Assert(id < _entities.Count,
                        $"Getting deSpawn for id {id} but we only know about entities up to {_entities.Count}.");
                    if (_entities[id].Type == null)
                    {
                        continue;
                    }

                    var entity = _entities[id];

                    // Already in the process of being deSpawned. This happens with same-snapshot spawn / deSpawn cases
                    if (entity.DeSpawnSequence > 0)
                    {
                        continue;
                    }

                    // If we are spawning and deSpawning in same snapshot, delay actual deletion of
                    // entity as we need it around to be able to read the update part of the snapshot
                    if (_tempSpawnList.Contains(id))
                    {
                        // keep until baseSequence >= deSpawnSequence
                        entity.DeSpawnSequence = sequence;
                    }
                    else
                    {
                        // otherwise remove right away; no further updates coming, not even in this snap
                        entity.Reset();
                    }

                    // Add to deSpawns list so we can request deSpawn from game later
                    GameDebug.Assert(!_deSpawns.Contains(id), "Double deSpawn in same snapshot? {0}", id);
                    _deSpawns.Add(id);
                }

                counters.AddSectionStats("snapShotDeSpawns", input.GetBitPosition2(),
                    NetworkSectionColor.SnapShotDeSpawnsColor);

                // Predict all active entities
                for (var id = 0; id < _entities.Count; id++)
                {
                    var entity = _entities[id];
                    if (entity.Type == null)
                    {
                        continue;
                    }

                    // NOTE : As long as the server haven't gotten the spawn acked, it will keep sending
                    // delta relative to 0, so we need to check if the entity was in the spawn list to determine
                    // if the delta is relative to the last update or not

                    int baseline0Time = 0;
                    uint[] baseline0 = entity.Type.Baseline;
                    GameDebug.Assert(baseline0 != null,
                        $"Unable to find schema baseline for type {entity.Type.TypeId}");

                    if (haveBaseline && !_tempSpawnList.Contains(id))
                    {
                        baseline0 = entity.Baselines.FindMax(baseSequence);
                        GameDebug.Assert(baseline0 != null, "Unable to find baseline for seq {0} for id {1}",
                            baseSequence,
                            id);
                        baseline0Time = _snapshots[baseSequence].ServerTime;
                    }

                    if (enableNetworkPrediction)
                    {
                        uint numBaselines = 1; // 1 because either we have schema baseline or we have a real baseline
                        int baseline1Time = 0;
                        int baseline2Time = 0;

                        uint[] baseline1 = null;
                        uint[] baseline2 = null;
                        if (baseSequence1 != baseSequence)
                        {
                            baseline1 = entity.Baselines.FindMax(baseSequence1);
                            if (baseline1 != null)
                            {
                                numBaselines = 2;
                                baseline1Time = _snapshots[baseSequence1].ServerTime;
                            }

                            if (baseSequence2 != baseSequence1)
                            {
                                baseline2 = entity.Baselines.FindMax(baseSequence2);
                                if (baseline2 != null)
                                {
                                    numBaselines = 3;
                                    baseline2Time = _snapshots[baseSequence2].ServerTime;
                                }
                            }
                        }

                        // TODO (petera) are these clears needed?
                        for (int i = 0; i < entity.FieldsChangedPrediction.Length; ++i)
                        {
                            entity.FieldsChangedPrediction[i] = 0;
                        }

                        for (int i = 0; i < NetworkConfig.MAXEntitySnapshotDataSize; i++)
                        {
                            entity.Prediction[i] = 0;
                        }

                        fixed (uint* prediction = entity.Prediction, baseline0P = baseline0, baseline1P =
                            baseline1, baseline2P = baseline2)
                        {
                            NetworkPrediction.PredictSnapshot(prediction,
                                entity.FieldsChangedPrediction, entity.Type.Schema, numBaselines,
                                (uint) baseline0Time, baseline0P,
                                (uint) baseline1Time, baseline1P,
                                (uint) baseline2Time, baseline2P,
                                (uint) snapshotInfo.ServerTime, entity.FieldMask);
                        }
                    }
                    else
                    {
                        for (var i = 0; i < entity.FieldsChangedPrediction.Length; ++i)
                        {
                            entity.FieldsChangedPrediction[i] = 0;
                        }

                        for (int i = 0, c = entity.Type.Schema.GetByteSize() / 4; i < c; ++i)
                        {
                            entity.Prediction[i] = baseline0[i];
                        }
                    }
                }

                // Read updates
                var updateCount = input.ReadPackedUInt(NetworkConfig.UpdateCountContext);
                for (var updateIndex = 0; updateIndex < updateCount; ++updateIndex)
                {
                    var id = input.ReadPackedIntDelta(previousId, NetworkConfig.IDContext);
                    previousId = id;

                    var entity = _entities[id];

                    uint hash = 0;
                    // Copy prediction to temp buffer as we now overwrite info.prediction with fully unpacked
                    // state by applying incoming delta to prediction.
                    for (int i = 0, c = entity.Type.Schema.GetByteSize() / 4; i < c; ++i)
                    {
                        _tempSnapshotBuffer[i] = entity.Prediction[i];
                    }

                    DeltaReader.Read(ref input, entity.Type.Schema, entity.Prediction, _tempSnapshotBuffer,
                        entity.FieldsChangedPrediction, entity.FieldMask, ref hash);

                    if (enableHashing)
                    {
                        uint hashCheck = input.ReadRawBits(32);
                        if (hash != hashCheck)
                        {
                            GameDebug.Log($"Hash check fail for entity {id}");

                            if (enableNetworkPrediction)
                            {
                                string msg;
                                if (haveBaseline)
                                {
                                    msg =
                                        $"Snapshot ({snapshotInfo.ServerTime}) Snap [BL] {baseSequence}-{baseSequence1}-{baseSequence2}. " +
                                        $"Schema : {schemaCount} Spawns : {spawnCount} DeSpawns : {deSpawnCount} Upd : {updateCount}";
                                }
                                else
                                {
                                    msg =
                                        $"Snapshot ({snapshotInfo.ServerTime}) Snap [  ] {baseSequence}-{baseSequence1}-{baseSequence2}. " +
                                        $"Schema : {schemaCount} Spawns : {spawnCount} DeSpawns : {deSpawnCount} Upd : {updateCount}";
                                }

                                GameDebug.Assert(false, msg);
                            }
                            else
                            {
                                string msg;
                                if (haveBaseline)
                                {
                                    msg =
                                        $"Snapshot ({snapshotInfo.ServerTime}) Snap [BL] {baseSequence}. Schema : {schemaCount} " +
                                        $"Spawns : {spawnCount} DeSpawns : {deSpawnCount} Upd : {updateCount}";
                                }
                                else
                                {
                                    msg =
                                        $"Snapshot ({snapshotInfo.ServerTime}) Snap [  ] {baseSequence}. Schema : {schemaCount} " +
                                        $"Spawns : {spawnCount} DeSpawns : {deSpawnCount} Upd : {updateCount}";
                                }

                                GameDebug.Assert(false, msg);
                            }
                        }
                    }
                }

                var predictionMsgName = enableNetworkPrediction ? "snapShotUpdatesPredict" : "snapShotUpdatesNoPredict";
                counters.AddSectionStats(predictionMsgName, input.GetBitPosition2(),
                    haveBaseline ? NetworkSectionColor.SnapShotUpdateColor : Color.cyan);

                uint numEntities = 0;

                for (int id = 0; id < _entities.Count; id++)
                {
                    var entity = _entities[id];
                    if (entity.Type == null)
                    {
                        continue;
                    }

                    // Skip deSpawned that have not also been spawned in this snapshot
                    if (entity.DeSpawnSequence > 0 && !_spawns.Contains(id))
                    {
                        continue;
                    }

                    // If just spawned or if new snapshot is different from the last we deserialized,
                    // we need to deserialize. Otherwise just ignore; no reason to deserialize the same values again
                    int schemaSize = entity.Type.Schema.GetByteSize();
                    if (entity.Baselines.GetSize() == 0 ||
                        NetworkUtils.MemCmp(entity.Prediction, 0, entity.LastUpdate, 0, schemaSize) != 0)
                    {
                        var data = entity.Baselines.Insert(sequence);
                        for (int i = 0; i < schemaSize / 4; ++i)
                        {
                            data[i] = entity.Prediction[i];
                        }

                        if (sequence > entity.LastUpdateSequence)
                        {
                            if (!_updates.Contains(id))
                            {
                                _updates.Add(id);
                            }

                            for (int i = 0; i < schemaSize / 4; ++i)
                            {
                                entity.LastUpdate[i] = entity.Prediction[i];
                            }

                            entity.LastUpdateSequence = sequence;
                        }
                    }

                    if (enableHashing && entity.DeSpawnSequence == 0)
                    {
                        NetworkUtils.SimpleHash(entity.Prediction, schemaSize);
                        numEntities++;
                    }
                }

                if (ClientDebug.IntValue > 1)
                {
                    if (ClientDebug.IntValue > 2 || 
                        spawnCount > 0 || deSpawnCount > 0 || schemaCount > 0 || !haveBaseline)
                    {
                        string entityIds = "";
                        for (var i = 0; i < _entities.Count; i++)
                        {
                            var e = _entities[i];
                            entityIds += e.Type == null
                                ? ",-"
                                : (e.DeSpawnSequence > 0 ? "," + i + "(" + e.DeSpawnSequence + ")" : "," + i);
                        }

                        string deSpawnIds = string.Join(",", _deSpawns);
                        string spawnIds = string.Join(",", _tempSpawnList);
                        string updateIds = string.Join(",", _updates);

                        if (enableNetworkPrediction)
                        {
                            GameDebug.Log(("SEQ:" + snapshotInfo.ServerTime + ":" + sequence) +
                                          (haveBaseline ? "Snap [BL]" : "Snap [  ]") + "  " + baseSequence + " - " +
                                          baseSequence1 + " - " + baseSequence2 + ". Schema: " + schemaCount +
                                          " Spawns: " +
                                          spawnCount + "(" + spawnIds + ") DeSpawns: " + deSpawnCount + "(" +
                                          deSpawnIds +
                                          ") Upd: " + updateCount + "(" + updateIds + ")  Entities:" + _entities.Count +
                                          " EntityIds:" + entityIds);
                        }
                        else
                        {
                            GameDebug.Log(("SEQ:" + snapshotInfo.ServerTime + ":" + sequence) +
                                          (haveBaseline ? "Snap [BL]" : "Snap [  ]") + "  " + baseSequence +
                                          ". Schema: " +
                                          schemaCount + " Spawns: " + spawnCount + "(" + spawnIds + ") DeSpawns: " +
                                          deSpawnCount + "(" + deSpawnIds + ") Upd: " + updateCount + "(" + updateIds +
                                          ")  Entities:" + _entities.Count + " EntityIds:" + entityIds);
                        }
                    }
                }

                if (enableHashing)
                {
                    uint numEntitiesCheck = input.ReadRawBits(32);
                    if (numEntitiesCheck != numEntities)
                    {
                        GameDebug.Log("SYNC PROBLEM: server num entities: " + numEntitiesCheck + " us:" + numEntities);
                        GameDebug.Assert(false);
                    }
                }

                counters.AddSectionStats("snapShotChecksum", input.GetBitPosition2(),
                    NetworkSectionColor.SnapShotChecksumColor);

                // Snapshot Reading Done. Now pass on resulting spawns / deSpawns to the snapshot consumer
                Profiler.BeginSample("ProcessSnapshot");

                consumer.ProcessEntityDeSpawn(ServerTime, _deSpawns);
                _deSpawns.Clear();

                foreach (var id in _spawns)
                {
                    GameDebug.Assert(_entities[id].Type != null, $"Processing spawn of id {id} but type is null");
                    consumer.ProcessEntitySpawn(ServerTime, id, _entities[id].Type.TypeId);
                }

                _spawns.Clear();

                foreach (var id in _updates)
                {
                    var info = _entities[id];
                    GameDebug.Assert(info.Type != null, $"Processing update of id {id} but type is null");
                    fixed (uint* data = info.LastUpdate)
                    {
                        var reader = new NetworkReader(data, info.Type.Schema);
                        consumer.ProcessEntityUpdate(ServerTime, id, ref reader);
                    }
                }

                _updates.Clear();

                Profiler.EndSample();
            }

            private void WriteClientConfig<TOutputStream>(ref TOutputStream output) where TOutputStream : IOutputStream
            {
                AddMessageContentFlag(NetworkMessage.ClientConfig);

                output.WriteRawBits((uint) _clientConfig.ServerUpdateRate, 32);
                output.WriteRawBits((uint) _clientConfig.ServerUpdateInterval, 16);
                _sendClientConfig = false;

                if (ClientDebug.IntValue > 0)
                {
                    var serverUpdateRate = _clientConfig.ServerUpdateRate;
                    var serverUpdateInterval = _clientConfig.ServerUpdateInterval;
                    GameDebug.Log(
                        $"WriteClientConfig: serverUpdateRate {serverUpdateRate} serverUpdateInterval {serverUpdateInterval}");
                }
            }

            private unsafe void WriteCommands<TOutputStream>(ClientPackageInfo packageInfo, ref TOutputStream output)
                where TOutputStream : IOutputStream
            {
                AddMessageContentFlag(NetworkMessage.Commands);

                counters.CommandsOut++;

                var includeSchema = _commandSequenceAck == 0;
                output.WriteRawBits(includeSchema ? 1U : 0, 1);
                if (includeSchema)
                {
                    NetworkSchema.WriteSchema(_commandSchema, ref output);
                }

                var sequence = _commandSequence;
                output.WriteRawBits(Sequence.ToUInt16(_commandSequence), 16);

                packageInfo.CommandSequence = _commandSequence;
                packageInfo.CommandTime = _commandsOut[_commandSequence].Time;

                CommandInfo previous = _defaultCommandInfo;
                while (_commandsOut.TryGetValue(sequence, out var command))
                {
                    // 1 bit to tell there is a command 
                    output.WriteRawBits(1, 1);
                    output.WritePackedIntDelta(command.Time, previous.Time, NetworkConfig.CommandTimeContext);
                    uint hash = 0;
                    fixed (uint* data = command.Data, baseline = previous.Data)
                    {
                        DeltaWriter.Write(ref output, _commandSchema, data, baseline, _zeroFieldsChanged, 0, ref hash);
                    }

                    previous = command;
                    --sequence;
                }

                output.WriteRawBits(0, 1);
            }

            protected override void NotifyDelivered(int sequence, ClientPackageInfo info, bool madeIt)
            {
                base.NotifyDelivered(sequence, info, madeIt);
                if (madeIt)
                {
                    if (info.CommandSequence > _commandSequenceAck)
                    {
                        _commandSequenceAck = info.CommandSequence;
                        LastAcknowledgedCommandTime = info.CommandTime;
                    }
                }
                else
                {
                    // Resend user config if the package was lost
                    if ((info.Content & NetworkMessage.ClientConfig) != 0)
                    {
                        _sendClientConfig = true;
                    }
                }
            }

            private class MapInfo
            {
                // Map reset was processed by game
                public bool Processed;

                // map identifier to discard duplicate messages
                public ushort MapSequence;

                // package sequence the map was acked in (discard packages before this)
                public int AckSequence;

                // Schema for the map info
                public NetworkSchema Schema;

                // Game specific map info payload
                public readonly uint[] Data = new uint[256];
            }

            private class SnapshotInfo
            {
                public int ServerTime;
            }

            private class CommandInfo
            {
                public int Time;
                public readonly uint[] Data = new uint[512];
            }

            private class EntityTypeInfo
            {
                public ushort TypeId;
                public NetworkSchema Schema;
                public uint[] Baseline;
            }

            private class EntityInfo
            {
                public EntityTypeInfo Type;
                public byte FieldMask;
                public int LastUpdateSequence;
                public int DeSpawnSequence;

                public readonly uint[] LastUpdate = new uint[NetworkConfig.MAXEntitySnapshotDataSize];
                public readonly uint[] Prediction = new uint[NetworkConfig.MAXEntitySnapshotDataSize];
                public readonly byte[] FieldsChangedPrediction = new byte[(NetworkConfig.MAXFieldsPerSchema + 7) / 8];

                public readonly SparseSequenceBuffer Baselines = new SparseSequenceBuffer(
                    NetworkConfig.SnapshotDeltaCacheSize, NetworkConfig.MAXEntitySnapshotDataSize);

                public void Reset()
                {
                    Type = null;
                    LastUpdateSequence = 0;
                    DeSpawnSequence = 0;
                    FieldMask = 0;
                    // TODO(petera) needed?
                    for (var i = 0; i < LastUpdate.Length; i++)
                    {
                        LastUpdate[i] = 0;
                    }

                    Baselines.Clear();
                }
            }

            public ConnectionState ConnectionState = ConnectionState.Connecting;
            public int ClientId = -1;
            public int ServerTickRate;
            public int ServerTime;
            public int LastAcknowledgedCommandTime;
            public NetworkCompressionModel CompressionModel;

            private int _commandSequence;
            private int _commandSequenceAck;
            private int _lastSentCommandSeq;
            private bool _sendClientConfig = true;

            private readonly SequenceBuffer<CommandInfo> _commandsOut =
                new SequenceBuffer<CommandInfo>(3, () => new CommandInfo());

            private readonly SequenceBuffer<SnapshotInfo> _snapshots =
                new SequenceBuffer<SnapshotInfo>(NetworkConfig.SnapshotDeltaCacheSize, () => new SnapshotInfo());

            private readonly List<int> _deSpawns = new List<int>();
            private readonly List<int> _spawns = new List<int>();
            private readonly List<int> _updates = new List<int>();
            private readonly List<int> _tempSpawnList = new List<int>();
            private readonly List<EntityInfo> _entities = new List<EntityInfo>();

            private readonly Dictionary<ushort, EntityTypeInfo> _entityTypes = new Dictionary<ushort, EntityTypeInfo>();

            private readonly CommandInfo _defaultCommandInfo = new CommandInfo();
            private readonly MapInfo _mapInfo = new MapInfo();
            private NetworkSchema _commandSchema;
        }
    }
}