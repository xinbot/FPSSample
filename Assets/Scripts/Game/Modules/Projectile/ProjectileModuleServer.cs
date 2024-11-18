using UnityEngine.Profiling;

public class ProjectileModuleServer
{
    [ConfigVar(Name = "projectile.drawserverdebug", DefaultValue = "0", Description = "Show projectile system debug")]
    public static ConfigVar DrawDebug;

    private readonly GameWorld _gameWorld;
    private readonly HandleServerProjectileRequests _handleRequests;
    private readonly CreateProjectileMovementCollisionQueries _createMovementQueries;
    private readonly HandleProjectileMovementCollisionQuery _handleMovementQueries;
    private readonly DespawnProjectiles _deSpawnProjectiles;

    public ProjectileModuleServer(GameWorld gameWorld, BundledResourceManager resourceSystem)
    {
        _gameWorld = gameWorld;

        _handleRequests = _gameWorld.GetECSWorld()
            .CreateManager<HandleServerProjectileRequests>(_gameWorld, resourceSystem);

        _createMovementQueries = _gameWorld.GetECSWorld()
            .CreateManager<CreateProjectileMovementCollisionQueries>(_gameWorld);

        _handleMovementQueries =
            _gameWorld.GetECSWorld().CreateManager<HandleProjectileMovementCollisionQuery>(_gameWorld);

        _deSpawnProjectiles = _gameWorld.GetECSWorld().CreateManager<DespawnProjectiles>(_gameWorld);
    }

    public void Shutdown()
    {
        _gameWorld.GetECSWorld().DestroyManager(_handleRequests);
        _gameWorld.GetECSWorld().DestroyManager(_createMovementQueries);
        _gameWorld.GetECSWorld().DestroyManager(_handleMovementQueries);
        _gameWorld.GetECSWorld().DestroyManager(_deSpawnProjectiles);
    }

    public void HandleRequests()
    {
        Profiler.BeginSample("ProjectileModuleServer.CreateMovementQueries");

        _handleRequests.Update();

        Profiler.EndSample();
    }

    public void MovementStart()
    {
        Profiler.BeginSample("ProjectileModuleServer.CreateMovementQueries");

        _createMovementQueries.Update();

        Profiler.EndSample();
    }

    public void MovementResolve()
    {
        Profiler.BeginSample("ProjectileModuleServer.HandleMovementQueries");

        _handleMovementQueries.Update();
        _deSpawnProjectiles.Update();

        Profiler.EndSample();
    }
}