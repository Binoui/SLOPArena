using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlopArena.Client.Animation;
using SlopArena.Client.Combat;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SlopArena.EditorTools;

public static class CharacterPresentationProfileSelfTest
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunAll()
    {
        RunAudioPools();
        RunCanonicalOwnersAndHeldTransfer();
        RunCloneAndFingerprint();
        UnityAuditPresentationSelfTest.RunAll();
        Debug.Log("[CharacterPresentationProfileSelfTest] Ordered pools, owner lifetime, canonical variants, held transfer, clones, fingerprints and corrected replay passed.");
    }

    public static void RunAudioPools()
    {
        var a = AudioClip.Create("Profile audio A", 60, 1, 1000, false);
        var b = AudioClip.Create("Profile audio B", 60, 1, 1000, false);
        var fixedClip = AudioClip.Create("Profile fixed audio", 60, 1, 1000, false);
        try
        {
            var cycle = new AudioPool(AudioPoolSelection.Cycle, new[] { a, null, b });
            var fixedPool = new AudioPool(AudioPoolSelection.Fixed, new[] { fixedClip });
            var cycling = Profile(cycle, cycle, cycle);
            var fixedOwner = Profile(fixedPool, fixedPool, fixedPool);
            int cursor = 0;
            Require(cycling.SelectImpact(ImpactTier.Light, ref cursor) == a && cursor == 1, "First cyclic impact was not A.");
            Require(fixedOwner.SelectImpact(ImpactTier.Medium, ref cursor) == fixedClip && cursor == 1, "Fixed attacker advanced the shared cursor.");
            Require(cycling.SelectImpact(ImpactTier.Medium, ref cursor) == null && cursor == 2, "Silent cyclic position was filtered or did not advance.");
            Require(cycling.SelectImpact(ImpactTier.Heavy, ref cursor) == b && cursor == 3, "Interleaved tier lost the shared cursor.");
            Require(cycling.SelectImpact(ImpactTier.Launch, ref cursor) == a && cursor == 4, "Launch did not continue the Heavy pool.");
            int swing = 0;
            Require(new CharacterPresentationProfile().SelectSwing(ref swing) == null && swing == 0, "Empty Swing must be explicit silence.");
            Require(cycle.Select(ref swing) == a && swing == 1 && cursor == 4, "Renderer swing cursor was shared with impact audio.");
            int overflow = int.MaxValue;
            cycle.Select(ref overflow); cycle.Select(ref overflow);
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(fixedClip); }
    }

    public static void RunCanonicalOwnersAndHeldTransfer()
    {
        using var fixture = new Fixture();
        var ground = fixture.Child("ProfileGround");
        var air = fixture.Child("ProfileAir");
        ground.SetActive(false); air.SetActive(false);
        var profile = new CharacterPresentationProfile().WithProjectiles(new[]
        {
            new ProjectilePresentation("ground.3", ground, 2f, ProjectileOrientation.PreservePrefab, "bone.right-hand"),
            new ProjectilePresentation("air.3", air, 3f, ProjectileOrientation.FlightPositiveY, "bone.right-hand"),
        });
        var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
        catalog.SetPresentationProfile(profile);
        try
        {
            var manager = fixture.Root.AddComponent<ProjectileVFXManager>();
            var simulation = Simulation();
            manager.SetPredictedSimulation(simulation);
            manager.RegisterOwner(42, catalog);
            var charged = new CookedChargedProjectileCapabilityParameters(30, 10, 20, 0, 2f, 3f,
                new CookedProjectile(0, 0, 0, 1, 0, 1f, 1, 30, 1, 0, 1, 30));
            var move = PresentationSelfTestContent.Move(1, new CookedStage(30, 0, 0, 0, 0,
                new[] { "anim.test" }, new CookedTimelineOperation[]
                { new CookedStartCapabilityOperation(0, AuthoringUnit.Meters, "test.charge", "1", charged) }));
            var slots = CanonicalSlotProjection.All.Select(s => PresentationSelfTestContent.Move(s.Ordinal)).ToArray();
            slots[1] = move;
            simulation.RegisterEntity(42, new CharacterDefinition { Class = CharacterClass.None, CookedSlots = slots }, default);
            var hand = fixture.Child("Held hand").transform;
            var aiming = new CharacterState { State = ActionState.Aiming, AttackSlot = AbilitySlots.Slot3, AttackSequence = 7, ChargeTicks = 20 };
            manager.UpdateHeldVisual(42, aiming, hand, move);
            var held = ((IDictionary)typeof(ProjectileVFXManager).GetField("_heldVisuals", Private).GetValue(manager))[42UL];
            var visual = (GameObject)held.GetType().GetField("Visual").GetValue(held);
            Require(visual != null && visual.transform.localScale == Vector3.one * 6f, "Held charge lost configured base scale.");
            fixture.Adopt(visual);
            simulation.Resolver.Spawn(new Hitbox
            {
                OwnerId = 42, ActivationId = 1, VisualOperationIndex = 1, AttackSequence = 7,
                AttackSlot = AbilitySlots.Slot3, X = 10, Y = 10, VX = 1, Radius = 3, DurationTicks = 20,
            });
            manager.OnTick();
            Require(Visuals(manager).Single() == visual && visual.transform.localScale == Vector3.one * 6f, "Launch did not transfer the held visual with radius scale.");
            manager.UpdateHeldVisual(42, aiming, hand, move);
            Require(((IDictionary)typeof(ProjectileVFXManager).GetField("_heldVisuals", Private).GetValue(manager))[42UL]
                .GetType().GetField("Visual").GetValue(((IDictionary)typeof(ProjectileVFXManager).GetField("_heldVisuals", Private).GetValue(manager))[42UL]) == null,
                "Repeated aim recreated a consumed held projectile.");
            simulation.Resolver.Spawn(new Hitbox { OwnerId = 42, ActivationId = 2, AttackSlot = AbilitySlots.Slot3,
                ActivationAirborne = true, X = 20, Y = 10, VZ = 1, Radius = 1, DurationTicks = 20 });
            manager.OnTick();
            var airborne = Visuals(manager).Single(v => v != visual);
            fixture.Adopt(airborne);
            Require(airborne.name.StartsWith(air.name) && airborne.transform.localScale == Vector3.one * 3f
                && Vector3.Dot(airborne.transform.up, Vector3.forward) > .999f,
                "Air activation did not select its canonical profile or positive-Y flight orientation.");
            manager.SetPredictedSimulation(Simulation());
            Require(visual == null && airborne == null, "Simulation reattach retained old visuals.");
            var fresh = Simulation(); manager.SetPredictedSimulation(fresh);
            fresh.Resolver.Spawn(new Hitbox { OwnerId = 42, AttackSlot = AbilitySlots.Slot3, VX = 1, Radius = 1, DurationTicks = 20 });
            manager.OnTick(); var retained = Visuals(manager).Single(); fixture.Adopt(retained);
            Require(retained.name.StartsWith(ground.name), "Simulation retry lost its valid owner binding.");
            manager.RegisterOwner(42, catalog);
            Require(retained == null && Visuals(manager).Length == 0, "Owner replacement retained old visuals.");
            aiming.AttackSequence++;
            manager.UpdateHeldVisual(42, aiming, hand, move);
            var firstHeld = ((IDictionary)typeof(ProjectileVFXManager).GetField("_heldVisuals", Private).GetValue(manager))[42UL];
            var previousHeld = (GameObject)firstHeld.GetType().GetField("Visual").GetValue(firstHeld);
            fixture.Adopt(previousHeld);
            aiming.Deaths++;
            manager.UpdateHeldVisual(42, aiming, hand, move);
            Require(previousHeld == null, "Death/sequence transition retained the previous held visual.");
            var nextHeld = ((IDictionary)typeof(ProjectileVFXManager).GetField("_heldVisuals", Private).GetValue(manager))[42UL];
            fixture.Adopt((GameObject)nextHeld.GetType().GetField("Visual").GetValue(nextHeld));
            aiming.State = ActionState.Idle;
            manager.UpdateHeldVisual(42, aiming, hand, move);
            manager.OnTick(); var replacement = Visuals(manager).Single(); fixture.Adopt(replacement);
            manager.UnregisterOwner(42);
            Require(replacement == null && Visuals(manager).Length == 0, "Owner removal retained its visual.");
            manager.OnTick();
            Require(Visuals(manager).Length == 0, "An unregistered owner recreated its projectile visual.");
        }
        finally { Object.DestroyImmediate(catalog); }
    }

    public static void RunCloneAndFingerprint()
    {
        const string first = "GlobalObjectId_V1-1-0123456789abcdef0123456789abcdef-8300000-0";
        const string second = "GlobalObjectId_V1-1-1123456789abcdef0123456789abcdef-8300000-0";
        var clips = new AudioClip[3];
        var ids = new[] { first, "", second };
        var pool = new AudioPool(AudioPoolSelection.Cycle, clips, ids);
        var source = ScriptableObject.CreateInstance<CharacterAssetCatalog>();
        var generated = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
        var copy = ScriptableObject.CreateInstance<CharacterAssetCatalog>();
        try
        {
            source.SetPresentationProfile(Profile(pool, pool, pool));
            generated.SetPresentationProfile(source.PresentationProfile);
            copy.SetPresentationProfile(source.PresentationProfile);
            string fingerprint = CharacterPackageAuthoringService.ComputeCatalogFingerprint(source);
            using (var serialized = new SerializedObject(copy))
            {
                serialized.Update();
                serialized.FindProperty("_presentationProfile").FindPropertyRelative("_respawnTint").colorValue = Color.red;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Require(copy.PresentationProfile.RespawnTint == Color.red && source.PresentationProfile.RespawnTint != Color.red
                && generated.PresentationProfile.RespawnTint != Color.red, "An owned profile clone changed its source or another owner.");
            Require(CharacterPackageAuthoringService.ComputeCatalogFingerprint(copy) != fingerprint, "A scalar-only profile edit did not invalidate freshness.");
            ids[0] = second; clips[0] = null;
            Require(source.PresentationProfile.LightImpact.ClipObjectIds[0] == first, "Caller arrays remained aliased.");
            var reversed = new AudioPool(AudioPoolSelection.Cycle, new AudioClip[3], new[] { second, "", first });
            copy.SetPresentationProfile(Profile(reversed, pool, pool));
            Require(CharacterPackageAuthoringService.ComputeCatalogFingerprint(copy) != fingerprint, "Ordered media identity did not invalidate freshness.");
            var baselineDependency = new CharacterCookDependencyRecord { Kind = "importer", Identity = "audio", ImporterSettings = "original", ApprovalVersion = "1" };
            string Hash() => CharacterCookDependencyTracker.ComputeSourceHash("{}", "{}", source, new[] { baselineDependency }, Array.Empty<CharacterCookAnimationDefinition>());
            string initial = Hash(); baselineDependency.ImporterSettings = "changed";
            Require(Hash() != initial, "Audio importer settings did not invalidate source hash.");
            initial = Hash(); baselineDependency.ApprovalVersion = "2";
            Require(Hash() != initial, "Ownership approval version did not invalidate source hash.");
        }
        finally { Object.DestroyImmediate(source); Object.DestroyImmediate(generated); Object.DestroyImmediate(copy); }
    }

    public static void RunNativeBaselineParity(string baselineJson)
    {
        CharacterPresentationMigration.Validate(baselineJson);
        var before = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" })
            .Select(AssetDatabase.GUIDToAssetPath).ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path)));
        string rerun = JsonSerializer.Serialize(CharacterPresentationMigration.Run(baselineJson));
        using (var result = JsonDocument.Parse(rerun)) Require(result.RootElement.GetProperty("changed").GetInt32() == 0, "An equal migration rerun changed assets.");
        foreach (var pair in before) Require(File.ReadAllBytes(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), pair.Key)).SequenceEqual(pair.Value), "An idempotent migration rerun rewrote a catalog.");
        foreach (string package in new[] { "manki", "fightguy", "wibou", "bonk", "bashie" })
        {
            var authored = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>($"Assets/CharacterPackages/{package}/CharacterAssetCatalog.asset");
            byte[] bytes = CharacterBindingWriter.Write(authored, Array.Empty<CharacterCookAnimationDefinition>(), new string('0', 64));
            var generated = CharacterAnimationCatalogGenerator.Create(bytes);
            try { Require(CharacterBindingWriter.ProfileJson(authored.PresentationProfile) == CharacterBindingWriter.ProfileJson(generated.PresentationProfile), "Native profile round trip changed media/scalars: " + package); }
            finally { Object.DestroyImmediate(generated); }
            var dependencies = CharacterCookDependencyTracker.Collect("Assets/CharacterPackages/" + package, authored, Array.Empty<CharacterCookAnimationDefinition>());
            Require(dependencies.Any(d => d.Kind == "importer" && d.Identity.Contains("Audio/"))
                && dependencies.Where(d => d.Kind == "asset" && d.Identity.Contains("Audio/")).All(d => d.MetaHash.Length > 0),
                "Actual audio importer/meta dependency closure is incomplete: " + package);
        }
        RunMissingMediaPositions();
        Debug.Log("[CharacterPresentationProfileSelfTest] All five native profiles/media match baseline; rerun is idempotent; generated profiles and audio closure round trip.");
    }

    public static void RunMissingMediaPositions()
    {
        var authored = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>("Assets/CharacterPackages/fightguy/CharacterAssetCatalog.asset");
        var root = JsonNode.Parse(CharacterBindingWriter.Write(authored, Array.Empty<CharacterCookAnimationDefinition>(), new string('0', 64)))!.AsObject();
        const string missing = "GlobalObjectId_V1-1-ffffffffffffffffffffffffffffffff-8300000-0";
        string installed = CharacterBindingWriter.ObjectId(authored.PresentationProfile.LightImpact.Clips[0]);
        root["presentationProfile"]!["swing"]!["selection"] = 1;
        root["presentationProfile"]!["swing"]!["clipGlobalObjectIds"] = new JsonArray(installed, "", missing);
        root["presentationProfile"]!["projectiles"]![0]!["prefabGlobalObjectId"] = missing;
        byte[] bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        Require(CharacterAnimationCatalogGenerator.TryCreateAllowingMissingPresentation(bytes, out var generated, out var error), error);
        try
        {
            var swing = generated.PresentationProfile.Swing;
            Require(swing.Clips.Length == 3 && swing.Clips[0] != null && swing.Clips[1] == null && swing.Clips[2] == null
                && swing.ClipObjectIds[1] == "" && swing.ClipObjectIds[2] == missing
                && generated.PresentationProfile.Projectiles[0].Prefab == null
                && generated.PresentationProfile.Projectiles[0].PrefabObjectId == missing
                && generated.MissingPresentationMedia.Length == 2, "Development reconstruction shortened or discarded missing reference positions/diagnostics.");
            using var encoded = JsonDocument.Parse(CharacterBindingWriter.ProfileJson(generated.PresentationProfile));
            Require(encoded.RootElement.GetProperty("swing").GetProperty("clipGlobalObjectIds")[2].GetString() == missing
                && encoded.RootElement.GetProperty("projectiles")[0].GetProperty("prefabGlobalObjectId").GetString() == missing,
                "Missing generated references lost committed identities.");
            bool rejected = false;
            try { var strict = CharacterAnimationCatalogGenerator.Create(bytes); Object.DestroyImmediate(strict); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Publishing accepted unresolved presentation media.");
        }
        finally { Object.DestroyImmediate(generated); }
    }

    // Run through the root-owned live verification window; removal effects have runtime lifetimes.
    public static void RunRemovalPolicyInPlay()
    {
        Require(Application.isPlaying, "Removal policy proof requires Play Mode.");
        using var fixture = new Fixture(allowPlay: true);
        var manager = fixture.Root.AddComponent<ProjectileVFXManager>();
        var config = ScriptableObject.CreateInstance<ProjectileVFXConfig>();
        var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
        var explosion = fixture.Child("ProfileExplosion"); explosion.SetActive(false); config.ExplosionPrefab = explosion;
        var simulation = Simulation();
        var hitMove = new CookedSlotDefinition(0, "ground.1", false, "Removal", "", "", AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None, 0, false, false, new CookedTimeline(Array.Empty<CookedStage>()), hitPresentationId: "presentation.test.hit");
        var slots = CanonicalSlotProjection.All.Select(s => PresentationSelfTestContent.Move(s.Ordinal)).ToArray(); slots[0] = hitMove;
        simulation.RegisterEntity(42, new CharacterDefinition { Class = CharacterClass.None, CookedSlots = slots }, default);
        manager.SetSimulation(simulation);
        typeof(ProjectileVFXManager).GetField("_config", Private).SetValue(manager, config);
        try
        {
            void Expire(Hitbox hitbox) { simulation.Resolver.Spawn(hitbox); simulation.Resolver.Tick(new List<SpellResolver.EntityData>()); }
            catalog.SetPresentationProfile(new CharacterPresentationProfile(Color.white, new AudioPool(),
                new AudioPool(), new AudioPool(), new AudioPool(), true, Array.Empty<ProjectilePresentation>()));
            manager.RegisterOwner(42, catalog);
            Expire(new Hitbox { OwnerId = 42, DurationTicks = 1, Explosion = new ProjectileExplosion { Radius = 2 } });
            Require(!ObjectsNamed("ProfileExplosion(Clone)").Any(), "Stationary exploding hitbox ignored profile suppression.");
            catalog.SetPresentationProfile(new CharacterPresentationProfile()); manager.RegisterOwner(42, catalog);
            Expire(new Hitbox { OwnerId = 42, DurationTicks = 1, Explosion = new ProjectileExplosion { Radius = 2 } });
            var burst = ObjectsNamed("ProfileExplosion(Clone)").Single(); fixture.Adopt(burst);
            Require(burst.transform.localScale == Vector3.one * 2f, "Generic explosion did not scale by radius.");
            manager.SetPredictedSimulation(simulation);
            Expire(new Hitbox { OwnerId = 42, DurationTicks = 1, Explosion = new ProjectileExplosion { Radius = 2 } });
            Require(ObjectsNamed("ProfileExplosion(Clone)").Length == 1, "Predicted removal replayed an explosion.");
            manager.SetSimulation(simulation);
            var remove = typeof(ProjectileVFXManager).GetMethod("OnHitboxRemoved", Private);
            int prior = ObjectsNamed("KiShotImpact").Length;
            var contact = new Hitbox { OwnerId = 42, AttackSlot = AbilitySlots.Slot1, VX = 1, Active = false };
            remove.Invoke(manager, new object[] { contact, 0f, 0f, 0f });
            Require(ObjectsNamed("KiShotImpact").Length == prior, "Configured contact duplicated its semantic effect.");
            contact.Active = true;
            remove.Invoke(manager, new object[] { contact, 0f, 0f, 0f });
            Require(ObjectsNamed("KiShotImpact").Length == prior + 1, "Moving nonexplosive expiry lost its procedural burst.");
            foreach (var effect in ObjectsNamed("KiShotImpact")) fixture.Adopt(effect);
        }
        finally { Object.DestroyImmediate(catalog); Object.DestroyImmediate(config); }
    }

    private static CharacterPresentationProfile Profile(AudioPool light, AudioPool medium, AudioPool heavy)
        => new(Color.cyan, new AudioPool(AudioPoolSelection.Fixed, Array.Empty<AudioClip>()), light, medium, heavy, false, Array.Empty<ProjectilePresentation>());
    private static ServerSimulation Simulation() => new(new ArenaDefinition { Name = "Profile fixture", KillHeight = -20, SpawnPoints = new[] { new SpawnPoint() } });
    private static GameObject[] Visuals(ProjectileVFXManager manager)
        => ((IDictionary)typeof(ProjectileVFXManager).GetField("_activeVisuals", Private).GetValue(manager)).Values.Cast<GameObject>().ToArray();
    private static GameObject[] ObjectsNamed(string name) => Resources.FindObjectsOfTypeAll<GameObject>().Where(o => o.name == name).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        internal readonly Scene Scene;
        internal readonly GameObject Root;
        private readonly bool _runtimeScene;
        internal Fixture(bool allowPlay = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode && !allowPlay) throw new InvalidOperationException("Run profile checks in stopped Edit Mode.");
            _runtimeScene = allowPlay && Application.isPlaying;
            Scene = _runtimeScene ? SceneManager.CreateScene("Profile fixture " + Guid.NewGuid().ToString("N")) : EditorSceneManager.NewPreviewScene();
            Root = new GameObject("Presentation profile fixture") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(Root, Scene);
        }
        internal GameObject Child(string name) { var child = new GameObject(name); child.transform.SetParent(Root.transform, false); return child; }
        internal void Adopt(GameObject visual) { if (visual != null) { visual.hideFlags = HideFlags.HideAndDontSave; if (visual.scene != Scene) SceneManager.MoveGameObjectToScene(visual, Scene); } }
        public void Dispose()
        {
            foreach (var manager in Root.GetComponents<ProjectileVFXManager>()) foreach (var visual in Visuals(manager)) if (visual != null) Object.DestroyImmediate(visual);
            foreach (var visual in Scene.GetRootGameObjects()) if (visual != Root) Object.DestroyImmediate(visual);
            Object.DestroyImmediate(Root);
            if (_runtimeScene) SceneManager.UnloadSceneAsync(Scene);
            else EditorSceneManager.ClosePreviewScene(Scene);
        }
    }
}
