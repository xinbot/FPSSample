using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

[DisableAutoCreation]
public class PreviewGameMode : BaseComponentSystem
{
    public int RespawnDelay = 20;

    private readonly PlayerState _player;
    private Vector3 _spawnPos;
    private Quaternion _spawnRot;

    private bool _respawnPending;
    private float _respawnTime;

    public PreviewGameMode(GameWorld world, PlayerState player) : base(world)
    {
        _player = player;

        // Fallback spawnPos!
        _spawnPos = new Vector3(0.0f, 2.0f, 0.0f);
        _spawnRot = new Quaternion();
    }

    protected override void OnUpdate()
    {
        var playerEntity = _player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = m_world.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);
        if (charControl.requestedCharacterType != -1 && charControl.characterType != charControl.requestedCharacterType)
        {
            charControl.characterType = charControl.requestedCharacterType;
            charControl.requestedCharacterType = -1;

            GameDebug.Log($"PreviewGameMode. Respawning as char requested. New CharType:{charControl.characterType}");

            Spawn(true);
            return;
        }

        if (_player.controlledEntity == Entity.Null)
        {
            GameDebug.Log($"PreviewGameMode. Spawning as we have to char. CharType:{charControl.characterType}");

            Spawn(false);
            return;
        }

        if (m_world.GetEntityManager().HasComponent<HealthStateData>(_player.controlledEntity))
        {
            var healthState = m_world.GetEntityManager().GetComponentData<HealthStateData>(_player.controlledEntity);
            if (!_respawnPending && healthState.health == 0)
            {
                _respawnPending = true;
                _respawnTime = Time.time + RespawnDelay;
            }

            if (_respawnPending && Time.time > _respawnTime)
            {
                Spawn(false);
                _respawnPending = false;
            }
        }
    }

    private void Spawn(bool keepCharPosition)
    {
        if (keepCharPosition && _player.controlledEntity != Entity.Null &&
            m_world.GetEntityManager().HasComponent<CharacterInterpolatedData>(_player.controlledEntity))
        {
            var charPresentationState = m_world.GetEntityManager()
                .GetComponentData<CharacterInterpolatedData>(_player.controlledEntity);
            _spawnPos = charPresentationState.position;
            _spawnRot = Quaternion.Euler(0f, charPresentationState.rotation, 0f);
        }
        else
        {
            FindSpawnTransform();
        }

        // DeSpawn old controlled
        if (_player.controlledEntity != Entity.Null)
        {
            if (EntityManager.HasComponent<Character>(_player.controlledEntity))
            {
                CharacterDespawnRequest.Create(PostUpdateCommands, _player.controlledEntity);
            }

            _player.controlledEntity = Entity.Null;
        }

        var playerEntity = _player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = EntityManager.GetComponentObject<PlayerCharacterControl>(playerEntity);
        if (charControl.characterType == 1000)
        {
            SpectatorCamSpawnRequest.Create(PostUpdateCommands, _spawnPos, _spawnRot, playerEntity);
        }
        else
        {
            CharacterSpawnRequest.Create(PostUpdateCommands, charControl.characterType, _spawnPos, _spawnRot,
                playerEntity);
        }
    }

    private void FindSpawnTransform()
    {
        // Find random spawn point that matches teamIndex
        var spawnPoints = Object.FindObjectsOfType<SpawnPoint>();
        var offset = UnityEngine.Random.Range(0, spawnPoints.Length);
        for (var i = 0; i < spawnPoints.Length; ++i)
        {
            var sp = spawnPoints[(i + offset) % spawnPoints.Length];
            if (sp.teamIndex != _player.teamIndex)
            {
                continue;
            }

            var transform = sp.transform;
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;
            break;
        }
    }
}

public class PreviewGameLoop : IGameLoop
{
    private StateMachine<PreviewState> _stateMachine;
    private BundledResourceManager _resourceSystem;

    private GameWorld _gameWorld;
    private CharacterModulePreview _characterModule;
    private ProjectileModuleClient _projectileModule;
    private HitCollisionModule _hitCollisionModule;
    private PlayerModuleClient _playerModuleClient;
    private PlayerModuleServer _playerModuleServer;
    private SpectatorCamModuleServer _spectatorCamModuleServer;
    private SpectatorCamModuleClient _spectatorCamModuleClient;
    private EffectModuleClient _effectModule;
    private ItemModule _itemModule;
    private UpdateReplicatedOwnerFlag _updateReplicatedOwnerFlag;

    private RagdollModule _ragDollModule;
    private SpinSystem _spinSystem;
    private DespawnProjectiles _deSpawnProjectiles;

    private PreviewGameMode _previewGameMode;
    private DamageAreaSystemServer _damageAreaSystemServer;

    private TeleporterSystemServer _teleporterSystemServer;
    private TeleporterSystemClient _teleporterSystemClient;

    private HandlePresentationOwnerDesawn _handlePresentationOwnerDeSpawn;
    private UpdatePresentationOwners _updatePresentationOwners;

    private HandleGrenadeRequest _handleGrenadeRequests;
    private StartGrenadeMovement _startGrenadeMovement;
    private FinalizeGrenadeMovement _finalizeGrenadeMovement;
    private ApplyGrenadePresentation _applyGrenadePresentation;

    private HandleNamePlateSpawn _handleNamePlateOwnerSpawn;
    private HandleNamePlateDespawn _handleNamePlateOwnerDeSpawn;
    private UpdateNamePlates _updateNamePlates;

    private MoverUpdate _moverUpdate;
    private DestructiblePropSystemClient _destructiblePropSystemClient;
    private UpdateDestructableProps _updateDestructibleProps;

    private TwistSystem _twistSystem;
    private FanSystem _fanSystem;
    private TranslateScaleSystem _translateScaleSystem;

    private PlayerState _player;

    private GameTime _gameTime = new GameTime(60);

    public bool Init(string[] args)
    {
        _stateMachine = new StateMachine<PreviewState>();
        _stateMachine.Add(PreviewState.Loading, null, UpdateLoadingState, null);
        _stateMachine.Add(PreviewState.Active, EnterActiveState, UpdateStateActive, LeaveActiveState);

        Console.AddCommand("nextchar", CmdNextHero, "Select next character", GetHashCode());
        Console.AddCommand("nextteam", CmdNextTeam, "Select next character", GetHashCode());
        Console.AddCommand("spectator", CmdSpectatorCam, "Select spectator cam", GetHashCode());
        Console.AddCommand("respawn", CmdRespawn,
            "Force a respawn. Optional argument defines now many seconds until respawn", this.GetHashCode());

        Console.SetOpen(false);

        _gameWorld = new GameWorld("World[PreviewGameLoop]");

        if (args.Length > 0)
        {
            Game.game.LevelManager.LoadLevel(args[0]);
            _stateMachine.SwitchTo(PreviewState.Loading);
        }
        else
        {
            _stateMachine.SwitchTo(PreviewState.Active);
        }

        GameDebug.Log("Preview initialized");
        return true;
    }

    public void Shutdown()
    {
        GameDebug.Log("PreviewGameState shutdown");
        Console.RemoveCommandsWithTag(this.GetHashCode());

        _stateMachine.Shutdown();

        _playerModuleServer.Shutdown();

        Game.game.LevelManager.UnloadLevel();

        _gameWorld.Shutdown();
    }

    private void UpdateLoadingState()
    {
        if (Game.game.LevelManager.IsCurrentLevelLoaded())
        {
            _stateMachine.SwitchTo(PreviewState.Active);
        }
    }

    public void Update()
    {
        _stateMachine.Update();
    }

    private void EnterActiveState()
    {
        _gameWorld.RegisterSceneEntities();

        _resourceSystem = new BundledResourceManager(_gameWorld, "BundledResources/Client");

        // Create serializers so we get errors in preview build
        var dataComponentSerializers = new DataComponentSerializers();

        _characterModule = new CharacterModulePreview(_gameWorld, _resourceSystem);
        _projectileModule = new ProjectileModuleClient(_gameWorld, _resourceSystem);
        _hitCollisionModule = new HitCollisionModule(_gameWorld, 1, 2);
        _playerModuleClient = new PlayerModuleClient(_gameWorld);
        _playerModuleServer = new PlayerModuleServer(_gameWorld, _resourceSystem);
        _spectatorCamModuleServer = new SpectatorCamModuleServer(_gameWorld, _resourceSystem);
        _spectatorCamModuleClient = new SpectatorCamModuleClient(_gameWorld);

        _effectModule = new EffectModuleClient(_gameWorld, _resourceSystem);
        _itemModule = new ItemModule(_gameWorld);
        _ragDollModule = new RagdollModule(_gameWorld);

        _deSpawnProjectiles = _gameWorld.GetECSWorld().CreateManager<DespawnProjectiles>(_gameWorld);
        _damageAreaSystemServer = _gameWorld.GetECSWorld().CreateManager<DamageAreaSystemServer>(_gameWorld);

        _teleporterSystemServer = _gameWorld.GetECSWorld().CreateManager<TeleporterSystemServer>(_gameWorld);
        _teleporterSystemClient = _gameWorld.GetECSWorld().CreateManager<TeleporterSystemClient>(_gameWorld);

        _updateDestructibleProps = _gameWorld.GetECSWorld().CreateManager<UpdateDestructableProps>(_gameWorld);
        _destructiblePropSystemClient =
            _gameWorld.GetECSWorld().CreateManager<DestructiblePropSystemClient>(_gameWorld);

        _updatePresentationOwners = _gameWorld.GetECSWorld()
            .CreateManager<UpdatePresentationOwners>(_gameWorld, _resourceSystem);
        _handlePresentationOwnerDeSpawn =
            _gameWorld.GetECSWorld().CreateManager<HandlePresentationOwnerDesawn>(_gameWorld);

        _handleGrenadeRequests =
            _gameWorld.GetECSWorld().CreateManager<HandleGrenadeRequest>(_gameWorld, _resourceSystem);
        _startGrenadeMovement = _gameWorld.GetECSWorld().CreateManager<StartGrenadeMovement>(_gameWorld);
        _finalizeGrenadeMovement = _gameWorld.GetECSWorld().CreateManager<FinalizeGrenadeMovement>(_gameWorld);
        _applyGrenadePresentation = _gameWorld.GetECSWorld().CreateManager<ApplyGrenadePresentation>(_gameWorld);

        _moverUpdate = _gameWorld.GetECSWorld().CreateManager<MoverUpdate>(_gameWorld);

        _spinSystem = _gameWorld.GetECSWorld().CreateManager<SpinSystem>(_gameWorld);
        _handleNamePlateOwnerSpawn = _gameWorld.GetECSWorld().CreateManager<HandleNamePlateSpawn>(_gameWorld);
        _handleNamePlateOwnerDeSpawn = _gameWorld.GetECSWorld().CreateManager<HandleNamePlateDespawn>(_gameWorld);
        _updateNamePlates = _gameWorld.GetECSWorld().CreateManager<UpdateNamePlates>(_gameWorld);

        _updateReplicatedOwnerFlag = _gameWorld.GetECSWorld().CreateManager<UpdateReplicatedOwnerFlag>(_gameWorld);

        _twistSystem = new TwistSystem(_gameWorld);
        _fanSystem = new FanSystem(_gameWorld);
        _translateScaleSystem = new TranslateScaleSystem(_gameWorld);

        _playerModuleClient.RegisterLocalPlayer(0, null);

        // Spawn PlayerState, Character and link up LocalPlayer
        _player = _playerModuleServer.CreatePlayer(_gameWorld, 0, "LocalHero", true);

        var playerEntity = _player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = _gameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);
        charControl.characterType = math.max(Game.CharacterType.IntValue, 0);
        _player.teamIndex = 0;

        _previewGameMode = _gameWorld.GetECSWorld().CreateManager<PreviewGameMode>(_gameWorld, _player);

        Game.SetMousePointerLock(true);
    }

    private void LeaveActiveState()
    {
        _characterModule.Shutdown();
        _projectileModule.Shutdown();
        _ragDollModule.Shutdown();
        _hitCollisionModule.Shutdown();
        _playerModuleClient.Shutdown();
        _playerModuleServer.Shutdown();
        _spectatorCamModuleServer.Shutdown();
        _spectatorCamModuleClient.Shutdown();
        _effectModule.Shutdown();
        _itemModule.Shutdown();

        _gameWorld.GetECSWorld().DestroyManager(_damageAreaSystemServer);
        _gameWorld.GetECSWorld().DestroyManager(_deSpawnProjectiles);

        _gameWorld.GetECSWorld().DestroyManager(_teleporterSystemServer);
        _gameWorld.GetECSWorld().DestroyManager(_teleporterSystemClient);

        _gameWorld.GetECSWorld().DestroyManager(_updateDestructibleProps);
        _gameWorld.GetECSWorld().DestroyManager(_destructiblePropSystemClient);

        _gameWorld.GetECSWorld().DestroyManager(_updatePresentationOwners);
        _gameWorld.GetECSWorld().DestroyManager(_handlePresentationOwnerDeSpawn);

        _gameWorld.GetECSWorld().DestroyManager(_handleGrenadeRequests);
        _gameWorld.GetECSWorld().DestroyManager(_startGrenadeMovement);
        _gameWorld.GetECSWorld().DestroyManager(_finalizeGrenadeMovement);
        _gameWorld.GetECSWorld().DestroyManager(_applyGrenadePresentation);

        _gameWorld.GetECSWorld().DestroyManager(_moverUpdate);
        _gameWorld.GetECSWorld().DestroyManager(_previewGameMode);
        _gameWorld.GetECSWorld().DestroyManager(_spinSystem);
        _gameWorld.GetECSWorld().DestroyManager(_handleNamePlateOwnerSpawn);
        _gameWorld.GetECSWorld().DestroyManager(_handleNamePlateOwnerDeSpawn);
        _gameWorld.GetECSWorld().DestroyManager(_updateNamePlates);

        _gameWorld.GetECSWorld().DestroyManager(_updateReplicatedOwnerFlag);

        _twistSystem.ShutDown();
        _fanSystem.ShutDown();
        _translateScaleSystem.ShutDown();

        _resourceSystem.Shutdown();
    }

    private void UpdateStateActive()
    {
        // Sample input
        bool userInputEnabled = Game.GetMousePointerLock();
        _playerModuleClient.SampleInput(userInputEnabled, Time.deltaTime, 0);

        if (_gameTime.tickRate != Game.ServerTickRate.IntValue)
        {
            _gameTime.tickRate = Game.ServerTickRate.IntValue;
        }

        if (Input.GetKeyUp(KeyCode.H) && Game.AllowCharChange.IntValue == 1)
        {
            CmdNextHero(null);
        }

        if (Input.GetKeyUp(KeyCode.T))
        {
            CmdNextTeam(null);
        }

        bool commandWasConsumed = false;
        while (Game.FrameTime > _gameWorld.NextTickTime)
        {
            _gameTime.Tick++;
            _gameTime.TickDuration = _gameTime.tickInterval;

            commandWasConsumed = true;

            PreviewTickUpdate();
            _gameWorld.NextTickTime += _gameWorld.WorldTime.tickInterval;
        }

        if (commandWasConsumed)
        {
            _playerModuleClient.ResetInput(userInputEnabled);
        }
    }

    public void FixedUpdate()
    {
    }

    private void PreviewTickUpdate()
    {
        _gameWorld.WorldTime = _gameTime;
        _gameWorld.frameDuration = _gameTime.TickDuration;

        _playerModuleClient.ResolveReferenceFromLocalPlayerToPlayer();
        _playerModuleClient.HandleCommandReset();
        _playerModuleClient.StoreCommand(_gameWorld.WorldTime.Tick);

        // Game mode update
        _previewGameMode.Update();

        // Handle spawn requests
        _characterModule.HandleSpawnRequests();
        _projectileModule.HandleProjectileRequests();
        _handleGrenadeRequests.Update();

        // Updates game entity presentation. After game entities are created but before component spawn handler
        _updatePresentationOwners.Update();

        _updateReplicatedOwnerFlag.Update();

        // Apply command for frame
        _playerModuleClient.RetrieveCommand(_gameWorld.WorldTime.Tick);

        // Handle spawn
        _characterModule.HandleSpawns();

        //TODO (mogensh) creates presentations, so it needs to be done first. Find better solution for ordering
        _spectatorCamModuleServer.HandleSpawnRequests();
        _hitCollisionModule.HandleSpawning();
        _handleNamePlateOwnerSpawn.Update();
        _playerModuleClient.HandleSpawn();
        _ragDollModule.HandleSpawning();
        _twistSystem.HandleSpawning();
        _fanSystem.HandleSpawning();
        _translateScaleSystem.HandleSpawning();
        _projectileModule.HandleProjectileSpawn();
        _itemModule.HandleSpawn();

        // Handle controlled entity changed
        _playerModuleClient.HandleControlledEntityChanged();
        _characterModule.HandleControlledEntityChanged();

        // Update movement of scene objects. Projectiles and grenades can also start update as they use collision data from last frame
        _spinSystem.Update();
        _moverUpdate.Update();
        _projectileModule.StartPredictedMovement();
        _startGrenadeMovement.Update();

        // Update movement of player controlled units (depends on moveable scene objects being done)
        _spectatorCamModuleClient.Update();
        _teleporterSystemServer.Update();
        _characterModule.AbilityRequestUpdate();
        _characterModule.MovementStart();
        _characterModule.MovementResolve();
        _characterModule.AbilityStart();
        _characterModule.AbilityResolve();

        _finalizeGrenadeMovement.Update();
        _projectileModule.FinalizePredictedMovement();

        // Handle damage        
        _hitCollisionModule.HandleSplashDamage();
        _updateDestructibleProps.Update();
        _damageAreaSystemServer.Update();
        _characterModule.HandleDamage();

        // Update presentation
        _characterModule.UpdatePresentation();
        _destructiblePropSystemClient.Update();
        _teleporterSystemClient.Update();
        _applyGrenadePresentation.Update();

        // Handle deSpawns
        _handlePresentationOwnerDeSpawn.Update();
        // TODO (mogensh) this destroys presentations and needs to be done first so its picked up. Find better solution  
        _characterModule.HandleDepawns();
        _deSpawnProjectiles.Update();
        _projectileModule.HandleProjectileDespawn();
        _handleNamePlateOwnerDeSpawn.Update();
        _twistSystem.HandleDespawning();
        _fanSystem.HandleDespawning();
        _ragDollModule.HandleDespawning();
        _hitCollisionModule.HandleDespawn();
        _translateScaleSystem.HandleDepawning();
        _gameWorld.ProcessDespawns();
    }

    public void LateUpdate()
    {
        // TODO (petera) Should the state machine actually have a lateupdate so we don't have to do this always?
        if (_stateMachine.CurrentState() == PreviewState.Active)
        {
            _gameWorld.frameDuration = Time.deltaTime;

            _translateScaleSystem.Schedule();
            var twistSystemHandle = _twistSystem.Schedule();
            _fanSystem.Schedule(twistSystemHandle);

            _hitCollisionModule.StoreColliderState();

            _characterModule.LateUpdate();
            _itemModule.LateUpdate();
            _ragDollModule.LateUpdate();

            _projectileModule.UpdateClientProjectilesPredicted();
            _effectModule.ClientUpdate();

            // Update camera
            _playerModuleClient.CameraUpdate();

            // Update UI
            _characterModule.UpdateUI();
            _updateNamePlates.Update();

            // Finalize jobs that needs to be done before rendering
            _translateScaleSystem.Complete();
            _fanSystem.Complete();
        }
    }

    private void CmdNextHero(string[] args)
    {
        if (_player == null)
        {
            return;
        }

        if (Game.AllowCharChange.IntValue != 1)
        {
            return;
        }

        var charSetupRegistry = _resourceSystem.GetResourceRegistry<HeroTypeRegistry>();
        var charSetupCount = charSetupRegistry.entries.Count;

        var playerEntity = _player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = _gameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);

        charControl.requestedCharacterType = charControl.characterType + 1;
        if (charControl.requestedCharacterType >= charSetupCount)
        {
            charControl.requestedCharacterType = 0;
        }

        GameDebug.Log($"PreviewGameLoop. Requesting char:{charControl.requestedCharacterType}");
    }

    private void CmdSpectatorCam(string[] args)
    {
        if (_player == null)
        {
            return;
        }

        if (Game.AllowCharChange.IntValue != 1)
        {
            return;
        }

        var playerEntity = _player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = _gameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);

        // Until we have better way of controlling other units than character, the spectator cam gets type 1000         
        charControl.requestedCharacterType = 1000;
    }

    private void CmdRespawn(string[] args)
    {
        if (_player == null)
        {
            return;
        }

        _previewGameMode.RespawnDelay = args.Length == 0 ? 3 : int.Parse(args[0]);

        var healthState = _gameWorld.GetEntityManager().GetComponentData<HealthStateData>(_player.controlledEntity);
        healthState.health = 0;
        _gameWorld.GetEntityManager().SetComponentData(_player.controlledEntity, healthState);
    }

    private void CmdNextTeam(string[] args)
    {
        if (_player == null)
        {
            return;
        }

        _player.teamIndex++;
        if (_player.teamIndex > 1)
        {
            _player.teamIndex = 0;
        }
    }

    private enum PreviewState
    {
        Loading,
        Active
    }
}