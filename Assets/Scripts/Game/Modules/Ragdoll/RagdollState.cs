using System;
using Networking;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct RagdollStateData : IComponentData, IReplicatedComponent
{
    [NonSerialized] public int RagdollActive;
    [NonSerialized] public Vector3 Impulse;

    public static IReplicatedComponentSerializerFactory CreateSerializerFactory()
    {
        return new ReplicatedComponentSerializerFactory<RagdollStateData>();
    }

    public void Serialize(ref SerializeContext context, ref NetworkWriter writer)
    {
        writer.WriteBoolean("ragdollEnabled", RagdollActive == 1);
        writer.WriteVector3Q("impulse", Impulse, 1);
    }

    public void Deserialize(ref SerializeContext context, ref NetworkReader reader)
    {
        RagdollActive = reader.ReadBoolean() ? 1 : 0;
        Impulse = reader.ReadVector3Q();
    }
}

public class RagdollState : ComponentDataProxy<RagdollStateData>
{
}