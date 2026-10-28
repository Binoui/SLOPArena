using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace SlopArena.Client.Input
{
    /// <summary>One action map for human gameplay, camera, utilities, and rebinding.</summary>
    public static class HumanInputActions
    {
        public const string KeyboardMouseGroup = "KeyboardMouse";
        private const string MouseGroup = "Mouse";
        public const string GamepadGroup = "Gamepad";

        private static readonly Dictionary<string, InputAction> Actions = new();
        private static InputActionAsset? _asset;
        private static InputActionMap? _map;
        private static InputActionRebindingExtensions.RebindingOperation? _operation;
        private static float _defaultDeadzone = -1f;
        private static bool _capturing;

        public static int BindingRevision { get; private set; }
        public static bool LastUsedGamepad { get; internal set; }

        [Serializable]
        private sealed class BindingOverridesJson { public BindingOverrideJson[] bindings; }
        [Serializable]
        private sealed class BindingOverrideJson
        {
            public string action;
            public string id;
            public string path;
            public string interactions;
            public string processors;
            public string group;
        }

        public static float DefaultDeadzone { get { EnsureCreated(); return _defaultDeadzone; } }
        public static bool IsCapturing => _capturing;
        public static InputAction MouseLook => Get("MouseLook");
        public static InputAction StickLook => Get("StickLook");
        public static InputAction Zoom => Get("Zoom");
        public static float ZoomDelta
        {
            get
            {
                float delta = Zoom.ReadValue<float>();
                if (delta == 0f) return 0f;
                string direction = delta > 0f ? "<Mouse>/scroll/up" : "<Mouse>/scroll/down";
                foreach (string name in RebindableActions)
                    foreach (var binding in Get(name).bindings)
                        if (string.Equals(binding.effectivePath, direction, StringComparison.OrdinalIgnoreCase))
                            return 0f;
                return delta;
            }
        }
        public static IReadOnlyList<string> RebindableActions { get; } = new[]
        {
            "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "MoveStick", "Jump", "Down",
            "Shield", "Grab", "Dash", "Slot1", "Slot3", "Slot4", "SlotA", "SlotE", "SlotR", "SlotF",
            "SpecialModifier", "FaceToCamera", "ToggleLock", "CameraToggle", "Retarget", "Pause", "Retry"
        };

        public static void Initialize(string overridesJson)
        {
            EnsureCreated();
            if (_map!.enabled || _capturing) return;
            ApplyOverrides(overridesJson);
            _map.Enable();
        }

        public static InputAction Get(string name) { EnsureCreated(); return Actions[name]; }

        /// <summary>Couch play: the shared map reads only P1's devices; null restores every device.</summary>
        public static void RestrictToDevices(InputDevice[]? devices)
        {
            EnsureCreated();
            _map!.devices = devices == null ? null : new ReadOnlyArray<InputDevice>(devices);
        }

        /// <summary>An enabled copy of the gameplay map that reads only the given devices.</summary>
        public static InputActionMap CreatePlayerMap(params InputDevice[] devices)
        {
            EnsureCreated();
            var map = _map!.Clone();
            map.devices = new ReadOnlyArray<InputDevice>(devices);
            map.Enable();
            return map;
        }
        public static string SaveOverrides()
        {
            EnsureCreated();
            string json = _asset!.SaveBindingOverridesAsJson();
            if (string.IsNullOrEmpty(json)) return json;
            var data = JsonUtility.FromJson<BindingOverridesJson>(json);
            // Runtime-created binding IDs change between launches. Keep the device group even
            // for cleared paths, which cannot identify their original binding by device.
            foreach (var entry in data.bindings)
                if (TryGetOverrideAction(entry, out var action))
                    foreach (var binding in action.bindings)
                        if (binding.id.ToString() == entry.id)
                        {
                            entry.group = binding.groups;
                            break;
                        }
            return JsonUtility.ToJson(data);
        }
        public static void CancelCapture() => _operation?.Cancel();

        /// <summary>
        /// The right stick flicks the specials and LB grabs; false keeps the stick on the camera
        /// with LB as the special modifier. Set from Client Settings; prompts refresh on change.
        /// </summary>
        public static bool RightStickSpecials { get; private set; } = true;
        public static void SetRightStickSpecials(bool value)
        {
            if (RightStickSpecials == value) return;
            RightStickSpecials = value;
            BindingRevision++;
        }

        public static void ClearBinding(string actionName, string group)
        {
            if (_capturing) return;
            int index = BindingIndex(actionName, group);
            if (index < 0) return;
            Get(actionName).ApplyBindingOverride(index, "");
            BindingRevision++;
        }

        public static void ApplyOverrides(string json)
        {
            EnsureCreated();
            bool wasEnabled = _map!.enabled;
            _map.Disable();
            _asset!.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(json))
            {
                try { _asset.LoadBindingOverridesFromJson(ResolveOverrideIds(json)); }
                catch (Exception ex) { Debug.LogWarning($"[Input] Ignoring invalid binding overrides: {ex.Message}"); }
            }
            if (wasEnabled) _map.Enable();
            BindingRevision++;
        }

        public static void ResetOverrides()
        {
            EnsureCreated();
            bool wasEnabled = _map!.enabled;
            _map.Disable();
            _asset!.RemoveAllBindingOverrides();
            if (wasEnabled) _map.Enable();
            BindingRevision++;
        }

        public static int BindingIndex(string actionName, string group)
        {
            var action = Get(actionName);
            for (int i = 0; i < action.bindings.Count; i++)
                if (action.bindings[i].groups?.Contains(group, StringComparison.OrdinalIgnoreCase) == true)
                    return i;
            return -1;
        }

        public static string BindingLabel(string actionName, string group)
        {
            int index = BindingIndex(actionName, group);
            if (index < 0) return "Unbound";
            var action = Get(actionName);
            if (string.IsNullOrEmpty(action.bindings[index].effectivePath)) return "Unbound";
            string label = action.GetBindingDisplayString(index);
            // Some platform keyboards name non-printing keys with control characters (Backspace
            // and Escape on Linux render as boxes); the layout's control name reads everywhere.
            bool printable = !string.IsNullOrWhiteSpace(label);
            foreach (char c in label ?? "") printable &= !char.IsControl(c);
            return printable ? label : InputControlPath.ToHumanReadableString(action.bindings[index].effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
        }

        // Number-row binds are physical keys: AZERTY reports &/é/"/' as their
        // legends, but the canonical 1–4 prompts should keep their digits.
        public static string KeyboardDisplayLabel(string path, string label)
        {
            const string prefix = "<Keyboard>/";
            if (path == null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.Length != prefix.Length + 1 || !char.IsDigit(path[prefix.Length])) return label;
            return path.Substring(prefix.Length);
        }

        public static void Rebind(string actionName, string group, Action<string, string> completed)
        {
            if (_capturing) return;
            int bindingIndex = BindingIndex(actionName, group);
            if (bindingIndex < 0) { completed(null, "This action has no binding for that device."); return; }

            var action = Get(actionName);
            string priorOverride = action.bindings[bindingIndex].overridePath;
            _capturing = true;
            _map!.Disable();
            string cancelPath = group == GamepadGroup ? "<Gamepad>/select" : "<Keyboard>/escape";
            _operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough(cancelPath)
                .OnComplete(op =>
                {
                    string path = action.bindings[bindingIndex].overridePath ?? action.bindings[bindingIndex].path;
                    string conflict = FindConflict(actionName, path, group);
                    if (conflict != null)
                    {
                        action.ApplyBindingOverride(bindingIndex, new InputBinding { overridePath = priorOverride });
                        FinishCapture(op);
                        completed(null, $"Already bound to {conflict}; clear that binding or choose another control.");
                    }
                    else
                    {
                        BindingRevision++;
                        FinishCapture(op);
                        completed(path, null);
                    }
                })
                .OnCancel(op => { FinishCapture(op); completed(null, null); });
            if (group == GamepadGroup)
                _operation.WithControlsHavingToMatchPath("<Gamepad>");
            else
                _operation.WithControlsHavingToMatchPath("<Keyboard>")
                    .WithControlsHavingToMatchPath("<Mouse>")
                    // Wheel directions are Axis controls; the signed x/y controls would
                    // bind both directions. Buttons also derive from Axis.
                    .WithExpectedControlType("Axis")
                    .WithControlsExcluding("<Mouse>/scroll/x")
                    .WithControlsExcluding("<Mouse>/scroll/y")
                    .OnMatchWaitForAnother(0f);
            _operation.Start();
        }

        private static string ResolveOverrideIds(string json)
        {
            var data = JsonUtility.FromJson<BindingOverridesJson>(json);
            if (data?.bindings == null) return json;
            foreach (var entry in data.bindings)
            {
                if (!TryGetOverrideAction(entry, out var action) || entry.path == null) continue;
                string name = action.name;
                string group = !string.IsNullOrEmpty(entry.group) ? entry.group
                    : entry.path.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase)
                    ? GamepadGroup
                    : name is "MouseLook" or "Zoom" ? MouseGroup : KeyboardMouseGroup;
                if (group == GamepadGroup && (name == "SlotA" || name == "SlotE" ||
                    name == "SlotR" || name == "SlotF" ||
                    name.StartsWith("Move", StringComparison.Ordinal) && name != "MoveStick"))
                    continue;
                for (int i = 0; i < action.bindings.Count; i++)
                    if (action.bindings[i].groups?.Contains(group, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        entry.id = action.bindings[i].id.ToString();
                        break;
                    }
            }
            return JsonUtility.ToJson(data);
        }

        private static bool TryGetOverrideAction(BindingOverrideJson entry, out InputAction action)
        {
            action = null;
            if (string.IsNullOrEmpty(entry.action)) return false;
            int slash = entry.action.LastIndexOf('/');
            string name = slash >= 0 ? entry.action.Substring(slash + 1) : entry.action;
            return Actions.TryGetValue(name, out action);
        }

        private static string FindConflict(string actionName, string path, string group)
        {
            foreach (var pair in Actions)
            {
                if (pair.Key == actionName) continue;
                if (group == GamepadGroup &&
                    (actionName == "ToggleLock" && pair.Key == "CameraToggle" ||
                     actionName == "CameraToggle" && pair.Key == "ToggleLock" ||
                     // Only one of the two is read, depending on the right-stick setting.
                     actionName == "Grab" && pair.Key == "SpecialModifier" ||
                     actionName == "SpecialModifier" && pair.Key == "Grab")) continue;
                foreach (var binding in pair.Value.bindings)
                    if (binding.groups?.Contains(group, StringComparison.OrdinalIgnoreCase) == true &&
                        string.Equals(binding.effectivePath, path, StringComparison.OrdinalIgnoreCase))
                        return pair.Key;
            }
            return null;
        }

        private static void FinishCapture(InputActionRebindingExtensions.RebindingOperation operation)
        {
            operation.Dispose();
            _operation = null;
            _capturing = false;
            _map!.Enable();
        }

        private static void EnsureCreated()
        {
            if (_asset != null) return;
            Actions.Clear();
            _defaultDeadzone = Mathf.Clamp(InputSystem.settings.defaultDeadzoneMin, 0.05f, 0.5f);
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _map = new InputActionMap("Gameplay");
            _asset.AddActionMap(_map);
            _map.actionTriggered += context =>
            {
                if (!context.performed || context.action.name is "MouseLook" or "Zoom") return;
                if (context.control.device is Gamepad)
                {
                    if (context.action.name is "MoveStick" or "StickLook" &&
                        context.ReadValue<Vector2>().sqrMagnitude < 0.09f) return;
                    LastUsedGamepad = true;
                }
                else if (context.control.device is Keyboard or Mouse)
                    LastUsedGamepad = false;
            };
            AddButton("MoveUp", "<Keyboard>/w");
            AddButton("MoveDown", "<Keyboard>/s");
            AddButton("MoveLeft", "<Keyboard>/a");
            AddButton("MoveRight", "<Keyboard>/d");
            // Controller layout v2 (ADR-0042): A jumps, X/Y/B are the three normals, RB dashes.
            // Right-stick specials (default): a right-stick flick is a special and LB grabs.
            // With the stick on the camera, LB + a face button is a special, A included, and
            // LB + RT grabs. Grab and SpecialModifier share LB; InputController reads only the one
            // the setting selects.
            AddButton("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            AddButton("Shield", "<Keyboard>/leftShift", "<Gamepad>/rightTrigger");
            AddButton("Grab", "<Keyboard>/c", "<Gamepad>/leftShoulder");
            AddButton("Dash", "<Keyboard>/4", "<Gamepad>/rightShoulder");
            AddButton("Down", "<Keyboard>/x", "<Gamepad>/leftTrigger");
            AddButton("Slot1", "<Keyboard>/1", "<Gamepad>/buttonWest");
            AddButton("Slot3", "<Keyboard>/2", "<Gamepad>/buttonNorth");
            AddButton("Slot4", "<Keyboard>/3", "<Gamepad>/buttonEast");
            // Keyboard paths are key positions: the key right of Tab reads A on AZERTY and Q on
            // QWERTY. Binding the key that types "A" put special A on move-left for QWERTY players.
            AddButton("SlotA", "<Keyboard>/q");
            AddButton("SlotE", "<Keyboard>/e");
            AddButton("SlotR", "<Keyboard>/r");
            AddButton("SlotF", "<Keyboard>/f");
            var modifier = _map.AddAction("SpecialModifier", InputActionType.Button);
            modifier.AddBinding("<Gamepad>/leftShoulder").WithGroup(GamepadGroup);
            Actions.Add("SpecialModifier", modifier);
            AddButton("FaceToCamera", "<Mouse>/leftButton", "<Gamepad>/leftStickPress");
            AddButton("ToggleLock", "<Mouse>/rightButton", "<Gamepad>/rightStickPress");
            Get("ToggleLock").ChangeBinding(BindingIndex("ToggleLock", GamepadGroup)).WithInteraction("tap(duration=0.45)");
            AddButton("CameraToggle", "<Keyboard>/y", "<Gamepad>/rightStickPress");
            Get("CameraToggle").ChangeBinding(BindingIndex("CameraToggle", GamepadGroup)).WithInteraction("hold(duration=0.45)");
            AddButton("Retarget", "<Keyboard>/tab", "<Gamepad>/dpad/right");
            AddButton("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            // Arcade only: a full fresh attempt. ArcadeRetryInput requires a short gamepad hold.
            AddButton("Retry", "<Keyboard>/backspace", "<Gamepad>/select");
            AddValue("MoveStick", "<Gamepad>/leftStick", "Vector2");
            AddValue("MouseLook", "<Mouse>/delta", "Vector2");
            AddValue("StickLook", "<Gamepad>/rightStick", "Vector2");
            AddValue("Zoom", "<Mouse>/scroll/y", "Axis");
        }

        private static void AddButton(string name, string keyboardOrMouse, string gamepad = null)
        {
            var action = _map!.AddAction(name, InputActionType.Button);
            action.AddBinding(keyboardOrMouse).WithGroup(KeyboardMouseGroup);
            if (gamepad != null) action.AddBinding(gamepad).WithGroup(GamepadGroup);
            Actions.Add(name, action);
        }

        private static void AddValue(string name, string path, string controlType)
        {
            var action = _map!.AddAction(name, InputActionType.Value, expectedControlLayout: controlType);
            action.AddBinding(path).WithGroup(path.StartsWith("<Gamepad>", StringComparison.Ordinal) ? GamepadGroup : MouseGroup);
            Actions.Add(name, action);
        }
    }
}
