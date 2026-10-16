using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Animation;

/// <summary>
/// Editor tool: populate a CharacterAnimationConfig's AbilityClips list
/// by matching clip names against animation names in an animation folder.
///
/// Usage: Tools > SLOPArena > Legacy > Populate Ability Clips
///        or right-click a CharacterAnimationConfig asset → SLOPArena > Legacy > Populate Ability Clips
/// </summary>
public class PopulateAbilityClips : EditorWindow
{
    [SerializeField] private CharacterAnimationConfig _config;
    [SerializeField] private DefaultAsset _animFolder;

    [MenuItem("Tools/SLOPArena/Legacy/Populate Ability Clips")]
    public static void ShowWindow()
    {
        var w = GetWindow<PopulateAbilityClips>("Populate Ability Clips");
        w.minSize = new Vector2(400, 140);
    }

    [MenuItem("Assets/SLOPArena/Legacy/Populate Ability Clips", true)]
    private static bool ValidateSelection()
    {
        return Selection.activeObject is CharacterAnimationConfig;
    }

    [MenuItem("Assets/SLOPArena/Legacy/Populate Ability Clips")]
    private static void PopulateFromSelection()
    {
        var config = Selection.activeObject as CharacterAnimationConfig;
        if (config == null) return;

        string configPath = AssetDatabase.GetAssetPath(config);
        string configDir = System.IO.Path.GetDirectoryName(configPath);

        // Guess the anim folder: sibling "Animations" directory
        string animDir = System.IO.Path.Combine(configDir, "Animations");
        if (!AssetDatabase.IsValidFolder(animDir))
        {
            // Try Resources/{Class}_AnimConfig convention:
            // Resources/AnimationConfigs/ → ../../../Art/Characters/{class}/Animations/
            if (configDir.EndsWith("AnimationConfigs"))
            {
                string className = System.IO.Path.GetFileNameWithoutExtension(configPath);
                animDir = $"Assets/Art/Characters/{className.ToLowerInvariant()}/Animations";
                if (!AssetDatabase.IsValidFolder(animDir))
                    animDir = $"Assets/Art/Characters/{className}/Animations";
            }
        }

        if (!AssetDatabase.IsValidFolder(animDir))
        {
            EditorUtility.DisplayDialog("Error", $"Could not find Animations folder.\nTried:\n{animDir}", "OK");
            return;
        }

        Populate(config, AssetDatabase.LoadAssetAtPath<DefaultAsset>(animDir));
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Populate Ability Clips", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        _config = (CharacterAnimationConfig)EditorGUILayout.ObjectField("Anim Config", _config, typeof(CharacterAnimationConfig), false);
        _animFolder = (DefaultAsset)EditorGUILayout.ObjectField("Anim Folder", _animFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space(10);

        GUI.enabled = _config != null && _animFolder != null;
        if (GUILayout.Button("Populate", GUILayout.Height(30)))
        {
            Populate(_config, _animFolder);
        }
        GUI.enabled = true;

        EditorGUILayout.Space(5);
        if (GUILayout.Button("Auto-detect from config path"))
        {
            if (_config == null) { EditorUtility.DisplayDialog("Error", "Select an Anim Config first.", "OK"); return; }

            string configPath = AssetDatabase.GetAssetPath(_config);
            string configDir = System.IO.Path.GetDirectoryName(configPath);
            string animDir = System.IO.Path.Combine(configDir, "Animations");

            if (!AssetDatabase.IsValidFolder(animDir))
            {
                EditorUtility.DisplayDialog("Error", $"No Animations folder found at:\n{animDir}", "OK");
                return;
            }

            _animFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(animDir);
            Repaint();
        }
    }

    private static void Populate(CharacterAnimationConfig config, DefaultAsset folder)
    {
        string folderPath = AssetDatabase.GetAssetPath(folder);
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Error", $"Not a valid folder:\n{folderPath}", "OK");
            return;
        }

        // Find all AnimationClips in the folder
        var pool = new Dictionary<string, AnimationClip>(System.StringComparer.OrdinalIgnoreCase);

        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            // Direct .anim assets
            var direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
            if (direct != null)
                pool[System.IO.Path.GetFileNameWithoutExtension(p)] = direct;
            // Sub-assets from .fbx imports
            foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(p))
                if (sub is AnimationClip ac && !string.IsNullOrEmpty(ac.name))
                    pool[ac.name] = ac;
        }

        if (pool.Count == 0)
        {
            EditorUtility.DisplayDialog("No clips", $"No AnimationClips found in:\n{folderPath}", "OK");
            return;
        }

        // Validate first: an empty folder must not erase the existing bindings.
        Undo.RecordObject(config, "Populate legacy ability clips");
        config.AbilityClips.Clear();

        // Match by name — no hardcoded list, just add everything that matches a clip.
        // The clip names already match the AnimationNames in the ability specs.
        int added = 0;
        foreach (var kv in pool)
        {
            // Skip standard clip names (those use the named fields)
            if (IsStandardClip(kv.Key)) continue;
            config.AbilityClips.Add(new CharacterAnimationConfig.AbilityClipEntry
            {
                Name = kv.Key,
                Clip = kv.Value
            });
            added++;
        }

        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);

        Debug.Log($"[PopulateAbilityClips] {config.name}: added {added} clips from {folderPath}");
        EditorUtility.DisplayDialog("Done", $"{config.name}: {added} clips added to AbilityClips.", "OK");
    }

    private static bool IsStandardClip(string name)
    {
        return name switch
        {
            "idle" or "run" or "jump" or "fall" or "tumble" or "dash" or "death" => true,
            "hit_small" or "hit_light" or "hit_medium" or "hit_hard" => true,
            _ => false,
        };
    }
}
