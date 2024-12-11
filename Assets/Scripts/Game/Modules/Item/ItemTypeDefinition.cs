using UnityEngine;

[CreateAssetMenu(fileName = "ItemTypeDefinition", menuName = "FPS Sample/Item/TypeDefinition")]
public class ItemTypeDefinition : ScriptableObject
{
    public WeakAssetReference prefabServer;
    public WeakAssetReference prefabClient;
    public WeakAssetReference prefab1P;
}