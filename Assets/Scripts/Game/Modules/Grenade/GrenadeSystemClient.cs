using UnityEngine;
using Unity.Entities;

[DisableAutoCreation]
public class ApplyGrenadePresentation : BaseComponentSystem
{
    private ComponentGroup _group;

    public ApplyGrenadePresentation(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(GrenadeClient), typeof(PresentationEntity),
            ComponentType.Subtractive<DespawningEntity>());
    }

    protected override void OnUpdate()
    {
        var grenadeClientArray = _group.GetComponentArray<GrenadeClient>();
        var presentationArray = _group.GetComponentArray<PresentationEntity>();

        for (var i = 0; i < grenadeClientArray.Length; i++)
        {
            var grenadeClient = grenadeClientArray[i];
            var presentation = presentationArray[i];
            if (!EntityManager.Exists(presentation.ownerEntity))
            {
                GameDebug.LogError("ApplyGrenadePresentation. Entity does not exist;" + presentation.ownerEntity);
                continue;
            }

            var interpolatedState = EntityManager.GetComponentData<Grenade.InterpolatedState>(presentation.ownerEntity);

            grenadeClient.transform.position = interpolatedState.position;

            if (interpolatedState.bouncetick > grenadeClient.BounceTick)
            {
                grenadeClient.BounceTick = interpolatedState.bouncetick;
                Game.soundSystem.Play(grenadeClient.bounceSound, interpolatedState.position);
            }

            if (interpolatedState.exploded == 1 && !grenadeClient.Exploded)
            {
                grenadeClient.Exploded = true;

                grenadeClient.geometry.SetActive(false);

                if (grenadeClient.explodeEffect != null)
                {
                    World.GetExistingManager<HandleSpatialEffectRequests>().Request(grenadeClient.explodeEffect,
                        interpolatedState.position, Quaternion.identity);
                }
            }
        }
    }
}