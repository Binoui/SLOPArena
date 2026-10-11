using System;
using System.Linq;
using System.Reflection;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Client.Tools;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools;

public static class AbilityLabVfxSelfTest
{
    [MenuItem("Tools/SlopArena/Tests/Ability Lab VFX Workflow")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run VFX workflow checks in Edit Mode.");
        VerifyPlayback();
        VerifyWorkspaceStatus();
        Debug.Log("[AbilityLabVfxSelfTest] Passed match/Lab lifetime parity, hitstop, interruption, replacement, rewind, disabled owner, missing bindings and draft-safe cook status.");
    }

    private static void VerifyPlayback()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("VFX workflow regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        var bone = new GameObject("mixamorig:Hips");
        bone.transform.SetParent(root.transform, false);
        var prefab = new GameObject("VFX fixture") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(prefab, scene);
        prefab.AddComponent<AttackBoundPresentation>();
        var renderer = root.AddComponent<PlayerRenderer>();
        var catalog = ScriptableObject.CreateInstance<CharacterAnimationCatalog>();
        catalog.Presentations = new[] { new CharacterAnimationCatalog.PresentationEntry { SemanticId = "vfx.test", Prefab = prefab } };
        var bindings = new[] { new CharacterAssetCatalog.PresentationBinding { SemanticId = "vfx.test", Prefab = prefab } };
        var dispatcher = new TimelinePresentationDispatcher();
        var preview = new AbilityLabPresentationPreviewer();
        var lastState = typeof(PlayerRenderer).GetField("_lastState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lastAction = typeof(PlayerRenderer).GetField("_lastAnimState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Present(CharacterState state)
        {
            lastState.SetValue(renderer, state);
            lastAction.SetValue(renderer, state.State);
        }
        var cue = new TimelinePresentationEvent(1, 1, 0, "vfx.test", 1, PresentationEventSource.Timeline, 0, 0, 0, 0)
        {
            Placement = new PresentationPlacement(AuthoringPresentationAttachmentMode.Bone, "bone.hips", DurationTicks: 3),
        };
        CharacterState State(ushort elapsed, ActionState action = ActionState.Attacking, byte sequence = 1)
            => new() { EntityId = 1, State = action, AttackSequence = sequence, AttackElapsedTicks = elapsed };
        var history = new[] { State(2), State(2), State(2), State(3), State(4), State(5), State(0, ActionState.Idle) };
        try
        {
            dispatcher.Register(1, renderer, catalog);
            for (int index = 0; index < history.Length; index++)
            {
                Present(history[index]);
                dispatcher.Tick(index == 0 ? new[] { cue } : Array.Empty<TimelinePresentationEvent>());
                int matchCount = bone.transform.childCount;
                preview.SetSimulationFrame(new[] { cue }, (uint)(index + 1), bindings, renderer, root.transform, history);
                Require(preview.ActiveInstanceCount == matchCount, $"Lab and match disagree at tick {index + 1}.");
                Require(preview.ActiveInstances.All(instance => (instance.hideFlags & HideFlags.DontSaveInEditor) != 0),
                    "Preview effect could be persisted into a scene.");
                Require(matchCount == (index < 5 ? 1 : 0), "Hitstop consumed lifetime or effect missed its expiry.");
                preview.Clear();
            }
            preview.SetSimulationFrame(new[] { cue }, 3, bindings, renderer, root.transform, history);
            Require(preview.ActiveInstanceCount == 1, "Backward scrubbing did not reconstruct the effect.");
            preview.Clear();

            foreach (var terminal in new[] { State(3, ActionState.Hitstun), State(3, sequence: 2) })
            {
                dispatcher.Clear();
                dispatcher.Register(1, renderer, catalog);
                var interrupted = new[] { State(2), terminal, State(4) };
                for (int index = 0; index < interrupted.Length; index++)
                {
                    Present(interrupted[index]);
                    dispatcher.Tick(index == 0 ? new[] { cue } : Array.Empty<TimelinePresentationEvent>());
                    int matchCount = bone.transform.childCount;
                    preview.SetSimulationFrame(new[] { cue }, (uint)(index + 1), bindings, renderer, root.transform, interrupted);
                    Require(preview.ActiveInstanceCount == matchCount && matchCount == (index == 0 ? 1 : 0),
                        "Interrupted/replaced effect remained visible or reappeared.");
                    preview.Clear();
                }
            }
            Present(State(2));
            renderer.enabled = false;
            preview.SetSimulationFrame(new[] { cue }, 1, bindings, renderer, root.transform, history);
            Require(preview.ActiveInstanceCount == 0, "Disabled owner retained an attached preview effect.");
            renderer.enabled = true;
            preview.SetSimulationFrame(new[] { cue with { PresentationId = "vfx.missing" } }, 1, bindings, renderer, root.transform, history);
            Require(preview.Diagnostics.Count == 1 && preview.Diagnostics[0].Contains("vfx.missing"), "Missing VFX binding was silent.");
            preview.SetSimulationFrame(new[] { cue }, 1, bindings, renderer, root.transform, history);
            Require(preview.Diagnostics.Count == 0, "Resolved binding retained a stale diagnostic.");
            preview.Clear();
            preview.SetSimulationFrame(new[] { cue with { Placement = cue.Placement with { BoneId = "bone.missing" } } },
                1, bindings, renderer, root.transform, history);
            Require(preview.Diagnostics.Count == 1 && preview.Diagnostics[0].Contains("bone.missing")
                && preview.ActiveInstanceCount == 1 && preview.ActiveInstances.Single().transform.parent == root.transform,
                "Missing attachment bone was silent or lost the event-position fallback.");
            preview.Clear();
            dispatcher.Clear();
            dispatcher.Register(1, renderer, catalog);
            dispatcher.Tick(new[] { cue, cue });
            Require(bone.transform.childCount == 1, "Duplicate event created another effect.");
            dispatcher.Register(1, null, null);
            dispatcher.Tick(Array.Empty<TimelinePresentationEvent>());
            Require(bone.transform.childCount == 0, "Removed owner retained an attached effect.");
            dispatcher.Register(1, renderer, catalog);
            dispatcher.Tick(new[] { cue with { AttackSequence = 2 } });
            Require(bone.transform.childCount == 0, "Late event attached to another attack.");
            var worldLifetime = new PresentationLifetime(3, false, 1, 0);
            Require(worldLifetime.Advance(false, ActionState.Idle, 0, 0)
                && worldLifetime.Advance(false, ActionState.Idle, 0, 0)
                && !worldLifetime.Advance(false, ActionState.Idle, 0, 0),
                "World contact lifetime incorrectly depended on its attack owner.");
        }
        finally
        {
            preview.Clear();
            dispatcher.Clear();
            UnityEngine.Object.DestroyImmediate(catalog);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void VerifyWorkspaceStatus()
    {
        var instance = typeof(AbilityLab).GetProperty(nameof(AbilityLab.Instance), BindingFlags.Public | BindingFlags.Static)!;
        var originalLab = AbilityLab.Instance;
        instance.SetValue(null, null);
        try
        {
            var workspace = new AbilityLabPackageWorkspace();
            Require(workspace.OpenPackage("Assets/CharacterPackages/fightguy"), "FightGuy package prerequisite is unavailable.");
            Require(workspace.Status == "Valid", "Cook FightGuy before checking persisted status.");
            var draft = workspace.Draft;
            Require(workspace.PrepareScenarioPreview() && workspace.Status == "Valid", "Preparing a cooked preview falsely marked it stale.");
            workspace.RefreshPersistedStatus(true);
            Require(ReferenceEquals(draft, workspace.Draft) && !workspace.IsDirty && !workspace.CanUndo && workspace.Status == "Valid",
                "Refreshing external cook status replaced the draft or Undo history.");
            workspace.SetDraft(draft with { DisplayName = draft.DisplayName + " VFX test" });
            var edited = workspace.Draft;
            workspace.RefreshPersistedStatus(true);
            Require(ReferenceEquals(edited, workspace.Draft) && workspace.IsDirty && workspace.CanUndo && workspace.Status == "Stale",
                "Persisted status overwrote an unsaved edit or reported it cooked.");
            workspace.Undo();
            Require(workspace.Draft.DisplayName == draft.DisplayName && workspace.CanRedo,
                "Status refresh damaged draft Undo.");
        }
        finally { instance.SetValue(null, originalLab); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
