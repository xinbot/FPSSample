using UnityEngine;

[CreateAssetMenu(fileName = "ProjectileModuleSettings", menuName = "FPS Sample/Projectile/ProjectileSystemSettings")]
public class ProjectileModuleSettings : ScriptableObject
{
    public ReplicatedEntityFactory projectileFactory;
}