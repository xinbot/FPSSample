using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

[AlwaysUpdateSystem]
[DisableAutoCreation]
public class HandleHitScanEffectRequests : BaseComponentSystem
{
    private struct HitScanEffectRequest
    {
        public HitscanEffectTypeDefinition EffectDef;
        public Vector3 StartPos;
        public Vector3 EndPos;
    }

    private readonly List<HitScanEffectRequest> _requests = new List<HitScanEffectRequest>(32);

    public void Request(HitscanEffectTypeDefinition effectDef, Vector3 startPos, Vector3 endPos)
    {
        _requests.Add(new HitScanEffectRequest
        {
            EffectDef = effectDef,
            StartPos = startPos,
            EndPos = endPos,
        });
    }

    public HandleHitScanEffectRequests(GameWorld world) : base(world)
    {
    }

    protected override void OnUpdate()
    {
        for (var i = 0; i < _requests.Count; i++)
        {
            var request = _requests[i];
            if (request.EffectDef.effect == null)
            {
                continue;
            }

            var vfxSystem = World.GetExistingManager<VFXSystem>();
            vfxSystem.SpawnLineEffect(request.EffectDef.effect, request.StartPos, request.EndPos);
        }

        _requests.Clear();
    }
}