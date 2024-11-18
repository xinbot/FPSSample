using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[AlwaysUpdateSystem]
[DisableAutoCreation]
public class HandleSpatialEffectRequests : BaseComponentSystem
{
    private struct SpatialEffectRequest
    {
        public SpatialEffectTypeDefinition EffectDef;
        public float3 Position;
        public quaternion Rotation;
    }

    private Collider[] _results = { };
    private readonly List<SpatialEffectRequest> _requests = new List<SpatialEffectRequest>(32);

    public HandleSpatialEffectRequests(GameWorld world) : base(world)
    {
    }

    public void Request(SpatialEffectTypeDefinition effectDef, float3 position, quaternion rotation)
    {
        _requests.Add(new SpatialEffectRequest
        {
            EffectDef = effectDef,
            Position = position,
            Rotation = rotation,
        });
    }

    protected override void OnUpdate()
    {
        for (var i = 0; i < _requests.Count; i++)
        {
            var request = _requests[i];

            if (request.EffectDef.effect != null)
            {
                var normal = math.mul(request.Rotation, new float3(0, 0, 1));

                var vfxSystem = World.GetExistingManager<VFXSystem>();
                vfxSystem.SpawnPointEffect(request.EffectDef.effect, request.Position, normal);
            }

            if (request.EffectDef.sound != null)
            {
                Game.soundSystem.Play(request.EffectDef.sound, request.Position);
            }

            if (request.EffectDef.shockwave.enabled)
            {
                var mask = 1 << LayerMask.NameToLayer("Debris");
                var explosionCenter = request.Position + (float3) UnityEngine.Random.insideUnitSphere * 0.2f;

                int count = Physics.OverlapSphereNonAlloc(request.Position, request.EffectDef.shockwave.radius,
                    _results, mask);
                for (var j = 0; j < count; j++)
                {
                    var rigidBody = _results[j].gameObject.GetComponent<Rigidbody>();
                    if (rigidBody != null)
                    {
                        rigidBody.AddExplosionForce(request.EffectDef.shockwave.force, explosionCenter,
                            request.EffectDef.shockwave.radius, request.EffectDef.shockwave.upwardsModifier,
                            request.EffectDef.shockwave.mode);
                    }
                }
            }

            /*
            var hdpipe = RenderPipelineManager.currentPipeline as UnityEngine.Experimental.Rendering.HDPipeline.HDRenderPipeline;
            if (hdpipe != null)
            {
                var matholder = GetComponent<DecalHolder>();
                if (matholder != null)
                {
                    var ds = UnityEngine.Experimental.Rendering.HDPipeline.DecalSystem.instance;
                    var go = new GameObject();
                    go.transform.rotation = effectEvent.rotation;
                    go.transform.position = effectEvent.position;
                    go.transform.Translate(-0.5f, 0, 0, Space.Self);
                    go.transform.up = go.transform.right;
                    var d = go.AddComponent<UnityEngine.Experimental.Rendering.HDPipeline.DecalProjectorComponent>();
                    d.m_Material = matholder.mat;
                    ds.AddDecal(d);
                }
            }
            */
        }

        _requests.Clear();
    }
}