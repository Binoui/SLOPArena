using SlopArena.Client.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow
{
    private Scene _previewScene;

    private void EnsureOwnedLab()
    {
        if (_lab != null || _waitingForAssemblyReload || EditorApplication.isCompiling ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        DestroyOwnedLab();
        _previewScene = EditorSceneManager.NewPreviewScene();
        _previewScene.name = "Ability Lab Preview";
        GameObject? root = null;
        try
        {
            root = new GameObject("AbilityLab") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, _previewScene);
            _lab = root.AddComponent<AbilityLab>();
            AddPreviewLight(root.transform, "Key Light", new Vector3(40f, -30f, 0f), Color.white, 1.2f);
            AddPreviewLight(root.transform, "Fill Light", new Vector3(325f, 150f, 0f), new Color(0.65f, 0.75f, 1f), 0.65f);
            root.SetActive(true);
            _workspace.BindPreviewLab(_lab);
            if (_grabSelected) _grabPriorShowHitboxes = _lab.ShowHitboxes;
        }
        catch
        {
            if (root != null) DestroyImmediate(root);
            DestroyOwnedLab();
            throw;
        }
    }

    private static void AddPreviewLight(Transform parent, string name, Vector3 rotation, Color color, float intensity)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.Euler(rotation);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
    }

    private void DestroyOwnedLab()
    {
        _workspace.BindPreviewLab(null);
        var lab = _lab;
        _lab = null;
        var scene = _previewScene;
        _previewScene = default;
        try
        {
            if (lab != null)
            {
                // Inactive dummy graphs must be released before their managed domain ends.
                try { lab.ReleasePreviewForReload(); }
                finally { DestroyImmediate(lab.gameObject); }
            }
        }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private void OnPreviewPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
        {
            ReleaseLabBeforeReload();
            ReleaseMovesViewportTarget();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            _waitingForAssemblyReload = false;
            _refreshAfterCompilation = true;
        }
    }
}
