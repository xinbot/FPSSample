using System;
using UnityEditor;
using UnityEngine;

[Serializable]
public struct ProjectileSettings
{
    public float velocity;
    public float impactDamage;
    public float impactImpulse;
    public float collisionRadius;
    public SplashDamageSettings splashDamage;
}

[CreateAssetMenu(fileName = "ProjectileTypeDefinition", menuName = "FPS Sample/Projectile/ProjectileTypeDefinition")]
public class ProjectileTypeDefinition : ScriptableObject
{
    [HideInInspector] public WeakAssetReference guid;

    public ProjectileSettings properties;

    // Client projectile settings.  
    public int clientProjectileBufferSize = 20;
    public WeakAssetReference clientProjectilePrefab;

#if UNITY_EDITOR
    private void OnValidate()
    {
        UpdateAssetGuid();
    }

    public void SetAssetGuid(string guidStr)
    {
        var newRef = new WeakAssetReference(guidStr);
        if (!newRef.Equals(guid))
        {
            guid = newRef;
            EditorUtility.SetDirty(this);
        }
    }

    public void UpdateAssetGuid()
    {
        var path = AssetDatabase.GetAssetPath(this);
        if (!string.IsNullOrEmpty(path))
        {
            var guidStr = AssetDatabase.AssetPathToGUID(path);
            SetAssetGuid(guidStr);
        }
    }
#endif
}