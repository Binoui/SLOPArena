using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SlopArena.Client.Combat;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools
{
    public static class UnityAuditPresentationSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/SLOPArena/Tests/Unity Audit Presentation/All")]
        public static void RunAll()
        {
            RunProjectileIdentity();
            RunCorrectedProjectileIdentity();
            RunMaterialLifetime();
        }

        public static void RunProjectileIdentity()
        {
            using var fixture = new Fixture();
            var simulation = new ServerSimulation(new ArenaDefinition
            {
                Name = "Unity audit", KillHeight = -20,
                SpawnPoints = new[] { new SpawnPoint() },
            });
            var manager = fixture.Root.AddComponent<ProjectileVFXManager>();
            manager.SetPredictedSimulation(simulation);
            manager.RegisterOwner(1, null); // Explicit neutral fixture binding.
            typeof(ProjectileVFXManager).GetField("_config", Private).SetValue(manager, null);
            simulation.Resolver.Spawn(Projectile(0, 1));
            simulation.Resolver.Spawn(Projectile(10));
            simulation.Resolver.Spawn(Projectile(20));
            var initial = simulation.CaptureSnapshot();
            manager.OnTick();
            fixture.Adopt(manager);
            var second = VisualAt(manager, 10);
            var third = VisualAt(manager, 20);

            simulation.Resolver.Tick(new List<SpellResolver.EntityData>());
            manager.OnTick();
            fixture.Adopt(manager);
            Require(Visuals(manager).Length == 2, "Expired projectile retained its visual.");
            Require(VisualAt(manager, 10) == second && VisualAt(manager, 20) == third,
                "Surviving stream projectiles inherited another emission's visual.");
            simulation.SetTick(300);
            manager.OnTick();
            fixture.Adopt(manager);
            Require(VisualAt(manager, 10) == second && VisualAt(manager, 20) == third,
                "Advancing the clock without resolver aging changed visual identity.");

            simulation.RestoreSnapshot(initial);
            manager.OnTick();
            fixture.Adopt(manager);
            Require(VisualAt(manager, 10) == second && VisualAt(manager, 20) == third,
                "Restoring a snapshot replaced surviving visuals and trails.");
            simulation.Resolver.Spawn(Projectile(30));
            manager.OnTick();
            fixture.Adopt(manager);
            var future = VisualAt(manager, 30);
            var expected = simulation.CaptureSnapshot();
            simulation.RestoreSnapshot(initial);
            simulation.Resolver.Spawn(Projectile(30));
            manager.OnTick();
            fixture.Adopt(manager);
            Require(VisualAt(manager, 30) == future, "Resimulation changed an emission's visual identity.");
            Require(expected.ContentEquals(simulation.CaptureSnapshot()), "Emission replay changed world bytes.");
            Debug.Log("[UnityAuditPresentationSelfTest] Same-tick emissions, stream removal, clock-only advance and rollback preserved visual identity.");
        }

        public static void RunCorrectedProjectileIdentity()
        {
            using var fixture = new Fixture();
            var firstPrefab = new GameObject("First projectile prefab");
            var correctedPrefab = new GameObject("Corrected projectile prefab");
            firstPrefab.transform.SetParent(fixture.Root.transform, false);
            correctedPrefab.transform.SetParent(fixture.Root.transform, false);
            firstPrefab.SetActive(false);
            correctedPrefab.SetActive(false);
            var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
            catalog.SetPresentationProfile(new CharacterPresentationProfile().WithProjectiles(new[]
            {
                new ProjectilePresentation("ground.1", firstPrefab, 1f, ProjectileOrientation.PreservePrefab),
                new ProjectilePresentation("ground.2", correctedPrefab, 1f, ProjectileOrientation.PreservePrefab),
                new ProjectilePresentation("air.1", correctedPrefab, 1f, ProjectileOrientation.PreservePrefab),
            }));
            try
            {
                var simulation = new ServerSimulation(new ArenaDefinition
                {
                    Name = "Corrected emission", KillHeight = -20,
                    SpawnPoints = new[] { new SpawnPoint() },
                });
                var manager = fixture.Root.AddComponent<ProjectileVFXManager>();
                manager.SetPredictedSimulation(simulation);
                manager.RegisterOwner(1, catalog);
                manager.RegisterOwner(2, catalog);
                var anchor = simulation.CaptureSnapshot();
                for (int variant = 0; variant < 5; variant++)
                {
                    var first = Projectile(10);
                    first.AttackSlot = AbilitySlots.Slot1;
                    simulation.RestoreSnapshot(anchor);
                    simulation.Resolver.Spawn(first);
                    manager.OnTick();
                    var predicted = Visuals(manager).Single();
                    Require(predicted.name.StartsWith(firstPrefab.name, StringComparison.Ordinal), "Prediction selected the wrong prefab.");
                    fixture.Adopt(manager);
                    var corrected = first;
                    switch (variant)
                    {
                        case 0: corrected.OwnerId++; break;
                        case 1: corrected.ActivationId++; break;
                        case 2: corrected.VisualOperationIndex++; break;
                        case 3: corrected.AttackSlot = AbilitySlots.Slot2; break;
                        case 4: corrected.ActivationAirborne = true; break;
                    }
                    simulation.RestoreSnapshot(anchor);
                    simulation.Resolver.Spawn(corrected);
                    manager.OnTick();
                    var replacement = Visuals(manager).Single();
                    string expected = variant >= 3 ? correctedPrefab.name : firstPrefab.name;
                    Require(replacement != predicted && predicted == null && replacement.name.StartsWith(expected, StringComparison.Ordinal),
                        "A corrected emission inherited an incompatible visual or trail, variant " + variant + ".");
                    fixture.Adopt(manager);
                }
                Debug.Log("[UnityAuditPresentationSelfTest] Corrected owner, activation, operation, slot and airborne emissions replaced incompatible visuals.");
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        public static void RunMaterialLifetime()
        {
            using var fixture = new Fixture();
            var manager = fixture.Root.AddComponent<ProjectileVFXManager>();
            var build = typeof(ProjectileVFXManager).GetMethod("BuildProjectileVisual", Private);
            var created = new List<GameObject>();
            var materials = new List<Material>();
            var borrowed = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var visual = (GameObject)build.Invoke(manager, null);
                    SceneManager.MoveGameObjectToScene(visual, fixture.Scene);
                    created.Add(visual);
                    materials.Add(visual.GetComponentInChildren<MeshRenderer>().sharedMaterial);
                    materials.Add(visual.GetComponent<TrailRenderer>().sharedMaterial);
                    var extra = new GameObject("Borrowed prefab material");
                    extra.transform.SetParent(visual.transform, false);
                    extra.AddComponent<MeshRenderer>().sharedMaterial = borrowed;
                }
                Object.DestroyImmediate(manager);
                Require(materials.All(material => material != null), "Destroying the manager invalidated surviving effects.");
                foreach (var visual in created) Object.DestroyImmediate(visual);
                created.Clear();
                Require(materials.All(material => material == null), "A procedural material survived its effect.");
                Require(borrowed != null, "Effect cleanup destroyed a borrowed prefab material.");
                Debug.Log("[UnityAuditPresentationSelfTest] Four procedural materials released; borrowed materials and effects outliving the manager remained valid.");
            }
            finally
            {
                foreach (var visual in created) if (visual != null) Object.DestroyImmediate(visual);
                foreach (var material in materials) if (material != null) Object.DestroyImmediate(material);
                if (borrowed != null) Object.DestroyImmediate(borrowed);
            }
        }

        private static Hitbox Projectile(float x, ushort duration = 20) => new()
        {
            OwnerId = 1, ActivationId = 7, VisualOperationIndex = 3,
            X = x, Y = 20, VX = 1, Radius = 0.1f, DurationTicks = duration,
        };

        private static GameObject[] Visuals(ProjectileVFXManager manager)
            => ((IDictionary)typeof(ProjectileVFXManager).GetField("_activeVisuals", Private)
                .GetValue(manager)).Values.Cast<GameObject>().ToArray();

        private static GameObject VisualAt(ProjectileVFXManager manager, float x)
            => Visuals(manager).Single(visual => Mathf.Abs(visual.transform.position.x - x) < 0.1f);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly GameObject Root;

            public Fixture()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Run this check while the Editor is stopped.");
                Scene = EditorSceneManager.NewPreviewScene();
                Root = new GameObject("Unity audit presentation") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(Root, Scene);
            }

            public void Dispose()
            {
                foreach (var manager in Root.GetComponents<ProjectileVFXManager>())
                    foreach (var visual in Visuals(manager))
                        if (visual != null) Object.DestroyImmediate(visual);
                Object.DestroyImmediate(Root);
                EditorSceneManager.ClosePreviewScene(Scene);
            }

            public void Adopt(ProjectileVFXManager manager)
            {
                foreach (var visual in Visuals(manager))
                {
                    visual.name = "Unity audit projectile emission";
                    visual.hideFlags = HideFlags.HideAndDontSave;
                    if (visual.scene != Scene) SceneManager.MoveGameObjectToScene(visual, Scene);
                }
            }
        }
    }
}
