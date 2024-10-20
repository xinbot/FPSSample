using UnityEngine;
using System.Collections.Generic;

public class SimpleBundleManager
{
    public const string AssetBundleFolder = "AssetBundles";

    [ConfigVar(Name = "res.runtimebundlepath", DefaultValue = "AssetBundles", Description = "Asset bundle folder",
        Flags = ConfigVar.Flags.ServerInfo)]
    public static ConfigVar RuntimeBundlePath;

    private static readonly Dictionary<string, AssetBundle> LevelBundles = new Dictionary<string, AssetBundle>();

    public static string GetRuntimeBundlePath()
    {
#if UNITY_PS4
        return Application.streamingAssetsPath + "/" + assetBundleFolder;
#else
        if (Application.isEditor)
        {
            return "AutoBuild/" + AssetBundleFolder;
        }

        return RuntimeBundlePath.Value;
#endif
    }

    public static void Init()
    {
    }

    public static AssetBundle LoadLevelAssetBundle(string name)
    {
        var bundlePathname = GetRuntimeBundlePath() + "/" + name;
        GameDebug.Log("loading:" + bundlePathname);

        var cacheKey = name.ToLower();
        AssetBundle result;
        if (!LevelBundles.TryGetValue(cacheKey, out result))
        {
            result = AssetBundle.LoadFromFile(bundlePathname);
            if (result != null)
            {
                LevelBundles.Add(cacheKey, result);
            }
        }

        return result;
    }

    public static void ReleaseLevelAssetBundle(string name)
    {
        var cacheKey = name.ToLower();
        if (LevelBundles.ContainsKey(cacheKey))
        {
            AssetBundle result = LevelBundles[cacheKey];
            result.Unload(true);
            LevelBundles.Remove(cacheKey);
        }
    }
}