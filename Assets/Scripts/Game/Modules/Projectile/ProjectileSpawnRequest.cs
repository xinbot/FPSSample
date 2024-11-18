using Unity.Entities;
using Unity.Mathematics;

public struct ProjectileRequest : IComponentData
{
    public WeakAssetReference ProjectileAssetGuid;

    public int TeamId;
    public int StartTick;
    public int CollisionTestTickDelay;

    public float3 StartPosition;
    public float3 EndPosition;

    public Entity Owner;

    public static void Create(EntityCommandBuffer commandBuffer, int tick, int tickDelay,
        WeakAssetReference projectileAsset, Entity owner, int teamId, float3 startPosition, float3 endPosition)
    {
        var request = new ProjectileRequest
        {
            ProjectileAssetGuid = projectileAsset,
            StartTick = tick,
            StartPosition = startPosition,
            EndPosition = endPosition,
            Owner = owner,
            CollisionTestTickDelay = tickDelay,
            TeamId = teamId
        };

        var entity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(entity, request);
    }
}