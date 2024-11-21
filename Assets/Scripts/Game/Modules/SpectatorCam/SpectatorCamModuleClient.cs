public class SpectatorCamModuleClient
{
    private readonly GameWorld _world;
    private readonly UpdateSpectatorCam _updateSpectatorCam;
    private readonly UpdateSpectatorCamControl _updateSpectatorCamControl;

    public SpectatorCamModuleClient(GameWorld world)
    {
        _world = world;
        _updateSpectatorCam = _world.GetECSWorld().CreateManager<UpdateSpectatorCam>(_world);
        _updateSpectatorCamControl = _world.GetECSWorld().CreateManager<UpdateSpectatorCamControl>(_world);
    }

    public void Shutdown()
    {
        _world.GetECSWorld().DestroyManager(_updateSpectatorCam);
        _world.GetECSWorld().DestroyManager(_updateSpectatorCamControl);
    }

    public void Update()
    {
        _updateSpectatorCam.Update();
        _updateSpectatorCamControl.Update();
    }
}