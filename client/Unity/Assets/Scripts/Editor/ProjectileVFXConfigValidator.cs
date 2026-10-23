using System;
using UnityEditor;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    [InitializeOnLoad]
    internal static class ProjectileVFXConfigValidator
    {
        static ProjectileVFXConfigValidator()
            => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        [MenuItem("Tools/SLOPArena/Diagnostics/Validate Projectile VFX")]
        private static void ValidateFromMenu()
            => Validate(false);

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                Validate(true);
        }

        private static bool Validate(bool blockPlayMode)
        {
            var config = Resources.Load<ProjectileVFXConfig>("VFXConfigs/ProjectileVisuals");
            if (config == null)
                return true;

            bool valid = true;
            valid &= ValidatePrefab(config.ExplosionPrefab, "ExplosionPrefab");
            foreach (string guid in AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" }))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                var diagnostics = new System.Collections.Generic.List<SlopArena.Shared.CharacterDiagnostic>();
                CharacterPresentationProfileValidation.Validate(catalog, diagnostics);
                foreach (var diagnostic in diagnostics)
                    if (diagnostic.Severity == SlopArena.Shared.CharacterDiagnosticSeverity.Error)
                    {
                        valid = false;
                        Debug.LogError($"[ProjectileVFX] {catalog.PackageId}: {diagnostic.Path}: {diagnostic.Message}");
                    }
            }

            if (!valid && blockPlayMode)
            {
                EditorApplication.isPlaying = false;
                Debug.LogError("[ProjectileVFX] Invalid prefab references. Play Mode was blocked.");
            }

            return valid;
        }

        private static bool ValidatePrefab(GameObject prefab, string label)
        {
            if (prefab == null)
            {
                Debug.LogError($"[ProjectileVFX] {label} has no prefab reference.");
                return false;
            }

            try
            {
                var clone = UnityEngine.Object.Instantiate(prefab);
                UnityEngine.Object.DestroyImmediate(clone);
                return true;
            }
            catch (InvalidCastException)
            {
                Debug.LogError($"[ProjectileVFX] {label} references '{prefab.name}', which cannot be instantiated as a GameObject.");
                return false;
            }
        }
    }
}
