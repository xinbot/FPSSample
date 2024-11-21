using UnityEngine;

[CreateAssetMenu(fileName = "SpectatorCamSettings", menuName = "FPS Sample/SpectatorCam/SpectatorCamSettings")]
public class SpectatorCamSettings : ScriptableObject
{
    public WeakAssetReference spectatorCamPrefab;
}