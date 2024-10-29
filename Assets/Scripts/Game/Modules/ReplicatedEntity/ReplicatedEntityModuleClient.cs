using System.Collections.Generic;
using Networking;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;

public class ReplicatedEntityModuleClient : ISnapshotConsumer
{
    [ConfigVar(Name = "replicatedentity.showclientinfo", DefaultValue = "0",
        Description = "Show replicated system info")]
    public static ConfigVar ShowInfo;

    private readonly GameWorld _world;
    private readonly GameObject _systemRoot;
    private readonly BundledResourceManager _resourceSystem;
    private readonly ReplicatedEntityRegistry _assetRegistry;
    private readonly ReplicatedEntityCollection _entityCollection;
    private readonly UpdateReplicatedOwnerFlag _updateReplicatedOwnerFlag;

    public ReplicatedEntityModuleClient(GameWorld world, BundledResourceManager resourceSystem)
    {
        _world = world;
        _resourceSystem = resourceSystem;
        _assetRegistry = resourceSystem.GetResourceRegistry<ReplicatedEntityRegistry>();
        _entityCollection = new ReplicatedEntityCollection(_world);

        _updateReplicatedOwnerFlag = _world.GetECSWorld().CreateManager<UpdateReplicatedOwnerFlag>(_world);

        // Load all replicated entity resources
        _assetRegistry.LoadAllResources(resourceSystem);

        if (world.SceneRoot != null)
        {
            _systemRoot = new GameObject("ReplicatedEntitySystem");
            _systemRoot.transform.SetParent(world.SceneRoot.transform);
        }
    }

    public void Shutdown()
    {
        _world.GetECSWorld().DestroyManager(_updateReplicatedOwnerFlag);

        if (_systemRoot != null)
        {
            Object.Destroy(_systemRoot);
        }
    }

    public void ProcessEntitySpawn(int serverTick, int id, ushort typeId)
    {
        if (ShowInfo.IntValue > 0)
        {
            GameDebug.Log($"ProcessEntitySpawns. Server tick:{serverTick} id:{id} typeid:{typeId}");
        }

        // If this is a replicated entity from the scene it only needs to be registered (not instantiated)
        if (id < _world.SceneEntities.Count)
        {
            var e = _world.SceneEntities[id];
            var gameObjectEntity = e.gameObject.GetComponent<GameObjectEntity>();
            GameDebug.Assert(gameObjectEntity != null, $"Replicated entity {e.name} has no GameObjectEntity component");

            _entityCollection.Register(_world.GetEntityManager(), id, gameObjectEntity.Entity);
            return;
        }

        int index = typeId;
        // If factory present it should be used to create entity
        GameDebug.Assert(index < _assetRegistry.entries.Count,
            $"TypeId:{typeId} not in range. Array Length:{_assetRegistry.entries.Count}");

        var entity = _resourceSystem.CreateEntity(_assetRegistry.entries[index].guid);
        if (entity == Entity.Null)
        {
            var guid = _assetRegistry.entries[index].guid;
            GameDebug.LogError($"Failed to create entity for index:{index} guid:{guid}");
            return;
        }

        Profiler.BeginSample("ReplicatedEntitySystemClient.ProcessEntitySpawns()");

        var replicatedDataEntity = _world.GetEntityManager().GetComponentData<ReplicatedEntityData>(entity);
        replicatedDataEntity.id = id;
        _world.GetEntityManager().SetComponentData(entity, replicatedDataEntity);

        _entityCollection.Register(_world.GetEntityManager(), id, entity);

        Profiler.EndSample();
    }

    public void ProcessEntityUpdate(int serverTick, int id, ref NetworkReader reader)
    {
        if (ShowInfo.IntValue > 1)
        {
            GameDebug.Log($"ApplyEntitySnapshot. ServerTick:{serverTick} entityId:{id}");
        }

        _entityCollection.ProcessEntityUpdate(serverTick, id, ref reader);
    }

    public void ProcessEntityDeSpawn(int serverTime, List<int> deSpawns)
    {
        if (ShowInfo.IntValue > 0)
        {
            GameDebug.Log($"ProcessEntityDeSpawn. Server tick:{serverTime} ids:{string.Join(",", deSpawns)}");
        }

        for (var i = 0; i < deSpawns.Count; i++)
        {
            var entity = _entityCollection.Unregister(_world.GetEntityManager(), deSpawns[i]);

            if (_world.GetEntityManager().HasComponent<ReplicatedEntity>(entity))
            {
                var replicatedEntity = _world.GetEntityManager().GetComponentObject<ReplicatedEntity>(entity);
                _world.RequestDespawn(replicatedEntity.gameObject);
                continue;
            }

            _world.RequestDespawn(entity);
        }
    }

    public void Rollback()
    {
        _entityCollection.Rollback();
    }

    public void Interpolate(GameTime time)
    {
        _entityCollection.Interpolate(time);
    }

    public void SetLocalPlayerId(int id)
    {
        _updateReplicatedOwnerFlag.SetLocalPlayerId(id);
    }

    public void UpdateControlledEntityFlags()
    {
        _updateReplicatedOwnerFlag.Update();
    }

#if UNITY_EDITOR

    public int GetEntityCount()
    {
        return _entityCollection.GetEntityCount();
    }

    public int GetSampleCount()
    {
        return _entityCollection.GetSampleCount();
    }

    public int GetSampleTick(int sampleIndex)
    {
        return _entityCollection.GetSampleTick(sampleIndex);
    }

    public int GetLastServerTick(int sampleIndex)
    {
        return _entityCollection.GetLastServerTick(sampleIndex);
    }

    public int GetNetIdFromEntityIndex(int entityIndex)
    {
        return _entityCollection.GetNetIdFromEntityIndex(entityIndex);
    }

    public ReplicatedEntityCollection.ReplicatedData GetReplicatedDataForNetId(int netId)
    {
        return _entityCollection.GetReplicatedDataForNetId(netId);
    }

    public void StorePredictedState(int predictedTick, int finalTick)
    {
        _entityCollection.StorePredictedState(predictedTick, finalTick);
    }

    public void FinalizedStateHistory(int tick, int lastServerTick, ref UserCommand command)
    {
        _entityCollection.FinalizedStateHistory(tick, lastServerTick, ref command);
    }

    public int FindSampleIndexForTick(int tick)
    {
        return _entityCollection.FindSampleIndexForTick(tick);
    }

    public bool IsPredicted(int entityIndex)
    {
        return _entityCollection.IsPredicted(entityIndex);
    }

#endif
}