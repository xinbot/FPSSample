using CollisionLib;
using Primitives;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

[BurstCompile(CompileSynchronously = true)]
public struct BroadPhaseSphereCastJob : IJob
{
    public NativeList<Entity> Result;

    private ray _ray;
    private float _rayDist;
    private float _rayRadius;
    private Entity _include;
    private Entity _exclude;
    private uint _flagMask;

    [ReadOnly] private NativeArray<uint> _flags;

    [ReadOnly] private NativeArray<Entity> _colliderEntities;

    [ReadOnly] private NativeArray<HitCollisionData> _colliderData;

    [ReadOnly] private NativeArray<sphere> _bounds;

    public BroadPhaseSphereCastJob(NativeArray<Entity> colliderEntities, NativeArray<HitCollisionData> colliderData,
        NativeArray<uint> flags, NativeArray<sphere> bounds, Entity exclude, Entity include, uint flagMask, ray ray,
        float distance, float radius)
    {
        _colliderEntities = colliderEntities;
        _colliderData = colliderData;
        _flags = flags;
        _bounds = bounds;

        _ray = ray;
        _include = include;
        _exclude = exclude;
        _flagMask = flagMask;
        _rayDist = distance;
        _rayRadius = radius;

        Result = new NativeList<Entity>(colliderEntities.Length, Allocator.TempJob);
    }

    public void Dispose()
    {
        Result.Dispose();
    }

    public void Execute()
    {
        for (int i = 0; i < _bounds.Length; i++)
        {
            var relevant = _include != Entity.Null && _include == _colliderData[i].HitCollisionOwner ||
                           !(_exclude != Entity.Null && _exclude == _colliderData[i].HitCollisionOwner) &&
                           (_flags[i] & _flagMask) != 0;

            if (!relevant)
            {
                continue;
            }

            var boundsHit = coll.RayCast(_bounds[i], _ray, _rayDist, _rayRadius);
            if (boundsHit)
            {
                Result.Add(_colliderEntities[i]);
            }
        }
    }
}

[BurstCompile(CompileSynchronously = true)]
public struct SphereCastSingleJob : IJob
{
    public NativeArray<HitCollisionData.CollisionResult> Result;
    public Entity HitCollObject;

    [ReadOnly] private NativeSlice<HitCollisionData.TransformHistory> _transformBuffer;

    [ReadOnly] private DynamicBuffer<HitCollisionData.Sphere> _sphereArray;

    [ReadOnly] private DynamicBuffer<HitCollisionData.Capsule> _capsuleArray;

    [ReadOnly] private DynamicBuffer<HitCollisionData.Box> _boxArray;

    private ray _ray;
    private float _rayDist;
    private float _rayRadius;

    public SphereCastSingleJob(EntityManager entityManager, Entity entity, ray ray, float distance, float radius,
        int tick)
    {
        _ray = ray;
        _rayDist = distance;
        _rayRadius = radius;

        HitCollObject = entity;

        var collData = entityManager.GetComponentData<HitCollisionData>(entity);
        var histIndex = collData.GetHistoryIndex(tick);

        _transformBuffer = new NativeSlice<HitCollisionData.TransformHistory>(
            entityManager.GetBuffer<HitCollisionData.TransformHistory>(entity).AsNativeArray(),
            histIndex * HitCollisionData.MaxColliderCount);

        _sphereArray = entityManager.GetBuffer<HitCollisionData.Sphere>(entity);

        _capsuleArray = entityManager.GetBuffer<HitCollisionData.Capsule>(entity);

        _boxArray = entityManager.GetBuffer<HitCollisionData.Box>(entity);

        Result = new NativeArray<HitCollisionData.CollisionResult>(1, Allocator.TempJob);
    }

    public void Dispose()
    {
        Result.Dispose();
    }

    public void Execute()
    {
        // TODO (mogensh) : find all hits and return closest

        var rayEnd = _ray.origin + _ray.direction * _rayDist;

        for (var i = 0; i < _sphereArray.Length; i++)
        {
            var prim = _sphereArray[i].SpherePrimitive;
            var sourceIndex = _sphereArray[i].TransformIndex;
            prim = primlib.transform(prim, _transformBuffer[sourceIndex].Pos, _transformBuffer[sourceIndex].Rot);

            var hit = coll.RayCast(prim, _ray, _rayDist, _rayRadius);
            if (hit)
            {
                Result[0] = new HitCollisionData.CollisionResult
                {
                    Info = _sphereArray[i].Info,
                    PrimitiveCenter = prim.center,
                    Hit = 1,
                    SpherePrimitive = prim,
                };
                return;
            }
        }

        for (var i = 0; i < _capsuleArray.Length; i++)
        {
            var prim = _capsuleArray[i].CapsulePrimitive;
            var sourceIndex = _capsuleArray[i].TransformIndex;
            prim = primlib.transform(prim, _transformBuffer[sourceIndex].Pos, _transformBuffer[sourceIndex].Rot);

            var rayCapsule = new capsule(_ray.origin, rayEnd, _rayRadius);
            var hit = InstersectionHelper.IntersectCapsuleCapsule(ref prim, ref rayCapsule);
            if (hit)
            {
                Result[0] = new HitCollisionData.CollisionResult
                {
                    Info = _capsuleArray[i].Info,
                    PrimitiveCenter = prim.p1 + (prim.p2 - prim.p1) * 0.5f,
                    Hit = 1,
                    CapsulePrimitive = prim,
                };
                return;
            }
        }

        for (var i = 0; i < _boxArray.Length; i++)
        {
            var prim = _boxArray[i].BoxPrimitive;
            var sourceIndex = _boxArray[i].TransformIndex;

            var primWorldSpace =
                primlib.transform(prim, _transformBuffer[sourceIndex].Pos, _transformBuffer[sourceIndex].Rot);
            var rayCapsule = new capsule(_ray.origin, rayEnd, _rayRadius);
            var hit = coll.OverlapCapsuleBox(rayCapsule, primWorldSpace);
            if (hit)
            {
                Result[0] = new HitCollisionData.CollisionResult()
                {
                    Info = _boxArray[i].Info,
                    PrimitiveCenter = primWorldSpace.center,
                    Hit = 1,
                    BoxPrimitive = primWorldSpace,
                };
                return;
            }
        }
    }
}

[BurstCompile(CompileSynchronously = true)]
public struct BroadPhaseSphereOverlapJob : IJob
{
    public sphere Sphere;

    [ReadOnly] public NativeArray<Entity> Entities;

    [ReadOnly] public NativeArray<sphere> Bounds;

    public NativeList<Entity> Result;

    public void Execute()
    {
        for (int i = 0; i < Bounds.Length; i++)
        {
            var dist = math.distance(Sphere.center, Bounds[i].center);
            var hit = dist < Sphere.radius + Bounds[i].radius;
            if (hit)
            {
                Result.Add(Entities[i]);
            }
        }
    }
}

[BurstCompile(CompileSynchronously = true)]
public struct SphereOverlapJob : IJob
{
    public sphere Sphere;

    [ReadOnly] public NativeSlice<HitCollisionData.TransformHistory> TransformBuffer;

    [ReadOnly] public NativeArray<HitCollisionData.Sphere> SphereArray;

    [ReadOnly] public NativeArray<HitCollisionData.Capsule> CapsuleArray;

    [ReadOnly] public NativeArray<HitCollisionData.Box> BoxArray;

    public NativeArray<HitCollisionData.CollisionResult> Result;

    public void Execute()
    {
        // TODO (mogensh) : find all hits and return closest

        for (var i = 0; i < SphereArray.Length; i++)
        {
            var prim = SphereArray[i].SpherePrimitive;
            var sourceIndex = SphereArray[i].TransformIndex;
            prim = primlib.transform(prim, TransformBuffer[sourceIndex].Pos, TransformBuffer[sourceIndex].Rot);

            var dist = math.distance(Sphere.center, prim.center);
            var hit = dist < Sphere.radius + prim.radius;
            if (hit)
            {
                Result[0] = new HitCollisionData.CollisionResult
                {
                    Info = SphereArray[i].Info,
                    PrimitiveCenter = prim.center,
                    Hit = 1,
                    SpherePrimitive = prim
                };
                return;
            }
        }

        for (var i = 0; i < CapsuleArray.Length; i++)
        {
            var prim = CapsuleArray[i].CapsulePrimitive;
            var sourceIndex = CapsuleArray[i].TransformIndex;
            prim = primlib.transform(prim, TransformBuffer[sourceIndex].Pos, TransformBuffer[sourceIndex].Rot);

            var v = prim.p2 - prim.p1;
            var hit = coll.RayCast(Sphere, new ray(prim.p1, math.normalize(v)), math.length(v), prim.radius);
            if (hit)
            {
                Result[0] = new HitCollisionData.CollisionResult()
                {
                    Info = CapsuleArray[i].Info,
                    PrimitiveCenter = prim.p1 + (prim.p2 - prim.p1) * 0.5f,
                    Hit = 1,
                    CapsulePrimitive = prim,
                };
                return;
            }
        }

        for (var i = 0; i < BoxArray.Length; i++)
        {
            var prim = BoxArray[i].BoxPrimitive;
            var sourceIndex = BoxArray[i].TransformIndex;
            var primWorldSpace =
                primlib.transform(prim, TransformBuffer[sourceIndex].Pos, TransformBuffer[sourceIndex].Rot);

            // TODO (mogensh) Sphere Box collision
            if (true)
            {
                Result[0] = new HitCollisionData.CollisionResult()
                {
                    Info = BoxArray[i].Info,
                    PrimitiveCenter = primWorldSpace.center,
                    Hit = 1,
                    BoxPrimitive = primWorldSpace,
                };
                return;
            }
        }
    }
}

[BurstCompile(CompileSynchronously = true)]
public struct StoreBonesJob : IJobParallelForTransform
{
    public NativeSlice<HitCollisionData.TransformHistory> TransformBuffer;

    public void Execute(int i, TransformAccess transform)
    {
        TransformBuffer[i] = new HitCollisionData.TransformHistory
        {
            Pos = transform.position,
            Rot = transform.rotation,
        };
    }
}