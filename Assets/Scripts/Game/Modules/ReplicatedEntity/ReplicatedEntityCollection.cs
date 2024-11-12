using System;
using System.Collections.Generic;
using Networking;
using Unity.Entities;
using UnityEngine;

public class ReplicatedEntityCollection : IEntityReferenceSerializer
{
    public struct ReplicatedData
    {
        public Entity Entity;
        public GameObject GameObject;
        public IReplicatedSerializer[] SerializableArray;
        public IPredictedSerializer[] PredictedArray;
        public IInterpolatedSerializer[] InterpolatedArray;
        public int LastServerUpdate;

#if UNITY_EDITOR
        public bool VerifyPrediction(int sampleIndex, int tick)
        {
            foreach (var predictedDataHandler in PredictedArray)
            {
                if (!predictedDataHandler.VerifyPrediction(sampleIndex, tick))
                {
                    return false;
                }
            }

            return true;
        }

        public bool HasState(int tick)
        {
            foreach (var predictedDataHandler in PredictedArray)
            {
                if (predictedDataHandler.HasServerState(tick))
                {
                    return true;
                }
            }

            return false;
        }
#endif
    }

    [ConfigVar(Name = "replicatedentity.showcollectioninfo", DefaultValue = "0",
        Description = "Show replicated system info")]
    public static ConfigVar ShowInfo;

    public static bool SampleHistory;

    public const int HistorySize = 128;
    public const int PredictionSize = 32;

    private readonly GameWorld _world;

    private readonly DataComponentSerializers _serializers = new DataComponentSerializers();

    private readonly List<ReplicatedData> _replicatedData = new List<ReplicatedData>(512);

    private readonly List<IReplicatedSerializer> _netReplicated = new List<IReplicatedSerializer>(32);
    private readonly List<IPredictedSerializer> _netPredicted = new List<IPredictedSerializer>(32);
    private readonly List<IInterpolatedSerializer> _netInterpolated = new List<IInterpolatedSerializer>(32);

#if UNITY_EDITOR
    private UserCommand[] _historyCommands;
    private int[] _hitstoryTicks;
    private int[] _hitstoryLastServerTick;
    private int _historyFirstIndex;
    private int _historyCount;
#endif

    public ReplicatedEntityCollection(GameWorld world)
    {
        _world = world;

#if UNITY_EDITOR
        _historyCommands = new UserCommand[HistorySize];
        _hitstoryTicks = new int[HistorySize];
        _hitstoryLastServerTick = new int[HistorySize];
#endif
    }

    public void Register(EntityManager entityManager, int entityId, Entity entity)
    {
        if (ShowInfo.IntValue > 0)
        {
            if (entityManager.HasComponent<Transform>(entity))
            {
                GameDebug.Log("RepEntity REGISTER NetID:" + entityId + " Entity:" + entity + " GameObject:" +
                              entityManager.GetComponentObject<Transform>(entity).name);
            }
            else
            {
                GameDebug.Log("RepEntity REGISTER NetID:" + entityId + " Entity:" + entity);
            }
        }

        // Grow to make sure there is room for entity            
        if (entityId >= _replicatedData.Count)
        {
            var count = entityId - _replicatedData.Count + 1;
            var emptyData = new ReplicatedData();
            for (var i = 0; i < count; i++)
            {
                _replicatedData.Add(emptyData);
            }
        }

        GameDebug.Assert(_replicatedData[entityId].Entity == Entity.Null, "ReplicatedData has entity set:{0}",
            _replicatedData[entityId].Entity);

        _netReplicated.Clear();
        _netPredicted.Clear();
        _netInterpolated.Clear();

        var go = entityManager.HasComponent<Transform>(entity)
            ? entityManager.GetComponentObject<Transform>(entity).gameObject
            : null;

        FindSerializers(entityManager, entity);

        if (entityManager.HasComponent<EntityGroupChildren>(entity))
        {
            var buffer = entityManager.GetBuffer<EntityGroupChildren>(entity);
            for (int i = 0; i < buffer.Length; i++)
            {
                var childEntity = buffer[i].Entity;
                if (ShowInfo.IntValue > 0)
                {
                    GameDebug.Log(" ReplicatedEntityChildren: " + i + " = " + childEntity);
                }

                FindSerializers(entityManager, childEntity);
            }
        }

        var data = new ReplicatedData
        {
            Entity = entity,
            GameObject = go,
            SerializableArray = _netReplicated.ToArray(),
            PredictedArray = _netPredicted.ToArray(),
            InterpolatedArray = _netInterpolated.ToArray(),
        };

        _replicatedData[entityId] = data;
    }

    private void FindSerializers(EntityManager entityManager, Entity entity)
    {
        // Add entity data handlers
        if (ShowInfo.IntValue > 0)
        {
            GameDebug.Log("  FindSerializers");
        }

        var componentTypes = entityManager.GetComponentTypes(entity);

        // Sort to ensure order when serializing components
        var typeArray = componentTypes.ToArray();
        Array.Sort(typeArray,
            delegate(ComponentType type1, ComponentType type2)
            {
                return string.Compare(type1.GetManagedType().Name, type2.GetManagedType().Name,
                    StringComparison.Ordinal);
            });

        var serializedComponentType = typeof(IReplicatedComponent);
        var predictedComponentType = typeof(IPredictedDataBase);
        var interpolatedComponentType = typeof(IInterpolatedDataBase);

        foreach (var componentType in typeArray)
        {
            var managedType = componentType.GetManagedType();

            if (!typeof(IComponentData).IsAssignableFrom(managedType))
            {
                continue;
            }

            if (serializedComponentType.IsAssignableFrom(managedType))
            {
                if (ShowInfo.IntValue > 0)
                {
                    GameDebug.Log("   new SerializedComponentDataHandler for:" + managedType.Name);
                }

                var serializer = _serializers.CreateNetSerializer(managedType, entityManager, entity, this);
                if (serializer != null)
                {
                    _netReplicated.Add(serializer);
                }
            }
            else if (predictedComponentType.IsAssignableFrom(managedType))
            {
                var interfaceTypes = managedType.GetInterfaces();
                foreach (var it in interfaceTypes)
                {
                    if (it.IsGenericType)
                    {
                        var type = it.GenericTypeArguments[0];
                        if (ShowInfo.IntValue > 0)
                        {
                            GameDebug.Log("   new IPredictedDataHandler for:" + it.Name + " arg type:" + type);
                        }

                        var serializer =
                            _serializers.CreatePredictedSerializer(managedType, entityManager, entity, this);
                        if (serializer != null)
                        {
                            _netPredicted.Add(serializer);
                        }

                        break;
                    }
                }
            }
            else if (interpolatedComponentType.IsAssignableFrom(managedType))
            {
                var interfaceTypes = managedType.GetInterfaces();
                foreach (var it in interfaceTypes)
                {
                    if (it.IsGenericType)
                    {
                        var type = it.GenericTypeArguments[0];
                        if (ShowInfo.IntValue > 0)
                        {
                            GameDebug.Log("   new IInterpolatedDataHandler for:" + it.Name + " arg type:" + type);
                        }

                        var serializer =
                            _serializers.CreateInterpolatedSerializer(managedType, entityManager, entity, this);
                        if (serializer != null)
                        {
                            _netInterpolated.Add(serializer);
                        }

                        break;
                    }
                }
            }
        }
    }

    public Entity Unregister(EntityManager entityManager, int entityId)
    {
        var entity = _replicatedData[entityId].Entity;
        GameDebug.Assert(entity != Entity.Null, "Unregister. ReplicatedData has has entity set");

        if (ShowInfo.IntValue > 0)
        {
            if (entityManager.HasComponent<Transform>(entity))
            {
                GameDebug.Log("RepEntity UNREGISTER NetID:" + entityId + " Entity:" + entity + " GameObject:" +
                              entityManager.GetComponentObject<Transform>(entity).name);
            }
            else
            {
                GameDebug.Log("RepEntity UNREGISTER NetID:" + entityId + " Entity:" + entity);
            }
        }

        _replicatedData[entityId] = new ReplicatedData();
        return entity;
    }

    public void ProcessEntityUpdate(int serverTick, int id, ref NetworkReader reader)
    {
        var data = _replicatedData[id];

        GameDebug.Assert(data.LastServerUpdate < serverTick,
            "Failed to apply snapshot. Wrong tick order. entityId:{0} snapshot tick:{1} last server tick:{2}", id,
            serverTick, data.LastServerUpdate);
        data.LastServerUpdate = serverTick;

        GameDebug.Assert(data.SerializableArray != null, "Failed to apply snapshot. SerializableArray is null");

        if (data.SerializableArray != null)
        {
            foreach (var entry in data.SerializableArray)
            {
                entry.Deserialize(ref reader, serverTick);
            }
        }

        if (data.PredictedArray != null)
        {
            foreach (var entry in data.PredictedArray)
            {
                entry.Deserialize(ref reader, serverTick);
            }
        }

        if (data.InterpolatedArray != null)
        {
            foreach (var entry in data.InterpolatedArray)
            {
                entry.Deserialize(ref reader, serverTick);
            }
        }

        _replicatedData[id] = data;
    }

    public void GenerateEntitySnapshot(int entityId, ref NetworkWriter writer)
    {
        var data = _replicatedData[entityId];

        GameDebug.Assert(data.SerializableArray != null, "Failed to generate snapshot. SerializableArray is null");

        if (data.SerializableArray != null)
        {
            foreach (var entry in data.SerializableArray)
            {
                entry.Serialize(ref writer);
            }
        }

        writer.SetFieldSection(NetworkWriter.FieldSectionType.OnlyPredicting);

        if (data.PredictedArray != null)
        {
            foreach (var entry in data.PredictedArray)
            {
                entry.Serialize(ref writer);
            }
        }

        writer.ClearFieldSection();

        writer.SetFieldSection(NetworkWriter.FieldSectionType.OnlyNotPredicting);

        if (data.InterpolatedArray != null)
        {
            foreach (var entry in data.InterpolatedArray)
            {
                entry.Serialize(ref writer);
            }
        }

        writer.ClearFieldSection();
    }

    public void Rollback()
    {
        for (var i = 0; i < _replicatedData.Count; i++)
        {
            if (_replicatedData[i].Entity == Entity.Null)
            {
                continue;
            }

            if (_replicatedData[i].PredictedArray == null)
            {
                continue;
            }

            if (!_world.GetEntityManager().HasComponent<ServerEntity>(_replicatedData[i].Entity))
            {
                continue;
            }

            foreach (var predicted in _replicatedData[i].PredictedArray)
            {
                predicted.Rollback();
            }
        }
    }

    public void Interpolate(GameTime time)
    {
        for (var i = 0; i < _replicatedData.Count; i++)
        {
            if (_replicatedData[i].Entity == Entity.Null)
            {
                continue;
            }

            if (_replicatedData[i].InterpolatedArray == null)
            {
                continue;
            }

            if (_world.GetEntityManager().HasComponent<ServerEntity>(_replicatedData[i].Entity))
            {
                continue;
            }

            foreach (var interpolated in _replicatedData[i].InterpolatedArray)
            {
                interpolated.Interpolate(time);
            }
        }
    }

    public string GenerateName(int entityId)
    {
        var data = _replicatedData[entityId];

        bool first = true;
        string name = "";
        foreach (var entry in data.SerializableArray)
        {
            if (!first)
            {
                name += "_";
            }
            else
            {
                name += entry.GetType().ToString();
            }

            first = false;
        }

        return name;
    }

    public void SerializeReference(ref NetworkWriter writer, string name, Entity entity)
    {
        if (entity == Entity.Null || !_world.GetEntityManager().Exists(entity))
        {
            writer.WriteInt32(name, -1);
            return;
        }

        if (_world.GetEntityManager().HasComponent<ReplicatedEntityData>(entity))
        {
            var replicatedDataEntity = _world.GetEntityManager().GetComponentData<ReplicatedEntityData>(entity);
            writer.WriteInt32(name, replicatedDataEntity.ID);
            return;
        }

        GameDebug.LogError("Failed to serialize reference named:" + name + " to entity:" + entity);
    }

    public void DeserializeReference(ref NetworkReader reader, ref Entity entity)
    {
        var replicatedId = reader.ReadInt32();
        if (replicatedId < 0)
        {
            entity = Entity.Null;
            return;
        }

        entity = _replicatedData[replicatedId].Entity;
    }

#if UNITY_EDITOR
    public int GetSampleCount()
    {
        return _historyCount;
    }

    public int GetSampleTick(int sampleIndex)
    {
        var i = (_historyFirstIndex + sampleIndex) % _hitstoryTicks.Length;
        return _hitstoryTicks[i];
    }

    public int GetLastServerTick(int sampleIndex)
    {
        var i = (_historyFirstIndex + sampleIndex) % _hitstoryTicks.Length;
        return _hitstoryLastServerTick[i];
    }

    public bool IsPredicted(int entityIndex)
    {
        var netId = GetNetIdFromEntityIndex(entityIndex);
        var replicatedData = _replicatedData[netId];
        return _world.GetEntityManager().HasComponent<ServerEntity>(replicatedData.Entity);
    }

    public int GetEntityCount()
    {
        int entityCount = 0;
        for (int i = 0; i < _replicatedData.Count; i++)
        {
            if (_replicatedData[i].Entity == Entity.Null)
                continue;
            entityCount++;
        }

        return entityCount;
    }

    public int GetNetIdFromEntityIndex(int entityIndex)
    {
        int entityCount = 0;
        for (int i = 0; i < _replicatedData.Count; i++)
        {
            if (_replicatedData[i].Entity == Entity.Null)
                continue;

            if (entityCount == entityIndex)
                return i;

            entityCount++;
        }

        return -1;
    }

    public ReplicatedData GetReplicatedDataForNetId(int netId)
    {
        return _replicatedData[netId];
    }

    public void StorePredictedState(int predictedTick, int finalTick)
    {
        if (!SampleHistory)
            return;

        var predictionIndex = finalTick - predictedTick;
        var sampleIndex = GetSampleIndex();

        for (int i = 0; i < _replicatedData.Count; i++)
        {
            if (_replicatedData[i].Entity == Entity.Null)
                continue;

            if (_replicatedData[i].PredictedArray == null)
                continue;

            if (!_world.GetEntityManager().HasComponent<ServerEntity>(_replicatedData[i].Entity))
                continue;

            foreach (var predicted in _replicatedData[i].PredictedArray)
            {
                predicted.StorePredictedState(sampleIndex, predictionIndex);
            }
        }
    }


    public void FinalizedStateHistory(int tick, int lastServerTick, ref UserCommand command)
    {
        if (!SampleHistory)
            return;

        var sampleIndex = (_historyFirstIndex + _historyCount) % _hitstoryTicks.Length;

        _hitstoryTicks[sampleIndex] = tick;
        _historyCommands[sampleIndex] = command;
        _hitstoryLastServerTick[sampleIndex] = lastServerTick;

        if (_historyCount < _hitstoryTicks.Length)
            _historyCount++;
        else
            _historyFirstIndex = (_historyFirstIndex + 1) % _hitstoryTicks.Length;
    }

    private int GetSampleIndex()
    {
        return (_historyFirstIndex + _historyCount) % _hitstoryTicks.Length;
    }

    public int FindSampleIndexForTick(int tick)
    {
        for (int i = 0; i < _hitstoryTicks.Length; i++)
        {
            if (_hitstoryTicks[i] == tick)
                return i;
        }

        return -1;
    }
#endif
}