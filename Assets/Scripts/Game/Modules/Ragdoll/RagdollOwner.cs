using System;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

[RequireComponent(typeof(Skeleton))]
public class RagdollOwner : MonoBehaviour
{
    public enum Phase
    {
        Inactive,
        PoseSampled,
        Active,
    }

    public Phase phase = Phase.Inactive;

    [NonSerialized] public float TimeUntilStar;

    [Tooltip("The skeleton group of a Ragdoll Prefab")]
    public GameObject ragdollPrefab;

    public GameObject ragdollInstance;
    public Skeleton ragdollSkeleton;

    public Transform[] targetBones;
    public Vector3[] lastBonePositions;
    public Quaternion[] lastBoneRotations;
}

[DisableAutoCreation]
public class HandleRagdollSpawn : InitializeComponentSystem<RagdollOwner>
{
    private readonly GameObject _systemRoot;

    public HandleRagdollSpawn(GameWorld gameWorld, GameObject systemRoot) : base(gameWorld)
    {
        _systemRoot = systemRoot;
    }

    protected override void Initialize(Entity entity, RagdollOwner ragdoll)
    {
        // Create ragDoll instance
        ragdoll.ragdollInstance = Object.Instantiate(ragdoll.ragdollPrefab);
        ragdoll.ragdollInstance.SetActive(false);
        ragdoll.ragdollInstance.name = ragdoll.gameObject.name + "_Ragdoll";

        if (_systemRoot != null)
        {
            ragdoll.ragdollInstance.transform.SetParent(_systemRoot.transform);
        }

        ragdoll.ragdollSkeleton = ragdoll.ragdollInstance.GetComponent<Skeleton>();

        Skeleton targetSkeleton = ragdoll.GetComponent<Skeleton>();

        int boneCount = ragdoll.ragdollSkeleton.bones.Length;
        ragdoll.targetBones = new Transform[boneCount];
        ragdoll.lastBonePositions = new Vector3[boneCount];
        ragdoll.lastBoneRotations = new Quaternion[boneCount];

        for (var i = 0; i < ragdoll.ragdollSkeleton.bones.Length; i++)
        {
            var targetBoneIndex = targetSkeleton.GetBoneIndex(ragdoll.ragdollSkeleton.nameHashes[i]);
            if (targetBoneIndex == -1)
            {
                continue;
            }

            ragdoll.targetBones[i] = targetSkeleton.bones[targetBoneIndex];
        }
    }
}

[DisableAutoCreation]
public class HandleRagdollDespawn : DeinitializeComponentSystem<RagdollOwner>
{
    public HandleRagdollDespawn(GameWorld gameWorld) : base(gameWorld)
    {
    }

    protected override void Deinitialize(Entity entity, RagdollOwner ragdoll)
    {
        Object.Destroy(ragdoll.ragdollInstance);
    }
}

[DisableAutoCreation]
public class UpdateRagdolls : BaseComponentSystem<CharacterPresentationSetup, RagdollOwner>
{
    public UpdateRagdolls(GameWorld gameWorld) : base(gameWorld)
    {
    }

    protected override void Update(Entity entity, CharacterPresentationSetup charPresentation,
        RagdollOwner ragdollOwner)
    {
        GameDebug.Assert(ragdollOwner.ragdollInstance != null,
            $"Ragdoll instance is NULL for object: {ragdollOwner.gameObject}");
        GameDebug.Assert(EntityManager.Exists(charPresentation.character), "CharPresentation character does not exist");
        GameDebug.Assert(EntityManager.HasComponent<RagdollStateData>(charPresentation.character),
            "CharPresentation character does not have RagdollState");

        var ragdollState = EntityManager.GetComponentData<RagdollStateData>(charPresentation.character);
        if (ragdollState.RagdollActive == 0)
        {
            return;
        }

        switch (ragdollOwner.phase)
        {
            case RagdollOwner.Phase.Inactive:

                ragdollOwner.TimeUntilStar -= m_world.frameDuration;

                if (ragdollOwner.TimeUntilStar <= m_world.WorldTime.tickInterval)
                {
                    // Store bone transforms so they can be used to calculate bone velocity next frame
                    for (int boneIndex = 0; boneIndex < ragdollOwner.targetBones.Length; boneIndex++)
                    {
                        if (ragdollOwner.targetBones[boneIndex] == null)
                        {
                            continue;
                        }

                        ragdollOwner.lastBonePositions[boneIndex] = ragdollOwner.targetBones[boneIndex].position;
                        ragdollOwner.lastBoneRotations[boneIndex] = ragdollOwner.targetBones[boneIndex].rotation;
                    }

                    ragdollOwner.phase = RagdollOwner.Phase.PoseSampled;
                }

                break;
            case RagdollOwner.Phase.PoseSampled:

                InitializeRagdoll(ragdollOwner, true, ragdollState.Impulse);
                ragdollOwner.phase = RagdollOwner.Phase.Active;

                break;
            case RagdollOwner.Phase.Active:

                for (int boneIndex = 0; boneIndex < ragdollOwner.targetBones.Length; boneIndex++)
                {
                    if (ragdollOwner.targetBones[boneIndex] == null)
                    {
                        continue;
                    }

                    ragdollOwner.targetBones[boneIndex].position =
                        ragdollOwner.ragdollSkeleton.bones[boneIndex].position;
                    ragdollOwner.targetBones[boneIndex].rotation =
                        ragdollOwner.ragdollSkeleton.bones[boneIndex].rotation;
                }

                break;
        }
    }

    private void InitializeRagdoll(RagdollOwner ragdollOwner, bool useAnimSpeed, Vector3 impulse)
    {
        ragdollOwner.ragdollInstance.SetActive(true);

        // Setup ragdoll
        var invFrameTime = 1.0f / m_world.frameDuration;
        for (int boneIndex = 0; boneIndex < ragdollOwner.targetBones.Length; boneIndex++)
        {
            if (ragdollOwner.targetBones[boneIndex] == null)
            {
                continue;
            }

            // Set start position
            var position = ragdollOwner.targetBones[boneIndex].position;
            var rotation = ragdollOwner.targetBones[boneIndex].rotation;
            ragdollOwner.ragdollSkeleton.bones[boneIndex].position = position;
            ragdollOwner.ragdollSkeleton.bones[boneIndex].rotation = rotation;

            // Set bone velocity
            var rigidBody = ragdollOwner.ragdollSkeleton.bones[boneIndex].GetComponent<Rigidbody>();
            if (rigidBody == null)
            {
                continue;
            }

            if (useAnimSpeed)
            {
                var torque = (Quaternion.Inverse(ragdollOwner.lastBoneRotations[boneIndex]) * rotation).eulerAngles *
                             invFrameTime;
                rigidBody.AddTorque(torque, ForceMode.VelocityChange);
            }

            var velocity = Vector3.zero;
            // velocity += (position - ragdollOwner.lastBonePositions[boneIndex]) * invFrameTime;
            velocity += impulse;
            rigidBody.AddForce(velocity, ForceMode.VelocityChange);
        }
    }
}