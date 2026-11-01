using System;
using Animancer;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Client.Tools;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AnimationBehaviorSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Animation Extrapolation")]
    public static void RunExtrapolation()
    {
        Require(!Application.isPlaying, "Run animation extrapolation checks in Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Animation extrapolation regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var bone = new GameObject("MovingBone").transform;
            bone.SetParent(root.transform, false);
            var synthetic = new GameObject("_weapon_tip").transform;
            synthetic.SetParent(root.transform, false);
            var untouched = new Vector3(-9f, 8f, 7f);
            synthetic.localPosition = untouched;
            var data = new BakedAnimationData
            {
                BoneNames = new[] { "MovingBone", "_weapon_tip" },
                Animations = new[]
                {
                    new BakedAnimationData.BakedAnim
                    {
                        Name = "fixture.motion",

                        Frames = new[]
                        {
                            new[] { 3f, 4f, 5f, 10f, 20f, 30f },
                            new[] { 4f, 2f, 5f, 20f, 40f, 60f },
                        },
                    },
                },
            };
            var extrapolator = ClipExtrapolator.FromBakedData(data, "fixture.motion", frameRate: 4f);
            Require(extrapolator != null, "Two baked frames did not produce an extrapolator.");
            extrapolator.Apply(root.transform, 0.25f);
            RequirePosition(bone, new Vector3(5f, 0f, 5f), "Positive time lost the baked velocity, including its negative Y component.");
            RequirePosition(synthetic, untouched, "Extrapolation moved a synthetic bone.");
            extrapolator.Apply(root.transform, 0f);
            RequirePosition(bone, new Vector3(4f, 2f, 5f), "Zero extra time did not restore the last baked frame.");
            RequirePosition(synthetic, untouched, "Zero extra time moved a synthetic bone.");
            extrapolator.Apply(root.transform, -0.25f);
            RequirePosition(bone, new Vector3(3f, 4f, 5f), "Negative extra time did not project back to the previous frame.");
            RequirePosition(synthetic, untouched, "Negative extra time moved a synthetic bone.");
            Require(ClipExtrapolator.FromBakedData(data, "missing") == null, "A missing animation produced an extrapolator.");
            Debug.Log("[AnimationBehaviorSelfTest] Passed baked extrapolation, zero/negative time and synthetic bone exclusion.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [MenuItem("Tools/SLOPArena/Tests/Attack Animation Restart")]
    public static void RunAttackRestart()
    {
        Require(!Application.isPlaying, "Run attack restart checks in Edit Mode.");
        var preview = AbilityLabPackagePreviewLoader.Load("bonk");
        Require(preview.IsAvailable, "Bonk's cooked preview is unavailable.");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Attack animation restart regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(preview.Package);
            definition = definition with { Class = CharacterClass.Bonk };
            var renderer = root.AddComponent<PlayerRenderer>();
            Require(renderer.Configure(definition, preview.BakedPoses, preview.AnimationCatalog,
                preview.Rig, out var error), error);
            var graph = root.GetComponentInChildren<AnimancerComponent>();
            Require(graph != null, "The package preview did not create an Animancer graph.");
            graph.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var spec = definition.GetCookedSlotAbility(AbilitySlots.Normal1, false);
            var expectedClip = Array.Find(preview.AnimationCatalog.Animations,
                entry => entry.SemanticId == spec.Clip(0).AnimationId).Clip;
            Require(expectedClip != null, "The package's ground.1 animation clip is unavailable.");
            var state = new CharacterState
            {
                State = ActionState.Attacking,
                AttackSlot = AbilitySlots.Normal1,
                AttackSequence = 7,
                StateTicks = spec.Timeline.Stages[0].DurationTicks,
                IsGrounded = true,
                PY = definition.CapsuleHeight * 0.5f,
            };
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            graph.Evaluate(0f);
            Require(graph.States.Current.Clip == expectedClip, "The attack did not play its package-bound clip.");
            graph.Evaluate(0.15f);
            double advancedTime = graph.States.Current.Time;
            Require(advancedTime > 0.01d, "The attack clip did not advance before testing restart.");

            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            graph.Evaluate(0f);
            RequireTime(graph, advancedTime, "An identical snapshot restarted the attack clip.");
            state.StateTicks--;
            state.AttackElapsedTicks++;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            graph.Evaluate(0f);
            RequireTime(graph, advancedTime, "An attack countdown restarted the clip.");

            state.AttackElapsedTicks = 0;
            state.StateTicks = spec.Timeline.Stages[0].DurationTicks;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            graph.Evaluate(0f);
            RequireTime(graph, advancedTime, "A clock correction within the same sequence restarted the clip.");

            state.AttackSequence++;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            graph.Evaluate(0f);
            Require(graph.States.Current.Clip == expectedClip, "A same-slot restart lost the attack clip.");
            RequireTime(graph, 0d, "A new same-slot attack sequence did not rewind the clip.");
            graph.Evaluate(0.1f);
            double restartedTime = graph.States.Current.Time;
            Require(restartedTime > 0.01d, "The restarted clip did not resume playback.");
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            graph.Evaluate(0f);
            RequireTime(graph, restartedTime, "Repeating the new sequence restarted the clip again.");
            Debug.Log("[AnimationBehaviorSelfTest] Passed same-slot sequence rewind and repeated/countdown snapshot continuity.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(preview.AnimationCatalog);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void RequirePosition(Transform bone, Vector3 expected, string message)
        => Require((bone.localPosition - expected).sqrMagnitude < 0.000001f,
            $"{message} Expected {expected}, got {bone.localPosition}.");

    private static void RequireTime(AnimancerComponent graph, double expected, string message)
        => Require(Math.Abs(graph.States.Current.Time - expected) < 0.00001d,
            $"{message} Expected {expected}, got {graph.States.Current.Time}.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
