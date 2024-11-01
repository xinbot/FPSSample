using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ReplicatedEntity))]
public class ReplicatedEntityEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        var replicatedEntity = target as ReplicatedEntity;
        if (replicatedEntity)
        {
            GUILayout.Label("GUID:" + replicatedEntity.Value.assetGuid.GetGuidStr());
        }
    }
}