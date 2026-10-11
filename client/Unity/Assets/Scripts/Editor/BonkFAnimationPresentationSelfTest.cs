using System;
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
    [MenuItem("Tools/SlopArena/Tests/Bonk F Animation Presentation")]
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
            definition.Class = CharacterClass.Bonk;
            var renderer = root.AddComponent<PlayerRenderer>();
            renderer.SetCharacterDefinition(definition);
            renderer.SetBakedData(preview.BakedPoses);
            renderer.SetAnimationCatalog(preview.AnimationCatalog);
            renderer.LoadModel(definition, preview.Rig);
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
                    renderer.ApplyServerState(frame.Actor);
                    graph.Evaluate(1f / 60f);
                    if (frame.Actor.State != ActionState.Aiming || frame.Actor.HitstopTicks > 0)
                        continue;
                    var spec = definition.GetSlotAbility(AbilitySlots.F - 1, action == "air.F");
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
