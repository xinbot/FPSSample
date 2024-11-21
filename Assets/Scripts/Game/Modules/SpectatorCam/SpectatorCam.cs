using Networking;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct SpectatorCamData : IComponentData, IReplicatedComponent
{
    public float3 Position;
    public quaternion Rotation;

    public static IReplicatedComponentSerializerFactory CreateSerializerFactory()
    {
        return new ReplicatedComponentSerializerFactory<SpectatorCamData>();
    }

    public void Serialize(ref SerializeContext context, ref NetworkWriter writer)
    {
        writer.WriteVector3Q("pos", Position, 1);
        writer.WriteQuaternionQ("rot", Rotation, 1);
    }

    public void Deserialize(ref SerializeContext context, ref NetworkReader reader)
    {
        Position = reader.ReadVector3Q();
        Rotation = reader.ReadQuaternionQ();
    }
}

public class SpectatorCam : ComponentDataProxy<SpectatorCamData>
{
}

public struct SpectatorCamSpawnRequest : IComponentData
{
    public Entity PlayerEntity;
    public Vector3 Position;
    public Quaternion Rotation;

    public static void Create(EntityCommandBuffer commandBuffer, Vector3 position, Quaternion rotation,
        Entity playerEntity)
    {
        var data = new SpectatorCamSpawnRequest
        {
            PlayerEntity = playerEntity,
            Position = position,
            Rotation = rotation,
        };
        var entity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(entity, data);
    }
}

[DisableAutoCreation]
public class UpdateSpectatorCam : BaseComponentSystem
{
    private ComponentGroup _group;

    public UpdateSpectatorCam(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(UserCommandComponentData), typeof(SpectatorCamData));
    }

    protected override void OnUpdate()
    {
        var spectatorCamEntityArray = _group.GetEntityArray();
        var spectatorCamArray = _group.GetComponentDataArray<SpectatorCamData>();
        var userCommandArray = _group.GetComponentDataArray<UserCommandComponentData>();
        for (var i = 0; i < spectatorCamArray.Length; i++)
        {
            var command = userCommandArray[i].command;
            var spectatorCam = spectatorCamArray[i];

            spectatorCam.Rotation = Quaternion.Euler(new Vector3(90 - command.lookPitch, command.lookYaw, 0));

            var forward = math.mul(spectatorCam.Rotation, Vector3.forward);
            var right = math.mul(spectatorCam.Rotation, Vector3.right);
            var maxVel = 3 * m_world.WorldTime.tickInterval;
            var moveDir = forward * Mathf.Cos(command.moveYaw * Mathf.Deg2Rad) +
                          right * Mathf.Sin(command.moveYaw * Mathf.Deg2Rad);
            spectatorCam.Position += moveDir * maxVel * command.moveMagnitude;

            EntityManager.SetComponentData(spectatorCamEntityArray[i], spectatorCam);
        }
    }
}

[DisableAutoCreation]
public class HandleSpectatorCamRequests : BaseComponentSystem
{
    private ComponentGroup _group;

    private readonly SpectatorCamSettings _settings;
    private readonly BundledResourceManager _resourceManager;
    
    public HandleSpectatorCamRequests(GameWorld world, BundledResourceManager resourceManager) : base(world)
    {
        _resourceManager = resourceManager;
        _settings = Resources.Load<SpectatorCamSettings>("SpectatorCamSettings");
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(SpectatorCamSpawnRequest));
    }

    protected override void OnUpdate()
    {
        var requestArray = _group.GetComponentDataArray<SpectatorCamSpawnRequest>();
        if (requestArray.Length == 0)
        {
            return;
        }

        var entityArray = _group.GetEntityArray();
        
        // Copy requests as spawning will invalidate Group
        var spawnRequests = new SpectatorCamSpawnRequest[requestArray.Length];
        for (var i = 0; i < requestArray.Length; i++)
        {
            spawnRequests[i] = requestArray[i];
            PostUpdateCommands.DestroyEntity(entityArray[i]);
        }

        for (var i = 0; i < spawnRequests.Length; i++)
        {
            var request = spawnRequests[i];
            var playerState = EntityManager.GetComponentObject<PlayerState>(request.PlayerEntity);
            
            var resource = _resourceManager.GetSingleAssetResource(_settings.spectatorCamPrefab);

            GameDebug.Assert(resource != null);
            
            var prefab = (GameObject) resource;
            GameDebug.Log("Spawning spectator cam");
            
            var goe = m_world.Spawn<GameObjectEntity>(prefab);
            goe.name = prefab.name;
            var entity = goe.Entity;

            var spectatorCam = EntityManager.GetComponentData<SpectatorCamData>(entity);
            spectatorCam.Position = request.Position;
            spectatorCam.Rotation = request.Rotation;
            EntityManager.SetComponentData(entity, spectatorCam);

            playerState.controlledEntity = entity;
        }
    }
}