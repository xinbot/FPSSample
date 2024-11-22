using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Jobs;

[DisableAutoCreation]
public class HandleHitCollisionSpawning : InitializeComponentGroupSystem<HitCollisionHistory,
    HandleHitCollisionSpawning.Initialized>
{
    private readonly GameObject _systemRoot;
    private int _bufferSize;

    private List<Collider> _sourceColliders = new List<Collider>();

    public struct Initialized : IComponentData
    {
    }

    public HandleHitCollisionSpawning(GameWorld world, GameObject systemRoot, int bufferSize) : base(world)
    {
        _systemRoot = systemRoot;
        _bufferSize = bufferSize;
    }

    protected override void Initialize(ref ComponentGroup group)
    {
        // We copy to list of incoming hit collisions as it is not allowed to add entities while iterating component array 
        var hitCollisionArray = group.GetComponentArray<HitCollisionHistory>().ToArray();
        var hitCollisionEntityArray = group.GetEntityArray().ToArray();

        for (var iHitColl = 0; iHitColl < hitCollisionArray.Length; iHitColl++)
        {
            var hitCollision = hitCollisionArray[iHitColl];
            var hitCollisionEntity = hitCollisionEntityArray[iHitColl];

            var externalSetup = hitCollision.settings.collisionSetup != null;
            var colliderSetup = externalSetup ? hitCollision.settings.collisionSetup.transform : hitCollision.transform;

            // TODO (mogensh) cache and reuse collision setup from each prefab - or find better serialization format
            // Find and disable all all colliders on collisionOwner
            _sourceColliders.Clear();
            RecursiveGetCollidersInChildren(colliderSetup.transform, ref _sourceColliders);
            foreach (var collider in _sourceColliders)
            {
                collider.enabled = false;
            }

            // Create collider collection
            if (_systemRoot != null)
            {
                hitCollision.transform.SetParent(_systemRoot.transform, false);
            }

            var uniqueParents = new List<Transform>(16);
            var colliderParents = new List<Transform>(16);
            var capsuleColliders = new List<CapsuleCollider>(16);
            var capsuleColliderParents = new List<Transform>(16);
            var sphereColliders = new List<SphereCollider>(16);
            var sphereColliderParents = new List<Transform>(16);
            var boxColliders = new List<BoxCollider>(16);
            var boxColliderParents = new List<Transform>(16);

            for (var i = 0; i < _sourceColliders.Count; i++)
            {
                var sourceCollider = _sourceColliders[i];
                var colliderParentBone = sourceCollider.transform.parent;
                if (externalSetup)
                {
                    var skeleton = EntityManager.GetComponentObject<Skeleton>(hitCollisionEntity);
                    var ownerBoneIndex = skeleton.GetBoneIndex(colliderParentBone.name.GetHashCode());
                    colliderParentBone = skeleton.bones[ownerBoneIndex];
                }

                colliderParents.Add(colliderParentBone);

                if (!uniqueParents.Contains(colliderParentBone))
                {
                    uniqueParents.Add(colliderParentBone);
                }

                var capsuleCollider = sourceCollider as CapsuleCollider;
                if (capsuleCollider != null)
                {
                    capsuleColliderParents.Add(colliderParentBone);
                    capsuleColliders.Add(capsuleCollider);
                }
                else
                {
                    var boxCollider = sourceCollider as BoxCollider;
                    if (boxCollider != null)
                    {
                        boxColliders.Add(boxCollider);
                        boxColliderParents.Add(colliderParentBone);
                    }
                    else
                    {
                        var sphereCollider = sourceCollider as SphereCollider;
                        if (sphereCollider != null)
                        {
                            sphereColliders.Add(sphereCollider);
                            sphereColliderParents.Add(colliderParentBone);
                        }
                    }
                }
            }

            hitCollision.ColliderParents = new TransformAccessArray(uniqueParents.ToArray());

            HitCollisionData.Setup(EntityManager, hitCollisionEntity, uniqueParents,
                hitCollision.settings.boundsRadius, hitCollision.settings.boundsHeight, capsuleColliders,
                capsuleColliderParents, sphereColliders, sphereColliderParents, boxColliders, boxColliderParents);
        }
    }

    private void RecursiveGetCollidersInChildren(Transform parent, ref List<Collider> colliders)
    {
        for (var i = 0; i < parent.transform.childCount; i++)
        {
            var child = parent.transform.GetChild(i);
            var collider = child.GetComponent<Collider>();
            if (collider != null)
            {
                colliders.Add(collider);
            }

            RecursiveGetCollidersInChildren(child, ref colliders);
        }
    }
}

[DisableAutoCreation]
public class HandleHitCollisionDespawning : DeinitializeComponentGroupSystem<HitCollisionHistory>
{
    public HandleHitCollisionDespawning(GameWorld world) : base(world)
    {
    }

    protected override void Deinitialize(ref ComponentGroup group)
    {
        var hitCollHistoryArray = group.GetComponentArray<HitCollisionHistory>().ToArray();

        for (var i = 0; i < hitCollHistoryArray.Length; i++)
        {
            var hitCollHistory = hitCollHistoryArray[i];

            if (hitCollHistory.ColliderParents.isCreated)
                hitCollHistory.ColliderParents.Dispose();
        }
    }
}

[DisableAutoCreation]
public class StoreColliderStates : BaseComponentSystem<HitCollisionHistory>
{
    public StoreColliderStates(GameWorld world) : base(world)
    {
    }

    protected override void Update(Entity entity, HitCollisionHistory hitColliderHist)
    {
        var sampleTick = m_world.WorldTime.Tick;

        HitCollisionData.StoreBones(EntityManager, entity, hitColliderHist.ColliderParents, sampleTick);

        if (HitCollisionModule.ShowDebug.IntValue == 1)
        {
            var primColor = Color.magenta;
            var boundsColor = Color.green;
            for (int i = 0; i < 20; i++)
            {
                primColor.a -= 0.01f;
                boundsColor.a -= 0.01f;
            }
        }
    }
}