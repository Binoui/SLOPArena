using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared.AI;
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
            RefreshSelection();
            RefreshVisibility();
        }

        private void BindControls()
        {
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

        private void OnDestroy() => _root?.RemoveFromHierarchy();
    }
}
