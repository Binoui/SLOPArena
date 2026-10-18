using System;
using System.Collections.Generic;
using SlopArena.Client.Input;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>Reusable frontend and in-match settings surface.</summary>
    public sealed class SettingsOverlay : MonoBehaviour
    {
        private const string Audio = "AUDIO";
        private const string Controls = "CONTROLS";
        private const string Gameplay = "GAMEPLAY";
        private static VisualTreeAsset? _layout;
        private static int _closedFrame = -1;
        private VisualElement? _surface;
        private VisualElement? _host;
        private VisualElement? _body;
        private VisualElement? _resetConfirmation;
        private VisualElement? _resetFocus;
        private VisualElement? _restoreFocus;
        private DropdownField? _device;
        private Label? _status;
        private Action? _onClose;
        private PickingMode _originalPickingMode;
        private bool _open;
        private string _deviceName = "Keyboard";
        private string _category = Audio;
        private FullScreenMode _displayRevertMode;
        private int _displayRevertWidth;
        private int _displayRevertHeight;
        private float _displayDeadline;
        private Label? _displayCountdown;
        private int _backConsumedFrame = -1;

        private void Update()
        {
            // The frontend router owns shell input; gameplay settings need
            // the same Back/Done shortcuts without relying on panel focus.
            if (_open && !FrontendController.IsFrontendActive
                && !ChatInputGate.ExternalOverlaySuppressed && !HumanInputActions.IsCapturing)
            {
                var pad = UnityEngine.InputSystem.Gamepad.current;
                if (pad?.buttonEast.wasPressedThisFrame == true)
                    HandleBack();
                else if (pad?.startButton.wasPressedThisFrame == true
                    && !pad.buttonSouth.wasPressedThisFrame)
                    GamepadUIBridge.Submit(ForwardButton);
            }
            if (_displayCountdown == null) return;
            if (!Application.isFocused || _surface == null || _surface.panel == null)
            {
                RevertDisplay();
                return;
            }
            int seconds = Mathf.CeilToInt(_displayDeadline - Time.unscaledTime);
            _displayCountdown.text = $"Keep these display settings? Reverting in {Mathf.Max(0, seconds)} seconds.";
            if (seconds <= 0) RevertDisplay();
        }

        public static SettingsOverlay? Active { get; private set; }
        public bool IsOpen => _open;
        public static bool ClosedThisFrame => _closedFrame == Time.frameCount;

        internal Button? ForwardButton => !_open ? null
            : _resetConfirmation?.Q<Button>() ?? _surface?.Q<Button>("settings-back");

        public void Open(VisualElement host, Action? onClose = null)
        {
            if (_open) return;
            _layout ??= Resources.Load<VisualTreeAsset>("UI/SettingsOverlay");
            if (_layout == null)
            {
                Debug.LogError("[Settings] Missing UI/SettingsOverlay layout.");
                return;
            }

            Active = this;
            _host = host;
            _originalPickingMode = host.pickingMode;
            host.pickingMode = PickingMode.Position;
            _open = true;
            _onClose = onClose;
            _restoreFocus = host.panel?.focusController.focusedElement as VisualElement;
            FindFirstObjectByType<InputController>()?.RequireReleaseBeforeHumanInput();

            _surface = _layout.CloneTree();
            var packSkin = Resources.Load<StyleSheet>("UI/LocalPack/PackSkin");
            if (packSkin != null) _surface.styleSheets.Add(packSkin);
            _surface.style.position = Position.Absolute;
            _surface.style.left = 0; _surface.style.right = 0;
            _surface.style.top = 0; _surface.style.bottom = 0;
            _body = _surface.Q<VisualElement>("settings-body");
            _status = _surface.Q<Label>("settings-status");
            foreach (var name in new[] { Audio, Controls, "VIDEO", Gameplay, "ACCESSIBILITY" })
            {
                var tab = _surface.Q<Button>($"settings-tab-{name.ToLowerInvariant()}");
                tab.clicked += () => SelectCategory(name);
            }
            var reset = _surface.Q<Button>("settings-reset");
            reset.clicked += AskReset;
            var back = _surface.Q<Button>("settings-back");
            back.clicked += HandleBack;
            _surface.RegisterCallback<ClickEvent>(_ => UISFX.PlayClick());
            _surface.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
            host.Add(_surface);
            UiModalState.Push();
            SelectCategory(_category);
            back.Focus();
        }

        private void SelectCategory(string category)
        {
            if (HumanInputActions.IsCapturing && category != _category)
            {
                HumanInputActions.CancelCapture();
                return;
            }
            if (_body == null) return;
            _category = category;
            _body.Clear();
            _status!.text = "";
            if (category == Audio) BuildAudio(_body);
            else if (category == Controls) BuildControls(_body);
            else if (category == "VIDEO") BuildVideo(_body);
            else if (category == "ACCESSIBILITY") BuildAccessibility(_body);
            else BuildGameplay(_body);
            ApplyReadableContrast(_body);
            _surface?.Query<Button>().ForEach(tab =>
            {
                if (tab.name == null || !tab.name.StartsWith("settings-tab-", StringComparison.Ordinal)) return;
                tab.EnableInClassList("sa-settings-tab--selected",
                    tab.name == $"settings-tab-{category.ToLowerInvariant()}");
            });
        }

        private static void BuildAudio(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            var columns = new VisualElement();
            columns.AddToClassList("sa-settings-columns");
            var left = new VisualElement();
            left.AddToClassList("sa-settings-column");
            left.AddToClassList("sa-settings-column--first");
            var right = new VisualElement();
            right.AddToClassList("sa-settings-column");
            AddSlider(left, "Master Volume", settings.Master, 0f, 100f, settings.SetMaster);
            AddSlider(left, "Music Volume", settings.Music, 0f, 100f, settings.SetMusic);
            AddSlider(right, "SFX Volume", settings.Sfx, 0f, 100f, settings.SetSfx);
            AddSlider(right, "UI Volume", settings.Ui, 0f, 100f, settings.SetUi);
            columns.Add(left); columns.Add(right); root.Add(columns);
            var mute = new Toggle("Mute When Unfocused") { value = settings.MuteWhenUnfocused };
            mute.AddToClassList("sa-settings-toggle");
            mute.RegisterValueChangedCallback(evt => settings.SetMuteWhenUnfocused(evt.newValue));
            root.Add(mute);
        }

        private void BuildControls(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            _device = new DropdownField("Device",
                new System.Collections.Generic.List<string> { "Keyboard", "Mouse", "Controller" },
                _deviceName == "Mouse" ? 1 : _deviceName == "Controller" ? 2 : 0);
            _device.AddToClassList("sa-settings-field");
            _device.RegisterValueChangedCallback(evt => { _deviceName = evt.newValue; SelectCategory(Controls); });
            root.Add(_device);
            if (_deviceName == "Controller")
            {
                foreach (var action in new[]
                         {
                             "MoveStick", "Slot1", "Slot2", "Slot3", "Slot4", "SpecialModifier",
                             "Jump", "Down", "Shield", "FaceToCamera", "ToggleLock", "Retarget", "Pause", "Retry"
                         })
                    AddBindingRow(root, action, ActionLabel(action, controller: true));
                var help = new Label("Specials: hold Special Modifier + Normal button. Grab: hold Special Modifier + RT.");
                help.AddToClassList("sa-settings-note");
                root.Add(help);
            }
            else
            {
                foreach (var action in HumanInputActions.RebindableActions)
                    if (action != "SpecialModifier") AddBindingRow(root, action, ActionLabel(action));
            }
            AddSlider(root, "Stick Deadzone", settings.StickDeadzone, 5f, 50f, settings.SetStickDeadzone);
            AddSlider(root, "Stick Look Speed", settings.StickLookSpeed * 100f, 10f, 200f, settings.SetStickLookSpeed);
            var vibration = new Toggle("Vibration") { value = settings.Vibration };
            vibration.AddToClassList("sa-settings-toggle");
            vibration.RegisterValueChangedCallback(evt => settings.SetVibration(evt.newValue));
            root.Add(vibration);
            var reset = new Button(() =>
            {
                if (HumanInputActions.IsCapturing) HumanInputActions.CancelCapture();
                settings.ResetControls();
                SelectCategory(Controls);
            }) { text = "RESET CONTROLS" };
            root.Add(reset);
        }

        private void AddBindingRow(VisualElement root, string action, string label)
        {
            var group = SelectedGroup;
            int index = HumanInputActions.BindingIndex(action, group);
            var row = new VisualElement();
            row.AddToClassList("sa-settings-binding-row");
            var name = new Label(label);
            name.AddToClassList("sa-settings-row-label");
            row.Add(name);
            var current = new Label(HumanInputActions.BindingLabel(action, group));
            current.AddToClassList("sa-settings-binding-current");
            row.Add(current);
            var bind = new Button(() => BeginCapture(action, label)) { text = "REMAP" };
            bind.AddToClassList("sa-settings-action");
            bind.SetEnabled(index >= 0);
            row.Add(bind);
            root.Add(row);
        }

        private void BeginCapture(string action, string label)
        {
            string group = SelectedGroup;
            _status!.text = _deviceName == "Controller"
                ? $"Press a {group} control for {label}. Select/View/Back cancels."
                : $"Press a {group} control for {label}. Escape cancels.";
            HumanInputActions.Rebind(action, group, (path, error) =>
            {
                if (_open && _category == Controls) SelectCategory(Controls);
                if (!string.IsNullOrEmpty(error)) _status.text = error;
                else if (!string.IsNullOrEmpty(path))
                {
                    ClientSettingsService.Instance.SetBindingOverrides(HumanInputActions.SaveOverrides());
                    _status.text = $"{label} bound to {HumanInputActions.BindingLabel(action, group)}.";
                }
                else _status.text = "Rebind cancelled.";
            });
        }

        private string SelectedGroup => _device?.value switch
        {
            "Mouse" => HumanInputActions.MouseGroup,
            "Controller" => HumanInputActions.GamepadGroup,
            _ => HumanInputActions.KeyboardGroup,
        };

        private static string ActionLabel(string action, bool controller = false) => action switch
        {
            "MoveUp" => "Move Forward", "MoveDown" => "Move Back", "MoveLeft" => "Move Left", "MoveRight" => "Move Right",
            "MoveStick" => "Analog Move Stick",
            "Down" => "Down (Crouch / Slide / Fast Fall)", "FaceToCamera" => "Face to Camera",
            "ToggleLock" => "Target Lock", "Retarget" => "Retarget Target", "Pause" => "Pause",
            "Retry" => controller ? "Arcade Retry (hold)" : "Arcade Retry",
            "Slot1" => controller ? "Normal 1" : "Action 1",
            "Slot2" => controller ? "Normal 2" : "Action 2",
            "Slot3" => controller ? "Normal 3" : "Action 3",
            "Slot4" => controller ? "Normal 4" : "Action 4",
            "SpecialModifier" => "Special Modifier",
            "Shield" => "Shield", "Grab" => "Grab", "Jump" => "Jump",
            "SlotA" => "Action A", "SlotE" => "Action E", "SlotR" => "Action R", "SlotF" => "Action F",
            _ => action,
        };

        private static void BuildGameplay(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            AddSlider(root, "Mouse Sensitivity", settings.CameraSensitivity * 100f, 10f, 200f, settings.SetCameraSensitivity);
            var horizontal = new Toggle("Invert Horizontal Camera") { value = settings.InvertCameraHorizontal };
            horizontal.RegisterValueChangedCallback(evt => settings.SetInvertCameraHorizontal(evt.newValue));
            root.Add(horizontal);
            var vertical = new Toggle("Invert Vertical Camera") { value = settings.InvertCameraVertical };
            vertical.RegisterValueChangedCallback(evt => settings.SetInvertCameraVertical(evt.newValue));
            root.Add(vertical);
            AddSlider(root, "Target Indicator Opacity", settings.TargetOpacity * 100f, 20f, 100f, settings.SetTargetOpacity);
            var overheadDamage = new Toggle("Show overhead damage") { value = settings.ShowOverheadDamage };
            overheadDamage.RegisterValueChangedCallback(evt => settings.SetShowOverheadDamage(evt.newValue));
            root.Add(overheadDamage);
            var overheadDamageNote = new Label("This hides only the target-lock damage percentage. Player panel percentages and the targeting marker remain visible.");
            overheadDamageNote.AddToClassList("sa-settings-note");
            root.Add(overheadDamageNote);
            var statsChoices = new List<string> { "Off", "Ping", "Detailed" };
            var stats = new DropdownField("Network Stats", statsChoices, statsChoices[settings.NetworkStats]);
            stats.RegisterValueChangedCallback(evt => settings.SetNetworkStats(statsChoices.IndexOf(evt.newValue)));
            root.Add(stats);
            var network = new Label("Network stats appear in the match HUD. Ping requires a live gameplay-server connection.");
            network.AddToClassList("sa-settings-note");
            root.Add(network);
            AddSlider(root, "Screen Shake", settings.ScreenShake * 100f, 0f, 100f, settings.SetScreenShake);
            var lockChoices = new List<string> { "Never", "Always", "On Hit" };
            var lockModes = new[] { TargetLockMode.Never, TargetLockMode.Always, TargetLockMode.OnHit };
            var lockMode = new DropdownField("Auto Target Lock", lockChoices,
                Array.IndexOf(lockModes, settings.AutoLockMode));
            lockMode.RegisterValueChangedCallback(evt =>
                settings.SetAutoLockMode(lockModes[lockChoices.IndexOf(evt.newValue)]));
            root.Add(lockMode);
            var sessionLog = new Toggle("Record practice session log") { value = settings.RecordSessionLog };
            sessionLog.RegisterValueChangedCallback(evt => settings.SetRecordSessionLog(evt.newValue));
            root.Add(sessionLog);
            var sessionLogNote = new Label("Saves a local file for each Training or Solo match you start, to measure hits, misses and camera handling. Nothing is uploaded. Files: "
                + SlopArena.Client.Diagnostics.PracticeSessionLog.Directory);
            sessionLogNote.AddToClassList("sa-settings-note");
            root.Add(sessionLogNote);
        }
        private static void AddSlider(VisualElement root, string label, float value, float min, float max, Action<float> setter)
        {
            var row = new VisualElement();
            row.AddToClassList("sa-settings-row");
            var title = new Label(label.ToUpperInvariant());
            title.AddToClassList("sa-settings-row-label");
            row.Add(title);
            var slider = new Slider(min, max) { value = value };
            slider.AddToClassList("sa-settings-slider");
            row.Add(slider);
            var amount = new Label($"{Mathf.RoundToInt(value)}%");
            amount.AddToClassList("sa-settings-value");
            row.Add(amount);
            slider.RegisterValueChangedCallback(evt => { setter(evt.newValue); amount.text = $"{Mathf.RoundToInt(evt.newValue)}%"; });
            root.Add(row);
        }
        private static void BuildAccessibility(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            var choices = new List<string> { "80%", "90%", "100%", "110%", "120%", "130%", "140%" };
            string current = $"{settings.UiScale}%";
            var scale = new DropdownField("UI Scale", choices, current);
            scale.RegisterValueChangedCallback(evt => settings.SetUiScale(int.Parse(evt.newValue.TrimEnd('%'))));
            root.Add(scale);
            var reduced = new Toggle("Reduced Flashing") { value = settings.ReducedFlashing };
            reduced.RegisterValueChangedCallback(evt => settings.SetReducedFlashing(evt.newValue));
            root.Add(reduced);
            var explanation = new Label("Reduces transient graphic hit rings and rays. Other visual effects may still flash.");
            explanation.AddToClassList("sa-settings-note");
            root.Add(explanation);
        }
        private static void ApplyReadableContrast(VisualElement root)
        {
            root.Query<DropdownField>().ForEach(field => field.AddToClassList("sa-settings-field"));
            root.Query<Toggle>().ForEach(toggle => toggle.AddToClassList("sa-settings-toggle"));
            root.Query<Button>().ForEach(button => button.AddToClassList("sa-settings-action"));
        }

        private void AskReset()
        {
            if (HumanInputActions.IsCapturing) HumanInputActions.CancelCapture();
            if (_resetConfirmation != null || _surface == null) return;
            _resetFocus = _surface.panel?.focusController.focusedElement as VisualElement;
            _resetConfirmation = new VisualElement();
            _resetConfirmation.AddToClassList("sa-settings-confirmation");
            var box = new VisualElement();
            box.AddToClassList("sa-settings-confirmation-card");
            var label = new Label("Reset all settings to defaults?");
            label.AddToClassList("sa-settings-confirmation-title");
            box.Add(label);
            var actions = new VisualElement();
            actions.AddToClassList("sa-settings-confirmation-actions");
            actions.Add(new Button(ConfirmReset) { text = "RESET" });
            actions.Add(new Button(CancelReset) { text = "CANCEL" });
            ApplyReadableContrast(actions);
            box.Add(actions);
            _resetConfirmation.Add(box); _surface.Add(_resetConfirmation); actions.Q<Button>()?.Focus();
        }

        private void CancelReset()
        {
            _resetConfirmation?.RemoveFromHierarchy();
            _resetConfirmation = null;
            _resetFocus?.Focus();
            _resetFocus = null;
        }

        private void BuildVideo(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            var modes = new List<string> { "Windowed", "Borderless", "Fullscreen" };
            string currentMode = Screen.fullScreenMode == FullScreenMode.FullScreenWindow ? "Borderless" :
                Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? "Fullscreen" : "Windowed";
            var mode = new DropdownField("Display Mode", modes, currentMode);
            root.Add(mode);
            var resolutions = new List<Resolution>();
            foreach (var item in Screen.resolutions)
                if (!resolutions.Exists(r => r.width == item.width && r.height == item.height)) resolutions.Add(item);
            if (!resolutions.Exists(r => r.width == Screen.width && r.height == Screen.height))
                resolutions.Add(new Resolution { width = Screen.width, height = Screen.height });
            var labels = resolutions.ConvertAll(r => $"{r.width} × {r.height}");
            int selected = resolutions.FindIndex(r => r.width == Screen.width && r.height == Screen.height);
            var resolution = new DropdownField("Resolution", labels, labels[Mathf.Max(selected, 0)]);
            root.Add(resolution);
            var qualities = new List<string> { "Low", "Medium", "High" };
            var quality = new DropdownField("Quality", qualities, qualities[Mathf.Clamp(settings.Quality, 0, 2)]);
            quality.RegisterValueChangedCallback(evt => settings.SetVideo(qualities.IndexOf(evt.newValue), settings.VSync, settings.TargetFps));
            root.Add(quality);
            var vsync = new Toggle("VSync (controls pacing when enabled)") { value = settings.VSync };
            vsync.RegisterValueChangedCallback(evt => settings.SetVideo(settings.Quality, evt.newValue, settings.TargetFps));
            root.Add(vsync);
            var fpsChoices = new List<string> { "60", "120", "144", "240", "Unlimited" };
            int fpsIndex = settings.TargetFps switch { 60 => 0, 120 => 1, 144 => 2, 240 => 3, _ => 4 };
            var fps = new DropdownField("FPS Limit", fpsChoices, fpsChoices[fpsIndex]);
            fps.RegisterValueChangedCallback(evt =>
            {
                int index = fpsChoices.IndexOf(evt.newValue);
                settings.SetVideo(settings.Quality, settings.VSync, index == 4 ? -1 : int.Parse(evt.newValue));
            });
            root.Add(fps);
            root.Add(new Button(() => BeginDisplayChange(mode.value, resolutions[labels.IndexOf(resolution.value)])) { text = "APPLY DISPLAY" });
        }

        private void BeginDisplayChange(string mode, Resolution resolution)
        {
            _displayRevertMode = Screen.fullScreenMode;
            _displayRevertWidth = Screen.width;
            _displayRevertHeight = Screen.height;
            var targetMode = mode switch
            {
                "Borderless" => FullScreenMode.FullScreenWindow,
                "Fullscreen" => FullScreenMode.ExclusiveFullScreen,
                _ => FullScreenMode.Windowed
            };
            Screen.SetResolution(resolution.width, resolution.height, targetMode);
            var box = new VisualElement();
            box.AddToClassList("sa-settings-confirmation");
            var panel = new VisualElement();
            panel.AddToClassList("sa-settings-confirmation-card");
            _displayCountdown = new Label();
            _displayCountdown.AddToClassList("sa-settings-confirmation-title");
            panel.Add(_displayCountdown);
            var actions = new VisualElement();
            actions.AddToClassList("sa-settings-confirmation-actions");
            actions.Add(new Button(() => ConfirmDisplay(targetMode, resolution.width, resolution.height)) { text = "KEEP" });
            actions.Add(new Button(RevertDisplay) { text = "REVERT" });
            ApplyReadableContrast(actions);
            panel.Add(actions);
            box.Add(panel);
            _resetConfirmation = box;
            _surface?.Add(box);
            _displayDeadline = Time.unscaledTime + 15f;
            actions.Q<Button>()?.Focus();
        }

        private void ConfirmDisplay(FullScreenMode mode, int width, int height)
        {
            ClientSettingsService.Instance.SetDisplay(mode, width, height);
            _resetConfirmation?.RemoveFromHierarchy(); _resetConfirmation = null; _displayCountdown = null;
        }

        private void RevertDisplay()
        {
            Screen.SetResolution(_displayRevertWidth, _displayRevertHeight, _displayRevertMode);
            _resetConfirmation?.RemoveFromHierarchy(); _resetConfirmation = null; _displayCountdown = null;
        }

        private void ConfirmReset()
        {
            ClientSettingsService.Instance.ResetAll();
            SelectCategory(_category);
            CancelReset();
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            evt.StopImmediatePropagation();
            // UI cancel is buttonEast; do not steal it from a controller rebind capture.
            if (!HumanInputActions.IsCapturing) HandleBack();
        }

        public void HandleBack()
        {
            if (_backConsumedFrame == Time.frameCount)
                return;
            _backConsumedFrame = Time.frameCount;
            if (HumanInputActions.IsCapturing) { HumanInputActions.CancelCapture(); return; }
            if (_displayCountdown != null) { RevertDisplay(); return; }
            if (_resetConfirmation != null) CancelReset();
            else Close();
        }

        public void Close()
        {
            if (_displayCountdown != null) RevertDisplay();
            if (HumanInputActions.IsCapturing) HumanInputActions.CancelCapture();
            _closedFrame = Time.frameCount;
            _open = false;
            ClientSettingsService.Instance.Save();
            FindFirstObjectByType<InputController>()?.RequireReleaseBeforeHumanInput();
            if (_host != null) _host.pickingMode = _originalPickingMode;
            _host = null;
            _surface?.RemoveFromHierarchy(); _surface = null; _body = null;
            UiModalState.Pop();
            _restoreFocus?.Focus(); _restoreFocus = null;
            _onClose?.Invoke(); _onClose = null;
        }

        private void OnDestroy()
        {
            if (_open) Close();
            if (Active == this) Active = null;
        }
    }
}
