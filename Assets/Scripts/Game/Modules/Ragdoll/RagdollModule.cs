using UnityEngine;

public class RagdollModule
{
    private readonly GameWorld _world;
    private readonly GameObject _systemRoot;

    private readonly UpdateRagdolls _updateRagdolls;
    private readonly HandleRagdollSpawn _handleRagdollSpawn;
    private readonly HandleRagdollDespawn _handleRagdollDespawn;

    public RagdollModule(GameWorld world)
    {
        _world = world;

        if (world.SceneRoot != null)
        {
            _systemRoot = new GameObject("RagdollSystem");
            _systemRoot.transform.SetParent(world.SceneRoot.transform);
        }

        _updateRagdolls = _world.GetECSWorld().CreateManager<UpdateRagdolls>(_world);
        _handleRagdollSpawn = _world.GetECSWorld().CreateManager<HandleRagdollSpawn>(_world, _systemRoot);
        _handleRagdollDespawn = _world.GetECSWorld().CreateManager<HandleRagdollDespawn>(_world);
    }

    public void Shutdown()
    {
        _world.GetECSWorld().DestroyManager(_updateRagdolls);
        _world.GetECSWorld().DestroyManager(_handleRagdollSpawn);
        _world.GetECSWorld().DestroyManager(_handleRagdollDespawn);

        if (_systemRoot != null)
        {
            Object.Destroy(_systemRoot);
        }
    }

    public void HandleSpawning()
    {
        _handleRagdollSpawn.Update();
    }

    public void HandleDespawning()
    {
        _handleRagdollDespawn.Update();
    }

    public void LateUpdate()
    {
        _updateRagdolls.Update();
    }
}