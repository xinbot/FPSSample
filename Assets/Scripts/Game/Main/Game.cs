#define DEBUG_LOGGING
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.HDPipeline;
using System;
using System.Globalization;
using Networking;
using UnityEngine.Rendering.PostProcessing;
#if UNITY_EDITOR
using UnityEditor;

#endif

public struct GameTime
{
    // Current tick
    public int Tick;

    // Duration of current tick
    public float TickDuration;

    /// <summary>
    /// Length of each world tick at current tick rate, e.g. 0.0166s if ticking at 60 fps.
    /// Time between ticks
    /// </summary>
    public float tickInterval { get; private set; }

    private int _tickRate;

    /// <summary>
    /// Number of ticks per second.
    /// </summary>
    public int tickRate
    {
        get => _tickRate;
        set
        {
            _tickRate = value;
            tickInterval = 1.0f / _tickRate;
        }
    }

    public float tickDurationAsFraction
    {
        get { return TickDuration / tickInterval; }
    }

    public GameTime(int tickRate)
    {
        _tickRate = tickRate;
        tickInterval = 1.0f / _tickRate;
        Tick = 1;
        TickDuration = 0;
    }

    public void SetTime(int tick, float tickDuration)
    {
        Tick = tick;
        TickDuration = tickDuration;
    }

    public float DurationSinceTick(int tick)
    {
        return (Tick - tick) * tickInterval + TickDuration;
    }

    public void AddDuration(float duration)
    {
        TickDuration += duration;
        int deltaTicks = Mathf.FloorToInt(TickDuration * tickRate);
        Tick += deltaTicks;
        TickDuration %= tickInterval;
    }

    public static float GetDuration(GameTime start, GameTime end)
    {
        if (start.tickRate != end.tickRate)
        {
            GameDebug.LogError(
                $"Trying to compare time with different tick rates ({start.tickRate} and {end.tickRate})");
            return 0;
        }

        float result = (end.Tick - start.Tick) * start.tickInterval + end.TickDuration - start.TickDuration;
        return result;
    }
}

public class EnumeratedArrayAttribute : PropertyAttribute
{
    public readonly string[] Names;

    public EnumeratedArrayAttribute(Type enumType)
    {
        Names = Enum.GetNames(enumType);
    }
}

public interface IGameLoop
{
    bool Init(string[] args);
    void Shutdown();
    void Update();
    void FixedUpdate();
    void LateUpdate();
}

public enum GameColor
{
    Friend,
    Enemy
}

[DefaultExecutionOrder(-1000)]
public class Game : MonoBehaviour
{
    public static class Input
    {
        [Flags]
        public enum Blocker
        {
            None = 0,
            Console = 1,
            Chat = 2,
            Debug = 4,
        }

        private static Blocker _blocks;

        public static void SetBlock(Blocker b, bool value)
        {
            if (value)
            {
                _blocks |= b;
            }
            else
            {
                _blocks &= ~b;
            }
        }

        internal static float GetAxisRaw(string axis)
        {
            return _blocks != Blocker.None ? 0.0f : UnityEngine.Input.GetAxisRaw(axis);
        }

        internal static bool GetKey(KeyCode key)
        {
            return _blocks != Blocker.None ? false : UnityEngine.Input.GetKey(key);
        }

        internal static bool GetKeyDown(KeyCode key)
        {
            return _blocks != Blocker.None ? false : UnityEngine.Input.GetKeyDown(key);
        }

        internal static bool GetMouseButton(int button)
        {
            return _blocks != Blocker.None ? false : UnityEngine.Input.GetMouseButton(button);
        }

        internal static bool GetKeyUp(KeyCode key)
        {
            return _blocks != Blocker.None ? false : UnityEngine.Input.GetKeyUp(key);
        }
    }

    public delegate void UpdateDelegate();

    public event UpdateDelegate EndUpdateEvent;

    public static Game game;
    public WeakAssetReference movableBoxPrototype;

    [EnumeratedArray(typeof(GameColor))] public Color[] gameColors;

    public GameStatistics gameStatistics { get; private set; }

    // Vars owned by server and replicated to clients
    [ConfigVar(Name = "server.tickrate", DefaultValue = "60", Description = "Tick rate for server",
        Flags = ConfigVar.Flags.ServerInfo)]
    public static ConfigVar ServerTickRate;

    [ConfigVar(Name = "config.fov", DefaultValue = "60", Description = "Field of view", Flags = ConfigVar.Flags.Save)]
    public static ConfigVar ConfigFov;

    [ConfigVar(Name = "config.mousesensitivity", DefaultValue = "1.5", Description = "Mouse sensitivity",
        Flags = ConfigVar.Flags.Save)]
    public static ConfigVar ConfigMouseSensitivity;

    [ConfigVar(Name = "config.inverty", DefaultValue = "0", Description = "Invert y mouse axis",
        Flags = ConfigVar.Flags.Save)]
    public static ConfigVar ConfigInvertY;

    [ConfigVar(Name = "debug.catchloop", DefaultValue = "1",
        Description = "Catch exceptions in gameloop and pause game", Flags = ConfigVar.Flags.None)]
    public static ConfigVar DebugCatchLoop;

    [ConfigVar(Name = "chartype", DefaultValue = "-1",
        Description = "Character to start with (-1 uses default character)")]
    public static ConfigVar CharacterType;

    [ConfigVar(Name = "allowcharchange", DefaultValue = "1", Description = "Is changing character allowed")]
    public static ConfigVar AllowCharChange;

    [ConfigVar(Name = "debug.cpuprofile", DefaultValue = "0", Description = "Profile and dump cpu usage")]
    public static ConfigVar DebugCpuProfile;

    [ConfigVar(Name = "net.dropevents", DefaultValue = "0",
        Description = "Drops a fraction of all packages containing events!!")]
    public static ConfigVar NetDropEvents;

    public static readonly string UserConfigFilename = "user.cfg";
    public static readonly string BootConfigFilename = "boot.cfg";

    public static GameConfiguration Config;
    public static InputSystem InputSystem;

    public UnityEngine.Audio.AudioMixer audioMixer;
    public SoundBank defaultBank;
    public Camera bootCamera;
    public ClientFrontend clientFrontend;

    public LevelManager LevelManager;
    public SQPClient SqpClient;

    public static double FrameTime;

    public static bool IsHeadless()
    {
        return game._isHeadless;
    }

    public static ISoundSystem soundSystem
    {
        get { return game._soundSystem; }
    }

    public static int gameLoopCount
    {
        get { return game == null ? 0 : 1; }
    }

    public static T GetGameLoop<T>() where T : class
    {
        if (game == null)
        {
            return null;
        }

        foreach (var gameLoop in game._gameLoops)
        {
            if (gameLoop is T result)
            {
                return result;
            }
        }

        return null;
    }

    public static System.Diagnostics.Stopwatch clock
    {
        get { return game._clock; }
    }

    public string buildId
    {
        get { return _buildId; }
    }

    public void RequestGameLoop(Type type, string[] args)
    {
        GameDebug.Assert(typeof(IGameLoop).IsAssignableFrom(type));

        _requestedGameLoopTypes.Add(type);
        _requestedGameLoopArguments.Add(args);
        GameDebug.Log("Game loop " + type + " requested");
    }

    // Pick argument for argument(!). Given list of args return null if option is
    // not found. Return argument following option if found or empty string if none given.
    // Options are expected to be prefixed with + or -
    public static string ArgumentForOption(List<string> args, string option)
    {
        var idx = args.IndexOf(option);
        if (idx < 0)
        {
            return null;
        }

        if (idx < args.Count - 1)
        {
            return args[idx + 1];
        }

        return "";
    }

    // Global camera handling
    private readonly List<Camera> _cameraStack = new List<Camera>();
    private AutoExposure _exposure;
    private PostProcessVolume _exposureVolume;

    private DebugOverlay _debugOverlay;
    private ISoundSystem _soundSystem;
    private System.Diagnostics.Stopwatch _clock;

    private long _stopwatchFrequency;
    private int _exposureReleaseCount;
    private string _buildId = "NoBuild";

    private bool _isHeadless;
    private bool _pipeSetup;
    private bool _errorState;
    private float _nextCpuProfileTime;
    private double _lastCpuUsage;
    private double _lastCpuUsageUser;

    private static int _mouseLockFrameNo;

    private readonly List<Type> _requestedGameLoopTypes = new List<Type>();
    private readonly List<IGameLoop> _gameLoops = new List<IGameLoop>();
    private readonly List<string[]> _requestedGameLoopArguments = new List<string[]>();

    public void Awake()
    {
        GameDebug.Assert(game == null);
        DontDestroyOnLoad(gameObject);
        game = this;

        _stopwatchFrequency = System.Diagnostics.Stopwatch.Frequency;
        _clock = new System.Diagnostics.Stopwatch();
        _clock.Start();

        var buildInfo = FindObjectOfType<BuildInfo>();
        if (buildInfo != null)
        {
            _buildId = buildInfo.buildId;
        }

        var commandLineArgs = new List<string>(Environment.GetCommandLineArgs());

#if UNITY_STANDALONE_LINUX
        m_isHeadless = true;
#else
        _isHeadless = commandLineArgs.Contains("-batchmode");
#endif
        var consoleRestoreFocus = commandLineArgs.Contains("-consolerestorefocus");

        if (_isHeadless)
        {
#if UNITY_STANDALONE_WIN
            string consoleTitle;

            var overrideTitle = ArgumentForOption(commandLineArgs, "-title");
            if (overrideTitle != null)
            {
                consoleTitle = overrideTitle;
            }
            else
            {
                consoleTitle = Application.productName + " Console";
            }

            consoleTitle += " [" + System.Diagnostics.Process.GetCurrentProcess().Id + "]";

            var consoleUI = new ConsoleTextWin(consoleTitle, consoleRestoreFocus);
#elif UNITY_STANDALONE_LINUX
            var consoleUI = new ConsoleTextLinux();
#else
            UnityEngine.Debug.Log("WARNING: starting without a console");
            var consoleUI = new ConsoleNullUI();
#endif
            Console.Init(consoleUI);
        }
        else
        {
            var consoleUI = Instantiate(Resources.Load<ConsoleGUI>("Prefabs/ConsoleGUI"));
            DontDestroyOnLoad(consoleUI);
            Console.Init(consoleUI);

            _debugOverlay = Instantiate(Resources.Load<DebugOverlay>("DebugOverlay"));
            DontDestroyOnLoad(_debugOverlay);
            _debugOverlay.Init();

            gameStatistics = new GameStatistics();
        }

        // If -logfile was passed, we try to put our own logs next to the engine's logfile
        var engineLogFileLocation = ".";
        var logfileArgIdx = commandLineArgs.IndexOf("-logfile");
        if (logfileArgIdx >= 0 && commandLineArgs.Count >= logfileArgIdx)
        {
            engineLogFileLocation = System.IO.Path.GetDirectoryName(commandLineArgs[logfileArgIdx + 1]);
        }

        var logName = _isHeadless ? "game_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") : "game";
        GameDebug.Init(engineLogFileLocation, logName);

        ConfigVar.Init();

        // Support -port and -query_port as per Multiplay standard
        var serverPort = ArgumentForOption(commandLineArgs, "-port");
        if (serverPort != null)
        {
            Console.EnqueueCommandNoHistory("server.port " + serverPort);
        }

        var sqpPort = ArgumentForOption(commandLineArgs, "-query_port");
        if (sqpPort != null)
        {
            Console.EnqueueCommandNoHistory("server.sqp_port " + sqpPort);
        }

        Console.EnqueueCommandNoHistory("exec -s " + UserConfigFilename);

        // Default is to allow no frame cap, i.e. as fast as possible if vsync is disabled
        Application.targetFrameRate = -1;

        if (_isHeadless)
        {
            Application.targetFrameRate = ServerTickRate.IntValue;
            // Needed to make Target Frame Rate work; even in headless mode
            QualitySettings.vSyncCount = 0;

#if !UNITY_STANDALONE_LINUX
            if (!commandLineArgs.Contains("-nographics"))
            {
                GameDebug.Log("WARNING: running -batchmod without -nographics");
            }
#endif
        }
        else
        {
            RenderSettings.Init();
        }

        // Out of the box game behaviour is driven by boot.cfg unless you ask it not to
        if (!commandLineArgs.Contains("-noboot"))
        {
            Console.EnqueueCommandNoHistory("exec -s " + BootConfigFilename);
        }

        if (_isHeadless)
        {
            _soundSystem = new SoundSystemNull();
        }
        else
        {
            _soundSystem = new SoundSystem();
            _soundSystem.Init(audioMixer);
            _soundSystem.MountBank(defaultBank);

            var go = (GameObject) Instantiate(Resources.Load("Prefabs/ClientFrontend", typeof(GameObject)));
            DontDestroyOnLoad(go);
            clientFrontend = go.GetComponentInChildren<ClientFrontend>();
        }

        SqpClient = new SQPClient();

        GameDebug.Log("FPS Sample initialized");

#if UNITY_EDITOR
        GameDebug.Log("Build type: editor");
#elif DEVELOPMENT_BUILD
        GameDebug.Log("Build type: development");
#else
        GameDebug.Log("Build type: release");
#endif

        GameDebug.Log("BuildID: " + buildId);
        GameDebug.Log("Cwd: " + System.IO.Directory.GetCurrentDirectory());

        SimpleBundleManager.Init();
        GameDebug.Log("SimpleBundleManager initialized");

        LevelManager = new LevelManager();
        LevelManager.Init();
        GameDebug.Log("LevelManager initialized");

        InputSystem = new InputSystem();
        GameDebug.Log("InputSystem initialized");

        // TODO (petera) added Instantiate here to avoid making changes to asset file.
        // Feels like maybe SO is not really the right tool here.
        Config = Instantiate((GameConfiguration) Resources.Load("GameConfiguration"));
        GameDebug.Log("Loaded game config");

        // Game loops
        Console.AddCommand("preview", CmdPreview, "Start preview mode");
        Console.AddCommand("serve", CmdServe, "Start server listening");
        Console.AddCommand("client", CmdClient, "client: Enter client mode.");
        Console.AddCommand("thinclient", CmdThinClient, "client: Enter thin client mode.");
        Console.AddCommand("boot", CmdBoot, "Go back to boot loop");
        Console.AddCommand("connect", CmdConnect, "connect <ip>: Connect to server on ip (default: localhost)");

        Console.AddCommand("menu", CmdMenu, "show the main menu");
        Console.AddCommand("load", CmdLoad, "Load level");
        Console.AddCommand("quit", CmdQuit, "Quits");
        Console.AddCommand("screenshot", CmdScreenshot,
            "Capture screenshot. Optional argument is destination folder or filename.");
        Console.AddCommand("crashme", (args) => { GameDebug.Assert(false); }, "Crashes the game next frame ");
        Console.AddCommand("saveconfig", CmdSaveConfig, "Save the user config variables");
        Console.AddCommand("loadconfig", CmdLoadConfig, "Load the user config variables");

#if UNITY_STANDALONE_WIN
        Console.AddCommand("windowpos", CmdWindowPosition, "Position of window. e.g. windowpos 100,100");
#endif

        Console.SetOpen(true);
        Console.ProcessCommandLineArguments(commandLineArgs.ToArray());

        PushCamera(bootCamera);
    }

    public Camera TopCamera()
    {
        var count = _cameraStack.Count;
        return count == 0 ? null : _cameraStack[count - 1];
    }

    public void PushCamera(Camera cam)
    {
        if (_cameraStack.Count > 0)
        {
            SetCameraEnabled(_cameraStack[_cameraStack.Count - 1], false);
        }

        _cameraStack.Add(cam);
        SetCameraEnabled(cam, true);
        _exposureReleaseCount = 10;
    }

    public void BlackFade(bool active)
    {
        if (_exposure != null)
        {
            _exposure.active = active;
        }
    }

    public void PopCamera(Camera cam)
    {
        GameDebug.Assert(_cameraStack.Count > 1, "Trying to pop last camera off stack!");
        GameDebug.Assert(cam == _cameraStack[_cameraStack.Count - 1]);
        if (cam != null)
        {
            SetCameraEnabled(cam, false);
        }

        _cameraStack.RemoveAt(_cameraStack.Count - 1);
        SetCameraEnabled(_cameraStack[_cameraStack.Count - 1], true);
    }

    private void SetCameraEnabled(Camera cam, bool state)
    {
        if (state)
        {
            RenderSettings.UpdateCameraSettings(cam);
        }

        cam.enabled = state;
        var audioListener = cam.GetComponent<AudioListener>();
        if (audioListener != null)
        {
            audioListener.enabled = state;
            soundSystem?.SetCurrentListener(state ? audioListener : null);
        }
    }

    private void OnDestroy()
    {
        GameDebug.Shutdown();
        Console.Shutdown();
        if (_debugOverlay != null)
        {
            _debugOverlay.Shutdown();
        }
    }

    public void Update()
    {
        if (!_isHeadless)
        {
            RenderSettings.Update();
        }

        // TODO (petera) remove this hack once we know exactly when renderer is available...
        if (!_pipeSetup)
        {
            if (RenderPipelineManager.currentPipeline is HDRenderPipeline)
            {
                var layer = LayerMask.NameToLayer("PostProcess Volumes");
                if (layer == -1)
                {
                    GameDebug.LogWarning("Unable to find layer mask for camera fader");
                }
                else
                {
                    _exposure = ScriptableObject.CreateInstance<AutoExposure>();
                    _exposure.active = false;
                    _exposure.enabled.Override(true);
                    _exposure.keyValue.Override(0);
                    _exposureVolume = PostProcessManager.instance.QuickVolume(layer, 100.0f, _exposure);
                }

                _pipeSetup = true;
            }
        }

        if (_exposureReleaseCount > 0)
        {
            _exposureReleaseCount--;
            if (_exposureReleaseCount == 0)
            {
                BlackFade(false);
            }
        }

        // Verify if camera was somehow destroyed and pop it
        if (_cameraStack.Count > 1 && _cameraStack[_cameraStack.Count - 1] == null)
        {
            PopCamera(null);
        }

#if UNITY_EDITOR
        // Ugly hack to force focus to game view when using scriptable render loops.
        if (Time.frameCount < 4)
        {
            try
            {
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var gameView = (EditorWindow) Resources.FindObjectsOfTypeAll(gameViewType)[0];
                gameView.Focus();
            }
            catch (Exception)
            {
                /* too bad */
            }
        }
#endif

        FrameTime = (double) _clock.ElapsedTicks / _stopwatchFrequency;

        // Switch game loop if needed
        if (_requestedGameLoopTypes.Count > 0)
        {
            // Multiple running gameloops only allowed in editor
#if !UNITY_EDITOR
            ShutdownGameLoops();
#endif
            bool initSucceeded = false;
            for (int i = 0; i < _requestedGameLoopTypes.Count; i++)
            {
                try
                {
                    IGameLoop gameLoop = (IGameLoop) Activator.CreateInstance(_requestedGameLoopTypes[i]);
                    initSucceeded = gameLoop.Init(_requestedGameLoopArguments[i]);
                    if (!initSucceeded)
                    {
                        break;
                    }

                    _gameLoops.Add(gameLoop);
                }
                catch (Exception e)
                {
                    GameDebug.Log(string.Format("Game loop initialization threw exception : ({0})\n{1}", e.Message,
                        e.StackTrace));
                }
            }

            if (!initSucceeded)
            {
                ShutdownGameLoops();

                GameDebug.Log("Game loop initialization failed ... reverting to boot loop");
            }

            _requestedGameLoopTypes.Clear();
            _requestedGameLoopArguments.Clear();
        }

        try
        {
            if (!_errorState)
            {
                foreach (var gameLoop in _gameLoops)
                {
                    gameLoop.Update();
                }

                LevelManager.Update();
            }
        }
        catch (Exception e)
        {
            HandleGameLoopException(e);
            throw;
        }

        if (_soundSystem != null)
        {
            _soundSystem.Update();
        }

        if (clientFrontend != null)
        {
            clientFrontend.UpdateGame();
        }

        Console.ConsoleUpdate();

        WindowFocusUpdate();

        UpdateCPUStats();

        SqpClient.Update();

        EndUpdateEvent?.Invoke();
    }

    public void FixedUpdate()
    {
        foreach (var gameLoop in _gameLoops)
        {
            gameLoop.FixedUpdate();
        }
    }

    public void LateUpdate()
    {
        try
        {
            if (!_errorState)
            {
                foreach (var gameLoop in _gameLoops)
                {
                    gameLoop.LateUpdate();
                }

                Console.ConsoleLateUpdate();
            }
        }
        catch (Exception e)
        {
            HandleGameLoopException(e);
            throw;
        }

        if (gameStatistics != null)
        {
            gameStatistics.TickLateUpdate();
        }

        if (_debugOverlay != null)
        {
            _debugOverlay.TickLateUpdate();
        }
    }

    void OnApplicationQuit()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        GameDebug.Log("Farewell, cruel world...");
        System.Diagnostics.Process.GetCurrentProcess().Kill();
#endif
        ShutdownGameLoops();
    }

    private void UpdateCPUStats()
    {
        if (DebugCpuProfile.IntValue <= 0)
        {
            return;
        }

        if (Time.time > _nextCpuProfileTime)
        {
            const float interval = 5.0f;
            _nextCpuProfileTime = Time.time + interval;
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var user = process.UserProcessorTime.TotalMilliseconds;
            var total = process.TotalProcessorTime.TotalMilliseconds;
            float userUsagePct = (float) (user - _lastCpuUsageUser) / 10.0f / interval;
            float totalUsagePct = (float) (total - _lastCpuUsage) / 10.0f / interval;
            _lastCpuUsage = total;
            _lastCpuUsageUser = user;
            GameDebug.Log($"CPU Usage {totalUsagePct}% (user: {userUsagePct}%)");
        }
    }

    private void LoadLevel(string levelName)
    {
        if (!game.LevelManager.CanLoadLevel(levelName))
        {
            GameDebug.Log("ERROR : Cannot load level : " + levelName);
            return;
        }

        game.LevelManager.LoadLevel(levelName);
    }

    private void HandleGameLoopException(Exception e)
    {
        if (DebugCatchLoop.IntValue <= 0)
        {
            return;
        }

        GameDebug.Log("EXCEPTION " + e.Message + "\n" + e.StackTrace);
        Console.SetOpen(true);
        _errorState = true;
    }

    private void ShutdownGameLoops()
    {
        foreach (var gameLoop in _gameLoops)
        {
            gameLoop.Shutdown();
        }

        _gameLoops.Clear();
    }

    private void CmdPreview(string[] args)
    {
        RequestGameLoop(typeof(PreviewGameLoop), args);
        Console.s_PendingCommandsWaitForFrames = 1;
    }

    private void CmdServe(string[] args)
    {
        RequestGameLoop(typeof(ServerGameLoop), args);
        Console.s_PendingCommandsWaitForFrames = 1;
    }

    private void CmdLoad(string[] args)
    {
        LoadLevel(args[0]);
        Console.SetOpen(false);
    }

    private void CmdBoot(string[] args)
    {
        clientFrontend.ShowMenu(ClientFrontend.MenuShowing.None);
        LevelManager.UnloadLevel();
        ShutdownGameLoops();
        Console.s_PendingCommandsWaitForFrames = 1;
        Console.SetOpen(true);
    }

    private void CmdClient(string[] args)
    {
        RequestGameLoop(typeof(ClientGameLoop), args);
        Console.s_PendingCommandsWaitForFrames = 1;
    }

    private void CmdConnect(string[] args)
    {
        // Special hack to allow "connect a.b.c.d" as shorthand
        if (_gameLoops.Count == 0)
        {
            RequestGameLoop(typeof(ClientGameLoop), args);
            Console.s_PendingCommandsWaitForFrames = 1;
            return;
        }

        ClientGameLoop clientGameLoop = GetGameLoop<ClientGameLoop>();
        ThinClientGameLoop thinClientGameLoop = GetGameLoop<ThinClientGameLoop>();
        if (clientGameLoop != null)
        {
            clientGameLoop.CmdConnect(args);
        }
        else if (thinClientGameLoop != null)
        {
            thinClientGameLoop.CmdConnect(args);
        }
        else
        {
            GameDebug.Log("Cannot connect from current game mode");
        }
    }

    private void CmdThinClient(string[] args)
    {
        RequestGameLoop(typeof(ThinClientGameLoop), args);
        Console.s_PendingCommandsWaitForFrames = 1;
    }

    private void CmdQuit(string[] args)
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void CmdScreenshot(string[] arguments)
    {
        string FindNewFilename(string pattern)
        {
            for (var i = 0; i < 10000; i++)
            {
                var path = string.Format(pattern, i);
                if (System.IO.File.Exists(path))
                {
                    continue;
                }

                return path;
            }

            return null;
        }

        string filename = null;
        var root = System.IO.Path.GetFullPath(".");
        if (arguments.Length == 0)
        {
            filename = FindNewFilename($"/screenshot{root}.png");
        }
        else if (arguments.Length == 1)
        {
            var arg = arguments[0];
            if (System.IO.Directory.Exists(arg))
            {
                filename = FindNewFilename($"/screenshot{arg}.png");
            }
            else if (!System.IO.File.Exists(arg))
            {
                filename = arg;
            }
            else
            {
                Console.Write($"File {arg} already exists");
                return;
            }
        }

        if (filename != null)
        {
            GameDebug.Log($"Saving screenshot to {filename}");
            Console.SetOpen(false);
            ScreenCapture.CaptureScreenshot(filename);
        }
    }

    private void CmdMenu(string[] args)
    {
        float fadeTime = 0.0f;
        ClientFrontend.MenuShowing show = ClientFrontend.MenuShowing.Main;
        if (args.Length > 0)
        {
            if (args[0] == "0")
            {
                show = ClientFrontend.MenuShowing.None;
            }
            else if (args[0] == "2")
            {
                show = ClientFrontend.MenuShowing.Ingame;
            }
        }

        if (args.Length > 1)
        {
            float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out fadeTime);
        }

        clientFrontend.ShowMenu(show, fadeTime);
        Console.SetOpen(false);
    }

    private void CmdSaveConfig(string[] arguments)
    {
        ConfigVar.Save(UserConfigFilename);
    }

    private void CmdLoadConfig(string[] arguments)
    {
        Console.EnqueueCommandNoHistory("exec " + UserConfigFilename);
    }

#if UNITY_STANDALONE_WIN
    private void CmdWindowPosition(string[] arguments)
    {
        if (arguments.Length == 1)
        {
            string[] cords = arguments[0].Split(',');
            if (cords.Length == 2)
            {
                int x, y;
                var xParsed = int.TryParse(cords[0], out x);
                var yParsed = int.TryParse(cords[1], out y);
                if (xParsed && yParsed)
                {
                    WindowsUtil.SetWindowPosition(x, y);
                    return;
                }
            }
        }

        Console.Write("Usage: windowpos <x,y>");
    }

#endif

    public static void RequestMousePointerLock()
    {
        _mouseLockFrameNo = Time.frameCount + 1;
    }

    public static void SetMousePointerLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        _mouseLockFrameNo = Time.frameCount; // prevent default handling in WindowFocusUpdate overriding requests
    }

    public static bool GetMousePointerLock()
    {
        return Cursor.lockState == CursorLockMode.Locked;
    }

    private void WindowFocusUpdate()
    {
        bool menusShowing = (clientFrontend != null && clientFrontend.menuShowing != ClientFrontend.MenuShowing.None);
        bool lockWhenClicked = !menusShowing && !Console.IsOpen();

        if (_mouseLockFrameNo == Time.frameCount)
        {
            SetMousePointerLock(true);
            return;
        }

        if (lockWhenClicked)
        {
            // Default behaviour when no menus or anything. Catch mouse on click, release on escape.
            if (UnityEngine.Input.GetMouseButtonUp(0) && !GetMousePointerLock())
            {
                SetMousePointerLock(true);
            }

            if (UnityEngine.Input.GetKeyUp(KeyCode.Escape) && GetMousePointerLock())
            {
                SetMousePointerLock(false);
            }
        }
        else
        {
            // When menu or console open, release lock
            if (GetMousePointerLock())
            {
                SetMousePointerLock(false);
            }
        }
    }
}