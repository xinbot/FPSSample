using System;
using System.Collections.Generic;
using Networking;
using UnityEngine;
using Unity.Entities;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;

#endif

[Serializable]
public struct ReplicatedEntityData : IComponentData, IReplicatedComponent
{
    // Guid of asset this entity is created from
    public WeakAssetReference assetGuid;
    [NonSerialized] public int ID;
    [NonSerialized] public int PredictingPlayerId;

    public ReplicatedEntityData(WeakAssetReference guid)
    {
        assetGuid = guid;
        ID = -1;
        PredictingPlayerId = -1;
    }

    public static IReplicatedComponentSerializerFactory CreateSerializerFactory()
    {
        return new ReplicatedComponentSerializerFactory<ReplicatedEntityData>();
    }

    public void Serialize(ref SerializeContext context, ref NetworkWriter writer)
    {
        writer.WriteInt32("predictingPlayerId", PredictingPlayerId);
    }

    public void Deserialize(ref SerializeContext context, ref NetworkReader reader)
    {
        PredictingPlayerId = reader.ReadInt32();
    }
}

[ExecuteAlways, DisallowMultipleComponent]
[RequireComponent(typeof(GameObjectEntity))]
public class ReplicatedEntity : ComponentDataProxy<ReplicatedEntityData>
{
    // guid of instance. Used for identifying replicated entities from the scene
    public byte[] netID;

    private void Awake()
    {
        // Ensure replicatedEntityData is set to default
        var val = Value;
        val.ID = -1;
        val.PredictingPlayerId = -1;
        Value = val;
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
        {
            SetUniqueNetID();
        }
#endif
    }

#if UNITY_EDITOR

    public static readonly Dictionary<byte[], ReplicatedEntity> NetGuidMap =
        new Dictionary<byte[], ReplicatedEntity>(new ByteArrayComp());

    private void OnValidate()
    {
        if (EditorApplication.isPlaying)
        {
            return;
        }

        PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(this);
        if (prefabAssetType == PrefabAssetType.Regular || prefabAssetType == PrefabAssetType.Model)
        {
            netID = null;
        }
        else
        {
            SetUniqueNetID();
        }

        UpdateAssetGuid();
    }

    public bool SetAssetGuid(string guidStr)
    {
        var guid = new WeakAssetReference(guidStr);
        var val = Value;
        var currentGuid = val.assetGuid;
        if (!guid.Equals(currentGuid))
        {
            val.assetGuid = guid;
            Value = val;
            PrefabUtility.SavePrefabAsset(gameObject);
            return true;
        }

        return false;
    }

    public void UpdateAssetGuid()
    {
        // Set type guid
        var stage = PrefabStageUtility.GetPrefabStage(gameObject);
        if (stage != null)
        {
            var guidStr = AssetDatabase.AssetPathToGUID(stage.prefabAssetPath);
            if (SetAssetGuid(guidStr))
            {
                EditorSceneManager.MarkSceneDirty(stage.scene);
            }
        }
    }

    private void SetUniqueNetID()
    {
        // Generate new if fresh object
        if (netID == null || netID.Length == 0)
        {
            var guid = Guid.NewGuid();
            netID = guid.ToByteArray();
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        // If we are the first add us
        if (!NetGuidMap.ContainsKey(netID))
        {
            NetGuidMap[netID] = this;
            return;
        }

        // Our guid is known and in use by another object??
        var oldReg = NetGuidMap[netID];
        if (oldReg != null && oldReg.GetInstanceID() != GetInstanceID() &&
            ByteArrayComp.Instance.Equals(oldReg.netID, netID))
        {
            // If actually *is* another ReplEnt that has our netID, *then* we give it up (usually happens because of copy / paste)
            netID = Guid.NewGuid().ToByteArray();
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        NetGuidMap[netID] = this;
    }

#endif
}

[DisableAutoCreation]
public class UpdateReplicatedOwnerFlag : BaseComponentSystem
{
    private ComponentGroup _repEntityDataGroup;

    private int _localPlayerId;
    private bool _initialized;

    public UpdateReplicatedOwnerFlag(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _repEntityDataGroup = GetComponentGroup(typeof(ReplicatedEntityData));
    }

    public void SetLocalPlayerId(int playerId)
    {
        _localPlayerId = playerId;
        _initialized = true;
    }

    protected override void OnUpdate()
    {
        var entityArray = _repEntityDataGroup.GetEntityArray();
        var repEntityDataArray = _repEntityDataGroup.GetComponentDataArray<ReplicatedEntityData>();
        for (var i = 0; i < entityArray.Length; i++)
        {
            var repDataEntity = repEntityDataArray[i];
            var locallyControlled = _localPlayerId == -1 || repDataEntity.PredictingPlayerId == _localPlayerId;

            SetFlagAndChildFlags(entityArray[i], locallyControlled);
        }
    }

    private void SetFlagAndChildFlags(Entity entity, bool set)
    {
        SetFlag(entity, set);

        if (EntityManager.HasComponent<EntityGroupChildren>(entity))
        {
            var buffer = EntityManager.GetBuffer<EntityGroupChildren>(entity);
            for (var i = 0; i < buffer.Length; i++)
            {
                SetFlag(buffer[i].Entity, set);
            }
        }
    }

    private void SetFlag(Entity entity, bool set)
    {
        var flagSet = EntityManager.HasComponent<ServerEntity>(entity);
        if (flagSet != set)
        {
            if (set)
            {
                PostUpdateCommands.AddComponent(entity, new ServerEntity());
            }
            else
            {
                PostUpdateCommands.RemoveComponent<ServerEntity>(entity);
            }
        }
    }
}