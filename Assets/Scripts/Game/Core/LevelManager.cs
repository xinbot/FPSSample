using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum LevelState
{
    Loading,
    Loaded,
}

public struct LevelLayer
{
    public AsyncOperation LoadOperation;
}

public class Level
{
    public LevelState State;
    public string Name;
    public readonly List<LevelLayer> Layers = new List<LevelLayer>(10);
}

public class LevelManager
{
    public static readonly string[] LayerNames =
    {
        "background",
        "gameplay",
    };

    public Level currentLevel { get; private set; }

    public void Init()
    {
    }

    public bool IsCurrentLevelLoaded()
    {
        return currentLevel != null && currentLevel.State == LevelState.Loaded;
    }

    public bool IsLoadingLevel()
    {
        return currentLevel != null && currentLevel.State == LevelState.Loading;
    }

    public bool CanLoadLevel(string name)
    {
        // TODO (petera). We can't really promise you can load a level before trying.
        // Refactor to handle errors during load.
        var bundle = SimpleBundleManager.LoadLevelAssetBundle(name);
        return bundle != null;
    }

    public bool LoadLevel(string name)
    {
        if (currentLevel != null)
        {
            UnloadLevel();
        }

        // This is a pretty ugly hack to handle problems with loading camera and post processing volumes
        // and those not being initialized at the same time. We simply disable the old camera and the 
        Game.game.TopCamera().enabled = false;
        Game.game.BlackFade(true);

        var newLevel = new Level();
        newLevel.Name = name;

        // TODO (petera) Use async? Seem to be not needed here.
        var bundle = SimpleBundleManager.LoadLevelAssetBundle(name);
        if (bundle == null)
        {
            GameDebug.Log("Could not load asset bundle for scene " + name);
            return false;
        }

        // Load using the name found in GetAllScenePaths because SceneManager.LoadSceneAsync is case sensitive
        // yet name may not have correct casing as file system may be case insensitive 
        var scenePaths = new List<string>(bundle.GetAllScenePaths());
        if (scenePaths.Count < 1)
        {
            GameDebug.Log("No scenes in asset bundle " + name);
            return false;
        }

        // If there is a main scene, load that first
        // TODO (petera) switch to LevelInfo based layers
        var mainScenePath = scenePaths.Find(x => x.ToLower().EndsWith("_main.unity"));
        var useLayers = true;
        if (mainScenePath == null)
        {
            useLayers = false;
            mainScenePath = scenePaths[0];
        }

        GameDebug.Log("Loading " + mainScenePath);
        var mainLoadOperation = SceneManager.LoadSceneAsync(mainScenePath, LoadSceneMode.Single);
        if (mainLoadOperation == null)
        {
            GameDebug.Log("Failed to load level : " + name);
            return false;
        }

        currentLevel = newLevel;
        currentLevel.Layers.Add(new LevelLayer {LoadOperation = mainLoadOperation});

        if (!useLayers)
        {
            return true;
        }

        // Now load all additional layers that may be here
        foreach (var layerName in LayerNames)
        {
            var layerScenePath = scenePaths.Find(sceneName => sceneName.ToLower().EndsWith(layerName + ".unity"));
            if (layerScenePath == null)
            {
                continue;
            }

            // TODO : Are we guaranteed that the scenes are initialized in order without setting allowactivation = false?
            GameDebug.Log("+Loading " + layerScenePath);
            var layerLoadOperation = SceneManager.LoadSceneAsync(layerScenePath, LoadSceneMode.Additive);
            if (layerLoadOperation != null)
            {
                currentLevel.Layers.Add(new LevelLayer {LoadOperation = layerLoadOperation});
            }
            else
            {
                GameDebug.Log("Warning : Unable to load level layer : " + layerScenePath);
            }
        }

        return true;
    }

    public void UnloadLevel()
    {
        if (currentLevel == null)
        {
            return;
        }

        if (currentLevel.State == LevelState.Loading)
        {
            throw new NotImplementedException("TODO : Implement unload during load");
        }

        // TODO : Load empty scene for now
        SceneManager.LoadScene(1);

        SimpleBundleManager.ReleaseLevelAssetBundle(currentLevel.Name);
        currentLevel = null;
    }

    public void Update()
    {
        if (currentLevel != null && currentLevel.State == LevelState.Loading)
        {
            var done = currentLevel.Layers.All(l => l.LoadOperation.isDone);
            if (done)
            {
                // Do activation here?
                currentLevel.State = LevelState.Loaded;

                if (Game.gameLoopCount == 1)
                {
                    if (Game.GetGameLoop<ServerGameLoop>() != null)
                    {
                        StripCode(BuildType.Server, true);
                    }
                    else if (Game.GetGameLoop<ClientGameLoop>() != null)
                    {
                        StripCode(BuildType.Client, true);
                    }
                    else
                    {
                        StripCode(BuildType.Default, true);
                    }
                }
                else
                {
                    StripCode(BuildType.Default, true);
                }

                GameDebug.Log("Scene " + currentLevel.Name + " loaded");
            }
        }
    }

    // TODO (petera) this code was moved here to make it available outside of editor - until we start cooking for client/server
    public enum BuildType
    {
        Default,
        Client,
        Server,
    }

    public static void StripCode(BuildType buildType, bool isDevelopmentBuild)
    {
        GameDebug.Log("Stripping code for " + buildType + " (" +
                      (isDevelopmentBuild ? "DevBuild" : "NonDevBuild") + ")");

        var deleteBehaviors = new List<MonoBehaviour>();
        var deleteGameObjects = new List<GameObject>();

        foreach (var behavior in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
        {
            if (behavior.GetType().GetCustomAttributes(typeof(EditorOnlyComponentAttribute), false).Length > 0)
            {
                deleteBehaviors.Add(behavior);
            }
            else if (behavior.GetType().GetCustomAttributes(typeof(EditorOnlyGameObjectAttribute), false).Length > 0)
            {
                deleteGameObjects.Add(behavior.gameObject);
            }
            else if (buildType == BuildType.Server &&
                     behavior.GetType().GetCustomAttributes(typeof(ClientOnlyComponentAttribute), false).Length > 0)
            {
                deleteBehaviors.Add(behavior);
            }
            else if (buildType == BuildType.Client &&
                     behavior.GetType().GetCustomAttributes(typeof(ServerOnlyComponentAttribute), false).Length > 0)
            {
                deleteBehaviors.Add(behavior);
            }
            else if (!isDevelopmentBuild && behavior.GetType()
                .GetCustomAttributes(typeof(DevelopmentOnlyComponentAttribute), false).Length > 0)
            {
                deleteBehaviors.Add(behavior);
            }
        }

        GameDebug.Log(string.Format("Stripping {0} game object(s) and {1} behavior(s)", deleteGameObjects.Count,
            deleteBehaviors.Count));

        foreach (var gameObject in deleteGameObjects)
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        foreach (var behavior in deleteBehaviors)
        {
            UnityEngine.Object.DestroyImmediate(behavior);
        }
    }
}