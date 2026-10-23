using System;
using System.Collections.Generic;
using SlopArena.Client.Entities;
using SlopArena.Client.Tools;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow
{
    private static readonly List<string> PhaseChoices = new() { "Timeline", "Charge / Hold", "Release / Fire" };
    private static readonly List<string> ReleaseChoices = new() { "Timeline", "Release / Fire" };
    private DropdownField _phaseSelector = null!;
    private Label _phaseClipLabel = null!;
    private WeaponAttachConfig? _attachmentPreviewConfig;
    private Label? _attachmentStatus;
    private Button? _attachmentUndo;
    private Button? _attachmentRedo;
    private Button? _attachmentSave;
    private Button? _attachmentRevert;

    private void BindPhaseAuthoring()
    {
        _phaseSelector.RegisterValueChangedCallback(evt =>
        {
            if (_updatingControls || _lab == null || _lab.IsScenarioPreview) return;
            _lab.Playing = false;
            if (evt.newValue == "Timeline") _lab.SetPhasePreview(false);
            else
            {
                _lab.SetAuthoringPhase(evt.newValue == "Charge / Hold"
                    ? AbilityLab.AuthoringPhase.Charge : AbilityLab.AuthoringPhase.Fire);
                _lab.SetPhasePreview(true);
            }
            UpdateTimelineControls();
            RefreshInspector();
            SceneView.RepaintAll();
        });
    }

    private void RefreshPhaseControls()
    {
        if (_phaseSelector == null) return;
        bool available = _lab != null && _lab.IsPackagePreview && !_grabSelected && !_lab.IsScenarioPreview;
        var choices = available && _lab!.CanPreviewCharge ? PhaseChoices : ReleaseChoices;
        if (!ReferenceEquals(_phaseSelector.choices, choices)) _phaseSelector.choices = choices;
        _phaseSelector.SetEnabled(available);
        string selected = available && _lab!.PhasePreviewActive
            ? (_lab.Phase == AbilityLab.AuthoringPhase.Charge ? "Charge / Hold" : "Release / Fire")
            : "Timeline";
        _phaseSelector.SetValueWithoutNotify(selected);
        _phaseClipLabel.text = !available ? "Recorded scenarios keep their original phase and frame."
            : _lab!.PhasePreviewActive
                ? $"{_lab.PhaseClipName} · {_lab.PhasePreviewStatus}"
                : _lab.CanPreviewCharge ? "Select Charge / Hold or Release / Fire to place attachments."
                    : "No charge/hold clip for this move. Release and timeline preview remain available.";
    }

    private bool UpdatePhaseTimeline()
    {
        if (_lab == null || !_lab.PhasePreviewActive || _lab.IsScenarioPreview || _grabSelected) return false;
        _updatingControls = true;
        _moveTimeline.style.display = DisplayStyle.Flex;
        _stageSelector.style.display = DisplayStyle.None;
        _timelineTrack.style.display = DisplayStyle.None;
        SetTimelineRange(Mathf.Max(0, _lab.PhaseDurationTicks - 1), _lab.PhaseTick);
        bool valid = _lab.PhaseDurationTicks > 0;
        _timelineSlider.SetEnabled(valid);
        _timelinePlay.SetEnabled(valid);
        _timelinePlay.text = _lab.Playing ? "Pause" : "Play";
        _timelineTick.text = $"Phase tick {_lab.PhaseTick}";
        _timelineDuration.text = $"{_lab.PhaseDurationTicks} ticks · {_lab.PhaseDurationTicks / 60f:0.00}s";
        _updatingControls = false;
        return true;
    }

    private void AddAttachmentAuthoring()
    {
        if (_lab == null || !_workspace.HasPackage || _grabSelected || _lab.IsScenarioPreview) return;
        var source = _workspace.Catalog?.WeaponConfig;
        if (source?.Entries == null) return;
        var indices = new List<int>();
        var labels = new List<string>();
        for (int i = 0; i < source.Entries.Length; ++i)
        {
            var entry = source.Entries[i];
            if (entry == null || (entry.AttackSlot != 0 && entry.AttackSlot != _lab.SlotIndex + 1)) continue;
            indices.Add(i);
            labels.Add($"{i + 1}: {entry.Prefab?.name ?? "Missing prefab"} · {entry.BoneName}");
        }
        if (indices.Count == 0) return;
        int current = indices.IndexOf(_workspace.AttachmentDraftEntryIndex);
        if (current < 0) current = 0;
        if (!_workspace.BeginAttachmentDraft(indices[current]))
        {
            var pending = new Foldout { text = "Unsaved attachment placement", value = true };
            pending.Add(new Label("Save or revert the previous attachment before editing another."));
            AddAttachmentButtons(pending);
            _inspector.Add(pending);
            return;
        }
        var group = new Foldout { name = "attachment-authoring", text = "Attachment placement", value = true };
        var selector = new DropdownField("Prop", labels, current) { name = "attachment-entry" };
        selector.RegisterValueChangedCallback(evt =>
        {
            int chosen = labels.IndexOf(evt.newValue);
            if (chosen < 0) return;
            if (!_workspace.BeginAttachmentDraft(indices[chosen])) selector.SetValueWithoutNotify(labels[current]);
            else { ApplyAttachmentPreview(); RefreshInspector(); }
        });
        group.Add(selector);
        group.Add(new Label("Bone-local metres / Euler degrees. Edits are a draft until Save attachment."));
        bool fire = _lab.PhasePreviewActive && _lab.Phase == AbilityLab.AuthoringPhase.Fire;
        var separate = new Toggle("Separate fire placement")
        {
            name = "attachment-fire-override", value = _workspace.AttachmentHasFirePhaseOverride,
            tooltip = "Charge keeps shared placement. Fire uses its own offsets when enabled."
        };
        separate.RegisterValueChangedCallback(evt =>
        {
            if (!_workspace.SetAttachmentFirePhaseOverride(evt.newValue))
                separate.SetValueWithoutNotify(_workspace.AttachmentHasFirePhaseOverride);
            ApplyAttachmentPreview();
            RefreshInspector();
        });
        group.Add(separate);
        bool editingFire = fire && _workspace.AttachmentHasFirePhaseOverride;
        group.Add(new Label(editingFire ? "Editing fire override" : "Editing shared placement"));
        AddAttachmentVector(group, "Position", "attachment-position",
            editingFire ? _workspace.AttachmentFirePositionOffset : _workspace.AttachmentPositionOffset,
            value => editingFire ? _workspace.SetAttachmentFirePositionOffset(value) : _workspace.SetAttachmentPositionOffset(value));
        AddAttachmentVector(group, "Rotation", "attachment-rotation",
            editingFire ? _workspace.AttachmentFireRotationOffset : _workspace.AttachmentRotationOffset,
            value => editingFire ? _workspace.SetAttachmentFireRotationOffset(value) : _workspace.SetAttachmentRotationOffset(value));
        AddAttachmentButtons(group);
        _inspector.Add(group);
        RefreshAttachmentStatus();
    }

    private void AddAttachmentVector(VisualElement parent, string label, string name, Vector3 value, Func<Vector3, bool> change)
    {
        var field = new Vector3Field(label) { name = name, value = value };
        field.RegisterValueChangedCallback(evt =>
        {
            if (!change(evt.newValue)) field.SetValueWithoutNotify(evt.previousValue);
            ApplyAttachmentPreview();
            RefreshAttachmentStatus();
        });
        parent.Add(field);
    }

    private void AddAttachmentButtons(VisualElement parent)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
        _attachmentUndo = new Button(() => AttachmentAction(() => _workspace.UndoAttachmentDraft())) { name = "attachment-undo", text = "Undo" };
        _attachmentRedo = new Button(() => AttachmentAction(() => _workspace.RedoAttachmentDraft())) { name = "attachment-redo", text = "Redo" };
        _attachmentSave = new Button(() => AttachmentAction(() => _workspace.SaveAttachmentDraft())) { name = "attachment-save", text = "Save attachment" };
        _attachmentRevert = new Button(() => AttachmentAction(() => _workspace.RevertAttachmentDraft())) { name = "attachment-revert", text = "Revert" };
        row.Add(_attachmentUndo); row.Add(_attachmentRedo); row.Add(_attachmentSave); row.Add(_attachmentRevert);
        parent.Add(row);
        _attachmentStatus = new Label { name = "attachment-status" };
        parent.Add(_attachmentStatus);
        RefreshAttachmentStatus();
    }

    private void AttachmentAction(Func<bool> action)
    {
        action();
        ApplyAttachmentPreview();
        RefreshInspector();
    }

    private void RefreshAttachmentStatus()
    {
        RefreshUnsavedChanges();
        if (_attachmentStatus != null)
            _attachmentStatus.text = !string.IsNullOrEmpty(_workspace.AttachmentDraftError) ? _workspace.AttachmentDraftError
                : _workspace.AttachmentDraftDirty ? "Unsaved attachment placement"
                : "Attachment saved · package may need cooking before publishing.";
        _attachmentUndo?.SetEnabled(_workspace.CanUndoAttachmentDraft);
        _attachmentRedo?.SetEnabled(_workspace.CanRedoAttachmentDraft);
        _attachmentSave?.SetEnabled(_workspace.AttachmentDraftDirty);
        _attachmentRevert?.SetEnabled(_workspace.HasAttachmentDraft);
    }

    private void ApplyAttachmentPreview()
    {
        if (_lab == null) return;
        var previous = _attachmentPreviewConfig;
        _attachmentPreviewConfig = _workspace.HasAttachmentDraft ? _workspace.CreateAttachmentPreviewConfig() : null;
        _lab.SetWeaponAttachConfigOverride(_attachmentPreviewConfig);
        if (previous != null) DestroyImmediate(previous);
        SceneView.RepaintAll();
        RefreshAttachmentStatus();
    }

    private void DisposeAttachmentPreview()
    {
        if (_lab != null) _lab.SetWeaponAttachConfigOverride(null);
        if (_attachmentPreviewConfig != null) DestroyImmediate(_attachmentPreviewConfig);
        _attachmentPreviewConfig = null;
    }

}
