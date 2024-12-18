﻿using System;
using UnityEngine;

public class GrenadeClient : MonoBehaviour
{
    public GameObject geometry;
    public SpatialEffectTypeDefinition explodeEffect;
    public SoundDef bounceSound;

    [NonSerialized] public bool Exploded;
    [NonSerialized] public int BounceTick;
}