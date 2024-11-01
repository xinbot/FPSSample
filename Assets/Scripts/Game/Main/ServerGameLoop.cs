using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Networking;
using Networking.Compression;
using Networking.Socket;
using UnityEngine.Profiling;

public class ServerGameWorld : ISnapshotGenerator, IClientCommandProcessor
{
    public int worldTick => _gameWorld.WorldTime.Tick;

    public int tickRate
    {
        get => _gameWorld.WorldTime.tickRate;
        set => _gameWorld.WorldTime.tickRate = value;
    }

    public float tickInterval => _gameWorld.WorldTime.tickInterval;

    // External systems
    private readonly NetworkServer _networkServer;
    private readonly Dictionary<int, ServerGameLoop.ClientInfo> _clients;
    private readonly ChatSystemServer _chatSystemServer;

    // Internal systems
    private GameWorld _gameWorld;
    private readonly CharacterModuleServer _characterModule;
    private readonly ProjectileModuleServer _projectileModule;
    private readonly HitCollisionModule _hitCollisionModule;
    private readonly PlayerModuleServer _playerModule;
    private readonly SpectatorCamModuleServer _spectatorCamModule;
    private readonly ReplicatedEntityModuleServer _replicatedEntityModule;
    private readonly ItemModule _itemModule;

    private readonly ServerCameraSystem _cameraSystem;
    private readonly GameModeSystemServer _gameModeSystem;

    private readonly DamageAreaSystemServer _damageAreaSystem;
    private readonly TeleporterSystemServer _teleporterSystem;

    private readonly HandleGrenadeRequest _handleGrenadeRequests;
    private readonly StartGrenadeMovement _startGrenadeMovement;
    private readonly FinalizeGrenadeMovement _finalizeGrenadeMovement;

    private readonly MoverUpdate _platformSystem;
    private readonly UpdateDestructableProps _destructiblePropSystem;
    private readonly MovableSystemServer _moveableSystem;

    private char[] _msgBuf = new char[256];

    public ServerGameWorld(GameWorld world, NetworkServer networkServer,
        Dictionary<int, ServerGameLoop.ClientInfo> clients, ChatSystemServer chatSystemServer,
        BundledResourceManager resourceSystem)
    {
        _networkServer = networkServer;
        _clients = clients;
        _chatSystemServer = chatSystemServer;

        _gameWorld = world;

        _characterModule = new CharacterModuleServer(_gameWorld, resourceSystem);
        _projectileModule = new ProjectileModuleServer(_gameWorld, resourceSystem);
        _hitCollisionModule = new HitCollisionModule(_gameWorld, 128, 1);
        _playerModule = new PlayerModuleServer(_gameWorld, resourceSystem);
        _spectatorCamModule = new SpectatorCamModuleServer(_gameWorld, resourceSystem);
        _replicatedEntityModule = new ReplicatedEntityModuleServer(_gameWorld, resourceSystem, _networkServer);
        _replicatedEntityModule.ReserveSceneEntities(networkServer);
        _itemModule = new ItemModule(_gameWorld);

        _gameModeSystem = _gameWorld.GetECSWorld()
            .CreateManager<GameModeSystemServer>(_gameWorld, chatSystemServer, resourceSystem);

        _destructiblePropSystem = _gameWorld.GetECSWorld().CreateManager<UpdateDestructableProps>(_gameWorld);

        _damageAreaSystem = _gameWorld.GetECSWorld().CreateManager<DamageAreaSystemServer>(_gameWorld);

        _teleporterSystem = _gameWorld.GetECSWorld().CreateManager<TeleporterSystemServer>(_gameWorld);

        _handleGrenadeRequests =
            _gameWorld.GetECSWorld().CreateManager<HandleGrenadeRequest>(_gameWorld, resourceSystem);
        _startGrenadeMovement = _gameWorld.GetECSWorld().CreateManager<StartGrenadeMovement>(_gameWorld);
        _finalizeGrenadeMovement = _gameWorld.GetECSWorld().CreateManager<FinalizeGrenadeMovement>(_gameWorld);

        _platformSystem = _gameWorld.GetECSWorld().CreateManager<MoverUpdate>(_gameWorld);

        _moveableSystem = new MovableSystemServer(_gameWorld, resourceSystem);
        _cameraSystem = new ServerCameraSystem(_gameWorld);
    }

    public void Shutdown()
    {
        _characterModule.Shutdown();
        _projectileModule.Shutdown();
        _hitCollisionModule.Shutdown();
        _playerModule.Shutdown();
        _spectatorCamModule.Shutdown();

        _gameWorld.GetECSWorld().DestroyManager(_destructiblePropSystem);
        _gameWorld.GetECSWorld().DestroyManager(_damageAreaSystem);
        _gameWorld.GetECSWorld().DestroyManager(_teleporterSystem);

        _gameWorld.GetECSWorld().DestroyManager(_handleGrenadeRequests);
        _gameWorld.GetECSWorld().DestroyManager(_startGrenadeMovement);
        _gameWorld.GetECSWorld().DestroyManager(_finalizeGrenadeMovement);

        _gameWorld.GetECSWorld().DestroyManager(_platformSystem);

        _replicatedEntityModule.Shutdown();
        _itemModule.Shutdown();

        _cameraSystem.Shutdown();
        _moveableSystem.Shutdown();

        _gameWorld = null;
    }

    public void RespawnPlayer(PlayerState player)
    {
        if (player.controlledEntity == Entity.Null)
        {
            return;
        }

        if (_gameWorld.GetEntityManager().HasComponent<Character>(player.controlledEntity))
        {
            CharacterDespawnRequest.Create(_gameWorld, player.controlledEntity);
        }

        player.controlledEntity = Entity.Null;
    }

    public void HandlePlayerSetupEvent(PlayerState player, PlayerSettings settings)
    {
        if (player.playerName != settings.PlayerName)
        {
            int length;
            if (player.playerName == "")
            {
                length = StringFormatter.Write(ref _msgBuf, 0, "{0} joined", settings.PlayerName);
            }
            else
            {
                length = StringFormatter.Write(ref _msgBuf, 0, "{0} is now known as {1}", player.playerName,
                    settings.PlayerName);
            }

            _chatSystemServer.SendChatAnnouncement(new CharBufView(_msgBuf, length));
            player.playerName = settings.PlayerName;
        }

        var playerEntity = player.gameObject.GetComponent<GameObjectEntity>().Entity;
        var charControl = _gameWorld.GetEntityManager().GetComponentObject<PlayerCharacterControl>(playerEntity);

        charControl.requestedCharacterType = settings.CharacterType;
    }

    public void ProcessCommand(int connectionId, int tick, ref NetworkReader data)
    {
        ServerGameLoop.ClientInfo client;
        if (!_clients.TryGetValue(connectionId, out client))
        {
            return;
        }

        if (client.Player)
        {
            var serializeContext = new SerializeContext
            {
                entityManager = _gameWorld.GetEntityManager(),
                entity = Entity.Null,
                refSerializer = null,
                tick = tick
            };

            if (tick == _gameWorld.WorldTime.Tick)
            {
                client.LatestCommand.Deserialize(ref serializeContext, ref data);
            }

            // Pass on command to controlled entity
            if (client.Player.controlledEntity != Entity.Null)
            {
                var userCommand = _gameWorld.GetEntityManager().GetComponentData<UserCommandComponentData>(
                    client.Player.controlledEntity);

                userCommand.command = client.LatestCommand;

                _gameWorld.GetEntityManager().SetComponentData(client.Player.controlledEntity, userCommand);
            }
        }
    }

    public bool HandleClientCommand(ServerGameLoop.ClientInfo client, string command)
    {
        if (command == "nextchar")
        {
            GameDebug.Log("nextchar for client " + client.ID);
            _gameModeSystem.RequestNextChar(client.Player);
        }
        else
        {
            return false;
        }

        return true;
    }

    public void ServerTickUpdate()
    {
        Profiler.BeginSample("ServerGameWorld.ServerTickUpdate()");

        _gameWorld.WorldTime.Tick++;
        _gameWorld.WorldTime.TickDuration = _gameWorld.WorldTime.tickInterval;
        _gameWorld.frameDuration = _gameWorld.WorldTime.tickInterval;

        Profiler.BeginSample("HandleClientCommands");

        // This call backs into ProcessCommand
        _networkServer.HandleClientCommands(_gameWorld.WorldTime.Tick, this);

        Profiler.EndSample();

        GameTime gameTime = new GameTime(_gameWorld.WorldTime.tickRate);
        gameTime.SetTime(_gameWorld.WorldTime.Tick, _gameWorld.WorldTime.tickInterval);

        // Handle spawn requests. All creation of game entities should happen in this phase        
        _characterModule.HandleSpawnRequests();
        _spectatorCamModule.HandleSpawnRequests();
        _projectileModule.HandleRequests();
        _handleGrenadeRequests.Update();

        // Handle newly spawned entities          
        _characterModule.HandleSpawns();
        _hitCollisionModule.HandleSpawning();
        _replicatedEntityModule.HandleSpawning();
        _itemModule.HandleSpawn();

        // Handle controlled entity changed
        _characterModule.HandleControlledEntityChanged();

        // Start movement of scene objects. Scene objects that player movement
        // depends on should finish movement in this phase
        _moveableSystem.Update();
        _platformSystem.Update();
        _projectileModule.MovementStart();
        _startGrenadeMovement.Update();
        _cameraSystem.Update();

        // Update movement of player controlled units 
        _teleporterSystem.Update();
        _characterModule.AbilityRequestUpdate();
        _characterModule.MovementStart();
        _characterModule.MovementResolve();
        _characterModule.AbilityStart();
        _characterModule.AbilityResolve();

        // Finalize movement of modules that only depend on data from previous frames
        // We want to wait as long as possible so queries potentially can be handled in jobs  
        _projectileModule.MovementResolve();
        _finalizeGrenadeMovement.Update();

        // Handle damage
        _destructiblePropSystem.Update();
        _damageAreaSystem.Update();
        _hitCollisionModule.HandleSplashDamage();
        _characterModule.HandleDamage();

        _characterModule.PresentationUpdate();

        // Update gameMode. Run last to allow picking up deaths etc.
        _gameModeSystem.Update();

        // Handle deSpawns
        // TODO (mogensh) this destroys presentations and needs to be done first so its picked up. We need better way of handling destruction ordering
        _characterModule.HandleDepawns();
        _hitCollisionModule.HandleDespawn();
        _replicatedEntityModule.HandleDespawning();
        _gameWorld.ProcessDespawns();

        Profiler.EndSample();
    }

    // This is called every render frame where an tick update has been performed
    public void LateUpdate()
    {
        _characterModule.AttachmentUpdate();

        _hitCollisionModule.StoreColliderState();
    }

    public void HandleClientConnect(ServerGameLoop.ClientInfo client)
    {
        client.Player = _playerModule.CreatePlayer(_gameWorld, client.ID, "", client.IsReady);
    }

    public void HandleClientDisconnect(ServerGameLoop.ClientInfo client)
    {
        _playerModule.CleanupPlayer(client.Player);
        _characterModule.CleanupPlayer(client.Player);
    }

    public void GenerateEntitySnapshot(int entityId, ref NetworkWriter writer)
    {
        Profiler.BeginSample("ServerGameLoop.GenerateEntitySnapshot()");

        _replicatedEntityModule.GenerateEntitySnapshot(entityId, ref writer);

        Profiler.EndSample();
    }

    public string GenerateEntityName(int entityId)
    {
        return _replicatedEntityModule.GenerateName(entityId);
    }
}

public class ServerGameLoop : IGameLoop, INetworkCallbacks
{
    [ConfigVar(Name = "show.gameloopinfo", DefaultValue = "0", Description = "Show gameloop info")]
    public static ConfigVar ShowGameLoopInfo;

    [ConfigVar(Name = "server.quitwhenempty", DefaultValue = "0",
        Description = "If enabled, quit when last client disconnects.")]
    public static ConfigVar ServerQuitWhenEmpty;

    [ConfigVar(Name = "server.recycleinterval", DefaultValue = "0",
        Description = "Exit when N seconds old AND when 0 players. 0 means never.")]
    public static ConfigVar ServerRecycleInterval;

    [ConfigVar(Name = "debug.servertickstats", DefaultValue = "0",
        Description = "Show stats about how many ticks we run per Unity update (headless only)")]
    public static ConfigVar DebugServerTickStats;

    [ConfigVar(Name = "server.maxclients", DefaultValue = "8", Description = "Maximum allowed clients")]
    public static ConfigVar ServerMaxClients;

    [ConfigVar(Name = "server.disconnecttimeout", DefaultValue = "30000",
        Description = "Timeout in ms. Server will kick clients after this interval if nothing has been heard.")]
    public static ConfigVar ServerDisconnectTimeout;

    [ConfigVar(Name = "server.servername", DefaultValue = "", Description = "Servername")]
    public static ConfigVar ServerServerName;

    private enum ServerState
    {
        Idle,
        Loading,
        Active,
    }

    public class ClientInfo
    {
        public int ID;
        public readonly PlayerSettings PlayerSettings = new PlayerSettings();
        public bool IsReady;
        public PlayerState Player;
        public UserCommand LatestCommand = UserCommand.defaultCommand;
    }

    private NetworkServer _networkServer;
    private GameWorld _gameWorld;
    private NetworkStatisticsServer _networkStatistics;
    private NetworkCompressionModel _model = NetworkCompressionModel.DefaultModel;

    private SocketTransport _networkTransport;

    private BundledResourceManager _resourceSystem;
    private ChatSystemServer _chatSystem;

    private StateMachine<ServerState> _stateMachine;
    private ServerGameWorld _serverGameWorld;
    private SQPServer _serverQueryProtocolServer;

    private int _maxClients;
    private int _simStartTimeTick;
    private float _lastSimTime;
    private float _serverStartTime;
    private bool _performLateUpdate;
    private double _nextTickTime;
    private long _simStartTime;
    private string _requestedGameMode;

    private readonly Dictionary<int, ClientInfo> _clients = new Dictionary<int, ClientInfo>();

    public bool Init(string[] args)
    {
        // Set up state machine for ServerGame
        _stateMachine = new StateMachine<ServerState>();
        _stateMachine.Add(ServerState.Idle, null, UpdateIdleState, null);
        _stateMachine.Add(ServerState.Loading, null, UpdateLoadingState, null);
        _stateMachine.Add(ServerState.Active, EnterActiveState, UpdateActiveState, LeaveActiveState);

        _stateMachine.SwitchTo(ServerState.Idle);

        _networkTransport = new SocketTransport(NetworkConfig.ServerPort.IntValue, ServerMaxClients.IntValue);
        var listenAddresses = NetworkUtils.GetLocalInterfaceAddresses();
        if (listenAddresses.Count > 0)
        {
            Console.SetPrompt($"{listenAddresses[0]}:{NetworkConfig.ServerPort.Value}> ");
        }

        GameDebug.Log("Listening on " + string.Join(", ", NetworkUtils.GetLocalInterfaceAddresses()) + " on port " +
                      NetworkConfig.ServerPort.IntValue);

        _networkServer = new NetworkServer(_networkTransport);

        if (Game.game.clientFrontend != null)
        {
            var serverPanel = Game.game.clientFrontend.serverPanel;
            serverPanel.SetPanelActive(true);
            serverPanel.serverInfo.text += "Listening on:\n";
            foreach (var address in NetworkUtils.GetLocalInterfaceAddresses())
            {
                serverPanel.serverInfo.text += address + ":" + NetworkConfig.ServerPort.IntValue + "\n";
            }
        }

        _networkServer.UpdateClientInfo();
        _networkServer.ServerInformation.CompressionModel = _model;

        if (ServerServerName.Value == "")
        {
            ServerServerName.Value = MakeServerName();
        }

        _serverQueryProtocolServer = new SQPServer(NetworkConfig.ServerSqpPort.IntValue > 0
            ? NetworkConfig.ServerSqpPort.IntValue
            : NetworkConfig.ServerPort.IntValue + NetworkConfig.SqpPortOffset);

#if UNITY_EDITOR
        Game.game.LevelManager.UnloadLevel();
#endif
        _gameWorld = new GameWorld("ServerWorld");

        _networkStatistics = new NetworkStatisticsServer(_networkServer);

        _chatSystem = new ChatSystemServer(_clients, _networkServer);

        GameDebug.Log("Network server initialized");

        Console.AddCommand("load", CmdLoad, "Load a named scene", this.GetHashCode());
        Console.AddCommand("unload", CmdUnload, "Unload current scene", this.GetHashCode());
        Console.AddCommand("respawn", CmdRespawn, "Respawn character (usage : respawn playername|playerId)",
            this.GetHashCode());
        Console.AddCommand("servername", CmdSetServerName, "Set name of the server", this.GetHashCode());
        Console.AddCommand("beginnetworkprofile", CmdBeginNetworkProfile, "begins a network profile",
            this.GetHashCode());
        Console.AddCommand("endnetworkprofile", CmdEndNetworkProfile,
            "Ends a network profile and analyzes. [optional] filepath for model data", this.GetHashCode());
        Console.AddCommand("loadcompressionmodel", CmdLoadNetworkCompressionModel,
            "Loads a network compression model from a filepath", this.GetHashCode());
        Console.AddCommand("list", CmdList, "List clients", this.GetHashCode());

        CmdLoad(args);
        Game.SetMousePointerLock(false);

        _serverStartTime = Time.time;

        GameDebug.Log("Server initialized");
        Console.SetOpen(false);

        return true;
    }

    public void Shutdown()
    {
        GameDebug.Log("ServerGameState shutdown");
        Console.RemoveCommandsWithTag(GetHashCode());

        _stateMachine.Shutdown();
        _networkServer.Shutdown();

        _networkTransport.Shutdown();
        Game.game.LevelManager.UnloadLevel();

        _gameWorld.Shutdown();
        _gameWorld = null;
    }

    public void Update()
    {
        if (ServerRecycleInterval.FloatValue > 0.0f)
        {
            // Recycle server if time is up and no clients connected
            if (_clients.Count == 0 && Time.time > _serverStartTime + ServerRecycleInterval.FloatValue)
            {
                GameDebug.Log("Server exiting because recycle timeout was hit.");
                Console.EnqueueCommandNoHistory("quit");
            }
        }

        if (_clients.Count > _maxClients)
        {
            _maxClients = _clients.Count;
        }

        if (ServerQuitWhenEmpty.IntValue > 0 && _maxClients > 0 && _clients.Count == 0)
        {
            GameDebug.Log("Server exiting because last client disconnected");
            Console.EnqueueCommandNoHistory("quit");
        }

        _simStartTime = Game.clock.ElapsedTicks;
        _simStartTimeTick = _serverGameWorld != null ? _serverGameWorld.worldTick : 0;

        UpdateNetwork();

        _stateMachine.Update();

        _networkServer.SendData();

        _networkStatistics.Update();

        if (ShowGameLoopInfo.IntValue > 0)
        {
            OnDebugDrawGameLoopInfo();
        }
    }

    public NetworkServer GetNetworkServer()
    {
        return _networkServer;
    }

    public void OnConnect(int id)
    {
        var client = new ClientInfo();
        client.ID = id;
        _clients.Add(id, client);

        if (_serverGameWorld != null)
        {
            _serverGameWorld.HandleClientConnect(client);
        }
    }

    public void OnDisconnect(int id)
    {
        ClientInfo client;
        if (_clients.TryGetValue(id, out client))
        {
            if (_serverGameWorld != null)
            {
                _serverGameWorld.HandleClientDisconnect(client);
            }

            _clients.Remove(id);
        }
    }

    public unsafe void OnEvent(int clientId, NetworkEvent info)
    {
        var client = _clients[clientId];
        fixed (uint* data = info.Data)
        {
            var reader = new NetworkReader(data, info.Type.Schema);

            switch ((GameNetworkEvents.EventType) info.Type.TypeId)
            {
                case GameNetworkEvents.EventType.PlayerReady:
                    _networkServer.MapReady(clientId); // TODO (petera) hacky
                    client.IsReady = true;
                    break;

                case GameNetworkEvents.EventType.PlayerSetup:
                    client.PlayerSettings.Deserialize(ref reader);
                    if (client.Player != null)
                    {
                        _serverGameWorld.HandlePlayerSetupEvent(client.Player, client.PlayerSettings);
                    }

                    break;

                case GameNetworkEvents.EventType.RemoteConsoleCmd:
                    HandleClientCommand(client, reader.ReadString());
                    break;

                case GameNetworkEvents.EventType.Chat:
                    _chatSystem.ReceiveMessage(client, reader.ReadString(256));
                    break;
            }
        }
    }

    private void HandleClientCommand(ClientInfo client, string command)
    {
        if (_serverGameWorld != null && _serverGameWorld.HandleClientCommand(client, command))
        {
            return;
        }

        // Fall back is just to become a server console command
        // TODO (petera) Add some sort of security system here
        Console.EnqueueCommandNoHistory(command);
    }

    private void UpdateNetwork()
    {
        Profiler.BeginSample("ServerGameLoop.UpdateNetwork");

        // If serverTickrate was changed, update both game world and 
        if ((ConfigVar.DirtyFlags & ConfigVar.Flags.ServerInfo) == ConfigVar.Flags.ServerInfo)
        {
            GameDebug.Log("WARNING: UpdateClientInfo deprecated");
            _networkServer.UpdateClientInfo();
            ConfigVar.DirtyFlags &= ~ConfigVar.Flags.ServerInfo;
        }

        if (_serverGameWorld != null && _serverGameWorld.tickRate != Game.ServerTickRate.IntValue)
        {
            _serverGameWorld.tickRate = Game.ServerTickRate.IntValue;
        }

        // Update SQP data with current values
        var sid = _serverQueryProtocolServer.ServerInfoData;
        sid.BuildId = Game.game.buildId;
        sid.Port = (ushort) NetworkConfig.ServerPort.IntValue;
        sid.CurrentPlayers = (ushort) _clients.Count;
        sid.GameType = GameModeSystemServer.modeName.Value;
        sid.Map = Game.game.LevelManager.currentLevel.Name;
        sid.MaxPlayers = (ushort) ServerMaxClients.IntValue;
        sid.ServerName = ServerServerName.Value;

        _serverQueryProtocolServer.Update();

        _networkServer.Update(this);

        Profiler.EndSample();
    }

    /// <summary>
    /// Idle state, no level is loaded
    /// </summary>
    private void UpdateIdleState()
    {
    }

    /// <summary>
    /// Loading state, load in progress
    /// </summary>
    private void UpdateLoadingState()
    {
        if (Game.game.LevelManager.IsCurrentLevelLoaded())
        {
            _stateMachine.SwitchTo(ServerState.Active);
        }
    }

    /// <summary>
    /// Active state, level loaded
    /// </summary>
    private void EnterActiveState()
    {
        GameDebug.Assert(_serverGameWorld == null);

        _gameWorld.RegisterSceneEntities();

        _resourceSystem = new BundledResourceManager(_gameWorld, "BundledResources/Server");

        _networkServer.InitializeMap((ref NetworkWriter data) =>
        {
            data.WriteString("name", Game.game.LevelManager.currentLevel.Name);
        });

        _serverGameWorld =
            new ServerGameWorld(_gameWorld, _networkServer, _clients, _chatSystem, _resourceSystem);
        foreach (var pair in _clients)
        {
            _serverGameWorld.HandleClientConnect(pair.Value);
        }
    }

    private readonly Dictionary<int, int> _tickStats = new Dictionary<int, int>();

    private void UpdateActiveState()
    {
        GameDebug.Assert(_serverGameWorld != null);

        int tickCount = 0;
        while (Game.FrameTime > _nextTickTime)
        {
            tickCount++;
            _serverGameWorld.ServerTickUpdate();

            Profiler.BeginSample("GenerateSnapshots");
            _networkServer.GenerateSnapshot(_serverGameWorld, _lastSimTime);
            Profiler.EndSample();

            _nextTickTime += _serverGameWorld.tickInterval;

            _performLateUpdate = true;
        }

        // If running as headless we nudge the Application.targetFramerate back and forth
        // around the actual framerate -- always trying to have a remaining time of half a frame
        // The goal is to have the while loop above tick exactly 1 time
        //
        // The reason for using targetFramerate is to allow Unity to sleep between frames
        // reducing cpu usage on server.
        //
        if (Game.IsHeadless())
        {
            float remainTime = (float) (_nextTickTime - Game.FrameTime);

            int rate = _serverGameWorld.tickRate;
            if (remainTime > 0.75f * _serverGameWorld.tickInterval)
            {
                rate -= 2;
            }
            else if (remainTime < 0.25f * _serverGameWorld.tickInterval)
            {
                rate += 2;
            }

            Application.targetFrameRate = rate;

            // Show some stats about how many world ticks per unity update we have been running
            if (DebugServerTickStats.IntValue > 0)
            {
                if (Time.frameCount % 10 == 0)
                {
                    GameDebug.Log(remainTime + ":" + rate);
                }

                if (!_tickStats.ContainsKey(tickCount))
                {
                    _tickStats[tickCount] = 0;
                }

                _tickStats[tickCount] = _tickStats[tickCount] + 1;
                if (Time.frameCount % 100 == 0)
                {
                    foreach (var p in _tickStats)
                    {
                        GameDebug.Log(p.Key + ":" + p.Value);
                    }
                }
            }
        }
    }

    private void LeaveActiveState()
    {
        _serverGameWorld.Shutdown();
        _serverGameWorld = null;

        _resourceSystem.Shutdown();
    }

    public void FixedUpdate()
    {
    }

    public void LateUpdate()
    {
        if (_serverGameWorld != null && _simStartTimeTick != _serverGameWorld.worldTick)
        {
            // Only update sim time if we actually simulatated
            // TODO : remove this when targetFrameRate works the way we want it.
            _lastSimTime = Game.clock.GetTicksDeltaAsMilliseconds(_simStartTime);
        }

        if (_performLateUpdate)
        {
            if (_serverGameWorld != null)
            {
                _serverGameWorld.LateUpdate();
            }

            _performLateUpdate = false;
        }
    }

    private void LoadLevel(string levelName, string gameMode = "deathmatch")
    {
        if (!Game.game.LevelManager.CanLoadLevel(levelName))
        {
            GameDebug.Log("ERROR : Cannot load level : " + levelName);
            return;
        }

        _requestedGameMode = gameMode;
        Game.game.LevelManager.LoadLevel(levelName);

        _stateMachine.SwitchTo(ServerState.Loading);
    }

    private void UnloadLevel()
    {
        // TODO
    }

    private void CmdSetServerName(string[] args)
    {
        if (args.Length > 0)
        {
            // TODO (petera) fix or remove this?
        }
        else
        {
            Console.Write("Invalid argument to servername (usage : servername name)");
        }
    }

    private void CmdLoad(string[] args)
    {
        if (args.Length == 1)
        {
            LoadLevel(args[0]);
        }
        else if (args.Length == 2)
        {
            LoadLevel(args[0], args[1]);
        }
    }

    private void CmdUnload(string[] args)
    {
        UnloadLevel();
    }

    private void CmdRespawn(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Write("Invalid argument for respawn command (usage : respawn playername|playerId)");
            return;
        }

        int playerId;
        var playerName = args[0];
        var usePlayerId = int.TryParse(args[0], out playerId);

        foreach (var pair in _clients)
        {
            var client = pair.Value;
            if (client.Player == null)
            {
                continue;
            }

            if (usePlayerId && client.ID != playerId)
            {
                continue;
            }

            if (!usePlayerId && client.Player.playerName != playerName)
            {
                continue;
            }

            _serverGameWorld.RespawnPlayer(client.Player);
        }

        Console.Write(
            "Could not find character. Unknown player, invalid character id or player doesn't have a character" +
            args[0]);
    }

    private void CmdBeginNetworkProfile(string[] args)
    {
        var networkServer = GetNetworkServer();
        if (networkServer != null)
        {
            networkServer.StartNetworkProfile();
            Console.Write("Profiling started");
        }
        else
        {
            Console.Write("No server running");
        }
    }

    private void CmdEndNetworkProfile(string[] args)
    {
        var networkServer = GetNetworkServer();
        if (networkServer != null)
        {
            networkServer.EndNetworkProfile(args.Length >= 1 ? args[0] : null);
        }
        else
        {
            Console.Write("No server running");
        }
    }

    private void CmdLoadNetworkCompressionModel(string[] args)
    {
        var networkServer = GetNetworkServer();
        if (networkServer != null && networkServer.GetConnections().Count > 0)
        {
            Console.Write("Can only load compression model when server when no clients are connected");
            return;
        }

        if (args.Length != 1)
        {
            Console.Write("Syntax: loadcompressionmodel filepath");
            return;
        }

        byte[] modelData;
        try
        {
            modelData = System.IO.File.ReadAllBytes(args[0]);
        }
        catch (System.Exception e)
        {
            Console.Write("Failed to read file: " + args[0] + " (" + e + ")");
            return;
        }

        _model = new NetworkCompressionModel(modelData);

        if (networkServer != null)
        {
            networkServer.ServerInformation.CompressionModel = _model;
        }

        Console.Write("Model Loaded");
    }

    private void CmdList(string[] args)
    {
        Console.Write("Players on server:");
        Console.Write("-------------------");
        Console.Write($"   ID PlayerName");
        Console.Write("-------------------");

        foreach (var c in _clients)
        {
            var client = c.Value;
            Console.Write($"   {client.ID:00} {client.PlayerSettings.PlayerName,-15}");
        }

        Console.Write("-------------------");
        Console.Write($"Total: {_clients.Count}/{ServerMaxClients.IntValue} players connected");
    }

    private string MakeServerName()
    {
        var front = new[]
        {
            "Ultimate", "Furry", "Quick", "Laggy", "Hot", "Curious", "Flappy", "Sneaky", "Nested", "Deep", "Blue",
            "Hipster", "Artificial"
        };
        var rear = new[]
        {
            "Speedrun", "Fragfest", "Win", "Exception", "Prefab", "Scene", "Garbage", "System", "Souls", "Whitespace",
            "Dolphin"
        };
        return front[Random.Range(0, front.Length)] + " " + rear[Random.Range(0, rear.Length)];
    }

    private void OnDebugDrawGameLoopInfo()
    {
    }
}