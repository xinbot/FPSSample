﻿using System;
using UnityEngine;
using UnityEngine.Experimental.VFX;

[CreateAssetMenu(fileName = "SpatialEffectTypeDefinition", menuName = "FPS Sample/Effect/SpatialEffectTypeDefinition")]
public class SpatialEffectTypeDefinition : ScriptableObject
{
    [Header("Visual Effect")] [Tooltip("Impact Effect template used by VFXImpactManager")]
    public VisualEffectAsset effect;

    public SoundDef sound;

    [Serializable]
    public class ShockwaveSettings
    {
        public bool enabled;
        public float force = 7;
        public float radius = 5;
        public float upwardsModifier;
        public ForceMode mode = ForceMode.Impulse;
    }

    public ShockwaveSettings shockwave;
}