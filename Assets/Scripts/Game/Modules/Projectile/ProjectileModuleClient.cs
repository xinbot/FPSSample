using UnityEngine;
using Object = UnityEngine.Object;

public class ProjectileModuleClient
{
    [ConfigVar(Name = "projectile.logclientinfo", DefaultValue = "0", Description = "Show projectile system info")]
    public static ConfigVar LogInfo;

    [ConfigVar(Name = "projectile.drawclientdebug", DefaultValue = "0", Description = "Show projectile system debug")]
    public static ConfigVar DrawDebug;

    private readonly GameWorld _world;
    private readonly GameObject _systemRoot;
    private readonly ProjectileModuleSettings _settings;

    private readonly HandleClientProjectileRequests _handleRequests;
    private readonly CreateProjectileMovementCollisionQueries _createProjectileMovementQueries;
    private readonly HandleProjectileMovementCollisionQuery _handleProjectileMovementQueries;

    private readonly HandleProjectileSpawn _handleProjectileSpawn;
    private readonly RemoveMispredictedProjectiles _removeMisPredictedProjectiles;
    private readonly DespawnClientProjectiles _deSpawnClientProjectiles;
    private readonly UpdateClientProjectilesNonPredicted _updateClientProjectilesNonPredicted;
    private readonly UpdateClientProjectilesPredicted _updateClientProjectilesPredicted;

    public ProjectileModuleClient(GameWorld world, BundledResourceManager resourceSystem)
    {
        _world = world;

        if (world.SceneRoot != null)
        {
            _systemRoot = new GameObject("ProjectileSystem");
            _systemRoot.transform.SetParent(world.SceneRoot.transform);
        }

        _settings = Resources.Load<ProjectileModuleSettings>("ProjectileModuleSettings");

        var clientProjectileFactory =
            new ClientProjectileFactory(_world, _world.GetEntityManager(), _systemRoot, resourceSystem);

        _handleRequests = _world.GetECSWorld()
            .CreateManager<HandleClientProjectileRequests>(_world, resourceSystem, _systemRoot,
                clientProjectileFactory);

        _handleProjectileSpawn = _world.GetECSWorld()
            .CreateManager<HandleProjectileSpawn>(_world, _systemRoot, resourceSystem, clientProjectileFactory);

        _removeMisPredictedProjectiles = _world.GetECSWorld().CreateManager<RemoveMispredictedProjectiles>(_world);

        _deSpawnClientProjectiles = _world.GetECSWorld()
            .CreateManager<DespawnClientProjectiles>(_world, clientProjectileFactory);

        _createProjectileMovementQueries =
            _world.GetECSWorld().CreateManager<CreateProjectileMovementCollisionQueries>(_world);

        _handleProjectileMovementQueries =
            _world.GetECSWorld().CreateManager<HandleProjectileMovementCollisionQuery>(_world);

        _updateClientProjectilesPredicted =
            _world.GetECSWorld().CreateManager<UpdateClientProjectilesPredicted>(_world);

        _updateClientProjectilesNonPredicted =
            _world.GetECSWorld().CreateManager<UpdateClientProjectilesNonPredicted>(_world);
    }

    public void Shutdown()
    {
        _world.GetECSWorld().DestroyManager(_handleRequests);
        _world.GetECSWorld().DestroyManager(_handleProjectileSpawn);
        _world.GetECSWorld().DestroyManager(_removeMisPredictedProjectiles);
        _world.GetECSWorld().DestroyManager(_deSpawnClientProjectiles);
        _world.GetECSWorld().DestroyManager(_createProjectileMovementQueries);
        _world.GetECSWorld().DestroyManager(_handleProjectileMovementQueries);
        _world.GetECSWorld().DestroyManager(_updateClientProjectilesPredicted);
        _world.GetECSWorld().DestroyManager(_updateClientProjectilesNonPredicted);

        if (_systemRoot != null)
        {
            Object.Destroy(_systemRoot);
        }

        Resources.UnloadAsset(_settings);
    }

    public void StartPredictedMovement()
    {
        _createProjectileMovementQueries.Update();
    }

    public void FinalizePredictedMovement()
    {
        _handleProjectileMovementQueries.Update();
    }

    public void HandleProjectileSpawn()
    {
        _handleProjectileSpawn.Update();
        _removeMisPredictedProjectiles.Update();
    }

    public void HandleProjectileDeSpawn()
    {
        _deSpawnClientProjectiles.Update();
    }

    public void HandleProjectileRequests()
    {
        _handleRequests.Update();
    }

    public void UpdateClientProjectilesNonPredicted()
    {
        _updateClientProjectilesNonPredicted.Update();
    }

    public void UpdateClientProjectilesPredicted()
    {
        _updateClientProjectilesPredicted.Update();
    }
}