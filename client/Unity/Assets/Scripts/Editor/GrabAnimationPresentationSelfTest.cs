using SlopArena.EditorTools;
using System;
using System.IO;
using System.Linq;
using Animancer;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GrabAnimationPresentationSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Grab Animation Presentation")]
    public static void Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run the isolated grab presentation check in Edit Mode.");

        var source = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(
            "Assets/CharacterPackages/manki/CharacterAssetCatalog.asset");
        var shared = Resources.Load<CharacterAnimationConfig>("AnimationConfigs/Shared_AnimConfig");
        var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
        catalog.Animations = source.Bindings.Select(binding => new CharacterAnimationCatalog.AnimationEntry
        {
            SemanticId = binding.SemanticId,
            Clip = binding.Clip,
        }).ToArray();
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Grab presentation regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var renderer = root.AddComponent<PlayerRenderer>();
            var definition = new CharacterDefinition
            {
                Class = CharacterClass.Manki,
                IdleAnim = "anim.manki.idle",
                HitSmallAnim = "anim.manki.hit-small",
                CookedSlots = new[] { PresentationSelfTestContent.Move(0,
                    PresentationSelfTestContent.Stage(30, new[] { "anim.manki.g1" })) },
            };
            Require(renderer.Configure(definition, null, catalog, source.Rig, out var error), error);
            var graph = root.GetComponentInChildren<AnimancerComponent>();
            var state = new CharacterState { State = ActionState.Attacking, AttackSlot = AbilitySlots.Normal1, IsGrounded = true };
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            int attemptTicks = DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks
                + DefenseConfig.GrabWhiffRecoveryTicks;
            state.State = ActionState.GrabAttempt;
            state.AttackSlot = 0;
            state.StateTicks = (ushort)attemptTicks;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            Require(renderer.CurrentAttackSlot == 0, "Attack-to-grab retained an attack slot.");
            Require(graph.States.Current.Clip == shared.GetClipByName("grab"), "Grab did not replace the attack clip.");

            state.StateTicks = (ushort)(attemptTicks / 2);
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            Require(Math.Abs(graph.States.Current.NormalizedTime - 0.5) < 0.00001, "Late grab snapshot lost its phase progress.");
            state.StateTicks = (ushort)attemptTicks;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            Require(graph.States.Current.NormalizedTime == 0, "Replayed grab did not rewind a reused clip.");
            state.StateTicks = (ushort)(attemptTicks / 2);
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            state.HitstopTicks = 2;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            graph.Evaluate(0.2f);
            Require(Math.Abs(graph.States.Current.NormalizedTime - 0.5) < 0.00001, "Hitstop advanced the grab pose.");

            state.HitstopTicks = 0;
            state.State = ActionState.Grabbing;
            state.StateTicks = DefenseConfig.GrabHoldTicks;
            state.InteractionPhase = (byte)DefenseInteractionPhase.Captured;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            Require(graph.States.Current.Clip == shared.GetClipByName("grab_idle_a"), "Grabber did not enter its hold loop.");
            state.InteractionId = 1;
            state.State = ActionState.Grabbed;
            renderer.ApplyServerState(state, null);
            Require(graph.States.Current.Clip == shared.GetClipByName("grab_idle_b"), "Victim did not enter its hold loop.");

            foreach (var kind in new[]
            {
                (Windup: DefenseInteractionPhase.Throwing, Released: DefenseInteractionPhase.FlingReleased, Recovery: (ushort)12),
                (Windup: DefenseInteractionPhase.UpTossing, Released: DefenseInteractionPhase.UpTossReleased, Recovery: (ushort)3),
            })
            {
                state.State = ActionState.Throwing;
                state.InteractionId = 1;
                state.InteractionPhase = (byte)kind.Windup;
                state.StateTicks = DefenseConfig.ThrowReleaseTicks;
                renderer.ApplyServerState(state, null);
                Require(graph.States.Current.Clip == shared.GetClipByName("throw")
                    && graph.States.Current.Time == 0, "Throw request did not start the throw clip.");
                state.StateTicks = 1;
                renderer.ApplyServerState(state, null);
                state.InteractionId = 0;
                state.InteractionPhase = (byte)kind.Released;
                state.StateTicks = kind.Recovery;
                state.HitstopTicks = 2;
                renderer.ApplyServerState(state, null);
                graph.Evaluate(0.2f);
                Require(Math.Abs(graph.States.Current.Time - 22.0 / 60.0) < 0.00001,
                    "Release freeze did not hold the authoritative throw pose.");
                Require(renderer.PlayScrubbedState(state, false, 0), "Recorded release could not resolve its clip.");
                Require(Math.Abs(graph.States.Current.Time - 22.0 / 60.0) < 0.00001,
                    "Recorded release differs from live playback.");
                state.HitstopTicks = 0;
                state.StateTicks--;
                renderer.ApplyServerState(state, null);
                Require(Math.Abs(graph.States.Current.Time - 23.0 / 60.0) < 0.00001,
                    "Detached recovery did not resume at natural clip time.");

                state.State = ActionState.Grabbed;
                state.InteractionId = 1;
                state.InteractionPhase = (byte)kind.Windup;
                state.StateTicks = DefenseConfig.ThrowReleaseTicks;
                renderer.ApplyServerState(state, null);
                Require(graph.States.Current.Clip == shared.GetClipByName("Grab_Throw_B")
                    && graph.States.Current.Time == 0, "Victim windup did not share the thrower's clock.");
                state.StateTicks = 1;
                renderer.ApplyServerState(state, null);
                state.State = ActionState.Idle;
                state.InteractionId = 0;
                state.InteractionPhase = (byte)kind.Released;
                state.HitstopTicks = 2;
                renderer.ApplyServerState(state, null);
                state.HitstopTicks = 0;
                state.State = ActionState.Hitstun;
                state.HitstunTicks = 20;
                state.HitReactionSequence++;
                renderer.ApplyServerState(state, null);
                Require(graph.States.Current.Clip == source.Bindings.Single(binding => binding.SemanticId == definition.HitSmallAnim).Clip,
                    "Released victim did not enter its ordinary hit reaction.");
                state.HitstunTicks = 0;
            }
            state.State = ActionState.Idle;
            state.StateTicks = 0;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            Require(graph.States.Current.Clip == source.Bindings.Single(binding => binding.SemanticId == definition.IdleAnim).Clip,
                "Release did not return to locomotion.");

            state.State = ActionState.GrabAttempt;
            state.StateTicks = (ushort)attemptTicks;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            state.State = ActionState.Hitstun;
            state.HitstunTicks = 10;
            renderer.ApplyServerState(state, renderer.CharacterDef?.GetCookedSlotAbility(state.AttackSlot, false));
            Require(graph.States.Current.Clip == source.Bindings.Single(binding => binding.SemanticId == definition.HitSmallAnim).Clip,
                "Hitstun did not interrupt grab.");
            Debug.Log("[GrabAnimationPresentationSelfTest] Passed grab/hold, both throw windups and release freeze, ordinary victim reactions, recovery, rewind, and interruption.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(catalog);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>Capture paired source-clip poses in a disposable scene without modifying package catalogs.</summary>
    public static string[] CaptureEvidence(string directory)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Capture in Edit Mode.");
        var source = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>("Assets/CharacterPackages/manki/CharacterAssetCatalog.asset");
        var compiled = CharacterPackageCompiler.Compile(
            File.ReadAllText("Assets/CharacterPackages/manki/package.json"),
            File.ReadAllText("Assets/CharacterPackages/manki/character.json"), CharacterCookProfile.TrustedBuiltIn);
        Require(compiled.CookedPackage != null && !compiled.HasErrors, "Manki source could not compile for pose capture.");
        var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage, CharacterClass.Manki);
        var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
        catalog.Animations = source.Bindings.Select(binding => new CharacterAnimationCatalog.AnimationEntry
        {
            SemanticId = binding.SemanticId, Clip = binding.Clip,
        }).ToArray();
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Paired grab pose evidence") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        var target = new RenderTexture(720, 480, 24);
        var texture = new Texture2D(720, 480, TextureFormat.RGB24, false);
        var previousTarget = RenderTexture.active;
        try
        {
            PlayerRenderer Actor(string name)
            {
                var actorRoot = new GameObject(name);
                actorRoot.transform.SetParent(root.transform, false);
                var renderer = actorRoot.AddComponent<PlayerRenderer>();
                Require(renderer.Configure(definition, null, catalog, source.Rig, out var error), error);
                foreach (var transform in actorRoot.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
                foreach (var mesh in actorRoot.GetComponentsInChildren<SkinnedMeshRenderer>()) mesh.updateWhenOffscreen = true;
                return renderer;
            }
            var grabber = Actor("Grabber");
            var victim = Actor("Victim");
            var camera = new GameObject("Evidence camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.scene = scene;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            camera.orthographic = true;
            camera.targetTexture = target;
            camera.aspect = 1.5f;
            camera.transform.rotation = Quaternion.Euler(12f, 65f, 0f);
            var light = new GameObject("Evidence light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.cullingMask = 1 << 31;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

            var controller = new SlopArena.Client.Tools.AbilityLabSimulationController();
            var recorded = controller.RunScenario(definition, null,
                new SlopArena.Client.Tools.AbilityLabScenarioOptions("grab", 300, 0.9f),
                new Vector3(0f, definition.CapsuleHeight * 0.5f, 0f), 0f);
            var held = recorded.Frames.First(frame => frame.Actor.State == ActionState.Grabbing);
            var throwing = recorded.Frames.First(frame => frame.Actor.State == ActionState.Throwing);
            var release = recorded.Frames[recorded.Interactions.First(observation => observation.Kind == "release").FrameIndex];
            var launched = recorded.Frames.First(frame => frame.Opponent.State == ActionState.Hitstun && !frame.Opponent.IsGrounded);
            var actionable = recorded.Frames.First(frame => frame.FrameIndex > launched.FrameIndex
                && frame.Opponent.State == ActionState.Idle && frame.Opponent.HitstopTicks == 0 && !frame.Opponent.IsGrounded);
            var phases = new[] { "hold", "throw-start", "release-freeze", "ordinary-launch", "actionable-flight" };
            var frames = new[] { held, throwing, release, launched, actionable };
            Directory.CreateDirectory(directory);
            var paths = new string[phases.Length];
            for (int index = 0; index < phases.Length; index++)
            {
                var actorState = frames[index].Actor;
                var victimState = frames[index].Opponent;
                Require(grabber.PlayScrubbedState(actorState, !actorState.IsGrounded, 0)
                    && victim.PlayScrubbedState(victimState, !victimState.IsGrounded, 0), "Paired pose did not resolve.");
                camera.Render();
                var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Where(renderer => renderer.enabled && !renderer.forceRenderingOff).ToArray();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var inverseView = Quaternion.Inverse(camera.transform.rotation);
                float halfWidth = 0f, halfHeight = 0f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var projected = inverseView * offset;
                    halfWidth = Mathf.Max(halfWidth, Mathf.Abs(projected.x));
                    halfHeight = Mathf.Max(halfHeight, Mathf.Abs(projected.y));
                }
                camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / camera.aspect) * 1.2f;
                camera.transform.position = bounds.center - camera.transform.forward * (bounds.extents.magnitude * 2f + 5f);
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 720, 480), 0, 0, false);
                texture.Apply(false, false);
                paths[index] = Path.Combine(directory, phases[index] + ".png");
                File.WriteAllBytes(paths[index], texture.EncodeToPNG());
            }
            return paths;
        }
        finally
        {
            RenderTexture.active = previousTarget;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
