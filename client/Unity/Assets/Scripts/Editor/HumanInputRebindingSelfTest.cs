using System;
using System.Reflection;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools
{
    public static class HumanInputRebindingSelfTest
    {
        private const string Desktop = HumanInputActions.KeyboardMouseGroup;

        [MenuItem("Tools/SLOPArena/Tests/Mouse and Wheel Rebinding")]
        public static void Run()
        {
            Require(!Application.isPlaying && SettingsOverlay.Active?.IsOpen != true && !ChatInputGate.SuppressGameplay,
                "Run the rebinding regression with Play Mode stopped and input captures/menus closed.");
            Require(!HumanInputActions.IsCapturing, "Another binding capture is active.");
            string previousOverrides = HumanInputActions.SaveOverrides();
            bool previouslyEnabled = HumanInputActions.Get("Slot1").actionMap.enabled;
            bool previouslyGamepad = HumanInputActions.LastUsedGamepad;
            var previousMouse = Mouse.current;
            var previousKeyboard = Keyboard.current;
            var previousPad = Gamepad.current;
            var previousSettings = InputSystem.settings;
            var testSettings = UnityEngine.Object.Instantiate(previousSettings);
            var scene = EditorSceneManager.NewPreviewScene();
            var host = new GameObject("Input rebinding regression") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false); // Avoid Awake creating the preferences singleton or loading saved settings.
            SceneManager.MoveGameObjectToScene(host, scene);
            var input = host.AddComponent<InputController>();
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                InputSystem.settings = testSettings;
                testSettings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                // Edit Mode normally ignores action state changes. Use the Input System's
                // test mode on a temporary settings object, without changing project settings.
                testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
                HumanInputActions.Initialize("");
                HumanInputActions.ResetOverrides();
                Require(HumanInputActions.SaveOverrides() == "", "Default controls unexpectedly save overrides.");
                MouseButtonsCastAndHold(input, mouse, pad);
                WheelDirectionsCastAndRelease(input, mouse);
                MouseDefenseReachesSimulationInput(input, mouse);
                ConflictsAndCancellationPreserveBindings(mouse, keyboard);
                OverridesSurviveNewBindingIds();
                LegacyOverridesStillLoad();
                Debug.Log("[HumanInputRebindingSelfTest] Passed all five mouse buttons, normal/special holds, four wheel directions and repeated ticks, defense, conflicts, cancellation, saved/cleared bindings, and legacy preferences.");
            }
            finally
            {
                HumanInputActions.CancelCapture();
                HumanInputActions.ApplyOverrides(previousOverrides);
                if (!previouslyEnabled) HumanInputActions.Get("Slot1").actionMap.Disable();
                UnityEngine.Object.DestroyImmediate(host);
                EditorSceneManager.ClosePreviewScene(scene);
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(pad);
                testSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", false);
                InputSystem.settings = previousSettings;
                UnityEngine.Object.DestroyImmediate(testSettings);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (previousPad != null && previousPad.added) previousPad.MakeCurrent();
                typeof(HumanInputActions).GetProperty(nameof(HumanInputActions.LastUsedGamepad))!
                    .SetValue(null, previouslyGamepad);
            }
        }

        private static void MouseButtonsCastAndHold(InputController input, Mouse mouse, Gamepad pad)
        {
            HumanInputActions.ClearBinding("FaceToCamera", Desktop);
            HumanInputActions.ClearBinding("ToggleLock", Desktop);
            var cases = new[]
            {
                ("Slot1", MouseButton.Left, "leftButton", AbilitySlots.Slot1),
                ("Slot2", MouseButton.Right, "rightButton", AbilitySlots.Slot2),
                ("Slot3", MouseButton.Middle, "middleButton", AbilitySlots.Slot3),
                ("Slot4", MouseButton.Forward, "forwardButton", AbilitySlots.Slot4),
                ("SlotA", MouseButton.Back, "backButton", AbilitySlots.A),
            };
            foreach (var (action, button, control, slot) in cases)
            {
                CaptureMouse(action, mouse, new MouseState().WithButton(button), "<Mouse>/" + control);
                SendMouse(mouse, default);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.LeftShoulder));
                SendMouse(mouse, new MouseState().WithButton(button));
                input.Poll();
                Require(input.ConsumePendingSlotPress() == slot, action + " did not cast its canonical slot from the mouse.");
                Require(input.IsSlotKeyHeld((byte)(slot - 1)), action + " lost its mouse hold.");
                SendMouse(mouse, new MouseState().WithButton(button));
                input.Poll();
                Require(input.ConsumePendingSlotPress() == 0, action + " repeated while held.");
                SendMouse(mouse, default);
                input.Poll();
                Require(!input.IsSlotKeyHeld((byte)(slot - 1)), action + " stayed held after mouse release.");
                InputSystem.QueueStateEvent(pad, new GamepadState());
                InputSystem.Update();
            }
        }

        private static void WheelDirectionsCastAndRelease(InputController input, Mouse mouse)
        {
            HumanInputActions.ResetOverrides();
            var cases = new[]
            {
                ("Slot1", Vector2.up, "up", AbilitySlots.Slot1),
                ("SlotE", Vector2.down, "down", AbilitySlots.E),
                ("SlotR", Vector2.left, "left", AbilitySlots.R),
                ("SlotF", Vector2.right, "right", AbilitySlots.F),
            };
            foreach (var (action, direction, control, slot) in cases)
            {
                CaptureMouse(action, mouse, new MouseState { scroll = direction * 120f }, "<Mouse>/scroll/" + control);
                SendMouse(mouse, default);
                for (int tick = 0; tick < 2; tick++)
                {
                    SendMouse(mouse, new MouseState { scroll = direction * 120f });
                    input.Poll();
                    Require(input.ConsumePendingSlotPress() == slot, control + " wheel did not cast its own slot.");
                    Require(input.IsSlotKeyHeld((byte)(slot - 1)), "Wheel pulse was not held for its input frame.");
                    Require(HumanInputActions.ZoomDelta == 0f, "A bound wheel direction also zoomed the camera.");
                    // Delta controls reset at the next update without a release event.
                    InputSystem.Update();
                    input.Poll();
                    Require(input.ConsumePendingSlotPress() == 0 && !input.IsSlotKeyHeld((byte)(slot - 1)),
                        "Wheel pulse repeated or stayed held after its automatic reset.");
                }
            }
            HumanInputActions.ClearBinding("SlotE", Desktop);
            SendMouse(mouse, new MouseState { scroll = Vector2.down * 120f });
            Require(HumanInputActions.ZoomDelta < 0f, "An unassigned wheel direction no longer zooms.");
            input.Poll();
            Require(input.ConsumePendingSlotPress() == 0, "The opposite wheel direction triggered another slot.");
            SendMouse(mouse, default);
        }

        private static void MouseDefenseReachesSimulationInput(InputController input, Mouse mouse)
        {
            HumanInputActions.ResetOverrides();
            HumanInputActions.ClearBinding("ToggleLock", Desktop);
            CaptureMouse("Shield", mouse, new MouseState().WithButton(MouseButton.Right), "<Mouse>/rightButton");
            SendMouse(mouse, default);
            SendMouse(mouse, new MouseState().WithButton(MouseButton.Right));
            input.Poll();
            var state = input.BuildInputState(null, 0f, false, 0, default, null).input;
            Require(state.ShieldPressed && state.ShieldHeld, "Mouse shield did not reach the simulation input.");
            SendMouse(mouse, new MouseState().WithButton(MouseButton.Right));
            input.Poll();
            state = input.BuildInputState(null, 0f, false, 0, default, null).input;
            Require(!state.ShieldPressed && state.ShieldHeld, "Held mouse shield repeated its edge or lost its hold.");
            SendMouse(mouse, default);
            input.Poll();
            Require(!input.BuildInputState(null, 0f, false, 0, default, null).input.ShieldHeld,
                "Mouse shield stayed held after release.");
        }

        private static void ConflictsAndCancellationPreserveBindings(Mouse mouse, Keyboard keyboard)
        {
            HumanInputActions.ResetOverrides();
            string error = null;
            HumanInputActions.Rebind("Slot1", Desktop, (_, failure) => error = failure);
            SendMouse(mouse, new MouseState().WithButton(MouseButton.Left));
            Require(!HumanInputActions.IsCapturing && error?.Contains("FaceToCamera") == true,
                "An occupied mouse control was silently assigned twice.");
            Require(Path("Slot1", Desktop) == "<Keyboard>/1", "A conflicting capture lost the prior binding.");
            SendMouse(mouse, default);

            bool cancelled = false;
            HumanInputActions.Rebind("Slot1", Desktop, (path, failure) => cancelled = path == null && failure == null);
            SendMouse(mouse, new MouseState { delta = new Vector2(100f, 40f), position = new Vector2(300f, 400f) });
            Require(HumanInputActions.IsCapturing, "Pointer motion was captured as an action button.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            Require(cancelled && !HumanInputActions.IsCapturing && Path("Slot1", Desktop) == "<Keyboard>/1",
                "Escape did not cancel capture while preserving the prior binding.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();

            HumanInputActions.Rebind("Slot1", Desktop, (path, failure) =>
                Require(failure == null && path == "<Keyboard>/j", "Keyboard capture failed in the desktop category."));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.J));
            InputSystem.Update();
            Require(!HumanInputActions.IsCapturing && Path("Slot1", Desktop) == "<Keyboard>/j",
                "Keyboard capture failed in the desktop category.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
        }

        private static void OverridesSurviveNewBindingIds()
        {
            HumanInputActions.Get("Slot1").ApplyBindingOverride(HumanInputActions.BindingIndex("Slot1", Desktop), "<Mouse>/leftButton");
            HumanInputActions.Get("SlotE").ApplyBindingOverride(HumanInputActions.BindingIndex("SlotE", Desktop), "<Mouse>/scroll/down");
            HumanInputActions.ClearBinding("FaceToCamera", Desktop);
            HumanInputActions.ClearBinding("Jump", HumanInputActions.GamepadGroup);
            string saved = HumanInputActions.SaveOverrides();
            // Simulate a fresh process, whose action map will assign new runtime GUIDs.
            foreach (string name in HumanInputActions.RebindableActions)
                foreach (var binding in HumanInputActions.Get(name).bindings)
                    saved = saved.Replace(binding.id.ToString(), Guid.NewGuid().ToString());
            HumanInputActions.ResetOverrides();
            HumanInputActions.ApplyOverrides(saved);
            Require(Path("Slot1", Desktop) == "<Mouse>/leftButton" && Path("SlotE", Desktop) == "<Mouse>/scroll/down",
                "Mouse or wheel bindings were lost across binding ID changes.");
            Require(Path("FaceToCamera", Desktop) == "" && Path("Jump", HumanInputActions.GamepadGroup) == "",
                "A cleared desktop/controller binding returned after loading preferences.");
            Require(HumanInputActions.BindingLabel("Jump", HumanInputActions.GamepadGroup) == "Unbound",
                "A cleared binding does not have a readable label.");
        }

        private static void LegacyOverridesStillLoad()
        {
            HumanInputActions.ApplyOverrides("{\"bindings\":[" +
                "{\"action\":\"Gameplay/Jump\",\"id\":\"old-keyboard-id\",\"path\":\"<Keyboard>/j\"}," +
                "{\"action\":\"Gameplay/FaceToCamera\",\"id\":\"old-mouse-id\",\"path\":\"<Mouse>/middleButton\"}," +
                "{\"action\":\"Gameplay/Slot1\",\"id\":\"old-gamepad-id\",\"path\":\"<Gamepad>/dpad/up\"}]}");
            Require(Path("Jump", Desktop) == "<Keyboard>/j" && Path("FaceToCamera", Desktop) == "<Mouse>/middleButton" &&
                Path("Slot1", HumanInputActions.GamepadGroup) == "<Gamepad>/dpad/up",
                "Existing keyboard, mouse utility or controller preferences stopped loading.");
            HumanInputActions.ResetOverrides();
        }

        private static void CaptureMouse(string action, Mouse mouse, MouseState state, string expectedPath)
        {
            SendMouse(mouse, default);
            string captured = null;
            string error = null;
            HumanInputActions.Rebind(action, Desktop, (path, failure) => { captured = path; error = failure; });
            SendMouse(mouse, state);
            Require(!HumanInputActions.IsCapturing && captured == expectedPath && error == null,
                $"{action} captured '{captured}' instead of '{expectedPath}': {error}");
        }

        private static void SendMouse(Mouse mouse, MouseState state)
        {
            InputSystem.QueueStateEvent(mouse, state);
            InputSystem.Update();
        }

        private static string Path(string action, string group) =>
            HumanInputActions.Get(action).bindings[HumanInputActions.BindingIndex(action, group)].effectivePath;

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
