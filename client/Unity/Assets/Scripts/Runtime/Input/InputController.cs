#nullable enable
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Camera;
using SlopArena.Client.UI;
using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SlopArena.Client.Input
{
    /// <summary>Latched logical defense and grab edges between input polls and simulation ticks.</summary>
    internal struct DefenseInputEdges
    {
        private bool _grabChordArmed;
        private bool _shieldReleaseRequired;
        private bool _controllerChordHeld;
        private bool _pendingShieldPressed;
        private bool _pendingGrabPressed;

        public DefenseInputEdges(bool grabChordArmed)
        {
            _grabChordArmed = grabChordArmed;
            _shieldReleaseRequired = false;
            _controllerChordHeld = false;
            _pendingShieldPressed = false;
            _pendingGrabPressed = false;
        }

        public void Update(bool shieldHeld, bool rtHeld, bool rtPressed, bool modifierHeld,
            bool modifierPressed, bool keyboardShieldPressed, bool directGrabPressed)
        {
            _controllerChordHeld = modifierHeld && rtHeld;
            bool chordComplete = _grabChordArmed && _controllerChordHeld && (modifierPressed || rtPressed);
            if (chordComplete)
            {
                _pendingGrabPressed = true;
                _pendingShieldPressed = false;
                _grabChordArmed = false;
                _shieldReleaseRequired = true;
            }
            else if (!modifierHeld || !rtHeld)
            {
                _grabChordArmed = true;
            }

            if (_shieldReleaseRequired && !shieldHeld)
                _shieldReleaseRequired = false;

            if (directGrabPressed)
            {
                _pendingGrabPressed = true;
                _pendingShieldPressed = false;
                if (shieldHeld) _shieldReleaseRequired = true;
            }

            bool defenseConsumed = directGrabPressed || chordComplete || _shieldReleaseRequired || _controllerChordHeld;
            if (!defenseConsumed && (rtPressed || keyboardShieldPressed))
                _pendingShieldPressed = true;
        }

        public bool ShieldHeld(bool rawShieldHeld) =>
            rawShieldHeld && !_shieldReleaseRequired && !_controllerChordHeld;

        public bool TakeShieldPressed()
        {
            bool value = _pendingShieldPressed;
            _pendingShieldPressed = false;
            return value;
        }

        public bool TakeGrabPressed()
        {
            bool value = _pendingGrabPressed;
            _pendingGrabPressed = false;
            return value;
        }

        public void ClearPending(bool shieldHeld)
        {
            _pendingShieldPressed = false;
            _pendingGrabPressed = false;
            if (shieldHeld) _shieldReleaseRequired = true;
        }

        public static byte ResolveFaceSlot(bool gamepad, bool modifierHeld, byte normal, byte special) =>
            gamepad && modifierHeld ? special : normal;
    }

    /// <summary>
    /// Centralized human input adapter. Polls the shared Input System action map once
    /// per frame, buffers discrete edges, and builds the existing InputState contract.
    /// </summary>
    public class InputController : MonoBehaviour
    {
        private void Awake()
        {
            var settings = ClientSettingsService.Instance;
            HumanInputActions.Initialize(settings.BindingOverrides);
        }

        // Couch play: a second Local Player reads its own device-restricted copy of the map.
        // NonSerialized: a Play Mode script reload would otherwise turn null into an empty map.
        [NonSerialized] private InputActionMap? _playerMap;
        public void UsePlayerMap(InputActionMap map) => _playerMap = map;
        private InputAction Act(string name) =>
            _playerMap != null ? _playerMap.FindAction(name, true) : HumanInputActions.Get(name);

        // ── Frame state (set by Poll) ──
        /// <summary>Pending jump: set by Poll, consumed by BuildInputState.</summary>
        private bool _pendingJump;
        /// <summary>Pending homing dash press (ADR-0042), consumed once by BuildInputState.</summary>
        private bool _pendingDash;
        private DefenseInputEdges _defenseInputEdges = new DefenseInputEdges(true);
        /// <summary>Pending dedicated Down press edge, consumed once by BuildInputState.</summary>
        private bool _pendingDownPressed;
        /// <summary>
        /// Prevent a key held through chat/pause/focus suppression from creating a new Down edge
        /// when gameplay input resumes. Cleared only after the bound key is released.
        /// </summary>
        private bool _downReleaseRequired;
        /// <summary>Pending LMB facing snap (ADR-0017, issue #126): set by Poll on the LMB
        /// press edge, consumed by BuildInputState. One tick of FaceToCamera.</summary>
        private bool _pendingFaceToCamera;
        /// <summary>Pending RMB target-lock toggle (ADR-0018, issue #127): set by Poll on
        /// the RMB press edge, consumed by BuildInputState. One tick of ToggleLock.</summary>
        private bool _pendingToggleLock;
        private bool _pendingRetarget;
        /// <summary>True if the action assigned to the canonical slot index is held.</summary>
        public bool IsSlotKeyHeld(byte slotIdx)
        {
            if (HumanInputBlocked) return false;
            string action = SlotAction(slotIdx);
            if (action != null)
                foreach (var control in Act(action).controls)
                    if (control.device is Keyboard or Mouse && control.IsPressed())
                        return true;
            foreach (byte heldSlot in _controllerSlots)
                if (heldSlot == slotIdx + 1) return true;
            return _stickSlot != 0 && _stickSlot == slotIdx + 1;
        }

        private static string SlotAction(byte slotIdx) => slotIdx switch
        {
            2 => "Slot1", 3 => "SlotE", 4 => "SlotR", 5 => "SlotF",
            7 => "Slot3", 8 => "Slot4", 10 => "SlotA",
            _ => null,
        };

        private static bool HumanInputSuppressed =>
            ChatInputGate.SuppressGameplay || SettingsOverlay.Active is { IsOpen: true } ||
            SettingsOverlay.ClosedThisFrame || HumanInputActions.IsCapturing ||
            MatchPauseMenu.Active is { IsPaused: true };

        private bool _humanReleaseRequired;
        // Face-button slots are fixed at press time; changing the modifier never changes a hold.
        private readonly byte[] _controllerSlots = new byte[4];
        private readonly ButtonControl?[] _controllerButtons = new ButtonControl?[4];
        // Right-stick specials: the slot a flick picked stays held until the stick returns to centre.
        private const float StickSpecialPress = 0.6f;
        private const float StickSpecialRelease = 0.35f;
        private byte _stickSlot;
        private bool _stickNeedsCentre;
        private static bool StickSpecials => HumanInputActions.RightStickSpecials;
        private bool HumanInputBlocked => HumanInputSuppressed || _humanReleaseRequired;

        public void RequireReleaseBeforeHumanInput()
        {
            _humanReleaseRequired = true;
            ClearPendingFrameState();
        }

        private bool AnyHumanControlHeld()
        {
            foreach (var name in HumanInputActions.RebindableActions)
                if (Act(name).IsPressed()) return true;
            return Act("MoveStick").ReadValue<Vector2>().sqrMagnitude > 0.01f
                || Act("StickLook").ReadValue<Vector2>().sqrMagnitude > 0.01f
                || Act("MouseLook").ReadValue<Vector2>().sqrMagnitude > 0.01f
                || Mathf.Abs(HumanInputActions.ZoomDelta) > 0.01f;
        }

        /// <summary>
        /// True while the bound fast-fall key is held. Kept separate from InputState.Down so
        /// release suppression can be enforced across pause/focus transitions.
        /// </summary>
        private bool IsDownHeld() => Act("Down").IsPressed();


        private void OnApplicationFocus(bool hasFocus)
        {
            ClearPendingFrameState();
            // InputSystem can reset device state while unfocused. Re-arm on both
            // loss and regain so background polling cannot release this guard.
            _downReleaseRequired = true;
        }

        private void OnApplicationPause(bool paused)
        {
            ClearPendingFrameState();
            _downReleaseRequired = true;
        }

        private void OnDisable()
        {
            ClearPendingFrameState();
            _downReleaseRequired = true;
        }

        // ── Slot press (set by Poll, consumed via ConsumePendingSlotPress) ──
        private byte _pendingSlotPress;

        // ════════════════════════════════════════════════════════════════
        //  Polling
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Read current input and store into frame state.
        /// Call once per frame before BuildInputState() or any property access.
        ///
        /// ActiveSlot retains existing wire values: 1→3, E→4, R→5, F→6, 2→7,
        /// 3→8, 4→9, A→11. Physical controls follow the player's bindings.
        /// Consume via <see cref="ConsumePendingSlotPress"/> after BuildInputState.
        /// </summary>
        public void Poll()
        {
            if (HumanInputSuppressed)
            {
                ClearPendingFrameState();
                return;
            }
            if (_humanReleaseRequired)
            {
                if (AnyHumanControlHeld()) return;
                _humanReleaseRequired = false;
            }

            var jump = Act("Jump");
            PollDefense();
            var down = Act("Down");
            if (_downReleaseRequired)
            {
                if (!down.IsPressed()) _downReleaseRequired = false;
            }
            else if (down.WasPressedThisFrame()) _pendingDownPressed = true;

            if (Act("FaceToCamera").WasPressedThisFrame())
                _pendingFaceToCamera = true;
            // An R3 click during a special flick is part of the flick, not a lock toggle.
            if (Act("ToggleLock").WasPerformedThisFrame() && _stickSlot == 0)
                _pendingToggleLock = true;
            if (Act("Retarget").WasPressedThisFrame())
                _pendingRetarget = true;
            if (Act("Dash").WasPressedThisFrame())
                _pendingDash = true;
            bool jumpBecameSpecial = PollSlots();
            if (jump.WasPressedThisFrame() && !jumpBecameSpecial) _pendingJump = true;
        }

        private static readonly (string action, byte slot)[] SlotActions =
        {
            ("Slot1", AbilitySlots.Slot1), ("SlotE", AbilitySlots.E), ("SlotR", AbilitySlots.R),
            ("SlotF", AbilitySlots.F), ("Slot3", AbilitySlots.Slot3),
            ("Slot4", AbilitySlots.Slot4), ("SlotA", AbilitySlots.A),
        };

        /// <summary>Returns true when the controller jump button was pressed as LB + A this frame.</summary>
        private bool PollSlots()
        {
            bool jumpBecameSpecial = false;
            bool modifier = !StickSpecials && Act("SpecialModifier").IsPressed();
            for (int face = 0; face < 4; face++)
            {
                var held = _controllerButtons[face];
                if (held != null && !held.isPressed)
                {
                    _controllerButtons[face] = null;
                    _controllerSlots[face] = 0;
                }
                var action = Act(FaceActions[face]);
                foreach (var control in action.controls)
                {
                    if (control.device is Gamepad && control is ButtonControl button &&
                        button.wasPressedThisFrame && _controllerButtons[face] == null)
                    {
                        // A alone jumps; only LB + A turns the jump button into special A.
                        if (NormalSlots[face] == AbilitySlots.None && !modifier) continue;
                        jumpBecameSpecial |= NormalSlots[face] == AbilitySlots.None;
                        _controllerButtons[face] = button;
                        _controllerSlots[face] = DefenseInputEdges.ResolveFaceSlot(
                            true, modifier, NormalSlots[face], SpecialSlots[face]);
                        if (_pendingSlotPress == 0) _pendingSlotPress = _controllerSlots[face];
                    }
                    else if (control.device is Keyboard or Mouse && NormalSlots[face] != AbilitySlots.None &&
                        WasControlPressedThisFrame(control, action) && _pendingSlotPress == 0)
                        _pendingSlotPress = DefenseInputEdges.ResolveFaceSlot(
                            false, false, NormalSlots[face], SpecialSlots[face]);
                }
            }
            foreach (var (actionName, slot) in SlotActions)
            {
                if (actionName == "Slot1" || actionName == "Slot3" || actionName == "Slot4") continue;
                if (Act(actionName).WasPressedThisFrame() && _pendingSlotPress == 0)
                    _pendingSlotPress = slot;
            }
            if (StickSpecials) PollStickSpecial();
            else _stickSlot = 0;
            return jumpBecameSpecial;
        }

        /// <summary>
        /// A right-stick flick past 60% presses the special in that direction. Holding the stick
        /// holds the slot for aiming and charging; a new special needs the stick back near centre.
        /// </summary>
        private void PollStickSpecial()
        {
            Vector2 stick = Act("StickLook").ReadValue<Vector2>();
            float magnitude = stick.magnitude;
            if (magnitude < StickSpecialRelease)
            {
                _stickSlot = 0;
                _stickNeedsCentre = false;
                return;
            }
            if (_stickSlot != 0 || _stickNeedsCentre || magnitude < StickSpecialPress) return;
            _stickSlot = StickSpecialSlot(stick);
            if (_pendingSlotPress == 0) _pendingSlotPress = _stickSlot;
        }

        /// <summary>The face diamond on the stick: up E (Y), left R (X), right F (B), down A (A).</summary>
        public static byte StickSpecialSlot(Vector2 stick) =>
            Mathf.Abs(stick.x) > Mathf.Abs(stick.y)
                ? stick.x > 0f ? AbilitySlots.F : AbilitySlots.R
                : stick.y > 0f ? AbilitySlots.E : AbilitySlots.A;
        private void PollDefense()
        {
            var shield = Act("Shield");
            var modifier = Act("SpecialModifier");
            bool stickSpecials = StickSpecials;
            bool rtHeld = false;
            bool rtPressed = false;
            bool desktopShieldPressed = false;
            foreach (var control in shield.controls)
            {
                if (control.device is Gamepad && control is ButtonControl button)
                {
                    rtHeld |= button.isPressed;
                    rtPressed |= button.wasPressedThisFrame;
                }
                else if (control.device is Keyboard or Mouse)
                    desktopShieldPressed |= WasControlPressedThisFrame(control, shield);
            }
            // Grab's LB binding is the special modifier unless the right stick holds the specials.
            bool grabPressed = false;
            var grab = Act("Grab");
            foreach (var control in grab.controls)
                if (control.device is Gamepad && control is ButtonControl button)
                    grabPressed |= stickSpecials && button.wasPressedThisFrame;
                else if (control.device is Keyboard or Mouse)
                    grabPressed |= WasControlPressedThisFrame(control, grab);
            _defenseInputEdges.Update(
                shield.IsPressed(), rtHeld, rtPressed, !stickSpecials && modifier.IsPressed(),
                !stickSpecials && modifier.WasPressedThisFrame(), desktopShieldPressed, grabPressed);
        }

        private static bool WasControlPressedThisFrame(InputControl control, InputAction action) =>
            control is ButtonControl button ? button.wasPressedThisFrame
                : control.IsPressed() && action.WasPressedThisFrame();



        // Face buttons A, X, Y, B by default. A is the jump button, so it has no normal.
        private static readonly string[] FaceActions = { "Jump", "Slot1", "Slot3", "Slot4" };
        private static readonly byte[] NormalSlots = { AbilitySlots.None, AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4 };
        private static readonly byte[] SpecialSlots = { AbilitySlots.A, AbilitySlots.R, AbilitySlots.E, AbilitySlots.F };

        /// <summary>
        /// Discard buffered jump/slot presses without consuming them. Called
        /// when pausing so stale presses don't fire on the first frame after
        /// resume (issue #77).
        /// </summary>
        public void ClearPendingFrameState()
        {
            _pendingJump = false;
            _pendingDash = false;
            _pendingDownPressed = false;
            _pendingFaceToCamera = false;
            _pendingToggleLock = false;
            _pendingRetarget = false;
            _defenseInputEdges.ClearPending(Act("Shield").IsPressed());
            _pendingSlotPress = 0;
            Array.Clear(_controllerSlots, 0, _controllerSlots.Length);
            Array.Clear(_controllerButtons, 0, _controllerButtons.Length);
            _stickNeedsCentre |= _stickSlot != 0;
            _stickSlot = 0;
            if (IsDownHeld())
                _downReleaseRequired = true;
        }

        public byte ConsumePendingSlotPress()
        {
            if (HumanInputBlocked)
            {
                _pendingSlotPress = 0;
                return 0;
            }

            byte slot = _pendingSlotPress;
            if (slot > 0) {
                Debug.Log($"[Input] ConsumePendingSlotPress: {slot}");
                _pendingSlotPress = 0;
            }
            return slot;
        }

        // ════════════════════════════════════════════════════════════════
        //  Movement
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get raw movement input for current frame.
        /// </summary>
        public Vector2 GetMovement()
        {
            if (HumanInputBlocked) return Vector2.zero;
            float x = (Act("MoveRight").IsPressed() ? 1f : 0f)
                    - (Act("MoveLeft").IsPressed() ? 1f : 0f);
            float y = (Act("MoveUp").IsPressed() ? 1f : 0f)
                    - (Act("MoveDown").IsPressed() ? 1f : 0f);
            Vector2 stick = Act("MoveStick").ReadValue<Vector2>();
            Vector2 keyboard = new Vector2(x, y);
            if (keyboard.sqrMagnitude > 0f) return keyboard.normalized;
            return Vector2.ClampMagnitude(keyboard + stick, 1f);
        }

        /// <summary>
        /// Stationary aim uses the left stick. The right stick is a fallback only while it is the
        /// camera stick; with right-stick specials it is holding the special.
        /// </summary>
        public Vector2 GetAimStick(bool locksMovement)
        {
            if (HumanInputBlocked) return Vector2.zero;
            Vector2 left = Act("MoveStick").ReadValue<Vector2>();
            if (locksMovement && left.sqrMagnitude > 0.01f) return left;
            return StickSpecials ? Vector2.zero : Act("StickLook").ReadValue<Vector2>();
        }

        // ════════════════════════════════════════════════════════════════
        //  BuildInputState
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a full InputState for one frame, including camera-relative direction math,
        /// 8-direction snap, and FSM movement gate.
        /// Returns (InputState, world-space moveDirection, camera-relative snappedInputDirection).
        /// </summary>
        private Quaternion? _heldMoveBasis;
        private float _heldStickAngle;

        public (InputState input, Vector3 moveDirection, Vector2 snappedInputDirection) BuildInputState(
            Camera.CameraMount? camera,
            float bodyYawDeg,
            byte pendingSlotPress,
            Camera.AimContext aimCtx,
            Func<bool>? canMove,
            byte targetEntityId = 0,
            bool aimFromFacing = false)
        {
            var input = new InputState();

            input.LockMode = ClientSettingsService.Instance.AutoLockMode;
            if (HumanInputBlocked)
            {
                ClearPendingFrameState();
                input.TargetEntityId = targetEntityId;
                return (input, Vector3.zero, Vector2.zero);
            }

            // ── Player path: camera-relative 8-direction input ──
            Vector3 camForward = Vector3.forward;
            Vector3 camRight = Vector3.right;
            if (camera != null)
            {
                camForward = camera.GetForwardDirection();
                camRight = camera.GetRightDirection();
            }

            // Build raw camera-relative direction from unified digital and stick input.
            bool stickSnap = ClientSettingsService.Instance.Stick8WaySnap;
            Vector2 moveInput = GetMovement();
            // Steady camera (ADR-0043): keep the basis a held direction started with, so a camera
            // turn does not bend a run already underway. A new direction re-reads the camera.
            if (camera != null && camera.HoldsMoveDirection && moveInput.sqrMagnitude >= 0.04f)
            {
                float stickAngle = Mathf.Atan2(moveInput.x, moveInput.y) * Mathf.Rad2Deg;
                if (!_heldMoveBasis.HasValue || Mathf.Abs(Mathf.DeltaAngle(stickAngle, _heldStickAngle)) > 40f)
                {
                    _heldMoveBasis = Quaternion.LookRotation(camForward, Vector3.up);
                    _heldStickAngle = stickAngle;
                }
                camForward = _heldMoveBasis.Value * Vector3.forward;
                camRight = _heldMoveBasis.Value * Vector3.right;
            }
            else
                _heldMoveBasis = null;
            // Steady does not face the target, so camera-aimed moves use facing, as in couch play.
            aimFromFacing |= camera != null && camera.Style == Camera.CameraStyle.Steady;
            bool keyboardMovement = Act("MoveRight").IsPressed()
                || Act("MoveLeft").IsPressed()
                || Act("MoveUp").IsPressed()
                || Act("MoveDown").IsPressed();
            Vector3 rawDir = camForward * moveInput.y + camRight * moveInput.x;

            Vector3 moveDirection = Vector3.zero;
            Vector2 snappedInputDirection = Vector2.zero;

            if (rawDir.sqrMagnitude > 0.001f)
            {
                // Convert to camera-relative 2D coordinates
                float rawForward = Vector3.Dot(rawDir, camForward);
                float rawRight = Vector3.Dot(rawDir, camRight);

                // Snap to 8 directions (45-degree increments)
                float angle = MathF.Atan2(rawRight, rawForward);
                const float snapStep = MathF.PI / 4f;
                float snappedAngle = stickSnap || keyboardMovement
                    ? MathF.Round(angle / snapStep) * snapStep : angle;

                float fwd = MathF.Cos(snappedAngle);
                float rgt = MathF.Sin(snappedAngle);

                snappedInputDirection = new Vector2(rgt, fwd);
                moveDirection = (camForward * fwd) + (camRight * rgt);
                moveDirection = moveDirection.normalized;
            }

            input.MoveX = moveDirection.x;
            input.MoveY = moveDirection.z;
            input.Up = moveDirection.z > 0.3f;
            // Dedicated Down remains independent from backward movement.
            input.Down = !_downReleaseRequired && Act("Down").IsPressed();
            input.DownPressed = _pendingDownPressed;
            _pendingDownPressed = false;
            input.Left = moveDirection.x < -0.3f;
            input.Right = moveDirection.x > 0.3f;
            // LB + A holds special A, not a jump.
            input.JumpHeld = Act("Jump").IsPressed() && _controllerSlots[0] == AbilitySlots.None;
            var shieldAction = Act("Shield");
            input.ShieldHeld = _defenseInputEdges.ShieldHeld(shieldAction.IsPressed());
            input.ShieldPressed = _defenseInputEdges.TakeShieldPressed();
            input.GrabPressed = _defenseInputEdges.TakeGrabPressed();
            input.ActiveSlot = pendingSlotPress;
            input.IsAiming = aimCtx.IsAiming;
            // Utility edges (ADR-0017/0018): consumed here, one tick each. Set before the
            // FSM gate — a snap/toggle/retarget is not movement and must survive canMove=false.
            input.FaceToCamera = _pendingFaceToCamera;
            _pendingFaceToCamera = false;
            input.ToggleLock = _pendingToggleLock;
            _pendingToggleLock = false;
            input.RetargetPressed = _pendingRetarget;
            _pendingRetarget = false;
            // The simulation owns dash admission, including attack IASA windows.
            input.DashPressed = _pendingDash;
            _pendingDash = false;

            // Yaw wraps before centidegree encoding; camera turns must not saturate the wire angle.
            float deg = Mathf.DeltaAngle(0f, bodyYawDeg);
            input.FacingYaw = (short)(deg * 100f);

            // Aim yaw: camera default, overridden by active ability
            float aimDeg = aimCtx.AimYawRad.HasValue
                ? aimCtx.AimYawRad.Value * Mathf.Rad2Deg
                : camera != null && !aimFromFacing ? camera.GetCameraYawDeg() : deg;
            input.AimYaw = (short)(Mathf.DeltaAngle(0f, aimDeg) * 100f);

            // Aim pitch: camera default, overridden by active ability
            float aimPitchDeg = camera != null && !aimFromFacing ? camera.GetCameraPitchDeg() : 0f;
            input.AimPitch = (short)Math.Clamp(aimPitchDeg * 100f, -9000f, 9000f);
            if (aimCtx.AimPitchRad.HasValue)
                input.AimPitch = (short)Math.Clamp(aimCtx.AimPitchRad.Value * Mathf.Rad2Deg * 100f, -9000f, 9000f);

            input.AimDistance = aimCtx.AimDistanceCm ?? 0;
            // FSM movement gate: zero out input if state disallows movement
            if (aimCtx.LocksMovement || (canMove != null && !canMove()))
            {
                input.MoveX = 0f;
                input.MoveY = 0f;
                input.Up = input.Left = input.Right = false;
                input.Jump = false;
                input.JumpHeld = false;
                moveDirection = Vector3.zero;
                snappedInputDirection = Vector2.zero;
            }
            else
            {
                // Gate allows movement — consume pending jump
                if (_pendingJump)
                {
                    input.Jump = true;
                    _pendingJump = false;
                    Debug.Log("[Input] _pendingJump consumed -> input.Jump=true");
                }
            }

            input.TargetEntityId = targetEntityId;
            return (input, moveDirection, snappedInputDirection);
        }
    }
}
