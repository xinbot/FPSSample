using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

[DisableAutoCreation]
public class CreateProjectileMovementCollisionQueries : BaseComponentSystem
{
    private ComponentGroup _group;

    public CreateProjectileMovementCollisionQueries(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(UpdateProjectileFlag), typeof(ProjectileData),
            ComponentType.Subtractive<DespawningEntity>());
    }

    protected override void OnUpdate()
    {
        var entityArray = _group.GetEntityArray();
        var projectileDataArray = _group.GetComponentDataArray<ProjectileData>();
        var time = m_world.WorldTime;
        for (var i = 0; i < projectileDataArray.Length; i++)
        {
            var projectileData = projectileDataArray[i];
            if (projectileData.ImpactTick > 0)
            {
                continue;
            }

            var entity = entityArray[i];

            var collisionTestTick = time.Tick - projectileData.CollisionCheckTickDelay;

            var totalMoveDuration = time.DurationSinceTick(projectileData.StartTick);
            var totalMoveDist = totalMoveDuration * projectileData.Settings.velocity;

            var dir = Vector3.Normalize(projectileData.EndPos - projectileData.StartPos);
            var newPosition = (Vector3) projectileData.StartPos + dir * totalMoveDist;
            var moveDist = math.distance(projectileData.Position, newPosition);

            var collisionMask = ~(1U << projectileData.TeamId);

            var queryReceiver = World.GetExistingManager<RaySphereQueryReceiver>();
            projectileData.RayQueryId = queryReceiver.RegisterQuery(new RaySphereQueryReceiver.Query
            {
                HitCollisionTestTick = collisionTestTick,
                Origin = projectileData.Position,
                Direction = dir,
                Distance = moveDist,
                Radius = projectileData.Settings.collisionRadius,
                Mask = collisionMask,
                ExcludeOwner = projectileData.ProjectileOwner,
            });

            PostUpdateCommands.SetComponent(entity, projectileData);
        }
    }
}

[DisableAutoCreation]
public class HandleProjectileMovementCollisionQuery : BaseComponentSystem
{
    private ComponentGroup _group;

    public HandleProjectileMovementCollisionQuery(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(UpdateProjectileFlag), typeof(ProjectileData),
            ComponentType.Subtractive<DespawningEntity>());
    }

    protected override void OnUpdate()
    {
        var entityArray = _group.GetEntityArray();
        var projectileDataArray = _group.GetComponentDataArray<ProjectileData>();
        var queryReceiver = World.GetExistingManager<RaySphereQueryReceiver>();
        for (var i = 0; i < projectileDataArray.Length; i++)
        {
            var projectileData = projectileDataArray[i];
            if (projectileData.ImpactTick > 0)
            {
                continue;
            }

            RaySphereQueryReceiver.Query query;
            RaySphereQueryReceiver.QueryResult queryResult;
            queryReceiver.GetResult(projectileData.RayQueryId, out query, out queryResult);

            var projectileVec = projectileData.EndPos - projectileData.StartPos;
            var projectileDir = Vector3.Normalize(projectileVec);
            var newPosition = (Vector3) projectileData.Position + projectileDir * query.Distance;

            var impact = queryResult.Hit == 1;
            if (impact)
            {
                projectileData.Impacted = 1;
                projectileData.ImpactPos = queryResult.HitPoint;
                projectileData.ImpactNormal = queryResult.HitNormal;
                projectileData.ImpactTick = m_world.WorldTime.Tick;

                // Owner can deSpawn while projectile is in flight, so we need to make sure we don't send non existing instigator
                var damageInstigator = EntityManager.Exists(projectileData.ProjectileOwner)
                    ? projectileData.ProjectileOwner
                    : Entity.Null;

                var collisionHit = queryResult.HitCollisionOwner != Entity.Null;
                if (collisionHit)
                {
                    if (damageInstigator != Entity.Null)
                    {
                        if (EntityManager.HasComponent<DamageEvent>(queryResult.HitCollisionOwner))
                        {
                            var damageEventBuffer = EntityManager.GetBuffer<DamageEvent>(queryResult.HitCollisionOwner);
                            DamageEvent.AddEvent(damageEventBuffer, damageInstigator,
                                projectileData.Settings.impactDamage, projectileDir,
                                projectileData.Settings.impactImpulse);
                        }
                    }
                }

                if (projectileData.Settings.splashDamage.radius > 0)
                {
                    if (damageInstigator != Entity.Null)
                    {
                        var collisionMask = ~(1 << projectileData.TeamId);
                        SplashDamageRequest.Create(PostUpdateCommands, query.HitCollisionTestTick, damageInstigator,
                            queryResult.HitPoint, collisionMask, projectileData.Settings.splashDamage);
                    }
                }

                newPosition = queryResult.HitPoint;
            }

            if (ProjectileModuleServer.DrawDebug.IntValue == 1)
            {
                var color = impact ? Color.red : Color.green;
                Debug.DrawLine(projectileData.Position, newPosition, color, 2);
                DebugDraw.Sphere(newPosition, 0.1f, color, impact ? 2 : 0);
            }

            projectileData.Position = newPosition;

            PostUpdateCommands.SetComponent(entityArray[i], projectileData);
        }
    }
}

[DisableAutoCreation]
public class DeSpawnProjectiles : BaseComponentSystem
{
    private ComponentGroup _group;

    public DeSpawnProjectiles(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(ProjectileData));
    }

    protected override void OnUpdate()
    {
        var time = m_world.WorldTime;
        var entityArray = _group.GetEntityArray();
        var projectileDataArray = _group.GetComponentDataArray<ProjectileData>();
        for (var i = 0; i < projectileDataArray.Length; i++)
        {
            var projectileData = projectileDataArray[i];
            if (projectileData.ImpactTick > 0)
            {
                if (m_world.WorldTime.DurationSinceTick(projectileData.ImpactTick) > 1.0f)
                {
                    PostUpdateCommands.AddComponent(entityArray[i], new DespawningEntity());
                }

                continue;
            }

            var age = time.DurationSinceTick(projectileData.StartTick);
            var tooOld = age > projectileData.MAXAge;
            if (tooOld)
            {
                PostUpdateCommands.AddComponent(entityArray[i], new DespawningEntity());
            }
        }
    }
}