using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.Input
{
    /// <summary>One action map for human gameplay, camera, utilities, and rebinding.</summary>
    public static class HumanInputActions
    {
        public const string KeyboardGroup = "Keyboard";
        public const string MouseGroup = "Mouse";
        public const string GamepadGroup = "Gamepad";

        private static readonly Dictionary<string, InputAction> Actions = new();
        private static InputActionAsset? _asset;
        private static InputActionMap? _map;
        private static InputActionRebindingExtensions.RebindingOperation? _operation;
        private static float _defaultDeadzone = -1f;
        private static bool _capturing;

        public static int BindingRevision { get; private set; }
        public static bool LastUsedGamepad { get; private set; }

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
        }

        public static float DefaultDeadzone { get { EnsureCreated(); return _defaultDeadzone; } }
        public static bool IsCapturing => _capturing;
        public static InputAction MouseLook => Get("MouseLook");
        public static InputAction StickLook => Get("StickLook");
        public static InputAction Zoom => Get("Zoom");
        public static IReadOnlyList<string> RebindableActions { get; } = new[]
        {
            "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "MoveStick", "Jump", "Down",
            "Shield", "Grab", "Slot1", "Slot2", "Slot3", "Slot4", "SlotA", "SlotE", "SlotR", "SlotF",
            "SpecialModifier", "FaceToCamera", "ToggleLock", "Retarget", "Pause", "Retry"
        };

        public static void Initialize(string overridesJson)
        {
            EnsureCreated();
            if (_map!.enabled || _capturing) return;
            ApplyOverrides(overridesJson);
            _map.Enable();
        }

        public static InputAction Get(string name) { EnsureCreated(); return Actions[name]; }
        public static string SaveOverrides() { EnsureCreated(); return _asset!.SaveBindingOverridesAsJson(); }
        public static void CancelCapture() => _operation?.Cancel();

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
            string label = action.GetBindingDisplayString(index);
            // Some platform keyboards name non-printing keys with control characters (Backspace
            // and Escape on Linux render as boxes); the layout's control name reads everywhere.
            bool printable = !string.IsNullOrWhiteSpace(label);
            foreach (char c in label ?? "") printable &= !char.IsControl(c);
            return printable ? label : InputControlPath.ToHumanReadableString(action.bindings[index].effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
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
            string devicePath = group == GamepadGroup ? "<Gamepad>" : group == MouseGroup ? "<Mouse>" : "<Keyboard>";
            string cancelPath = group == GamepadGroup ? "<Gamepad>/select" : "<Keyboard>/escape";
            _operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsHavingToMatchPath(devicePath)
                .WithCancelingThrough(cancelPath)
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/scroll")
                .OnComplete(op =>
                {
                    string path = action.bindings[bindingIndex].overridePath ?? action.bindings[bindingIndex].path;
                    string conflict = FindConflict(actionName, path, group);
                    if (conflict != null)
                    {
                        action.ApplyBindingOverride(bindingIndex, new InputBinding { overridePath = priorOverride });
                        completed(null, $"Already bound to {conflict}; choose another control.");
                    }
                    else
                    {
                        BindingRevision++;
                        completed(path, null);
                    }
                    FinishCapture(op);
                })
                .OnCancel(op => { completed(null, null); FinishCapture(op); });
            _operation.Start();
        }

        private static string ResolveOverrideIds(string json)
        {
            var data = JsonUtility.FromJson<BindingOverridesJson>(json);
            if (data?.bindings == null) return json;
            foreach (var entry in data.bindings)
            {
                if (string.IsNullOrEmpty(entry.action) || string.IsNullOrEmpty(entry.path)) continue;
                int slash = entry.action.LastIndexOf('/');
                string name = slash >= 0 ? entry.action.Substring(slash + 1) : entry.action;
                if (!Actions.TryGetValue(name, out var action)) continue;
                string group = entry.path.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase)
                    ? GamepadGroup
                    : entry.path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase) ? MouseGroup : KeyboardGroup;
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

        private static string FindConflict(string actionName, string path, string group)
        {
            foreach (var pair in Actions)
            {
                if (pair.Key == actionName) continue;
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
            AddButton("Jump", "<Keyboard>/space", "<Gamepad>/rightShoulder");
            AddButton("Shield", "<Keyboard>/leftShift", "<Gamepad>/rightTrigger");
            AddButton("Grab", "<Keyboard>/c");
            AddButton("Down", "<Keyboard>/x", "<Gamepad>/leftTrigger");
            AddButton("Slot1", "<Keyboard>/1", "<Gamepad>/buttonSouth");
            AddButton("Slot2", "<Keyboard>/2", "<Gamepad>/buttonWest");
            AddButton("Slot3", "<Keyboard>/3", "<Gamepad>/buttonNorth");
            AddButton("Slot4", "<Keyboard>/4", "<Gamepad>/buttonEast");
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
            string group = keyboardOrMouse.StartsWith("<Mouse>", StringComparison.Ordinal) ? MouseGroup : KeyboardGroup;
            action.AddBinding(keyboardOrMouse).WithGroup(group);
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
