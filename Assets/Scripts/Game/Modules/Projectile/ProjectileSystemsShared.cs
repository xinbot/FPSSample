using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

[DisableAutoCreation]
public class CreateProjectileMovementCollisionQueries : BaseComponentSystem
{
    ComponentGroup ProjectileGroup;

    public CreateProjectileMovementCollisionQueries(GameWorld world) : base(world) { }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        ProjectileGroup = GetComponentGroup(typeof(UpdateProjectileFlag), typeof(ProjectileData), 
            ComponentType.Subtractive<DespawningEntity>());
    }

    protected override void OnUpdate()
    {
        var entityArray = ProjectileGroup.GetEntityArray();
        var projectileDataArray = ProjectileGroup.GetComponentDataArray<ProjectileData>();
        var time = m_world.WorldTime;
        for (var i = 0; i < projectileDataArray.Length; i++)
        {
            var projectileData = projectileDataArray[i];
            if (projectileData.ImpactTick > 0)
                continue;

            var entity = entityArray[i];

            var collisionTestTick = time.Tick - projectileData.CollisionCheckTickDelay;

            var totalMoveDuration = time.DurationSinceTick(projectileData.StartTick);
            var totalMoveDist = totalMoveDuration * projectileData.Settings.velocity;

            var dir = Vector3.Normalize(projectileData.EndPos - projectileData.StartPos);
            var newPosition = (Vector3)projectileData.StartPos + dir * totalMoveDist;
            var moveDist = math.distance(projectileData.Position, newPosition);

            var collisionMask = ~(1U << projectileData.TeamId);

            var queryReciever = World.GetExistingManager<RaySphereQueryReciever>();
            projectileData.RayQueryId = queryReciever.RegisterQuery(new RaySphereQueryReciever.Query()
            {
                hitCollisionTestTick = collisionTestTick,
                origin = projectileData.Position,
                direction = dir,
                distance = moveDist,
                radius = projectileData.Settings.collisionRadius,
                mask = collisionMask,
                ExcludeOwner = projectileData.ProjectileOwner,
            });
            PostUpdateCommands.SetComponent(entity,projectileData);
        }
    }
}

[DisableAutoCreation]
public class HandleProjectileMovementCollisionQuery : BaseComponentSystem
{
    ComponentGroup ProjectileGroup;

    public HandleProjectileMovementCollisionQuery(GameWorld world) : base(world) { }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        ProjectileGroup = GetComponentGroup(typeof(UpdateProjectileFlag), typeof(ProjectileData), 
            ComponentType.Subtractive<DespawningEntity>());
    }
    
    protected override void OnUpdate()
    {
        var entityArray = ProjectileGroup.GetEntityArray();
        var projectileDataArray = ProjectileGroup.GetComponentDataArray<ProjectileData>();
        var queryReciever = World.GetExistingManager<RaySphereQueryReciever>();    
        for (var i = 0; i < projectileDataArray.Length; i++)
        {
            var projectileData = projectileDataArray[i];
            
            if (projectileData.ImpactTick > 0)
                continue;
            
            RaySphereQueryReciever.Query query;
            RaySphereQueryReciever.QueryResult queryResult;
            queryReciever.GetResult(projectileData.RayQueryId, out query, out queryResult);
            
            var projectileVec = projectileData.EndPos - projectileData.StartPos;
            var projectileDir = Vector3.Normalize(projectileVec);
            var newPosition = (Vector3)projectileData.Position + projectileDir * query.distance;

            var impact = queryResult.hit == 1;
            if (impact)
            {
                projectileData.Impacted = 1;
                projectileData.ImpactPos = queryResult.hitPoint;
                projectileData.ImpactNormal = queryResult.hitNormal;
                projectileData.ImpactTick = m_world.WorldTime.Tick;

                // Owner can despawn while projectile is in flight, so we need to make sure we dont send non existing instigator
                var damageInstigator = EntityManager.Exists(projectileData.ProjectileOwner) ? projectileData.ProjectileOwner : Entity.Null;

                var collisionHit = queryResult.hitCollisionOwner != Entity.Null;
                if (collisionHit)
                {
                    if (damageInstigator != Entity.Null)
                    {
                        if (EntityManager.HasComponent<DamageEvent>(queryResult.hitCollisionOwner))
                        {
                            var damageEventBuffer = EntityManager.GetBuffer<DamageEvent>(queryResult.hitCollisionOwner);
                            DamageEvent.AddEvent(damageEventBuffer, damageInstigator, projectileData.Settings.impactDamage, projectileDir, projectileData.Settings.impactImpulse);
                        }
                    }
                }

                if (projectileData.Settings.splashDamage.radius > 0)
                {
                    if (damageInstigator != Entity.Null)
                    {
                        var collisionMask = ~(1 << projectileData.TeamId);
                        SplashDamageRequest.Create(PostUpdateCommands, query.hitCollisionTestTick, damageInstigator, queryResult.hitPoint, collisionMask, projectileData.Settings.splashDamage);
                    }
                }

                newPosition = queryResult.hitPoint;
            }

            if (ProjectileModuleServer.DrawDebug.IntValue == 1)
            {
                var color = impact ? Color.red : Color.green;
                Debug.DrawLine(projectileData.Position, newPosition, color, 2);
                DebugDraw.Sphere(newPosition, 0.1f, color, impact ? 2 : 0);
            }

            projectileData.Position = newPosition;
            PostUpdateCommands.SetComponent(entityArray[i],projectileData);
        }
    }
}


[DisableAutoCreation]
public class DespawnProjectiles : BaseComponentSystem
{
    ComponentGroup ProjectileGroup;

    public DespawnProjectiles(GameWorld world) : base(world) { }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        ProjectileGroup = GetComponentGroup(typeof(ProjectileData));
    }
    
    protected override void OnUpdate()
    {
        var time = m_world.WorldTime;
        var entityArray = ProjectileGroup.GetEntityArray();
        var projectileDataArray = ProjectileGroup.GetComponentDataArray<ProjectileData>();
        for (var i = 0; i < projectileDataArray.Length; i++)
        {
            var projectileData = projectileDataArray[i];
            
            if (projectileData.ImpactTick > 0)
            {
                if (m_world.WorldTime.DurationSinceTick(projectileData.ImpactTick) > 1.0f)
                {
                    PostUpdateCommands.AddComponent(entityArray[i],new DespawningEntity());
                }
                continue;
            }

            var age = time.DurationSinceTick(projectileData.StartTick);
            var toOld = age > projectileData.MAXAge;
            if (toOld)
            {
                PostUpdateCommands.AddComponent(entityArray[i],new DespawningEntity());
            }
        }
    }
}



