using Unity.Entities;
using UnityEngine;

public class SpectatorCamControl : MonoBehaviour
{
}

[DisableAutoCreation]
public class UpdateSpectatorCamControl : BaseComponentSystem
{
    private ComponentGroup _group;

    public UpdateSpectatorCamControl(GameWorld world) : base(world)
    {
    }

    protected override void OnCreateManager()
    {
        base.OnCreateManager();
        _group = GetComponentGroup(typeof(LocalPlayer), typeof(PlayerCameraSettings), typeof(SpectatorCamControl));
    }

    protected override void OnUpdate()
    {
        var localPlayerArray = _group.GetComponentArray<LocalPlayer>();
        var cameraSettingsArray = _group.GetComponentArray<PlayerCameraSettings>();

        for (var i = 0; i < localPlayerArray.Length; i++)
        {
            var controlledEntity = localPlayerArray[i].controlledEntity;

            if (controlledEntity == Entity.Null || !EntityManager.HasComponent<SpectatorCamData>(controlledEntity))
            {
                continue;
            }

            var spectatorCam = EntityManager.GetComponentData<SpectatorCamData>(controlledEntity);
            var cameraSettings = cameraSettingsArray[i];
            cameraSettings.isEnabled = true;
            cameraSettings.position = spectatorCam.Position;
            cameraSettings.rotation = spectatorCam.Rotation;
        }
    }
}