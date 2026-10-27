using SlopArena.Shared;
using SlopArena.Client.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow
{
    private IMGUIContainer? _movesViewport;
    private RenderTexture? _movesViewportTarget;
    private Label? _movesSceneStatus;
    private Toggle? _sceneHitboxes;
    private Toggle? _sceneHurtboxes;
    private Toggle? _sceneMotion;
    private EventCallback<ChangeEvent<bool>>? _hitboxesChanged;
    private EventCallback<ChangeEvent<bool>>? _hurtboxesChanged;
    private EventCallback<ChangeEvent<bool>>? _motionChanged;
    private bool _releasePreviewControl;

    internal float CommandViewportAspect => _movesViewport is { contentRect: { width: > 0f, height: > 0f } rect }
        ? rect.width / rect.height
        : 0f;

    private void BindMovesViewport()
    {
        ReleaseMovesViewport();
        _movesViewport = _root.Q<IMGUIContainer>("move-scene-viewport");
        _movesSceneStatus = _root.Q<Label>("scene-status");
        _sceneHitboxes = _root.Q<Toggle>("scene-hitboxes");
        _sceneHurtboxes = _root.Q<Toggle>("scene-hurtboxes");
        _sceneMotion = _root.Q<Toggle>("scene-motion");
        if (_movesViewport == null || _movesSceneStatus == null || _sceneHitboxes == null ||
            _sceneHurtboxes == null || _sceneMotion == null)
        {
            if (_movesSceneStatus != null) _movesSceneStatus.text = "Scene viewport controls are unavailable.";
            return;
        }

        _movesViewport.onGUIHandler = DrawMovesViewport;
        _movesViewport.RegisterCallback<PointerUpEvent>(OnPreviewPointerUp, TrickleDown.TrickleDown);
        _hitboxesChanged = evt => SetSceneOverlay(hitboxes: evt.newValue);
        _hurtboxesChanged = evt => SetSceneOverlay(hurtboxes: evt.newValue);
        _motionChanged = evt => SetSceneOverlay(motion: evt.newValue);
        _sceneHitboxes.RegisterValueChangedCallback(_hitboxesChanged);
        _sceneHurtboxes.RegisterValueChangedCallback(_hurtboxesChanged);
        _sceneMotion.RegisterValueChangedCallback(_motionChanged);
        RefreshMovesViewport();
    }

    private void RefreshMovesViewport()
    {
        if (_movesSceneStatus == null) return;
        if (!_workspace.HasPackage)
        {
            _movesSceneStatus.text = "Select a package to preview.";
        }
        else if (_lab == null)
        {
            _movesSceneStatus.text = EditorApplication.isPlayingOrWillChangePlaymode
                ? "Preview resumes in Edit Mode."
                : "Preparing isolated preview…";
        }
        else if (_workspace.PreviewPrerequisitesStale)
        {
            _movesSceneStatus.text = "Preview poses or bindings are stale. Reload or Save + Cook before running scenarios.";
        }
        else if (_workspace.LiveDraftInvalid)
        {
            _movesSceneStatus.text = "Draft invalid · scene preview unavailable.";
        }
        else if (_lab.PreviewCamera == null)
        {
            _movesSceneStatus.text = "Preview camera unavailable.";
        }
        else if (_lab.IsScenarioPreview)
        {
            _movesSceneStatus.text = $"Recorded Shared scenario · frame {_lab.ScenarioFrame}";
        }
        else if (_lab.PhasePreviewActive)
        {
            _movesSceneStatus.text = "Presentation-only phase preview";
        }
        else if (!_lab.IsPackagePreview)
        {
            string status = string.IsNullOrWhiteSpace(_lab.PreviewStatus)
                ? "No package preview is available."
                : $"No package preview · {_lab.PreviewStatus}";
            _movesSceneStatus.text = _lab.PreviewCamera == null
                ? $"{status} · Preview camera unavailable."
                : status;
        }
        else
        {
            _movesSceneStatus.text = _lab.PreviewStatus;
        }

        bool canDraw = _workspace.HasPackage && !_workspace.LiveDraftInvalid && !_workspace.PreviewPrerequisitesStale && _lab?.PreviewCamera != null &&
            (_lab.IsPackagePreview || _lab.IsScenarioPreview || _lab.PhasePreviewActive);
        _sceneHitboxes?.SetEnabled(canDraw && !_grabSelected);
        _sceneHurtboxes?.SetEnabled(canDraw);
        _sceneMotion?.SetEnabled(canDraw);
        if (_lab != null)
        {
            _sceneHitboxes?.SetValueWithoutNotify(_lab.ShowHitboxes);
            _sceneHurtboxes?.SetValueWithoutNotify(_lab.ShowHurtboxes);
            _sceneMotion?.SetValueWithoutNotify(_lab.ShowTrajectory);
        }
        _movesViewport?.MarkDirtyRepaint();
    }

    private void ReleaseMovesViewport()
    {
        if (_sceneHitboxes != null && _hitboxesChanged != null)
            _sceneHitboxes.UnregisterValueChangedCallback(_hitboxesChanged);
        if (_sceneHurtboxes != null && _hurtboxesChanged != null)
            _sceneHurtboxes.UnregisterValueChangedCallback(_hurtboxesChanged);
        if (_sceneMotion != null && _motionChanged != null)
            _sceneMotion.UnregisterValueChangedCallback(_motionChanged);
        if (_movesViewport != null)
        {
            _movesViewport.UnregisterCallback<PointerUpEvent>(OnPreviewPointerUp, TrickleDown.TrickleDown);
            _movesViewport.onGUIHandler = null;
        }
        ReleaseMovesViewportTarget();
        _movesViewport = null;
        _movesSceneStatus = null;
        _sceneHitboxes = null;
        _sceneHurtboxes = null;
        _sceneMotion = null;
        _hitboxesChanged = null;
        _hurtboxesChanged = null;
        _motionChanged = null;
        _releasePreviewControl = false;
    }

    private void ReleaseMovesViewportTarget()
    {
        if (_movesViewportTarget == null) return;
        _movesViewportTarget.Release();
        DestroyImmediate(_movesViewportTarget);
        _movesViewportTarget = null;
    }

    private void SetSceneOverlay(bool? hitboxes = null, bool? hurtboxes = null, bool? motion = null)
    {
        if (_lab == null) return;
        if (hitboxes.HasValue) _lab.ShowHitboxes = hitboxes.Value;
        if (hurtboxes.HasValue) _lab.ShowHurtboxes = hurtboxes.Value;
        if (motion.HasValue) _lab.ShowTrajectory = motion.Value;
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        _movesViewport?.MarkDirtyRepaint();
    }

    private void DrawMovesViewport()
    {
        if (_movesViewport == null) return;
        var rect = _movesViewport.contentRect;
        if (rect.width <= 0f || rect.height <= 0f || !_workspace.HasPackage || _workspace.LiveDraftInvalid || _lab == null ||
            (!_lab.IsPackagePreview && !_lab.IsScenarioPreview && !_lab.PhasePreviewActive)) return;
        var camera = _lab.PreviewCamera;
        if (camera == null) return;
        // Unity forwards mouse events to IMGUI containers beyond their visible
        // bounds. A stale nearest handle must not consume toolbar/field clicks.
        if (Event.current.type == EventType.MouseDown && !rect.Contains(Event.current.mousePosition)) return;
        if (Event.current.isMouse && !rect.Contains(Event.current.mousePosition) && GUIUtility.hotControl == 0) return;
        HandlePreviewCameraInput(rect);
        int width = Mathf.Max(1, Mathf.CeilToInt(rect.width * EditorGUIUtility.pixelsPerPoint));
        int height = Mathf.Max(1, Mathf.CeilToInt(rect.height * EditorGUIUtility.pixelsPerPoint));
        if (_movesViewportTarget == null || _movesViewportTarget.width != width || _movesViewportTarget.height != height)
        {
            ReleaseMovesViewportTarget();
            _movesViewportTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "AbilityLabMovesViewport",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _movesViewportTarget.Create();
        }

        var target = camera.targetTexture;
        float aspect = camera.aspect;
        Rect cameraRect = camera.rect;
        Rect pixelRect = camera.pixelRect;
        var active = RenderTexture.active;
        bool repaint = Event.current.type == EventType.Repaint;
        if (repaint) GL.PushMatrix();
        try
        {
            // The native Normal path renders into RenderTexture.active. Isolate that clear,
            // then let IMGUI composite under its own clip instead of painting over the toolbar.
            camera.targetTexture = _movesViewportTarget;
            camera.aspect = width / (float)height;
            RenderTexture.active = _movesViewportTarget;
            Handles.DrawCamera(rect, camera, DrawCameraMode.Normal);
            // Draw handles into the same local render target as the model so they
            // share its projection and cannot paint outside the viewport.
            camera.pixelRect = new Rect(0f, 0f, width, height);
            Handles.SetCamera(camera);
            RenderTexture.active = _movesViewportTarget;
            DrawPreviewHandles();
        }
        finally
        {
            RenderTexture.active = active;
            camera.targetTexture = target;
            camera.aspect = aspect;
            camera.rect = cameraRect;
            camera.pixelRect = pixelRect;
            if (repaint) GL.PopMatrix();
            if (_releasePreviewControl && repaint)
            {
                // IMGUIContainer owns a separate GUI state: release the native
                // handle here, inside that state, as well as Toolkit capture.
                _releasePreviewControl = false;
                GUIUtility.hotControl = 0;
                _movesViewport?.ReleasePointer(PointerId.mousePointerId);
            }
        }
        if (Event.current.type != EventType.Repaint) return;
        GUI.DrawTexture(rect, _movesViewportTarget, ScaleMode.StretchToFill, false);
        if (_lab.IsScenarioPreview && _lab.Scenario is { } scenario &&
            SlopArena.Shared.CanonicalSlotProjection.TryGet(scenario.Options.Action, out var address))
        {
            int slotIndex = System.Array.IndexOf(AbilityLab.SlotNames, address.InputLabel);
            if (slotIndex >= 0)
            {
                var cooked = _lab.Def.GetCookedSlotAbility(CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[slotIndex]), address.IsAirborne);
                SlopArena.Shared.CookedChargedDirectionalDashCapabilityParameters? charge = null;
                if (cooked != null)
                    foreach (var stage in cooked.Timeline.Stages)
                    foreach (var operation in stage.Operations)
                        if (operation is SlopArena.Shared.CookedStartCapabilityOperation
                            { Parameters: SlopArena.Shared.CookedChargedDirectionalDashCapabilityParameters parameters })
                        {
                            charge = parameters;
                            break;
                        }
                if (charge != null)
                {
                    ushort ticks = scenario.Frames[_lab.ScenarioFrame].Actor.ChargeTicks;
                    byte tier = charge.GetChargeTier(ticks);
                    Color color = tier switch
                    {
                        2 => new Color(1f, 0.3f, 0.12f),
                        1 => new Color(1f, 0.8f, 0.15f),
                        _ => new Color(0.35f, 0.85f, 1f),
                    };
                    var badge = new Rect(rect.x + 12f, rect.y + 12f, 210f, 34f);
                    EditorGUI.DrawRect(badge, new Color(0f, 0f, 0f, 0.82f));
                    var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = color }, fontSize = 16 };
                    GUI.Label(badge, $"CHARGE {ticks} · TIER {tier + 1}", style);
                }
            }
        }
    }

    private void HandlePreviewCameraInput(Rect rect)
    {
        var evt = Event.current;
        int control = GUIUtility.GetControlID(FocusType.Passive);
        switch (evt.GetTypeForControl(control))
        {
            case EventType.MouseDown when rect.Contains(evt.mousePosition) && (evt.button == 1 || evt.button == 2):
                GUIUtility.hotControl = control;
                evt.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == control:
                var delta = new Vector2(evt.delta.x, -evt.delta.y);
                _lab!.AdjustCameraView(evt.button == 1 ? delta : Vector2.zero,
                    evt.button == 2 ? delta : Vector2.zero, 0f);
                _movesViewport?.MarkDirtyRepaint();
                evt.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == control:
                GUIUtility.hotControl = 0;
                evt.Use();
                break;
            case EventType.ScrollWheel when rect.Contains(evt.mousePosition):
                _lab!.AdjustCameraView(Vector2.zero, Vector2.zero, evt.delta.y * 0.1f);
                _movesViewport?.MarkDirtyRepaint();
                evt.Use();
                break;
            case EventType.ContextClick when rect.Contains(evt.mousePosition):
                evt.Use();
                break;
        }
    }

    private void OnPreviewPointerUp(PointerUpEvent evt)
    {
        if (evt.button != 0 || (!_sceneRadiusEditing && !_scenePresentationEditing)) return;
        // Let native handles release their IMGUI control before a source refresh
        // rebuilds the selected controls and presentation objects.
        _releasePreviewControl = true;
        _movesViewport?.schedule.Execute(CommitPreviewHandleEdits);
        _movesViewport?.MarkDirtyRepaint();
    }

    private void CommitPreviewHandleEdits()
    {
        if (_sceneRadiusEditing) CommitSceneRadius();
        if (_scenePresentationEditing) CommitScenePresentation();
    }
}
