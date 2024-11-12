using Networking;
using Unity.Entities;
using UnityEngine;

[DisableAutoCreation]
public class HandleReplicatedEntityDataSpawn : InitializeComponentDataSystem<ReplicatedEntityData,
    HandleReplicatedEntityDataSpawn.Initialized>
{
    public struct Initialized : IComponentData
    {
    }

    private readonly NetworkServer _network;
    private readonly ReplicatedEntityRegistry _assetRegistry;
    private readonly ReplicatedEntityCollection _entityCollection;

    public HandleReplicatedEntityDataSpawn(GameWorld world, NetworkServer network,
        ReplicatedEntityRegistry assetRegistry, ReplicatedEntityCollection entityCollection) : base(world)
    {
        _network = network;
        _assetRegistry = assetRegistry;
        _entityCollection = entityCollection;
    }

    protected override void Initialize(Entity entity, ReplicatedEntityData component)
    {
        var typeId = _assetRegistry.GetEntryIndex(component.assetGuid);
        component.id = _network.RegisterEntity(component.id, (ushort) typeId, component.predictingPlayerId);

        _entityCollection.Register(EntityManager, component.id, entity);

        PostUpdateCommands.SetComponent(entity, component);

        if (ReplicatedEntityModuleServer.ShowInfo.IntValue > 0)
        {
            GameDebug.Log("HandleReplicatedEntityDataSpawn.Initialize entity:" + entity + " type:" + typeId + " id:" +
                          component.id);
        }
    }
}

[DisableAutoCreation]
public class HandleReplicatedEntityDataDespawn : DeinitializeComponentDataSystem<ReplicatedEntityData>
{
    private readonly NetworkServer _network;
    private readonly ReplicatedEntityCollection _entityCollection;

    public HandleReplicatedEntityDataDespawn(GameWorld world, NetworkServer network,
        ReplicatedEntityCollection entityCollection) : base(world)
    {
        _network = network;
        _entityCollection = entityCollection;
    }

    protected override void Deinitialize(Entity entity, ReplicatedEntityData component)
    {
        if (ReplicatedEntityModuleServer.ShowInfo.IntValue > 0)
        {
            GameDebug.Log("HandleReplicatedEntityDataDespawn.Deinitialize entity:" + entity + " id:" + component.id);
        }

        _entityCollection.Unregister(EntityManager, component.id);
        _network.UnregisterEntity(component.id);
    }
}

public class ReplicatedEntityModuleServer
{
    [ConfigVar(Name = "server.replicatedsysteminfo", DefaultValue = "0", Description = "Show replicated system info")]
    public static ConfigVar ShowInfo;

    private readonly GameWorld _world;

    private readonly GameObject _systemRoot;
    private readonly ReplicatedEntityCollection _entityCollection;

    private readonly HandleReplicatedEntityDataSpawn _handleDataSpawn;

    private readonly HandleReplicatedEntityDataDespawn _handleDataDespawn;

    private readonly UpdateReplicatedOwnerFlag _updateReplicatedOwnerFlag;

    public ReplicatedEntityModuleServer(GameWorld world, BundledResourceManager resourceSystem, NetworkServer network)
    {
        _world = world;
        var assetRegistry = resourceSystem.GetResourceRegistry<ReplicatedEntityRegistry>();
        _entityCollection = new ReplicatedEntityCollection(_world);

        if (world.SceneRoot != null)
        {
            _systemRoot = new GameObject("ReplicatedEntitySystem");
            _systemRoot.transform.SetParent(world.SceneRoot.transform);
        }

        _handleDataSpawn = _world.GetECSWorld().CreateManager<HandleReplicatedEntityDataSpawn>(_world, network,
            assetRegistry, _entityCollection);

        _handleDataDespawn = _world.GetECSWorld().CreateManager<HandleReplicatedEntityDataDespawn>(_world, network,
            _entityCollection);

        _updateReplicatedOwnerFlag = _world.GetECSWorld().CreateManager<UpdateReplicatedOwnerFlag>(_world);
        _updateReplicatedOwnerFlag.SetLocalPlayerId(-1);

        // Load all replicated entity resources
        assetRegistry.LoadAllResources(resourceSystem);
    }

    public void Shutdown()
    {
        _world.GetECSWorld().DestroyManager(_handleDataSpawn);

        _world.GetECSWorld().DestroyManager(_handleDataDespawn);

        _world.GetECSWorld().DestroyManager(_updateReplicatedOwnerFlag);

        if (_systemRoot != null)
        {
            Object.Destroy(_systemRoot);
        }
    }

    internal void ReserveSceneEntities(NetworkServer networkServer)
    {
        // TODO (petera) remove this
        for (var i = 0; i < _world.SceneEntities.Count; i++)
        {
            var gameObjectEntity = _world.SceneEntities[i].GetComponent<GameObjectEntity>();
            var repEntityData = _world.GetEntityManager()
                .GetComponentData<ReplicatedEntityData>(gameObjectEntity.Entity);
            GameDebug.Assert(repEntityData.id == i, "Scene entities must be have the first network ids!");
        }

        networkServer.ReserveSceneEntities(_world.SceneEntities.Count);
    }

    public void HandleSpawning()
    {
        _handleDataSpawn.Update();
        _updateReplicatedOwnerFlag.Update();
    }

    public void HandleDespawning()
    {
        _handleDataDespawn.Update();
    }

    public void GenerateEntitySnapshot(int entityId, ref NetworkWriter writer)
    {
        _entityCollection.GenerateEntitySnapshot(entityId, ref writer);
    }

    public string GenerateName(int entityId)
    {
        return _entityCollection.GenerateName(entityId);
    }
}