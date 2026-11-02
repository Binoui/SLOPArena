using System.Linq;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.Replays;
using SlopArena.Client.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Generates Scenes/Replay.unity from Arena_Offline, so the Replay viewer uses the same camera, HUD, lights and
    /// fighter prefabs as local play. ReplayMatch replaces TrainingMatch and inherits its MatchBase references.
    /// </summary>
    public static class ReplaySceneBuilder
    {
        public const string SourcePath = "Assets/Scenes/Arena_Offline.unity";
        public const string ScenePath = "Assets/Scenes/" + ReplayLaunch.SceneName + ".unity";

        [MenuItem("Tools/SLOPArena/Replays/Rebuild Replay Scene")]
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Build the Replay scene in Edit Mode.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null && !AssetDatabase.DeleteAsset(ScenePath))
                throw new System.InvalidOperationException($"Could not replace {ScenePath}.");
            if (!AssetDatabase.CopyAsset(SourcePath, ScenePath))
                throw new System.InvalidOperationException($"Could not copy {SourcePath}.");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var training = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TrainingMatch>(true)).Single();
                var host = training.gameObject;
                var replay = host.AddComponent<ReplayMatch>();
                var from = new SerializedObject(training);
                var to = new SerializedObject(replay);
                // MatchBase's references carry over by name; TrainingMatch's NPC renderer becomes the second fighter.
                var property = to.GetIterator();
                for (bool enter = true; property.NextVisible(enter); enter = false)
                {
                    if (property.propertyPath == "m_Script") continue;
                    var source = from.FindProperty(property.propertyPath);
                    if (source != null) to.CopyFromSerializedProperty(source);
                }
                to.FindProperty("_fighterTemplate").objectReferenceValue = from.FindProperty("_npcRenderer").objectReferenceValue;
                to.FindProperty("_combatFeedback").objectReferenceValue = from.FindProperty("_combatFeedback").objectReferenceValue;
                to.ApplyModifiedPropertiesWithoutUndo();
                Object.DestroyImmediate(training);
                host.name = nameof(ReplayMatch);
                if (replay.GetComponent<CombatFeedback>() == null && to.FindProperty("_combatFeedback").objectReferenceValue == null)
                    host.AddComponent<CombatFeedback>();
                if (to.FindProperty("_playerRenderer").objectReferenceValue is not PlayerRenderer)
                    throw new System.InvalidOperationException("Arena_Offline has no player renderer for the Replay's first fighter.");
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                foreach (var settings in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/EditorBuildSettings.asset"))
                    AssetDatabase.SaveAssetIfDirty(settings);
            }
            return ScenePath;
        }
    }
}
