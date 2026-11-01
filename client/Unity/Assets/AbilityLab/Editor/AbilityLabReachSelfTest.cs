using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public static class AbilityLabReachSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Ability Lab Reach")]
    public static void Run()
    {
        var window = AbilityLabWindow.FindExistingForCommand();
        Require(window != null && window.EnsureCommandUi(), "Open a package in Ability Lab first.");
        var workspace = window!.CommandWorkspace;
        var lab = window.CommandLab;
        Require(workspace.HasPackage && !workspace.HasUnsavedChanges && lab != null, "Reach self-test requires a clean, valid package preview.");
        var cursor = lab!.CaptureTimelineCursor();
        byte originalForm = workspace.SelectedForm;
        string diskPath = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), workspace.PackageRoot, "character.json");
        byte[] diskBefore = File.ReadAllBytes(diskPath);
        var identity = workspace.Preview.Identity;
        try
        {
            Require(window.SelectFormForCommand(0), "Cannot select base form.");
            Require(SlopArenaAbilityLabCommands.Preview("ground.1", 0).Success, "Cannot select ground.1.");
            var result = SlopArenaAbilityLabCommands.Reach();
            Require(result.Success && result.Reach != null, "Native Reach failed.");
            var before = workspace.Reach!;
            Require(workspace.IsReachCurrent("ground.1"), "Fresh result is stale.");
            string json = before.Document.ToJson();
            Require(JsonConvert.SerializeObject(result).Contains(json, StringComparison.Ordinal), "Pipeline reserialized the Shared document.");
            var document = JObject.Parse(json);
            Require(document["draft"]?["unsaved"]?.Value<bool>() == false && document["targets"]?["FightGuy"] != null &&
                document["targets"]?["Manki"] != null, "Draft provenance or targets missing.");
            Require(!JsonConvert.SerializeObject(SlopArenaAbilityLabCommands.Inspect()).Contains("\"reach\"", StringComparison.Ordinal), "Inspect leaked reach fields.");
            Require(workspace.TryResolveCanonicalSlot("ground.1", out _, out var slot), "Missing source slot.");
            var operation = slot.Timeline.Stages[0].Operations.Select((value, index) => (value, index))
                .First(item => item.value is SpawnHitboxOperationSource);
            var hitbox = (SpawnHitboxOperationSource)operation.value;
            Require(workspace.ReplaceHitbox("ground.1", 0, operation.index, hitbox.Hitbox with { Radius = hitbox.Hitbox.Radius + .8f }), "Radius draft edit failed.");
            window.RefreshScenarioControls();
            Require(!workspace.IsReachCurrent("ground.1") && window.rootVisualElement.Q<Label>("reach-summary").text.Contains("STALE", StringComparison.Ordinal), "Draft edit did not show STALE.");
            var afterResult = SlopArenaAbilityLabCommands.Reach("ground.1");
            var after = workspace.Reach!;
            Require(afterResult.Success && workspace.IsReachCurrent("ground.1") && after.Document.Draft!.Unsaved,
                "Rerun did not measure the unsaved draft.");
            Require(!ReferenceEquals(before.Definition, after.Definition) && before.SourceHash != after.SourceHash,
                "Draft edit did not replace the measured definition and fingerprint.");
            Require(before.Move.Arc.Zip(after.Move.Arc, (a, b) => !a.Rows.SequenceEqual(b.Rows)).Any(changed => changed) ||
                before.Move.Reach.Zip(after.Move.Reach, (a, b) => !a.Intervals.SequenceEqual(b.Intervals)).Any(changed => changed),
                "Radius edit did not change measured contact geometry or reach.");
            Require(workspace.Preview.Identity == identity && diskBefore.SequenceEqual(File.ReadAllBytes(diskPath)), "Measurement saved or cooked source.");
            workspace.Undo();
            Require(!workspace.IsReachCurrent("ground.1"), "Undo retained a current edited result.");
            var unchanged = workspace.Reach;
            var bad = SlopArenaAbilityLabCommands.Reach("grab");
            Require(!bad.Success && bad.Diagnostics.Any(d => d.Code == "reach.action.invalid") && ReferenceEquals(unchanged, workspace.Reach), "Invalid action replaced the observation.");
            Require(!workspace.ReachMatchesSelection("ground.2"), "Another move inherited the result.");
            if (workspace.Draft.AlternateForm != null)
            {
                Require(window.SelectFormForCommand(1) && !workspace.ReachMatchesSelection("ground.1"), "Another form inherited the result.");
                Require(window.SelectFormForCommand(0), "Cannot restore form.");
            }
            var aerial = SlopArenaAbilityLabCommands.Reach("air.1");
            Require(aerial.Success && workspace.Reach!.Move.Air && workspace.Reach.Move.Vertical.Count == 2 &&
                workspace.Reach.Move.Arc.Count == 0, "Aerial vertical result is unavailable.");
            Debug.Log($"[AbilityLabReachSelfTest] Passed native JSON embedding, clean provenance, radius edit -> STALE -> changed unsaved result, definition replacement, Undo, slot/form isolation, invalid action and aerial vertical reach. Baseline {before.ElapsedMs:0.0} ms; edited {after.ElapsedMs:0.0} ms.");
        }
        finally
        {
            Require(workspace.DiscardUnsavedChanges(), "Cannot discard the temporary draft.");
            window.SelectFormForCommand(originalForm);
            lab.RestoreTimelineCursor(cursor);
            window.RefreshScenarioControls();
            Require(diskBefore.SequenceEqual(File.ReadAllBytes(diskPath)), "Reach self-test changed package source.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
