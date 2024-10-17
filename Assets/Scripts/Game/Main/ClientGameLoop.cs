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

        _gameWorld.worldTime = _renderTime;
        _gameWorld.frameDuration = frameDuration;
        _gameWorld.lastServerTick = _networkClient.serverTime;

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
        _gameWorld.worldTime = _predictedTime;
        _projectileModule.StartPredictedMovement();

        if (IsPredictionAllowed())
        {
            // ROLLBACK. All predicted entities (with the ServerEntity component) are rolled back to last server state 
            _gameWorld.worldTime.SetTime(_networkClient.serverTime, _predictedTime.tickInterval);
            PredictionRollback();

            // PREDICT PREVIOUS TICKS. Replay every tick *after* the last tick we have from server up to the last stored command we have
            for (var tick = _networkClient.serverTime + 1; tick < _predictedTime.tick; tick++)
            {
                _gameWorld.worldTime.SetTime(tick, _predictedTime.tickInterval);
                _playerModule.RetrieveCommand(_gameWorld.worldTime.tick);
                PredictionUpdate();
#if UNITY_EDITOR
                // We only want to store "full" tick to we use m_PredictedTime.tick-1 (as current can be fraction of tick)
                _replicatedEntityModule.StorePredictedState(tick, _predictedTime.tick - 1);
#endif
            }

            // PREDICT CURRENT TICK. Update current tick using duration of current tick
            _gameWorld.worldTime = _predictedTime;
            _playerModule.RetrieveCommand(_gameWorld.worldTime.tick);

            // Do not update systems with close to zero time. 
            if (_gameWorld.worldTime.tickDuration > 0.008f)
            {
                PredictionUpdate();
            }
        }

        _projectileModule.FinalizePredictedMovement();

        _gameModeSystem.Update();

        // Update Presentation
        _gameWorld.worldTime = _predictedTime;
        _characterModule.UpdatePresentation();
        _destructiblePropSystem.Update();
        _teleporterSystem.Update();

        _gameWorld.worldTime = _renderTime;
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
            _replicatedEntityModule.FinalizedStateHistory(_predictedTime.tick - 1, _networkClient.serverTime,
                ref userCommand.command);
        }
#endif
    }

    public void LateUpdate(ChatSystemClient chatSystem, float frameDuration)
    {
        _gameWorld.worldTime = _renderTime;
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

        _gameWorld.worldTime = _renderTime;
        _projectileModule.UpdateClientProjectilesNonPredicted();

        _gameWorld.worldTime = _predictedTime;
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

        if (_predictedTime.tick <= _networkClient.serverTime)
        {
            GameDebug.Log("No predict! Predict time not ahead of server tick! " + GetFramePredictInfo());
            return false;
        }

        if (!_playerModule.HasCommands(_networkClient.serverTime + 1, _predictedTime.tick))
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
            _networkClient.serverTime, _predictedTime.tick,
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
        //  The time passed in here is used to calculate the amount of rotation from stick position
        //  The command stores final view direction
        bool chatOpen = Game.game.clientFrontend != null && Game.game.clientFrontend.chatPanel.isOpen;
        bool userInputEnabled = Game.GetMousePointerLock() && !chatOpen;
        _playerModule.SampleInput(userInputEnabled, Time.deltaTime, _renderTime.tick);

        int prevTick = _predictedTime.tick;

        // Increment time
        var deltaPredictedTime = frameDuration * FrameTimeScale;
        _predictedTime.AddDuration(deltaPredictedTime);

        // Adjust time to be synchronized with server
        int preferredBufferedCommandCount = 2;
        int preferredTick = _networkClient.serverTime +
                            (int) (((_networkClient.timeSinceSnapshot + _networkStatisticsClient.rtt.average) /
                                    1000.0f) *
                                   _gameWorld.worldTime.tickRate) + preferredBufferedCommandCount;

        bool resetTime = false;
        if (_predictedTime.tick < preferredTick - 3)
        {
            GameDebug.Log("Client hard catchup ... ");
            resetTime = true;
        }

        if (!resetTime && _predictedTime.tick > preferredTick + 6)
        {
            GameDebug.Log("Client hard slowdown ... ");
            resetTime = true;
        }

        FrameTimeScale = 1.0f;
        if (resetTime)
        {
            GameDebug.Log(string.Format("CATCHUP ({0} -> {1})", _predictedTime.tick, preferredTick));

            _networkStatisticsClient.notifyHardCatchup = true;
            _gameWorld.nextTickTime = Game.frameTime;
            _predictedTime.tick = preferredTick;
            _predictedTime.SetTime(preferredTick, 0);
        }
        else
        {
            int bufferedCommands = _networkClient.lastAcknowlegdedCommandTime - _networkClient.serverTime;
            if (bufferedCommands < preferredBufferedCommandCount)
                FrameTimeScale = 1.01f;

            if (bufferedCommands > preferredBufferedCommandCount)
                FrameTimeScale = 0.99f;
        }

        // Increment interpolation time
        _renderTime.AddDuration(frameDuration * FrameTimeScale);

        // Force interp time to not exceed server time
        if (_renderTime.tick >= _networkClient.serverTime)
        {
            _renderTime.SetTime(_networkClient.serverTime, 0);
        }

        // hard catchup
        if (_renderTime.tick < _networkClient.serverTime - 10)
        {
            _renderTime.SetTime(_networkClient.serverTime - 8, 0);
        }

        // Throttle up to catch up
        if (_renderTime.tick < _networkClient.serverTime - 1)
        {
            _renderTime.AddDuration(frameDuration * 0.01f);
        }

        // If predicted time has entered a new tick the stored commands should be sent to server 
        if (_predictedTime.tick > prevTick)
        {
            var oldestCommandToSend = Mathf.Max(prevTick, _predictedTime.tick - NetworkConfig.CommandClientBufferSize);
            for (int tick = oldestCommandToSend; tick < _predictedTime.tick; tick++)
            {
                _playerModule.StoreCommand(tick);
                _playerModule.SendCommand(tick);
            }

            _playerModule.ResetInput(userInputEnabled);
            _playerModule.StoreCommand(_predictedTime.tick);
        }

        // Store command
        _playerModule.StoreCommand(_predictedTime.tick);
    }
}

public class ClientGameLoop : Game.IGameLoop, INetworkCallbacks, INetworkClientCallbacks
{
    [ConfigVar(Name = "client.updaterate", DefaultValue = "30000",
        Description = "Max bytes/sec client wants to receive", Flags = ConfigVar.Flags.ClientInfo)]
    public static ConfigVar clientUpdateRate;

    [ConfigVar(Name = "client.updateinterval", DefaultValue = "3",
        Description = "Snapshot sendrate requested by client", Flags = ConfigVar.Flags.ClientInfo)]
    public static ConfigVar clientUpdateInterval;

    [ConfigVar(Name = "client.playername", DefaultValue = "Noname", Description = "Name of player",
        Flags = ConfigVar.Flags.ClientInfo | ConfigVar.Flags.Save)]
    public static ConfigVar clientPlayerName;

    [ConfigVar(Name = "client.matchmaker", DefaultValue = "0.0.0.0:80", Description = "Address of matchmaker",
        Flags = ConfigVar.Flags.None)]
    public static ConfigVar clientMatchmaker;

    public bool Init(string[] args)
    {
        m_StateMachine = new StateMachine<ClientState>();
        m_StateMachine.Add(ClientState.Browsing, EnterBrowsingState, UpdateBrowsingState, LeaveBrowsingState);
        m_StateMachine.Add(ClientState.Connecting, EnterConnectingState, UpdateConnectingState, null);
        m_StateMachine.Add(ClientState.Loading, EnterLoadingState, UpdateLoadingState, null);
        m_StateMachine.Add(ClientState.Playing, EnterPlayingState, UpdatePlayingState, LeavePlayingState);

#if UNITY_EDITOR
        Game.game.levelManager.UnloadLevel();
#endif
        m_GameWorld = new GameWorld("ClientWorld");

        m_NetworkTransport = new SocketTransport();
        m_NetworkClient = new NetworkClient(m_NetworkTransport);

        if (Application.isEditor || Game.game.buildId == "AutoBuild")
            NetworkClient.ClientVerifyProtocol.Value = "0";

        m_NetworkClient.UpdateClientConfig();
        m_NetworkStatistics = new NetworkStatisticsClient(m_NetworkClient);
        m_ChatSystem = new ChatSystemClient(m_NetworkClient);

        GameDebug.Log("Network client initialized");

        m_requestedPlayerSettings.playerName = clientPlayerName.Value;
        m_requestedPlayerSettings.teamId = -1;

        Console.AddCommand("disconnect", CmdDisconnect, "Disconnect from server if connected", this.GetHashCode());
        Console.AddCommand("prediction", CmdTogglePrediction, "Toggle prediction", this.GetHashCode());
        Console.AddCommand("runatserver", CmdRunAtServer, "Run command at server", this.GetHashCode());
        Console.AddCommand("respawn", CmdRespawn, "Force a respawn", this.GetHashCode());
        Console.AddCommand("nextchar", CmdNextChar, "Select next character", this.GetHashCode());
        Console.AddCommand("nextteam", CmdNextTeam, "Select next character", this.GetHashCode());
        Console.AddCommand("spectator", CmdSpectator, "Select spectator cam", this.GetHashCode());
        Console.AddCommand("matchmake", CmdMatchmake, "matchmake <hostname[:port]/{projectid}>: Find and join a server",
            this.GetHashCode());

        if (args.Length > 0)
        {
            targetServer = args[0];
            m_StateMachine.SwitchTo(ClientState.Connecting);
        }
        else
            m_StateMachine.SwitchTo(ClientState.Browsing);

        GameDebug.Log("Client initialized");

        return true;
    }

    public void Shutdown()
    {
        GameDebug.Log("ClientGameLoop shutdown");
        Console.RemoveCommandsWithTag(this.GetHashCode());

        m_StateMachine.Shutdown();

        m_NetworkClient.Shutdown();
        m_NetworkTransport.Shutdown();

        m_GameWorld.Shutdown();
    }

    public ClientGameWorld GetClientGameWorld()
    {
        return m_clientWorld;
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
        switch ((GameNetworkEvents.EventType) info.Type.TypeId)
        {
            case GameNetworkEvents.EventType.Chat:
                fixed (uint* data = info.Data)
                {
                    var reader = new NetworkReader(data, info.Type.Schema);
                    m_ChatSystem.ReceiveMessage(reader.ReadString(256));
                }

                break;
        }

        Profiler.EndSample();
    }

    public void OnMapUpdate(ref NetworkReader data)
    {
        m_LevelName = data.ReadString();
        if (m_StateMachine.CurrentState() != ClientState.Loading)
            m_StateMachine.SwitchTo(ClientState.Loading);
    }

    public void Update()
    {
        Profiler.BeginSample("ClientGameLoop.Update");

        Profiler.BeginSample("-NetworkClientUpdate");
        m_NetworkClient.Update(this, m_clientWorld?.GetSnapshotConsumer());
        Profiler.EndSample();

        Profiler.BeginSample("-StateMachine update");
        m_StateMachine.Update();
        Profiler.EndSample();

        // TODO (petera) change if we have a lobby like setup one day
        if (m_StateMachine.CurrentState() == ClientState.Playing && Game.game.clientFrontend != null)
            Game.game.clientFrontend.UpdateChat(m_ChatSystem);

        m_NetworkClient.SendData();

        // TODO (petera) merge with clientinfo 
        if (m_requestedPlayerSettings.playerName != clientPlayerName.Value)
        {
            // Cap name length
            clientPlayerName.Value = clientPlayerName.Value.Substring(0, Mathf.Min(clientPlayerName.Value.Length, 16));
            m_requestedPlayerSettings.playerName = clientPlayerName.Value;
            m_playerSettingsUpdated = true;
        }

        if (m_NetworkClient.isConnected && m_playerSettingsUpdated)
        {
            m_playerSettingsUpdated = false;
            SendPlayerSettings();
        }

        if (m_clientWorld != null)
            m_NetworkStatistics.Update(m_clientWorld.FrameTimeScale,
                GameTime.GetDuration(m_clientWorld.renderTime, m_clientWorld.predictedTime));

        Profiler.EndSample();
    }

    void EnterBrowsingState()
    {
        GameDebug.Assert(m_clientWorld == null);
        m_ClientState = ClientState.Browsing;
    }

    void UpdateBrowsingState()
    {
        if (m_useMatchmaking)
        {
            m_matchmaker?.Update();
        }
    }

    void LeaveBrowsingState()
    {
    }

    string targetServer = "";
    int connectRetryCount;

    void EnterConnectingState()
    {
        GameDebug.Assert(m_ClientState == ClientState.Browsing, "Expected ClientState to be browsing");
        GameDebug.Assert(m_clientWorld == null, "Expected ClientWorld to be null");
        GameDebug.Assert(m_NetworkClient.connectionState == ConnectionState.Disconnected,
            "Expected network connectionState to be disconnected");

        m_ClientState = ClientState.Connecting;
        connectRetryCount = 0;
    }

    void UpdateConnectingState()
    {
        switch (m_NetworkClient.connectionState)
        {
            case ConnectionState.Connected:
                m_GameMessage = "Waiting for map info";
                break;
            case ConnectionState.Connecting:
                // Do nothing; just wait for either success or failure
                break;
            case ConnectionState.Disconnected:
                if (connectRetryCount < 2)
                {
                    connectRetryCount++;
                    m_GameMessage = string.Format("Trying to connect to {0} (attempt #{1})...", targetServer,
                        connectRetryCount);
                    GameDebug.Log(m_GameMessage);
                    m_NetworkClient.Connect(targetServer);
                }
                else
                {
                    m_GameMessage = "Failed to connect to server";
                    GameDebug.Log(m_GameMessage);
                    m_NetworkClient.Disconnect();
                    m_StateMachine.SwitchTo(ClientState.Browsing);
                }

                break;
        }
    }

    void EnterLoadingState()
    {
        if (Game.game.clientFrontend != null)
            Game.game.clientFrontend.ShowMenu(ClientFrontend.MenuShowing.None);

        Console.SetOpen(false);

        GameDebug.Assert(m_clientWorld == null);
        GameDebug.Assert(m_NetworkClient.isConnected);

        m_requestedPlayerSettings.playerName = clientPlayerName.Value;
        m_requestedPlayerSettings.characterType = (short) Game.characterType.IntValue;
        m_playerSettingsUpdated = true;

        m_ClientState = ClientState.Loading;
    }

    void UpdateLoadingState()
    {
        // Handle disconnects
        if (!m_NetworkClient.isConnected)
        {
            m_GameMessage = m_DisconnectReason != null
                ? string.Format("Disconnected from server ({0})", m_DisconnectReason)
                : "Disconnected from server (lost connection)";
            m_DisconnectReason = null;
            m_StateMachine.SwitchTo(ClientState.Browsing);
        }

        // Wait until we got level info
        if (m_LevelName == null)
            return;

        // Load if we are not already loading
        var level = Game.game.levelManager.currentLevel;
        if (level == null || level.name != m_LevelName)
        {
            if (!Game.game.levelManager.LoadLevel(m_LevelName))
            {
                m_DisconnectReason = string.Format("could not load requested level '{0}'", m_LevelName);
                m_NetworkClient.Disconnect();
                return;
            }

            level = Game.game.levelManager.currentLevel;
        }

        // Wait for level to be loaded
        if (level.state == LevelState.Loaded)
            m_StateMachine.SwitchTo(ClientState.Playing);
    }

    void EnterPlayingState()
    {
        GameDebug.Assert(m_clientWorld == null && Game.game.levelManager.IsCurrentLevelLoaded());

        m_GameWorld.RegisterSceneEntities();

        m_resourceSystem = new BundledResourceManager(m_GameWorld, "BundledResources/Client");

        m_clientWorld = new ClientGameWorld(m_GameWorld, m_NetworkClient, m_NetworkStatistics, m_resourceSystem);
        m_clientWorld.PredictionEnabled = m_predictionEnabled;

        m_LocalPlayer = m_clientWorld.RegisterLocalPlayer(m_NetworkClient.clientId);

        m_NetworkClient.QueueEvent((ushort) GameNetworkEvents.EventType.PlayerReady, true,
            (ref NetworkWriter data) => { });

        m_ClientState = ClientState.Playing;
    }

    void LeavePlayingState()
    {
        m_resourceSystem.Shutdown();

        m_LocalPlayer = null;

        m_clientWorld.Shutdown();
        m_clientWorld = null;

        // TODO (petera) replace this with a stack of levels or similar thing. For now we just load the menu no matter what
        //Game.game.levelManager.UnloadLevel();
        //Game.game.levelManager.LoadLevel("level_menu");

        m_resourceSystem.Shutdown();

        m_GameWorld.Shutdown();
        m_GameWorld = new GameWorld("ClientWorld");

        if (Game.game.clientFrontend != null)
        {
            Game.game.clientFrontend.Clear();
            Game.game.clientFrontend.ShowMenu(ClientFrontend.MenuShowing.None);
        }

        Game.game.levelManager.LoadLevel("level_menu");

        GameDebug.Log("Left playingstate");
    }

    void UpdatePlayingState()
    {
        // Handle disconnects
        if (!m_NetworkClient.isConnected)
        {
            m_GameMessage = m_DisconnectReason != null
                ? string.Format("Disconnected from server ({0})", m_DisconnectReason)
                : "Disconnected from server (lost connection)";
            m_StateMachine.SwitchTo(ClientState.Browsing);
            return;
        }

        // (re)send client info if any of the configvars that contain clientinfo has changed
        if ((ConfigVar.DirtyFlags & ConfigVar.Flags.ClientInfo) == ConfigVar.Flags.ClientInfo)
        {
            m_NetworkClient.UpdateClientConfig();
            ConfigVar.DirtyFlags &= ~ConfigVar.Flags.ClientInfo;
        }

        if (Game.Input.GetKeyUp(KeyCode.H))
        {
            RemoteConsoleCommand("nextchar");
        }

        if (Game.Input.GetKeyUp(KeyCode.T))
            CmdNextTeam(null);

        float frameDuration = m_lastFrameTime != 0 ? (float) (Game.frameTime - m_lastFrameTime) : 0;
        m_lastFrameTime = Game.frameTime;

        m_clientWorld.Update(frameDuration);
        m_performGameWorldLateUpdate = true;
    }

    public void FixedUpdate()
    {
    }

    public void LateUpdate()
    {
        if (m_clientWorld != null && m_performGameWorldLateUpdate)
        {
            m_performGameWorldLateUpdate = false;
            m_clientWorld.LateUpdate(m_ChatSystem, Time.deltaTime);
        }

        ShowInfoOverlay(0, 1);
    }

    public void RemoteConsoleCommand(string command)
    {
        m_NetworkClient.QueueEvent((ushort) GameNetworkEvents.EventType.RemoteConsoleCmd, true,
            (ref NetworkWriter writer) => { writer.WriteString("args", command); });
    }

    public void CmdConnect(string[] args)
    {
        if (m_StateMachine.CurrentState() == ClientState.Browsing)
        {
            targetServer = args.Length > 0 ? args[0] : "127.0.0.1";
            m_StateMachine.SwitchTo(ClientState.Connecting);
        }
        else if (m_StateMachine.CurrentState() == ClientState.Connecting)
        {
            m_NetworkClient.Disconnect();
            targetServer = args.Length > 0 ? args[0] : "127.0.0.1";
            connectRetryCount = 0;
        }
        else
        {
            GameDebug.Log("Unable to connect from this state: " + m_StateMachine.CurrentState().ToString());
        }
    }

    void CmdDisconnect(string[] args)
    {
        m_DisconnectReason = "user manually disconnected";
        m_NetworkClient.Disconnect();
        m_StateMachine.SwitchTo(ClientState.Browsing);
    }

    void CmdTogglePrediction(string[] args)
    {
        m_predictionEnabled = !m_predictionEnabled;
        Console.Write("Prediction:" + m_predictionEnabled);

        if (m_clientWorld != null)
            m_clientWorld.PredictionEnabled = m_predictionEnabled;
    }

    void CmdRunAtServer(string[] args)
    {
        RemoteConsoleCommand(string.Join(" ", args));
    }

    void CmdRespawn(string[] args)
    {
        if (m_LocalPlayer == null || m_LocalPlayer.playerState == null ||
            m_LocalPlayer.playerState.controlledEntity == Entity.Null)
            return;

        // Request new char type
        if (args.Length == 1)
        {
            m_requestedPlayerSettings.characterType = short.Parse(args[0]);
            m_playerSettingsUpdated = true;
        }

        // Tell server who to respawn
        RemoteConsoleCommand(string.Format("respawn {0}", m_LocalPlayer.playerState.playerId));
    }


    void CmdNextChar(string[] args)
    {
        if (m_LocalPlayer == null || m_LocalPlayer.playerState == null ||
            m_LocalPlayer.playerState.controlledEntity == Entity.Null)
            return;

        if (Game.allowCharChange.IntValue != 1)
            return;

        if (!m_GameWorld.GetEntityManager()
            .HasComponent<Character>(m_LocalPlayer.playerState.controlledEntity))
            return;

        var charSetupRegistry = m_resourceSystem.GetResourceRegistry<HeroTypeRegistry>();
        var charSetupCount = charSetupRegistry.entries.Count;

        m_requestedPlayerSettings.characterType = m_requestedPlayerSettings.characterType + 1;
        if (m_requestedPlayerSettings.characterType >= charSetupCount)
            m_requestedPlayerSettings.characterType = 0;
        m_playerSettingsUpdated = true;
    }

    void CmdSpectator(string[] args)
    {
        if (m_LocalPlayer == null || m_LocalPlayer.playerState == null ||
            m_LocalPlayer.playerState.controlledEntity == Entity.Null)
            return;

        if (Game.allowCharChange.IntValue != 1)
            return;

        var isControllingSpectatorCam = m_GameWorld.GetEntityManager()
            .HasComponent<SpectatorCamData>(m_LocalPlayer.playerState.controlledEntity);

        // TODO find better way to identity spectatorcam
        m_requestedPlayerSettings.characterType = isControllingSpectatorCam ? 0 : 1000;
        m_playerSettingsUpdated = true;
    }

    void CmdNextTeam(string[] args)
    {
        if (m_LocalPlayer == null || m_LocalPlayer.playerState == null)
            return;

        if (Game.allowCharChange.IntValue != 1)
            return;

        m_requestedPlayerSettings.teamId = (short) (m_LocalPlayer.playerState.teamIndex + 1);
        if (m_requestedPlayerSettings.teamId > 1)
            m_requestedPlayerSettings.teamId = 0;
        m_playerSettingsUpdated = true;
    }

    /// <summary>
    /// Start matchmaking by issuing a request to the provided endpoint. Use client.matchmaker value 
    /// as endpoint if none given.
    /// </summary>
    void CmdMatchmake(string[] args)
    {
        if (m_matchmaker != null)
        {
            GameDebug.Log("matchmake: Already in a matchmaking session. Wait for completion before matchmaking again.");
            return;
        }

        string endpoint = clientMatchmaker.Value;
        if (args.Length > 0)
            endpoint = args[0];

        if (string.IsNullOrEmpty(endpoint))
        {
            GameDebug.LogError("matchmake: command requires an endpoint <ex: cloud.connected.unity3d.com/{projectid}>");
            return;
        }

        if (string.IsNullOrEmpty(clientPlayerName.Value))
        {
            GameDebug.LogError("matchmake: Player name must be set before matchmaking can be started");
            return;
        }

        if (m_StateMachine.CurrentState() != ClientState.Browsing)
        {
            GameDebug.LogError("matchmake: matchmaking can only be started in Browsing state.  Current state is " +
                               m_StateMachine.CurrentState().ToString());
            return;
        }

        GameDebug.Log(
            $"matchmake: Starting the matchmaker. Requesting match from {endpoint} for request ID {clientPlayerName.Value}.");
        m_useMatchmaking = true;
        m_matchmaker = new Matchmaker(endpoint, OnMatchmakingSuccess, OnMatchmakingError);

        MatchmakingPlayerProperties playerProps = new MatchmakingPlayerProperties() {hats = 5};
        MatchmakingGroupProperties groupProps = new MatchmakingGroupProperties() {mode = 0};

        m_matchmaker.RequestMatch(clientPlayerName.Value, playerProps, groupProps);
    }

    void OnMatchmakingSuccess(Assignment assignment)
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

        m_useMatchmaking = false;
        m_matchmaker = null;
    }

    void OnMatchmakingError(string errorInfo)
    {
        GameDebug.LogError($"Matchmaking failed! Error is: {errorInfo}");
        m_useMatchmaking = false;
        m_matchmaker = null;
    }

    void ShowInfoOverlay(float x, float y)
    {
        if (m_showTickInfo.IntValue == 1)
            DebugOverlay.Write(x, y++, "Tick:{0} Last server:{1} Predicted:{2}", m_clientWorld.predictedTime.tick,
                m_NetworkClient.serverTime, m_clientWorld.predictedTime.tick - m_NetworkClient.serverTime - 1);

        if (m_showCommandInfo.IntValue == 1)
        {
            UserCommand command = UserCommand.defaultCommand;
            bool valid = m_LocalPlayer.commandBuffer.TryGetValue(m_clientWorld.predictedTime.tick + 1, ref command);
            if (valid)
                DebugOverlay.Write(x, y++, "Next cmd: PrimaryFire:{0}",
                    command.buttons.IsSet(UserCommand.Button.PrimaryFire));
            valid = m_LocalPlayer.commandBuffer.TryGetValue(m_clientWorld.predictedTime.tick, ref command);
            if (valid)
                DebugOverlay.Write(x, y++, "Tick cmd: PrimaryFire:{0}",
                    command.buttons.IsSet(UserCommand.Button.PrimaryFire));
        }
    }

    void SendPlayerSettings()
    {
        m_NetworkClient.QueueEvent((ushort) GameNetworkEvents.EventType.PlayerSetup, true,
            (ref NetworkWriter writer) => { m_requestedPlayerSettings.Serialize(ref writer); });
    }

    enum ClientState
    {
        Browsing,
        Connecting,
        Loading,
        Playing,
    }

    StateMachine<ClientState> m_StateMachine;

    ClientState m_ClientState;

    GameWorld m_GameWorld;

    SocketTransport m_NetworkTransport;

    NetworkClient m_NetworkClient;

    LocalPlayer m_LocalPlayer;
    PlayerSettings m_requestedPlayerSettings = new PlayerSettings();
    bool m_playerSettingsUpdated;

    NetworkStatisticsClient m_NetworkStatistics;
    ChatSystemClient m_ChatSystem;

    ClientGameWorld m_clientWorld;
    BundledResourceManager m_resourceSystem;

    string m_LevelName;

    string m_DisconnectReason = null;
    string m_GameMessage = "Welcome to the sample game!";

    double m_lastFrameTime;
    bool m_predictionEnabled = true;
    bool m_performGameWorldLateUpdate;

    bool m_useMatchmaking = false;
    Matchmaker m_matchmaker;

    [ConfigVar(Name = "client.showtickinfo", DefaultValue = "0", Description = "Show tick info")]
    static ConfigVar m_showTickInfo;

    [ConfigVar(Name = "client.showcommandinfo", DefaultValue = "0", Description = "Show command info")]
    static ConfigVar m_showCommandInfo;
}