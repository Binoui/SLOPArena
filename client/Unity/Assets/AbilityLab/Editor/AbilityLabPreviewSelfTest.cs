using System;
using System.Linq;
using System.Reflection;
using SlopArena.Client.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools;

public static class AbilityLabPreviewSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Ability Lab Preview Isolation")]
    public static void Run() => Debug.Log(RunIsolation());

    public static string RunIsolation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Preview isolation checks require Edit Mode.");
        string scenes = SceneSnapshot();
        var selection = Selection.activeObject;
        var singleton = AbilityLab.Instance;
        int previewCount = EditorSceneManager.previewSceneCount;
        AbilityLabWindow? window = null;
        AbilityLabWindow? other = null;
        try
        {
            for (int cycle = 0; cycle < 2; cycle++)
            {
                window = ScriptableObject.CreateInstance<AbilityLabWindow>();
                window.hideFlags = HideFlags.HideAndDontSave;
                window.CreateGUI();
                var lab = window.CommandLab;
                Require(lab != null && EditorSceneManager.IsPreviewSceneObject(lab.gameObject), "Window did not create a private preview.");
                var scene = lab!.gameObject.scene;
                Require(lab.PreviewCamera != null && lab.PreviewCamera.scene == scene &&
                    !lab.PreviewCamera.enabled && lab.GetComponentsInChildren<AudioListener>(true).Length == 0,
                    "Preview borrowed a camera or created a live camera/listener.");
                Require(lab.GetComponentsInChildren<Light>(true).Length == 2, "Preview lighting is unavailable.");
                Require(EditorSceneManager.previewSceneCount == previewCount + 1, "Opening created duplicate preview scenes.");
                Require(AbilityLab.Instance == singleton && SceneSnapshot() == scenes && Selection.activeObject == selection,
                    "Opening changed the scene, selection, camera, lighting or scene Lab singleton.");
                window.CreateGUI();
                Require(window.EnsureLabForCommand(out var reused, out bool created) && !created && reused == lab,
                    "Opening/ensuring the same window duplicated its rig.");

                var actor = lab.Renderer;
                var dummy = lab.DummyRenderer;
                var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
                foreach (string package in new[] { "bonk", "fightguy", "bonk" })
                {
                    Require(window.OpenPackageForCommand(service.Inspect(package), out _, out string error), error);
                    Require(lab.IsPackagePreview && lab.SelectedPackageId == package, $"Package preview is unavailable: {package}.");
                    Require(lab.Renderer == actor && lab.DummyRenderer == dummy &&
                        actor.GetComponentsInChildren<Animator>(true).Count(animator => animator.isHuman) == 1,
                        $"Switching to {package} accumulated models or replaced the stable renderer roots: " +
                        $"actor={actor.GetInstanceID()}/{lab.Renderer.GetInstanceID()}, dummy={dummy.GetInstanceID()}/{lab.DummyRenderer.GetInstanceID()}, " +
                        $"children={string.Join(",", actor.GetComponentsInChildren<Transform>(true).Where(t => t.parent == actor.transform).Select(t => t.name))}.");
                    Require(lab.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.scene == scene),
                        "A preview object escaped into an ordinary scene.");
                    Require(SceneSnapshot() == scenes && AbilityLab.Instance == singleton, "Switching packages changed another scene.");
                }

                other = ScriptableObject.CreateInstance<AbilityLabWindow>();
                other.hideFlags = HideFlags.HideAndDontSave;
                other.CreateGUI();
                Require(other.CommandLab != lab && other.CommandLab!.gameObject.scene != scene, "Two windows share a rig.");
                var draft = window.CommandWorkspace.Draft;
                var otherWorkspace = other.CommandWorkspace;
                otherWorkspace.ReplaceGeneral(otherWorkspace.Draft.DisplayName, otherWorkspace.Draft.Weight + 1f,
                    otherWorkspace.Draft.CapsuleRadius, otherWorkspace.Draft.CapsuleHeight,
                    otherWorkspace.Draft.HipHeight, otherWorkspace.Draft.HurtboxRadius);
                Require(ReferenceEquals(window.CommandWorkspace.Draft, draft) && lab.SelectedPackageId == "bonk",
                    "Another workspace's edits reached this preview.");
                UnityEngine.Object.DestroyImmediate(other);
                other = null;

                // Exercise the actual release/rebuild paths without saving or cooking.
                var objects = lab.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).ToArray();
                Invoke(window, "ReleaseLabBeforeReload");
                Require(window.CommandLab == null && !scene.IsValid() && objects.All(o => o == null) &&
                    EditorSceneManager.previewSceneCount == previewCount, "Reload release left preview objects or a scene alive.");
                typeof(AbilityLabWindow).GetField("_waitingForAssemblyReload", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, false);
                Invoke(window, "Update");
                lab = window.CommandLab;
                Require(lab != null && lab.IsPackagePreview && lab.SelectedPackageId == "bonk" &&
                    ReferenceEquals(window.CommandWorkspace.Draft, draft), "Rebuilding lost the preview or draft.");
                scene = lab!.gameObject.scene;
                objects = lab.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).ToArray();
                UnityEngine.Object.DestroyImmediate(window);
                window = null;
                Require(!scene.IsValid() && objects.All(o => o == null) && EditorSceneManager.previewSceneCount == previewCount,
                    "Closing left preview objects or a scene alive.");
                Require(SceneSnapshot() == scenes && AbilityLab.Instance == singleton && Selection.activeObject == selection,
                    "Closing changed the open scene or selection.");
            }
            return "PASS: automatic private preview; own camera/lights; stable roots across package switches; independent workspaces; repeat open/close and reload cleanup; open scenes, cameras, lights and selection unchanged.";
        }
        finally
        {
            if (other != null) UnityEngine.Object.DestroyImmediate(other);
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static string SceneSnapshot()
        => string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(index =>
        {
            var scene = SceneManager.GetSceneAt(index);
            var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            return $"{scene.handle}|{scene.path}|{scene.isDirty}|{SceneManager.GetActiveScene() == scene}|" +
                string.Join(",", objects.Select(t => EditorJsonUtility.ToJson(t))) + "|" +
                string.Join(",", objects.SelectMany(t => t.GetComponents<Camera>()).Select(camera => EditorJsonUtility.ToJson(camera))) + "|" +
                string.Join(",", objects.SelectMany(t => t.GetComponents<Light>()).Select(light => EditorJsonUtility.ToJson(light)));
        }));

    private static void Invoke(AbilityLabWindow window, string name)
        => typeof(AbilityLabWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
