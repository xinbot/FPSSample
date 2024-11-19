using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

// Added to projectiles that are created locally. System attempts to find a matching projectile coming from server.
// If no match is found when server version should have been found the predicted projectile is deleted.
public struct PredictedProjectile : IComponentData
{
    public PredictedProjectile(int startTick)
    {
        StartTick = startTick;
    }

    public int StartTick;
}

// Added to projectiles that has a client projectile 
public struct ClientProjectileOwner : IComponentData
{
    public Entity ClientProjectile;
}

[DisableAutoCreation]
public class HandleClientProjectileRequests : BaseComponentSystem
{
    private ComponentGroup _group;

    private readonly GameObject _systemRoot;
    private readonly BundledResourceManager _resourceSystem;
    private readonly ProjectileModuleSettings _settings;
    private readonly ClientProjectileFactory _clientProjectileFactory;
    private readonly List<ProjectileRequest> _requestBuffer = new List<ProjectileRequest>(16);

    public HandleClientProjectileRequests(GameWorld world, BundledResourceManager resourceSystem, GameObject systemRoot,
        ClientProjectileFactory clientProjectileFactory) : base(world)
    {
        _resourceSystem = resourceSystem;
        _systemRoot = systemRoot;
        _settings = Resources.Load<ProjectileModuleSettings>("ProjectileModuleSettings");
        _clientProjectileFactory = clientProjectileFactory;
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(ProjectileRequest));
    }

    protected override void OnDestroyManager()
    {
        base.OnDestroyManager();
        Resources.UnloadAsset(_settings);
    }

    protected override void OnUpdate()
    {
        if (_group.CalculateLength() == 0)
        {
            return;
        }

        // Copy requests as spawning will invalidate Group 
        _requestBuffer.Clear();
        var requestArray = _group.GetComponentDataArray<ProjectileRequest>();
        var requestEntityArray = _group.GetEntityArray();
        for (var i = 0; i < requestArray.Length; i++)
        {
            _requestBuffer.Add(requestArray[i]);
            PostUpdateCommands.DestroyEntity(requestEntityArray[i]);
        }

        // Handle requests
        var projectileRegistry = _resourceSystem.GetResourceRegistry<ProjectileRegistry>();
        foreach (var request in _requestBuffer)
        {
            var registryIndex = projectileRegistry.FindIndex(request.ProjectileAssetGuid);
            if (registryIndex == -1)
            {
                GameDebug.LogError("Cant find asset guid in registry");
                continue;
            }

            // Create projectile and initialize
            var projectileEntity = _settings.projectileFactory.Create(EntityManager, _resourceSystem, m_world);
            var projectileData = EntityManager.GetComponentData<ProjectileData>(projectileEntity);

            projectileData.SetupFromRequest(request, registryIndex);
            projectileData.Initialize(projectileRegistry);
            EntityManager.SetComponentData(projectileEntity, projectileData);
            EntityManager.AddComponentData(projectileEntity, new PredictedProjectile(request.StartTick));
            EntityManager.AddComponentData(projectileEntity, new UpdateProjectileFlag());

            if (ProjectileModuleClient.LogInfo.IntValue > 0)
            {
                GameDebug.Log("New predicted projectile created: " + projectileEntity);
            }

            // Create client projectile
            var clientProjectileEntity = _clientProjectileFactory.CreateClientProjectile(projectileEntity);
            EntityManager.AddComponentData(clientProjectileEntity, new UpdateProjectileFlag());

            if (ProjectileModuleClient.DrawDebug.IntValue == 1)
            {
                Debug.DrawLine(projectileData.StartPos, projectileData.EndPos, Color.cyan, 1.0f);
            }
        }
    }
}

public static class ProjectilesSystemsClient
{
    public static void Update(GameWorld world, EntityCommandBuffer commandBuffer, ClientProjectile clientProjectile)
    {
        var deltaTime = world.frameDuration;
        if (clientProjectile.Impacted)
        {
            return;
        }

        // When projectile disappears we hide client projectile
        if (clientProjectile.Projectile == Entity.Null)
        {
            clientProjectile.SetVisible(false);
            return;
        }

        var projectileData = world.GetEntityManager().GetComponentData<ProjectileData>(clientProjectile.Projectile);
        var aliveDuration = world.WorldTime.DurationSinceTick(projectileData.StartTick);

        // Interpolation delay can cause projectiles to be spawned before they should be shown.  
        if (aliveDuration < 0)
        {
            return;
        }

        if (!clientProjectile.isVisible)
        {
            clientProjectile.SetVisible(true);
        }

        var dir = Vector3.Normalize(projectileData.EndPos - projectileData.StartPos);
        var moveDist = aliveDuration * projectileData.Settings.velocity;
        var pos = (Vector3) projectileData.StartPos + dir * moveDist;
        var rot = Quaternion.LookRotation(dir);

        var worldOffset = Vector3.zero;
        if (clientProjectile.OffsetScale > 0.0f)
        {
            clientProjectile.OffsetScale -= deltaTime / clientProjectile.offsetScaleDuration;
            worldOffset = rot * clientProjectile.StartOffset * clientProjectile.OffsetScale;
        }

        if (projectileData.Impacted == 1 && !clientProjectile.Impacted)
        {
            clientProjectile.Impacted = true;

            if (clientProjectile.impactEffect != null)
            {
                world.GetECSWorld().GetExistingManager<HandleSpatialEffectRequests>().Request(
                    clientProjectile.impactEffect,
                    projectileData.ImpactPos, Quaternion.LookRotation(projectileData.ImpactNormal));
            }

            clientProjectile.SetVisible(false);
        }

        clientProjectile.transform.position = pos + worldOffset;

        clientProjectile.Roll += deltaTime * clientProjectile.rotationSpeed;
        var roll = Quaternion.Euler(0, 0, clientProjectile.Roll);
        clientProjectile.transform.rotation = rot * roll;

        if (ProjectileModuleClient.DrawDebug.IntValue == 1)
        {
            Debug.DrawLine(projectileData.StartPos, pos, Color.red, 1.0f);
            DebugDraw.Sphere(clientProjectile.transform.position, 0.1f, Color.cyan, 1.0f);
        }
    }
}

[DisableAutoCreation]
public class UpdateClientProjectilesPredicted : BaseComponentSystem<ClientProjectile>
{
    public UpdateClientProjectilesPredicted(GameWorld world) : base(world)
    {
        ExtraComponentRequirements = new[] {ComponentType.Create<UpdateProjectileFlag>()};
    }

    protected override void Update(Entity entity, ClientProjectile clientProjectile)
    {
        ProjectilesSystemsClient.Update(m_world, PostUpdateCommands, clientProjectile);
    }
}

[DisableAutoCreation]
public class UpdateClientProjectilesNonPredicted : BaseComponentSystem<ClientProjectile>
{
    public UpdateClientProjectilesNonPredicted(GameWorld world) : base(world)
    {
        ExtraComponentRequirements = new[] {ComponentType.Subtractive<UpdateProjectileFlag>()};
    }

    protected override void Update(Entity entity, ClientProjectile clientProjectile)
    {
        ProjectilesSystemsClient.Update(m_world, PostUpdateCommands, clientProjectile);
    }
}

[DisableAutoCreation]
[AlwaysUpdateSystemAttribute]
public class HandleProjectileSpawn : BaseComponentSystem
{
    private readonly GameObject _systemRoot;
    private readonly BundledResourceManager _resourceSystem;

    private ComponentGroup _predictedProjectileGroup;
    private ComponentGroup _inComingProjectileGroup;

    private readonly ClientProjectileFactory _clientProjectileFactory;
    private readonly List<Entity> _addClientProjArray = new List<Entity>(32);

    public HandleProjectileSpawn(GameWorld world, GameObject systemRoot, BundledResourceManager resourceSystem,
        ClientProjectileFactory projectileFactory) : base(world)
    {
        _systemRoot = systemRoot;
        _resourceSystem = resourceSystem;
        _clientProjectileFactory = projectileFactory;
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();

        _predictedProjectileGroup = GetComponentGroup(typeof(ProjectileData), typeof(PredictedProjectile),
            ComponentType.Subtractive<DespawningEntity>());

        _inComingProjectileGroup =
            GetComponentGroup(typeof(ProjectileData), ComponentType.Subtractive<ClientProjectileOwner>());
    }

    protected override void OnUpdate()
    {
        if (_inComingProjectileGroup.CalculateLength() > 0)
        {
            HandleIncomingProjectiles();
        }
    }

    private void HandleIncomingProjectiles()
    {
        // Run through all incoming projectiles. Attempt to match with predicted projectiles.
        // If none is found add to addClientProjArray so a client projectile will be created for projectile
        _addClientProjArray.Clear();

        var inEntityArray = _inComingProjectileGroup.GetEntityArray();
        var inProjectileDataArray = _inComingProjectileGroup.GetComponentDataArray<ProjectileData>();

        var predictedProjectileArray = _predictedProjectileGroup.GetComponentDataArray<ProjectileData>();
        var predictedProjectileEntities = _predictedProjectileGroup.GetEntityArray();
        for (var j = 0; j < inProjectileDataArray.Length; j++)
        {
            var inProjectileData = inProjectileDataArray[j];
            var inProjectileEntity = inEntityArray[j];
            if (ProjectileModuleClient.LogInfo.IntValue > 0)
            {
                GameDebug.Log(string.Format("Projectile spawn:" + inProjectileEntity));
            }

            // Initialize new projectile with correct settings
            var projectileRegistry = _resourceSystem.GetResourceRegistry<ProjectileRegistry>();
            inProjectileData.Initialize(projectileRegistry);
            m_world.GetEntityManager().SetComponentData(inProjectileEntity, inProjectileData);

            // I new projectile in not predicted, attempt to find a predicted that that should link to it 
            var matchFound = false;
            if (!m_world.GetEntityManager().HasComponent<PredictedProjectile>(inProjectileEntity))
            {
                for (var i = 0; i < predictedProjectileEntities.Length; i++)
                {
                    var predictedProjectile = predictedProjectileArray[i];

                    // Attempt to find matching 
                    if (predictedProjectile.TypeId != inProjectileData.TypeId
                        || predictedProjectile.StartTick != inProjectileData.StartTick
                        || predictedProjectile.ProjectileOwner != inProjectileData.ProjectileOwner
                        || math.distance(predictedProjectile.StartPos, inProjectileData.StartPos) > 0.1f
                        || math.distance(predictedProjectile.EndPos, inProjectileData.EndPos) > 0.1f)
                    {
                        continue;
                    }

                    // Match found
                    matchFound = true;
                    var predictedProjectileEntity = predictedProjectileEntities[i];
                    if (ProjectileModuleClient.LogInfo.IntValue > 0)
                    {
                        GameDebug.Log("ProjectileSystemClient. Predicted projectile" + predictedProjectileEntity +
                                      " matched with " + inProjectileEntity + " from server. startTick:" +
                                      inProjectileData.StartTick);
                    }

                    // Reassign client projectile to use new projectile
                    var clientProjectileOwner =
                        EntityManager.GetComponentData<ClientProjectileOwner>(predictedProjectileEntity);
                    var clientProjectile =
                        EntityManager.GetComponentObject<ClientProjectile>(clientProjectileOwner.ClientProjectile);
                    clientProjectile.Projectile = inProjectileEntity;
                    PostUpdateCommands.AddComponent(inProjectileEntity, clientProjectileOwner);
                    PostUpdateCommands.AddComponent(inProjectileEntity, new UpdateProjectileFlag());

                    // Destroy predicted
                    if (ProjectileModuleClient.LogInfo.IntValue > 0)
                    {
                        GameDebug.Log("ProjectileSystemClient. Destroying predicted:" + predictedProjectileEntity);
                    }

                    PostUpdateCommands.RemoveComponent(predictedProjectileEntity, typeof(ClientProjectileOwner));
                    PostUpdateCommands.RemoveComponent(predictedProjectileEntity, typeof(UpdateProjectileFlag));
                    m_world.RequestDespawn(PostUpdateCommands, predictedProjectileEntity);
                    break;
                }
            }

            if (ProjectileModuleClient.DrawDebug.IntValue == 1)
            {
                var color = matchFound ? Color.green : Color.yellow;
                DebugDraw.Sphere(inProjectileData.StartPos, 0.12f, color, 1.0f);
                Debug.DrawLine(inProjectileData.StartPos, inProjectileData.EndPos, color, 1.0f);
            }

            // If match was found the new projectile has already been assigned an existing client projectile
            if (!matchFound)
            {
                _addClientProjArray.Add(inProjectileEntity);
            }
        }

        // Create client projectiles. This is deferred as we cant create
        // client projectiles while iterating over component array 
        foreach (var projectileEntity in _addClientProjArray)
        {
            if (ProjectileModuleClient.LogInfo.IntValue > 0)
            {
                var projectileData = EntityManager.GetComponentData<ProjectileData>(projectileEntity);
                GameDebug.Log("Creating client projectile for projectile:" + projectileData);
            }

            _clientProjectileFactory.CreateClientProjectile(projectileEntity);
        }
    }
}

[DisableAutoCreation]
[AlwaysUpdateSystemAttribute]
public class RemoveMisPredictedProjectiles : BaseComponentSystem
{
    private ComponentGroup _group;

    public RemoveMisPredictedProjectiles(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group =
            GetComponentGroup(typeof(PredictedProjectile), ComponentType.Subtractive<DespawningEntity>());
    }

    protected override void OnUpdate()
    {
        // Remove all predicted projectiles that should have been acknowledged by now
        var predictedProjectileArray = _group.GetComponentDataArray<PredictedProjectile>();
        var predictedProjectileEntityArray = _group.GetEntityArray();
        for (var i = 0; i < predictedProjectileArray.Length; i++)
        {
            var predictedEntity = predictedProjectileArray[i];
            if (predictedEntity.StartTick >= m_world.LastServerTick)
            {
                continue;
            }

            var entity = predictedProjectileEntityArray[i];
            PostUpdateCommands.AddComponent(entity, new DespawningEntity());

            if (ProjectileModuleClient.LogInfo.IntValue > 0)
            {
                GameDebug.Log(
                    $"<color=red>Predicted projectile {entity} destroyed as it was not verified. startTick:{predictedEntity.StartTick}]</color>");
            }
        }
    }
}

[DisableAutoCreation]
[AlwaysUpdateSystemAttribute]
public class DeSpawnClientProjectiles : BaseComponentSystem
{
    private ComponentGroup _group;

    private readonly ClientProjectileFactory _clientProjectileFactory;
    private readonly List<Entity> _clientProjectiles = new List<Entity>(32);

    public DeSpawnClientProjectiles(GameWorld world, ClientProjectileFactory clientProjectileFactory) : base(world)
    {
        _clientProjectileFactory = clientProjectileFactory;
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();

        _group = GetComponentGroup(typeof(ClientProjectileOwner), typeof(DespawningEntity));
    }

    protected override void OnUpdate()
    {
        // Remove all client projectiles that are has deSpawning projectile
        var clientProjectileOwnerArray = _group.GetComponentDataArray<ClientProjectileOwner>();
        if (clientProjectileOwnerArray.Length > 0)
        {
            _clientProjectiles.Clear();
            for (var i = 0; i < clientProjectileOwnerArray.Length; i++)
            {
                var clientProjectileOwner = clientProjectileOwnerArray[i];
                _clientProjectiles.Add(clientProjectileOwner.ClientProjectile);
            }

            for (var i = 0; i < _clientProjectiles.Count; i++)
            {
                _clientProjectileFactory.DestroyClientProjectile(_clientProjectiles[i], PostUpdateCommands);
                if (ProjectileModuleClient.LogInfo.IntValue > 0)
                {
                    GameDebug.Log("Projectile deSpawned so deSpawn of client projectile requested");
                }
            }
        }
    }
}

public class ClientProjectileFactory
{
    private class Pool
    {
        public int PoolIndex;
        public GameObject Prefab;
        public readonly List<GameObjectEntity> Instances = new List<GameObjectEntity>();
        public readonly Queue<int> FreeList = new Queue<int>();
    }

    private readonly Pool[] _pools;

    private readonly GameWorld _world;
    private readonly EntityManager _entityManager;
    private readonly GameObject _systemRoot;

    public ClientProjectileFactory(GameWorld world, EntityManager entityManager, GameObject systemRoot,
        BundledResourceManager resourceSystem)
    {
        _world = world;
        _entityManager = entityManager;
        _systemRoot = systemRoot;

        var projectileRegistry = resourceSystem.GetResourceRegistry<ProjectileRegistry>();
        var typeCount = projectileRegistry.entries.Count;
        _pools = new Pool[typeCount];

        for (var i = 0; i < projectileRegistry.entries.Count; i++)
        {
            var pool = new Pool();

            var entry = projectileRegistry.entries[i];
            pool.Prefab = (GameObject) resourceSystem.GetSingleAssetResource(entry.definition.clientProjectilePrefab);
            pool.PoolIndex = i;
            Allocate(pool, entry.definition.clientProjectileBufferSize);
            _pools[i] = pool;
        }
    }

    private int Reserve(Pool pool)
    {
        if (pool.FreeList.Count == 0)
        {
            Allocate(pool, 5);
        }

        return pool.FreeList.Dequeue();
    }

    private void Free(Pool pool, int index)
    {
        GameDebug.Assert(!pool.FreeList.Contains(index));
        pool.FreeList.Enqueue(index);
    }

    private void Allocate(Pool pool, int count)
    {
        var startIndex = pool.Instances.Count;
        for (var i = 0; i < count; i++)
        {
            var bufferIndex = startIndex + i;
            var projectile = _world.Spawn<GameObjectEntity>(pool.Prefab);
            pool.FreeList.Enqueue(bufferIndex);
            if (_systemRoot != null)
            {
                projectile.transform.SetParent(_systemRoot.transform);
            }

            var entity = projectile.Entity;
            var clientProjectile = _entityManager.GetComponentObject<ClientProjectile>(entity);
            clientProjectile.PoolIndex = pool.PoolIndex;
            clientProjectile.BufferIndex = bufferIndex;
            clientProjectile.SetVisible(false);
            clientProjectile.gameObject.SetActive(false);

            pool.Instances.Add(projectile);
        }
    }

    public Entity CreateClientProjectile(Entity projectileEntity)
    {
        var projectileData = _entityManager.GetComponentData<ProjectileData>(projectileEntity);

        // Create client projectile
        var pool = _pools[projectileData.TypeId];
        var instanceIndex = Reserve(pool);
        var gameObjectEntity = pool.Instances[instanceIndex];
        gameObjectEntity.gameObject.SetActive(true);

        var clientProjectileEntity = gameObjectEntity.Entity;
        var clientProjectile = _entityManager.GetComponentObject<ClientProjectile>(clientProjectileEntity);

        GameDebug.Assert(clientProjectile.Projectile == Entity.Null, "Entity not null");
        clientProjectile.Projectile = projectileEntity;
        clientProjectile.SetVisible(false);

        if (ProjectileModuleClient.LogInfo.IntValue > 0)
        {
            GameDebug.Log($"Creating client projectile {clientProjectile} for projectile {projectileEntity}]");
        }

        // Add clientProjectileOwner to projectile
        var clientProjectileOwner = new ClientProjectileOwner
        {
            ClientProjectile = clientProjectileEntity
        };
        _entityManager.AddComponentData(projectileEntity, clientProjectileOwner);

        return clientProjectileEntity;
    }

    public void DestroyClientProjectile(Entity clientProjectileEntity, EntityCommandBuffer commandBuffer)
    {
        var clientProjectile = _entityManager.GetComponentObject<ClientProjectile>(clientProjectileEntity);

        Free(_pools[clientProjectile.PoolIndex], clientProjectile.BufferIndex);

        clientProjectile.gameObject.SetActive(false);
        clientProjectile.Reset();
    }
}