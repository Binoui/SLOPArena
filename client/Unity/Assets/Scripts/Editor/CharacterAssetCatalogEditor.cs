using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Shared;
public sealed class CharacterAssetCatalogEditor : EditorWindow
{
    private CharacterAssetCatalog _catalog;
    private Vector2 _scroll;
    private List<CharacterDiagnostic> _diagnostics = new List<CharacterDiagnostic>();
    private CharacterCookStatus _status;

    [MenuItem("Tools/SLOPArena/Authoring/Character Asset Catalog")]
    public static void ShowWindow() => GetWindow<CharacterAssetCatalogEditor>("Character Asset Catalog");

    private void OnEnable() { }

    private void OnGUI()
    {
        _catalog = (CharacterAssetCatalog)EditorGUILayout.ObjectField("Catalog", _catalog, typeof(CharacterAssetCatalog), false);
        if (_catalog == null)
        {
            EditorGUILayout.HelpBox("Select a CharacterAssetCatalog asset.", MessageType.Info);
            return;
        }
        using (var serialized = new SerializedObject(_catalog))
        {
            serialized.Update();
            EditorGUILayout.PropertyField(serialized.FindProperty("_packageId"));
            EditorGUILayout.PropertyField(serialized.FindProperty("_catalogSchemaVersion"));
            EditorGUILayout.PropertyField(serialized.FindProperty("_weaponConfig"));
            EditorGUILayout.PropertyField(serialized.FindProperty("_bindings"), true);
            EditorGUILayout.PropertyField(serialized.FindProperty("_presentations"), true);
            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(_catalog);
                AssetDatabase.SaveAssets();
            }
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Sync IDs")) SyncIds();
            if (GUILayout.Button("Validate")) Validate();
            EditorGUI.BeginDisabledGroup(_diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error));
            if (GUILayout.Button("Cook"))
            {
                string packageRoot = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_catalog)).Replace('\\', '/');
                var result = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook(packageRoot);
                _diagnostics = result.RawDiagnostics.ToList();
            }
            EditorGUI.EndDisabledGroup();
        }
        _status = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).ReadStatus(_catalog.PackageId);
        EditorGUILayout.LabelField("Status", _status.State);
        EditorGUILayout.LabelField("Current source hash", _status.CurrentSourceHash);
        EditorGUILayout.LabelField("Cooked source hash", _status.CookedSourceHash);
        EditorGUILayout.LabelField("Cooked content hash", _status.CookedContentHash);
        EditorGUILayout.LabelField("Package hash", _status.PackageHash);
        if (_status.State == "Stale" || _status.State == "Failed")
            EditorGUILayout.HelpBox($"{_catalog.PackageId} package status: {_status.State}. Recook before shipping.", _status.State == "Failed" ? MessageType.Error : MessageType.Warning);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var diagnostic in _diagnostics)
            EditorGUILayout.HelpBox($"{diagnostic.Code} [{diagnostic.Path}] {diagnostic.Message}", MessageType.Error);
        if (_status.Diagnostics != null)
            foreach (var diagnostic in _status.Diagnostics)
                EditorGUILayout.HelpBox($"{diagnostic.Code} [{diagnostic.Path}] {diagnostic.Message}", diagnostic.Severity == "error" ? MessageType.Error : MessageType.Warning);
        EditorGUILayout.EndScrollView();
    }

    private void SyncIds()
    {
        string packagePath = AssetDatabase.GetAssetPath(_catalog);
        string packageRoot = Path.GetDirectoryName(packagePath).Replace('\\', '/');
        string characterPath = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), packageRoot, "character.json");
        string manifestPath = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), packageRoot, "package.json");
        if (!File.Exists(characterPath) || !File.Exists(manifestPath))
        {
            _diagnostics = new List<CharacterDiagnostic>
            {
                UnityCharacterAssetCooker.Error("asset-catalog.schema", packageRoot, "Package JSON files are missing beside the catalog.")
            };
            return;
        }
        var profile = CharacterPackageAuthoringService.ProfileFor(_catalog.PackageId);
        CharacterCompileResult compiled = CharacterPackageCompiler.Compile(
            File.ReadAllText(manifestPath), File.ReadAllText(characterPath), profile);
        _diagnostics = compiled.Diagnostics.ToList();
        if (compiled.CookedPackage == null) return;
        var required = UnityCharacterAssetCooker.GetRequiredIds(compiled.CookedPackage);
        var existing = new Dictionary<string, CharacterAssetCatalog.AnimationBinding>(StringComparer.Ordinal);
        foreach (var binding in _catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            if (binding != null && !existing.ContainsKey(binding.SemanticId ?? ""))
                existing.Add(binding.SemanticId ?? "", binding);
        var bindings = new List<CharacterAssetCatalog.AnimationBinding>();
        foreach (var entry in required)
        {
            if (!existing.TryGetValue(entry.Id, out var binding))
                binding = new CharacterAssetCatalog.AnimationBinding { SemanticId = entry.Id, PoseTrackId = entry.Id };
            bindings.Add(binding);
        }
        foreach (var binding in _catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            if (binding != null && !required.Any(x => x.Id == binding.SemanticId)) bindings.Add(binding);
        Undo.RecordObject(_catalog, "Synchronize character catalog IDs");
        _catalog.Bindings = bindings.ToArray();
        EditorUtility.SetDirty(_catalog);
        AssetDatabase.SaveAssets();
    }

    private void Validate()
    {
        string packageRoot = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_catalog)).Replace('\\', '/');
        var result = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(packageRoot);
        _diagnostics = result.RawDiagnostics.ToList();
        Repaint();
    }
}
