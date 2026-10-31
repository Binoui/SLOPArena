using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using SlopArena.Client.Tools;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools;

public static partial class SlopArenaAbilityLabCommands
{
    [CliCommand(
        "sloparena.lab.open",
        "Open a Character Package in the Ability Lab without saving or cooking it.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/lab" })]
    public static AbilityLabCommandResult Open(
        [CliArg("target", "Package ID or package root inside Assets/CharacterPackages.", Required = true)] string target)
    {
        AbilityLabWindow? window = AbilityLabWindow.FindExistingForCommand();
        if (!CanMutate(out var modeError))
            return Failure("lab.mode.unsupported", modeError, window);
        try
        {
            var inspection = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(target);
            if (!inspection.Success || inspection.Source == null || inspection.Catalog == null)
            {
                var failure = Snapshot(window);
                failure.Success = false;
                foreach (var diagnostic in inspection.Diagnostics)
                    failure.Diagnostics.Add(new AbilityLabCommandDiagnostic(
                        diagnostic.Severity, diagnostic.Code, diagnostic.Path, diagnostic.Message));
                return failure;
            }

            if (window != null && !AbilityLabWindow.CanCommandOpenPackage(
                    window.CommandWorkspace.HasPackage,
                    window.CommandWorkspace.PackageId,
                    window.CommandWorkspace.IsDirty,
                    inspection.PackageId))
                return Failure("workspace.dirty",
                    $"Cannot switch from dirty package '{window.CommandWorkspace.PackageId}' to '{inspection.PackageId}'.", window);

            window ??= AbilityLabWindow.OpenForCommand();
            if (!window.EnsureCommandUi())
                return Failure("lab.window.unavailable", "Ability Lab window could not initialize its UI.", window);
            if (!window.OpenPackageForCommand(inspection, out string errorCode, out string errorMessage))
                return Failure(errorCode, errorMessage, window);
            if (!window.EnsureLabForCommand(out _, out _))
                return Failure("lab.rig.unavailable", "Ability Lab could not create or select its preview rig.", window);
            return Snapshot(window);
        }
        catch (Exception ex)
        {
            return Failure("lab.open.failed", ex.Message, window);
        }
    }

    [CliCommand("sloparena.lab.form", "Select the package form shown and edited in Ability Lab.",
        MainThreadRequired = true, Tags = new[] { "authoring/lab" })]
    public static AbilityLabCommandResult Form(
        [CliArg("form", "Form index: 0 or 1.", Required = true)] int form)
    {
        var window = AbilityLabWindow.FindExistingForCommand();
        if (!CanMutate(out var modeError)) return Failure("lab.mode.unsupported", modeError, window);
        if (form < 0 || form > 1 || window == null || !window.SelectFormForCommand((byte)form))
            return Failure("lab.form.unavailable", "Open a package containing the requested form first.", window);
        return Snapshot(window);
    }

[CliCommand(
        "sloparena.lab.run",
        "Run an authoritative Shared scenario against an idle or shielding mirror opponent.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/lab" })]
    public static AbilityLabCommandResult Run(
        [CliArg("action", "Canonical action ID, for example ground.1 or air.R, or grab.", Required = true)] string action,
        [CliArg("ticks", "Last zero-based scenario frame (0–3600).", Required = false, DefaultValue = 60)] int ticks = 60,
        [CliArg("distance", "Opponent distance in metres.", Required = false, DefaultValue = 2.5f)] float distance = 2.5f,
        [CliArg("opponent", "Opponent behavior: idle or shield.", Required = false, DefaultValue = "idle")] string opponent = "idle",
        [CliArg("facing", "Relative facing angle in degrees.", Required = false, DefaultValue = 180f)] float facing = 180f,
        [CliArg("damage", "Opponent initial damage (0–999).", Required = false, DefaultValue = 0)] int damage = 0,
        [CliArg("charge-ticks", "Held charge duration (0–600 input frames).", Required = false, DefaultValue = 0)] int chargeTicks = 0)
    {
        AbilityLabWindow? window = AbilityLabWindow.FindExistingForCommand();
        if (!CanMutate(out var modeError))
            return Failure("lab.mode.unsupported", modeError, window);
        if (string.IsNullOrEmpty(action))
            return Failure("scenario.action.invalid", "Action must be a canonical slot ID or 'grab'.", window);
        if (window == null || !window.CommandWorkspace.HasPackage)
            return Failure("workspace.missing", "Open a Character Package with sloparena.lab.open first.", window);
        if (window.CommandWorkspace.LiveDraftInvalid || window.CommandWorkspace.Preview?.IsAvailable != true)
            return Failure("preview.unavailable", "The current source draft has no available Ability Lab preview.", window);
        if (damage is < 0 or > 999)
            return Failure("scenario.damage.invalid", "Opponent damage must be in [0, 999].", window);
        AbilityLabOpponentBehavior behavior;
        if (string.Equals(opponent, "idle", StringComparison.OrdinalIgnoreCase))
            behavior = AbilityLabOpponentBehavior.Idle;
        else if (string.Equals(opponent, "shield", StringComparison.OrdinalIgnoreCase))
            behavior = AbilityLabOpponentBehavior.Shield;
        else
            return Failure("scenario.opponent.invalid", "Opponent must be 'idle' or 'shield'.", window);

        AbilityLabScenarioOptions options;
        try
        {
            options = new AbilityLabScenarioOptions(action, ticks, distance, behavior, (ushort)damage, facing, chargeTicks);
            options.Validate();
        }
        catch (ArgumentException ex)
        {
            return Failure("scenario.options.invalid", ex.Message, window);
        }

        if (!window.CommandWorkspace.PrepareScenarioPreview())
        {
            window.CommandLab?.MarkPackageDraftInvalid();
            window.RefreshScenarioControls();
            return Failure("preview.unavailable", "The current source draft has no available Ability Lab preview.", window);
        }
        AbilityLab? lab = window.CommandLab;
        if (lab == null || !lab.IsPackagePreview || lab.SelectedPackageId != window.CommandWorkspace.PackageId)
            return Failure("lab.rig.missing", "Open the package in Ability Lab before running a scenario.", window);
        if (action == "grab")
        {
            if (!lab.CanPreviewGrab)
                return Failure("scenario.action.unavailable", "Shared grab is unavailable for the current preview.", window);
        }
        else
        {
            if (!TryBuildTimeline(window, action, out var address, out _, out string actionCode, out string actionError) ||
                !IsPackageSlotAvailable(window.CommandWorkspace, address))
                return Failure(actionCode, actionError.Length == 0 ? $"Action '{action}' is unavailable in the current draft." : actionError, window);
        }

        try
        {
            AbilityLabScenarioResult scenario = lab.RunScenario(options);
            window.RefreshScenarioControls();
            SceneView.RepaintAll();
            return Snapshot(window, scenario);
        }
        catch (Exception ex)
        {
            return Failure("scenario.run.failed", ex.Message, window);
        }
    }

    [CliCommand(
        "sloparena.lab.preview",
        "Select an action and seek its timeline/scenario tick; optionally configure supported preview overlays and dummy visibility.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/lab" })]
    public static AbilityLabCommandResult Preview(
        [CliArg("action", "Canonical action ID, for example ground.1 or air.R, or grab.", Required = true)] string action,
        [CliArg("tick", "Cumulative authoring tick or recorded scenario frame.", Required = true)] int tick,
        [CliArg("overlays", "'current', 'none', or comma-separated hitboxes,hurtboxes,bones,trajectory. 'none' also hides the coupled dummy hurtbox overlay.")] string overlays = null,
        [CliArg("dummy", "'on' or 'off' for preview opponent and its coupled hurtbox overlay; omit to retain current.")] string dummy = null)
    {
        AbilityLabWindow? window = AbilityLabWindow.FindExistingForCommand();
        if (!CanMutate(out var modeError))
            return Failure("lab.mode.unsupported", modeError, window);
        if (!TryParseViewOptions(null, null, null, overlays, dummy, out var viewOptions, out string viewError))
            return Failure("preview.options.invalid", viewError, window);
        if (window == null || !window.CommandWorkspace.HasPackage)
            return Failure("workspace.missing", "Open a Character Package with sloparena.lab.open first.", window);
        if (window.CommandWorkspace.LiveDraftInvalid || window.CommandWorkspace.Preview?.IsAvailable != true)
            return Failure("preview.unavailable", "The current source draft has no available Ability Lab preview.", window);

        AbilityLab? lab = window.CommandLab;
        AbilityLabScenarioResult? currentScenario = lab?.Scenario;
        if (currentScenario != null && currentScenario.Options.Action == action)
        {
            if (lab == null || !lab.IsPackagePreview || lab.SelectedPackageId != window.CommandWorkspace.PackageId)
                return Failure("lab.rig.missing", "The recorded scenario is not attached to the current package preview.", window);
            if (tick < 0 || tick >= currentScenario.Frames.Count)
                return Failure("preview.tick.out-of-range",
                    $"Scenario frame must be in [0, {currentScenario.Frames.Count - 1}] for action '{action}'.", window);
            try
            {
                lab!.SeekScenario(tick);
                ApplyViewFlags(lab, viewOptions);
                window.RefreshScenarioControls();
                SceneView.RepaintAll();
                return Snapshot(window);
            }
            catch (Exception ex)
            {
                return Failure("lab.preview.failed", ex.Message, window);
            }
        }

        if (action == "grab")
        {
            int maxFrame = currentScenario?.Options.Action == "grab" ? currentScenario.Frames.Count - 1 : 60;
            if (tick < 0 || tick > maxFrame)
                return Failure("preview.tick.out-of-range",
                    $"Scenario frame must be in [0, {maxFrame}] for action 'grab'.", window);
            if (lab != null && !lab.CanPreviewGrab)
                return Failure("scenario.action.unavailable", "Shared grab is unavailable for the current preview.", window);
            try
            {
                if (!window.EnsureCommandUi() ||
                    !window.EnsureLabForCommand(out lab, out _) || lab == null)
                    return Failure("lab.rig.unavailable", "Ability Lab could not create or select its preview rig.", window);
                if (!lab.CanPreviewGrab)
                    return Failure("scenario.action.unavailable", "Shared grab is unavailable for the current preview.", window);
                lab.RunScenario(new AbilityLabScenarioOptions("grab"));
                lab.SeekScenario(tick);
                ApplyViewFlags(lab, viewOptions);
                window.RefreshScenarioControls();
                SceneView.RepaintAll();
                return Snapshot(window);
            }
            catch (Exception ex)
            {
                return Failure("lab.preview.failed", ex.Message, window);
            }
        }

        if (!TryValidateTimeline(window, action, tick, out var address, out var projection, out string code, out string message))
            return Failure(code, message, window);

        lab = window.CommandLab;
        bool created = false;
        AbilityLab.TimelineCursor cursor = default;
        bool hasCursor = false;
        try
        {
            if (!window.EnsureCommandUi())
                return Failure("lab.window.unavailable", "Ability Lab window could not initialize its UI.", window);
            if (!window.EnsureLabForCommand(out lab, out created) || lab == null || !IsRuntimeSlotAvailable(window, lab, address))
            {
                window.DestroyTemporaryLabForCommand(lab!, created);
                return Failure("slot.unavailable", $"Slot '{action}' is not available in the current runtime preview.", window);
            }

            cursor = lab.CaptureTimelineCursor();
            hasCursor = true;
            window.ApplyCommandTimeline(action, address, projection, tick);
            window.CompleteCommandPreview(address);
            ApplyViewFlags(lab, viewOptions);
            window.RefreshScenarioControls();
            var result = Snapshot(window);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            if (hasCursor && lab != null)
            {
                try { lab.RestoreTimelineCursor(cursor); } catch { }
                window.RefreshScenarioControls();
            }
            window.DestroyTemporaryLabForCommand(lab!, created);
            return Failure("lab.preview.failed", ex.Message, window);
        }
    }

    [CliCommand(
        "sloparena.lab.inspect",
        "Read current Lab workspace/preview and evaluated actor presentation; optional named bones, without refreshing or opening anything.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/lab" })]
    public static AbilityLabCommandResult Inspect(
        [CliArg("bones", "Comma-separated presentation bone names or bone.* aliases to observe; omit for actor/attachment snapshot.")]
        string bones = null)
    {
        var window = AbilityLabWindow.FindExistingForCommand();
        var result = Snapshot(window);
        var names = string.IsNullOrWhiteSpace(bones) ? Array.Empty<string>()
            : bones.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal).ToArray();
        var observed = SlopArenaPresentationCommands.ReadLab(
            window != null ? window.CommandLab : AbilityLab.Instance, names);
        result.Presentation = observed.Actor;
        if (!observed.Success)
            result.PresentationError = observed.Error;
        return result;
    }

[CliCommand(
        "sloparena.lab.capture",
        "Render authoring/scenario PNGs with explicit actor-relative view or world camera angles and temporary overlay controls.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/lab" })]
    public static AbilityLabCommandResult Capture(
        [CliArg("action", "Canonical action ID, for example ground.1 or air.R, or grab.", Required = true)] string action,
        [CliArg("ticks", "Comma-separated distinct cumulative ticks or scenario frame indexes.", Required = true)] string ticks,
        [CliArg("output", "Repository-relative output directory below .ability-lab-cache.", Required = true)] string output,
        [CliArg("width", "PNG width (64–4096).", Required = false, DefaultValue = 1280)] int width = 1280,
        [CliArg("height", "PNG height (64–4096).", Required = false, DefaultValue = 720)] int height = 720,
        [CliArg("view", "'current', 'front', 'back', 'left', 'right', 'top', or 'bottom', relative to the actor's facing; omit to retain camera orientation.")] string view = null,
        [CliArg("camera-yaw", "Absolute world camera yaw in degrees [-180,180]; cannot combine with --view.")] string cameraYaw = null,
        [CliArg("camera-pitch", "Absolute world camera pitch in degrees [-90,90]; cannot combine with --view.")] string cameraPitch = null,
        [CliArg("overlays", "'current', 'none', or comma-separated hitboxes,hurtboxes,bones,trajectory. 'none' also hides the coupled dummy hurtbox overlay.")] string overlays = null,
        [CliArg("dummy", "'on' or 'off' for the rendered opponent and its coupled hurtbox overlay; omit to retain current.")] string dummy = null,
        [CliArg("charge-ticks", "Held charge duration for a new scenario capture (0–600); omit to retain a recorded scenario.", Required = false, DefaultValue = -1)] int chargeTicks = -1)
    {
        AbilityLabWindow? window = AbilityLabWindow.FindExistingForCommand();
        if (chargeTicks is < -1 or > AbilityLabScenarioOptions.MaxChargeTicks)
            return Failure("scenario.charge-ticks.invalid", "Charge ticks must be omitted or in [0, 600].", window);
        if (!CanMutate(out var modeError))
            return Failure("lab.mode.unsupported", modeError, window);
        if (!TryParseViewOptions(view, cameraYaw, cameraPitch, overlays, dummy, out var viewOptions, out string viewError))
            return Failure("capture.options.invalid", viewError, window);
        if (width is < 64 or > 4096 || height is < 64 or > 4096)
            return Failure("capture.dimensions.invalid", "Capture width and height must each be between 64 and 4096.", window);
        if (!TryParseTicks(ticks, out var requestedTicks, out string tickError))
            return Failure("capture.ticks.invalid", tickError, window);
        if (!TryResolveCaptureDirectory(output, out string outputDirectory, out string outputError))
            return Failure("capture.output.invalid", outputError, window);
        if (window == null || !window.CommandWorkspace.HasPackage)
            return Failure("workspace.missing", "Open a Character Package with sloparena.lab.open first.", window);
        if (window.CommandWorkspace.LiveDraftInvalid || window.CommandWorkspace.Preview?.IsAvailable != true)
            return Failure("preview.unavailable", "The current source draft has no available Ability Lab preview.", window);

        AbilityLab? lab = window.CommandLab;
        AbilityLabScenarioResult? scenario = lab?.Scenario;
        bool createDefaultGrab = action == "grab" && scenario?.Options.Action != "grab";
        bool createChargeScenario = action != "grab" && chargeTicks >= 0 &&
            (scenario?.Options.Action != action || scenario.Options.ChargeTicks != chargeTicks);
        bool scenarioFrames = scenario?.Options.Action == action || createChargeScenario;
        SlotAddress address = default;
        AbilityLabTimelineProjection? projection = null;
        if (scenarioFrames && !createChargeScenario)
        {
            int maxFrame = scenario!.Frames.Count - 1;
            if (requestedTicks.Any(sample => sample < 0 || sample > maxFrame))
                return Failure("capture.tick.out-of-range",
                    $"Every scenario frame must be in [0, {maxFrame}] for action '{action}'.", window);
        }
        else if (createChargeScenario)
        {
            if (requestedTicks.Any(sample => sample < 0 || sample > AbilityLabScenarioOptions.MaxFrame))
                return Failure("capture.tick.out-of-range",
                    $"Every scenario frame must be in [0, {AbilityLabScenarioOptions.MaxFrame}] for action '{action}'.", window);
        }
        else if (action == "grab")
        {
            const int defaultLastFrame = 60;
            if (requestedTicks.Any(sample => sample < 0 || sample > defaultLastFrame))
                return Failure("capture.tick.out-of-range",
                    $"Every scenario frame must be in [0, {defaultLastFrame}] for the default grab run.", window);
            try { new AbilityLabScenarioOptions("grab").Validate(); }
            catch (Exception ex) { return Failure("scenario.options.invalid", ex.Message, window); }
            scenarioFrames = true;
        }
        else
        {
            if (!TryBuildTimeline(window, action, out address, out var builtProjection, out string actionCode, out string actionError))
                return Failure(actionCode, actionError, window);
            projection = builtProjection;
            if (requestedTicks.Any(sample => sample < 0 || sample > projection.DurationTicks))
                return Failure("capture.tick.out-of-range",
                    $"Every cumulative tick must be in [0, {projection.DurationTicks}] for action '{action}'.", window);
        }
        string packageId = window.CommandWorkspace.PackageId;
        var capturePaths = requestedTicks.Select(sample => Path.Combine(
            outputDirectory, $"{packageId}_{action}_tick-{sample.ToString("D4", CultureInfo.InvariantCulture)}.png")).ToArray();
        if (capturePaths.Any(File.Exists))
            return Failure("capture.output.exists", "One or more requested PNG paths already exist; choose an empty output directory.", window);

        if (lab == null)
            return Failure("lab.rig.missing", "The preview rig was removed; use sloparena.lab.open or sloparena.lab.preview to restore it.", window);
        if (!lab.IsPackagePreview || lab.SelectedPackageId != window.CommandWorkspace.PackageId)
            return Failure("lab.rig.missing", "The preview rig is not attached to the current package.", window);
        if (createDefaultGrab && !lab.CanPreviewGrab)
            return Failure("scenario.action.unavailable", "Shared grab is unavailable for the current preview.", window);
        if (!scenarioFrames && !IsRuntimeSlotAvailable(window, lab, address))
            return Failure("slot.unavailable", $"Action '{action}' is not available in the current runtime preview.", window);

        bool hasCursor = false;
        bool hasCameraState = false;
        AbilityLab.TimelineCursor cursor = default;
        AbilityLab.CameraState cameraState = default;
        Camera? camera = null;
        RenderTexture? renderTexture = null;
        Texture2D? texture = null;
        RenderTexture? oldActive = RenderTexture.active;
        var writtenPaths = new List<string>();
        var captures = new List<AbilityLabCommandCapture>(requestedTicks.Count);
        Exception? captureError = null;
        GameObject? captureCameraObject = null;
        var offscreenSkins = new List<SkinnedMeshRenderer>();
        try
        {
            if (!window.EnsureCommandUi())
                throw new InvalidOperationException("Ability Lab window could not initialize its UI.");

            cursor = lab.CaptureTimelineCursor();
            hasCursor = true;
            cameraState = lab.CaptureCameraState();
            hasCameraState = true;
            // Bone evaluation alone does not refresh an offscreen skinned mesh.
            foreach (var skin in lab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.forceMatrixRecalculationPerRender) continue;
                offscreenSkins.Add(skin);
                skin.forceMatrixRecalculationPerRender = true;
            }
            if (createChargeScenario)
            {
                int lastFrame = requestedTicks.Max();
                lab.RunScenario(new AbilityLabScenarioOptions(action, lastFrame, chargeTicks: chargeTicks));
                scenario = lab.Scenario;
                scenarioFrames = true;
            }
            if (createDefaultGrab)
            {
                lab.RunScenario(new AbilityLabScenarioOptions("grab"));
                scenario = lab.Scenario;
                scenarioFrames = true;
            }
            lab.EnsureCamera();
            if (lab.PreviewCamera == null) throw new InvalidOperationException("Ability Lab has no camera to capture.");
            captureCameraObject = new GameObject("AbilityLabCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            captureCameraObject.transform.SetParent(lab.transform, false);
            camera = captureCameraObject.AddComponent<Camera>();
            camera.CopyFrom(lab.PreviewCamera);
            camera.scene = lab.PreviewCamera.scene;
            camera.transform.rotation = lab.PreviewCamera.transform.rotation;
            camera.enabled = false;
            camera.cullingMask = ~0;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.usePhysicalProperties = false;
            camera.lensShift = Vector2.zero;
            camera.ResetProjectionMatrix();
            camera.ResetCullingMatrix();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.1f, 0.12f);

            Directory.CreateDirectory(outputDirectory);
            if (!HasNoOutputSymlinks(outputDirectory, out outputError))
                throw new InvalidOperationException(outputError);
            if (capturePaths.Any(File.Exists))
                throw new IOException("One or more requested PNG paths already exist; choose an empty output directory.");

            renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            texture = new Texture2D(width, height, TextureFormat.RGB24, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            camera.targetTexture = renderTexture;
            camera.aspect = (float)width / height;

            foreach (int requestedTick in requestedTicks)
            {
                AbilityLabScenarioFrame? scenarioFrame = null;
                if (scenarioFrames)
                {
                    if (scenario == null || requestedTick >= scenario.Frames.Count)
                        throw new InvalidOperationException($"Recorded scenario has no frame {requestedTick}.");
                    lab.SeekScenario(requestedTick);
                    scenarioFrame = scenario.Frames[requestedTick];
                }
                else
                    window.ApplyCommandTimeline(action, address, projection!, requestedTick);

                ApplyViewFlags(lab, viewOptions);
                if (viewOptions.View != "current")
                {
                    var actor = lab.Renderer;
                    if (actor == null) throw new InvalidOperationException("Ability Lab has no actor whose facing can define the capture view.");
                    camera.transform.rotation = ResolveViewRotation(
                        viewOptions, camera.transform.rotation, actor.transform.forward);
                }

                FrameCapture(camera, lab);
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                string path = capturePaths[captures.Count];
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    writtenPaths.Add(path);
                    byte[] png = texture.EncodeToPNG();
                    stream.Write(png, 0, png.Length);
                }

                var capture = new AbilityLabCommandCapture
                {
                    Action = action, Tick = requestedTick, Path = RepositoryRelativePath(path),
                    Camera = CaptureViewInfo(camera, lab, viewOptions.View)
                };
                if (scenarioFrame.HasValue)
                {
                    capture.Frame = ToFrameDto(scenario!, scenarioFrame.Value);
                    capture.MatchTick = scenarioFrame.Value.MatchTick;
                }
                else
                {
                    AbilityLabWindow.TryResolveCumulativeTick(projection!, requestedTick,
                        out var appliedStage, out ushort appliedTick, out int appliedCumulativeTick);
                    capture.AppliedStage = appliedStage.SourceStageIndex;
                    capture.AppliedTick = appliedTick;
                    capture.AppliedCumulativeTick = appliedCumulativeTick;
                }
                captures.Add(capture);
            }
        }
        catch (Exception ex)
        {
            captureError = ex;
        }
        finally
        {
            foreach (var skin in offscreenSkins)
                if (skin != null) skin.forceMatrixRecalculationPerRender = false;
            // Camera destruction can release SRP targets; unbind before destroying it.
            try { RenderTexture.active = oldActive; }
            catch (Exception ex) { captureError ??= ex; }
            if (camera != null)
            {
                try { camera.targetTexture = null; }
                catch (Exception ex) { captureError ??= ex; }
            }
            if (captureCameraObject != null)
            {
                try { UnityEngine.Object.DestroyImmediate(captureCameraObject); }
                catch (Exception ex) { captureError ??= ex; }
            }
            if (hasCursor && lab != null)
            {
                try { lab.RestoreTimelineCursor(cursor); }
                catch (Exception ex) { captureError ??= ex; }
            }
            if (hasCameraState && lab != null)
            {
                try { lab.RestoreCameraState(cameraState); }
                catch (Exception ex) { captureError ??= ex; }
            }
            if (texture != null)
            {
                try { UnityEngine.Object.DestroyImmediate(texture); }
                catch (Exception ex) { captureError ??= ex; }
            }
            if (renderTexture != null)
            {
                try { RenderTexture.ReleaseTemporary(renderTexture); }
                catch (Exception ex) { captureError ??= ex; }
            }
            try { window.RefreshScenarioControls(); }
            catch (Exception ex) { captureError ??= ex; }
        }

        if (captureError != null)
        {
            foreach (string path in writtenPaths)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            return Failure("capture.failed", captureError.Message, window);
        }

        var result = Snapshot(window);
        result.Success = true;
        result.Captures = captures;
        return result;
    }

    [CliCommand(
        "sloparena.lab.capture-window",
        "Grab PNGs of the whole Ability Lab window (toolbar, timeline, fields and embedded viewport) at authoring ticks or recorded scenario frames.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/lab" })]
    public static AbilityLabCommandResult CaptureWindow(
        [CliArg("action", "Canonical action ID, for example ground.1 or air.R. A matching recorded scenario is replayed; otherwise the authoring timeline is scrubbed.", Required = true)] string action,
        [CliArg("ticks", "Comma-separated distinct cumulative ticks or scenario frame indexes.", Required = true)] string ticks,
        [CliArg("output", "Repository-relative output directory below .ability-lab-cache.", Required = true)] string output,
        [CliArg("view", "'current', 'front', 'back', 'left', 'right', 'top', or 'bottom', relative to the actor's facing at the first tick.")] string view = null,
        [CliArg("camera-yaw", "Absolute world camera yaw in degrees [-180,180]; cannot combine with --view.")] string cameraYaw = null,
        [CliArg("camera-pitch", "Absolute world camera pitch in degrees [-90,90]; cannot combine with --view.")] string cameraPitch = null,
        [CliArg("overlays", "'current', 'none', or comma-separated hitboxes,hurtboxes,bones,trajectory.")] string overlays = null,
        [CliArg("dummy", "'on' or 'off' for the rendered opponent and its coupled hurtbox overlay; omit to retain current.")] string dummy = null,
        [CliArg("frame", "'current' keeps the viewport camera distance; 'fit' frames the fighters of every requested tick once, so a sequence does not jitter.", Required = false, DefaultValue = "current")] string frame = "current")
    {
        AbilityLabWindow? window = AbilityLabWindow.FindExistingForCommand();
        if (!CanMutate(out var modeError))
            return Failure("lab.mode.unsupported", modeError, window);
        if (!TryParseViewOptions(view, cameraYaw, cameraPitch, overlays, dummy, out var viewOptions, out string viewError))
            return Failure("capture.options.invalid", viewError, window);
        bool fit = string.Equals(frame, "fit", StringComparison.OrdinalIgnoreCase);
        if (!fit && !string.Equals(frame, "current", StringComparison.OrdinalIgnoreCase))
            return Failure("capture.options.invalid", "Frame must be current or fit.", window);
        if (!TryParseTicks(ticks, out var requestedTicks, out string tickError))
            return Failure("capture.ticks.invalid", tickError, window);
        if (!TryResolveCaptureDirectory(output, out string outputDirectory, out string outputError))
            return Failure("capture.output.invalid", outputError, window);
        if (window == null || !window.CommandWorkspace.HasPackage)
            return Failure("workspace.missing", "Open a Character Package with sloparena.lab.open first.", window);
        if (window.CommandWorkspace.LiveDraftInvalid || window.CommandWorkspace.Preview?.IsAvailable != true)
            return Failure("preview.unavailable", "The current source draft has no available Ability Lab preview.", window);

        AbilityLab? currentLab = window.CommandLab;
        if (currentLab == null || !currentLab.IsPackagePreview || currentLab.SelectedPackageId != window.CommandWorkspace.PackageId)
            return Failure("lab.rig.missing", "The preview rig is not attached to the current package.", window);
        AbilityLab lab = currentLab;
        AbilityLabScenarioResult? scenario = lab.Scenario;
        bool scenarioFrames = scenario?.Options.Action == action;
        SlotAddress address = default;
        AbilityLabTimelineProjection? projection = null;
        if (scenarioFrames)
        {
            int maxFrame = scenario!.Frames.Count - 1;
            if (requestedTicks.Any(sample => sample < 0 || sample > maxFrame))
                return Failure("capture.tick.out-of-range",
                    $"Every scenario frame must be in [0, {maxFrame}] for action '{action}'.", window);
        }
        else
        {
            if (action == "grab")
                return Failure("scenario.missing", "Run a grab scenario with sloparena.lab.run before capturing it.", window);
            if (!TryBuildTimeline(window, action, out address, out var builtProjection, out string actionCode, out string actionError))
                return Failure(actionCode, actionError, window);
            projection = builtProjection;
            if (requestedTicks.Any(sample => sample < 0 || sample > projection.DurationTicks))
                return Failure("capture.tick.out-of-range",
                    $"Every cumulative tick must be in [0, {projection.DurationTicks}] for action '{action}'.", window);
            if (!IsRuntimeSlotAvailable(window, lab, address))
                return Failure("slot.unavailable", $"Action '{action}' is not available in the current runtime preview.", window);
        }
        string packageId = window.CommandWorkspace.PackageId;
        var capturePaths = requestedTicks.Select(sample => Path.Combine(
            outputDirectory, $"{packageId}_{action}_window-{sample.ToString("D4", CultureInfo.InvariantCulture)}.png")).ToArray();
        if (capturePaths.Any(File.Exists))
            return Failure("capture.output.exists", "One or more requested PNG paths already exist; choose an empty output directory.", window);

        bool hasCursor = false, hasCameraState = false;
        AbilityLab.TimelineCursor cursor = default;
        AbilityLab.CameraState cameraState = default;
        var writtenPaths = new List<string>();
        var captures = new List<AbilityLabCommandCapture>(requestedTicks.Count);
        var offscreenSkins = new List<SkinnedMeshRenderer>();
        Exception? captureError = null;

        void Seek(int sample)
        {
            if (scenarioFrames) lab.SeekScenario(sample);
            else
            {
                window.ApplyCommandTimeline(action, address, projection!, sample);
                window.CompleteCommandPreview(address);
            }
            ApplyViewFlags(lab, viewOptions);
        }

        try
        {
            if (!window.EnsureCommandUi())
                throw new InvalidOperationException("Ability Lab window could not initialize its UI.");
            cursor = lab.CaptureTimelineCursor();
            hasCursor = true;
            lab.EnsureCamera();
            Camera camera = lab.PreviewCamera ?? throw new InvalidOperationException("Ability Lab has no camera to capture.");
            cameraState = lab.CaptureCameraState();
            hasCameraState = true;
            foreach (var skin in lab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.forceMatrixRecalculationPerRender) continue;
                offscreenSkins.Add(skin);
                skin.forceMatrixRecalculationPerRender = true;
            }
            // Lay the window out before reading its viewport aspect.
            RepaintWindowNow(window);

            Seek(requestedTicks[0]);
            if (viewOptions.View != "current")
            {
                var actor = lab.Renderer;
                if (actor == null) throw new InvalidOperationException("Ability Lab has no actor whose facing can define the capture view.");
                camera.transform.rotation = ResolveViewRotation(viewOptions, camera.transform.rotation, actor.transform.forward);
            }
            if (fit)
            {
                Bounds bounds = VisibleBounds(lab);
                foreach (int sample in requestedTicks.Skip(1))
                {
                    Seek(sample);
                    bounds.Encapsulate(VisibleBounds(lab));
                }
                float aspect = window.CommandViewportAspect;
                if (aspect > 0f) camera.aspect = aspect;
                FitCamera(camera, bounds);
            }

            Directory.CreateDirectory(outputDirectory);
            if (!HasNoOutputSymlinks(outputDirectory, out outputError))
                throw new InvalidOperationException(outputError);
            if (capturePaths.Any(File.Exists))
                throw new IOException("One or more requested PNG paths already exist; choose an empty output directory.");

            foreach (int requestedTick in requestedTicks)
            {
                Seek(requestedTick);
                window.RefreshScenarioControls();
                string path = capturePaths[captures.Count];
                writtenPaths.Add(path);
                GrabWindow(window, path);
                var capture = new AbilityLabCommandCapture
                {
                    Action = action, Tick = requestedTick, Path = RepositoryRelativePath(path),
                    Camera = CaptureViewInfo(camera, lab, viewOptions.View)
                };
                if (scenarioFrames)
                {
                    capture.Frame = ToFrameDto(scenario!, scenario!.Frames[requestedTick]);
                    capture.MatchTick = scenario.Frames[requestedTick].MatchTick;
                }
                else
                {
                    AbilityLabWindow.TryResolveCumulativeTick(projection!, requestedTick,
                        out var appliedStage, out ushort appliedTick, out int appliedCumulativeTick);
                    capture.AppliedStage = appliedStage.SourceStageIndex;
                    capture.AppliedTick = appliedTick;
                    capture.AppliedCumulativeTick = appliedCumulativeTick;
                }
                captures.Add(capture);
            }
        }
        catch (Exception ex)
        {
            captureError = ex;
        }
        finally
        {
            foreach (var skin in offscreenSkins)
                if (skin != null) skin.forceMatrixRecalculationPerRender = false;
            if (hasCursor)
            {
                try { lab.RestoreTimelineCursor(cursor); }
                catch (Exception ex) { captureError ??= ex; }
            }
            if (hasCameraState)
            {
                try { lab.RestoreCameraState(cameraState); }
                catch (Exception ex) { captureError ??= ex; }
            }
            try
            {
                window.RefreshScenarioControls();
                window.Repaint();
            }
            catch (Exception ex) { captureError ??= ex; }
        }

        if (captureError != null)
        {
            foreach (string path in writtenPaths)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
            return Failure("capture.failed", captureError.Message, window);
        }

        var result = Snapshot(window);
        result.Success = true;
        result.Captures = captures;
        return result;
    }

    private const BindingFlags EditorInternals = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // EditorWindow exposes no public way to paint synchronously or read its pixels; Unity's own
    // screenshot tooling uses these internals, so a missing member fails loudly after an upgrade.
    private static void RepaintWindowNow(EditorWindow window)
    {
        var repaint = typeof(EditorWindow).GetMethod("RepaintImmediately", EditorInternals, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException("EditorWindow.RepaintImmediately is unavailable in this Unity version.");
        repaint.Invoke(window, null);
    }

    private static void GrabWindow(EditorWindow window, string path)
    {
        object host = typeof(EditorWindow).GetField("m_Parent", EditorInternals)?.GetValue(window)
            ?? throw new InvalidOperationException("Ability Lab window has no host view to grab.");
        var grab = host.GetType().GetMethod("GrabPixels", EditorInternals, null, new[] { typeof(RenderTexture), typeof(Rect) }, null)
            ?? throw new MissingMethodException("GUIView.GrabPixels is unavailable in this Unity version.");
        RepaintWindowNow(window);
        float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
        int width = Mathf.RoundToInt(window.position.width * pixelsPerPoint);
        int height = Mathf.RoundToInt(window.position.height * pixelsPerPoint);
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture? oldActive = RenderTexture.active;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            grab.Invoke(host, new object[] { target, new Rect(0f, 0f, width, height) });
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            texture.Apply(false, false);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] png = texture.EncodeToPNG();
            stream.Write(png, 0, png.Length);
        }
        finally
        {
            RenderTexture.active = oldActive;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    internal readonly struct CommandViewOptions
    {
        internal readonly string View;
        internal readonly float? Yaw, Pitch;
        internal readonly int? OverlayMask;
        internal readonly bool? Dummy;

        internal CommandViewOptions(string view, float? yaw, float? pitch, int? overlayMask, bool? dummy)
        {
            View = view; Yaw = yaw; Pitch = pitch; OverlayMask = overlayMask; Dummy = dummy;
        }
    }

    internal static bool TryParseViewOptions(
        string? view, string? yaw, string? pitch, string? overlays, string? dummy,
        out CommandViewOptions options, out string error)
    {
        options = default;
        error = "";
        string selected = view == null ? "current" : view.Trim().ToLowerInvariant();
        if (selected is not ("current" or "front" or "back" or "left" or "right" or "top" or "bottom"))
        {
            error = "View must be current, front, back, left, right, top, or bottom.";
            return false;
        }
        if (view != null && (yaw != null || pitch != null))
        {
            error = "Provide --view or --camera-yaw/--camera-pitch, not both.";
            return false;
        }
        if (!TryViewAngle(yaw, -180f, 180f, "camera-yaw", out var parsedYaw, out error) ||
            !TryViewAngle(pitch, -90f, 90f, "camera-pitch", out var parsedPitch, out error))
            return false;
        if (parsedYaw.HasValue || parsedPitch.HasValue)
            selected = "custom";

        int? mask = null;
        if (overlays != null)
        {
            string value = overlays.Trim().ToLowerInvariant();
            if (value == "none") mask = 0;
            else if (value != "current")
            {
                int flags = 0;
                foreach (string part in value.Split(','))
                {
                    int flag = part.Trim() switch
                    {
                        "hitboxes" => 1, "hurtboxes" => 2, "bones" => 4, "trajectory" => 8, _ => 0
                    };
                    if (flag == 0 || (flags & flag) != 0)
                    {
                        error = "Overlays must be current, none, or distinct comma-separated hitboxes,hurtboxes,bones,trajectory.";
                        return false;
                    }
                    flags |= flag;
                }
                mask = flags;
            }
        }
        bool? showDummy = null;
        if (dummy != null)
        {
            switch (dummy.Trim().ToLowerInvariant())
            {
                case "on": case "true": case "1": showDummy = true; break;
                case "off": case "false": case "0": showDummy = false; break;
                default: error = "Dummy must be on or off."; return false;
            }
        }
        if (mask == 0)
        {
            if (showDummy == true)
            {
                error = "--overlays none cannot combine with --dummy on: dummy visibility and its hurtbox overlay are coupled.";
                return false;
            }
            showDummy = false;
        }
        options = new CommandViewOptions(selected, parsedYaw, parsedPitch, mask, showDummy);
        return true;
    }

    private static bool TryViewAngle(
        string? value, float min, float max, string name, out float? angle, out string error)
    {
        angle = null;
        error = "";
        if (value == null) return true;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
            !float.IsFinite(parsed) || parsed < min || parsed > max)
        {
            error = $"{name} must be a finite angle in [{min}, {max}] degrees.";
            return false;
        }
        angle = parsed;
        return true;
    }

    internal static Quaternion ResolveViewRotation(CommandViewOptions options, Quaternion current, Vector3 actorForward)
    {
        if (options.View == "current") return current;
        if (options.View == "custom")
        {
            Vector3 angles = current.eulerAngles;
            return Quaternion.Euler(
                options.Pitch ?? Mathf.DeltaAngle(0f, angles.x),
                options.Yaw ?? Mathf.DeltaAngle(0f, angles.y), 0f);
        }
        actorForward.y = 0f;
        if (!float.IsFinite(actorForward.x) || !float.IsFinite(actorForward.z) || actorForward.sqrMagnitude < 0.0001f)
            throw new InvalidOperationException("Actor facing is unavailable for a relative capture view.");
        actorForward.Normalize();
        Vector3 actorRight = Vector3.Cross(Vector3.up, actorForward);
        return options.View switch
        {
            "front" => Quaternion.LookRotation(-actorForward, Vector3.up),
            "back" => Quaternion.LookRotation(actorForward, Vector3.up),
            "left" => Quaternion.LookRotation(actorRight, Vector3.up),
            "right" => Quaternion.LookRotation(-actorRight, Vector3.up),
            "top" => Quaternion.LookRotation(Vector3.down, actorForward),
            "bottom" => Quaternion.LookRotation(Vector3.up, actorForward),
            _ => throw new ArgumentException("Unsupported capture view.")
        };
    }

    private static void ApplyViewFlags(AbilityLab lab, CommandViewOptions options)
    {
        if (options.OverlayMask.HasValue)
        {
            int mask = options.OverlayMask.Value;
            lab.ShowHitboxes = (mask & 1) != 0;
            lab.ShowHurtboxes = (mask & 2) != 0;
            lab.ShowBakedBones = (mask & 4) != 0;
            lab.ShowTrajectory = (mask & 8) != 0;
        }
        if (options.Dummy.HasValue && lab.ShowDummy != options.Dummy.Value)
        {
            lab.ShowDummy = options.Dummy.Value;
            lab.RefreshPose();
        }
    }

    private static AbilityLabCommandCaptureCamera CaptureViewInfo(Camera camera, AbilityLab lab, string view)
    {
        Vector3 position = camera.transform.position;
        Vector3 forward = camera.transform.forward;
        Quaternion rotation = camera.transform.rotation;
        return new AbilityLabCommandCaptureCamera
        {
            View = view, Position = new[] { position.x, position.y, position.z },
            Forward = new[] { forward.x, forward.y, forward.z },
            Rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w },
            Hitboxes = lab.ShowHitboxes, Hurtboxes = lab.ShowHurtboxes,
            BakedBones = lab.ShowBakedBones, Trajectory = lab.ShowTrajectory, Dummy = lab.ShowDummy
        };
    }

    private static void FrameCapture(Camera camera, AbilityLab lab) => FitCamera(camera, VisibleBounds(lab));

    private static Bounds VisibleBounds(AbilityLab lab)
    {
        bool hasBounds = false;
        Bounds bounds = default;
        foreach (var renderer in lab.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer.forceRenderingOff) continue;
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!hasBounds) throw new InvalidOperationException("Ability Lab has no visible model to capture.");
        return bounds;
    }

    private static void FitCamera(Camera camera, Bounds bounds)
    {
        // Fit all corners in the selected view; scenario motion must not leave
        // only the idle dummy onscreen. Keep the preview camera's orientation.
        float halfHeight = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float halfWidth = halfHeight * camera.aspect;
        float distance = camera.nearClipPlane;
        Quaternion inverseRotation = Quaternion.Inverse(camera.transform.rotation);
        Vector3 extents = bounds.extents;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = inverseRotation * Vector3.Scale(extents, new Vector3(x, y, z));
            distance = Mathf.Max(distance, camera.nearClipPlane - corner.z);
            distance = Mathf.Max(distance, Mathf.Abs(corner.x) * 1.1f / halfWidth - corner.z);
            distance = Mathf.Max(distance, Mathf.Abs(corner.y) * 1.1f / halfHeight - corner.z);
        }
        if (camera.orthographic)
            camera.orthographicSize = Mathf.Max(bounds.extents.magnitude, 0.1f) * 1.1f / Mathf.Min(camera.aspect, 1f);
        camera.transform.position = bounds.center - camera.transform.forward * distance;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance + extents.magnitude + 1f);
    }

    private static bool CanMutate(out string error)
    {
        bool blocked = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
        error = blocked ? "Ability Lab open, run, preview, and capture commands are edit-mode only." : "";
        return !blocked;
    }

    private static bool TryValidateTimeline(
        AbilityLabWindow window,
        string slot,
        int tick,
        out SlotAddress address,
        out AbilityLabTimelineProjection projection,
        out string code,
        out string message)
    {
        if (!TryBuildTimeline(window, slot, out address, out projection, out code, out message)) return false;
        if (tick < 0 || tick > projection.DurationTicks)
        {
            code = "preview.tick.out-of-range";
            message = $"Cumulative tick must be in [0, {projection.DurationTicks}] for slot '{slot}'.";
            return false;
        }
        return true;
    }

    private static bool TryBuildTimeline(
        AbilityLabWindow window,
        string slot,
        out SlotAddress address,
        out AbilityLabTimelineProjection projection,
        out string code,
        out string message)
    {
        address = default;
        projection = null!;
        code = "slot.invalid";
        message = $"'{slot}' is not a canonical Ability Lab slot ID.";
        if (!CanonicalSlotProjection.TryGet(slot, out _)) return false;
        try
        {
            if (!window.TryBuildCommandTimeline(slot, out address, out projection))
            {
                code = "slot.unavailable";
                message = $"Slot '{slot}' has no authored timeline stages.";
                return false;
            }
            code = "";
            message = "";
            return true;
        }
        catch (Exception ex)
        {
            code = "slot.invalid";
            message = ex.Message;
            return false;
        }
    }

    private static bool IsRuntimeSlotAvailable(AbilityLabWindow window, AbilityLab lab, SlotAddress address)
    {
        var workspace = window.CommandWorkspace;
        if (!workspace.HasPackage || workspace.LiveDraftInvalid || !lab.IsPackagePreview ||
            lab.SelectedPackageId != workspace.PackageId || lab.Def == null)
            return false;
        int labelIndex = Array.IndexOf(AbilityLab.SlotNames, address.InputLabel);
        if (labelIndex < 0) return false;
        var spec = lab.Def.GetCookedSlotAbility(CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[labelIndex]), address.IsAirborne);
        return spec?.Timeline.Stages is { Count: > 0 };
    }
    private static bool IsPackageSlotAvailable(AbilityLabPackageWorkspace workspace, SlotAddress address)
    {
        if (!workspace.HasPackage || workspace.LiveDraftInvalid || workspace.Preview?.IsAvailable != true)
            return false;
        var package = workspace.LiveDraftPackage ?? workspace.Preview.Package;
        if (package?.Definition == null) return false;
        foreach (var slot in package.Definition.Slots)
            if (slot.Id == address.Id && slot.Timeline.Stages.Count > 0)
                return true;
        return false;
    }

    internal static bool TryParseTicks(string value, out List<int> ticks, out string error)
    {
        ticks = new List<int>();
        error = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "At least one cumulative tick is required.";
            return false;
        }
        string[] parts = value.Split(',');
        if (parts.Length > 64)
        {
            error = "Capture batches are limited to 64 samples.";
            return false;
        }
        var distinct = new HashSet<int>();
        foreach (string part in parts)
        {
            if (!int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int tick))
            {
                error = $"'{part}' is not a non-negative integer tick.";
                return false;
            }
            if (!distinct.Add(tick))
            {
                error = $"Duplicate cumulative tick {tick} is not allowed.";
                return false;
            }
            ticks.Add(tick);
        }
        return true;
    }

    internal static bool TryResolveCaptureDirectory(string output, out string fullPath, out string error)
    {
        fullPath = "";
        error = "";
        if (string.IsNullOrWhiteSpace(output))
        {
            error = "Output must name a repository-relative directory below .ability-lab-cache.";
            return false;
        }
        string normalized = output.Replace('\\', '/').TrimEnd('/');
        string[] segments = normalized.Split('/');
        if (Path.IsPathRooted(output) || normalized.StartsWith("/", StringComparison.Ordinal) ||
            segments.Length < 2 || segments[0] != ".ability-lab-cache" ||
            segments.Any(segment => string.IsNullOrEmpty(segment) || segment == "." || segment == ".." ||
                segment.IndexOfAny(new[] { ':', '<', '>', '"', '|', '?', '*' }) >= 0))
        {
            error = "Output must be a repository-relative directory below .ability-lab-cache with no traversal or absolute path.";
            return false;
        }

        try
        {
            string repositoryRoot = RepositoryRoot();
            string cacheRoot = Path.Combine(repositoryRoot, ".ability-lab-cache");
            fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = cacheRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullPath.StartsWith(prefix, comparison))
            {
                error = "Output resolves outside .ability-lab-cache.";
                return false;
            }
            return HasNoOutputSymlinks(fullPath, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool HasNoOutputSymlinks(string outputDirectory, out string error)
    {
        error = "";
        string repositoryRoot = RepositoryRoot();
        string relative = Path.GetRelativePath(repositoryRoot, Path.GetFullPath(outputDirectory));
        string current = repositoryRoot;
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment == "..")
            {
                error = "Output resolves outside the repository.";
                return false;
            }
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    error = $"Output path contains a symbolic link or reparse point: {current}";
                    return false;
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return true;
    }

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "..", ".."));

    private static string RepositoryRelativePath(string path)
        => Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/');

    private static AbilityLabCommandResult Snapshot(AbilityLabWindow? window, AbilityLabScenarioResult? scenarioOverride = null)
    {
        var result = new AbilityLabCommandResult { Success = true };
        AbilityLab? lab = window != null ? window.CommandLab : AbilityLab.Instance;
        result.PresentationDiagnostics = lab?.PresentationPreviewDiagnostics.ToList();
        result.ActiveVfxCount = lab?.PresentationPreviewInstanceCount;
        result.RigInstanceId = lab != null ? lab.gameObject.GetInstanceID() : null;
        ApplyScenarioSnapshot(result, lab, scenarioOverride);
        if (window == null)
        {
            result.WorkspaceStatus = "No window";
            result.PreviewPackageId = lab != null && lab.IsPackagePreview ? lab.SelectedPackageId : null;
            result.PreviewStatus = lab != null ? lab.PreviewStatus : "No preview";
            result.Playing = lab?.Playing;
            result.AuthoritativePreview = lab != null && lab.IsPackagePreview && lab.AuthoritativePreview;
            if (lab?.Scenario == null)
            {
                result.Slot = lab?.SelectedSlotId;
                result.Stage = lab?.StageIndex;
                result.Tick = lab?.Tick;
            }
            result.RigName = lab != null ? lab.gameObject.name : null;
            result.CameraName = lab != null && lab.PreviewCamera != null ? lab.PreviewCamera.name : null;
            return result;
        }

        var workspace = window.CommandWorkspace;
        if (!workspace.HasPackage)
        {
            result.WorkspaceStatus = "No package";
            result.PreviewPackageId = lab != null && lab.IsPackagePreview ? lab.SelectedPackageId : null;
            result.PreviewStatus = lab != null ? lab.PreviewStatus : "No preview";
            result.Playing = lab?.Playing;
            result.AuthoritativePreview = lab != null && lab.IsPackagePreview && lab.AuthoritativePreview;
            if (lab?.Scenario == null)
            {
                result.Slot = lab?.SelectedSlotId;
                result.Stage = lab?.StageIndex;
                result.Tick = lab?.Tick;
            }
            result.RigName = lab != null ? lab.gameObject.name : null;
            result.CameraName = lab != null && lab.PreviewCamera != null ? lab.PreviewCamera.name : null;
            return result;
        }

        result.PackageId = workspace.PackageId;
        result.Form = workspace.SelectedForm;
        result.Dirty = workspace.IsDirty;
        result.WorkspaceStatus = workspace.Status;
        result.PreviewPackageId = lab != null && lab.IsPackagePreview ? lab.SelectedPackageId : null;
        result.PreviewStatus = lab != null
            ? lab.PreviewStatus
            : workspace.LiveDraftInvalid ? "Draft invalid"
            : workspace.Preview?.IsAvailable == true ? "Preview not attached" : "Preview unavailable";
        result.AuthoritativePreview = lab != null && lab.IsPackagePreview && lab.AuthoritativePreview;
        result.Playing = lab?.Playing;
        result.RigName = lab != null ? lab.gameObject.name : null;
        result.CameraName = lab != null && lab.PreviewCamera != null ? lab.PreviewCamera.name : null;
        if (lab != null)
        {
            if (lab.Scenario != null)
            {
                result.CumulativeTick = lab.ScenarioFrame;
                result.DurationTicks = lab.Scenario.Frames.Count;
            }
            else
            {
                result.Slot = lab.SelectedSlotId;
                result.Stage = lab.StageIndex;
                result.Tick = lab.Tick;
                try
                {
                    if (lab.IsPackagePreview && window.TryBuildCommandTimeline(lab.SelectedSlotId, out _, out var currentProjection) &&
                        lab.StageIndex >= 0 && lab.StageIndex < currentProjection.Stages.Count)
                    {
                        var stage = currentProjection.Stages[lab.StageIndex];
                        result.CumulativeTick = Mathf.Clamp(stage.StartTick + lab.Tick, 0, currentProjection.DurationTicks);
                        result.DurationTicks = currentProjection.DurationTicks;
                        result.StageDurationTicks = stage.DurationTicks;
                    }
                }
                catch (Exception ex)
                {
                    result.Diagnostics.Add(new AbilityLabCommandDiagnostic("error", "preview.selection.invalid", lab.SelectedSlotId, ex.Message));
                }
            }
        }

        var slots = new List<AbilityLabCommandSlot>(CanonicalSlotProjection.All.Count);
        foreach (var address in CanonicalSlotProjection.All)
        {
            bool available = false;
            int duration = 0;
            try
            {
                if (window.TryBuildCommandTimeline(address.Id, out _, out var projection))
                {
                    duration = projection.DurationTicks;
                    available = IsPackageSlotAvailable(workspace, address);
                }
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new AbilityLabCommandDiagnostic("error", "slot.invalid", address.Id, ex.Message));
            }
            slots.Add(new AbilityLabCommandSlot { Id = address.Id, Available = available, DurationTicks = duration });
        }
        result.Slots = slots;

        var diagnostics = workspace.Diagnostics.Concat(workspace.Preview?.Diagnostics ?? Array.Empty<CharacterDiagnostic>());
        foreach (var diagnostic in diagnostics
                     .GroupBy(item => $"{item.Severity}|{item.Code}|{item.Path}|{item.Message}", StringComparer.Ordinal)
                     .Select(group => group.First()))
            result.Diagnostics.Add(new AbilityLabCommandDiagnostic(
                diagnostic.Severity.ToString().ToLowerInvariant(), diagnostic.Code, diagnostic.Path, diagnostic.Message));
        return result;
    }

    private static void ApplyScenarioSnapshot(AbilityLabCommandResult result, AbilityLab? lab, AbilityLabScenarioResult? scenarioOverride)
    {
        if (lab == null) return;
        AbilityLabScenarioResult? scenario = scenarioOverride ?? lab.Scenario;
        result.SelectedAction = scenario?.Options.Action ?? lab.SelectedSlotId;
        result.GrabAvailable = lab.CanPreviewGrab;
        result.ScenarioFrame = scenario != null ? lab.ScenarioFrame : null;
        result.Scenario = scenario != null ? ToScenarioDto(scenario) : null;
    }

    private static AbilityLabCommandScenario ToScenarioDto(AbilityLabScenarioResult scenario)
    {
        var dto = new AbilityLabCommandScenario
        {
            Action = scenario.Options.Action,
            LastFrame = scenario.Options.LastFrame,
            Distance = scenario.Options.Distance,
            Opponent = scenario.Options.OpponentBehavior.ToString().ToLowerInvariant(),
            Damage = scenario.Options.OpponentDamage,
            Facing = scenario.Options.RelativeFacingDegrees,
            ChargeTicks = scenario.Options.ChargeTicks,
            Frames = scenario.Frames.Select(frame => ToFrameDto(scenario, frame)).ToList(),
            Contacts = scenario.Contacts.Select(ToContactDto).ToList(),
            Interactions = scenario.Interactions.Select(item => new AbilityLabCommandInteraction
            {
                FrameIndex = item.FrameIndex,
                MatchTick = item.MatchTick,
                Kind = item.Kind,
                InteractionId = item.InteractionId,
            }).ToList(),
            PresentationEvents = scenario.PresentationEvents.Select(item => new AbilityLabCommandPresentationEvent
            {
                MatchTick = item.MatchTick,
                EntityId = item.EntityId,
                OperationIndex = item.OperationIndex,
                PresentationId = item.PresentationId,
                AttackSequence = item.AttackSequence,
                Source = item.Source.ToString(),
                WorldX = item.WorldX,
                WorldY = item.WorldY,
                WorldZ = item.WorldZ,
                WorldYaw = item.WorldYaw,
            }).ToList(),
            Deaths = scenario.Deaths.Select(item => new AbilityLabCommandDeath
            {
                MatchTick = item.Tick,
                EntityId = item.EntityId,
                KillerEntityId = item.KillerEntityId,
                LastHitEntityId = item.LastHitEntityId,
                LastHitTick = item.LastHitTick,
                LastHitSlot = item.LastHitSlot,
                Boundary = item.Boundary,
            }).ToList(),
        };
        return dto;
    }

    private static AbilityLabCommandFrame ToFrameDto(AbilityLabScenarioResult scenario, AbilityLabScenarioFrame frame)
    {
        var dto = new AbilityLabCommandFrame
        {
            FrameIndex = frame.FrameIndex,
            MatchTick = frame.MatchTick,
            Actor = ToFighterDto(frame.Actor),
            Opponent = ToFighterDto(frame.Opponent),
            Contacts = scenario.Contacts.Where(item => item.FrameIndex == frame.FrameIndex).Select(ToContactDto).ToList(),
            Interactions = scenario.Interactions.Where(item => item.FrameIndex == frame.FrameIndex)
                .Select(item => new AbilityLabCommandInteraction
                {
                    FrameIndex = item.FrameIndex, MatchTick = item.MatchTick, Kind = item.Kind, InteractionId = item.InteractionId,
                }).ToList(),
            PresentationEvents = scenario.PresentationEvents.Where(item => item.MatchTick == frame.MatchTick)
                .Select(item => new AbilityLabCommandPresentationEvent
                {
                    MatchTick = item.MatchTick, EntityId = item.EntityId, OperationIndex = item.OperationIndex,
                    PresentationId = item.PresentationId, AttackSequence = item.AttackSequence,
                    Source = item.Source.ToString(), WorldX = item.WorldX, WorldY = item.WorldY,
                    WorldZ = item.WorldZ, WorldYaw = item.WorldYaw,
                }).ToList(),
            Deaths = scenario.Deaths.Where(item => item.Tick == frame.MatchTick)
                .Select(item => new AbilityLabCommandDeath
                {
                    MatchTick = item.Tick, EntityId = item.EntityId, KillerEntityId = item.KillerEntityId,
                    LastHitEntityId = item.LastHitEntityId, LastHitTick = item.LastHitTick,
                    LastHitSlot = item.LastHitSlot,
                    Boundary = item.Boundary,
                }).ToList(),
        };
        return dto;
    }

    private static AbilityLabCommandFighter ToFighterDto(CharacterState state)
        => new()
        {
            X = state.PX, Y = state.PY, Z = state.PZ,
            VelocityX = state.VX, VelocityY = state.VY, VelocityZ = state.VZ,
            KnockbackX = state.KVX, KnockbackY = state.KVY, KnockbackZ = state.KVZ,
            FacingYaw = state.FacingYaw,
            HitstopTicks = state.HitstopTicks,
            HitstunTicks = state.HitstunTicks,
            State = state.State.ToString(),
            StateTicks = state.StateTicks,
            Damage = state.DamagePercent,
            Grounded = state.IsGrounded,
            BlockStunTicks = state.BlockStunTicks,
            BlockHitstop = ((DefenseBlockHitstopKind)state.BlockHitstopKind).ToString(),
            Interaction = ((DefenseInteractionPhase)state.InteractionPhase).ToString(),
            InteractionId = state.InteractionId,
            InteractionPartnerId = state.InteractionPartnerId,
            InteractionTick = state.InteractionTick,
            InteractionTerminalTick = state.InteractionTerminalTick,
            AttackElapsedTicks = state.AttackElapsedTicks,
            AttackSlot = state.AttackSlot,
            ChargeTicks = state.ChargeTicks,
            IsAiming = state.IsAiming,
            InPostHitstunFlight = state.InPostHitstunFlight,
        };

    private static AbilityLabCommandContact ToContactDto(AbilityLabScenarioContact contact)
    {
        var hit = contact.Hit;
        return new AbilityLabCommandContact
        {
            FrameIndex = contact.FrameIndex,
            MatchTick = hit.MatchTick,
            OwnerEntityId = hit.OwnerEntityId,
            TargetEntityId = hit.TargetEntityId,
            AttackSlot = hit.AttackSlot,
            Damage = hit.Damage,
            Blocked = hit.Blocked,
            ImpactForce = hit.ImpactForce,
            KnockbackAngle = hit.KnockbackAngle,
            KnockbackDirection = hit.KnockbackDirection.ToString(),
            StunTicks = hit.StunTicks,
            HitstopTicks = hit.HitstopTicks,
            HitX = hit.HitX,
            HitY = hit.HitY,
            HitZ = hit.HitZ,
        };
    }

    private static AbilityLabCommandResult Failure(string code, string message, AbilityLabWindow? window, string path = "lab")
    {
        var result = Snapshot(window);
        result.Success = false;
        result.Diagnostics.Add(new AbilityLabCommandDiagnostic("error", code, path, message));
        return result;
    }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandResult
{
    [JsonProperty("reach", NullValueHandling = NullValueHandling.Ignore)] public Newtonsoft.Json.Linq.JRaw? Reach { get; set; }
    [JsonProperty("reachElapsedMs", NullValueHandling = NullValueHandling.Ignore)] public double? ReachElapsedMs { get; set; }
    [JsonProperty("form")] public byte Form { get; set; }
    [JsonProperty("success")] public bool Success { get; set; }
    [JsonProperty("packageId", NullValueHandling = NullValueHandling.Ignore)] public string? PackageId { get; set; }
    [JsonProperty("previewPackageId", NullValueHandling = NullValueHandling.Ignore)] public string? PreviewPackageId { get; set; }
    [JsonProperty("slot", NullValueHandling = NullValueHandling.Ignore)] public string? Slot { get; set; }
    [JsonProperty("selectedAction", NullValueHandling = NullValueHandling.Ignore)] public string? SelectedAction { get; set; }
    [JsonProperty("scenarioFrame", NullValueHandling = NullValueHandling.Ignore)] public int? ScenarioFrame { get; set; }
    [JsonProperty("grabAvailable")] public bool GrabAvailable { get; set; }
    [JsonProperty("scenario", NullValueHandling = NullValueHandling.Ignore)] public AbilityLabCommandScenario? Scenario { get; set; }
    [JsonProperty("stage", NullValueHandling = NullValueHandling.Ignore)] public int? Stage { get; set; }
    [JsonProperty("tick", NullValueHandling = NullValueHandling.Ignore)] public ushort? Tick { get; set; }
    [JsonProperty("cumulativeTick", NullValueHandling = NullValueHandling.Ignore)] public int? CumulativeTick { get; set; }
    [JsonProperty("durationTicks", NullValueHandling = NullValueHandling.Ignore)] public int? DurationTicks { get; set; }
    [JsonProperty("stageDurationTicks", NullValueHandling = NullValueHandling.Ignore)] public int? StageDurationTicks { get; set; }
    [JsonProperty("playing", NullValueHandling = NullValueHandling.Ignore)] public bool? Playing { get; set; }
    [JsonProperty("authoritativePreview")] public bool AuthoritativePreview { get; set; }
    [JsonProperty("dirty", NullValueHandling = NullValueHandling.Ignore)] public bool? Dirty { get; set; }
    [JsonProperty("previewStatus", NullValueHandling = NullValueHandling.Ignore)] public string? PreviewStatus { get; set; }
    [JsonProperty("workspaceStatus", NullValueHandling = NullValueHandling.Ignore)] public string? WorkspaceStatus { get; set; }
    [JsonProperty("rigName", NullValueHandling = NullValueHandling.Ignore)] public string? RigName { get; set; }
    [JsonProperty("rigInstanceId", NullValueHandling = NullValueHandling.Ignore)] public int? RigInstanceId { get; set; }
    [JsonProperty("cameraName", NullValueHandling = NullValueHandling.Ignore)] public string? CameraName { get; set; }
    [JsonProperty("slots", NullValueHandling = NullValueHandling.Ignore)] public List<AbilityLabCommandSlot>? Slots { get; set; }
    [JsonProperty("diagnostics")] public List<AbilityLabCommandDiagnostic> Diagnostics { get; } = new();
    [JsonProperty("captures", NullValueHandling = NullValueHandling.Ignore)] public List<AbilityLabCommandCapture>? Captures { get; set; }
    [JsonProperty("presentation", NullValueHandling = NullValueHandling.Ignore)] public SlopArenaPresentationActorInfo? Presentation { get; set; }
    [JsonProperty("presentationError", NullValueHandling = NullValueHandling.Ignore)] public string? PresentationError { get; set; }
    [JsonProperty("presentationDiagnostics", NullValueHandling = NullValueHandling.Ignore)] public List<string>? PresentationDiagnostics { get; set; }
    [JsonProperty("activeVfxCount", NullValueHandling = NullValueHandling.Ignore)] public int? ActiveVfxCount { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandSlot
{
    [JsonProperty("id")] public string Id { get; set; } = "";
    [JsonProperty("available")] public bool Available { get; set; }
    [JsonProperty("durationTicks")] public int DurationTicks { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandDiagnostic
{
    public AbilityLabCommandDiagnostic(string severity, string code, string path, string message)
    {
        Severity = severity;
        Code = code;
        Path = path;
        Message = message;
    }

    [JsonProperty("severity")] public string Severity { get; }
    [JsonProperty("code")] public string Code { get; }
    [JsonProperty("path")] public string Path { get; }
    [JsonProperty("message")] public string Message { get; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandCapture
{
    [JsonProperty("action")] public string Action { get; set; } = "";
    [JsonProperty("tick")] public int Tick { get; set; }
    [JsonProperty("appliedStage", NullValueHandling = NullValueHandling.Ignore)] public int? AppliedStage { get; set; }
    [JsonProperty("appliedTick", NullValueHandling = NullValueHandling.Ignore)] public ushort? AppliedTick { get; set; }
    [JsonProperty("appliedCumulativeTick", NullValueHandling = NullValueHandling.Ignore)] public int? AppliedCumulativeTick { get; set; }
    [JsonProperty("matchTick", NullValueHandling = NullValueHandling.Ignore)] public uint? MatchTick { get; set; }
    [JsonProperty("frame", NullValueHandling = NullValueHandling.Ignore)] public AbilityLabCommandFrame? Frame { get; set; }
    [JsonProperty("path")] public string Path { get; set; } = "";
    [JsonProperty("camera")] public AbilityLabCommandCaptureCamera? Camera { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandCaptureCamera
{
    [JsonProperty("view")] public string View { get; set; } = "";
    [JsonProperty("position")] public float[] Position { get; set; } = Array.Empty<float>();
    [JsonProperty("forward")] public float[] Forward { get; set; } = Array.Empty<float>();
    [JsonProperty("rotation")] public float[] Rotation { get; set; } = Array.Empty<float>();
    [JsonProperty("hitboxes")] public bool Hitboxes { get; set; }
    [JsonProperty("hurtboxes")] public bool Hurtboxes { get; set; }
    [JsonProperty("bakedBones")] public bool BakedBones { get; set; }
    [JsonProperty("trajectory")] public bool Trajectory { get; set; }
    [JsonProperty("dummy")] public bool Dummy { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandScenario
{
    [JsonProperty("action")] public string Action { get; set; } = "";
    [JsonProperty("lastFrame")] public int LastFrame { get; set; }
    [JsonProperty("distance")] public float Distance { get; set; }
    [JsonProperty("opponent")] public string Opponent { get; set; } = "";
    [JsonProperty("damage")] public ushort Damage { get; set; }
    [JsonProperty("facing")] public float Facing { get; set; }
    [JsonProperty("chargeTicks")] public int ChargeTicks { get; set; }
    [JsonProperty("frames")] public List<AbilityLabCommandFrame> Frames { get; set; } = new();
    [JsonProperty("contacts")] public List<AbilityLabCommandContact> Contacts { get; set; } = new();
    [JsonProperty("interactions")] public List<AbilityLabCommandInteraction> Interactions { get; set; } = new();
    [JsonProperty("presentationEvents")] public List<AbilityLabCommandPresentationEvent> PresentationEvents { get; set; } = new();
    [JsonProperty("deaths")] public List<AbilityLabCommandDeath> Deaths { get; set; } = new();
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandFrame
{
    [JsonProperty("frameIndex")] public int FrameIndex { get; set; }
    [JsonProperty("matchTick")] public uint MatchTick { get; set; }
    [JsonProperty("actor")] public AbilityLabCommandFighter Actor { get; set; } = new();
    [JsonProperty("opponent")] public AbilityLabCommandFighter Opponent { get; set; } = new();
    [JsonProperty("contacts")] public List<AbilityLabCommandContact> Contacts { get; set; } = new();
    [JsonProperty("interactions")] public List<AbilityLabCommandInteraction> Interactions { get; set; } = new();
    [JsonProperty("presentationEvents")] public List<AbilityLabCommandPresentationEvent> PresentationEvents { get; set; } = new();
    [JsonProperty("deaths")] public List<AbilityLabCommandDeath> Deaths { get; set; } = new();
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandFighter
{
    [JsonProperty("x")] public float X { get; set; }
    [JsonProperty("y")] public float Y { get; set; }
    [JsonProperty("z")] public float Z { get; set; }
    [JsonProperty("velocityX")] public float VelocityX { get; set; }
    [JsonProperty("velocityY")] public float VelocityY { get; set; }
    [JsonProperty("velocityZ")] public float VelocityZ { get; set; }
    [JsonProperty("knockbackX")] public float KnockbackX { get; set; }
    [JsonProperty("knockbackY")] public float KnockbackY { get; set; }
    [JsonProperty("knockbackZ")] public float KnockbackZ { get; set; }
    [JsonProperty("facingYaw")] public float FacingYaw { get; set; }
    [JsonProperty("hitstopTicks")] public ushort HitstopTicks { get; set; }
    [JsonProperty("hitstunTicks")] public ushort HitstunTicks { get; set; }
    [JsonProperty("state")] public string State { get; set; } = "";
    [JsonProperty("stateTicks")] public ushort StateTicks { get; set; }
    [JsonProperty("damage")] public ushort Damage { get; set; }
    [JsonProperty("grounded")] public bool Grounded { get; set; }
    [JsonProperty("blockStunTicks")] public ushort BlockStunTicks { get; set; }
    [JsonProperty("blockHitstop")] public string BlockHitstop { get; set; } = "";
    [JsonProperty("interaction")] public string Interaction { get; set; } = "";
    [JsonProperty("interactionId")] public ulong InteractionId { get; set; }
    [JsonProperty("interactionPartnerId")] public ulong InteractionPartnerId { get; set; }
    [JsonProperty("interactionTick")] public uint InteractionTick { get; set; }
    [JsonProperty("interactionTerminalTick")] public uint InteractionTerminalTick { get; set; }
    [JsonProperty("attackElapsedTicks")] public ushort AttackElapsedTicks { get; set; }
    [JsonProperty("attackSlot")] public byte AttackSlot { get; set; }
    [JsonProperty("chargeTicks")] public ushort ChargeTicks { get; set; }
    [JsonProperty("isAiming")] public bool IsAiming { get; set; }
    [JsonProperty("inPostHitstunFlight")] public bool InPostHitstunFlight { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandContact
{
    [JsonProperty("frameIndex")] public int FrameIndex { get; set; }
    [JsonProperty("matchTick")] public uint MatchTick { get; set; }
    [JsonProperty("ownerEntityId")] public ulong OwnerEntityId { get; set; }
    [JsonProperty("targetEntityId")] public ulong TargetEntityId { get; set; }
    [JsonProperty("attackSlot")] public byte AttackSlot { get; set; }
    [JsonProperty("damage")] public float Damage { get; set; }
    [JsonProperty("blocked")] public bool Blocked { get; set; }
    [JsonProperty("impactForce")] public float ImpactForce { get; set; }
    [JsonProperty("knockbackAngle")] public sbyte KnockbackAngle { get; set; }
    [JsonProperty("knockbackDirection")] public string KnockbackDirection { get; set; } = "";
    [JsonProperty("stunTicks")] public ushort StunTicks { get; set; }
    [JsonProperty("hitstopTicks")] public ushort HitstopTicks { get; set; }
    [JsonProperty("hitX")] public float HitX { get; set; }
    [JsonProperty("hitY")] public float HitY { get; set; }
    [JsonProperty("hitZ")] public float HitZ { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandInteraction
{
    [JsonProperty("frameIndex")] public int FrameIndex { get; set; }
    [JsonProperty("matchTick")] public uint MatchTick { get; set; }
    [JsonProperty("kind")] public string Kind { get; set; } = "";
    [JsonProperty("interactionId")] public ulong InteractionId { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandPresentationEvent
{
    [JsonProperty("matchTick")] public uint MatchTick { get; set; }
    [JsonProperty("entityId")] public ulong EntityId { get; set; }
    [JsonProperty("operationIndex")] public int OperationIndex { get; set; }
    [JsonProperty("presentationId")] public string PresentationId { get; set; } = "";
    [JsonProperty("attackSequence")] public byte AttackSequence { get; set; }
    [JsonProperty("source")] public string Source { get; set; } = "";
    [JsonProperty("worldX")] public float WorldX { get; set; }
    [JsonProperty("worldY")] public float WorldY { get; set; }
    [JsonProperty("worldZ")] public float WorldZ { get; set; }
    [JsonProperty("worldYaw")] public float WorldYaw { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
public sealed class AbilityLabCommandDeath
{
    [JsonProperty("matchTick")] public uint MatchTick { get; set; }
    [JsonProperty("entityId")] public ulong EntityId { get; set; }
    [JsonProperty("killerEntityId")] public ulong KillerEntityId { get; set; }
    [JsonProperty("lastHitEntityId")] public ulong LastHitEntityId { get; set; }
    [JsonProperty("lastHitTick")] public uint LastHitTick { get; set; }
    [JsonProperty("lastHitSlot")] public byte LastHitSlot { get; set; }
    [JsonProperty("boundary")] public string Boundary { get; set; } = "";
}
