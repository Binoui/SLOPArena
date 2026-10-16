#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools
{
    public static class SlopArenaArcadePhase3Commands
    {
        public const string ScenePath = "Assets/Scenes/Arcade_Slopball.unity";

        [MenuItem("Tools/SLOPArena/Arcade/Phase 3/Prepare menu and Slopball")]
        [CliCommand("sloparena.arcade.phase3.prepare", "Add the Arcade page to the existing frontend and include its playable Slopball scene in builds.",
            MainThreadRequired = true, Tags = new[] { "arcade", "ui" })]
        public static object Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Prepare Arcade in Edit Mode.");
            if (SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the current scene before preparing Arcade.");
            if (!File.Exists(SlopArenaSlopballCommands.ScenePath)
                || AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Stages/slopball.prefab") == null
                || !File.Exists(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "data/arenas/slopball.arena")))
            {
                var bake = SlopArenaSlopballCommands.Prepare();
                if (!bake.Success) throw new InvalidOperationException("Slopball preparation failed.");
            }
            if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset(SlopArenaSlopballCommands.ScenePath, ScenePath))
                throw new InvalidOperationException("Could not create the playable Slopball scene.");
            const string frontendPath = "Assets/Scenes/Frontend.unity";
            var scene = SceneManager.GetSceneByPath(frontendPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(frontendPath, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty) throw new InvalidOperationException("Save the frontend before preparing Arcade.");
                var shell = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<FrontendController>(true)).Single();
                var page = shell.transform.Find("ArcadePage")?.gameObject;
                if (page == null)
                {
                    page = new GameObject("ArcadePage"); page.SetActive(false);
                    SceneManager.MoveGameObjectToScene(page, scene);
                    page.transform.SetParent(shell.transform, false);
                    page.AddComponent<ArcadeSelectController>();
                }
                var serialized = new SerializedObject(shell);
                serialized.FindProperty("_arcadePage").objectReferenceValue = page;
                serialized.FindProperty("_arcadeFragment").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Arcade.uxml");
                if (serialized.FindProperty("_arcadeFragment").objectReferenceValue == null)
                    throw new InvalidOperationException("Arcade UI fragment has not imported.");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            var scenes = EditorBuildSettings.scenes.ToList();
            int index = scenes.FindIndex(x => x.path == ScenePath);
            if (index < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            else scenes[index].enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            return new { success = true, frontend = frontendPath, scene = ScenePath,
                scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray() };
        }

        [CliCommand("sloparena.arcade.status", "Read the active Slopball attempt and its captured local result.",
            MainThreadRequired = true, Tags = new[] { "arcade", "simulation" })]
        public static object Status()
        {
            var match = UnityEngine.Object.FindFirstObjectByType<SlopballMatch>();
            var state = match?.Simulation?.Slopball;
            return new { success = true, page = FrontendController.CurrentPage.ToString(),
                scene = SceneManager.GetActiveScene().path, ready = match?.Ready ?? false,
                tick = match?.SimulationTick, phase = state?.Phase.ToString(), countdownTicks = state?.CountdownTicks,
                returns = state?.Returns, savedBest = match?.SavedBest, recordUpdate = match?.LastRecordUpdate,
                bestStreak = state?.BestStreak, sessionSeed = state?.SessionSeed, shotIndex = state?.Shot.Index,
                recordsFile = ArcadeRecordsStore.Instance.Path, recordFeedback = ArcadeRecordsStore.Instance.Feedback };
        }
    }
}
#endif
