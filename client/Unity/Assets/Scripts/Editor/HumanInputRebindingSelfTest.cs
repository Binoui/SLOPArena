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
            bool previousStickSpecials = HumanInputActions.RightStickSpecials;
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
                // The older cases predate right-stick specials; LB is their special modifier.
                HumanInputActions.SetRightStickSpecials(false);
                MouseButtonsCastAndHold(input, mouse, pad);
                WheelDirectionsCastAndRelease(input, mouse);
                MouseDefenseReachesSimulationInput(input, mouse);
                ControllerLayoutJumpsDashesAndCastsSpecialA(input, pad);
                RightStickFlicksSpecialsAndLbGrabs(input, pad);
                ConflictsAndCancellationPreserveBindings(mouse, keyboard);
                OverridesSurviveNewBindingIds();
                LegacyOverridesStillLoad();
                Debug.Log("[HumanInputRebindingSelfTest] Passed all five mouse buttons, normal/special holds, four wheel directions and repeated ticks, defense, the controller layout (A jump, LB+A special, RB dash, X/Y/B normals), right-stick specials with LB grab, conflicts, cancellation, saved/cleared bindings, and legacy preferences.");
            }
            finally
            {
                HumanInputActions.CancelCapture();
                HumanInputActions.ApplyOverrides(previousOverrides);
                HumanInputActions.SetRightStickSpecials(previousStickSpecials);
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
            var state = input.BuildInputState(null, 0f, 0, default, null).input;
            Require(state.ShieldPressed && state.ShieldHeld, "Mouse shield did not reach the simulation input.");
            SendMouse(mouse, new MouseState().WithButton(MouseButton.Right));
            input.Poll();
            state = input.BuildInputState(null, 0f, 0, default, null).input;
            Require(!state.ShieldPressed && state.ShieldHeld, "Held mouse shield repeated its edge or lost its hold.");
            SendMouse(mouse, default);
            input.Poll();
            Require(!input.BuildInputState(null, 0f, 0, default, null).input.ShieldHeld,
                "Mouse shield stayed held after release.");
        }

        private static SlopArena.Shared.InputState PressPad(InputController input, Gamepad pad, GamepadState state)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState());
            InputSystem.Update();
            input.Poll();
            input.ClearPendingFrameState();
            InputSystem.QueueStateEvent(pad, state);
            InputSystem.Update();
            input.Poll();
            return input.BuildInputState(null, 0f, input.ConsumePendingSlotPress(), default, null).input;
        }

        /// <summary>
        /// ADR-0042 with the right stick on the camera: A jumps, LB + A casts special A without
        /// jumping, RB dashes, X/Y/B are the normals.
        /// </summary>
        private static void ControllerLayoutJumpsDashesAndCastsSpecialA(InputController input, Gamepad pad)
        {
            HumanInputActions.ResetOverrides();
            HumanInputActions.SetRightStickSpecials(false);
            SlopArena.Shared.InputState Press(GamepadState state) => PressPad(input, pad, state);

            var jump = Press(new GamepadState().WithButton(GamepadButton.South));
            Require(jump.Jump && jump.JumpHeld && jump.ActiveSlot == 0, "A did not jump on its own.");
            var special = Press(new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.South));
            Require(!special.Jump && !special.JumpHeld && special.ActiveSlot == AbilitySlots.A,
                "LB + A did not cast special A without a jump.");
            var dash = Press(new GamepadState().WithButton(GamepadButton.RightShoulder));
            Require(dash.DashPressed && !dash.Jump && dash.ActiveSlot == 0, "RB did not press the homing dash.");
            Require(Press(new GamepadState().WithButton(GamepadButton.West)).ActiveSlot == AbilitySlots.Slot1, "X is not normal 1.");
            Require(Press(new GamepadState().WithButton(GamepadButton.North)).ActiveSlot == AbilitySlots.Slot3, "Y is not normal 2 (slot 3).");
            Require(Press(new GamepadState().WithButton(GamepadButton.East)).ActiveSlot == AbilitySlots.Slot4, "B is not normal 3 (slot 4).");
            Require(Press(new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.West)).ActiveSlot == AbilitySlots.R,
                "LB + X is not special R.");
            Require(!Press(new GamepadState().WithButton(GamepadButton.LeftShoulder)).GrabPressed,
                "LB alone grabbed while it is the special modifier.");
            Require(Press(new GamepadState { rightStick = Vector2.up }).ActiveSlot == 0,
                "The camera stick cast a special.");
            InputSystem.QueueStateEvent(pad, new GamepadState());
            InputSystem.Update();
            input.ClearPendingFrameState();
        }

        /// <summary>
        /// Right-stick specials (default): a flick casts the special in that direction and holds
        /// it until the stick returns to centre; LB grabs; LB + A is a jump, not a special.
        /// </summary>
        private static void RightStickFlicksSpecialsAndLbGrabs(InputController input, Gamepad pad)
        {
            HumanInputActions.ResetOverrides();
            HumanInputActions.SetRightStickSpecials(true);
            SlopArena.Shared.InputState Press(GamepadState state) => PressPad(input, pad, state);
            void Hold(GamepadState state)
            {
                InputSystem.QueueStateEvent(pad, state);
                InputSystem.Update();
                input.Poll();
            }

            Require(Press(new GamepadState { rightStick = Vector2.up }).ActiveSlot == AbilitySlots.E, "Right stick up is not special E.");
            Require(input.IsSlotKeyHeld((byte)(AbilitySlots.E - 1)), "A held flick does not hold special E for aiming.");
            Hold(new GamepadState { rightStick = Vector2.right });
            Require(input.ConsumePendingSlotPress() == 0, "Rolling the held stick cast a second special.");
            Hold(new GamepadState { rightStick = new Vector2(0.2f, 0f) });
            Require(!input.IsSlotKeyHeld((byte)(AbilitySlots.E - 1)), "Centring the stick did not release special E.");
            Hold(new GamepadState { rightStick = Vector2.right });
            Require(input.ConsumePendingSlotPress() == AbilitySlots.F, "Right stick right is not special F after centring.");
            Require(Press(new GamepadState { rightStick = Vector2.left }).ActiveSlot == AbilitySlots.R, "Right stick left is not special R.");
            Require(Press(new GamepadState { rightStick = Vector2.down }).ActiveSlot == AbilitySlots.A, "Right stick down is not special A.");
            Require(Press(new GamepadState { rightStick = new Vector2(0.5f, 0f) }).ActiveSlot == 0,
                "A light right-stick tilt cast a special.");
            Require(input.GetAimStick(true) == Vector2.zero, "The special stick also aimed the special.");

            var grab = Press(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            Require(grab.GrabPressed && grab.ActiveSlot == 0, "LB did not grab.");
            var jump = Press(new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.South));
            Require(jump.Jump && jump.ActiveSlot == 0, "LB + A cast a special instead of jumping.");
            Require(Press(new GamepadState().WithButton(GamepadButton.West)).ActiveSlot == AbilitySlots.Slot1, "X is not normal 1.");
            InputSystem.QueueStateEvent(pad, new GamepadState());
            InputSystem.Update();
            input.ClearPendingFrameState();
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
            HumanInputActions.Get("Walk").ApplyBindingOverride(HumanInputActions.BindingIndex("Walk", Desktop), "<Keyboard>/v");
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
            Require(Path("Walk", Desktop) == "<Keyboard>/v", "Walk binding was lost across binding ID changes.");
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
