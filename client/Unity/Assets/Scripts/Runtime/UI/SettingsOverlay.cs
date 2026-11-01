using System;
using System.Collections.Generic;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;
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
        private static readonly string[] Categories = { Audio, Controls, "VIDEO", Gameplay, "ACCESSIBILITY" };
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
        private string _deviceName = "Keyboard & Mouse";
        private string _category = Audio;
        private FullScreenMode _displayRevertMode;
        private int _displayRevertWidth;
        private int _displayRevertHeight;
        private float _displayDeadline;
        private Label? _displayCountdown;
        private int _backConsumedFrame = -1;

        private void Update()
        {
            if (_open && _surface != null)
            {
                MenuPrompts.ApplyInputMode(_surface);
                PollCategories();
            }
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
            MenuPrompts.ApplyInputMode(_surface);
            var tabs = _surface.Q<VisualElement>(className: "sa-settings-tabs");
            MenuPrompts.Flank(tabs, MenuAction.PrevSection, MenuAction.NextSection);
            tabs?.RegisterCallback<NavigationMoveEvent>(OnTabsNavigate);
            _body?.RegisterCallback<NavigationMoveEvent>(OnBodyNavigate);
            foreach (var name in Categories)
            {
                var tab = _surface.Q<Button>($"settings-tab-{name.ToLowerInvariant()}");
                tab.clicked += () => SelectCategory(name);
            }
            var reset = _surface.Q<Button>("settings-reset");
            reset.clicked += AskReset;
            var back = _surface.Q<Button>("settings-back");
            back.clicked += HandleBack;
            MenuPrompts.Stick(back, MenuAction.Back);
            _surface.RegisterCallback<ClickEvent>(_ => UISFX.PlayClick());
            _surface.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
            host.Add(_surface);
            UiModalState.Push();
            SelectCategory(_category);
            // The press that opened Settings can also reach the panel a frame
            // later; landing on BACK would close it at once, the tab is harmless.
            _surface.Q<Button>($"settings-tab-{_category.ToLowerInvariant()}")?.Focus();
        }

        private void PollCategories()
        {
            if (ChatInputGate.ExternalOverlaySuppressed || HumanInputActions.IsCapturing
                || _resetConfirmation != null || _displayCountdown != null
                || _surface?.panel?.focusController?.focusedElement is TextField)
                return;
            var pad = CouchPads.MenuPad;
            int step = pad?.rightShoulder.wasPressedThisFrame == true ? 1
                : pad?.leftShoulder.wasPressedThisFrame == true ? -1 : 0;
            if (step == 0) return;
            int index = (Array.IndexOf(Categories, _category) + step + Categories.Length) % Categories.Length;
            var tab = _surface?.Q<Button>($"settings-tab-{Categories[index].ToLowerInvariant()}");
            GamepadUIBridge.Submit(tab);
            tab?.Focus();
        }

        // Pad navigation only follows controls overlapping the current one's column,
        // and setting rows put their control right of the label, so Up/Down walk
        // the rows in order instead: tabs, each setting, then BACK.
        private void OnTabsNavigate(NavigationMoveEvent evt)
        {
            if (evt.direction == NavigationMoveEvent.Direction.Down && !HumanInputActions.IsCapturing)
            {
                var rows = SettingRows();
                MoveFocus(evt, rows.Count > 0 ? rows[0] : null);
            }
        }

        private void OnBodyNavigate(NavigationMoveEvent evt)
        {
            bool down = evt.direction == NavigationMoveEvent.Direction.Down;
            if (!down && evt.direction != NavigationMoveEvent.Direction.Up || HumanInputActions.IsCapturing) return;
            var rows = SettingRows();
            var focused = _surface?.focusController?.focusedElement as VisualElement;
            int index = rows.FindIndex(row => row == focused || row.Contains(focused));
            if (index < 0) return;
            int next = index + (down ? 1 : -1);
            MoveFocus(evt, next < 0 ? _surface?.Q<Button>($"settings-tab-{_category.ToLowerInvariant()}")
                : next >= rows.Count ? _surface?.Q<Button>("settings-back") : rows[next]);
        }

        private List<VisualElement> SettingRows()
        {
            var rows = new List<VisualElement>();
            _body?.Query<VisualElement>().ForEach(e =>
            {
                if (e.focusable && e.canGrabFocus && !rows.Exists(row => row.Contains(e))) rows.Add(e);
            });
            return rows;
        }

        private void MoveFocus(NavigationMoveEvent evt, VisualElement? target)
        {
            if (target == null) return;
            evt.StopPropagation();
            _surface?.focusController?.IgnoreEvent(evt);
            target.Focus();
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
            var mute = new Toggle("MUTE WHEN UNFOCUSED") { value = settings.MuteWhenUnfocused };
            mute.AddToClassList("sa-settings-toggle");
            mute.RegisterValueChangedCallback(evt => settings.SetMuteWhenUnfocused(evt.newValue));
            root.Add(mute);
        }

        private void BuildControls(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            _device = new DropdownField("DEVICE",
                new System.Collections.Generic.List<string> { "Keyboard & Mouse", "Controller" },
                _deviceName == "Controller" ? 1 : 0);
            _device.AddToClassList("sa-settings-field");
            _device.RegisterValueChangedCallback(evt => { _deviceName = evt.newValue; SelectCategory(Controls); });
            root.Add(_device);
            if (_deviceName == "Controller")
            {
                var stickChoices = new System.Collections.Generic.List<string> { "Specials", "Camera" };
                var rightStick = new DropdownField("RIGHT STICK", stickChoices, settings.RightStickSpecials ? 0 : 1)
                    { name = "right-stick-mode" };
                rightStick.AddToClassList("sa-settings-field");
                rightStick.RegisterValueChangedCallback(evt =>
                {
                    settings.SetRightStickSpecials(evt.newValue == stickChoices[0]);
                    SelectCategory(Controls);
                });
                root.Add(rightStick);
                bool stickSpecials = settings.RightStickSpecials;
                foreach (var action in new[]
                         {
                             "MoveStick", "Normal1", "Normal2", "Normal3", stickSpecials ? "Grab" : "SpecialModifier",
                             "Jump", "Dash", "Down", "Shield", "FaceToCamera", "ToggleLock", "Retarget", "Pause", "Retry"
                         })
                    AddBindingRow(root, action, ActionLabel(action, controller: true));
                var help = new Label(stickSpecials
                    ? "Specials: flick the right stick. Up E, left R, right F, down Q. Hold it to aim with the left stick or to charge. The camera turns by itself."
                    : "Specials: hold Special Modifier + Jump or a Normal button. Grab: hold Special Modifier + RT. The right stick turns the camera.");
                help.AddToClassList("sa-settings-note");
                root.Add(help);
            }
            else
            {
                foreach (var action in HumanInputActions.RebindableActions)
                    if (action != "SpecialModifier" && HumanInputActions.BindingIndex(action, SelectedGroup) >= 0)
                        AddBindingRow(root, action, ActionLabel(action));
                var help = new Label("Use any key, mouse button or wheel direction. Clear a binding to free its control for another action. Wheel directions assigned to actions stop zooming the camera.");
                help.AddToClassList("sa-settings-note");
                root.Add(help);
            }
            if (_deviceName == "Controller")
            {
                var snap = new Toggle("STICK 8-WAY SNAP") { name = "stick-8-way-snap", value = settings.Stick8WaySnap };
                snap.AddToClassList("sa-settings-toggle");
                snap.RegisterValueChangedCallback(evt => settings.SetStick8WaySnap(evt.newValue));
                root.Add(snap);
                AddSlider(root, "Stick Deadzone", settings.StickDeadzone, 5f, 50f, settings.SetStickDeadzone);
                if (!settings.RightStickSpecials)
                    AddSlider(root, "Stick Look Speed", settings.StickLookSpeed * 100f, 10f, 200f, settings.SetStickLookSpeed);
                var vibration = new Toggle("VIBRATION") { value = settings.Vibration };
                vibration.AddToClassList("sa-settings-toggle");
                vibration.RegisterValueChangedCallback(evt => settings.SetVibration(evt.newValue));
                root.Add(vibration);
            }
            var reset = new Button(() =>
            {
                if (HumanInputActions.IsCapturing) HumanInputActions.CancelCapture();
                settings.ResetControls();
                SelectCategory(Controls);
            }) { text = "RESET CONTROLS" };
            reset.AddToClassList("sa-settings-body-action");
            root.Add(reset);
        }

        private void AddBindingRow(VisualElement root, string action, string label)
        {
            var group = SelectedGroup;
            int index = HumanInputActions.BindingIndex(action, group);
            var row = new VisualElement { name = $"settings-binding-{action}" };
            row.AddToClassList("sa-settings-binding-row");
            var name = new Label(label.ToUpperInvariant());
            name.AddToClassList("sa-settings-row-label");
            row.Add(name);
            var current = new Label(HumanInputActions.BindingLabel(action, group));
            current.AddToClassList("sa-settings-binding-current");
            row.Add(current);
            var bind = new Button(() => BeginCapture(action, label)) { name = $"settings-rebind-{action}", text = "REMAP" };
            bind.AddToClassList("sa-settings-action");
            bind.SetEnabled(index >= 0);
            row.Add(bind);
            var clear = new Button(() =>
            {
                HumanInputActions.ClearBinding(action, group);
                ClientSettingsService.Instance.SetBindingOverrides(HumanInputActions.SaveOverrides());
                SelectCategory(Controls);
                _status!.text = $"{label} binding cleared.";
            }) { name = $"settings-clear-{action}", text = "CLEAR" };
            clear.AddToClassList("sa-settings-action");
            clear.SetEnabled(index >= 0 && !string.IsNullOrEmpty(HumanInputActions.Get(action).bindings[index].effectivePath));
            row.Add(clear);
            root.Add(row);
        }

        private void BeginCapture(string action, string label)
        {
            string group = SelectedGroup;
            _status!.text = _deviceName == "Controller"
                ? $"Press a {group} control for {label}. Select/View/Back cancels."
                : $"Press a key, mouse button or wheel direction for {label}. Escape cancels.";
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
            "Controller" => HumanInputActions.GamepadGroup,
            _ => HumanInputActions.KeyboardMouseGroup,
        };

        private static string ActionLabel(string action, bool controller = false) => action switch
        {
            "MoveUp" => "Move Forward", "MoveDown" => "Move Back", "MoveLeft" => "Move Left", "MoveRight" => "Move Right",
            "MoveStick" => "Analog Move Stick",
            "Down" => "Down (Crouch / Slide / Fast Fall)", "FaceToCamera" => "Face to Camera",
            "ToggleLock" => controller ? "Target Lock (tap)" : "Target Lock",
            "CameraToggle" => controller ? "Camera Toggle (hold)" : "Camera Toggle", "Retarget" => "Retarget Target", "Pause" => "Pause",
            "Retry" => controller ? "Arcade Retry (hold)" : "Arcade Retry",
            "Normal1" => controller ? "Normal 1" : "Action 1",
            "Normal2" => controller ? "Normal 2" : "Action 2",
            "Normal3" => controller ? "Normal 3" : "Action 3",
            "Dash" => "Beeline",
            "SpecialModifier" => "Special Modifier",
            "Shield" => "Shield", "Grab" => "Grab", "Walk" => "Walk", "Jump" => "Jump",
            "SlotA" => "Action A", "SlotE" => "Action E", "SlotR" => "Action R", "SlotF" => "Action F",
            _ => action,
        };

        private static void BuildGameplay(VisualElement root)
        {
            var settings = ClientSettingsService.Instance;
            var cameraChoices = new List<string> { "Steady", "Arena", "Manual" };
            var cameraStyles = new[] { CameraStyle.Steady, CameraStyle.Arena, CameraStyle.Manual };
            void AddCameraRow(string label, string name, bool controller)
            {
                var cameraStyle = new DropdownField(label, cameraChoices,
                    Array.IndexOf(cameraStyles, settings.ResolveCameraStyle(controller))) { name = name };
                var cameraNote = new Label { name = name + "-description" };
                cameraNote.AddToClassList("sa-settings-note");
                void DescribeCamera() => cameraNote.text = settings.ResolveCameraStyle(controller) switch
                {
                    CameraStyle.Steady => "A high view centred on you. It turns slowly, only when your target leaves a wide cone, and holds still when anyone jumps. Bubbles at the screen edge show fighters it cannot hold. The mouse, or the right stick when it is set to Camera, overrides briefly.",
                    CameraStyle.Arena => "A wider, elevated view that follows and zooms to frame the fighters. The viewing direction stays steady; right stick or mouse adjusts the angle.",
                    _ => "The original player-following camera. Turn with the right stick or mouse.",
                };
                cameraStyle.RegisterValueChangedCallback(evt =>
                {
                    int index = cameraChoices.IndexOf(evt.newValue);
                    if (index < 0) return;
                    settings.SetCameraStyle(cameraStyles[index], controller);
                    DescribeCamera();
                });
                DescribeCamera();
                root.Add(cameraStyle);
                root.Add(cameraNote);
            }
            AddCameraRow("CONTROLLER CAMERA", "controller-camera-style", true);
            AddCameraRow("MOUSE CAMERA", "camera-style", false);
            var turnChoices = new List<string> { "Calm", "Medium", "Quick" };
            var turnSpeeds = new[] { 20f, 30f, 45f };
            int turnIndex = Array.FindIndex(turnSpeeds, speed => Mathf.Approximately(speed, settings.CameraTurnSpeed));
            var turnSpeed = new DropdownField("STEADY TURN SPEED", turnChoices, Mathf.Max(0, turnIndex)) { name = "camera-turn-speed" };
            turnSpeed.RegisterValueChangedCallback(evt =>
            {
                int index = turnChoices.IndexOf(evt.newValue);
                if (index >= 0) settings.SetCameraTurnSpeed(turnSpeeds[index]);
            });
            root.Add(turnSpeed);
            var holdRun = new Toggle("STEADY: HOLD RUN DIRECTION") { value = settings.HoldRunDirection, name = "camera-hold-run" };
            holdRun.RegisterValueChangedCallback(evt => settings.SetHoldRunDirection(evt.newValue));
            root.Add(holdRun);
            var holdRunNote = new Label("While the stick stays held, your run keeps its direction even as the Steady camera turns.");
            holdRunNote.AddToClassList("sa-settings-note");
            root.Add(holdRunNote);
            AddSlider(root, "Mouse Sensitivity", settings.CameraSensitivity * 100f, 10f, 200f, settings.SetCameraSensitivity);
            AddSlider(root, "Stick Look Speed", settings.StickLookSpeed * 100f, 10f, 200f, settings.SetStickLookSpeed);
            var horizontal = new Toggle("INVERT HORIZONTAL CAMERA") { value = settings.InvertCameraHorizontal };
            horizontal.RegisterValueChangedCallback(evt => settings.SetInvertCameraHorizontal(evt.newValue));
            root.Add(horizontal);
            var vertical = new Toggle("INVERT VERTICAL CAMERA") { value = settings.InvertCameraVertical };
            vertical.RegisterValueChangedCallback(evt => settings.SetInvertCameraVertical(evt.newValue));
            root.Add(vertical);
            AddSlider(root, "Target Indicator Opacity", settings.TargetOpacity * 100f, 20f, 100f, settings.SetTargetOpacity);
            var overheadDamage = new Toggle("SHOW OVERHEAD DAMAGE") { value = settings.ShowOverheadDamage };
            overheadDamage.RegisterValueChangedCallback(evt => settings.SetShowOverheadDamage(evt.newValue));
            root.Add(overheadDamage);
            var overheadDamageNote = new Label("This hides only the target-lock damage percentage. Player panel percentages and the targeting marker remain visible.");
            overheadDamageNote.AddToClassList("sa-settings-note");
            root.Add(overheadDamageNote);
            var statsChoices = new List<string> { "Off", "Ping", "Detailed" };
            var stats = new DropdownField("NETWORK STATS", statsChoices, statsChoices[settings.NetworkStats]);
            stats.RegisterValueChangedCallback(evt => settings.SetNetworkStats(statsChoices.IndexOf(evt.newValue)));
            root.Add(stats);
            var network = new Label("Network stats appear in the match HUD. Ping requires a live gameplay-server connection.");
            network.AddToClassList("sa-settings-note");
            root.Add(network);
            AddSlider(root, "Screen Shake", settings.ScreenShake * 100f, 0f, 100f, settings.SetScreenShake);
            var lockChoices = new List<string> { "Never", "Always", "On Hit" };
            var lockModes = new[] { TargetLockMode.Never, TargetLockMode.Always, TargetLockMode.OnHit };
            var lockMode = new DropdownField("AUTO TARGET LOCK", lockChoices,
                Array.IndexOf(lockModes, settings.AutoLockMode));
            lockMode.RegisterValueChangedCallback(evt =>
                settings.SetAutoLockMode(lockModes[lockChoices.IndexOf(evt.newValue)]));
            root.Add(lockMode);
            var sessionLog = new Toggle("RECORD PRACTICE SESSION LOG") { value = settings.RecordSessionLog };
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
            var scale = new DropdownField("UI SCALE", choices, current);
            scale.RegisterValueChangedCallback(evt => settings.SetUiScale(int.Parse(evt.newValue.TrimEnd('%'))));
            root.Add(scale);
            var reduced = new Toggle("REDUCED FLASHING") { value = settings.ReducedFlashing };
            reduced.RegisterValueChangedCallback(evt => settings.SetReducedFlashing(evt.newValue));
            root.Add(reduced);
            var explanation = new Label("Reduces transient graphic hit rings and rays. Other visual effects may still flash.");
            explanation.AddToClassList("sa-settings-note");
            root.Add(explanation);
        }
        // Labels are uppercased where each control is built. Never rewrite a field's
        // label afterwards: the label's ChangeEvent<string> bubbles into the field's
        // own value callback (a dropdown would rebuild its tab, or parse its label).
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
            var cancel = new Button(CancelReset) { text = "CANCEL" };
            actions.Add(cancel);
            ApplyReadableContrast(actions);
            box.Add(actions);
            // Start on the safe choice: a stray confirm press must not wipe settings.
            _resetConfirmation.Add(box); _surface.Add(_resetConfirmation); cancel.Focus();
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
            var mode = new DropdownField("DISPLAY MODE", modes, currentMode);
            root.Add(mode);
            var resolutions = new List<Resolution>();
            foreach (var item in Screen.resolutions)
                if (!resolutions.Exists(r => r.width == item.width && r.height == item.height)) resolutions.Add(item);
            if (!resolutions.Exists(r => r.width == Screen.width && r.height == Screen.height))
                resolutions.Add(new Resolution { width = Screen.width, height = Screen.height });
            var labels = resolutions.ConvertAll(r => $"{r.width} × {r.height}");
            int selected = resolutions.FindIndex(r => r.width == Screen.width && r.height == Screen.height);
            var resolution = new DropdownField("RESOLUTION", labels, labels[Mathf.Max(selected, 0)]);
            root.Add(resolution);
            var qualities = new List<string> { "Low", "Medium", "High" };
            var quality = new DropdownField("QUALITY", qualities, qualities[Mathf.Clamp(settings.Quality, 0, 2)]);
            quality.RegisterValueChangedCallback(evt => settings.SetVideo(qualities.IndexOf(evt.newValue), settings.VSync, settings.TargetFps));
            root.Add(quality);
            var vsync = new Toggle("VSYNC (CONTROLS PACING WHEN ENABLED)") { value = settings.VSync };
            vsync.RegisterValueChangedCallback(evt => settings.SetVideo(settings.Quality, evt.newValue, settings.TargetFps));
            root.Add(vsync);
            var fpsChoices = new List<string> { "60", "120", "144", "240", "Unlimited" };
            int fpsIndex = settings.TargetFps switch { 60 => 0, 120 => 1, 144 => 2, 240 => 3, _ => 4 };
            var fps = new DropdownField("FPS LIMIT", fpsChoices, fpsChoices[fpsIndex]);
            fps.RegisterValueChangedCallback(evt =>
            {
                int index = fpsChoices.IndexOf(evt.newValue);
                settings.SetVideo(settings.Quality, settings.VSync, index == 4 ? -1 : int.Parse(evt.newValue));
            });
            root.Add(fps);
            var apply = new Button(() => BeginDisplayChange(mode.value, resolutions[labels.IndexOf(resolution.value)])) { text = "APPLY DISPLAY" };
            apply.AddToClassList("sa-settings-body-action");
            root.Add(apply);
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
