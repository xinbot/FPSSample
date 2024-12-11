using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Playables;

public class ItemActionTimelineTrigger : MonoBehaviour
{
    [Serializable]
    public struct ActionTimelines
    {
        public CharacterPredictedData.Action action;
        public PlayableDirector director;
    }

    public ActionTimelines[] actionTimelines;

    public Dictionary<CharacterPredictedData.Action, PlayableDirector> m_actionTimelines =
        new Dictionary<CharacterPredictedData.Action, PlayableDirector>();

    public PlayableDirector currentActionTimeline;
    public CharacterPredictedData.Action prevAction;
    public int prevActionTick;

    private void Awake()
    {
        foreach (var map in actionTimelines)
        {
            if (map.director != null)
            {
                m_actionTimelines.Add(map.action, map.director);
            }
        }
    }
}

[DisableAutoCreation]
public class UpdateItemActionTimelineTrigger :
    BaseComponentSystem<CharacterPresentationSetup, ItemActionTimelineTrigger>
{
    public UpdateItemActionTimelineTrigger(GameWorld world) : base(world)
    {
    }

    protected override void Update(Entity entity, CharacterPresentationSetup charPresentation,
        ItemActionTimelineTrigger behavior)
    {
        if (!charPresentation.IsVisible)
        {
            return;
        }

        var animState = EntityManager.GetComponentData<CharacterInterpolatedData>(charPresentation.character);
        Update(behavior, animState);
    }

    public static void Update(ItemActionTimelineTrigger behavior, CharacterInterpolatedData animState)
    {
        var newAction = behavior.prevAction != animState.charAction;
        var newActionTick = behavior.prevActionTick != animState.charActionTick;
        if (newAction || newActionTick)
        {
            PlayableDirector director;
            if (behavior.m_actionTimelines.TryGetValue(animState.charAction, out director))
            {
                if (behavior.currentActionTimeline != null && director != behavior.currentActionTimeline)
                {
                    behavior.currentActionTimeline.Stop();
                }

                behavior.currentActionTimeline = director;
                behavior.currentActionTimeline.time = 0;

                behavior.currentActionTimeline.Play();
            }
        }

        behavior.prevAction = animState.charAction;
        behavior.prevActionTick = animState.charActionTick;
    }
}