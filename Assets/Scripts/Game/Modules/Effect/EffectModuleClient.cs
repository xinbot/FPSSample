public class EffectModuleClient
{
    private readonly GameWorld _gameWorld;
    private readonly BundledResourceManager _resourceSystem;

    private HandleSpatialEffectRequests _handleSpatialEffectRequests;
    private HandleHitScanEffectRequests _handleHitScanEffectRequests;
    private VFXSystem _VFXSystem;

    public EffectModuleClient(GameWorld world, BundledResourceManager resourceSystem)
    {
        _gameWorld = world;
        _resourceSystem = resourceSystem;

        _handleSpatialEffectRequests =
            _gameWorld.GetECSWorld().CreateManager<HandleSpatialEffectRequests>(_gameWorld);

        _handleHitScanEffectRequests =
            _gameWorld.GetECSWorld().CreateManager<HandleHitScanEffectRequests>(_gameWorld);

        _VFXSystem = _gameWorld.GetECSWorld().CreateManager<VFXSystem>();
    }

    public void Shutdown()
    {
        _gameWorld.GetECSWorld().DestroyManager(_handleSpatialEffectRequests);
        _gameWorld.GetECSWorld().DestroyManager(_handleHitScanEffectRequests);
        _gameWorld.GetECSWorld().DestroyManager(_VFXSystem);
    }

    public void ClientUpdate()
    {
        _handleSpatialEffectRequests.Update();
        _handleHitScanEffectRequests.Update();
        _VFXSystem.Update();
    }
}