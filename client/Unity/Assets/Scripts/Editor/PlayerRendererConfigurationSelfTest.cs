using System;
using System.Linq;
using Animancer;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class PlayerRendererConfigurationSelfTest
    {
        [MenuItem("Tools/SLOPArena/Tests/Player Renderer Configuration")]
        public static void Run()
        {
            Require(!Application.isPlaying, "Run renderer configuration checks in Edit Mode.");
            var source = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(
                "Assets/CharacterPackages/manki/CharacterAssetCatalog.asset");
            var manki = Resources.Load<CharacterAnimationConfig>("AnimationConfigs/Manki_AnimConfig");
            var wibou = Resources.Load<CharacterAnimationConfig>("AnimationConfigs/Wibou_AnimConfig");
            Require(source != null && source.Rig != null, "Manki's rig is unavailable.");
            Require(manki != null && manki.Idle != null && manki.Run != null,
                "Manki's Resources idle and run clips are required.");
            Require(wibou != null && wibou.Idle != null && wibou.Idle != manki.Idle,
                "Wibou's distinct Resources idle clip is required.");

            var first = Catalog(source.Rig, manki.Idle);
            var second = Catalog(source.Rig, manki.Run);
            var animationOverride = ScriptableObject.CreateInstance<CharacterAnimationConfig>();
            animationOverride.hideFlags = HideFlags.HideAndDontSave;
            animationOverride.Idle = manki.Run;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                CheckCatalogAndRejection(NewObject(scene, "Catalog configuration").AddComponent<PlayerRenderer>(),
                    first, second, animationOverride, NewObject(scene, "Rig without Animator"));
                CheckFallbackAndOverrides(NewObject(scene, "Animation sources").AddComponent<PlayerRenderer>(),
                    first, animationOverride, manki, wibou);
                CheckDefinitionOnly(NewObject(scene, "Definition-only configuration"));
                Debug.Log("[PlayerRendererConfigurationSelfTest] Passed catalog-before-model, same-prefab refresh, " +
                    "Resources and explicit/serialized overrides, rejected configuration preservation and definition-only setup.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(animationOverride);
            }
        }

        private static void CheckCatalogAndRejection(
            PlayerRenderer renderer, CharacterAnimationCatalog first, CharacterAnimationCatalog second,
            CharacterAnimationConfig animationOverride, GameObject invalidRig)
        {
            var definition = Definition(CharacterClass.FightGuy);
            var baked = new BakedAnimationData
            {
                BoneNames = new[] { "_weapon_hilt", "_weapon_tip" },
                Animations = Array.Empty<BakedAnimationData.BakedAnim>(),
            };
            renderer.EntityId = 123;
            Require(renderer.Configure(definition, baked, first, null, out var error, animationOverride), error);
            var graph = renderer.GetComponentInChildren<AnimancerComponent>();
            Require(graph != null && graph.States.Current?.Clip == first.Animations[0].Clip,
                "Initial configuration did not resolve the catalog before playing idle.");
            Require(!renderer.Animator.applyRootMotion, "The model retained root-motion authority.");
            var attachment = new GameObject("Caller-owned attachment");
            attachment.transform.SetParent(renderer.Animator.transform, false);
            var updated = Definition(CharacterClass.FightGuy) with
            {
                CapsuleRadius = 0.8f,
                CapsuleHeight = 2.4f,
                ModelYOffset = -0.7f,
                VisualScale = 1.2f,
                HurtboxBoneDefs = new[] { new HurtboxBoneDef("fixture-bone", .1f, .2f, .3f, .4f) },
            };
            second.Rig = invalidRig;
            Require(renderer.Configure(updated, baked, second, first.Rig, out error), error);
            Require(renderer.GetComponentInChildren<AnimancerComponent>() == graph && attachment != null,
                "An unchanged prefab replaced its model or caller-owned attachment.");
            Require(graph.States.Current?.Clip == second.Animations[0].Clip,
                "An unchanged prefab retained the prior catalog's animation.");
            Require(graph.transform.localScale == Vector3.one * updated.VisualScale,
                "An unchanged prefab retained its prior scale.");
            Require(renderer.CharacterDef == updated && renderer.BakedData == baked
                && renderer.CapsuleRadius == 0.8f && renderer.CapsuleHeight == 2.4f
                && renderer.ModelYOffset == -0.7f && renderer.HurtboxBoneDefs.SequenceEqual(updated.HurtboxBoneDefs),
                "The renderer did not derive its geometry and poses from the new definition.");

            var installedBones = renderer.HurtboxBoneDefs;
            Require(!ReferenceEquals(installedBones, updated.HurtboxBoneDefs), "Configure did not own its hurtbox array.");

            void Reject(CharacterDefinition candidate, CharacterAnimationCatalog catalog, GameObject rig)
            {
                Require(!renderer.Configure(candidate, null, catalog, rig, out var rejected) && rejected.Length > 0,
                    "Invalid configuration succeeded or returned no diagnostic.");
                Require(renderer.CharacterDef == updated && renderer.BakedData == baked && renderer.EntityId == 123
                    && renderer.CapsuleRadius == 0.8f && renderer.CapsuleHeight == 2.4f
                    && renderer.ModelYOffset == -0.7f && ReferenceEquals(renderer.HurtboxBoneDefs, installedBones)
                    && renderer.HurtboxBoneDefs.SequenceEqual(updated.HurtboxBoneDefs)
                    && renderer.GetComponentInChildren<AnimancerComponent>() == graph && attachment != null
                    && graph.States.Current?.Clip == second.Animations[0].Clip,
                    "Rejected configuration changed the previous working setup.");
                renderer.PlayScrubbed("idle", 0.25f);
                Require(graph.States.Current?.Clip == second.Animations[0].Clip,
                    "Rejected configuration changed subsequent animation resolution.");
            }

            Reject(null, null, null);
            Reject(Definition(CharacterClass.Manki), first, invalidRig);
            var missing = Definition(CharacterClass.Manki) with
            {
                ModelResourcePath = "__RendererConfigurationSelfTest__/MissingRig",
            };
            Reject(missing, null, null);
            var invalid = Definition(CharacterClass.Manki) with { VisualScale = float.NaN };
            Reject(invalid, first, first.Rig);
            invalid = Definition(CharacterClass.Manki) with { CapsuleRadius = -1f };
            Reject(invalid, first, first.Rig);
            Reject(Definition(CharacterClass.FightGuy), null, first.Rig);
            Reject(Definition(CharacterClass.None), null, first.Rig);
        }

        private static void CheckFallbackAndOverrides(
            PlayerRenderer renderer, CharacterAnimationCatalog catalog, CharacterAnimationConfig animationOverride,
            CharacterAnimationConfig manki, CharacterAnimationConfig wibou)
        {
            var definition = Definition(CharacterClass.Manki);
            Require(renderer.Configure(definition, null, null, catalog.Rig, out var error), error);
            var graph = renderer.GetComponentInChildren<AnimancerComponent>();
            Require(graph.States.Current?.Clip == manki.Idle, "The Resources fallback did not resolve Manki's idle.");

            definition = Definition(CharacterClass.Wibou);
            Require(renderer.Configure(definition, null, null, catalog.Rig, out error), error);
            Require(renderer.GetComponentInChildren<AnimancerComponent>() == graph
                && graph.States.Current?.Clip == wibou.Idle,
                "A new fallback source retained cached clips from the previous definition.");

            Require(renderer.Configure(definition, null, catalog, catalog.Rig, out error), error);
            Require(graph.States.Current?.Clip == manki.Idle, "The catalog did not replace the Resources fallback.");
            Require(renderer.Configure(definition, null, null, catalog.Rig, out error), error);
            Require(graph.States.Current?.Clip == wibou.Idle, "Returning from a catalog retained stale fallback clips.");

            Require(renderer.Configure(definition, null, null, catalog.Rig, out error, animationOverride), error);
            Require(graph.States.Current?.Clip == animationOverride.Idle, "The explicit AnimConfig override was ignored.");
            Require(renderer.Configure(definition, null, null, catalog.Rig, out error), error);
            Require(graph.States.Current?.Clip == wibou.Idle, "A previous call's override leaked into a new configuration.");

            var serialized = new SerializedObject(renderer);
            serialized.FindProperty("_charConfig").objectReferenceValue = animationOverride;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Require(renderer.Configure(definition, null, catalog, catalog.Rig, out error), error);
            Require(graph.States.Current?.Clip == manki.Idle, "A serialized override displaced the semantic catalog.");
            Require(renderer.Configure(definition, null, null, catalog.Rig, out error), error);
            Require(graph.States.Current?.Clip == animationOverride.Idle,
                "A catalog transition lost the serialized AnimConfig override.");
            serialized.Update();
            serialized.FindProperty("_charConfig").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            definition = Definition(CharacterClass.None) with { ModelResourcePath = "Characters/Manki" };
            Require(renderer.Configure(definition, null, null, catalog.Rig, out error), error);
            graph = renderer.GetComponentInChildren<AnimancerComponent>();
            Require(graph.States.Current?.Clip == manki.Idle,
                "The model-name Resources fallback no longer resolves placeholder animation config.");
        }

        private static void CheckDefinitionOnly(GameObject root)
        {
            var marker = new GameObject("Caller-owned geometry");
            marker.transform.SetParent(root.transform, false);
            var renderer = root.AddComponent<PlayerRenderer>();
            var definition = Definition(CharacterClass.FightGuy);
            var baked = new BakedAnimationData
            {
                BoneNames = Array.Empty<string>(),
                Animations = Array.Empty<BakedAnimationData.BakedAnim>(),
            };
            Require(renderer.Configure(definition, baked, null, null, out var error), error);
            renderer.EnsureModel();
            Require(renderer.CharacterDef == definition && renderer.BakedData == baked
                && renderer.CapsuleRadius == definition.CapsuleRadius && renderer.CapsuleHeight == definition.CapsuleHeight
                && renderer.ModelYOffset == definition.ModelYOffset && marker != null
                && root.GetComponentInChildren<AnimancerComponent>() == null,
                "Definition-only configuration required a rig or replaced caller-owned geometry.");
            Require(renderer.Configure(definition, null, null, null, out error) && renderer.BakedData == null,
                "Definition-only reconfiguration retained the previous baked data.");
        }

        private static CharacterDefinition Definition(CharacterClass fighter) => new()
        {
            Class = fighter,
            CapsuleRadius = 0.4f,
            CapsuleHeight = 1.6f,
            ModelYOffset = -0.3f,
            HurtboxBoneDefs = Array.Empty<HurtboxBoneDef>(),
        };

        private static CharacterAnimationCatalog Catalog(GameObject rig, AnimationClip idle)
        {
            var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
            catalog.hideFlags = HideFlags.HideAndDontSave;
            catalog.Rig = rig;
            catalog.Animations = new[]
            {
                new CharacterAnimationCatalog.AnimationEntry { SemanticId = "idle", Clip = idle },
            };
            return catalog;
        }

        private static GameObject NewObject(Scene scene, string name)
        {
            var root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
