using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public class CharacterDebugWindow : EditorWindow
{
    private static Character character;
    private static Character[] availableCharacters;


    class DamageInfo
    {
        public Vector3 aimPoint = Vector3.up;
        public Quaternion direction = Quaternion.identity;
        public float impulse;
        public float damage;
    }

    private static DamageInfo damageInfo = new DamageInfo(); 
    
    [MenuItem("FPS Sample/Windows/Character Debug")]
    public static void ShowWindow()
    {
        GetWindow<CharacterDebugWindow>(false, "Char Debug", true);

        ScanForCharacters();       
    }
    
    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += change => ScanForCharacters();
        SceneView.onSceneGUIDelegate += OnSceneGuiDelegate;
    }

    private void OnSceneGuiDelegate(SceneView sceneview)
    {
        if (character == null)
            return;

        var goe = character.GetComponent<GameObjectEntity>();
        var presentState = goe.EntityManager.GetComponentData<CharacterInterpolatedData>(goe.Entity);

        {
            var aimPointWorld = presentState.position + damageInfo.aimPoint;
            EditorGUI.BeginChangeCheck();
            var pos = Handles.PositionHandle(aimPointWorld,quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                damageInfo.aimPoint = pos - presentState.position;
            }
        }

        {
            var aimPointWorld = presentState.position + damageInfo.aimPoint;
            EditorGUI.BeginChangeCheck();
            var rot = Handles.RotationHandle(damageInfo.direction, aimPointWorld);
            if (EditorGUI.EndChangeCheck())
            {
                damageInfo.direction = rot;
            }
        }

        {
            var aimPointWorld = presentState.position + damageInfo.aimPoint;
            var damDir = damageInfo.direction * Vector3.forward;
            var damStart = aimPointWorld - damDir * 2;
            var damVector = damageInfo.direction*Vector3.forward * 100;    
            Debug.DrawLine(damStart, damStart + damVector, Color.red);
            DebugDraw.Sphere(damStart, 0.1f, Color.red);
        }        
    }

    static void ScanForCharacters()
    {
        availableCharacters = FindObjectsOfType<Character>();
        if(availableCharacters.Length > 0)
            character = availableCharacters[0];
    }
    
    void OnGUI()
    {
        GUILayout.Label("CHARACTER", EditorStyles.boldLabel);
        // Character selection
        if (GUILayout.Button("Scan for chars"))
        {
            ScanForCharacters();
        }

        if (availableCharacters != null)
        {
            var charNames = new string[availableCharacters.Length];
            var selectedindex = -1;
            for (var i = 0; i < availableCharacters.Length; i++)
            {
                charNames[i] = availableCharacters[i].name;
                if (availableCharacters[i] == character)
                    selectedindex = i;
            }

            selectedindex = EditorGUILayout.Popup("Char", selectedindex, charNames);
            if(selectedindex >= 0 && selectedindex < availableCharacters.Length)
                character = availableCharacters[selectedindex];
        }
        
        if (character == null)
        {
            GUILayout.Label("Please select character ...");
            return;
        }
        
        GUILayout.Label("DAMAGE", EditorStyles.boldLabel);
        
        // Give damage
        damageInfo.damage = EditorGUILayout.FloatField("damage", damageInfo.damage);
        damageInfo.impulse = EditorGUILayout.FloatField("impulse", damageInfo.impulse);
        damageInfo.aimPoint = EditorGUILayout.Vector3Field("aimPoint", damageInfo.aimPoint);
        damageInfo.direction.eulerAngles = EditorGUILayout.Vector3Field("dir", damageInfo.direction.eulerAngles);
        if (GUILayout.Button("Give Damage"))
        {
            
            var goe = character.GetComponent<GameObjectEntity>();
            var presentState = goe.EntityManager.GetComponentData<CharacterInterpolatedData>(goe.Entity);

            var aimPointWorld = presentState.position + damageInfo.aimPoint;
            var damDir = damageInfo.direction * Vector3.forward;
            var damStart = aimPointWorld - damDir * 2;
            
            var collisionMask = ~0U;
            var queryReciever = World.Active.GetExistingManager<RaySphereQueryReceiver>();
            var id = queryReciever.RegisterQuery(new RaySphereQueryReceiver.Query()
            {
                Origin = damStart,
                Direction = damDir,
                Distance = 1000,
                ExcludeOwner = Entity.Null,
                HitCollisionTestTick = 1,
                Radius = 0,
                Mask = collisionMask,
            });
        
            RaySphereQueryReceiver.Query query;
            RaySphereQueryReceiver.QueryResult queryResult;
            queryReciever.GetResult(id, out query, out queryResult);

            if (queryResult.Hit == 1)
            {
                var damageEventBuffer = goe.EntityManager.GetBuffer<DamageEvent>(queryResult.HitCollisionOwner);
                DamageEvent.AddEvent(damageEventBuffer, Entity.Null, damageInfo.damage,
                    damageInfo.direction * Vector3.forward, damageInfo.impulse);
            }
        }
    }
}
