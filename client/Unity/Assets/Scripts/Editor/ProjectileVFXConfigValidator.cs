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

        [MenuItem("Tools/SlopArena/Diagnostics/Validate Projectile VFX")]
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
            for (int i = 0; i < config.ProjectileEntries.Length; i++)
            {
                var entry = config.ProjectileEntries[i];
                valid &= ValidatePrefab(
                    entry.Prefab,
                    $"ProjectileEntries[{i}] ({entry.Character}, slot {entry.AttackSlot}, airborne={entry.Airborne})");
            }

            valid &= ValidatePrefab(config.ExplosionPrefab, "ExplosionPrefab");
            for (int i = 0; i < config.ExplosionOverrides.Length; i++)
            {
                var entry = config.ExplosionOverrides[i];
                valid &= ValidatePrefab(
                    entry.Prefab,
                    $"ExplosionOverrides[{i}] ({entry.Character}, slot {entry.AttackSlot})");
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
