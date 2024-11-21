public class SpectatorCamModuleServer
{
    private readonly GameWorld _world;
    private readonly HandleSpectatorCamRequests _handleSpectatorCamRequests;

    public SpectatorCamModuleServer(GameWorld world, BundledResourceManager resourceManager)
    {
        _world = world;
        _handleSpectatorCamRequests =
            world.GetECSWorld().CreateManager<HandleSpectatorCamRequests>(world, resourceManager);
    }

    public void Shutdown()
    {
        _world.GetECSWorld().DestroyManager(_handleSpectatorCamRequests);
    }

    public void HandleSpawnRequests()
    {
        _handleSpectatorCamRequests.Update();
    }
}