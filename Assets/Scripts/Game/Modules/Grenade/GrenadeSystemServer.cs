using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Profiling;

public struct GrenadeSpawnRequest : IComponentData
{
    public WeakAssetReference AssetGuid;
    public Vector3 Position;
    public Vector3 Velocity;
    public Entity Owner;
    public int TeamId;

    public static void Create(EntityCommandBuffer commandBuffer, WeakAssetReference assetGuid, Vector3 position,
        Vector3 velocity, Entity owner, int teamId)
    {
        var data = new GrenadeSpawnRequest();
        data.AssetGuid = assetGuid;
        data.Position = position;
        data.Velocity = velocity;
        data.Owner = owner;
        data.TeamId = teamId;

        var entity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(entity, data);
    }
}

public class HandleGrenadeRequest : BaseComponentDataSystem<GrenadeSpawnRequest>
{
    private readonly BundledResourceManager _resourceManager;

    public HandleGrenadeRequest(GameWorld world, BundledResourceManager resourceManager) : base(world)
    {
        _resourceManager = resourceManager;
    }

    protected override void Update(Entity entity, GrenadeSpawnRequest request)
    {
        var grenadeEntity = _resourceManager.CreateEntity(request.AssetGuid);

        var internalState = EntityManager.GetComponentData<Grenade.InternalState>(grenadeEntity);
        internalState.StartTick = m_world.WorldTime.Tick;
        internalState.Owner = request.Owner;
        internalState.TeamId = request.TeamId;
        internalState.Velocity = request.Velocity;
        internalState.Position = request.Position;
        EntityManager.SetComponentData(grenadeEntity, internalState);

        PostUpdateCommands.DestroyEntity(entity);
    }
}

[DisableAutoCreation]
public class StartGrenadeMovement : BaseComponentSystem
{
    private ComponentGroup _group;

    public StartGrenadeMovement(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(Grenade.Settings), typeof(Grenade.InternalState));
    }

    protected override void OnUpdate()
    {
        var time = m_world.WorldTime;

        // Update movements  
        var entityArray = _group.GetEntityArray();
        var settingsArray = _group.GetComponentDataArray<Grenade.Settings>();
        var internalStateArray = _group.GetComponentDataArray<Grenade.InternalState>();
        for (var i = 0; i < internalStateArray.Length; i++)
        {
            var internalState = internalStateArray[i];

            if (internalState.Active == 0 || math.length(internalState.Velocity) < 0.5f)
            {
                continue;
            }

            var entity = entityArray[i];
            var settings = settingsArray[i];

            // Crate movement query
            var startPos = internalState.Position;
            var newVelocity = internalState.Velocity - new float3(0, 1, 0) * settings.gravity * time.TickDuration;
            var deltaPos = newVelocity * time.TickDuration;

            internalState.Position = startPos + deltaPos;
            internalState.Velocity = newVelocity;

            var collisionMask = ~(1U << internalState.TeamId);

            // Setup new collision query
            var queryReceiver = World.GetExistingManager<RaySphereQueryReceiver>();
            internalState.RayQueryId = queryReceiver.RegisterQuery(new RaySphereQueryReceiver.Query()
            {
                HitCollisionTestTick = time.Tick,
                Origin = startPos,
                Direction = math.normalize(newVelocity),
                Distance = math.length(deltaPos) + settings.collisionRadius,
                Radius = settings.proximityTriggerDist,
                Mask = collisionMask,
                ExcludeOwner = time.DurationSinceTick(internalState.StartTick) < 0.2f
                    ? internalState.Owner
                    : Entity.Null,
            });

            EntityManager.SetComponentData(entity, internalState);
        }
    }
}

[DisableAutoCreation]
public class FinalizeGrenadeMovement : BaseComponentSystem
{
    private ComponentGroup _group;

    public FinalizeGrenadeMovement(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(Grenade.Settings), typeof(Grenade.InternalState),
            typeof(Grenade.InterpolatedState));
    }

    protected override void OnUpdate()
    {
        Profiler.BeginSample("FinalizeGrenadeMovement");

        var time = m_world.WorldTime;
        var queryReceiver = World.GetExistingManager<RaySphereQueryReceiver>();

        var grenadeEntityArray = _group.GetEntityArray();
        var settingsArray = _group.GetComponentDataArray<Grenade.Settings>();
        var internalStateArray = _group.GetComponentDataArray<Grenade.InternalState>();
        var interpolatedStateArray = _group.GetComponentDataArray<Grenade.InterpolatedState>();

        for (var i = 0; i < internalStateArray.Length; i++)
        {
            var internalState = internalStateArray[i];
            var entity = grenadeEntityArray[i];

            if (internalState.Active == 0)
            {
                // Keep grenades around for a short duration so short-lived grenades gets a chance to get replicated 
                // and explode effect played
                if (m_world.WorldTime.DurationSinceTick(internalState.ExplodeTick) > 1.0f)
                {
                    m_world.RequestDespawn(PostUpdateCommands, entity);
                }

                continue;
            }

            var settings = settingsArray[i];
            var interpolatedState = interpolatedStateArray[i];
            var hitCollisionOwner = Entity.Null;
            if (internalState.RayQueryId != -1)
            {
                RaySphereQueryReceiver.Query query;
                RaySphereQueryReceiver.QueryResult queryResult;
                queryReceiver.GetResult(internalState.RayQueryId, out query, out queryResult);
                internalState.RayQueryId = -1;

                // If grenade hit something that was no hitCollision it is environment and grenade should bounce
                if (queryResult.Hit == 1 && queryResult.HitCollisionOwner == Entity.Null)
                {
                    var moveDir = math.normalize(internalState.Velocity);
                    var moveVel = math.length(internalState.Velocity);

                    internalState.Position = queryResult.HitPoint + queryResult.HitNormal * settings.collisionRadius;

                    moveDir = Vector3.Reflect(moveDir, queryResult.HitNormal);
                    internalState.Velocity = moveDir * moveVel * settings.bounciness;

                    if (moveVel > 1.0f)
                    {
                        interpolatedState.bouncetick = m_world.WorldTime.Tick;
                    }
                }

                if (queryResult.HitCollisionOwner != Entity.Null)
                {
                    internalState.Position = queryResult.HitPoint;
                }

                hitCollisionOwner = queryResult.HitCollisionOwner;
            }

            // Should we explode ?
            var timeout = time.DurationSinceTick(internalState.StartTick) > settings.maxLifetime;
            if (timeout || hitCollisionOwner != Entity.Null)
            {
                internalState.Active = 0;
                internalState.ExplodeTick = time.Tick;
                interpolatedState.exploded = 1;

                if (settings.splashDamage.radius > 0)
                {
                    var collisionMask = ~(1 << internalState.TeamId);

                    SplashDamageRequest.Create(PostUpdateCommands, time.Tick, internalState.Owner,
                        internalState.Position,
                        collisionMask, settings.splashDamage);
                }
            }

            interpolatedState.position = internalState.Position;

            DebugDraw.Sphere(interpolatedState.position, settings.collisionRadius, Color.red);

            EntityManager.SetComponentData(entity, internalState);
            EntityManager.SetComponentData(entity, interpolatedState);
        }

        Profiler.EndSample();
    }
}