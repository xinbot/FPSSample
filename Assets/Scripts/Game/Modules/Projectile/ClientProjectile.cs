using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.VFX;

[ClientOnlyComponent]
public class ClientProjectile : MonoBehaviour
{
    // Settings
    public GameObject shellRoot;
    public GameObject trailRoot;
    public SoundDef thrustSound;
    public float rotationSpeed = 500;
    public float offsetScaleDuration = 0.5f;
    public SoundSystem.SoundHandle thrustSoundHandle;
    public SpatialEffectTypeDefinition impactEffect;

    // State
    public bool isVisible => _isVisible == 1;

    [NonSerialized] public bool Impacted;
    [NonSerialized] public float Roll;
    [NonSerialized] public float OffsetScale;

    [NonSerialized] public Entity Projectile;
    [NonSerialized] public Vector3 StartOffset;

    [NonSerialized] public int PoolIndex;
    [NonSerialized] public int BufferIndex;

    private int _isVisible = -1;

    public void Reset()
    {
        Projectile = Entity.Null;
        Impacted = false;
    }

    public void SetVisible(bool state)
    {
        var newVal = state ? 1 : 0;
        if (_isVisible != -1 && newVal == _isVisible)
        {
            return;
        }

        _isVisible = newVal;

        if (shellRoot != null)
        {
            shellRoot.SetActive(state);
        }

        if (trailRoot != null)
        {
            if (state)
            {
                StartAllEffects(trailRoot);
            }
            else
            {
                StopAllEffects(trailRoot);
            }
        }

        if (thrustSound && state)
        {
            thrustSoundHandle = Game.soundSystem.Play(thrustSound, gameObject.transform);
        }
        else if (thrustSoundHandle.IsValid() && !state)
        {
            Game.soundSystem.Stop(thrustSoundHandle);
        }

        var lights = GetComponentsInChildren<Light>();
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i].enabled = state;
        }
    }

    public void SetMuzzlePosition(EntityManager entityManager, float3 muzzlePos)
    {
        if (ProjectileModuleClient.LogInfo.IntValue > 1)
        {
            GameDebug.Log("SetMuzzlePosition client projectile:" + name + " projectile:" + Projectile);
        }

        var projectileData = entityManager.GetComponentData<ProjectileData>(Projectile);

        var dir = Vector3.Normalize(projectileData.EndPos - projectileData.StartPos);
        var deltaPos = muzzlePos - projectileData.StartPos;
        var q = Quaternion.LookRotation(dir);
        var invQ = Quaternion.Inverse(q);

        StartOffset = invQ * deltaPos;
        OffsetScale = 1;
    }

    private void StopAllEffects(GameObject root)
    {
        if (root)
        {
            var effects = root.GetComponentsInChildren<VisualEffect>();
            for (int i = 0; i < effects.Length; i++)
            {
                effects[i].Stop();
            }

            var lights = root.GetComponentsInChildren<Light>();
            for (var i = 0; i < lights.Length; i++)
            {
                lights[i].enabled = false;
            }
        }
    }

    private void StartAllEffects(GameObject root)
    {
        if (root)
        {
            var effects = root.GetComponentsInChildren<VisualEffect>();
            for (var i = 0; i < effects.Length; i++)
            {
                effects[i].Play();
            }
        }
    }
}