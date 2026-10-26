using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared.AI;
using SlopArena.Shared;
using SlopArena.Client.Camera;
using SlopArena.Client.World;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Training keeps a compact behavior readout during play. Pause expands the
    /// controls in the same position and owns mouse access.
    /// </summary>
    public class TrainingSettingsPanel : MonoBehaviour
    {
        private TrainingMatch _match;
        private CameraMount _cameraMount;
        private VisualElement _root;
        private VisualElement _panel;
        private Label _damageLabel;
        private Label _modeDescription;
        private Label _inputHint;
        private Label _summaryMode;
        private Label _movementSummary;
        private Label _movementDescription;
        private Button _movementButton;
        private Button _snapButton;
        private VisualElement _progressiveControls;
        private Button _startButton;
        private Button _rampButton;
        private Button _curveButton;
        private Button _keepButton;
        private Button _stopButton;
        private Button _pauseMovementEntry;
        private ScrollView _controls;
        private bool _showDuringPlay;
        private readonly Dictionary<NpcAiMode, Button> _modeButtons = new();
        private readonly Dictionary<CpuDifficulty, Button> _difficultyButtons = new();

        public void Init(TrainingMatch match, UIDocument document, CameraMount cameraMount)
        {
            _match = match;
            _cameraMount = cameraMount;
            var template = Resources.Load<VisualTreeAsset>("UI/TrainingSettingsPanel");
            if (template == null || document == null)
            {
                Debug.LogError("[TrainingSettings] Training UI template and HUD document are required.");
                return;
            }
            _root = template.Instantiate();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.position = Position.Absolute;
            _root.style.top = 16;
            _root.style.right = 16;
            _root.style.bottom = 180;
            _root.style.width = 300;
            _panel = _root.Q<VisualElement>("ts-panel");
            _controls = _panel.Q<ScrollView>("ts-scroll");
            BindControls();
            // Added above the pause cover so the panel keeps its position and stays
            // usable while paused. Settings dialogs added later still cover it.
            document.rootVisualElement.Add(_root);
            var pauseSettings = document.rootVisualElement.Q<Button>("pause-settings");
            if (pauseSettings != null)
            {
                _pauseMovementEntry = new Button(() =>
                {
                    RefreshVisibility();
                    _controls.ScrollTo(_movementButton);
                    _movementButton.Focus();
                }) { name = "pause-movement-prototype", text = "MOVEMENT PROTOTYPE" };
                _pauseMovementEntry.AddToClassList("match-pause-action");
                pauseSettings.parent.Insert(pauseSettings.parent.IndexOf(pauseSettings), _pauseMovementEntry);
            }
            RefreshSelection();
            RefreshVisibility();
        }

        private void BindControls()
        {
            _movementSummary = _panel.Q<Label>("movement-summary");
            _movementDescription = _panel.Q<Label>("movement-description");
            _movementButton = _panel.Q<Button>("movement-model");
            _snapButton = _panel.Q<Button>("movement-snap");
            _movementButton.clicked += () => _match.SetMovementPrototype(
                (GroundSpeedModel)(((int)_match.MovementModel + 1) % 4), _match.MovementStickSnap);
            _snapButton.clicked += () => _match.SetMovementPrototype(_match.MovementModel, !_match.MovementStickSnap);
            _progressiveControls = _panel.Q<VisualElement>("movement-progressive-controls");
            _startButton = _panel.Q<Button>("movement-start");
            _rampButton = _panel.Q<Button>("movement-ramp");
            _curveButton = _panel.Q<Button>("movement-curve");
            _keepButton = _panel.Q<Button>("movement-keep");
            _stopButton = _panel.Q<Button>("movement-stop");
            _startButton.clicked += () =>
            {
                var t = _match.ProgressiveTuning;
                var values = new[] { 0f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f };
                SetProgressive(t.RampTicks, t.Curve, t.TurnaroundKeep, t.StopTicks,
                    values[(System.Array.IndexOf(values, t.StartRatio) + 1) % values.Length]);
            };
            _rampButton.clicked += () =>
            {
                var t = _match.ProgressiveTuning;
                SetProgressive(t.RampTicks > 38 ? 6 : t.RampTicks + 2, t.Curve, t.TurnaroundKeep, t.StopTicks);
            };
            _curveButton.clicked += () =>
            {
                var t = _match.ProgressiveTuning;
                SetProgressive(t.RampTicks, t.Curve == GroundSpeedCurve.Linear ? GroundSpeedCurve.SlowStart : GroundSpeedCurve.Linear,
                    t.TurnaroundKeep, t.StopTicks);
            };
            _keepButton.clicked += () =>
            {
                var t = _match.ProgressiveTuning;
                SetProgressive(t.RampTicks, t.Curve, t.TurnaroundKeep >= 1f ? 0f : t.TurnaroundKeep + 0.25f, t.StopTicks);
            };
            _stopButton.clicked += () =>
            {
                var t = _match.ProgressiveTuning;
                SetProgressive(t.RampTicks, t.Curve, t.TurnaroundKeep, t.StopTicks > 28 ? 4 : t.StopTicks + 2);
            };
            foreach (var button in new[] { _movementButton, _snapButton, _startButton, _rampButton, _curveButton, _keepButton, _stopButton })
                button.RegisterCallback<FocusInEvent>(_ => _controls.ScrollTo(button));
            RefreshMovement();
            var cooldownToggle = _panel.Q<Toggle>("no-cd-toggle");
            cooldownToggle.SetValueWithoutNotify(_match.NoCooldowns);
            cooldownToggle.RegisterValueChangedCallback(evt => _match.SetNoCooldowns(evt.newValue));
            var visibilityToggle = _panel.Q<Toggle>("show-during-play");
            visibilityToggle.SetValueWithoutNotify(_showDuringPlay);
            visibilityToggle.RegisterValueChangedCallback(evt =>
            {
                _showDuringPlay = evt.newValue;
                RefreshVisibility();
            });
            _damageLabel = _panel.Q<Label>("npc-damage-label");
            _modeDescription = _panel.Q<Label>("npc-mode-description");
            _inputHint = _panel.Q<Label>("ts-input-hint");
            _summaryMode = _panel.Q<Label>("ts-summary-mode");

            foreach (NpcAiMode mode in Enum.GetValues(typeof(NpcAiMode)))
            {
                var selectedMode = mode;
                var button = _panel.Q<Button>($"mode-{mode.ToString().ToLowerInvariant()}");
                button.clicked += () => _match.SetNpcMode(selectedMode);
                _modeButtons.Add(mode, button);
            }
            foreach (CpuDifficulty difficulty in Enum.GetValues(typeof(CpuDifficulty)))
            {
                var selectedDifficulty = difficulty;
                var button = _panel.Q<Button>($"difficulty-{difficulty.ToString().ToLowerInvariant()}");
                button.clicked += () => _match.SetNpcDifficulty(selectedDifficulty);
                _difficultyButtons.Add(difficulty, button);
            }
            _panel.Q<Button>("dmg-minus").clicked += () =>
                _match.SetSelectedNpcDamage(_match.GetSelectedNpcDamage() - 10f);
            _panel.Q<Button>("dmg-plus").clicked += () =>
                _match.SetSelectedNpcDamage(_match.GetSelectedNpcDamage() + 10f);
            _panel.Q<Button>("dmg-reset").clicked += () => _match.SetSelectedNpcDamage(0f);
            _panel.Q<Button>("reset-damage").clicked += _match.ResetTrainingDamage;
            _panel.Q<Button>("reset-position").clicked += ResetPositions;
            _panel.Q<Button>("reset-all").clicked += () =>
            {
                _match.ResetTrainingDamage();
                ResetPositions();
            };
        }

        private void SetProgressive(int ramp, GroundSpeedCurve curve, float keep, int stop, float? start = null)
            => _match.SetMovementPrototype(_match.MovementModel, _match.MovementStickSnap,
                GroundSpeedTuning.Progressive(ramp, curve, keep, stop, start ?? _match.ProgressiveTuning.StartRatio));

        private void ResetPositions()
        {
            _match.ResetTrainingPositions();
            // Resetting aim presentation restores the camera's normal mode. Pause
            // continues to own the cursor until the player resumes.
            if (_cameraMount != null && MatchPauseMenu.Active is { IsPaused: true })
            {
                _cameraMount.FreezeAtCurrentAngles();
                _cameraMount.SetMode(CameraMode.FreeCursor);
            }
        }

        private void Update()
        {
            if (_panel == null) return;
            _damageLabel.text = $"{_match.GetSelectedNpcDamage():F0}%";
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            bool paused = MatchPauseMenu.Active is { IsPaused: true };
            bool expanded = paused || _showDuringPlay;
            _controls.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _panel.SetEnabled(paused);
            _panel.EnableInClassList("ts-panel--editing", paused);
            _panel.EnableInClassList("ts-panel--compact", !expanded);
            _inputHint.text = paused ? "PAUSED · ADJUST TRAINING" : "ESC / START · ADJUST TRAINING";
        }

        public void RefreshMovement()
        {
            if (_movementSummary == null) return;
            _movementSummary.text = _match.MovementPrototypeLabel;
            _movementButton.text = $"MODEL · {_match.MovementModel.ToString().ToUpperInvariant()}  ›";
            _snapButton.text = $"STICK 8-WAY SNAP · {(_match.MovementStickSnap ? "ON" : "OFF")}";
            var t = _match.ProgressiveTuning;
            _progressiveControls.style.display = _match.MovementModel == GroundSpeedModel.Progressive
                ? DisplayStyle.Flex : DisplayStyle.None;
            _startButton.text = $"START SPEED · {t.StartRatio * 100f:F0}%  ›";
            _rampButton.text = $"RAMP · {t.RampTicks} TICKS  ›";
            _curveButton.text = $"CURVE · {(t.Curve == GroundSpeedCurve.SlowStart ? "SLOW START" : "LINEAR")}  ›";
            _keepButton.text = $"TURNAROUND KEEP · {t.TurnaroundKeep * 100f:F0}%  ›";
            _stopButton.text = $"STOP · {t.StopTicks} TICKS  ›";
            _movementDescription.text = _match.MovementModel switch
            {
                GroundSpeedModel.Tiers => "Tilt ≤70% walks at 40%. Full tilt runs instantly. Walk stops in 3 ticks.",
                GroundSpeedModel.Progressive => "Any tilt starts at START SPEED, then ramps to full run. At 0%, ramping starts on tick 1. Turns keep at least START SPEED.",
                GroundSpeedModel.Hybrid => "Tilt ≤70% walks at 40%. Walk ramps in 3 ticks, run in 6. Run keeps Rush reversals.",
                _ => "Current movement. Any tilt gives full run immediately.",
            };
        }

        public void RefreshRoster() => RefreshSelection();

        public void RefreshSelection()
        {
            if (_panel == null) return;
            _summaryMode.text = _match.CurrentNpcMode.ToString().ToUpperInvariant();
            if (_match.CurrentNpcMode is NpcAiMode.Attack or NpcAiMode.Avoid)
                _summaryMode.text += $" · {_match.CurrentNpcDifficulty.ToString().ToUpperInvariant()}";
            foreach (var pair in _modeButtons)
                pair.Value.EnableInClassList("active", pair.Key == _match.CurrentNpcMode);
            foreach (var pair in _difficultyButtons)
                pair.Value.EnableInClassList("active", pair.Key == _match.CurrentNpcDifficulty);
            _panel.Q<VisualElement>("difficulty-section").style.display =
                _match.CurrentNpcMode is NpcAiMode.Attack or NpcAiMode.Avoid
                    ? DisplayStyle.Flex : DisplayStyle.None;
            _modeDescription.text = _match.CurrentNpcMode switch
            {
                NpcAiMode.Attack => "Fights you with attacks, defense and recovery.",
                NpcAiMode.Shield => "Holds shield. Practice pressure and grabs.",
                NpcAiMode.Avoid => "Keeps its distance and avoids stage edges.",
                _ => "Stands still. Practice hits and combos.",
            };
            _damageLabel.text = $"{_match.GetSelectedNpcDamage():F0}%";
        }

        private void OnDestroy()
        {
            _pauseMovementEntry?.RemoveFromHierarchy();
            _root?.RemoveFromHierarchy();
        }
    }
}
