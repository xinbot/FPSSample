using System.Collections.Generic;
using Unity.Entities;

public class ItemModule
{
    private readonly List<ScriptBehaviourManager> _handleSpawnSystems = new List<ScriptBehaviourManager>();
    private readonly List<ScriptBehaviourManager> _systems = new List<ScriptBehaviourManager>();
    private readonly GameWorld _world;

    public ItemModule(GameWorld world)
    {
        _world = world;

        // TODO (mogensh) make server version without all this client stuff
        _systems.Add(_world.GetECSWorld().CreateManager<RobotWeaponClientProjectileSpawnHandler>(world));
        _systems.Add(_world.GetECSWorld().CreateManager<TerraformerWeaponClientProjectileSpawnHandler>(world));
        _systems.Add(_world.GetECSWorld().CreateManager<UpdateTerraformerWeaponA>(world));
        _systems.Add(_world.GetECSWorld().CreateManager<UpdateItemActionTimelineTrigger>(world));
        _systems.Add(_world.GetECSWorld().CreateManager<System_RobotWeaponA>(world));
    }

    public void HandleSpawn()
    {
        foreach (var system in _handleSpawnSystems)
        {
            system.Update();
        }
    }

    public void Shutdown()
    {
        foreach (var system in _handleSpawnSystems)
        {
            _world.GetECSWorld().DestroyManager(system);
        }

        foreach (var system in _systems)
        {
            _world.GetECSWorld().DestroyManager(system);
        }
    }

    public void LateUpdate()
    {
        foreach (var system in _systems)
        {
            system.Update();
        }
    }
}