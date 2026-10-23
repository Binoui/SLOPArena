using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Tools;

namespace SlopArena.EditorTools;

public static class AbilityLabScenarioCommandSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Ability Lab Scenario Commands")]
    public static void Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Ability Lab scenario command self-test requires Edit Mode.");

        AbilityLabWindow? window = AbilityLabWindow.FindExistingForCommand();
        if (window == null || !window.CommandWorkspace.HasPackage || window.CommandWorkspace.LiveDraftInvalid ||
            window.CommandWorkspace.Preview?.IsAvailable != true)
            throw new InvalidOperationException("Open a valid Character Package in Ability Lab before running this self-test.");
        AbilityLab? lab = window.CommandLab;
        if (lab == null || !lab.IsPackagePreview || lab.SelectedPackageId != window.CommandWorkspace.PackageId)
            throw new InvalidOperationException("Ability Lab package preview rig is unavailable.");

        var inspection = SlopArenaAbilityLabCommands.Inspect();
        string? action = inspection.Slots?.Find(slot => slot.Available)?.Id;
        if (action == null)
            throw new InvalidOperationException("The open package has no available canonical action to simulate.");

        var originalCursor = lab.CaptureTimelineCursor();
        var originalCamera = lab.CaptureCameraState();
        bool originalHurtboxes = lab.ShowHurtboxes;
        bool originalHitboxes = lab.ShowHitboxes;
        bool originalBakedBones = lab.ShowBakedBones;
        bool originalDummy = lab.ShowDummy;
        Camera? previewCamera = lab.PreviewCamera;
        Vector3 cameraPosition = previewCamera != null ? previewCamera.transform.position : default;
        Quaternion cameraRotation = previewCamera != null ? previewCamera.transform.rotation : default;
        RenderTexture? cameraTarget = previewCamera != null ? previewCamera.targetTexture : null;
        float cameraAspect = previewCamera != null ? previewCamera.aspect : 0f;
        bool hadCamera = previewCamera != null;
        string output = $".ability-lab-cache/scenario-command-selftest-{Guid.NewGuid():N}";
        string? outputDirectory = null;
        try
        {
            var run = SlopArenaAbilityLabCommands.Run(action, ticks: 24, distance: 0.5f,
                opponent: "idle", damage: 0, facing: 180f);

            AbilityLabCommandScenario runScenario = run.Scenario ?? throw new InvalidOperationException("Native run returned no scenario data.");
            if (!run.Success || runScenario.Action != action ||
                runScenario.Frames.Count != 25 || runScenario.Frames[0].FrameIndex != 0 ||
                runScenario.Frames[0].MatchTick != 1 || run.ScenarioFrame != 24)
                throw new InvalidOperationException("Native run did not return the actual frame-zero/MatchTick-one Shared scenario and leave preview at its last frame.");
            AbilityLabScenarioResult recorded = lab.Scenario ?? throw new InvalidOperationException("Native run was not retained by Ability Lab.");
            if (recorded.Frames.Count != runScenario.Frames.Count || lab.ScenarioFrame != 24)
                throw new InvalidOperationException("Native run result does not match the live recorded scenario.");
            AssertOffscreenSkeletonMoves(lab, recorded);
            AssertAimReleasePoseMatchesPlayback(lab);
            AssertAttachmentPhasePreviewAndRuntime(lab);
            if (recorded.Contacts.Any(contact => !contact.Hit.Blocked))
            {
                if (!runScenario.Contacts.Any(contact => !contact.Blocked && contact.Damage > 0f)
                    || !runScenario.Frames.Any(frame => frame.Opponent.Damage > 0))
                    throw new InvalidOperationException("Native hit verdict omitted contact or applied opponent damage.");
            }

            var beforeInvalid = lab.CaptureTimelineCursor();
            AbilityLabScenarioResult beforeInvalidScenario = lab.Scenario ?? throw new InvalidOperationException("Recorded scenario disappeared.");
            var invalid = SlopArenaAbilityLabCommands.Run(action, ticks: AbilityLabScenarioOptions.MaxFrame + 1);
            if (invalid.Success || !ReferenceEquals(beforeInvalidScenario, lab.Scenario) || lab.ScenarioFrame != 24)
                throw new InvalidOperationException("Out-of-range scenario input succeeded or mutated the live scenario/cursor.");
            var invalidOpponent = SlopArenaAbilityLabCommands.Run(action, opponent: "other");
            if (invalidOpponent.Success || !ReferenceEquals(beforeInvalidScenario, lab.Scenario) || lab.ScenarioFrame != 24)
                throw new InvalidOperationException("Invalid opponent input mutated the recorded scenario/cursor.");
            var invalidDistance = SlopArenaAbilityLabCommands.Run(action, distance: -0.01f);
            if (invalidDistance.Success || !ReferenceEquals(beforeInvalidScenario, lab.Scenario) || lab.ScenarioFrame != 24)
                throw new InvalidOperationException("Invalid opponent distance mutated the recorded scenario/cursor.");
            lab.RestoreTimelineCursor(beforeInvalid);
            SlopArena.Shared.CanonicalSlotProjection.TryGet(action, out var address);
            int index = Array.IndexOf(AbilityLab.SlotNames, address.InputLabel);
            var spec = lab.Def.GetCookedSlotAbility(CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[index]), address.IsAirborne);
            var originalDefinition = lab.Def;
            var missingStage = new CookedStage(spec.Timeline.Stages[0].DurationTicks, 0, 0, 0, 0,
                new[] { "selftest.missing.animation" }, spec.Timeline.Stages[0].Operations);
            var missingMove = PresentationSelfTestContent.Replace(spec, new CookedTimeline(new[] { missingStage }), spec.AimAnimationId);
            var missingSlots = originalDefinition.CookedSlots.ToArray();
            missingSlots[spec.Ordinal] = missingMove;
            try
            {
                SetDefinition(lab, originalDefinition with { CookedSlots = missingSlots });
                var missingBinding = SlopArenaAbilityLabCommands.Run(action, ticks: 12);
                if (missingBinding.Success || !ReferenceEquals(beforeInvalidScenario, lab.Scenario) || lab.ScenarioFrame != 24)
                    throw new InvalidOperationException("A missing animation binding replaced the active run before failing.");
            }
            finally { SetDefinition(lab, originalDefinition); }

            var preview = SlopArenaAbilityLabCommands.Preview(action, 4);
            if (!preview.Success || lab.ScenarioFrame != 4 || preview.ScenarioFrame != 4)
                throw new InvalidOperationException("Native preview did not seek the recorded scenario frame.");

            if (lab.PreviewCamera == null)
                throw new InvalidOperationException("Ability Lab camera is unavailable for capture restoration coverage.");
            outputDirectory = SlopArenaAbilityLabCommands.TryResolveCaptureDirectory(output, out string resolved, out string error)
                ? resolved : throw new InvalidOperationException(error);
            lab.ShowHurtboxes = !originalHurtboxes;
            lab.ShowHitboxes = !originalHitboxes;
            lab.ShowBakedBones = !originalBakedBones;
            lab.ShowDummy = !originalDummy;
            var capture = CaptureFramed(lab, action, output);
            if (!capture.Success || capture.Captures?.Count != 2 || capture.Captures[0].Frame?.FrameIndex != 0 ||
                capture.Captures[0].MatchTick != 1 || capture.Captures[1].Frame?.FrameIndex != 4 ||
                !ReferenceEquals(recorded, lab.Scenario) || lab.ScenarioFrame != 4)
                throw new InvalidOperationException("Capture did not report recorded scenario frames or restore its scenario cursor.");
            if (lab.ShowHurtboxes != !originalHurtboxes || lab.ShowHitboxes != !originalHitboxes ||
                lab.ShowBakedBones != !originalBakedBones || lab.ShowDummy != !originalDummy)
                throw new InvalidOperationException("Capture changed Ability Lab visibility before caller-state restoration.");
            if (hadCamera && (lab.PreviewCamera == null || lab.PreviewCamera.transform.position != cameraPosition ||
                              lab.PreviewCamera.transform.rotation != cameraRotation ||
                              lab.PreviewCamera.targetTexture != cameraTarget || lab.PreviewCamera.aspect != cameraAspect))
                throw new InvalidOperationException("Capture did not restore the camera transform, target, and aspect.");
            AssertDistinctImages(capture.Captures[0].Path, capture.Captures[1].Path);
            var shortRun = SlopArenaAbilityLabCommands.Run(action, ticks: 12, distance: 9f,
                opponent: "shield", damage: 7, facing: 90f);
            if (!shortRun.Success) throw new InvalidOperationException("Cross-action fixture could not run.");
            var beforeCross = lab.Scenario;
            var cross = SlopArenaAbilityLabCommands.Capture("grab", "19", output + "/cross", 320, 240);
            if (!cross.Success || cross.Captures[0].Frame?.FrameIndex != 19
                || !ReferenceEquals(beforeCross, lab.Scenario) || lab.ScenarioFrame != 12)
                throw new InvalidOperationException("Default grab capture inherited another action's horizon or failed restoration.");
            int grabHorizon = DefenseConfig.GrabStartupTicks + DefenseConfig.GrabHoldTicks + DefenseConfig.ThrowReleaseTicks + 1;
            var grabRun = SlopArenaAbilityLabCommands.Run("grab", ticks: grabHorizon, distance: 0.7f);
            if (!grabRun.Success || !grabRun.Scenario.Interactions.Any(observation => observation.Kind == "capture")
                || !grabRun.Scenario.Interactions.Any(observation => observation.Kind == "release")
                || !grabRun.Scenario.Frames.Any(frame => frame.Opponent.Damage > 0))
                throw new InvalidOperationException("Native grab verdict lost observed capture, release or damage.");
            Debug.Log("[AbilityLabScenarioCommandSelfTest] Passed native run/seek/capture, frame chronology, invalid-input non-mutation and complete restoration.");
        }
        finally
        {
            lab.RestoreTimelineCursor(originalCursor);
            lab.RestoreCameraState(originalCamera);
            lab.ShowHurtboxes = originalHurtboxes;
            lab.ShowHitboxes = originalHitboxes;
            lab.ShowBakedBones = originalBakedBones;
            lab.ShowDummy = originalDummy;
            window.RefreshScenarioControls();
            if (outputDirectory != null && Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    internal static void AssertRunRevalidatesPreview(AbilityLabWindow window, Action recook)
    {
        window.Show();
        window.Focus();
        if (!ReferenceEquals(AbilityLabWindow.FindExistingForCommand(), window))
            throw new InvalidOperationException("The isolated refresh fixture must be the command-selected Ability Lab window.");
        var workspace = window.CommandWorkspace;
        var lab = window.CommandLab ?? throw new InvalidOperationException("CLI freshness fixture has no Lab.");
        var retained = workspace.Preview;
        recook();
        typeof(AbilityLabWindow).GetField("_uiReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
        try { workspace.SetDraft(workspace.Draft with { DisplayName = "CLI admission before polling" }); }
        finally { typeof(AbilityLabWindow).GetField("_uiReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true); }
        if (workspace.PreviewPrerequisitesStale || !lab.CanRunScenario || lab.Scenario != null)
            throw new InvalidOperationException("CLI freshness fixture observed persisted status before the command.");
        lab.Playing = true;
        var result = SlopArenaAbilityLabCommands.Run("ground.1", ticks: 4);
        if (result.Success || !result.Diagnostics.Any(diagnostic => diagnostic.Code == "preview.unavailable") ||
            !workspace.PreviewPrerequisitesStale || !ReferenceEquals(retained, workspace.Preview) ||
            lab.CanRunScenario || lab.Playing || lab.Scenario != null || !workspace.IsDirty)
            throw new InvalidOperationException("CLI Run admitted stale prerequisites or lost the retained dirty draft.");
        if (!workspace.DiscardUnsavedChanges())
            throw new InvalidOperationException("Could not discard the isolated CLI admission edit.");
        var recovered = SlopArenaAbilityLabCommands.Run("ground.1", ticks: 4);
        if (!recovered.Success || lab.Scenario == null || lab.Scenario.Frames.Count != 5 ||
            workspace.PreviewPrerequisitesStale || workspace.Preview.Identity.PackageHash != workspace.PackageHash ||
            ReferenceEquals(retained, workspace.Preview))
            throw new InvalidOperationException("CLI Run did not recover current prerequisites after Discard.");
        lab.ExitScenario();
    }

    private static void AssertOffscreenSkeletonMoves(AbilityLab lab, AbilityLabScenarioResult recorded)
    {
        var source = lab.GetComponentsInChildren<PlayerRenderer>().First(renderer => renderer.gameObject.activeInHierarchy);
        var catalog = (CharacterAnimationCatalog)typeof(PlayerRenderer)
            .GetField("_animationCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
        var sourceModel = source.GetComponentInChildren<Animator>().gameObject;
        var clip = source.GetComponentInChildren<Animancer.AnimancerComponent>().States.Current.Clip;
        string animationId = catalog.Animations.First(entry => entry.Clip == clip).SemanticId;
        var fixture = new GameObject("OffscreenScrubRegression") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var renderer = fixture.AddComponent<PlayerRenderer>();
            if (!renderer.Configure(source.CharacterDef, source.BakedData, catalog, sourceModel, out var error))
                throw new InvalidOperationException(error);
            foreach (var mesh in fixture.GetComponentsInChildren<Renderer>()) mesh.forceRenderingOff = true;
            var animator = fixture.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var bones = new[] { HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.Head };
            Vector3[] ReadSkeleton() => bones.Select(bone =>
                animator.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position)).ToArray();
            void AssertMoved(Vector3[] before, string path)
            {
                var after = ReadSkeleton();
                if (!before.Where((position, index) => (position - after[index]).sqrMagnitude > 0.000001f).Any())
                    throw new InvalidOperationException($"{path} advanced animation time but left the offscreen skeleton frozen.");
                if (animator.cullingMode != AnimatorCullingMode.CullUpdateTransforms)
                    throw new InvalidOperationException($"{path} did not restore the Animator culling policy.");
            }

            renderer.PlayScrubbed(animationId, 0.1f);
            var authoringPose = ReadSkeleton();
            renderer.PlayScrubbed(animationId, 0.7f);
            AssertMoved(authoringPose, "Authoring scrub");

            SlopArena.Shared.CanonicalSlotProjection.TryGet(recorded.Options.Action, out var address);
            var first = recorded.Frames[0];
            var last = recorded.Frames[recorded.Frames.Count - 1];
            if (!renderer.PlayScrubbedState(first.Actor, address.IsAirborne, first.ActorPoseTicks))
                throw new InvalidOperationException("Recorded first pose is unavailable.");
            var recordedPose = ReadSkeleton();
            if (!renderer.PlayScrubbedState(last.Actor, address.IsAirborne, last.ActorPoseTicks))
                throw new InvalidOperationException("Recorded final pose is unavailable.");
            AssertMoved(recordedPose, "Recorded-frame scrub");

            // Visual root offsets must not move the shared simulation hip pivot.
            renderer.ModelYOffset = .2f;
            var unpitched = first.Actor with { FacingYaw = .7f, AttackPosePitch = 0f };
            renderer.PlayScrubbedState(unpitched, address.IsAirborne, first.ActorPoseTicks);
            var worldBefore = bones.Select(bone => animator.GetBoneTransform(bone).position).ToArray();
            Vector3 rootBefore = renderer.transform.position;
            var def = source.CharacterDef;
            Vector3 pivot = new(unpitched.PX, def.BoneYToWorldY(unpitched.PY, 0f), unpitched.PZ);
            Vector3 right = new(Mathf.Cos(unpitched.FacingYaw), 0f, -Mathf.Sin(unpitched.FacingYaw));
            foreach (float pitch in new[] { -Mathf.PI / 12f, Mathf.PI / 12f })
            {
                renderer.PlayScrubbedState(unpitched with { AttackPosePitch = pitch },
                    address.IsAirborne, first.ActorPoseTicks);
                Quaternion rotation = Quaternion.AngleAxis(-pitch * Mathf.Rad2Deg, right);
                for (int index = 0; index < bones.Length; index++)
                {
                    Vector3 expected = pivot + rotation * (worldBefore[index] - pivot);
                    Vector3 actual = animator.GetBoneTransform(bones[index]).position;
                    if ((expected - actual).sqrMagnitude > .000001f)
                        throw new InvalidOperationException($"Pitched {bones[index]} diverged from the shared hip pivot.");
                }
                if ((renderer.transform.position - rootBefore).sqrMagnitude > .000001f)
                    throw new InvalidOperationException("Pitch moved the authoritative entity root.");
            }
            renderer.PlayScrubbedState(unpitched, address.IsAirborne, first.ActorPoseTicks);
            for (int index = 0; index < bones.Length; index++)
                if ((animator.GetBoneTransform(bones[index]).position - worldBefore[index]).sqrMagnitude > .000001f)
                    throw new InvalidOperationException($"Cleared pitch leaked into {bones[index]}.");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }

    private static void AssertAimReleasePoseMatchesPlayback(AbilityLab lab)
    {
        if (lab.SelectedPackageId != "manki") return;
        var cursor = lab.CaptureTimelineCursor();
        var fixture = new GameObject("AimReleasePoseRegression") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var run = SlopArenaAbilityLabCommands.Run("ground.F", ticks: 24, distance: 12f);
            if (!run.Success) throw new InvalidOperationException("Aim-release scenario is unavailable.");
            var frame = lab.Scenario.Frames[18];
            var source = lab.Renderer;
            var catalog = (CharacterAnimationCatalog)typeof(PlayerRenderer)
                .GetField("_animationCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            var prefab = (GameObject)typeof(PlayerRenderer)
                .GetField("_modelPrefab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            var playback = fixture.AddComponent<PlayerRenderer>();
            if (!playback.Configure(source.CharacterDef, source.BakedData, catalog, prefab, out var error))
                throw new InvalidOperationException(error);
            var normal = fixture.GetComponentInChildren<Animancer.AnimancerComponent>();
            normal.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var spec = source.CharacterDef.GetCookedSlotAbility(frame.Actor.AttackSlot, false);
            typeof(PlayerRenderer).GetMethod("PlayAbilityAnim", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(playback, new object[] { frame.Actor, spec, (int)frame.Actor.ComboStage });
            normal.Evaluate(0f);
            normal.Evaluate(frame.Actor.AttackElapsedTicks / 60f);
            var bones = new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
            var expected = bones.Select(bone => normal.transform.InverseTransformPoint(
                normal.Animator.GetBoneTransform(bone).position)).ToArray();
            void AssertPose(string path)
            {
                var preview = source.GetComponentInChildren<Animancer.AnimancerComponent>();
                for (int index = 0; index < bones.Length; index++)
                {
                    Vector3 actual = preview.transform.InverseTransformPoint(
                        preview.Animator.GetBoneTransform(bones[index]).position);
                    if ((actual - expected[index]).sqrMagnitude > 0.000001f)
                        throw new InvalidOperationException(
                            $"{path} aim-release pose differs from normal playback at {bones[index]}.");
                }
            }
            lab.SeekScenario(18);
            AssertPose("Recorded");
            lab.ExitScenario();
            var authoring = SlopArenaAbilityLabCommands.Preview("ground.F", 18);
            if (!authoring.Success) throw new InvalidOperationException("Aim-release authoring preview is unavailable.");
            AssertPose("Authoring");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
            lab.RestoreTimelineCursor(cursor);
        }
    }
    private static void AssertAttachmentPhasePreviewAndRuntime(AbilityLab lab)
    {
        if (lab.SelectedPackageId != "manki") return;
        var cursor = lab.CaptureTimelineCursor();
        var phaseTestModel = new GameObject("PhaseAttachmentRuntimeRegression") { hideFlags = HideFlags.HideAndDontSave };
        var weaponPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        weaponPrefab.hideFlags = HideFlags.HideAndDontSave;
        WeaponAttachConfig config = ScriptableObject.CreateInstance<WeaponAttachConfig>();
        try
        {
            AbilityLabScenarioResult scenario = lab.Scenario
                ?? throw new InvalidOperationException("Scenario-preservation phase fixture requires a recorded run.");
            int scenarioFrame = lab.ScenarioFrame;
            if (lab.SetPhasePreview(true) || !ReferenceEquals(lab.Scenario, scenario)
                || lab.ScenarioFrame != scenarioFrame)
                throw new InvalidOperationException("Authoring phase activation altered or replaced the recorded scenario.");
            lab.ExitScenario();
            if (!SlopArena.Shared.CanonicalSlotProjection.TryGet("ground.F", out var address))
                throw new InvalidOperationException("Manki F slot mapping is unavailable.");
            lab.SetSlot(address);
            var spec = lab.Def.GetCookedSlotAbility(CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[7]), false);
            if (spec == null || spec.ClipCount == 0 || string.IsNullOrEmpty(spec.AimAnimationId))
                throw new InvalidOperationException("Manki F does not expose both authored aim and release clips.");
            var source = lab.Renderer;
            var catalog = (CharacterAnimationCatalog)typeof(PlayerRenderer)
                .GetField("_animationCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            string ActualClipName(string semanticId)
                => catalog.Animations.First(entry => entry.SemanticId == semanticId).Clip.name;

            lab.SetAuthoringPhase(AbilityLab.AuthoringPhase.Charge);
            if (!lab.CanPreviewCharge || !lab.SetPhasePreview(true)
                || lab.PhaseClipName != ActualClipName(spec.AimAnimationId) || lab.PhaseDurationTicks < 19)
                throw new InvalidOperationException("Charge preview did not resolve the actual looping aim clip.");
            var animator = lab.Renderer.GetComponentInChildren<Animator>();
            Vector3[] ReadPose() => new[]
            {
                animator.GetBoneTransform(HumanBodyBones.Head).position,
                animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
                animator.GetBoneTransform(HumanBodyBones.RightHand).position
            };
            lab.SetPhaseTick(0);
            lab.SetPhaseTick(18);
            float chargeSampledTime = lab.Renderer.GetComponentInChildren<Animancer.AnimancerComponent>()
                .States.Current.Time;
            if (Mathf.Abs(chargeSampledTime - 0.3f) > 0.00001f)
                throw new InvalidOperationException($"Charge tick 18 sampled at {chargeSampledTime}s instead of 0.3s.");
            lab.SetPhaseTick(0);
            Vector3[] chargeStart = ReadPose();
            lab.SetPhaseTick(Math.Max(1, lab.PhaseDurationTicks / 3));
            Vector3[] chargeScrub = ReadPose();
            if (!chargeStart.Where((position, index) =>
                    (position - chargeScrub[index]).sqrMagnitude > 0.000001f).Any())
                throw new InvalidOperationException("Charge-local scrubbing did not update the actual preview skeleton.");

            lab.SetPhaseTick(0);
            typeof(AbilityLab).GetField("_playAccum", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(lab, 1f / AbilityLab.TickRate);
            lab.Playing = true;
            typeof(AbilityLab).GetMethod("AdvancePlayback", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(lab, null);
            lab.Playing = false;
            if (lab.PhaseTick != 1)
                throw new InvalidOperationException("Charge phase playback did not advance its independent local tick.");

            lab.SetAuthoringPhase(AbilityLab.AuthoringPhase.Fire);
            if (!lab.SetPhasePreview(true) || lab.PhaseClipName != ActualClipName(spec.Clip(0).AnimationId))
                throw new InvalidOperationException("Fire preview did not resolve the selected release clip.");
            lab.SetPhaseTick(18);
            if (!source.TryGetAbilityPhaseAnimation(
                (byte)((CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[7]) - 1) + 1), false, 0, false,
                out _, out _, out _, out float fireSpeed))
                throw new InvalidOperationException("Fire preview runtime speed mapping is unavailable.");
            float expectedFireTime = 18f / AbilityLab.TickRate * fireSpeed;
            float fireSampledTime = lab.Renderer.GetComponentInChildren<Animancer.AnimancerComponent>()
                .States.Current.Time;
            if (Mathf.Abs(fireSampledTime - expectedFireTime) > 0.00001f)
                throw new InvalidOperationException(
                    $"Fire tick 18 sampled at {fireSampledTime}s instead of runtime-mapped {expectedFireTime}s.");
            lab.SetPhaseTick(Math.Max(0, lab.PhaseDurationTicks / 3));
            Vector3[] firePose = ReadPose();
            if (!chargeStart.Where((position, index) =>
                    (position - firePose[index]).sqrMagnitude > 0.000001f).Any())
                throw new InvalidOperationException("Charge and fire clips did not produce distinct actual bone poses.");

            var originalDefinition = lab.Def;
            var missingAimSlots = originalDefinition.CookedSlots.ToArray();
            missingAimSlots[spec.Ordinal] = PresentationSelfTestContent.Replace(spec, spec.Timeline, null);
            try
            {
                SetDefinition(lab, originalDefinition with { CookedSlots = missingAimSlots });
                lab.SetAuthoringPhase(AbilityLab.AuthoringPhase.Charge);
                if (lab.CanPreviewCharge || lab.SetPhasePreview(true) || lab.PhaseClipName.Length != 0
                    || !lab.PhasePreviewStatus.Contains("no resolved aim clip"))
                    throw new InvalidOperationException("Missing charge clip silently fell back to the fire pose.");
                lab.SetAuthoringPhase(AbilityLab.AuthoringPhase.Fire);
                if (lab.PhaseClipName != ActualClipName(spec.Clip(0).AnimationId))
                    throw new InvalidOperationException("Missing charge clip made the valid fire preview unavailable.");
            }
            finally { SetDefinition(lab, originalDefinition); }

            var prefab = (GameObject)typeof(PlayerRenderer)
                .GetField("_modelPrefab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            var runtimeRenderer = phaseTestModel.AddComponent<PlayerRenderer>();
            if (!runtimeRenderer.Configure(source.CharacterDef, source.BakedData, catalog, prefab, out var error))
                throw new InvalidOperationException(error);
            var rightHand = runtimeRenderer.GetComponentInChildren<Animator>()
                .GetBoneTransform(HumanBodyBones.RightHand);
            config.Entries = new[]
            {
                new WeaponEntry
                {
                    AttackSlot = (byte)((CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[7]) - 1) + 1),
                    BoneName = rightHand.name,
                    Prefab = weaponPrefab,
                    PositionOffset = Vector3.zero,
                    HasFirePhaseOverride = true,
                    FirePositionOffset = new Vector3(0.07f, 0.18f, -0.04f),
                    FireRotationOffset = new Vector3(12f, 24f, 7f)
                }
            };
            var weaponAttach = phaseTestModel.AddComponent<WeaponAttach>();
            weaponAttach.Init(runtimeRenderer, config);
            typeof(PlayerRenderer).GetField("_lastAttackSlot", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(runtimeRenderer, (byte)((CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[7]) - 1) + 1));
            var actionState = typeof(PlayerRenderer).GetField("_lastAnimState", BindingFlags.Instance | BindingFlags.NonPublic)!;
            actionState.SetValue(runtimeRenderer, SlopArena.Shared.ActionState.Aiming);
            weaponAttach.RefreshPresentation();
            Vector3 sharedPosition = rightHand.TransformPoint(Vector3.zero);
            Vector3 aimingPosition = Position(weaponAttach);
            actionState.SetValue(runtimeRenderer, SlopArena.Shared.ActionState.Attacking);
            weaponAttach.RefreshPresentation();
            Vector3 firePosition = rightHand.TransformPoint(config.Entries[0].FirePositionOffset);
            Vector3 attackingPosition = Position(weaponAttach);
            if ((aimingPosition - sharedPosition).sqrMagnitude > 0.000001f
                || (attackingPosition - firePosition).sqrMagnitude > 0.000001f
                || (aimingPosition - attackingPosition).sqrMagnitude < 0.000001f)
                throw new InvalidOperationException("Runtime attachment did not select shared Aim and fire override Attack transforms.");
        }
        finally
        {
            lab.RestoreTimelineCursor(cursor);
            UnityEngine.Object.DestroyImmediate(phaseTestModel);
            UnityEngine.Object.DestroyImmediate(weaponPrefab);
            UnityEngine.Object.DestroyImmediate(config);
        }

        static Vector3 Position(WeaponAttach attach)
        {
            float[] position = attach.ReadInspectionEntries()[0].WorldPosition;
            return new Vector3(position[0], position[1], position[2]);
        }
    }


    private static AbilityLabCommandResult CaptureFramed(AbilityLab lab, string action, string output)
    {
        string? framingError = null;
        int renderedFrames = 0;
        Camera previewCamera = lab.PreviewCamera;
        int originalMask = previewCamera.cullingMask;
        void CheckFrame(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            if (camera.targetTexture == null) return;
            renderedFrames++;
            foreach (var renderer in lab.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer.forceRenderingOff) continue;
                if ((camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                    framingError = $"{renderer.name} is excluded by the capture camera.";
                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    Vector3 viewport = camera.WorldToViewportPoint(corner);
                    if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f ||
                        viewport.z < camera.nearClipPlane || viewport.z > camera.farClipPlane)
                        framingError = $"{renderer.name} is cropped at scenario frame {lab.ScenarioFrame}: {viewport}.";
                }
            }
        }
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += CheckFrame;
        previewCamera.cullingMask = 0;
        try
        {
            var result = SlopArenaAbilityLabCommands.Capture(action, "0,4", output, 320, 240);
            if (framingError != null) throw new InvalidOperationException(framingError);
            if (result.Success && renderedFrames != 2)
                throw new InvalidOperationException("Capture framing was not observed for both requested frames.");
            return result;
        }
        finally
        {
            previewCamera.cullingMask = originalMask;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= CheckFrame;
        }
    }

    private static void AssertDistinctImages(string firstPath, string secondPath)
    {
        string root = Path.GetFullPath(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "..", ".."));
        var first = new Texture2D(2, 2);
        var second = new Texture2D(2, 2);
        try
        {
            if (!first.LoadImage(File.ReadAllBytes(Path.Combine(root, firstPath)))
                || !second.LoadImage(File.ReadAllBytes(Path.Combine(root, secondPath))))
                throw new InvalidOperationException("Native capture did not produce decodable PNGs.");
            var a = first.GetPixels32();
            var b = second.GetPixels32();
            bool distinct = false;
            for (int i = 0; i < a.Length && !distinct; i++) distinct = !a[i].Equals(b[i]);
            if (!distinct) throw new InvalidOperationException("Distinct recorded poses produced identical captured pixels.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }
    private static void SetDefinition(AbilityLab lab, CharacterDefinition definition)
    {
        typeof(AbilityLab).GetProperty(nameof(AbilityLab.Def)).SetValue(lab, definition);
        typeof(AbilityLab).GetProperty(nameof(AbilityLab.DisplayDef)).SetValue(lab, definition);
    }

}
