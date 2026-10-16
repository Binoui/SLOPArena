using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SlopArena.Client.Tools;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public static class AbilityLabScenarioUISelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Ability Lab Scenario Controls")]
    public static void Run()
    {
        var window = AbilityLabWindow.FindExistingForCommand();
        if (window == null || !window.EnsureCommandUi() || window.CommandLab?.CanPreviewGrab != true)
            throw new InvalidOperationException("Open a valid package in Ability Lab first.");
        var lab = window.CommandLab;
        var cursor = lab.CaptureTimelineCursor();
        var draft = window.CommandWorkspace.Draft;
        bool dirty = window.CommandWorkspace.IsDirty;
        var root = window.rootVisualElement;
        try
        {
            Click(root.Q<Button>("selected-grab"));
            if (lab.Scenario?.Options.Action != "grab" || lab.ScenarioFrame != 0)
                throw new InvalidOperationException("Grab selection did not enter recorded Shared playback.");
            var slider = root.Q<SliderInt>("timeline-slider");
            if (!slider.enabledSelf) throw new InvalidOperationException("Grab scenario is not scrubbable.");
            slider.value = 7;
            var expected = lab.Scenario.Frames[7];
            if (lab.ScenarioFrame != 7 || lab.Renderer.CurrentActionState != expected.Actor.State
                || lab.DummyRenderer.CurrentActionState != expected.Opponent.State)
                throw new InvalidOperationException("UI scrubbing left a fighter in another frame's action state.");
            slider.value = 0;
            if (lab.ScenarioFrame != 0 || lab.Renderer.CurrentActionState != SlopArena.Shared.ActionState.GrabAttempt)
                throw new InvalidOperationException("Backward scrubbing did not rewind actual grab presentation.");
            Click(root.Q<Button>("scenario-exit"));
            if (lab.IsScenarioPreview || !root.Q<Button>("selected-ground-1").enabledSelf)
                throw new InvalidOperationException("Exit did not restore canonical authoring controls.");
            if (!ReferenceEquals(draft, window.CommandWorkspace.Draft) || window.CommandWorkspace.IsDirty != dirty)
                throw new InvalidOperationException("Scenario controls changed the draft or dirty state.");
            lab.RunScenario(new AbilityLabScenarioOptions("grab", 24, 0.7f, AbilityLabOpponentBehavior.Shield));
            lab.SeekScenario(0);
            if (lab.DummyRenderer.transform.Find("ShieldGuard") == null)
                throw new InvalidOperationException("Recorded Shielding state has no actual guard visual.");
            lab.SeekScenario(7);
            if (lab.DummyRenderer.transform.Find("ShieldGuard") != null)
                throw new InvalidOperationException("Grabbed state retained the prior shield visual.");
            lab.SeekScenario(0);
            if (lab.DummyRenderer.transform.Find("ShieldGuard") == null)
                throw new InvalidOperationException("Backward replay did not restore the actual shield visual.");
            var observed = lab.Scenario;
            var outcomeLabel = root.Q<Label>("scenario-outcomes");
            window.RefreshScenarioControls();
            foreach (var observation in observed.Interactions)
                if (!outcomeLabel.text.Contains($"{observation.Kind} frame {observation.FrameIndex}", StringComparison.Ordinal))
                    throw new InvalidOperationException("Scenario panel omitted an observed coupled-interaction outcome.");
            Debug.Log("[AbilityLabScenarioUISelfTest] Passed actual Grab selection, fighter-state scrubbing, rewind, authoring exit and draft preservation.");
        }
        finally
        {
            lab.RestoreTimelineCursor(cursor);
            window.RefreshScenarioControls();
        }
    }

    [MenuItem("Tools/SLOPArena/Tests/Charged Dash Scenario Controls")]
    public static void RunChargedDashScenarioChargeControlSelfTest()
    {
        var window = AbilityLabWindow.FindExistingForCommand();
        if (window == null || !window.EnsureCommandUi() || window.CommandLab == null)
            throw new InvalidOperationException("Open a valid charged-dash package preview in Ability Lab first.");
        var lab = window.CommandLab;
        if (!CanonicalSlotProjection.TryGet("ground.R", out var address))
            throw new InvalidOperationException("Ground R is not a canonical slot.");
        int slotIndex = Array.IndexOf(AbilityLab.SlotNames, address.InputLabel);
        var cooked = lab.Def.GetCookedSlotAbility((byte)(AbilityLab.SlotIndices[slotIndex] + 1), address.IsAirborne);
        var parameters = cooked?.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<CookedStartCapabilityOperation>()
            .Select(operation => operation.Parameters)
            .OfType<CookedChargedDirectionalDashCapabilityParameters>()
            .FirstOrDefault();
        if (parameters == null)
            throw new InvalidOperationException("The package preview has no cooked charged directional dash on ground.R.");

        var cursor = lab.CaptureTimelineCursor();
        string output = $".ability-lab-cache/charged-dash-window-{Guid.NewGuid():N}";
        string tapOutput = output + "-tap";
        string outputDirectory = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), output);
        string tapOutputDirectory = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), tapOutput);
        try
        {
            lab.SetSlot(address);
            window.RefreshScenarioControls();
            var chargeField = window.rootVisualElement.Q<IntegerField>("scenario-charge-ticks");
            var horizonField = window.rootVisualElement.Q<IntegerField>("scenario-horizon");
            if (chargeField == null || horizonField == null)
                throw new InvalidOperationException("Scenario charge/duration controls are missing.");
            chargeField.value = 20;
            horizonField.value = 80;
            Click(window.rootVisualElement.Q<Button>("scenario-run"));

            var scenario = lab.Scenario ?? throw new InvalidOperationException("Scenario controls did not record a Shared run.");
            if (scenario.Options.ChargeTicks != 20 ||
                scenario.Frames[19].Actor.ChargeTicks != 20 ||
                scenario.Frames[19].Actor.IsAiming == false ||
                scenario.Frames[20].Actor.ChargeTicks != 20)
                throw new InvalidOperationException("Charge duration did not hold exactly twenty input frames in the authoritative snapshot.");
            if (parameters.GetChargeTier(scenario.Frames[19].Actor.ChargeTicks) != 1)
                throw new InvalidOperationException("Recorded charge did not reach the expected tier-2 boundary.");
            bool SwordTrail(AbilityLabScenarioFrame frame)
                => lab.Renderer.IsSwordTrailTick(frame.Actor, address.IsAirborne);
            int firstSwordFrame = scenario.Frames.Select((frame, index) => (frame, index))
                .FirstOrDefault(item => SwordTrail(item.frame)).index;
            if (firstSwordFrame <= 0 || SwordTrail(scenario.Frames[firstSwordFrame - 1]) ||
                scenario.Frames[firstSwordFrame].Actor.AttackElapsedTicks < parameters.FinisherSeekTick ||
                SwordTrail(scenario.Frames[scenario.Frames.Count - 1]))
                throw new InvalidOperationException("The charge scenario did not expose a distinct windup-to-sword-pose window and recovery boundary.");

            var capture = SlopArenaAbilityLabCommands.Capture("ground.R", firstSwordFrame.ToString(),
                output, width: 320, height: 240, overlays: "none");
            if (!capture.Success || capture.Captures?.Count != 1 ||
                capture.Captures[0].Frame?.Actor.ChargeTicks != 20)
                throw new InvalidOperationException("Capture omitted the recorded charged sword-pose frame and charge receipt.");
            var tapCapture = SlopArenaAbilityLabCommands.Capture("ground.R", firstSwordFrame.ToString(),
                tapOutput, width: 320, height: 240, overlays: "none", chargeTicks: 0);
            if (!tapCapture.Success || tapCapture.Captures?.Count != 1 ||
                tapCapture.Captures[0].Frame?.Actor.ChargeTicks != 0 ||
                lab.Scenario?.Options.ChargeTicks != 20)
                throw new InvalidOperationException("Explicit --charge-ticks 0 did not replace the recorded hold for capture or restore the prior scenario.");
            window.RefreshScenarioControls();
        }
        finally
        {
            lab.RestoreTimelineCursor(cursor);
            window.RefreshScenarioControls();
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
            if (Directory.Exists(tapOutputDirectory)) Directory.Delete(tapOutputDirectory, true);
        }
        Debug.Log("[AbilityLabScenarioUISelfTest] Charged dash UI charge control, tier threshold, sword-pose window, recovery boundary and native capture receipt passed.");
    }

    private static void Click(Button button)
    {
        if (button == null || !button.enabledSelf) throw new InvalidOperationException("Required scenario control is unavailable.");
        typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(button.clickable, new object[] { null, 0 });
    }
}
