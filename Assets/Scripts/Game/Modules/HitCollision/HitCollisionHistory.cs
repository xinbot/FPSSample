using System;
using System.Collections.Generic;
using Primitives;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Profiling;

[DisallowMultipleComponent]
public class HitCollisionHistory : MonoBehaviour
{
    [Serializable]
    public class Settings
    {
        public GameObject collisionSetup;
        public float boundsRadius = 2.0f;
        public float boundsHeight = 1.0f;
    }

    public Settings settings;

    [NonSerialized] public TransformAccessArray ColliderParents;

#if UNITY_EDITOR
    private void OnDisable()
    {
        if (ColliderParents.isCreated)
        {
            ColliderParents.Dispose();
        }
    }
#endif
}

public struct HitCollisionData : IComponentData
{
    public const int MaxColliderCount = 16;
    public const int HistoryCount = 16;

    public enum HitCollType
    {
        Undefined,
        Body,
        Head,
    }

    public struct HitCollInfo
    {
        public HitCollType Type;
    }

    public struct CollisionResult
    {
        public int Hit;
        public HitCollInfo Info;
        public float3 PrimitiveCenter;

        public box BoxPrimitive;
        public capsule CapsulePrimitive;
        public sphere SpherePrimitive;
    }

    [InternalBufferCapacity(MaxColliderCount)]
    public struct Box : IBufferElementData
    {
        public HitCollInfo Info;
        public int TransformIndex;
        public box BoxPrimitive;
    }

    [InternalBufferCapacity(MaxColliderCount)]
    public struct Capsule : IBufferElementData
    {
        public HitCollInfo Info;
        public int TransformIndex;
        public capsule CapsulePrimitive;
    }

    [InternalBufferCapacity(MaxColliderCount)]
    public struct Sphere : IBufferElementData
    {
        public HitCollInfo Info;
        public int TransformIndex;
        public sphere SpherePrimitive;
    }

    [InternalBufferCapacity(HistoryCount)]
    public struct BoundsHistory : IBufferElementData
    {
        public float3 Pos;
    }

    [InternalBufferCapacity(MaxColliderCount * HistoryCount)]
    public struct TransformHistory : IBufferElementData
    {
        public float3 Pos;
        public quaternion Rot;
    }

    public Entity HitCollisionOwner;

    public float BoundsRadius;
    public float BoundsHeightOffset;

    private int _lastTick;
    private int _lastIndex;
    private int _historyCount;

    public static void Setup(EntityManager entityManager, Entity entity, List<Transform> parents,
        float boundsRadius, float boundsHeightOffset,
        List<CapsuleCollider> capsuleColliders, List<Transform> capsuleColliderParents,
        List<SphereCollider> sphereColliders, List<Transform> sphereColliderParents,
        List<BoxCollider> boxColliders, List<Transform> boxColliderParents)
    {
        HitCollisionData coll;
        if (entityManager.HasComponent<HitCollisionData>(entity))
        {
            coll = entityManager.GetComponentData<HitCollisionData>(entity);
        }
        else
        {
            coll = new HitCollisionData();
        }

        coll._lastTick = -1;
        coll._lastIndex = -1;
        coll.BoundsRadius = boundsRadius;
        coll.BoundsHeightOffset = boundsHeightOffset;
        if (entityManager.HasComponent<HitCollisionData>(entity))
        {
            entityManager.SetComponentData(entity, coll);
        }
        else
        {
            entityManager.AddComponentData(entity, coll);
        }

        // Setup history
        entityManager.AddBuffer<TransformHistory>(entity);
        var historyBuffer = entityManager.GetBuffer<TransformHistory>(entity);
        for (var i = 0; i < HistoryCount * MaxColliderCount; i++)
        {
            historyBuffer.Add(new TransformHistory());
        }

        entityManager.AddBuffer<BoundsHistory>(entity);
        var boundsBuffer = entityManager.GetBuffer<BoundsHistory>(entity);
        for (var i = 0; i < HistoryCount; i++)
        {
            boundsBuffer.Add(new BoundsHistory());
        }

        // Primitives
        entityManager.AddBuffer<Capsule>(entity);
        var capsuleBuffer = entityManager.GetBuffer<Capsule>(entity);
        for (var i = 0; i < capsuleColliders.Count; i++)
        {
            var collider = capsuleColliders[i];
            var localPos = collider.center;

            var direction = collider.direction;
            var axis = direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;

            var radius = collider.radius;
            var offset = 0.5f * axis * (collider.height - 2 * radius);
            var prim = new capsule
            {
                p1 = localPos - offset,
                p2 = localPos + offset,
                radius = radius,
            };

            var trans = collider.transform;
            var capsule = new Capsule();
            capsule.CapsulePrimitive = primlib.transform(prim, trans.localPosition, trans.localRotation);

            var parent = capsuleColliderParents[i];
            capsule.TransformIndex = parents.IndexOf(parent);
            capsule.Info = new HitCollInfo
            {
                Type = HitCollType.Body
            };
            capsuleBuffer.Add(capsule);
        }

        entityManager.AddBuffer<Box>(entity);
        var boxBuffer = entityManager.GetBuffer<Box>(entity);
        for (var i = 0; i < boxColliders.Count; i++)
        {
            var collider = boxColliders[i];
            var prim = new box
            {
                center = collider.center,
                size = collider.size,
                rotation = Quaternion.identity
            };

            var trans = collider.transform;
            var box = new Box();
            box.BoxPrimitive = primlib.transform(prim, trans.localPosition, trans.localRotation);

            var parent = boxColliderParents[i];
            box.TransformIndex = parents.IndexOf(parent);
            box.Info = new HitCollInfo
            {
                Type = HitCollType.Body
            };
            boxBuffer.Add(box);
        }

        entityManager.AddBuffer<Sphere>(entity);
        var sphereBuffer = entityManager.GetBuffer<Sphere>(entity);
        for (var i = 0; i < sphereColliders.Count; i++)
        {
            var collider = sphereColliders[i];
            var prim = new sphere
            {
                center = collider.center,
                radius = collider.radius,
            };

            var trans = collider.transform;
            var sphere = new Sphere();
            sphere.SpherePrimitive = primlib.transform(prim, trans.localPosition, trans.localRotation);

            var parent = sphereColliderParents[i];
            sphere.TransformIndex = parents.IndexOf(parent);
            sphere.Info = new HitCollInfo
            {
                Type = HitCollType.Body
            };
            sphereBuffer.Add(sphere);
        }
    }

    public int GetHistoryIndex(int tick)
    {
        // If we exceed buffer size we should always use last value (if player latency to high no rollback is performed)
        var rollBackTicks = _lastTick - tick;
        if (rollBackTicks >= _historyCount || tick > _lastTick)
        {
            rollBackTicks = 0;
        }

        var index = _lastIndex - rollBackTicks;
        while (index < 0)
        {
            index += HistoryCount;
        }

        return index;
    }

    public static bool IsRelevant(EntityManager entityManager, Entity hitCollisionEntity, int flagMask,
        Entity forceExcluded, Entity forceIncluded)
    {
        var hitCollisionData = entityManager.GetComponentData<HitCollisionData>(hitCollisionEntity);

        if (hitCollisionData.HitCollisionOwner == Entity.Null)
        {
            GameDebug.Assert(false, $"HitCollisionHistory:{hitCollisionData} has a null hitCollisionOwner");
            return false;
        }

        Profiler.BeginSample("IsRelevant");

        var hitCollisionOwner =
            entityManager.GetComponentData<HitCollisionOwnerData>(hitCollisionData.HitCollisionOwner);
        var valid = forceIncluded != Entity.Null && forceIncluded == hitCollisionData.HitCollisionOwner ||
                    !(forceExcluded != Entity.Null && forceExcluded == hitCollisionData.HitCollisionOwner) &&
                    hitCollisionOwner.collisionEnabled == 1 &&
                    (hitCollisionOwner.colliderFlags & flagMask) != 0;

        Profiler.EndSample();

        return valid;
    }

    public static void StoreBones(EntityManager entityManager, Entity entity, TransformAccessArray boneTransformArray,
        int sampleTick)
    {
        var collisionData = entityManager.GetComponentData<HitCollisionData>(entity);

        var transformHistories = entityManager.GetBuffer<TransformHistory>(entity);
        var boundsHistories = entityManager.GetBuffer<BoundsHistory>(entity);

        // To make sure all ticks have valid data we store state of all ticks up to sampleTick that has not been
        // stored (in editor server might run multiple game frames for each time sample state is called) 
        var lastStoredTick = collisionData._lastTick;
        var endTick = sampleTick;
        var startTick = lastStoredTick != -1 ? lastStoredTick + 1 : sampleTick;
        for (var tick = startTick; tick <= endTick; tick++)
        {
            collisionData._lastIndex = (collisionData._lastIndex + 1) % HistoryCount;
            collisionData._lastTick = tick;
            if (collisionData._historyCount < HistoryCount)
            {
                collisionData._historyCount++;
            }

            var slice = new NativeSlice<TransformHistory>(transformHistories.AsNativeArray(),
                collisionData._lastIndex * MaxColliderCount);

            var job = new StoreBonesJob
            {
                TransformBuffer = slice,
            };
            var handle = job.Schedule(boneTransformArray);
            handle.Complete();

            boundsHistories[collisionData._lastIndex] = new BoundsHistory
            {
                Pos = transformHistories[0].Pos + new float3(0, 1, 0) * collisionData.BoundsHeightOffset,
            };
        }

        entityManager.SetComponentData(entity, collisionData);
    }

    public static bool SphereOverlapSingle(EntityManager entityManager, Entity entity, int tick, sphere sphere,
        out CollisionResult result)
    {
        var collData = entityManager.GetComponentData<HitCollisionData>(entity);

        var histIndex = collData.GetHistoryIndex(tick);
        var transformBuffer = entityManager.GetBuffer<TransformHistory>(entity);

        var sphereArray = entityManager.GetBuffer<Sphere>(entity);
        var capsuleArray = entityManager.GetBuffer<Capsule>(entity);
        var boxArray = entityManager.GetBuffer<Box>(entity);

        var resultArray = new NativeArray<CollisionResult>(1, Allocator.TempJob);

        var job = new SphereOverlapJob
        {
            TransformBuffer =
                new NativeSlice<TransformHistory>(transformBuffer.AsNativeArray(), histIndex * MaxColliderCount),
            SphereArray = sphereArray.AsNativeArray(),
            CapsuleArray = capsuleArray.AsNativeArray(),
            BoxArray = boxArray.AsNativeArray(),
            Sphere = sphere,
            Result = resultArray,
        };

        var handle = job.Schedule();
        handle.Complete();
        result = resultArray[0];

        if (math.length(result.BoxPrimitive.size) > 0)
        {
            DebugDraw.Prim(result.BoxPrimitive, Color.red, 1);
        }

        if (result.CapsulePrimitive.radius > 0)
        {
            DebugDraw.Prim(result.CapsulePrimitive, Color.red, 1);
        }

        if (result.SpherePrimitive.radius > 0)
        {
            DebugDraw.Prim(result.SpherePrimitive, Color.red, 1);
        }

        resultArray.Dispose();

        return result.Hit == 1;
    }
}