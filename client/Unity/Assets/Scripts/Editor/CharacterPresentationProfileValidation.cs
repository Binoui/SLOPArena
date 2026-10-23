using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class CharacterPresentationProfileValidation
{
    internal static void Validate(CharacterAssetCatalog catalog, List<CharacterDiagnostic> diagnostics, bool instantiatePrefabs = false)
    {
        try
        {
            // Validate the same metadata which publishing will emit, before doing a pose bake.
            diagnostics.AddRange(CharacterPackageAssembler.ValidateClientBindings(CharacterBindingWriter.Write(
                catalog, Array.Empty<CharacterCookAnimationDefinition>(), new string('0', 64)))
                .Where(d => d.Code != "package.binding.media-id" || d.Path != "presentationProfile.projectiles.prefabGlobalObjectId"));
            if (catalog.PresentationProfile == null) return;
            string packageRoot = "Assets/CharacterPackages/" + catalog.PackageId;
            using (var serialized = new SerializedObject(catalog))
            {
                serialized.Update();
                var profileProperty = serialized.FindProperty("_presentationProfile");
                foreach (string poolName in new[] { "_swing", "_lightImpact", "_mediumImpact", "_heavyImpact" })
                {
                    var clips = profileProperty.FindPropertyRelative(poolName)?.FindPropertyRelative("_clips");
                    if (clips == null) continue;
                    for (int i = 0; i < clips.arraySize; i++)
                    {
                        var clip = clips.GetArrayElementAtIndex(i);
                        if (clip.objectReferenceValue == null && clip.objectReferenceInstanceIDValue != 0)
                            diagnostics.Add(UnityCharacterAssetCooker.Error("asset-catalog.presentation.missing", poolName + "[" + i + "]", "Authored audio reference is not installed."));
                    }
                }
            }
            var prefabs = new HashSet<GameObject>();
            foreach (var binding in catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                if (binding?.Prefab != null) prefabs.Add(binding.Prefab);
            foreach (var media in CharacterBindingWriter.ProfileMedia(catalog.PresentationProfile))
            {
                if (media.Asset == null)
                {
                    if (!string.IsNullOrEmpty(media.ObjectId))
                        diagnostics.Add(UnityCharacterAssetCooker.Error("asset-catalog.presentation.missing", media.Identity, "Referenced presentation media is not installed."));
                    continue; // Empty audio positions are authored silence.
                }
                string path = AssetDatabase.GetAssetPath(media.Asset);
                if (!EditorUtility.IsPersistent(media.Asset) || string.IsNullOrEmpty(path))
                    diagnostics.Add(UnityCharacterAssetCooker.Error("asset-catalog.presentation.unresolved", media.Identity, "Presentation media must have a stable asset identity."));
                ValidateOwnership(path, media.Identity);
                if (media.Asset is GameObject prefab) prefabs.Add(prefab);
            }
            foreach (var row in catalog.PresentationProfile.Projectiles)
            {
                if (row.Prefab == null)
                    diagnostics.Add(UnityCharacterAssetCooker.Error("asset-catalog.presentation.missing", row.SlotId, "Projectile prefab is missing."));
                if (!string.IsNullOrEmpty(row.HeldBoneId) &&
                    (catalog.Rig == null || !catalog.Rig.GetComponentsInChildren<Transform>(true)
                        .Any(t => t.name == PlayerRenderer.PresentationBoneName(row.HeldBoneId))))
                    diagnostics.Add(UnityCharacterAssetCooker.Error("asset-catalog.rig.incompatible", row.SlotId, "Held projectile bone is not present on the rig: " + row.HeldBoneId));
            }
            foreach (var prefab in prefabs)
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!PrefabUtility.IsPartOfPrefabAsset(prefab))
                    diagnostics.Add(UnityCharacterAssetCooker.Error("asset-catalog.presentation.invalid", path, "Presentation GameObjects must be prefab assets."));
                else if (instantiatePrefabs)
                {
                    var scene = EditorSceneManager.NewPreviewScene();
                    try
                    {
                        var clone = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                        if (clone == null) throw new InvalidOperationException("Prefab could not be instantiated.");
                        UnityEngine.Object.DestroyImmediate(clone);
                    }
                    finally { EditorSceneManager.ClosePreviewScene(scene); }
                }
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                    if (AssetDatabase.GetMainAssetTypeAtPath(dependency) == typeof(AudioClip))
                        ValidateOwnership(dependency, path);
            }
            void ValidateOwnership(string path, string field)
            {
                var classification = CharacterPackageAuthoringService.ClassifyDependency(packageRoot, path);
                if (classification.Classification is not ("package" or "shared-approved"))
                    diagnostics.Add(UnityCharacterAssetCooker.Error("asset-catalog.dependency.foreign", field, "Presentation media is not package-owned or explicitly approved: " + path));
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(UnityCharacterAssetCooker.Error("asset-catalog.presentation.invalid", "catalog.presentationProfile", ex.Message));
        }
    }
}
