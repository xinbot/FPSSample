using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct PresentationOwnerData : IComponentData
{
    public int variation;
    public int currentVariation;
    public Entity currentVariationEntity;

    public PresentationOwnerData(int variation)
    {
        this.variation = variation;
        currentVariation = -1;
        currentVariationEntity = Entity.Null;
    }
}

public class PresentationOwner : ComponentDataProxy<PresentationOwnerData>
{
}

[DisableAutoCreation]
public class UpdatePresentationOwners : BaseComponentSystem
{
    private ComponentGroup _group;
    private readonly PresentationRegistry _presentationRegistry;
    private readonly BundledResourceManager _resourceManager;

    private readonly List<Entity> _entityBuffer = new List<Entity>(16);
    private readonly List<PresentationOwnerData> _typeDataBuffer = new List<PresentationOwnerData>(16);

    public UpdatePresentationOwners(GameWorld world, BundledResourceManager resourceManager) : base(world)
    {
        _presentationRegistry = resourceManager.GetResourceRegistry<PresentationRegistry>();
        _resourceManager = resourceManager;
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(PresentationOwnerData));
    }

    protected override void OnUpdate()
    {
        // Add entities that needs change to buffer (as we cant destroy/create while iterating)
        var gameEntityTypeArray = _group.GetComponentDataArray<PresentationOwnerData>();
        var entityArray = _group.GetEntityArray();
        _entityBuffer.Clear();
        _typeDataBuffer.Clear();

        for (int i = 0; i < gameEntityTypeArray.Length; i++)
        {
            var typeData = gameEntityTypeArray[i];

            if (typeData.variation == typeData.currentVariation)
            {
                continue;
            }

            _entityBuffer.Add(entityArray[i]);
            _typeDataBuffer.Add(typeData);
        }

        for (int i = 0; i < _entityBuffer.Count; i++)
        {
            var entity = _entityBuffer[i];
            var typeData = _typeDataBuffer[i];

            var replicatedData = EntityManager.GetComponentData<ReplicatedEntityData>(entity);

            WeakAssetReference presentationGuid;
            var found = _presentationRegistry.GetPresentation(replicatedData.assetGuid, out presentationGuid);

            if (!found)
            {
                continue;
            }

            var presentation = _resourceManager.CreateEntity(presentationGuid);
            GameDebug.Assert(presentation != Entity.Null, "failed to create presentation");

            typeData.currentVariation = typeData.variation;
            typeData.currentVariationEntity = presentation;
            EntityManager.SetComponentData(entity, typeData);

            var presentationEntity = EntityManager.GetComponentObject<PresentationEntity>(presentation);
            presentationEntity.OwnerEntity = entity;
        }
    }
}

[DisableAutoCreation]
public class HandlePresentationOwnerDesawn : DeinitializeComponentDataSystem<PresentationOwnerData>
{
    public HandlePresentationOwnerDesawn(GameWorld world) : base(world)
    {
    }

    protected override void Deinitialize(Entity entity, PresentationOwnerData component)
    {
        if (component.currentVariationEntity != Entity.Null)
        {
            // TODO (mogensh) for now we know presentation is a game object. We should support entity with entity group
            var gameObject = EntityManager.GetComponentObject<Transform>(component.currentVariationEntity).gameObject;
            m_world.RequestDespawn(gameObject);

            component.currentVariation = -1;
            EntityManager.SetComponentData(entity, component);
        }
    }
}