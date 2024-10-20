using System.Collections.Generic;
using Networking.Compression;

namespace Networking
{
    public delegate void NetworkEventGenerator(ref NetworkWriter data);

    public delegate void NetworkEventProcessor(ushort typeId, ref NetworkReader data);

    public class NetworkEventType
    {
        public ushort TypeId;
        public NetworkSchema Schema;
    }

    public class NetworkEvent
    {
        public int Sequence;
        public bool Reliable;
        public NetworkEventType Type;
        public readonly uint[] Data = new uint[NetworkConfig.MAXEventDataSize];

        private int _refCount;
        private static int _sequence;
        private static readonly NetworkObjectPool<NetworkEvent> Pool = new NetworkObjectPool<NetworkEvent>(100);

        public void AddRef()
        {
            ++_refCount;
        }

        public void Release()
        {
            GameDebug.Assert(_refCount > 0, "Trying to release an event that has ref count 0 (seq: {0})", Sequence);
            if (--_refCount == 0)
            {
                if (NetworkConfig.NetDebug.IntValue > 0)
                {
                    GameDebug.Log("Releasing event " + ((GameNetworkEvents.EventType) Type.TypeId) + ":" + Sequence);
                }

                Pool.Release(this);
            }
        }

        private static NetworkEvent Create(NetworkEventType type, bool reliable = false)
        {
            var result = Pool.Allocate();
            GameDebug.Assert(result._refCount == 0);
            result._refCount = 1;
            result.Sequence = 0;
            result.Reliable = reliable;
            result.Type = type;

            return result;
        }

        public static unsafe NetworkEvent Serialize(ushort typeId, bool reliable,
            Dictionary<ushort, NetworkEventType> eventTypes, NetworkEventGenerator generator)
        {
            bool generateSchema = false;
            NetworkEventType type;
            if (!eventTypes.TryGetValue(typeId, out type))
            {
                generateSchema = true;
                type = new NetworkEventType()
                    {TypeId = typeId, Schema = new NetworkSchema(NetworkConfig.FirstEventTypeSchemaId + typeId)};
                eventTypes.Add(typeId, type);
            }

            var result = Create(type, reliable);
            result.Sequence = ++_sequence;
            if (NetworkConfig.NetDebug.IntValue > 0)
            {
                GameDebug.Log("Serializing event " + ((GameNetworkEvents.EventType) result.Type.TypeId) +
                              " in seq no: " + result.Sequence);
            }

            fixed (uint* data = result.Data)
            {
                NetworkWriter writer = new NetworkWriter(data, result.Data.Length, type.Schema, generateSchema);
                generator(ref writer);
                writer.Flush();
            }

            return result;
        }

        public static int ReadEvents<TInputStream>(Dictionary<ushort, NetworkEventType> eventTypesIn, int connectionId,
            ref TInputStream input, INetworkCallbacks networkConsumer)
            where TInputStream : IInputStream
        {
            var eventCount = input.ReadPackedUInt(NetworkConfig.EventCountContext);
            for (var eventCounter = 0; eventCounter < eventCount; ++eventCounter)
            {
                var typeId = (ushort) input.ReadPackedUInt(NetworkConfig.EventTypeIdContext);
                var schemaIncluded = input.ReadRawBits(1) != 0;
                if (schemaIncluded)
                {
                    var eventType = new NetworkEventType() {TypeId = typeId};
                    eventType.Schema = NetworkSchema.ReadSchema(ref input);

                    if (!eventTypesIn.ContainsKey(typeId))
                    {
                        eventTypesIn.Add(typeId, eventType);
                    }
                }

                // TODO (petera) do we need to Create an info (as we are just releasing it right after?)
                var type = eventTypesIn[typeId];
                var info = Create(type);
                NetworkSchema.CopyFieldsToBuffer(type.Schema, ref input, info.Data);
                if (NetworkConfig.NetDebug.IntValue > 0)
                {
                    GameDebug.Log("Received event " +
                                  ((GameNetworkEvents.EventType) info.Type.TypeId + ":" + info.Sequence));
                }

                networkConsumer.OnEvent(connectionId, info);

                info.Release();
            }

            return (int) eventCount;
        }

        public static unsafe void WriteEvents<TOutputStream>(List<NetworkEvent> events,
            List<NetworkEventType> knownEventTypes, ref TOutputStream output)
            where TOutputStream : IOutputStream
        {
            output.WritePackedUInt((uint) events.Count, NetworkConfig.EventCountContext);
            foreach (var info in events)
            {
                // Write event schema if the client haven't acked this event type
                output.WritePackedUInt(info.Type.TypeId, NetworkConfig.EventCountContext);
                if (!knownEventTypes.Contains(info.Type))
                {
                    output.WriteRawBits(1, 1);
                    NetworkSchema.WriteSchema(info.Type.Schema, ref output);
                }
                else
                {
                    output.WriteRawBits(0, 1);
                }

                // Write event data
                fixed (uint* data = info.Data)
                {
                    NetworkSchema.CopyFieldsFromBuffer(info.Type.Schema, data, ref output);
                }
            }
        }
    }
}