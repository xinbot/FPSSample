using UnityEngine;
using Unity.Entities;

[DisableAutoCreation]
public class HandleServerProjectileRequests : BaseComponentSystem
{
    private ComponentGroup _group;

    private readonly BundledResourceManager _resourceSystem;
    private readonly ProjectileModuleSettings _settings;

    public HandleServerProjectileRequests(GameWorld world, BundledResourceManager resourceSystem) : base(world)
    {
        _resourceSystem = resourceSystem;
        _settings = Resources.Load<ProjectileModuleSettings>("ProjectileModuleSettings");
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(ProjectileRequest));
    }

    protected override void OnDestroyManager()
    {
        base.OnDestroyManager();
        Resources.UnloadAsset(_settings);
    }

    protected override void OnUpdate()
    {
        var entityArray = _group.GetEntityArray();
        var requestArray = _group.GetComponentDataArray<ProjectileRequest>();

        // Copy requests as spawning will invalidate Group 
        var requests = new ProjectileRequest[requestArray.Length];
        for (var i = 0; i < requestArray.Length; i++)
        {
            requests[i] = requestArray[i];
            PostUpdateCommands.DestroyEntity(entityArray[i]);
        }

        // Handle requests
        var projectileRegistry = _resourceSystem.GetResourceRegistry<ProjectileRegistry>();
        foreach (var request in requests)
        {
            var registryIndex = projectileRegistry.FindIndex(request.ProjectileAssetGuid);
            if (registryIndex == -1)
            {
                GameDebug.LogError("Cant find asset guid in registry");
                continue;
            }

            var projectileEntity = _settings.projectileFactory.Create(EntityManager, _resourceSystem, m_world);

            var projectileData = EntityManager.GetComponentData<ProjectileData>(projectileEntity);
            projectileData.SetupFromRequest(request, registryIndex);
            projectileData.Initialize(projectileRegistry);

            PostUpdateCommands.SetComponent(projectileEntity, projectileData);
            PostUpdateCommands.AddComponent(projectileEntity, new UpdateProjectileFlag());
        }
    }
}