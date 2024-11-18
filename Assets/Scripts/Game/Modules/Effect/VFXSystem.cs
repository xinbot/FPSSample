using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.VFX;

[DisableAutoCreation]
public class VFXSystem : ComponentSystem
{
    private static readonly int PositionID = Shader.PropertyToID("position");
    private static readonly int TargetPositionID = Shader.PropertyToID("targetPosition");
    private static readonly int DirectionID = Shader.PropertyToID("direction");

    private class EffectTypeData
    {
        // TODO (mogensh) For performance reasons we want to stop effects that are "done". For now all effect use same timeout duration.  
        public const float MaxDuration = 4.0f;
        public bool Active;
        public float LastTriggerTime;

        public VisualEffect VisualEffect;
        public VFXEventAttribute EventAttribute;
    }

    private struct PointEffectRequest
    {
        public float3 Position;
        public float3 Normal;
        public VisualEffectAsset Asset;
    }

    private struct LineEffectRequest
    {
        public float3 Start;
        public float3 End;
        public VisualEffectAsset Asset;
    }

    private GameObject _rootGameObject;
    private readonly List<PointEffectRequest> _pointEffectRequests = new List<PointEffectRequest>(32);
    private readonly List<LineEffectRequest> _lineEffectRequests = new List<LineEffectRequest>(32);

    private readonly Dictionary<VisualEffectAsset, EffectTypeData> _effectTypeData =
        new Dictionary<VisualEffectAsset, EffectTypeData>(32);

    protected override void OnCreateManager()
    {
        base.OnCreateManager();

        _rootGameObject = new GameObject("VFXSystem");
        _rootGameObject.transform.position = Vector3.zero;
        _rootGameObject.transform.rotation = Quaternion.identity;
        Object.DontDestroyOnLoad(_rootGameObject);
    }

    protected override void OnDestroyManager()
    {
        base.OnDestroyManager();

        foreach (var effectType in _effectTypeData.Values)
        {
            effectType.VisualEffect.Reinit();
        }
    }

    public void SpawnPointEffect(VisualEffectAsset asset, float3 position, float3 normal)
    {
        _pointEffectRequests.Add(new PointEffectRequest
        {
            Asset = asset,
            Position = position,
            Normal = normal,
        });
    }

    public void SpawnLineEffect(VisualEffectAsset asset, float3 start, float3 end)
    {
        _lineEffectRequests.Add(new LineEffectRequest
        {
            Asset = asset,
            Start = start,
            End = end,
        });
    }

    protected override void OnUpdate()
    {
        // Handle request
        foreach (var request in _pointEffectRequests)
        {
            EffectTypeData effectType;
            if (!_effectTypeData.TryGetValue(request.Asset, out effectType))
            {
                effectType = RegisterImpactType(request.Asset);
            }

            // GameDebug.Log("Spawn effect:" + effectType.visualEffect.name + " pos:" + request.position);

            effectType.EventAttribute.SetVector3(PositionID, request.Position);
            effectType.EventAttribute.SetVector3(DirectionID, request.Normal);
            effectType.VisualEffect.Play(effectType.EventAttribute);
            effectType.VisualEffect.pause = false;
            effectType.LastTriggerTime = (float) Game.FrameTime;
            effectType.Active = true;
        }

        _pointEffectRequests.Clear();

        foreach (var request in _lineEffectRequests)
        {
            EffectTypeData effectType;
            if (!_effectTypeData.TryGetValue(request.Asset, out effectType))
            {
                effectType = RegisterImpactType(request.Asset);
            }

            // GameDebug.Log("Spawn effect:" + effectType.visualEffect.name + " start:" + request.start);

            effectType.EventAttribute.SetVector3(PositionID, request.Start);
            effectType.EventAttribute.SetVector3(TargetPositionID, request.End);
            effectType.VisualEffect.Play(effectType.EventAttribute);
            effectType.VisualEffect.pause = false;
            effectType.LastTriggerTime = (float) Game.FrameTime;
            effectType.Active = true;
        }

        _lineEffectRequests.Clear();

        foreach (var effectTypeData in _effectTypeData.Values)
        {
            var isAlive = (float) Game.FrameTime > effectTypeData.LastTriggerTime + EffectTypeData.MaxDuration;
            if (effectTypeData.Active && isAlive)
            {
                effectTypeData.VisualEffect.pause = true;
                effectTypeData.Active = false;
            }
        }
    }

    private EffectTypeData RegisterImpactType(VisualEffectAsset template)
    {
        GameDebug.Assert(!_effectTypeData.ContainsKey(template));
        GameDebug.Assert(template != null);

        var go = new GameObject(template.name);
        go.transform.parent = _rootGameObject.transform;
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var vfx = go.AddComponent<VisualEffect>();
        vfx.visualEffectAsset = template;
        vfx.Reinit();
        vfx.Stop();

        var data = new EffectTypeData
        {
            VisualEffect = vfx,
            EventAttribute = vfx.CreateVFXEventAttribute(),
        };

        _effectTypeData.Add(template, data);

        return data;
    }
}