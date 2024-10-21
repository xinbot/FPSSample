using Networking;
using Networking.Socket;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Profiling;
using UnityEngine.Ucg.Matchmaking;

public class ClientGameWorld
{
    public bool PredictionEnabled = true;
    public float FrameTimeScale = 1.0f;

    private readonly GameWorld _gameWorld;
    private LocalPlayer _localPlayer;

    private GameTime _predictedTime = new GameTime(60);
    public GameTime predictedTime => _predictedTime;

    private GameTime _renderTime = new GameTime(60);
    public GameTime renderTime => _renderTime;

    // External systems
    private readonly NetworkClient _networkClient;
    private readonly NetworkStatisticsClient _networkStatisticsClient;
    private readonly ClientFrontendUpdate _clientFrontendUpdate;

    // Internal systems
    private readonly CharacterModuleClient _characterModule;
    private readonly ProjectileModuleClient _projectileModule;
    private readonly HitCollisionModule _hitCollisionModule;
    private readonly PlayerModuleClient _playerModule;
    private readonly SpectatorCamModuleClient _spectatorCamModule;
    private readonly EffectModuleClient _effectModule;
    private readonly ReplicatedEntityModuleClient _replicatedEntityModule;
    private readonly ItemModule _itemModule;

    private readonly RagdollModule _ragDollSystem;
    private readonly GameModeSystemClient _gameModeSystem;

    private readonly ApplyGrenadePresentation _applyGrenadePresentation;

    private readonly HandlePresentationOwnerDesawn _handlePresentationOwnerDeSpawn;
    private readonly UpdatePresentationOwners _updatePresentationOwners;

    private readonly TwistSystem _twistSystem;
    private readonly FanSystem _fanSystem;
    private readonly TranslateScaleSystem _translateScaleSystem;

    private readonly MoverUpdate _moverUpdate;
    private readonly DestructiblePropSystemClient _destructiblePropSystem;
    private readonly HandleNamePlateSpawn _handleNamePlateOwnerSpawn;
    private readonly HandleNamePlateDespawn _handleNamePlateOwnerDeSpawn;

    private readonly UpdateNamePlates _updateNamePlates;
    private readonly SpinSystem _spinSystem;
    private readonly TeleporterSystemClient _teleporterSystem;

    public ReplicatedEntityModuleClient replicatedEntityModule
    {
        get { return _replicatedEntityModule; }
    }

    public ClientGameWorld(GameWorld world, NetworkClient networkClient,
        NetworkStatisticsClient networkStatisticsClientClient,
        BundledResourceManager resourceSystem)
    {
        _gameWorld = world;

        _networkClient = networkClient;
        _networkStatisticsClient = networkStatisticsClientClient;

        _characterModule = new CharacterModuleClient(_gameWorld, resourceSystem);
        _projectileModule = new ProjectileModuleClient(_gameWorld, resourceSystem);
        _effectModule = new EffectModuleClient(_gameWorld, resourceSystem);
        _replicatedEntityModule = new ReplicatedEntityModuleClient(_gameWorld, resourceSystem);

        _hitCollisionModule = new HitCollisionModule(_gameWorld, 1, 1);

        _itemModule = new ItemModule(_gameWorld);
        _playerModule = new PlayerModuleClient(_gameWorld);
        _spectatorCamModule = new SpectatorCamModuleClient(_gameWorld);
        _ragDollSystem = new RagdollModule(_gameWorld);

        _gameModeSystem = _gameWorld.GetECSWorld().CreateManager<GameModeSystemClient>(_gameWorld);
        _clientFrontendUpdate = _gameWorld.GetECSWorld().CreateManager<ClientFrontendUpdate>(_gameWorld);
        _destructiblePropSystem =
            _gameWorld.GetECSWorld().CreateManager<DestructiblePropSystemClient>(_gameWorld);
        _applyGrenadePresentation = _gameWorld.GetECSWorld().CreateManager<ApplyGrenadePresentation>(_gameWorld);
        _updatePresentationOwners = _gameWorld.GetECSWorld()
            .CreateManager<UpdatePresentationOwners>(_gameWorld, resourceSystem);
        _handlePresentationOwnerDeSpawn =
            _gameWorld.GetECSWorld().CreateManager<HandlePresentationOwnerDesawn>(_gameWorld);
        _moverUpdate = _gameWorld.GetECSWorld().CreateManager<MoverUpdate>(_gameWorld);
        _teleporterSystem = _gameWorld.GetECSWorld().CreateManager<TeleporterSystemClient>(_gameWorld);
        _spinSystem = _gameWorld.GetECSWorld().CreateManager<SpinSystem>(_gameWorld);
        _handleNamePlateOwnerSpawn = _gameWorld.GetECSWorld().CreateManager<HandleNamePlateSpawn>(_gameWorld);
        _handleNamePlateOwnerDeSpawn = _gameWorld.GetECSWorld().CreateManager<HandleNamePlateDespawn>(_gameWorld);
        _updateNamePlates = _gameWorld.GetECSWorld().CreateManager<UpdateNamePlates>(_gameWorld);

        _gameModeSystem.SetLocalPlayerId(_networkClient.clientId);

        _twistSystem = new TwistSystem(_gameWorld);
        _fanSystem = new FanSystem(_gameWorld);
        _translateScaleSystem = new TranslateScaleSystem(_gameWorld);
    }

    public void Shutdown()
    {
        _characterModule.Shutdown();
        _projectileModule.Shutdown();
        _hitCollisionModule.Shutdown();
        _playerModule.Shutdown();
        _spectatorCamModule.Shutdown();
        _effectModule.Shutdown();
        _replicatedEntityModule.Shutdown();
        _itemModule.Shutdown();
        _ragDollSystem.Shutdown();
        _twistSystem.ShutDown();
        _fanSystem.ShutDown();
        _translateScaleSystem.ShutDown();

        _gameWorld.GetECSWorld().DestroyManager(_gameModeSystem);
        _gameWorld.GetECSWorld().DestroyManager(_destructiblePropSystem);
        _gameWorld.GetECSWorld().DestroyManager(_applyGrenadePresentation);
        _gameWorld.GetECSWorld().DestroyManager(_updatePresentationOwners);
        _gameWorld.GetECSWorld().DestroyManager(_handlePresentationOwnerDeSpawn);
        _gameWorld.GetECSWorld().DestroyManager(_moverUpdate);
        _gameWorld.GetECSWorld().DestroyManager(_teleporterSystem);
        _gameWorld.GetECSWorld().DestroyManager(_spinSystem);
        _gameWorld.GetECSWorld().DestroyManager(_handleNamePlateOwnerSpawn);
        _gameWorld.GetECSWorld().DestroyManager(_handleNamePlateOwnerDeSpawn);
        _gameWorld.GetECSWorld().DestroyManager(_updateNamePlates);
    }

    // This is called at the actual client frame rate, so may be faster or slower than tick rate.
    public void Update(float frameDuration)
    {
        // Advances time and accumulate input into the UserCommand being generated
        HandleTime(frameDuration);

        _gameWorld.WorldTime = _renderTime;
        _gameWorld.frameDuration = frameDuration;
        _gameWorld.LastServerTick = _networkClient.serverTime;

        _playerModule.ResolveReferenceFromLocalPlayerToPlayer();
        _playerModule.HandleCommandReset();
        _replicatedEntityModule.UpdateControlledEntityFlags();

        // Handle spawn requests
        _projectileModule.HandleProjectileRequests();
        _updatePresentationOwners.Update();

        // Handle spawning  
        _characterModule.HandleSpawns();
        _projectileModule.HandleProjectileSpawn();
        _hitCollisionModule.HandleSpawning();
        _handleNamePlateOwnerSpawn.Update();
        _ragDollSystem.HandleSpawning();
        _twistSystem.HandleSpawning();
        _fanSystem.HandleSpawning();
        _translateScaleSystem.HandleSpawning();
        _playerModule.HandleSpawn();
        _itemModule.HandleSpawn();

        // Handle controlled entity changed
        _playerModule.HandleControlledEntityChanged();
        _characterModule.HandleControlledEntityChanged();

        // Update movement of scene objects. Projectiles and grenades can also start update as they use collision data from last frame
        _spinSystem.Update();
        _moverUpdate.Update();
        _characterModule.Interpolate();
        _replicatedEntityModule.Interpolate(_renderTime);

        // Prediction
        _gameWorld.WorldTime = _predictedTime;
        _projectileModule.StartPredictedMovement();

        if (IsPredictionAllowed())
        {
            // ROLLBACK. All predicted entities (with the ServerEntity component) are rolled back to last server state 
            _gameWorld.WorldTime.SetTime(_networkClient.serverTime, _predictedTime.tickInterval);
            PredictionRollback();

            // PREDICT PREVIOUS TICKS. Replay every tick *after* the last tick we have from server up to the last stored command we have
            for (var tick = _networkClient.serverTime + 1; tick < _predictedTime.Tick; tick++)
            {
                _gameWorld.WorldTime.SetTime(tick, _predictedTime.tickInterval);
                _playerModule.RetrieveCommand(_gameWorld.WorldTime.Tick);
                PredictionUpdate();
#if UNITY_EDITOR
                // We only want to store "full" tick to we use m_PredictedTime.tick-1 (as current can be fraction of tick)
                _replicatedEntityModule.StorePredictedState(tick, _predictedTime.Tick - 1);
#endif
            }

            // PREDICT CURRENT TICK. Update current tick using duration of current tick
            _gameWorld.WorldTime = _predictedTime;
            _playerModule.RetrieveCommand(_gameWorld.WorldTime.Tick);

            // Do not update systems with close to zero time. 
            if (_gameWorld.WorldTime.TickDuration > 0.008f)
            {
                PredictionUpdate();
            }
        }

        _projectileModule.FinalizePredictedMovement();

        _gameModeSystem.Update();

        // Update Presentation
        _gameWorld.WorldTime = _predictedTime;
        _characterModule.UpdatePresentation();
        _destructiblePropSystem.Update();
        _teleporterSystem.Update();

        _gameWorld.WorldTime = _renderTime;
        // Handle deSpawns
        _handlePresentationOwnerDeSpawn.Update();
        _handleNamePlateOwnerDeSpawn.Update();
        // TODO (mogensh) this destroys presentations and needs to be done first so its picked up. We need better way of handling destruction ordering
        _characterModule.HandleDepawns();
        _projectileModule.HandleProjectileDespawn();
        _twistSystem.HandleDespawning();
        _fanSystem.HandleDespawning();
        _ragDollSystem.HandleDespawning();
        _hitCollisionModule.HandleDespawn();
        _translateScaleSystem.HandleDepawning();
        _gameWorld.ProcessDespawns();

#if UNITY_EDITOR
        if (_gameWorld.GetEntityManager().Exists(_localPlayer.controlledEntity) &&
            _gameWorld.GetEntityManager().HasComponent<UserCommandComponentData>(_localPlayer.controlledEntity))
        {
            var userCommand = _gameWorld.GetEntityManager()
                .GetComponentData<UserCommandComponentData>(_localPlayer.controlledEntity);
            _replicatedEntityModule.FinalizedStateHistory(_predictedTime.Tick - 1, _networkClient.serverTime,
                ref userCommand.command);
        }
#endif
    }

    public void LateUpdate(ChatSystemClient chatSystem, float frameDuration)
    {
        _gameWorld.WorldTime = _renderTime;
        _hitCollisionModule.StoreColliderState();

        _ragDollSystem.LateUpdate();

        _translateScaleSystem.Schedule();
        var twistSystemHandle = _twistSystem.Schedule();
        _fanSystem.Schedule(twistSystemHandle);

        var teamId = -1;
        bool showScorePanel = false;
        if (_localPlayer != null && _localPlayer.playerState != null &&
            _localPlayer.playerState.controlledEntity != Entity.Null)
        {
            teamId = _localPlayer.playerState.teamIndex;

            if (_gameWorld.GetEntityManager()
                .HasComponent<HealthStateData>(_localPlayer.playerState.controlledEntity))
            {
                var healthState = _gameWorld.GetEntityManager()
                    .GetComponentData<HealthStateData>(_localPlayer.playerState.controlledEntity);

                // Only show score board when alive
                showScorePanel = healthState.health <= 0;
            }
        }

        // TODO (petera) fix this hack
        chatSystem.UpdateLocalTeamIndex(teamId);

        _itemModule.LateUpdate();

        _characterModule.CameraUpdate();
        _playerModule.CameraUpdate();

        _characterModule.LateUpdate();

        _gameWorld.WorldTime = _renderTime;
        _projectileModule.UpdateClientProjectilesNonPredicted();

        _gameWorld.WorldTime = _predictedTime;
        _projectileModule.UpdateClientProjectilesPredicted();

        _applyGrenadePresentation.Update();

        _effectModule.ClientUpdate();

        _updateNamePlates.Update();

        if (Game.game.clientFrontend != null)
        {
            _clientFrontendUpdate.Update();
            Game.game.clientFrontend.SetShowScorePanel(showScorePanel);
        }

        _translateScaleSystem.Complete();
        _fanSystem.Complete();
    }

    public LocalPlayer RegisterLocalPlayer(int playerId)
    {
        _replicatedEntityModule.SetLocalPlayerId(playerId);
        _localPlayer = _playerModule.RegisterLocalPlayer(playerId, _networkClient);
        return _localPlayer;
    }

    public ISnapshotConsumer GetSnapshotConsumer()
    {
        return _replicatedEntityModule;
    }

    private bool IsPredictionAllowed()
    {
        if (!_playerModule.PlayerStateReady)
        {
            GameDebug.Log("No predict! No player state.");
            return false;
        }

        if (!_playerModule.IsControllingEntity)
        {
            GameDebug.Log("No predict! No controlled entity.");
            return false;
        }

        if (_predictedTime.Tick <= _networkClient.serverTime)
        {
            GameDebug.Log("No predict! Predict time not ahead of server tick! " + GetFramePredictInfo());
            return false;
        }

        if (!_playerModule.HasCommands(_networkClient.serverTime + 1, _predictedTime.Tick))
        {
            GameDebug.Log("No predict! No commands available. " + GetFramePredictInfo());
            return false;
        }

        return true;
    }

    private string GetFramePredictInfo()
    {
        int firstCommandTick;
        int lastCommandTick;
        _playerModule.GetBufferedCommandsTick(out firstCommandTick, out lastCommandTick);

        return string.Format("Last server:{0} predicted:{1} buffer:{2}->{3} time since snap:{4}  rtt avr:{5}",
            _networkClient.serverTime, _predictedTime.Tick,
            firstCommandTick, lastCommandTick,
            _networkClient.timeSinceSnapshot, _networkStatisticsClient.rtt.average);
    }

    private void PredictionRollback()
    {
        _replicatedEntityModule.Rollback();
    }

    private void PredictionUpdate()
    {
        _spectatorCamModule.Update();

        _characterModule.AbilityRequestUpdate();

        _characterModule.MovementStart();
        _characterModule.MovementResolve();

        _characterModule.AbilityStart();
        _characterModule.AbilityResolve();
    }

    private void HandleTime(float frameDuration)
    {
        // Update tick rate (this will only change runtime in test scenarios)
        // TODO (petera) consider use ConfigVars with Server flag for this
        if (_networkClient.serverTickRate != _predictedTime.tickRate)
        {
            _predictedTime.tickRate = _networkClient.serverTickRate;
            _renderTime.tickRate = _networkClient.serverTickRate;
        }

        // Sample input into current command
        // The time passed in here is used to calculate the amount of rotation from stick position
        // The command stores final view direction
        bool chatOpen = Game.game.clientFrontend != null && Game.game.clientFrontend.chatPanel.isOpen;
        bool userInputEnabled = Game.GetMousePointerLock() && !chatOpen;
        _playerModule.SampleInput(userInputEnabled, Time.deltaTime, _renderTime.Tick);

        int prevTick = _predictedTime.Tick;

        // Increment time
        var deltaPredictedTime = frameDuration * FrameTimeScale;
        _predictedTime.AddDuration(deltaPredictedTime);

        // Adjust time to be synchronized with server
        int preferredBufferedCommandCount = 2;
        int preferredTick = _networkClient.serverTime +
                            (int) (((_networkClient.timeSinceSnapshot + _networkStatisticsClient.rtt.average) /
                                    1000.0f) *
                                   _gameWorld.WorldTime.tickRate) + preferredBufferedCommandCount;

        bool resetTime = false;
        if (_predictedTime.Tick < preferredTick - 3)
        {
            GameDebug.Log("Client hard catchup ... ");
            resetTime = true;
        }

        if (!resetTime && _predictedTime.Tick > preferredTick + 6)
        {
            GameDebug.Log("Client hard slowdown ... ");
            resetTime = true;
        }

        FrameTimeScale = 1.0f;
        if (resetTime)
        {
            GameDebug.Log(string.Format("CATCHUP ({0} -> {1})", _predictedTime.Tick, preferredTick));

            _networkStatisticsClient.notifyHardCatchup = true;
            _gameWorld.NextTickTime = Game.FrameTime;
            _predictedTime.Tick = preferredTick;
            _predictedTime.SetTime(preferredTick, 0);
        }
        else
        {
            int bufferedCommands = _networkClient.lastAcknowlegdedCommandTime - _networkClient.serverTime;
            if (bufferedCommands < preferredBufferedCommandCount)
            {
                FrameTimeScale = 1.01f;
            }

            if (bufferedCommands > preferredBufferedCommandCount)
            {
                FrameTimeScale = 0.99f;
            }
        }

        // Increment interpolation time
        _renderTime.AddDuration(frameDuration * FrameTimeScale);

        // Force interp time to not exceed server time
        if (_renderTime.Tick >= _networkClient.serverTime)
        {
            _renderTime.SetTime(_networkClient.serverTime, 0);
        }

        // hard catchup
        if (_renderTime.Tick < _networkClient.serverTime - 10)
        {
            _renderTime.SetTime(_networkClient.serverTime - 8, 0);
        }

        // Throttle up to catch up
        if (_renderTime.Tick < _networkClient.serverTime - 1)
        {
            _renderTime.AddDuration(frameDuration * 0.01f);
        }

        // If predicted time has entered a new tick the stored commands should be sent to server 
        if (_predictedTime.Tick > prevTick)
        {
            var oldestCommandToSend = Mathf.Max(prevTick, _predictedTime.Tick - NetworkConfig.CommandClientBufferSize);
            for (int tick = oldestCommandToSend; tick < _predictedTime.Tick; tick++)
            {
                _playerModule.StoreCommand(tick);
                _playerModule.SendCommand(tick);
            }

            _playerModule.ResetInput(userInputEnabled);
            _playerModule.StoreCommand(_predictedTime.Tick);
        }

        // Store command
        _playerModule.StoreCommand(_predictedTime.Tick);
    }
}

public class ClientGameLoop : IGameLoop, INetworkClientCallbacks
{
    private enum ClientState
    {
        Browsing,
        Connecting,
        Loading,
        Playing,
    }

    [ConfigVar(Name = "client.updaterate", DefaultValue = "30000",
        Description = "Max bytes/sec client wants to receive", Flags = ConfigVar.Flags.ClientInfo)]
    public static ConfigVar ClientUpdateRate;

    [ConfigVar(Name = "client.updateinterval", DefaultValue = "3",
        Description = "Snapshot send rate requested by client", Flags = ConfigVar.Flags.ClientInfo)]
    public static ConfigVar ClientUpdateInterval;

    [ConfigVar(Name = "client.playername", DefaultValue = "Noname", Description = "Name of player",
        Flags = ConfigVar.Flags.ClientInfo | ConfigVar.Flags.Save)]
    public static ConfigVar ClientPlayerName;

    [ConfigVar(Name = "client.matchmaker", DefaultValue = "0.0.0.0:80", Description = "Address of matchmaker",
        Flags = ConfigVar.Flags.None)]
    public static ConfigVar ClientMatchmaker;

    [ConfigVar(Name = "client.showtickinfo", DefaultValue = "0", Description = "Show tick info")]
    public static ConfigVar ShowTickInfo;

    [ConfigVar(Name = "client.showcommandinfo", DefaultValue = "0", Description = "Show command info")]
    public static ConfigVar ShowCommandInfo;

    private string _levelName;
    private string _disconnectReason;
    private string _gameMessage = "Welcome to the sample game!";
    private string _targetServer = "";

    private int _connectRetryCount;
    private bool _predictionEnabled = true;
    private bool _performGameWorldLateUpdate;
    private bool _playerSettingsUpdated;
    private bool _useMatchmaking;
    private double _lastFrameTime;

    private ClientState _clientState;
    private GameWorld _gameWorld;
    private SocketTransport _networkTransport;
    private NetworkClient _networkClient;
    private LocalPlayer _localPlayer;

    private Matchmaker _matchmaker;
    private NetworkStatisticsClient _networkStatistics;
    private ChatSystemClient _chatSystem;

    private ClientGameWorld _clientWorld;
    private BundledResourceManager _resourceSystem;
    private StateMachine<ClientState> _stateMachine;

    private readonly PlayerSettings _requestedPlayerSettings = new PlayerSettings();

    public bool Init(string[] args)
    {
        _stateMachine = new StateMachine<ClientState>();
        _stateMachine.Add(ClientState.Browsing, EnterBrowsingState, UpdateBrowsingState, LeaveBrowsingState);
        _stateMachine.Add(ClientState.Connecting, EnterConnectingState, UpdateConnectingState, null);
        _stateMachine.Add(ClientState.Loading, EnterLoadingState, UpdateLoadingState, null);
        _stateMachine.Add(ClientState.Playing, EnterPlayingState, UpdatePlayingState, LeavePlayingState);

#if UNITY_EDITOR
        Game.game.LevelManager.UnloadLevel();
#endif
        _gameWorld = new GameWorld("ClientWorld");

        _networkTransport = new SocketTransport();
        _networkClient = new NetworkClient(_networkTransport);

        if (Application.isEditor || Game.game.buildId == "AutoBuild")
        {
            NetworkClient.ClientVerifyProtocol.Value = "0";
        }

        _networkClient.UpdateClientConfig();
        _networkStatistics = new NetworkStatisticsClient(_networkClient);
        _chatSystem = new ChatSystemClient(_networkClient);

        GameDebug.Log("Network client initialized");

        _requestedPlayerSettings.PlayerName = ClientPlayerName.Value;
        _requestedPlayerSettings.TeamId = -1;

        Console.AddCommand("disconnect", CmdDisconnect, "Disconnect from server if connected", GetHashCode());
        Console.AddCommand("prediction", CmdTogglePrediction, "Toggle prediction", GetHashCode());
        Console.AddCommand("runatserver", CmdRunAtServer, "Run command at server", GetHashCode());
        Console.AddCommand("respawn", CmdRespawn, "Force a respawn", GetHashCode());
        Console.AddCommand("nextchar", CmdNextChar, "Select next character", GetHashCode());
        Console.AddCommand("nextteam", CmdNextTeam, "Select next character", GetHashCode());
        Console.AddCommand("spectator", CmdSpectator, "Select spectator cam", GetHashCode());
        Console.AddCommand("matchmake", CmdMatchMake,
            "Match make <hostname[:port]/{projectId}>: Find and join a server", GetHashCode());

        if (args.Length > 0)
        {
            _targetServer = args[0];
            _stateMachine.SwitchTo(ClientState.Connecting);
        }
        else
        {
            _stateMachine.SwitchTo(ClientState.Browsing);
        }

        GameDebug.Log("Client initialized");

        return true;
    }

    public void Shutdown()
    {
        GameDebug.Log("ClientGameLoop shutdown");
        Console.RemoveCommandsWithTag(GetHashCode());

        _stateMachine.Shutdown();
        _networkClient.Shutdown();
        _networkTransport.Shutdown();
        _gameWorld.Shutdown();
    }

    public ClientGameWorld GetClientGameWorld()
    {
        return _clientWorld;
    }

    public void OnConnect(int clientId)
    {
    }

    public void OnDisconnect(int clientId)
    {
    }

    public unsafe void OnEvent(int clientId, NetworkEvent info)
    {
        Profiler.BeginSample("-ProcessEvent");

        var typeId = (GameNetworkEvents.EventType) info.Type.TypeId;
        switch (typeId)
        {
            case GameNetworkEvents.EventType.Chat:
                fixed (uint* data = info.Data)
                {
                    var reader = new NetworkReader(data, info.Type.Schema);
                    _chatSystem.ReceiveMessage(reader.ReadString(256));
                }

                break;
        }

        Profiler.EndSample();
    }

    public void OnMapUpdate(ref NetworkReader data)
    {
        _levelName = data.ReadString();
        if (_stateMachine.CurrentState() != ClientState.Loading)
        {
            _stateMachine.SwitchTo(ClientState.Loading);
        }
    }

    public void Update()
    {
        Profiler.BeginSample("ClientGameLoop.Update");

        Profiler.BeginSample("-NetworkClientUpdate");
        _networkClient.Update(this, _clientWorld?.GetSnapshotConsumer());
        Profiler.EndSample();

        Profiler.BeginSample("-StateMachine update");
        _stateMachine.Update();
        Profiler.EndSample();

        // TODO (petera) change if we have a lobby like setup one day
        if (_stateMachine.CurrentState() == ClientState.Playing && Game.game.clientFrontend != null)
        {
            Game.game.clientFrontend.UpdateChat(_chatSystem);
        }

        _networkClient.SendData();

        // TODO (petera) merge with clientinfo 
        if (_requestedPlayerSettings.PlayerName != ClientPlayerName.Value)
        {
            // Cap name length
            ClientPlayerName.Value = ClientPlayerName.Value.Substring(0, Mathf.Min(ClientPlayerName.Value.Length, 16));
            _requestedPlayerSettings.PlayerName = ClientPlayerName.Value;
            _playerSettingsUpdated = true;
        }

        if (_networkClient.isConnected && _playerSettingsUpdated)
        {
            _playerSettingsUpdated = false;
            SendPlayerSettings();
        }

        if (_clientWorld != null)
        {
            _networkStatistics.Update(_clientWorld.FrameTimeScale,
                GameTime.GetDuration(_clientWorld.renderTime, _clientWorld.predictedTime));
        }

        Profiler.EndSample();
    }

    private void EnterBrowsingState()
    {
        GameDebug.Assert(_clientWorld == null);
        _clientState = ClientState.Browsing;
    }

    private void UpdateBrowsingState()
    {
        if (_useMatchmaking)
        {
            _matchmaker?.Update();
        }
    }

    private void LeaveBrowsingState()
    {
    }

    private void EnterConnectingState()
    {
        GameDebug.Assert(_clientState == ClientState.Browsing, "Expected ClientState to be browsing");
        GameDebug.Assert(_clientWorld == null, "Expected ClientWorld to be null");
        GameDebug.Assert(_networkClient.connectionState == ConnectionState.Disconnected,
            "Expected network connectionState to be disconnected");

        _clientState = ClientState.Connecting;
        _connectRetryCount = 0;
    }

    private void UpdateConnectingState()
    {
        switch (_networkClient.connectionState)
        {
            case ConnectionState.Connected:
                _gameMessage = "Waiting for map info";
                break;
            case ConnectionState.Connecting:
                // Do nothing; just wait for either success or failure
                break;
            case ConnectionState.Disconnected:
                if (_connectRetryCount < 2)
                {
                    _connectRetryCount++;
                    _gameMessage = $"Trying to connect to {_targetServer} (attempt #{_connectRetryCount})...";
                    GameDebug.Log(_gameMessage);
                    _networkClient.Connect(_targetServer);
                }
                else
                {
                    _gameMessage = "Failed to connect to server";
                    GameDebug.Log(_gameMessage);
                    _networkClient.Disconnect();
                    _stateMachine.SwitchTo(ClientState.Browsing);
                }

                break;
        }
    }

    private void EnterLoadingState()
    {
        if (Game.game.clientFrontend != null)
        {
            Game.game.clientFrontend.ShowMenu(ClientFrontend.MenuShowing.None);
        }

        Console.SetOpen(false);

        GameDebug.Assert(_clientWorld == null);
        GameDebug.Assert(_networkClient.isConnected);

        _requestedPlayerSettings.PlayerName = ClientPlayerName.Value;
        _requestedPlayerSettings.CharacterType = (short) Game.CharacterType.IntValue;
        _playerSettingsUpdated = true;
        _clientState = ClientState.Loading;
    }

    private void UpdateLoadingState()
    {
        // Handle disconnects
        if (!_networkClient.isConnected)
        {
            _gameMessage = _disconnectReason != null
                ? $"Disconnected from server ({_disconnectReason})"
                : "Disconnected from server (lost connection)";
            _disconnectReason = null;
            _stateMachine.SwitchTo(ClientState.Browsing);
        }

        // Wait until we got level info
        if (_levelName == null)
        {
            return;
        }

        // Load if we are not already loading
        var level = Game.game.LevelManager.currentLevel;
        if (level == null || level.Name != _levelName)
        {
            if (!Game.game.LevelManager.LoadLevel(_levelName))
            {
                _disconnectReason = $"could not load requested level '{_levelName}'";
                _networkClient.Disconnect();
                return;
            }

            level = Game.game.LevelManager.currentLevel;
        }

        // Wait for level to be loaded
        if (level.State == LevelState.Loaded)
        {
            _stateMachine.SwitchTo(ClientState.Playing);
        }
    }

    private void EnterPlayingState()
    {
        GameDebug.Assert(_clientWorld == null && Game.game.LevelManager.IsCurrentLevelLoaded());

        _gameWorld.RegisterSceneEntities();

        _resourceSystem = new BundledResourceManager(_gameWorld, "BundledResources/Client");

        _clientWorld = new ClientGameWorld(_gameWorld, _networkClient, _networkStatistics, _resourceSystem);
        _clientWorld.PredictionEnabled = _predictionEnabled;

        _localPlayer = _clientWorld.RegisterLocalPlayer(_networkClient.clientId);

        _networkClient.QueueEvent((ushort) GameNetworkEvents.EventType.PlayerReady, true,
            (ref NetworkWriter data) => { });

        _clientState = ClientState.Playing;
    }

    private void UpdatePlayingState()
    {
        // Handle disconnects
        if (!_networkClient.isConnected)
        {
            _gameMessage = _disconnectReason != null
                ? $"Disconnected from server ({_disconnectReason})"
                : "Disconnected from server (lost connection)";
            _stateMachine.SwitchTo(ClientState.Browsing);
            return;
        }

        // (re)send client info if any of the configvars that contain clientInfo has changed
        if ((ConfigVar.DirtyFlags & ConfigVar.Flags.ClientInfo) == ConfigVar.Flags.ClientInfo)
        {
            _networkClient.UpdateClientConfig();
            ConfigVar.DirtyFlags &= ~ConfigVar.Flags.ClientInfo;
        }

        if (Game.Input.GetKeyUp(KeyCode.H))
        {
            RemoteConsoleCommand("nextchar");
        }

        if (Game.Input.GetKeyUp(KeyCode.T))
        {
            CmdNextTeam(null);
        }

        float frameDuration = _lastFrameTime != 0 ? (float) (Game.FrameTime - _lastFrameTime) : 0;
        _lastFrameTime = Game.FrameTime;

        _clientWorld.Update(frameDuration);
        _performGameWorldLateUpdate = true;
    }

    private void LeavePlayingState()
    {
        _resourceSystem.Shutdown();

        _localPlayer = null;

        _clientWorld.Shutdown();
        _clientWorld = null;

        _resourceSystem.Shutdown();

        _gameWorld.Shutdown();
        _gameWorld = new GameWorld("ClientWorld");

        if (Game.game.clientFrontend != null)
        {
            Game.game.clientFrontend.Clear();
            Game.game.clientFrontend.ShowMenu(ClientFrontend.MenuShowing.None);
        }

        Game.game.LevelManager.LoadLevel("level_menu");

        GameDebug.Log("Left playing state");
    }

    public void FixedUpdate()
    {
    }

    public void LateUpdate()
    {
        if (_clientWorld != null && _performGameWorldLateUpdate)
        {
            _performGameWorldLateUpdate = false;
            _clientWorld.LateUpdate(_chatSystem, Time.deltaTime);
        }

        ShowInfoOverlay(0, 1);
    }

    private void RemoteConsoleCommand(string command)
    {
        _networkClient.QueueEvent((ushort) GameNetworkEvents.EventType.RemoteConsoleCmd, true,
            (ref NetworkWriter writer) => { writer.WriteString("args", command); });
    }

    public void CmdConnect(string[] args)
    {
        if (_stateMachine.CurrentState() == ClientState.Browsing)
        {
            _targetServer = args.Length > 0 ? args[0] : "127.0.0.1";
            _stateMachine.SwitchTo(ClientState.Connecting);
        }
        else if (_stateMachine.CurrentState() == ClientState.Connecting)
        {
            _networkClient.Disconnect();
            _targetServer = args.Length > 0 ? args[0] : "127.0.0.1";
            _connectRetryCount = 0;
        }
        else
        {
            GameDebug.Log("Unable to connect from this state: " + _stateMachine.CurrentState());
        }
    }

    private void CmdDisconnect(string[] args)
    {
        _disconnectReason = "user manually disconnected";
        _networkClient.Disconnect();
        _stateMachine.SwitchTo(ClientState.Browsing);
    }

    private void CmdTogglePrediction(string[] args)
    {
        _predictionEnabled = !_predictionEnabled;
        Console.Write("Prediction:" + _predictionEnabled);

        if (_clientWorld != null)
        {
            _clientWorld.PredictionEnabled = _predictionEnabled;
        }
    }

    private void CmdRunAtServer(string[] args)
    {
        RemoteConsoleCommand(string.Join(" ", args));
    }

    private void CmdRespawn(string[] args)
    {
        if (_localPlayer == null || _localPlayer.playerState == null ||
            _localPlayer.playerState.controlledEntity == Entity.Null)
        {
            return;
        }

        // Request new char type
        if (args.Length == 1)
        {
            _requestedPlayerSettings.CharacterType = short.Parse(args[0]);
            _playerSettingsUpdated = true;
        }

        // Tell server who to respawn
        RemoteConsoleCommand(string.Format("respawn {0}", _localPlayer.playerState.playerId));
    }

    private void CmdNextChar(string[] args)
    {
        if (_localPlayer == null || _localPlayer.playerState == null ||
            _localPlayer.playerState.controlledEntity == Entity.Null)
        {
            return;
        }

        if (Game.AllowCharChange.IntValue != 1)
        {
            return;
        }

        if (!_gameWorld.GetEntityManager()
            .HasComponent<Character>(_localPlayer.playerState.controlledEntity))
        {
            return;
        }

        var charSetupRegistry = _resourceSystem.GetResourceRegistry<HeroTypeRegistry>();
        var charSetupCount = charSetupRegistry.entries.Count;

        _requestedPlayerSettings.CharacterType += 1;
        if (_requestedPlayerSettings.CharacterType >= charSetupCount)
        {
            _requestedPlayerSettings.CharacterType = 0;
        }

        _playerSettingsUpdated = true;
    }

    private void CmdSpectator(string[] args)
    {
        if (_localPlayer == null || _localPlayer.playerState == null ||
            _localPlayer.playerState.controlledEntity == Entity.Null)
        {
            return;
        }

        if (Game.AllowCharChange.IntValue != 1)
        {
            return;
        }

        var isControllingSpectatorCam = _gameWorld.GetEntityManager()
            .HasComponent<SpectatorCamData>(_localPlayer.playerState.controlledEntity);

        // TODO find better way to identity spectator cam
        _requestedPlayerSettings.CharacterType = isControllingSpectatorCam ? 0 : 1000;
        _playerSettingsUpdated = true;
    }

    private void CmdNextTeam(string[] args)
    {
        if (_localPlayer == null || _localPlayer.playerState == null)
        {
            return;
        }

        if (Game.AllowCharChange.IntValue != 1)
        {
            return;
        }

        _requestedPlayerSettings.TeamId = (short) (_localPlayer.playerState.teamIndex + 1);
        if (_requestedPlayerSettings.TeamId > 1)
            _requestedPlayerSettings.TeamId = 0;
        _playerSettingsUpdated = true;
    }

    /// <summary>
    /// Start matchmaking by issuing a request to the provided endpoint. Use client.matchmaker value 
    /// as endpoint if none given.
    /// </summary>
    private void CmdMatchMake(string[] args)
    {
        if (_matchmaker != null)
        {
            GameDebug.Log("matchmake: Already in a matchmaking session. Wait for completion before matchmaking again.");
            return;
        }

        string endpoint = ClientMatchmaker.Value;
        if (args.Length > 0)
        {
            endpoint = args[0];
        }

        if (string.IsNullOrEmpty(endpoint))
        {
            GameDebug.LogError("matchmake: command requires an endpoint <ex: cloud.connected.unity3d.com/{projectid}>");
            return;
        }

        if (string.IsNullOrEmpty(ClientPlayerName.Value))
        {
            GameDebug.LogError("matchmake: Player name must be set before matchmaking can be started");
            return;
        }

        if (_stateMachine.CurrentState() != ClientState.Browsing)
        {
            GameDebug.LogError("matchmake: matchmaking can only be started in Browsing state.  Current state is " +
                               _stateMachine.CurrentState().ToString());
            return;
        }

        GameDebug.Log(
            $"matchmake: Starting the matchmaker. Requesting match from {endpoint} for request ID {ClientPlayerName.Value}.");
        _useMatchmaking = true;
        _matchmaker = new Matchmaker(endpoint, OnMatchmakingSuccess, OnMatchmakingError);

        MatchmakingPlayerProperties playerProps = new MatchmakingPlayerProperties() {hats = 5};
        MatchmakingGroupProperties groupProps = new MatchmakingGroupProperties() {mode = 0};
        _matchmaker.RequestMatch(ClientPlayerName.Value, playerProps, groupProps);
    }

    private void OnMatchmakingSuccess(Assignment assignment)
    {
        if (string.IsNullOrEmpty(assignment.ConnectionString))
        {
            GameDebug.Log(
                "Matchmaking finished, but did not return a game server.  Ensure your server has been allocated and is running then try again.");
            GameDebug.Log($"MM Error: {assignment.AssignmentError ?? "None"}");
        }
        else
        {
            GameDebug.Log(
                $"Matchmaking has found a game! The server is at {assignment.ConnectionString}.  Attempting to connect...");
            Console.EnqueueCommand($"connect {assignment.ConnectionString}");
        }

        _useMatchmaking = false;
        _matchmaker = null;
    }

    private void OnMatchmakingError(string errorInfo)
    {
        GameDebug.LogError($"Matchmaking failed! Error is: {errorInfo}");
        _useMatchmaking = false;
        _matchmaker = null;
    }

    private void ShowInfoOverlay(float x, float y)
    {
        if (ShowTickInfo.IntValue == 1)
        {
            DebugOverlay.Write(x, y++, "Tick:{0} Last server:{1} Predicted:{2}", _clientWorld.predictedTime.Tick,
                _networkClient.serverTime, _clientWorld.predictedTime.Tick - _networkClient.serverTime - 1);
        }

        if (ShowCommandInfo.IntValue == 1)
        {
            UserCommand command = UserCommand.defaultCommand;
            bool valid = _localPlayer.commandBuffer.TryGetValue(_clientWorld.predictedTime.Tick + 1, ref command);
            if (valid)
            {
                DebugOverlay.Write(x, y++, "Next cmd: PrimaryFire:{0}",
                    command.buttons.IsSet(UserCommand.Button.PrimaryFire));
            }

            valid = _localPlayer.commandBuffer.TryGetValue(_clientWorld.predictedTime.Tick, ref command);
            if (valid)
            {
                DebugOverlay.Write(x, y++, "Tick cmd: PrimaryFire:{0}",
                    command.buttons.IsSet(UserCommand.Button.PrimaryFire));
            }
        }
    }

    private void SendPlayerSettings()
    {
        _networkClient.QueueEvent((ushort) GameNetworkEvents.EventType.PlayerSetup, true,
            (ref NetworkWriter writer) => { _requestedPlayerSettings.Serialize(ref writer); });
    }
}