using Networking;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Component set on projectiles that should be updated. Client only puts this on predicted projectiles
public struct UpdateProjectileFlag : IComponentData
{
}

public struct ProjectileData : IComponentData, IReplicatedComponent
{
    public int Impacted;
    public int StartTick;
    public int TypeId;

    public float3 StartPos;
    public float3 EndPos;
    public float3 ImpactPos;
    public float3 ImpactNormal;

    public Entity ProjectileOwner;

    // State Properties
    public int TeamId;
    public int RayQueryId;
    public int ImpactTick;
    public int CollisionCheckTickDelay;

    public float MAXAge;
    public float3 Position;
    public ProjectileSettings Settings;

    public static IReplicatedComponentSerializerFactory CreateSerializerFactory()
    {
        return new ReplicatedComponentSerializerFactory<ProjectileData>();
    }

    public void Serialize(ref SerializeContext context, ref NetworkWriter networkWriter)
    {
        context.refSerializer.SerializeReference(ref networkWriter, "owner", ProjectileOwner);
        networkWriter.WriteUInt16("typeId", (ushort) TypeId);
        networkWriter.WriteInt32("startTick", StartTick);
        networkWriter.WriteVector3Q("startPosition", StartPos, 2);
        networkWriter.WriteVector3Q("endPosition", EndPos, 2);
        networkWriter.WriteBoolean("impacted", Impacted == 1);
        networkWriter.WriteVector3Q("impactPosition", ImpactPos, 2);
        networkWriter.WriteVector3Q("impactNormal", ImpactNormal, 2);
    }

    public void Deserialize(ref SerializeContext context, ref NetworkReader networkReader)
    {
        context.refSerializer.DeserializeReference(ref networkReader, ref ProjectileOwner);
        TypeId = networkReader.ReadUInt16();
        StartTick = networkReader.ReadInt32();
        StartPos = networkReader.ReadVector3Q();
        EndPos = networkReader.ReadVector3Q();
        Impacted = networkReader.ReadBoolean() ? 1 : 0;
        ImpactPos = networkReader.ReadVector3Q();
        ImpactNormal = networkReader.ReadVector3Q();
    }

    public void SetupFromRequest(ProjectileRequest request, int typeId)
    {
        RayQueryId = -1;
        ProjectileOwner = request.Owner;
        TypeId = typeId;
        StartTick = request.StartTick;
        StartPos = request.StartPosition;
        EndPos = request.EndPosition;
        TeamId = request.TeamId;
        CollisionCheckTickDelay = request.CollisionTestTickDelay;
    }

    public void Initialize(ProjectileRegistry registry)
    {
        Settings = registry.entries[TypeId].definition.properties;

        MAXAge = Vector3.Magnitude(EndPos - StartPos) / Settings.velocity;
        Position = StartPos;
    }
}