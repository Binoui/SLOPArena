using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using SlopArena.Client.World;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;
using Unity.Cinemachine;
using SlopArena.Shared;

namespace SlopArena.Client.Editor
{
    public static class SlopArenaSceneSetup
    {
        [MenuItem("Tools/SLOPArena/Prototypes/Rebuild Offline Scene")]
        public static void CreateOfflineScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            scene.name = "Arena_Offline";

            // Remove default camera
            var defaultCamera = GameObject.Find("Main Camera");
            if (defaultCamera != null) Object.DestroyImmediate(defaultCamera);

            // ── 1. Directional Light ──
            var lightGO = new GameObject("Directional Light", typeof(Light));
            lightGO.GetComponent<Light>().type = LightType.Directional;
            lightGO.GetComponent<Light>().intensity = 1.5f;
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
            if (lightGO.GetComponent<UniversalAdditionalLightData>() == null)
                lightGO.AddComponent<UniversalAdditionalLightData>();

            // ── 2. CameraMount ──
            var camMountGO = new GameObject("CameraMount");
            var cmCam = camMountGO.AddComponent<CinemachineCamera>();
            var orbital = camMountGO.AddComponent<CinemachineOrbitalFollow>();
            orbital.VerticalAxis.Range = new Vector2(10f, 30f);
            orbital.RadialAxis.Range = new Vector2(0.3f, 1.5f);
            var camMount = camMountGO.AddComponent<CameraMount>();
            var camGO = new GameObject("Main Camera", typeof(UnityEngine.Camera), typeof(AudioListener));
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(camMountGO.transform);
            camGO.transform.localPosition = Vector3.zero;
            camGO.transform.localRotation = Quaternion.identity;
            camGO.AddComponent<CinemachineBrain>();

            // ── 4. Player (from prefab) ──
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            GameObject playerGO;
            PlayerRenderer playerRenderer;
            InputController inputCtrl;
            if (playerPrefab != null)
            {
                playerGO = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                playerGO.name = "Player";
                playerRenderer = playerGO.GetComponent<PlayerRenderer>();
                inputCtrl = playerGO.GetComponent<InputController>();
            }
            else
            {
                playerGO = new GameObject("Player");
                playerRenderer = playerGO.AddComponent<PlayerRenderer>();
                inputCtrl = playerGO.AddComponent<InputController>();
            }
            playerGO.transform.position = new Vector3(0, 5, 5);
            camMount.SetTarget(playerGO.transform);

            // ── 5. Training Dummy (from prefab) ──
            var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Dummy.prefab");
            GameObject dummyGO;
            PlayerRenderer dummyRenderer;
            if (dummyPrefab != null)
            {
                dummyGO = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                dummyGO.name = "TrainingDummy";
                dummyRenderer = dummyGO.GetComponent<PlayerRenderer>();
            }
            else
            {
                dummyGO = new GameObject("TrainingDummy");
                dummyRenderer = dummyGO.AddComponent<PlayerRenderer>();
            }
            dummyGO.transform.position = new Vector3(0, 5, -5);
            dummyGO.transform.rotation = Quaternion.Euler(0, 180, 0);


            // ── 6. TrainingMatch ──
            var matchGO = new GameObject("TrainingMatch");
            var match = matchGO.AddComponent<TrainingMatch>();
            var matchSo = new SerializedObject(match);
            matchSo.FindProperty("_playerRenderer").objectReferenceValue = playerRenderer;
            matchSo.FindProperty("_npcRenderer").objectReferenceValue = dummyRenderer;
            matchSo.FindProperty("_inputController").objectReferenceValue = inputCtrl;
            matchSo.FindProperty("_cameraMount").objectReferenceValue = camMount;
            matchSo.ApplyModifiedProperties();

            // ── 7. GameManager (singleton) ──
            var gmGO = new GameObject("GameManager");
            gmGO.AddComponent<GameManager>();

            // ── Save ──
            string path = "Assets/Scenes/Arena_Offline.unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[SLOPArena] Offline scene created at {path}");

            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (asset != null)
                EditorGUIUtility.PingObject(asset);
        }
    }
}
