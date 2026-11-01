using System;
using System.Collections.Generic;
using System.Linq;
using Animancer;
using SlopArena.Client.Entities;
using SlopArena.Client.Tools;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BonkFAnimationPresentationSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Capability Phase Presentation")]
    public static void RunCapabilityPhases()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run capability presentation checks in Edit Mode.");
        CheckPhases("bonk", CharacterClass.Bonk, "ground.E", 10f, "anim.bonk.ge-land");
        CheckPhases("bonk", CharacterClass.Bonk, "air.E", 10f, "anim.bonk.ge-land");
        CheckPhases("bonk", CharacterClass.Bonk, "ground.R", 1000f,
            "anim.bonk.gr-gallop", "anim.bonk.gr-stop");
        CheckPhases("bonk", CharacterClass.Bonk, "ground.R", 4f,
            "anim.bonk.gr-gallop", "anim.bonk.gr-hit");
        CheckPhases("bonk", CharacterClass.Bonk, "air.R", 1000f,
            "anim.bonk.gr-gallop", "anim.bonk.gr-lookdown");
        CheckPhases("bashie", CharacterClass.Bashie, "ground.A", 5f, "anim.bashie.ring-reel");
        CheckPhases("bashie", CharacterClass.Bashie, "ground.A", 30f, "anim.bashie.ring-retract");
        Debug.Log("[CapabilityPhasePresentationSelfTest] Passed real simulation phase playback for leap landing, " +
            "joust gallop/hit/miss/air endings, and rescue ring reel/retract.");
    }

    private static void CheckPhases(string package, CharacterClass fighter, string action, float distance,
        params string[] expectedPhases)
    {
        var preview = AbilityLabPackagePreviewLoader.Load(package);
        if (!preview.IsAvailable)
            throw new InvalidOperationException($"{package}'s cooked preview is unavailable.");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Capability phase regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(preview.Package) with { Class = fighter };
            var renderer = root.AddComponent<PlayerRenderer>();
            if (!renderer.Configure(definition, preview.BakedPoses, preview.AnimationCatalog, preview.Rig, out var error))
                throw new InvalidOperationException(error);
            var graph = root.GetComponentInChildren<AnimancerComponent>();
            var recording = new AbilityLabSimulationController().RunScenario(definition, preview.BakedPoses,
                new AbilityLabScenarioOptions(action, 420, distance, chargeTicks: 20),
                new Vector3(20f, definition.CapsuleHeight * 0.5f, 10f), 0f);
            var slot = definition.GetCookedSlotAbility(CanonicalSlotProjection.ToWire(
                CanonicalSlotProjection.All.Single(address => address.Id == action)), action.StartsWith("air."));
            var observed = new HashSet<string>();
            foreach (var frame in recording.Frames)
            {
                var previousClip = graph.States.Current?.Clip;
                renderer.ApplyServerState(frame.Actor, frame.Actor.AttackSlot == 0 ? null : slot);
                graph.Evaluate(0f);
                if (frame.Actor.HitstopTicks > 0)
                {
                    if (graph.States.Current?.Clip != previousClip || graph.States.Current.Speed != 0f)
                        throw new InvalidOperationException($"{package} {action}: hitstop changed or advanced the held clip.");
                    continue;
                }
                if (frame.Actor.State != ActionState.Attacking || frame.Actor.AttackSlot == 0)
                    continue;
                string id = slot.Clip(frame.Actor.AnimIndex).AnimationId;
                if (!expectedPhases.Contains(id))
                    continue;
                var clip = Array.Find(preview.AnimationCatalog.Animations, entry => entry.SemanticId == id).Clip;
                if (clip == null || graph.States.Current?.Clip != clip)
                    throw new InvalidOperationException($"{package} {action} frame {frame.FrameIndex}: phase {id} did not play.");
                observed.Add(id);
                if (!renderer.PlayScrubbedState(frame.Actor, slot.IsAir, frame.ActorPoseTicks)
                    || graph.States.Current?.Clip != clip)
                    throw new InvalidOperationException($"{package} {action}: recorded phase {id} differs from live playback.");
            }
            foreach (string id in expectedPhases)
                if (!observed.Contains(id))
                    throw new InvalidOperationException($"{package} {action}: simulation never presented phase {id}.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(preview.AnimationCatalog);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [MenuItem("Tools/SLOPArena/Tests/Bonk E Landing Continuity")]
    public static void RunLeapLandingContinuity()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run the landing pose check in Edit Mode.");
        var preview = AbilityLabPackagePreviewLoader.Load("bonk");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Bonk E landing regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(preview.Package) with { Class = CharacterClass.Bonk };
            var renderer = root.AddComponent<PlayerRenderer>();
            if (!renderer.Configure(definition, preview.BakedPoses, preview.AnimationCatalog, preview.Rig, out var error))
                throw new InvalidOperationException(error);
            var graph = root.GetComponentInChildren<AnimancerComponent>();
            graph.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var hips = graph.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = graph.Animator.GetBoneTransform(HumanBodyBones.Head);
            foreach (var action in new[] { "ground.E", "air.E" })
            {
                var recording = new AbilityLabSimulationController().RunScenario(definition, preview.BakedPoses,
                    new AbilityLabScenarioOptions(action, 220, 25f),
                    new Vector3(20f, definition.CapsuleHeight * 0.5f, 10f), 0f);
                int checkedFrames = 0;
                Vector3? previousDirection = null;
                foreach (var frame in recording.Frames)
                {
                    var actor = frame.Actor;
                    if (actor.State != ActionState.Attacking) continue;
                    var slot = definition.GetCookedSlotAbility(actor.AttackSlot, action.StartsWith("air."));
                    string id = slot.Clip(actor.AnimIndex).AnimationId;
                    bool handoff = id == "anim.bonk.ge-drop" && actor.AttackElapsedTicks >= 16
                        || id == "anim.bonk.ge-land" && actor.AttackElapsedTicks <= 6;
                    if (!handoff) continue;
                    if (!renderer.PlayScrubbedState(actor, slot.IsAir, frame.ActorPoseTicks))
                        throw new InvalidOperationException($"{action}: landing pose was unavailable.");
                    var direction = (head.position - hips.position).normalized;
                    if (previousDirection.HasValue && Vector3.Angle(previousDirection.Value, direction) > 45f)
                        throw new InvalidOperationException($"{action} {id} tick {actor.AttackElapsedTicks}: discontinuous landing orientation.");
                    previousDirection = direction;
                    float upright = direction.y;
                    if (upright > 0.7f)
                        throw new InvalidOperationException($"{action} {id} tick {actor.AttackElapsedTicks}: upright landing snap ({upright:F3}).");
                    checkedFrames++;
                }
                if (checkedFrames < 9)
                    throw new InvalidOperationException($"{action}: did not exercise the drop/landing handoff.");
            }
            Debug.Log("[BonkELandingContinuitySelfTest] Passed ground and air drop/landing poses without an upright snap.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(preview.AnimationCatalog);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [MenuItem("Tools/SLOPArena/Tests/Bonk F Animation Presentation")]
    public static void Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run the isolated Bonk F check in Edit Mode.");
        var preview = AbilityLabPackagePreviewLoader.Load("bonk");
        if (!preview.IsAvailable)
            throw new InvalidOperationException("Bonk's cooked preview is unavailable.");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Bonk F animation regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(preview.Package);
            definition = definition with { Class = CharacterClass.Bonk };
            var renderer = root.AddComponent<PlayerRenderer>();
            if (!renderer.Configure(definition, preview.BakedPoses, preview.AnimationCatalog, preview.Rig, out var error))
                throw new InvalidOperationException(error);
            root.AddComponent<WeaponAttach>().Init(renderer, preview.AnimationCatalog.WeaponConfig);
            var graph = root.GetComponentInChildren<AnimancerComponent>();
            var controller = new AbilityLabSimulationController();
            foreach (var action in new[] { "ground.F", "ground.F", "air.F" })
            {
                var recording = controller.RunScenario(definition, preview.BakedPoses,
                    new AbilityLabScenarioOptions(action, 130, 10f),
                    new Vector3(20f, definition.CapsuleHeight * 0.5f, 10f), 0f);
                bool checkedAim = false;
                foreach (var frame in recording.Frames)
                {
                    renderer.ApplyServerState(frame.Actor, renderer.CharacterDef?.GetCookedSlotAbility(frame.Actor.AttackSlot, action == "air.F"));
                    graph.Evaluate(1f / 60f);
                    if (frame.Actor.State != ActionState.Aiming || frame.Actor.HitstopTicks > 0)
                        continue;
                    var spec = definition.GetCookedSlotAbility(AbilitySlots.F, action == "air.F");
                    var animation = graph.States.Current;
                    var expectedClip = Array.Find(preview.AnimationCatalog.Animations,
                        entry => entry.SemanticId == spec.AimAnimationId).Clip;
                    // The first graph evaluation applies the seek before time advances.
                    bool invalidTime = checkedAim ? animation.Time <= 0d : animation.Time > 1d / 30d;
                    if (animation.Clip != expectedClip || animation.Speed <= 0f || invalidTime)
                        throw new InvalidOperationException($"{action} frame {frame.FrameIndex}: F aim animation did not start/advance (time={animation.Time}, speed={animation.Speed}).");
                    checkedAim = true;
                }
                if (!checkedAim)
                    throw new InvalidOperationException($"{action} did not exercise F's aim phase.");
            }
            Debug.Log("[BonkFAnimationPresentationSelfTest] Passed initial, repeated and airborne F animation playback.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(preview.AnimationCatalog);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
