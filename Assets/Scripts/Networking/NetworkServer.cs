using System;
using System.Collections.Generic;
using System.IO;
using Networking.Compression;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Networking
{
    public interface ISnapshotGenerator
    {
        int worldTick { get; }
        void GenerateEntitySnapshot(int entityId, ref NetworkWriter writer);
        string GenerateEntityName(int entityId);
    }

    public interface IClientCommandProcessor
    {
        void ProcessCommand(int connectionId, int tick, ref NetworkReader data);
    }

    public unsafe class MapInfo
    {
        // The server frame the map was initialized
        public int ServerInitSequence;

        // Unique sequence number for the map (to deal with redundant mapinfo messages)
        public ushort MapId;

        // Schema for the map info
        public NetworkSchema Schema;

        // Game specific payload
        public readonly uint* Data = (uint*) UnsafeUtility.Malloc(1024, UnsafeUtility.AlignOf<uint>(),
            Unity.Collections.Allocator.Persistent);
    }

    public unsafe class EntityTypeInfo
    {
        public string Name;
        public ushort TypeId;
        public int CreatedSequence;
        public uint* Baseline;
        public int StatsCount;
        public int StatsBits;

        public NetworkSchema Schema;
    }

    // Holds a information about a snapshot for an entity.
    // Each entity has a circular history of these
    public unsafe class EntitySnapshotInfo
    {
        // pointer into WorldSnapshot.data block (see below)
        public uint* Start;

        // length of data in words
        public int Length;
    }

    // Each tick a WorldSnapshot is generated. The data buffer contains serialized data
    // from all serializable entities
    public unsafe class WorldSnapshot
    {
        // server tick for this snapshot
        public int ServerTime;

        // length of data in data field
        public int Length;

        public uint* Data;
    }

    public unsafe class EntityInfo
    {
        public ushort TypeId;
        public int PredictingClientId = -1;

        public int SpawnSequence;
        public int DeSpawnSequence;
        public int UpdateSequence;

        public readonly SequenceBuffer<EntitySnapshotInfo> Snapshots;

        // NOTE: used in WriteSnapshot but invalid outside that function
        public uint* Prediction;
        public readonly byte[] FieldsChangedPrediction = new byte[(NetworkConfig.MAXFieldsPerSchema + 7) / 8];

        public EntityInfo()
        {
            Snapshots = new SequenceBuffer<EntitySnapshotInfo>(NetworkConfig.SnapshotDeltaCacheSize,
                () => new EntitySnapshotInfo());
        }

        public void Reset()
        {
            TypeId = 0;
            SpawnSequence = 0;
            DeSpawnSequence = 0;
            UpdateSequence = 0;
            Snapshots.Clear();
            for (var i = 0; i < FieldsChangedPrediction.Length; i++)
            {
                FieldsChangedPrediction[i] = 0;
            }

            PredictingClientId = -1;
        }

        // On server the field mask of an entity is different depending on what client we are sending to
        // Flags:
        //    1 : receiving client is predicting
        //    2 : receiving client is not predicting
        public byte GetFieldMask(int connectionId)
        {
            if (PredictingClientId == -1)
            {
                return 0;
            }

            byte mask = 0;
            if (PredictingClientId == connectionId)
            {
                mask |= 0x1; // 0001
            }
            else
            {
                mask |= 0x2; // 0010
            }

            return mask;
        }
    }

    public class ServerPackageInfo : PackageInfo
    {
        // Used to map package sequences back to server sequence
        public int ServerSequence;
        public int ServerTime;

        public override void Reset()
        {
            base.Reset();
            ServerSequence = 0;
        }
    }

    public unsafe class NetworkServer
    {
        [ConfigVar(Name = "server.debug", DefaultValue = "0",
            Description = "Enable debug printing of server handshake etc.", Flags = ConfigVar.Flags.None)]
        public static ConfigVar ServerDebug;

        [ConfigVar(Name = "server.debugentityids", DefaultValue = "0",
            Description = "Enable debug printing entity id recycling.", Flags = ConfigVar.Flags.None)]
        public static ConfigVar ServerDebugEntityIds;

        [ConfigVar(Name = "server.dump_client_streams", DefaultValue = "0",
            Description = "Store client streams raw in files on server")]
        public static ConfigVar DumpClientStreams;

        [ConfigVar(Name = "server.print_senddata_time", DefaultValue = "0",
            Description = "Print average server time spent in senddata")]
        public static ConfigVar PrintSenddataTime;

        [ConfigVar(Name = "server.network_prediction", DefaultValue = "1",
            Description = "Predict snapshots data to improve compression and minimize bandwidth")]
        public static ConfigVar NetworkPrediction;

        [ConfigVar(Name = "server.debug_hashing", DefaultValue = "1",
            Description = "Send entity hashes to clients for debugging.")]
        public static ConfigVar DebugHashing;

        // Each client needs to receive this on connect and when any of the values changes
        public class ServerInfo
        {
            public int ServerTickRate;
            public NetworkCompressionModel CompressionModel = NetworkCompressionModel.DefaultModel;
        }

        public class Counters : NetworkConnectionCounters
        {
            public int SnapshotsOut;
            public int CommandsIn;
        }

        public int StatsSnapshotData;
        public int StatsGeneratedEntitySnapshots;
        public int StatsSentUpdates;
        public int StatsProcessedOutgoing;
        public int StatsSentOutgoing;
        public int StatsGeneratedSnapshotSize;
        public readonly ServerInfo ServerInformation;

        // Entity count of entire snapshot
        private uint _lastEntityCount;

        // The time it took to simulate the last update
        private float _serverSimTime;

        private int _serverSequence = 1;
        private int _predictionIndex;
        private long _accumulateSendDataTicks;
        private long _lastUpdateTick;

        private readonly WorldSnapshot[] _snapshots;
        private readonly uint* _prediction;

        private readonly List<int> _tempSpawnList = new List<int>();
        private readonly List<int> _tempDeSpawnList = new List<int>();
        private readonly List<int> _tempUpdateList = new List<int>();
        private readonly List<int> _freeEntities = new List<int>();

        private readonly List<Counters> _counters = new List<Counters>();
        private readonly List<EntityInfo> _entities = new List<EntityInfo>();
        private readonly List<EntityTypeInfo> _tempTypeList = new List<EntityTypeInfo>();

        private readonly MapInfo _mapInfo = new MapInfo();
        private readonly INetworkTransport _transport;
        private NetworkCompressionCapture _networkCompressionCapture;

        private readonly Dictionary<int, ServerConnection> _connections = new Dictionary<int, ServerConnection>();
        private readonly Dictionary<ushort, EntityTypeInfo> _entityTypes = new Dictionary<ushort, EntityTypeInfo>();

        private readonly Dictionary<ushort, NetworkEventType> _eventTypesOut =
            new Dictionary<ushort, NetworkEventType>();

        /// <summary>
        /// The game time on the server
        /// </summary>
        public int serverTime { get; private set; }

        // Used for stats
        public float serverSimTime => _serverSimTime;

        public int numEntities => _entities.Count - _freeEntities.Count;

        // TODO (petera) remove this. 
        // We need to split ClientInfo (tick rate etc.) from the connection
        // handshake (protocol version etc.)
        public void UpdateClientInfo()
        {
            ServerInformation.ServerTickRate = Game.ServerTickRate.IntValue;

            foreach (var pair in _connections)
            {
                pair.Value.ClientInfoAcked = false;
            }
        }

        public List<Counters> GetCounters()
        {
            // Gather counters from connections
            _counters.Clear();
            foreach (var pair in _connections)
            {
                _counters.Add(pair.Value.counters);
            }

            return _counters;
        }

        public delegate void DataGenerator(ref NetworkWriter writer);

        public delegate void SnapshotGenerator(int entityId, ref NetworkWriter writer);

        public delegate void CommandProcessor(int time, ref NetworkReader reader);

        public delegate void EventProcessor(ushort typeId, ref NetworkReader data);

        public delegate string EntityTypeNameGenerator(int typeId);

        public NetworkServer(INetworkTransport transport)
        {
            _transport = transport;
            ServerInformation = new ServerInfo();

            // Allocate array to hold world snapshots
            _snapshots = new WorldSnapshot[NetworkConfig.SnapshotDeltaCacheSize];
            for (var i = 0; i < _snapshots.Length; ++i)
            {
                _snapshots[i] = new WorldSnapshot();
                _snapshots[i].Data = (uint*) UnsafeUtility.Malloc(NetworkConfig.MAXWorldSnapshotDataSize,
                    UnsafeUtility.AlignOf<UInt32>(), Unity.Collections.Allocator.Persistent);
            }

            // Allocate scratch
            // *) buffer to hold predictions.
            // *) This is overwritten every time a snapshot is being written to a specific client
            _prediction = (uint*) UnsafeUtility.Malloc(NetworkConfig.MAXWorldSnapshotDataSize,
                UnsafeUtility.AlignOf<UInt32>(), Unity.Collections.Allocator.Persistent);
        }

        public void Shutdown()
        {
            UnsafeUtility.Free(_prediction, Unity.Collections.Allocator.Persistent);
            for (var i = 0; i < _snapshots.Length; ++i)
            {
                UnsafeUtility.Free(_snapshots[i].Data, Unity.Collections.Allocator.Persistent);
            }
        }

        public void InitializeMap(DataGenerator generator)
        {
            // Generate schema the first time we set map info
            bool generateSchema = false;
            if (_mapInfo.Schema == null)
            {
                _mapInfo.Schema = new NetworkSchema(NetworkConfig.MapSchemaId);
                generateSchema = true;
            }

            // Update map info
            var writer = new NetworkWriter(_mapInfo.Data, 1024, _mapInfo.Schema, generateSchema);
            generator(ref writer);
            writer.Flush();

            _mapInfo.ServerInitSequence = _serverSequence;
            ++_mapInfo.MapId;

            // Reset map and connection state
            serverTime = 0;
            _entities.Clear();
            _freeEntities.Clear();
            foreach (var pair in _connections)
            {
                pair.Value.Reset();
            }
        }

        public void MapReady(int clientId)
        {
            GameDebug.Log("Client " + clientId + " is ready");
            GameDebug.Assert(_connections.ContainsKey(clientId), "Got MapReady from unknown client?");
            _connections[clientId].MapReady = true;
        }

        // Reserve scene entities with sequential id's starting from 0
        public void ReserveSceneEntities(int count)
        {
            GameDebug.Assert(_entities.Count == 0,
                "ReserveSceneEntities: Only allowed before other entities have been registered");
            for (var i = 0; i < count; i++)
            {
                _entities.Add(new EntityInfo());
            }
        }

        // Currently predictingClient can only be set on an entity at time of creation
        // in the future it should be something you can change if you for example enter/leave
        // a vehicle. There are subtle but tricky replication issues when predicting 'ownership' changes, though...
        public int RegisterEntity(int id, ushort typeId, int predictingClientId)
        {
            Profiler.BeginSample("NetworkServer.RegisterEntity()");
            EntityInfo entityInfo;
            int freeCount = _freeEntities.Count;

            if (id >= 0)
            {
                GameDebug.Assert(_entities[id].SpawnSequence == 0,
                    "RegisterEntity: Trying to reuse an id that is used by a scene entity");
                entityInfo = _entities[id];
            }
            else if (freeCount > 0)
            {
                id = _freeEntities[freeCount - 1];
                _freeEntities.RemoveAt(freeCount - 1);
                entityInfo = _entities[id];
                entityInfo.Reset();
            }
            else
            {
                entityInfo = new EntityInfo();
                _entities.Add(entityInfo);
                id = _entities.Count - 1;
            }

            entityInfo.TypeId = typeId;
            entityInfo.PredictingClientId = predictingClientId;
            entityInfo.SpawnSequence = _serverSequence + 1; // NOTE : Associate the spawn with the next snapshot

            if (ServerDebugEntityIds.IntValue > 1)
            {
                GameDebug.Log("Registered entity id: " + id);
            }

            Profiler.EndSample();
            return id;
        }

        public void UnregisterEntity(int id)
        {
            Profiler.BeginSample("NetworkServer.UnregisterEntity()");
            _entities[id].DeSpawnSequence = _serverSequence + 1;
            Profiler.EndSample();
        }

        public void HandleClientCommands(int tick, IClientCommandProcessor processor)
        {
            foreach (var connection in _connections)
            {
                connection.Value.ProcessCommands(tick, processor);
            }
        }

        public void QueueEvent(int clientId, ushort typeId, bool reliable, NetworkEventGenerator generator)
        {
            ServerConnection connection;
            if (_connections.TryGetValue(clientId, out connection))
            {
                var e = NetworkEvent.Serialize(typeId, reliable, _eventTypesOut, generator);
                connection.QueueEvent(e);
                e.Release();
            }
        }

        public void QueueEventBroadcast(ushort typeId, bool reliable, NetworkEventGenerator generator)
        {
            var info = NetworkEvent.Serialize(typeId, reliable, _eventTypesOut, generator);
            foreach (var pair in _connections)
            {
                pair.Value.QueueEvent(info);
            }

            info.Release();
        }

        public void GenerateSnapshot(ISnapshotGenerator snapshotGenerator, float simTime)
        {
            var time = snapshotGenerator.worldTick;
            // Time should always flow forward
            GameDebug.Assert(time > serverTime);
            // Initialize map before generating snapshot
            GameDebug.Assert(_mapInfo.MapId > 0);

            ++_serverSequence;

            // We currently keep entities around until every client has ack'ed the snapshot with the deSpawn
            // Then we delete them from our list and recycle the id
            // TODO: we do not need this anymore?

            // Find oldest (smallest seq no) acked snapshot.
            var minClientAck = int.MaxValue;
            foreach (var pair in _connections)
            {
                var c = pair.Value;
                // If a client is so far behind that we have to send non-baseline updates to it
                // there is no reason to keep deSpawned entities around for this client's sake
                // -2 because we want 3 baselines!
                if (_serverSequence - c.MAXSnapshotAck >= NetworkConfig.SnapshotDeltaCacheSize - 2)
                {
                    continue;
                }

                var acked = c.MAXSnapshotAck;
                if (acked < minClientAck)
                {
                    minClientAck = acked;
                }
            }

            // Recycle deSpawned entities that have been acked by all
            for (var i = 0; i < _entities.Count; i++)
            {
                var entity = _entities[i];
                if (entity.DeSpawnSequence > 0 && entity.DeSpawnSequence < minClientAck)
                {
                    if (ServerDebugEntityIds.IntValue > 1)
                    {
                        GameDebug.Log(
                            $"Recycling entity id: {i} because deSpawned in {entity.DeSpawnSequence} and minAck is now {minClientAck}.");
                    }

                    entity.Reset();
                    _freeEntities.Add(i);
                }
            }

            serverTime = time;
            _serverSimTime = simTime;
            _lastEntityCount = 0;

            // Grab world snapshot from circular buffer
            var worldSnapshot = _snapshots[_serverSequence % _snapshots.Length];
            worldSnapshot.ServerTime = time;
            worldSnapshot.Length = 0;

            // Run through all the registered network entities and serialize the snapshot
            for (var id = 0; id < _entities.Count; id++)
            {
                var entity = _entities[id];

                // Skip freed
                if (entity.SpawnSequence == 0)
                {
                    continue;
                }

                // Skip entities that are deSpawned
                if (entity.DeSpawnSequence > 0)
                {
                    continue;
                }

                // If we are here and are deSpawned, we must be a deSpawn / spawn in same frame situation
                GameDebug.Assert(entity.DeSpawnSequence == 0 || entity.DeSpawnSequence == entity.SpawnSequence,
                    "Snapshotting entity that was deleted in the past?");
                GameDebug.Assert(entity.DeSpawnSequence == 0 || entity.DeSpawnSequence == _serverSequence, "WUT");

                // For now we generate the entity type info the first time we generate a snapshot
                // for the particular entity as a more lightweight approach rather than introducing
                // a full schema system where the game code must generate and register the type
                bool generateSchema = false;
                if (!_entityTypes.TryGetValue(entity.TypeId, out var entityTypeInfo))
                {
                    entityTypeInfo = new EntityTypeInfo
                    {
                        Name = snapshotGenerator.GenerateEntityName(id),
                        TypeId = entity.TypeId,
                        CreatedSequence = _serverSequence,
                        Schema = new NetworkSchema(entity.TypeId + NetworkConfig.FirstEntitySchemaId)
                    };
                    _entityTypes.Add(entity.TypeId, entityTypeInfo);
                    generateSchema = true;
                }

                // Generate entity snapshot
                var snapshotInfo = entity.Snapshots.Acquire(_serverSequence);
                snapshotInfo.Start = worldSnapshot.Data + worldSnapshot.Length;

                var bufferSize = NetworkConfig.MAXWorldSnapshotDataSize / 4 - worldSnapshot.Length;
                var writer = new NetworkWriter(snapshotInfo.Start, bufferSize, entityTypeInfo.Schema, generateSchema);
                snapshotGenerator.GenerateEntitySnapshot(id, ref writer);
                writer.Flush();
                snapshotInfo.Length = writer.GetLength();

                worldSnapshot.Length += snapshotInfo.Length;

                if (entity.DeSpawnSequence == 0)
                {
                    _lastEntityCount++;
                }

                GameDebug.Assert(snapshotInfo.Length > 0,
                    "Tried to generate a entity snapshot but no data was delivered by generator?");

                if (generateSchema)
                {
                    GameDebug.Assert(entityTypeInfo.Baseline == null, "Generating schema twice?");
                    // First time a type/schema is encountered, we clone the serialized data and
                    // use it as the type-baseline
                    entityTypeInfo.Baseline = (uint*) UnsafeUtility.Malloc(snapshotInfo.Length * 4,
                        UnsafeUtility.AlignOf<UInt32>(), Unity.Collections.Allocator.Persistent);
                    for (int i = 0; i < snapshotInfo.Length; i++)
                    {
                        entityTypeInfo.Baseline[i] = *(snapshotInfo.Start + i);
                    }
                }

                // Check if it is different from the previous generated snapshot
                var dirty = !entity.Snapshots.Exists(_serverSequence - 1);
                if (!dirty)
                {
                    var previousSnapshot = entity.Snapshots[_serverSequence - 1];
                    // TODO (petera) how could length differ???
                    if (previousSnapshot.Length != snapshotInfo.Length ||
                        UnsafeUtility.MemCmp(previousSnapshot.Start, snapshotInfo.Start, snapshotInfo.Length) != 0)
                    {
                        dirty = true;
                    }
                }

                if (dirty)
                {
                    entity.UpdateSequence = _serverSequence;
                }

                StatsGeneratedEntitySnapshots++;
                StatsSnapshotData += snapshotInfo.Length;
            }

            StatsGeneratedSnapshotSize += worldSnapshot.Length * 4;
        }

        public void Update(INetworkCallbacks loop)
        {
            _transport.Update();

            TransportEvent e = new TransportEvent();
            while (_transport.NextEvent(ref e))
            {
                switch (e.EventType)
                {
                    case TransportEvent.Type.Connect:
                        OnConnect(e.ConnectionId, loop);
                        break;
                    case TransportEvent.Type.Disconnect:
                        OnDisconnect(e.ConnectionId, loop);
                        break;
                    case TransportEvent.Type.Data:
                        OnData(e.ConnectionId, e.Data, e.DataSize, loop);
                        break;
                }
            }
        }

        public void SendData()
        {
            Profiler.BeginSample("NetworkServer.SendData");

            long startTick = 0;
            if (PrintSenddataTime.IntValue > 0)
            {
                startTick = Game.clock.ElapsedTicks;
            }

            foreach (var pair in _connections)
            {
// unreachable code
#pragma warning disable 0162
                switch (NetworkConfig.IOStreamType)
                {
                    case IOStreamType.Raw:
                        pair.Value.SendPackage<RawOutputStream>(_networkCompressionCapture);
                        break;
                    case IOStreamType.Huffman:
                        pair.Value.SendPackage<HuffmanOutputStream>(_networkCompressionCapture);
                        break;
                    default:
                        GameDebug.Assert(false);
                }
#pragma warning restore
            }

            if (PrintSenddataTime.IntValue > 0)
            {
                long stopTick = Game.clock.ElapsedTicks;
                long ticksPerSecond = System.Diagnostics.Stopwatch.Frequency;
                _accumulateSendDataTicks += stopTick - startTick;

                if (stopTick >= _lastUpdateTick + ticksPerSecond)
                {
                    GameDebug.Log("SendData Time per second: " + _accumulateSendDataTicks * 1000.0 / ticksPerSecond);
                    _accumulateSendDataTicks = 0;
                    _lastUpdateTick = Game.clock.ElapsedTicks;
                }
            }

            Profiler.EndSample();
        }

        public Dictionary<int, ServerConnection> GetConnections()
        {
            return _connections;
        }

        public Dictionary<ushort, EntityTypeInfo> GetEntityTypes()
        {
            return _entityTypes;
        }

        private void OnConnect(int connectionId, INetworkCallbacks callback)
        {
            GameDebug.Assert(!_connections.ContainsKey(connectionId));

            if (_connections.Count >= ServerGameLoop.serverMaxClients.IntValue)
            {
                GameDebug.Log($"Refusing incoming connection {connectionId} due to server.maxclients");
                _transport.Disconnect(connectionId);
                return;
            }

            GameDebug.Log(
                $"Incoming connection: #{connectionId} from: {_transport.GetConnectionDescription(connectionId)}");

            var connection = new ServerConnection(this, connectionId, _transport);
            _connections.Add(connectionId, connection);

            callback.OnConnect(connectionId);
        }

        private void OnDisconnect(int connectionId, INetworkCallbacks callback)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                callback.OnDisconnect(connectionId);
                GameDebug.Log($"Client {connectionId} disconnected");

                var outSequence = connection.OutSequence;
                var inSequence = connection.InSequence;
                var time = NetworkUtils.Stopwatch.ElapsedMilliseconds - connection.InSequenceTime;
                GameDebug.Log($"Last package sent : {outSequence} . Last package received {inSequence} {time} ms ago");

                connection.Shutdown();
                _connections.Remove(connectionId);
            }
        }

        private void OnData(int connectionId, byte[] data, int size, INetworkCallbacks callback)
        {
// unreachable code
#pragma warning disable 0162
            switch (NetworkConfig.IOStreamType)
            {
                case IOStreamType.Raw:
                {
                    _connections[connectionId]
                        .ReadPackage<RawInputStream>(data, size, NetworkCompressionModel.DefaultModel, callback);
                    break;
                }
                case IOStreamType.Huffman:
                {
                    _connections[connectionId]
                        .ReadPackage<HuffmanInputStream>(data, size, NetworkCompressionModel.DefaultModel, callback);
                    break;
                }
                default:
                    GameDebug.Assert(false);
            }
#pragma warning restore
        }

        public void StartNetworkProfile()
        {
            _networkCompressionCapture =
                new NetworkCompressionCapture(NetworkConfig.MAXContexts, ServerInformation.CompressionModel);
        }

        public void EndNetworkProfile(string filepath)
        {
            byte[] model = _networkCompressionCapture.AnalyzeAndGenerateModel();
            if (filepath != null)
            {
                File.WriteAllBytes(filepath, model);
            }

            _networkCompressionCapture = null;
        }

        public class ServerConnection : NetworkConnection<Counters, ServerPackageInfo>
        {
            private class CommandInfo
            {
                public int Time;
                public readonly uint[] Data = new uint[NetworkConfig.MAXCommandDataSize];
            }

            public int MAXSnapshotAck;

            // Connection handshake
            public bool ClientInfoAcked;
            public bool MapReady;

            private bool _mapAcked;
            private bool _mapSchemaAcked;

            private int _snapshotInterval;
            private int _maxBps;
            private int _snapshotServerLastWritten;
            private int _snapshotPackageBaseline;
            private int _maxSnapshotTime;
            private int _joinSequence;
            private int _commandSequenceIn;
            private int _commandSequenceProcessed;
            private int _lastClearedAck;

            private double _nextOutPackageTime;

            // flags for ack of individual snapshots indexed by client sequence
            private readonly bool[] _snapshotAcks = new bool[NetworkConfig.ClientAckCacheSize];

            // corresponding server baseline no for each client seq
            private readonly int[] _snapshotSeqs = new int[NetworkConfig.ClientAckCacheSize];

            private readonly byte[] _zeroFieldsChanged = new byte[(NetworkConfig.MAXFieldsPerSchema + 7) / 8];

            private readonly NetworkServer _server;
            private NetworkSchema _commandSchema;
            private readonly CommandInfo _defaultCommandInfo = new CommandInfo();

            private readonly SequenceBuffer<CommandInfo> _commandsIn =
                new SequenceBuffer<CommandInfo>(NetworkConfig.CommandServerQueueSize, () => new CommandInfo());

            public ServerConnection(NetworkServer server, int connectionId, INetworkTransport transport)
                : base(connectionId, transport)
            {
                _server = server;

                if (DumpClientStreams.IntValue > 0)
                {
                    var name = $"client_stream_{connectionId}.bin";
                    DebugSendStreamWriter = new BinaryWriter(File.Open(name, FileMode.Create));
                    GameDebug.Log($"Storing client data stream in {name}");
                }

                // update rate overridden by client info right after connect. Start at 1, i.e. update every tick, to allow fast handshake
                _snapshotInterval = 1;
                _maxBps = 0;
                _nextOutPackageTime = 0;
            }

            public new void Reset()
            {
                base.Reset();

                _mapAcked = false;
                MapReady = false;

                MAXSnapshotAck = 0;
                _maxSnapshotTime = 0;
                _lastClearedAck = 0;
                _snapshotPackageBaseline = 0;

                _snapshotSeqs.Clear();
                _snapshotAcks.Clear();
            }

            public void ProcessCommands(int maxTime, IClientCommandProcessor processor)
            {
                // Check for time jumps backward in the command stream and reset the queue in case
                // we find one. (This will happen if the client determines that it has gotten too
                // far ahead and recalculate the client time.)

                // TODO : We should be able to do this in a smarter way
                for (var sequence = _commandSequenceProcessed + 1; sequence <= _commandSequenceIn; ++sequence)
                {
                    CommandInfo previous;
                    CommandInfo current;

                    _commandsIn.TryGetValue(sequence, out current);
                    _commandsIn.TryGetValue(sequence - 1, out previous);

                    if (current != null && previous != null && current.Time <= previous.Time)
                    {
                        _commandSequenceProcessed = sequence - 1;
                    }
                }

                for (var sequence = _commandSequenceProcessed + 1; sequence <= _commandSequenceIn; ++sequence)
                {
                    CommandInfo info;
                    if (_commandsIn.TryGetValue(sequence, out info))
                    {
                        if (info.Time <= maxTime)
                        {
                            fixed (uint* data = info.Data)
                            {
                                var reader = new NetworkReader(data, _commandSchema);
                                processor.ProcessCommand(ConnectionId, info.Time, ref reader);
                            }

                            _commandSequenceProcessed = sequence;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }

            public void ReadPackage<TInputStream>(byte[] packageData, int packageSize, NetworkCompressionModel model,
                INetworkCallbacks loop) where TInputStream : struct, IInputStream
            {
                counters.BytesIn += packageSize;

                NetworkMessage content;
                byte[] assembledData;
                int assembledSize;
                int headerSize;
                var packageSequence = ProcessPackageHeader(packageData, packageSize, out content, out assembledData,
                    out assembledSize, out headerSize);

                // Bail out if the package was bad (duplicate or too old)
                if (packageSequence == 0)
                {
                    return;
                }

                var input = default(TInputStream);
                input.Initialize(model, assembledData, headerSize);

                if ((content & NetworkMessage.ClientConfig) != 0)
                {
                    ReadClientConfig(ref input);
                }

                if ((content & NetworkMessage.Commands) != 0)
                {
                    ReadCommands(ref input);
                }

                if ((content & NetworkMessage.Events) != 0)
                {
                    ReadEvents(ref input, loop);
                }
            }

            public void SendPackage<TOutputStream>(NetworkCompressionCapture networkCompressionCapture)
                where TOutputStream : struct, IOutputStream
            {
                // Check if we can and should send new package
                var rawOutputStream = new BitOutputStream(PackageBuffer);

                var canSendPackage = CanSendPackage(ref rawOutputStream);
                if (!canSendPackage)
                {
                    return;
                }

                // Distribute clients evenly according to their with snapshotInterval > 1
                // TODO: This kind of assumes same update interval by all ....
                if ((_server._serverSequence + ConnectionId) % _snapshotInterval != 0)
                {
                    return;
                }

                // Respect max bps rate cap
                if (Game.FrameTime < _nextOutPackageTime)
                {
                    return;
                }

                ServerPackageInfo packageInfo;
                BeginSendPackage(ref rawOutputStream, out packageInfo);

                int endOfHeaderPos = rawOutputStream.Align();
                var output = default(TOutputStream); // new TOutputStream();  Due to bug new generates garbage here
                output.Initialize(_server.ServerInformation.CompressionModel, PackageBuffer, endOfHeaderPos,
                    networkCompressionCapture);
                
                // We store the server sequence in the package info to be able to map back to 
                // the snapshot baseline when we get delivery notification for the package and 
                // similarly for the time as we send the server time as a delta relative to 
                // the last acknowledged server time

                packageInfo.ServerSequence = _server._serverSequence; // the server snapshot sequence
                packageInfo.ServerTime = _server.serverTime; // Server time (could be ticks or could be ms)

                // The ifs below are in essence the 'connection handshake' logic.
                if (!ClientInfoAcked)
                {
                    // Keep sending client info until it is acked
                    WriteClientInfo(ref output);
                }
                else if (!_mapAcked)
                {
                    if (_server._mapInfo.ServerInitSequence > 0)
                    {
                        // Keep sending map info until it is acked
                        WriteMapInfo(ref output);
                    }
                }
                else
                {
                    // Send snapshot, buf only
                    //   if client has declared itself ready
                    //   if we have not already sent for this tick (because we need to be able to map a snapshot 
                    //     sequence to a package sequence we cannot send the same snapshot multiple times).
                    if (MapReady && _server._serverSequence > _snapshotServerLastWritten)
                    {
                        WriteSnapshot(ref output);
                    }

                    WriteEvents(packageInfo, ref output);
                }

                // TODO (petera) this is not nice. We need one structure only to keep track of outstanding packages / acks
                // We have to ensure all sequence numbers that have been used by packages sent elsewhere from here
                // gets cleared as 'not ack'ed' so they don't show up later as snapshots we think the client has
                for (int i = _lastClearedAck + 1; i <= OutSequence; ++i)
                {
                    _snapshotAcks[i % NetworkConfig.ClientAckCacheSize] = false;
                }

                _lastClearedAck = OutSequence;

                int compressedSize = output.Flush();
                rawOutputStream.SkipBytes(compressedSize);

                var messageSize = CompleteSendPackage(packageInfo, ref rawOutputStream);

                // Decide when next package can go out
                if (_maxBps > 0)
                {
                    double timeLimitBps = messageSize * 1.0f / _maxBps;
                    if (timeLimitBps > _snapshotInterval / Game.ServerTickRate.FloatValue)
                    {
                        GameDebug.Log("SERVER: Choked by BPS sending " + messageSize);
                        _nextOutPackageTime = Game.FrameTime + timeLimitBps;
                    }
                }
            }

            private void WriteClientInfo<TOutputStream>(ref TOutputStream output) where TOutputStream : IOutputStream
            {
                AddMessageContentFlag(NetworkMessage.ClientInfo);
                output.WriteRawBits((uint) ConnectionId, 8);
                output.WriteRawBits((uint) _server.ServerInformation.ServerTickRate, 8);
                output.WriteRawBits(NetworkConfig.ProtocolVersion, 8);

                byte[] modelData = _server.ServerInformation.CompressionModel.ModelData;
                output.WriteRawBits((uint) modelData.Length, 16);
                for (int i = 0; i < modelData.Length; i++)
                {
                    output.WriteRawBits(modelData[i], 8);
                }

                if (ServerDebug.IntValue > 0)
                {
                    GameDebug.Log(
                        $"WriteClientInfo: connectionId {ConnectionId} serverTickRate {_server.ServerInformation.ServerTickRate}");
                }
            }

            private void WriteMapInfo<TOutputStream>(ref TOutputStream output) where TOutputStream : IOutputStream
            {
                AddMessageContentFlag(NetworkMessage.MapInfo);

                output.WriteRawBits(_server._mapInfo.MapId, 16);

                // Write schema if client haven't acked it
                output.WriteRawBits(_mapSchemaAcked ? 0 : 1U, 1);
                if (!_mapSchemaAcked)
                {
                    NetworkSchema.WriteSchema(_server._mapInfo.Schema, ref output);
                }

                // Write map data
                NetworkSchema.CopyFieldsFromBuffer(_server._mapInfo.Schema, _server._mapInfo.Data, ref output);
            }

            private void WriteSnapshot<TOutputStream>(ref TOutputStream output) where TOutputStream : IOutputStream
            {
                _server.StatsSentUpdates++;

                Profiler.BeginSample("NetworkServer.WriteSnapshot()");
                // joinSequence is the *first* snapshot that could have received.
                if (_joinSequence == 0)
                {
                    _joinSequence = _server._serverSequence;
                    if (ServerDebug.IntValue > 0)
                    {
                        GameDebug.Log("Client " + ConnectionId + " got first snapshot at " + _joinSequence);
                    }
                }

                AddMessageContentFlag(NetworkMessage.Snapshot);
                counters.SnapshotsOut++;

                bool enableNetworkPrediction = NetworkPrediction.IntValue != 0;
                bool enableHashing = DebugHashing.IntValue != 0;


                // Check if the baseline from the client is too old. We keep N number of snapshots on the server 
                // so if the client baseline is older than that we cannot generate the snapshot. Furthermore, we require
                // the client to keep the last N updates for any entity, so even though the client might have much older
                // baselines for some entities we cannot guarantee it. 
                // TODO : Can we make this simpler?
                var haveBaseline = MAXSnapshotAck != 0;
                // -2 because we want 3 baselines!
                if (_server._serverSequence - MAXSnapshotAck >= NetworkConfig.SnapshotDeltaCacheSize - 2)
                {
                    if (ServerDebug.IntValue > 0)
                    {
                        GameDebug.Log("ServerSequence ahead of latest ack'ed snapshot by more than cache size. " +
                                      (haveBaseline ? "nobaseline" : "baseline"));
                    }

                    haveBaseline = false;
                }

                var baseline = haveBaseline ? MAXSnapshotAck : 0;

                int snapshot0Baseline = baseline;
                int snapshot1Baseline = baseline;
                int snapshot2Baseline = baseline;
                int snapshot0BaselineClient = _snapshotPackageBaseline;
                int snapshot1BaselineClient = _snapshotPackageBaseline;
                int snapshot2BaselineClient = _snapshotPackageBaseline;
                if (enableNetworkPrediction && haveBaseline)
                {
                    var end = _snapshotPackageBaseline - NetworkConfig.ClientAckCacheSize;
                    end = end < 0 ? 0 : end;
                    var a = _snapshotPackageBaseline - 1;
                    while (a > end)
                    {
                        if (_snapshotAcks[a % NetworkConfig.ClientAckCacheSize])
                        {
                            var base1 = _snapshotSeqs[a % NetworkConfig.ClientAckCacheSize];
                            if (_server._serverSequence - base1 < NetworkConfig.SnapshotDeltaCacheSize - 2)
                            {
                                snapshot1Baseline = base1;
                                snapshot1BaselineClient = a;
                                snapshot2Baseline = _snapshotSeqs[a % NetworkConfig.ClientAckCacheSize];
                                snapshot2BaselineClient = a;
                            }

                            break;
                        }

                        a--;
                    }

                    a--;
                    while (a > end)
                    {
                        if (_snapshotAcks[a % NetworkConfig.ClientAckCacheSize])
                        {
                            var base2 = _snapshotSeqs[a % NetworkConfig.ClientAckCacheSize];
                            if (_server._serverSequence - base2 < NetworkConfig.SnapshotDeltaCacheSize - 2)
                            {
                                snapshot2Baseline = base2;
                                snapshot2BaselineClient = a;
                            }

                            break;
                        }

                        a--;
                    }
                }

                // NETTODO: Write up a list of all sequence numbers. Ensure they are all needed
                output.WriteRawBits(haveBaseline ? 1u : 0, 1);
                output.WritePackedIntDelta(snapshot0BaselineClient, OutSequence - 1, NetworkConfig.BaseSequenceContext);
                output.WriteRawBits(enableNetworkPrediction ? 1u : 0u, 1);
                output.WriteRawBits(enableHashing ? 1u : 0u, 1);
                if (enableNetworkPrediction)
                {
                    output.WritePackedIntDelta(haveBaseline ? snapshot1BaselineClient : 0, snapshot0BaselineClient - 1,
                        NetworkConfig.BaseSequence1Context);
                    output.WritePackedIntDelta(haveBaseline ? snapshot2BaselineClient : 0, snapshot1BaselineClient - 1,
                        NetworkConfig.BaseSequence2Context);
                }

                // NETTODO: For us serverTime == tick but network layer only cares about a growing int
                output.WritePackedIntDelta(_server.serverTime, haveBaseline ? _maxSnapshotTime : 0,
                    NetworkConfig.ServerTimeContext);

                // NETTODO: a more generic way to send stats
                var temp = _server._serverSimTime * 10;
                output.WriteRawBits((byte) temp, 8);

                // NETTODO: Rename TempListType etc.
                // NETTODO: Consider if we need to distinguish between Type & Schema
                _server._tempTypeList.Clear();
                _server._tempSpawnList.Clear();
                _server._tempDeSpawnList.Clear();
                _server._tempUpdateList.Clear();

                Profiler.BeginSample("NetworkServer.PredictSnapshot");
                _server._predictionIndex = 0;
                for (int id = 0, c = _server._entities.Count; id < c; id++)
                {
                    var entity = _server._entities[id];

                    // Skip freed
                    if (entity.SpawnSequence == 0)
                    {
                        continue;
                    }

                    bool spawnedSinceBaseline = (entity.SpawnSequence > baseline);
                    bool despawned = (entity.DeSpawnSequence > 0);

                    // Note to future self: This is a bit tricky... We consider lifetimes of entities
                    // re the baseline (last ack'ed, so in the past) and the snapshot we are building (now)
                    // There are 6 cases (S == spawn, D = despawn):
                    //
                    //  --------------------------------- time ----------------------------------->
                    //
                    //                   BASELINE          SNAPSHOT
                    //                      |                 |
                    //                      v                 v
                    //  1.    S-------D                                                  IGNORE
                    //  2.    S------------------D                                       SEND DESPAWN
                    //  3.    S-------------------------------------D                    SEND UPDATE
                    //  4.                        S-----D                                IGNORE
                    //  5.                        S-----------------D                    SEND SPAWN + UPDATE
                    //  6.                                         S----------D          INVALID (FUTURE)
                    //

                    if (despawned && entity.DeSpawnSequence <= baseline)
                    {
                        continue; // case 1: ignore
                    }

                    if (despawned && !spawnedSinceBaseline)
                    {
                        _server._tempDeSpawnList.Add(id); // case 2: despawn
                        continue;
                    }

                    if (spawnedSinceBaseline && despawned)
                    {
                        continue; // case 4: ignore
                    }

                    if (spawnedSinceBaseline)
                    {
                        _server._tempSpawnList.Add(id); // case 5: send spawn + update
                    }

                    // case 5. and 3. fall through to here and gets updated

                    // Send data from latest tick
                    var tickToSend = _server._serverSequence;
                    // If deSpawned, however, we have stopped generating updates so pick latest valid
                    if (despawned)
                    {
                        tickToSend = Mathf.Max(entity.UpdateSequence, entity.DeSpawnSequence - 1);
                    }
                    //GameDebug.Assert(tickToSend == server.m_ServerSequence || tickToSend == entity.despawnSequence - 1, "Sending snapshot. Expect to send either current tick or last tick before despawn.");

                    {
                        var entityType = _server._entityTypes[entity.TypeId];

                        var snapshot = entity.Snapshots[tickToSend];

                        // NOTE : As long as the server haven't gotten the spawn acked, it will keep sending
                        // delta relative to 0 as we cannot know if we have a valid baseline on the client or not

                        uint numBaselines =
                            1; // if there is no normal baseline, we use schema baseline so there is always one
                        uint* baseline0 = entityType.Baseline;
                        int time0 = _maxSnapshotTime;

                        if (haveBaseline && entity.SpawnSequence <= MAXSnapshotAck)
                        {
                            baseline0 = entity.Snapshots[snapshot0Baseline].Start;
                        }

                        if (enableNetworkPrediction)
                        {
                            uint* baseline1 = entityType.Baseline;
                            uint* baseline2 = entityType.Baseline;
                            int time1 = _maxSnapshotTime;
                            int time2 = _maxSnapshotTime;

                            if (haveBaseline && entity.SpawnSequence <= MAXSnapshotAck)
                            {
                                GameDebug.Assert(
                                    _server._snapshots[snapshot0Baseline % _server._snapshots.Length].ServerTime ==
                                    _maxSnapshotTime, "serverTime == maxSnapshotTime");
                                GameDebug.Assert(entity.Snapshots.Exists(snapshot0Baseline),
                                    "Exists(snapshot0Baseline)");

                                // Newly spawned entities might not have earlier baselines initially
                                if (snapshot1Baseline != snapshot0Baseline &&
                                    entity.Snapshots.Exists(snapshot1Baseline))
                                {
                                    numBaselines = 2;
                                    baseline1 = entity.Snapshots[snapshot1Baseline].Start;
                                    time1 = _server._snapshots[snapshot1Baseline % _server._snapshots.Length]
                                        .ServerTime;

                                    if (snapshot2Baseline != snapshot1Baseline &&
                                        entity.Snapshots.Exists(snapshot2Baseline))
                                    {
                                        numBaselines = 3;
                                        baseline2 = entity.Snapshots[snapshot2Baseline].Start;
                                        //time2 = entity.snapshots[snapshot2Baseline].serverTime;
                                        time2 = _server._snapshots[snapshot2Baseline % _server._snapshots.Length]
                                            .ServerTime;
                                    }
                                }
                            }

                            entity.Prediction = _server._prediction + _server._predictionIndex;
                            global::Networking.NetworkPrediction.PredictSnapshot(entity.Prediction,
                                entity.FieldsChangedPrediction,
                                entityType.Schema, numBaselines, (uint) time0, baseline0, (uint) time1, baseline1,
                                (uint) time2, baseline2, (uint) _server.serverTime, entity.GetFieldMask(ConnectionId));
                            _server._predictionIndex += entityType.Schema.GetByteSize() / 4;
                            _server.StatsProcessedOutgoing += entityType.Schema.GetByteSize();

                            if (UnsafeUtility.MemCmp(entity.Prediction, snapshot.Start,
                                entityType.Schema.GetByteSize()) != 0)
                            {
                                _server._tempUpdateList.Add(id);
                            }

                            if (ServerDebug.IntValue > 2)
                            {
                                GameDebug.Log((haveBaseline ? "Upd [BL]" : "Upd [  ]") +
                                              "num_baselines: " + numBaselines + " serverSequence: " + tickToSend +
                                              " " +
                                              snapshot0Baseline + "(" + snapshot0BaselineClient + "," + time0 + ") - " +
                                              snapshot1Baseline + "(" + snapshot1BaselineClient + "," + time1 + ") - " +
                                              snapshot2Baseline + "(" + snapshot2BaselineClient + "," + time2 +
                                              "). Sche: " +
                                              _server._tempTypeList.Count + " Spwns: " + _server._tempSpawnList.Count +
                                              " Desp: " + _server._tempDeSpawnList.Count + " Upd: " +
                                              _server._tempUpdateList.Count);
                            }
                        }
                        else
                        {
                            var prediction = baseline0;

                            var fcp = entity.FieldsChangedPrediction;
                            for (int i = 0, l = fcp.Length; i < l; ++i)
                                fcp[i] = 0;

                            if (UnsafeUtility.MemCmp(prediction, snapshot.Start, entityType.Schema.GetByteSize()) != 0)
                            {
                                _server._tempUpdateList.Add(id);
                            }

                            if (ServerDebug.IntValue > 2)
                            {
                                GameDebug.Log((haveBaseline ? "Upd [BL]" : "Upd [  ]") + snapshot0Baseline + "(" +
                                              snapshot0BaselineClient + "," + time0 + "). Sche: " +
                                              _server._tempTypeList.Count + " Spwns: " + _server._tempSpawnList.Count +
                                              " Desp: " + _server._tempDeSpawnList.Count + " Upd: " +
                                              _server._tempUpdateList.Count);
                            }
                        }
                    }
                }

                Profiler.EndSample();

                if (ServerDebug.IntValue > 1 &&
                    (_server._tempSpawnList.Count > 0 || _server._tempDeSpawnList.Count > 0))
                {
                    GameDebug.Log(ConnectionId + ": spwns: " + string.Join(",", _server._tempSpawnList) +
                                  "    despwans: " + string.Join(",", _server._tempDeSpawnList));
                }

                foreach (var pair in _server._entityTypes)
                {
                    if (pair.Value.CreatedSequence > MAXSnapshotAck)
                    {
                        _server._tempTypeList.Add(pair.Value);
                    }
                }

                output.WritePackedUInt((uint) _server._tempTypeList.Count, NetworkConfig.SchemaCountContext);
                foreach (var typeInfo in _server._tempTypeList)
                {
                    output.WritePackedUInt(typeInfo.TypeId, NetworkConfig.SchemaTypeIdContext);
                    NetworkSchema.WriteSchema(typeInfo.Schema, ref output);

                    GameDebug.Assert(typeInfo.Baseline != null);
                    NetworkSchema.CopyFieldsFromBuffer(typeInfo.Schema, typeInfo.Baseline, ref output);
                }

                int previousId = 1;
                output.WritePackedUInt((uint) _server._tempSpawnList.Count, NetworkConfig.SpawnCountContext);
                foreach (var id in _server._tempSpawnList)
                {
                    output.WritePackedIntDelta(id, previousId, NetworkConfig.IDContext);
                    previousId = id;

                    var entity = _server._entities[id];

                    output.WritePackedUInt((uint) entity.TypeId, NetworkConfig.SpawnTypeIdContext);
                    output.WriteRawBits(entity.GetFieldMask(ConnectionId), 8);
                }

                output.WritePackedUInt((uint) _server._tempDeSpawnList.Count, NetworkConfig.DeSpawnCountContext);
                foreach (var id in _server._tempDeSpawnList)
                {
                    output.WritePackedIntDelta(id, previousId, NetworkConfig.IDContext);
                    previousId = id;
                }

                int numUpdates = _server._tempUpdateList.Count;
                output.WritePackedUInt((uint) numUpdates, NetworkConfig.UpdateCountContext);

                if (ServerDebug.IntValue > 0)
                {
                    foreach (var t in _server._entityTypes)
                    {
                        t.Value.StatsCount = 0;
                        t.Value.StatsBits = 0;
                    }
                }

                foreach (var id in _server._tempUpdateList)
                {
                    var entity = _server._entities[id];
                    var entityType = _server._entityTypes[entity.TypeId];

                    uint* prediction = null;
                    if (enableNetworkPrediction)
                    {
                        prediction = entity.Prediction;
                    }
                    else
                    {
                        prediction = entityType.Baseline;
                        if (haveBaseline && entity.SpawnSequence <= MAXSnapshotAck)
                        {
                            prediction = entity.Snapshots[snapshot0Baseline].Start;
                        }
                    }

                    output.WritePackedIntDelta(id, previousId, NetworkConfig.IDContext);
                    previousId = id;

                    // TODO (petera) It is a mess that we have to repeat the logic about tickToSend from above here
                    int tickToSend = _server._serverSequence;
                    if (entity.DeSpawnSequence > 0)
                        tickToSend = Mathf.Max(entity.DeSpawnSequence - 1, entity.UpdateSequence);

                    GameDebug.Assert(_server._serverSequence - tickToSend < NetworkConfig.SnapshotDeltaCacheSize);

                    if (!entity.Snapshots.Exists(tickToSend))
                    {
                        GameDebug.Log("maxSnapAck: " + MAXSnapshotAck);
                        GameDebug.Log("lastWritten: " + _snapshotServerLastWritten);
                        GameDebug.Log("spawn: " + entity.SpawnSequence);
                        GameDebug.Log("despawn: " + entity.DeSpawnSequence);
                        GameDebug.Log("update: " + entity.UpdateSequence);
                        GameDebug.Log("tick: " + _server._serverSequence);
                        GameDebug.Log("id: " + id);
                        GameDebug.Log("snapshots: " + entity.Snapshots.ToString());
                        //GameDebug.Log("WOULD HAVE crashed looking for " + tickToSend + " changing to " + (entity.despawnSequence - 1));
                        //tickToSend = entity.despawnSequence - 1;
                        GameDebug.Assert(false,
                            "Unable to find " + tickToSend + " in snapshots. Would update have worked?");
                    }

                    var snapshotInfo = entity.Snapshots[tickToSend];

                    // NOTE : As long as the server haven't gotten the spawn acked, it will keep sending
                    // delta relative to 0 as we cannot know if we have a valid baseline on the client or not
                    uint entity_hash = 0;
                    var bef = output.GetBitPosition2();
                    DeltaWriter.Write(ref output, entityType.Schema, snapshotInfo.Start, prediction,
                        entity.FieldsChangedPrediction, entity.GetFieldMask(ConnectionId), ref entity_hash);
                    var aft = output.GetBitPosition2();
                    if (ServerDebug.IntValue > 0)
                    {
                        entityType.StatsCount++;
                        entityType.StatsBits += (aft - bef);
                    }

                    if (enableHashing)
                    {
                        output.WriteRawBits(entity_hash, 32);
                    }
                }

                if (!haveBaseline && ServerDebug.IntValue > 0)
                {
                    Debug.Log("Sending no-baseline snapshot. C: " + ConnectionId + " Seq: " + OutSequence + " Max: " +
                              MAXSnapshotAck + "  Total entities sent: " + _server._tempUpdateList.Count +
                              " Type breakdown:");
                    foreach (var c in _server._entityTypes)
                    {
                        Debug.Log(c.Value.Name + " " + c.Key + " #" + (c.Value.StatsCount) + " " +
                                  (c.Value.StatsBits / 8) + " bytes");
                    }
                }

                if (enableHashing)
                {
                    output.WriteRawBits(_server._lastEntityCount, 32);
                }

                _server.StatsSentOutgoing += output.GetBitPosition2() / 8;

                _snapshotServerLastWritten = _server._serverSequence;
                _snapshotSeqs[OutSequence % NetworkConfig.ClientAckCacheSize] = _server._serverSequence;

                Profiler.EndSample();
            }

            private void ReadClientConfig<TInputStream>(ref TInputStream input) where TInputStream : IInputStream
            {
                _maxBps = (int) input.ReadRawBits(32);
                var interval = (int) input.ReadRawBits(16);
                if (interval < 1)
                {
                    interval = 1;
                }

                _snapshotInterval = interval;

                if (ServerDebug.IntValue > 0)
                {
                    GameDebug.Log($"ReadClientConfig: updateRate: {_maxBps}  snapshotRate: {_snapshotInterval}");
                }
            }

            private void ReadCommands<TInputStream>(ref TInputStream input) where TInputStream : IInputStream
            {
                counters.CommandsIn++;
                var schema = input.ReadRawBits(1) != 0;
                if (schema)
                {
                    _commandSchema = NetworkSchema.ReadSchema(ref input); // might be overridden
                }

                // NETTODO Reconstruct the wide sequence
                // NETTODO Rename to commandMessageSequence?
                var sequence = Sequence.FromUInt16((ushort) input.ReadRawBits(16), _commandSequenceIn);
                if (sequence > _commandSequenceIn)
                {
                    _commandSequenceIn = sequence;
                }

                CommandInfo previous = _defaultCommandInfo;
                while (input.ReadRawBits(1) != 0)
                {
                    var command = _commandsIn.Acquire(sequence);
                    command.Time = input.ReadPackedIntDelta(previous.Time, NetworkConfig.CommandTimeContext);

                    uint hash = 0;
                    DeltaReader.Read(ref input, _commandSchema, command.Data, previous.Data, _zeroFieldsChanged, 0,
                        ref hash);

                    previous = command;
                    --sequence;
                }
            }

            // when incoming package, this is called up to 16 times, one for each pack that gets acked
            // sequence: the 'top' package that is being acknowledged in this package
            // TODO (petera) shouldn't sequence be in info?
            protected override void NotifyDelivered(int sequence, ServerPackageInfo info, bool madeIt)
            {
                base.NotifyDelivered(sequence, info, madeIt);

                if (madeIt)
                {
                    if ((info.Content & NetworkMessage.ClientInfo) != 0)
                        ClientInfoAcked = true;

                    // Check if the client received the map info
                    if ((info.Content & NetworkMessage.MapInfo) != 0 &&
                        info.ServerSequence >= _server._mapInfo.ServerInitSequence)
                    {
                        _mapAcked = true;
                        _mapSchemaAcked = true;
                    }

                    // Update the snapshot baseline if the client received the snapshot
                    if (_mapAcked && (info.Content & NetworkMessage.Snapshot) != 0)
                    {
                        _snapshotPackageBaseline = sequence;

                        GameDebug.Assert(_snapshotSeqs[sequence % NetworkConfig.ClientAckCacheSize] > 0,
                            "Got ack for package we did not expect?");
                        _snapshotAcks[sequence % NetworkConfig.ClientAckCacheSize] = true;

                        // Keep track of newest ack'ed snapshot
                        if (info.ServerSequence > MAXSnapshotAck)
                        {
                            if (MAXSnapshotAck == 0 && ServerDebug.IntValue > 0)
                                Debug.Log("SERVER: first max ack for " + info.ServerSequence);
                            MAXSnapshotAck = info.ServerSequence;
                            _maxSnapshotTime = info.ServerTime;
                        }
                    }
                }
            }
        }
    }
}