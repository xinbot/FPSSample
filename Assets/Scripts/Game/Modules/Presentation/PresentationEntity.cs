using System;
using Unity.Entities;
using UnityEngine;

public class PresentationEntity : MonoBehaviour
{
    [AssetType(typeof(ReplicatedEntityFactory))]
    public WeakAssetReference presentationOwner;

    [NonSerialized] public Entity OwnerEntity;

    // Project specific
    public UInt16 platformFlags;

    // Owner type dependent
    public UInt32 type;

    // Variation, replicated on owner
    public UInt16 variation;
}