using System;
using System.Linq;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow
{
    private void AddDashStrikeInspector(Foldout group, AbilityLabOperationProjection operation,
        StartCapabilityOperationSource source, DashStrikeCapabilityParameters value)
    {
        AddDelayedInteger(group, "Start tick", source.Tick, tick => CommitCapabilityStart(operation, tick));
        AddDelayedFloat(group, "Dash distance (m)", value.Distance,
            distance => CommitDashStrike(operation, p => p with { Distance = distance }));
        AddDelayedFloat(group, "Dash speed (m/s)", value.Speed,
            speed => CommitDashStrike(operation, p => p with { Speed = speed }));
        AddDelayedInteger(group, "Tip pose tick", value.TipPoseTick,
            tick => CommitDashStrike(operation, p => p with { TipPoseTick = UShort(tick) }));
        AddDelayedInteger(group, "Recovery ticks", value.RecoveryTicks,
            ticks => CommitDashStrike(operation, p => p with { RecoveryTicks = UShort(ticks) }));
        AddDashStrikeHitbox(group, operation, "Light cut", false, value.LightHitbox);
        AddDashStrikeHitbox(group, operation, "Endpoint tip", true, value.TipHitbox);
    }

    private static ushort UShort(int value) => (ushort)Mathf.Clamp(value, 0, ushort.MaxValue);

    private void AddDashStrikeHitbox(Foldout parent, AbilityLabOperationProjection operation,
        string label, bool finisher, HitboxSource value)
    {
        var group = new Foldout { text = label, value = false };
        var timing = new Foldout { text = "Timing & behavior", value = false };
        if (finisher)
            AddDelayedInteger(timing, "Active duration ticks", value.DurationTicks,
                v => CommitDashStrikeHitbox(operation, finisher, h => h with { DurationTicks = UShort(v) }));
        else
            timing.Add(new Label("Active for the computed dash travel duration."));
        AddDelayedInteger(timing, "Hitstun gate (0 = off)", value.StunTicks,
            v => CommitDashStrikeHitbox(operation, finisher, h => h with { StunTicks = UShort(v) }));
        AddDelayedInteger(timing, "Fixed hitstun ticks (0 = automatic)", value.FixedHitstunTicks,
            v => CommitDashStrikeHitbox(operation, finisher, h => h with { FixedHitstunTicks = UShort(v) }));
        AddToggle(timing, "Interruptible", value.Interruptible,
            v => CommitDashStrikeHitbox(operation, finisher, h => h with { Interruptible = v }));
        timing.Add(new Label("Light and tip share one cut per opponent."));
        group.Add(timing);

        var combat = new Foldout { text = "Combat", value = false };
        AddDelayedFloat(combat, "Damage", value.Damage, v => CommitDashStrikeHitbox(operation, finisher, h => h with { Damage = v }));
        AddDelayedFloat(combat, "Angle", value.Angle, v => CommitDashStrikeHitbox(operation, finisher, h => h with { Angle = v }));
        AddDelayedFloat(combat, "Base knockback", value.BaseKnockback, v => CommitDashStrikeHitbox(operation, finisher, h => h with { BaseKnockback = v }));
        AddDelayedFloat(combat, "Knockback growth", value.KnockbackGrowth, v => CommitDashStrikeHitbox(operation, finisher, h => h with { KnockbackGrowth = v }));
        var direction = new EnumField("Knockback direction", value.KnockbackDirection);
        direction.RegisterValueChangedCallback(evt => CommitDashStrikeHitbox(operation, finisher,
            h => h with { KnockbackDirection = (AuthoringKnockbackDirection)evt.newValue }));
        combat.Add(direction);
        group.Add(combat);

        var shape = new Foldout { text = "Geometry", value = false };
        var shapeField = new EnumField("Shape", value.Shape);
        shapeField.RegisterValueChangedCallback(evt => CommitDashStrikeHitbox(operation, finisher,
            h => h with { Shape = (AuthoringHitboxShape)evt.newValue }));
        shape.Add(shapeField);
        AddDelayedFloat(shape, "Radius", value.Radius, v => CommitDashStrikeHitbox(operation, finisher, h => h with { Radius = v }));
        AddDelayedFloat(shape, "Offset X", value.OffsetX, v => CommitDashStrikeHitbox(operation, finisher, h => h with { OffsetX = v }));
        AddDelayedFloat(shape, "Offset Y", value.OffsetY, v => CommitDashStrikeHitbox(operation, finisher, h => h with { OffsetY = v }));
        AddDelayedFloat(shape, "Offset Z", value.OffsetZ, v => CommitDashStrikeHitbox(operation, finisher, h => h with { OffsetZ = v }));
        AddDelayedFloat(shape, "End offset X", value.EndOffsetX, v => CommitDashStrikeHitbox(operation, finisher, h => h with { EndOffsetX = v }));
        AddDelayedFloat(shape, "End offset Y", value.EndOffsetY, v => CommitDashStrikeHitbox(operation, finisher, h => h with { EndOffsetY = v }));
        AddDelayedFloat(shape, "End offset Z", value.EndOffsetZ, v => CommitDashStrikeHitbox(operation, finisher, h => h with { EndOffsetZ = v }));
        group.Add(shape);
        AddBonePopup(group, "Start bone", value.StartBoneId,
            id => CommitDashStrikeHitbox(operation, finisher, h => h with { StartBoneId = id }));
        AddBonePopup(group, "End bone", value.EndBoneId,
            id => CommitDashStrikeHitbox(operation, finisher, h => h with { EndBoneId = id }));
        parent.Add(group);
    }

    private void CommitDashStrike(AbilityLabOperationProjection selected,
        Func<DashStrikeCapabilityParameters, DashStrikeCapabilityParameters> edit)
    {
        if (_updatingControls || _lab == null || selected.Source is not StartCapabilityOperationSource original ||
            original.Parameters is not DashStrikeCapabilityParameters parameters ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _)) return;
        CommitDashStrikeCore(selected, slotIndex, original, edit(parameters));
    }

    private void CommitDashStrikeCore(AbilityLabOperationProjection selected, int slotIndex,
        StartCapabilityOperationSource original, DashStrikeCapabilityParameters parameters)
    {
        _updatingControls = true;
        bool accepted;
        try
        {
            accepted = _workspace.ReplaceOperation(slotIndex, selected.SourceStageIndex, selected.SourceOperationIndex,
                original with { Parameters = parameters });
        }
        finally { _updatingControls = false; }
        if (!accepted) return;
        UpdateTimelineControls();
        _selectedOperation = FindProjectedOperation(selected.SourceStageIndex, selected.SourceOperationIndex);
        _timelineTrack.SelectedOperation = _selectedOperation;
        RefreshInspector();
        SceneView.RepaintAll();
    }

    private void CommitDashStrikeHitbox(AbilityLabOperationProjection selected, bool finisher,
        Func<HitboxSource, HitboxSource> edit)
    {
        if (_updatingControls || _lab == null || selected.Source is not StartCapabilityOperationSource original ||
            original.Parameters is not DashStrikeCapabilityParameters parameters ||
            !_workspace.TryResolveCanonicalSlot(_lab.SelectedSlotId, out int slotIndex, out _)) return;
        var updated = finisher
            ? parameters with { TipHitbox = edit(parameters.TipHitbox) }
            : parameters with { LightHitbox = edit(parameters.LightHitbox) };
        CommitDashStrikeCore(selected, slotIndex, original, updated);
    }

    private void AddDashStrikeButton(VisualElement parent, int stageIndex,
        CharacterSlotSource slot)
    {
        var add = new Button(() =>
        {
            if (_lab == null || !_workspace.AddDashStrike(_lab.SelectedSlotId, stageIndex)) return;
            UpdateTimelineControls();
            _selectedOperation = _timelineProjection?.Stages[stageIndex].Operations.LastOrDefault();
            CacheSelectedHitbox();
            SyncSelectedHitbox();
            _timelineTrack.SelectedOperation = _selectedOperation;
            RefreshInspector();
            SceneView.RepaintAll();
        }) { name = "add-dash-strike", text = "Add dash strike" };
        add.tooltip = "Dash a fixed distance toward the current target, with an endpoint tip.";
        add.SetEnabled(!slot.Timeline.Stages.SelectMany(phase => phase.Operations)
            .OfType<StartCapabilityOperationSource>()
            .Any(op => op.CapabilityId == CharacterPackageCompiler.DashStrikeCapabilityId));
        parent.Add(add);
    }
}
