using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using Networking;
using Networking.Socket;

public class NullSnapshotConsumer : ISnapshotConsumer
{
    public void ProcessEntityDeSpawn(int serverTime, List<int> deSpawns)
    {
    }

    public void ProcessEntitySpawn(int serverTime, int id, ushort typeId)
    {
    }

    public void ProcessEntityUpdate(int serverTime, int id, ref NetworkReader reader)
    {
    }
}

public class ThinClientGameWorld
{
    public bool PredictionEnabled = true;
    public float FrameTimeScale = 1.0f;

    public GameTime predictedTime
    {
        get { return _predictedTime; }
    }

    public GameTime renderTime
    {
        get { return _renderTime; }
    }

    private readonly GameWorld _gameWorld;
    private GameTime _predictedTime = new GameTime(60);
    private GameTime _renderTime = new GameTime(60);
    private GameObject _localPlayerPrefab;

    // External systems
    private LocalPlayer _localPlayer;
    private ClientFrontendUpdate _clientFrontendUpdate;
    private readonly NetworkClient _networkClient;
    private readonly ISnapshotConsumer _nullSnapshotConsumer;
    private readonly NetworkStatisticsClient _networkStatistics;

    public ThinClientGameWorld(GameWorld world, NetworkClient networkClient, NetworkStatisticsClient networkStatistics)
    {
        _gameWorld = world;

        _networkClient = networkClient;
        _nullSnapshotConsumer = new NullSnapshotConsumer();
        _networkStatistics = networkStatistics;
    }

    public void Shutdown()
    {
    }

    // This is called at the actual client frame rate, so may be faster or slower than tick rate.
    public void Update(float frameDuration)
    {
        // Advances time and accumulate input into the UserCommand being generated
        HandleTime(frameDuration);

        _gameWorld.worldTime = _renderTime;
        _gameWorld.frameDuration = frameDuration;
        _gameWorld.lastServerTick = _networkClient.serverTime;

        // Prediction
        _gameWorld.worldTime = _predictedTime;

        // Update Presentation
        _gameWorld.worldTime = _predictedTime;
        _gameWorld.worldTime = _renderTime;

#if UNITY_EDITOR
        if (_gameWorld.GetEntityManager().Exists(_localPlayer.controlledEntity) &&
            _gameWorld.GetEntityManager().HasComponent<UserCommandComponentData>(_localPlayer.controlledEntity))
        {
            //var userCommand = m_GameWorld.GetEntityManager().GetComponentData<UserCommandComponentData>(m_localPlayer.controlledEntity);
            //m_ReplicatedEntityModule.FinalizedStateHistory(m_PredictedTime.tick-1, m_NetworkClient.serverTime, ref userCommand.command);
        }
#endif
    }

    public void LateUpdate(ChatSystemClient chatSystem, float frameDuration)
    {
    }

    public LocalPlayer RegisterLocalPlayer(int playerId)
    {
        if (_localPlayerPrefab == null)
        {
            _localPlayerPrefab = Resources.Load("Prefabs/LocalPlayer") as GameObject;
        }

        _localPlayer = Object.Instantiate(_localPlayerPrefab).GetComponent<LocalPlayer>();
        _localPlayer.playerId = playerId;
        _localPlayer.networkClient = _networkClient;
        _localPlayer.command.lookPitch = 90;

        var playerState = _localPlayer.gameObject.AddComponent<PlayerState>();
        playerState.playerId = playerId;
        playerState.playerName = "asdf";

        _localPlayer.playerState = playerState;

        return _localPlayer;
    }

    public ISnapshotConsumer GetSnapshotConsumer()
    {
        return _nullSnapshotConsumer;
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
        PlayerModuleClient.SampleInput(_localPlayer, false, Time.deltaTime, _renderTime.Tick);

        int prevTick = _predictedTime.Tick;

        // Increment time
        var deltaPredictedTime = frameDuration * FrameTimeScale;
        _predictedTime.AddDuration(deltaPredictedTime);

        // Adjust time to be synchronized with server
        int preferredBufferedCommandCount = 2;
        int preferredTick = _networkClient.serverTime +
                            (int) (((_networkClient.timeSinceSnapshot + _networkStatistics.rtt.average) / 1000.0f) *
                                   _gameWorld.worldTime.tickRate) + preferredBufferedCommandCount;

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
            GameDebug.Log($"CATCHUP ({_predictedTime.Tick} -> {preferredTick})");

            _networkStatistics.notifyHardCatchup = true;
            _gameWorld.nextTickTime = Game.frameTime;
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

        // Force interp time to not exeede server time
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
                PlayerModuleClient.StoreCommand(_localPlayer, tick);
                PlayerModuleClient.SendCommand(_localPlayer, tick);
            }

            PlayerModuleClient.StoreCommand(_localPlayer, _predictedTime.Tick);
        }

        // Store command
        PlayerModuleClient.StoreCommand(_localPlayer, _predictedTime.Tick);
    }
}

public class ThinClientGameLoop : Game.IGameLoop
{
    [ConfigVar(Name = "thinclient.requested", DefaultValue = "4", Description = "Number of thin clients wanted")]
    public static ConfigVar ThinClientNum;

    private string _targetServer = "";
    private readonly List<ThinClient> _thinClients = new List<ThinClient>();

    public void FixedUpdate()
    {
    }

    public bool Init(string[] args)
    {
        NetworkClient.DropSnapshots = true;

#if UNITY_EDITOR
        Game.game.levelManager.UnloadLevel();
#endif
        Console.AddCommand("disconnect", CmdDisconnect, "Disconnect from server if connected", this.GetHashCode());

        GameDebug.Log("ThinClient initialized");

        return true;
    }

    private void CmdDisconnect(string[] args)
    {
        foreach (var thinClient in _thinClients)
        {
            thinClient.Disconnect();
        }
    }

    public void LateUpdate()
    {
    }

    public void Shutdown()
    {
        NetworkClient.DropSnapshots = false;
    }

    public void Update()
    {
        if (_targetServer != "" && (Time.frameCount % 10 == 0))
        {
            if (_thinClients.Count < ThinClientNum.IntValue)
            {
                GameDebug.Log("Creating new thin client:" + _thinClients.Count);
                var thinClient = new ThinClient();
                _thinClients.Add(thinClient);
                thinClient.Connect(_targetServer);
            }
            else if (_thinClients.Count > ThinClientNum.IntValue && _thinClients.Count > 0)
            {
                GameDebug.Log("Removing thin client:" + _thinClients.Count);
                var i = _thinClients.Count - 1;
                _thinClients[i].Disconnect();
                _thinClients.RemoveAt(i);
            }
        }

        for (var i = 0; i < _thinClients.Count; ++i)
        {
            _thinClients[i].Update();
        }
    }

    public void CmdConnect(string[] args)
    {
        _targetServer = args.Length > 0 ? args[0] : "127.0.0.1";
        GameDebug.Log("Will connect to: " + _targetServer);
    }
}

public class ThinClient : INetworkClientCallbacks
{
    private const bool PredictionEnabled = true;

    private enum ClientState
    {
        Browsing,
        Connecting,
        Loading,
        Playing,
    }

    private ClientState _clientState;
    private GameWorld _gameWorld;
    private LocalPlayer _localPlayer;

    private readonly SocketTransport _transport;
    private readonly NetworkClient _networkClient;

    private readonly NetworkStatisticsClient _networkStatistics;
    private readonly PlayerSettings _requestedPlayerSettings = new PlayerSettings();
    private readonly StateMachine<ClientState> _stateMachine;

    private ChatSystemClient _chatSystem;
    private ThinClientGameWorld _clientWorld;

    private string _levelName;
    private string _targetServer = "";

    private int _connectRetryCount;
    private double _lastFrameTime;
    private bool _playerSettingsUpdated;
    private bool _performGameWorldLateUpdate;

    public ThinClient()
    {
        _stateMachine = new StateMachine<ClientState>();
        _stateMachine.Add(ClientState.Browsing, EnterBrowsingState, UpdateBrowsingState, LeaveBrowsingState);
        _stateMachine.Add(ClientState.Connecting, EnterConnectingState, UpdateConnectingState, null);
        _stateMachine.Add(ClientState.Loading, EnterLoadingState, UpdateLoadingState, null);
        _stateMachine.Add(ClientState.Playing, EnterPlayingState, UpdatePlayingState, LeavePlayingState);
        _stateMachine.SwitchTo(ClientState.Browsing);

        _gameWorld = new GameWorld("ClientWorld");
        _transport = new SocketTransport();
        _networkClient = new NetworkClient(_transport);

        if (Application.isEditor || Game.game.buildId == "AutoBuild")
        {
            NetworkClient.ClientVerifyProtocol.Value = "0";
        }

        _networkClient.UpdateClientConfig();
        _networkStatistics = new NetworkStatisticsClient(_networkClient);
        _chatSystem = new ChatSystemClient(_networkClient);

        GameDebug.Log("Network client initialized");

        _requestedPlayerSettings.PlayerName = ClientGameLoop.ClientPlayerName.Value;
        _requestedPlayerSettings.TeamId = -1;
    }

    public void Shutdown()
    {
        GameDebug.Log("ClientGameLoop shutdown");
        Console.RemoveCommandsWithTag(this.GetHashCode());

        _stateMachine.Shutdown();
        _networkClient.Shutdown();
        _gameWorld.Shutdown();
        _transport.Shutdown();
    }

    public void OnConnect(int clientId)
    {
    }

    public void OnDisconnect(int clientId)
    {
    }

    public void OnEvent(int clientId, NetworkEvent info)
    {
        Profiler.BeginSample("-ProcessEvent");

        switch ((GameNetworkEvents.EventType) info.Type.TypeId)
        {
            case GameNetworkEvents.EventType.Chat:
                /*
                fixed (uint* data = info.Data)
                {
                    var reader = new NetworkReader(data, info.Type.Schema);
                    m_ChatSystem.ReceiveMessage(reader.ReadString(256));
                }
                */
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

        _networkClient.SendData();

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
                break;
            case ConnectionState.Connecting:
                // Do nothing; just wait for either success or failure
                break;
            case ConnectionState.Disconnected:
                if (_connectRetryCount < 2)
                {
                    _connectRetryCount++;
                    GameDebug.Log($"Trying to connect to {_targetServer} (attempt #{_connectRetryCount})...");
                    _networkClient.Connect(_targetServer);
                }
                else
                {
                    GameDebug.Log("Failed to connect to server");
                    _networkClient.Disconnect();
                    _stateMachine.SwitchTo(ClientState.Browsing);
                }

                break;
        }
    }

    private void EnterLoadingState()
    {
        GameDebug.Assert(_clientWorld == null);
        GameDebug.Assert(_networkClient.isConnected);

        _requestedPlayerSettings.PlayerName = "ThinPlayer";
        _requestedPlayerSettings.CharacterType = (short) Game.characterType.IntValue;
        _playerSettingsUpdated = true;

        _clientState = ClientState.Loading;
    }

    private void UpdateLoadingState()
    {
        // Handle disconnects
        if (!_networkClient.isConnected)
        {
            GameDebug.Log("Disconnected from server (lost connection)");
            _stateMachine.SwitchTo(ClientState.Browsing);
        }

        _stateMachine.SwitchTo(ClientState.Playing);
    }

    private void EnterPlayingState()
    {
        GameDebug.Assert(_clientWorld == null);

        _clientWorld = new ThinClientGameWorld(_gameWorld, _networkClient, _networkStatistics);
        _clientWorld.PredictionEnabled = PredictionEnabled;
        _localPlayer = _clientWorld.RegisterLocalPlayer(_networkClient.clientId);

        _networkClient.QueueEvent((ushort) GameNetworkEvents.EventType.PlayerReady, true,
            (ref NetworkWriter data) => { });

        _clientState = ClientState.Playing;
    }

    private void LeavePlayingState()
    {
        Object.Destroy(_localPlayer.gameObject);
        _localPlayer = null;

        _clientWorld.Shutdown();
        _clientWorld = null;

        _gameWorld.Shutdown();
        _gameWorld = new GameWorld("ClientWorld");

        GameDebug.Log("Left playing state");
    }

    private void UpdatePlayingState()
    {
        // Handle disconnects
        if (!_networkClient.isConnected)
        {
            GameDebug.Log("Disconnected from server (lost connection)");
            _stateMachine.SwitchTo(ClientState.Browsing);
            return;
        }

        // (re)send client info if any of the config vars that contain client info has changed
        if ((ConfigVar.DirtyFlags & ConfigVar.Flags.ClientInfo) == ConfigVar.Flags.ClientInfo)
        {
            _networkClient.UpdateClientConfig();
            ConfigVar.DirtyFlags &= ~ConfigVar.Flags.ClientInfo;
        }

        float frameDuration = _lastFrameTime != 0 ? (float) (Game.frameTime - _lastFrameTime) : 0;
        _lastFrameTime = Game.frameTime;

        _clientWorld.Update(frameDuration);
        _performGameWorldLateUpdate = true;
    }

    public void RemoteConsoleCommand(string command)
    {
        _networkClient.QueueEvent((ushort) GameNetworkEvents.EventType.RemoteConsoleCmd, true,
            (ref NetworkWriter writer) => { writer.WriteString("args", command); });
    }

    public void Disconnect()
    {
        _networkClient.Disconnect();
        _stateMachine.SwitchTo(ClientState.Browsing);
    }

    private void SendPlayerSettings()
    {
        _networkClient.QueueEvent((ushort) GameNetworkEvents.EventType.PlayerSetup, true,
            (ref NetworkWriter writer) => { _requestedPlayerSettings.Serialize(ref writer); });
    }

    public void Connect(string targetServer)
    {
        if (_stateMachine.CurrentState() != ClientState.Browsing)
        {
            return;
        }

        _targetServer = targetServer;
        _stateMachine.SwitchTo(ClientState.Connecting);
    }
}