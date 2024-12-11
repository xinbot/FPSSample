using System;
using System.Collections.Generic;
using Primitives;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using Unity.Jobs;

[Serializable]
public struct SplashDamageSettings
{
    public float radius;
    public float falloffStartRadius;

    public float damage;
    public float minDamage;

    public float impulse;
    public float minImpulse;

    public float ownerDamageFraction;
}

public struct SplashDamageRequest : IComponentData
{
    public int Tick;
    public int CollisionMask;

    public float3 Center;
    public Entity Instigator;

    public SplashDamageSettings Settings;

    public static void Create(EntityCommandBuffer commandBuffer, int tick, Entity instigator, float3 center,
        int collisionMask, SplashDamageSettings settings)
    {
        var request = new SplashDamageRequest
        {
            Tick = tick,
            CollisionMask = collisionMask,
            Center = center,
            Instigator = instigator,
            Settings = settings
        };

        var entity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(entity, request);
    }
}

public struct ClosestCollision
{
    public HitCollision HitCollision;
    public Vector3 ClosestPoint;
    public float Dist;
}

[DisableAutoCreation]
public class HandleSplashDamageRequests : BaseComponentSystem
{
    private ComponentGroup _requestGroup;
    private ComponentGroup _colliderGroup;

    private readonly List<HitCollisionData.CollisionResult> _resultsBuffer =
        new List<HitCollisionData.CollisionResult>(32);

    private readonly List<Entity> _resultsOwnerBuffer = new List<Entity>(32);

    private readonly Collider[] _colliderBuffer = new Collider[128];
    private readonly int _hitCollisionLayer;

    public HandleSplashDamageRequests(GameWorld world) : base(world)
    {
        _hitCollisionLayer = LayerMask.NameToLayer("hitcollision_enabled");
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _requestGroup = GetComponentGroup(typeof(SplashDamageRequest));
        _colliderGroup = GetComponentGroup(typeof(HitCollisionHistory));
    }

    protected override void OnUpdate()
    {
        var requestEntityArray = _requestGroup.GetEntityArray();
        var requestArray = _requestGroup.GetComponentDataArray<SplashDamageRequest>();
        var requestCount = requestArray.Length;

        var hitCollisionEntityArray = _colliderGroup.GetEntityArray();

        // Broad phase hit collision check
        var entityArray = new NativeArray<Entity>(hitCollisionEntityArray.Length, Allocator.TempJob);
        var boundsArray = new NativeArray<sphere>[requestCount];
        var broadPhaseResultArray = new NativeList<Entity>[requestCount];
        var broadPhaseHandleArray = new NativeArray<JobHandle>(requestCount, Allocator.Temp);

        // TODO (mogensh) find faster/easier way to copy entity array to native
        for (int i = 0; i < hitCollisionEntityArray.Length; i++)
        {
            entityArray[i] = hitCollisionEntityArray[i];
        }

        for (var i = 0; i < requestCount; i++)
        {
            boundsArray[i] = new NativeArray<sphere>(hitCollisionEntityArray.Length, Allocator.TempJob);
            broadPhaseResultArray[i] = new NativeList<Entity>(hitCollisionEntityArray.Length, Allocator.TempJob);
        }

        for (int i = 0; i < requestCount; i++)
        {
            GetBounds(EntityManager, entityArray, requestArray[i].Tick, ref boundsArray[i]);
        }

        for (int i = 0; i < requestCount; i++)
        {
            var request = requestArray[i];
            var broadPhaseJob = new BroadPhaseSphereOverlapJob
            {
                Entities = entityArray,
                Bounds = boundsArray[i],
                Sphere = new sphere(request.Center, request.Settings.radius),
                Result = broadPhaseResultArray[i],
            };
            broadPhaseHandleArray[i] = broadPhaseJob.Schedule();
        }

        var broadPhaseHandle = JobHandle.CombineDependencies(broadPhaseHandleArray);
        broadPhaseHandle.Complete();

        for (var i = 0; i < requestArray.Length; i++)
        {
            var request = requestArray[i];

            // HitCollision damage
            {
                var requestSphere = new sphere(request.Center, request.Settings.radius);
                var broadPhaseResult = broadPhaseResultArray[i];
                var forceIncluded = request.Settings.ownerDamageFraction > 0 ? request.Instigator : Entity.Null;

                _resultsBuffer.Clear();
                _resultsOwnerBuffer.Clear();

                SphereOverlapAll(EntityManager, ref broadPhaseResult,
                    request.Tick, request.CollisionMask, Entity.Null, forceIncluded, requestSphere, _resultsBuffer,
                    _resultsOwnerBuffer);

                for (int j = 0; j < _resultsBuffer.Count; j++)
                {
                    Damage(request.Center, ref request.Settings, request.Instigator, _resultsOwnerBuffer[j],
                        _resultsBuffer[j].PrimitiveCenter);
                }
            }

            // Environment damage
            {
                var hitColliderMask = 1 << _hitCollisionLayer;
                var count = Physics.OverlapSphereNonAlloc(request.Center, request.Settings.radius, _colliderBuffer,
                    hitColliderMask);

                var colliderCollections = new Dictionary<Entity, ClosestCollision>();
                GetClosestCollision(_colliderBuffer, count, request.Center, colliderCollections);

                foreach (var collision in colliderCollections.Values)
                {
                    var collisionOwnerEntity = collision.HitCollision.Owner;

                    Damage(request.Center, ref request.Settings, request.Instigator, collisionOwnerEntity,
                        collision.ClosestPoint);
                }
            }

            PostUpdateCommands.DestroyEntity(requestEntityArray[i]);
        }

        broadPhaseHandleArray.Dispose();
        entityArray.Dispose();
        for (var i = 0; i < requestCount; i++)
        {
            boundsArray[i].Dispose();
            broadPhaseResultArray[i].Dispose();
        }
    }

    private void Damage(float3 origin, ref SplashDamageSettings settings, Entity instigator,
        Entity hitCollisionOwnerEntity, float3 centerOfMass)
    {
        // TODO (mogens) dont hardcode center of mass - and dont get from Character. Should be set on hitCollOwner by some other system
        if (EntityManager.HasComponent<Character>(hitCollisionOwnerEntity))
        {
            var charPredictedState = EntityManager.GetComponentData<CharacterPredictedData>(hitCollisionOwnerEntity);
            centerOfMass = charPredictedState.position + Vector3.up * 1.2f;
        }

        // Calc damage
        var damageVector = centerOfMass - origin;
        var damageDirection = math.normalize(damageVector);
        var distance = math.length(damageVector);
        if (distance > settings.radius)
        {
            return;
        }

        var damage = settings.damage;
        var impulse = settings.impulse;
        if (distance > settings.falloffStartRadius)
        {
            var falloffFraction = (distance - settings.falloffStartRadius) /
                                  (settings.radius - settings.falloffStartRadius);
            damage -= (settings.damage - settings.minDamage) * falloffFraction;
            impulse -= (settings.impulse - settings.minImpulse) * falloffFraction;
        }

        if (instigator != Entity.Null && instigator == hitCollisionOwnerEntity)
        {
            damage = damage * settings.ownerDamageFraction;
        }

        //GameDebug.Log(string.Format("SplashDamage. Target:{0} Inst:{1}", collider.hitCollision, m_world.GetGameObjectFromEntity(instigator)));
        var damageEventBuffer = EntityManager.GetBuffer<DamageEvent>(hitCollisionOwnerEntity);
        DamageEvent.AddEvent(damageEventBuffer, instigator, damage, damageDirection, impulse);
    }

    public static void SphereOverlapAll(EntityManager entityManager,
        ref NativeList<Entity> hitCollHistEntityArray, int tick, int mask,
        Entity forceExcluded, Entity forceIncluded, sphere sphere,
        List<HitCollisionData.CollisionResult> results, List<Entity> hitCollisionOwners)
    {
        for (var i = 0; i < hitCollHistEntityArray.Length; i++)
        {
            if (!HitCollisionData.IsRelevant(entityManager, hitCollHistEntityArray[i], mask, forceExcluded,
                forceIncluded))
            {
                continue;
            }

            var entity = hitCollHistEntityArray[i];

            HitCollisionData.CollisionResult collisionResult;
            var hit = HitCollisionData.SphereOverlapSingle(entityManager, entity, tick, sphere, out collisionResult);
            if (hit)
            {
                var hitCollisionData = entityManager.GetComponentData<HitCollisionData>(hitCollHistEntityArray[i]);

                results.Add(collisionResult);
                hitCollisionOwners.Add(hitCollisionData.HitCollisionOwner);
            }
        }
    }

    public static void GetBounds(EntityManager entityManager, NativeArray<Entity> hitCollHistEntityArray, int tick,
        ref NativeArray<sphere> boundsArray)
    {
        for (int i = 0; i < hitCollHistEntityArray.Length; i++)
        {
            var entity = hitCollHistEntityArray[i];

            var collData = entityManager.GetComponentData<HitCollisionData>(entity);
            var historyBuffer = entityManager.GetBuffer<HitCollisionData.BoundsHistory>(entity);

            var histIndex = collData.GetHistoryIndex(tick);
            var boundSphere = primlib.sphere(historyBuffer[histIndex].Pos, collData.BoundsRadius);
            boundsArray[i] = boundSphere;
        }
    }

    public static void GetClosestCollision(Collider[] colliders, int colliderCount, Vector3 origin,
        Dictionary<Entity, ClosestCollision> colliderOwners)
    {
        for (int i = 0; i < colliderCount; i++)
        {
            var collider = colliders[i];

            var hitCollision = collider.GetComponent<HitCollision>();
            if (hitCollision == null)
            {
                GameDebug.LogError("Collider:" + collider + " has no hit collision");
                continue;
            }

            var closestPoint = collider.transform.position;
            float dist = Vector3.Distance(origin, closestPoint);

            ClosestCollision currentClosest;
            if (colliderOwners.TryGetValue(hitCollision.Owner, out currentClosest))
            {
                if (dist < currentClosest.Dist)
                {
                    currentClosest.HitCollision = hitCollision;
                    currentClosest.ClosestPoint = closestPoint;
                    currentClosest.Dist = dist;
                    colliderOwners[hitCollision.Owner] = currentClosest;
                }
            }
            else
            {
                currentClosest.HitCollision = hitCollision;
                currentClosest.ClosestPoint = closestPoint;
                currentClosest.Dist = dist;
                colliderOwners.Add(hitCollision.Owner, currentClosest);
            }
        }
    }
}