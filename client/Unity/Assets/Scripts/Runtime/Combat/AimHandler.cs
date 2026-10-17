using SlopArena.Shared;
using SlopArena.Client.Camera;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Owns the full aim pipeline for the local player each FixedUpdate tick:
    ///   1. Resolves which AbilitySpec is currently active (just-pressed or held during attack)
    ///   2. Transitions CameraMount mode (Normal / FreeCursor / Aiming)
    ///   3. Activates/deactivates AimCameraMount for CameraForward3D abilities
    ///   4. Computes GroundCursor/GroundVector aim and drives the presentation views
    ///   5. Returns AimContext for InputController.BuildInputState
    ///
    /// TrainingMatch (and future match types) call Init() once then Evaluate() each tick.
    /// Zero aim logic leaks back to the caller. Aim input is computed here and never
    /// depends on any indicator being present — views are write-only consumers.
    /// </summary>
    public class AimHandler : MonoBehaviour
    {
        [Header("Views (optional — input works without them)")]
        [SerializeField] private TargetLockIndicator _targetLockIndicator;
        [SerializeField] private GroundDestinationIndicator _groundDestinationIndicator;
        [SerializeField] private ProjectileTrajectoryIndicator _trajectoryIndicator;

        [Header("Camera")]
        [SerializeField] private CameraMount _cameraMount;
        [SerializeField] private AimCameraMount _aimCameraMount;
        [SerializeField] private float _aimSensitivity = 0.15f;

        [Header("Ground cursor")]
        [SerializeField] private float _minRange = 1f;
        [SerializeField] private float _maxRange = 12f;

        private CookedChargedDirectionalDashCapabilityParameters _chargedDashParameters;
        private bool _hasChargedDashParameters;
        private CameraMode _activeMode = CameraMode.Normal;
        private byte _aimingSlot;
        private Transform _characterTransform;
        private float _capsuleHeight = 1.3f;
        /// <summary>Cached aim values — persist after key release so server gets right direction during fire delay.</summary>
        private float _lastAimYawRad;
        private float _lastAimPitchRad;
        private ushort _lastAimDistanceCm;
        private float _groundVectorDistance = 5f;
        private byte _lastAimingSlot;
        /// <summary>
        /// GroundVector aim: screen-space offset of a hidden cursor anchored at the character's
        /// screen position. The indicator direction = angle from the character to the cursor, so
        /// both horizontal AND vertical mouse movement rotate it naturally (1:1 with the mouse).
        /// </summary>
        private Vector2 _aimScreenOffset;
        private Vector2 _controllerCursorPosition;
        private bool _controllerCursorActive;
        /// <summary>Dead zone radius (px) — below this the aim keeps its last direction.</summary>
        private const float AimScreenDeadZone = 20f;
        /// <summary>Max cursor offset (px) — clamps runaway spin while keeping the angle.</summary>
        private const float AimScreenMaxOffset = 600f;
        /// <summary>Whether the current aimed move needs the HUD crosshair.</summary>
        public bool ShowCrosshair { get; private set; }

        // Last computed ground-cursor destination, kept for UpdateTargetPresentation-free
        // per-tick view driving in Evaluate.
        private Vector3 _groundAimTarget;
        private bool _hasGroundAimTarget;

        /// <summary>
        /// Wire camera into the aim pipeline once the scene is ready.
        /// Call from OnMatchStart after the camera hierarchy exists.
        /// </summary>
        public void Init(CameraMount cameraMount, UnityEngine.Camera renderCamera, Transform characterTransform, float capsuleHeight)
        {
            _cameraMount = cameraMount;
            _characterTransform = characterTransform;
            _capsuleHeight = capsuleHeight;
            EnsureViewComponents();
            if (_targetLockIndicator != null)
                _targetLockIndicator.Init(null, renderCamera);
            _cameraMount?.SetMode(CameraMode.Normal);
            _activeMode = CameraMode.Normal;
        }

        /// <summary>
        /// Bind the HUD's dedicated targeting root (call after HUD initialization /
        /// roster rebuild so the indicator tracks the current panel).
        /// </summary>
        public void BindTargetPresentation(VisualElement targetingRoot)
        {
            EnsureViewComponents();
            if (_targetLockIndicator != null)
            {
                _targetLockIndicator.Init(targetingRoot, _cameraMount?.RenderCamera);
                _targetLockIndicator.Clear(immediate: true);
            }
        }

        /// <summary>Post-tick target presentation for the projected lock arrow.</summary>
        public void UpdateTargetPresentation(
            CharacterState localState,
            PlayerRenderer target,
            ushort targetDamagePercent)
        {
            if (_targetLockIndicator == null) return;
            _targetLockIndicator.SetTarget(target, localState.LockOn, targetDamagePercent);
        }

        public void UpdateObjectTargetPresentation(CharacterState localState, Transform target,
            float radius, ushort? damagePercent)
            => _targetLockIndicator?.SetObjectTarget(target, radius, localState.LockOn, damagePercent);

        /// <summary>
        /// Immediate lifecycle reset (death/stock reset, match end, teardown, re-Init):
        /// clears every view, restores camera mode, and clears cached aim state.
        /// Ordinary key release must NOT call this — cached aim feeds the fire delay.
        /// </summary>
        public void ResetPresentation()
        {
            _targetLockIndicator?.Clear(immediate: true);
            _groundDestinationIndicator?.Clear();
            _trajectoryIndicator?.Clear();
            _aimCameraMount?.Deactivate();
            _cameraMount?.SetMode(CameraMode.Normal);
            _activeMode = CameraMode.Normal;
            _aimingSlot = 0;
            _lastAimingSlot = 0;
            _lastAimYawRad = 0f;
            _lastAimPitchRad = 0f;
            _lastAimDistanceCm = 0;
            _aimScreenOffset = Vector2.zero;
            _controllerCursorPosition = Vector2.zero;
            _controllerCursorActive = false;
            _hasGroundAimTarget = false;
            ShowCrosshair = false;
        }

        /// <summary>
        /// Resolve aim state for this tick.
        /// Figures out the active aimed ability from player state + just-pressed slot,
        /// drives camera and views, returns an AimContext for BuildInputState.
        /// </summary>
        public AimContext Evaluate(
            CharacterState playerState,
            byte pendingSlotPress,
            CharacterDefinition charDef,
            InputController inputController)
        {
            if (ChatInputGate.SuppressGameplay || SettingsOverlay.Active is { IsOpen: true } ||
                SettingsOverlay.ClosedThisFrame || HumanInputActions.IsCapturing ||
                MatchPauseMenu.Active is { IsPaused: true })
                return AimContext.None;

            // ── 1. Resolve active aim spec ──
            AbilitySpec? spec = null;
            _aimingSlot = 0;

            // A slot was just pressed and its key is still held down
            if (pendingSlotPress > 0)
            {
                byte slotIdx = (byte)(pendingSlotPress - 1);
                if (inputController.IsSlotKeyHeld(slotIdx))
                {
                    var candidate = charDef.GetSlotAbility(slotIdx, !playerState.IsGrounded);
                    if (candidate != null && (candidate.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D or AimMode.GroundVector
                        || candidate.Behavior == AbilityBehavior.ChargeAttack))
                    {
                        spec = candidate;
                        _aimingSlot = slotIdx;
                    }
                }
            }
            // Already attacking/aiming with an aimed ability and key is still held
            if (spec == null && playerState.State is (ActionState.Attacking or ActionState.Aiming) && playerState.AttackSlot > 0)
            {
                byte slotIdx = (byte)(playerState.AttackSlot - 1);
                if (inputController.IsSlotKeyHeld(slotIdx))
                {
                    var candidate = charDef.GetSlotAbility(slotIdx, !playerState.IsGrounded);
                    if (candidate != null && (candidate.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D or AimMode.GroundVector
                        || candidate.Behavior == AbilityBehavior.ChargeAttack))
                    {
                        spec = candidate;
                        _aimingSlot = slotIdx;
                    }
                }
            }

            AimMode aimMode = spec?.AimMode ?? AimMode.None;
            bool locksMovement = spec?.AimMovement == AimMovementMode.Fixed;
            Vector2 aimStick = inputController.GetAimStick(locksMovement);

            // ── 2. Drive camera mode (transitions only) ──
            CameraMode desired = aimMode switch
            {
                AimMode.GroundCursor    => CameraMode.FreeCursor,
                AimMode.CameraForward3D => CameraMode.Aiming,
                AimMode.GroundVector    => CameraMode.Frozen,
                _                       => CameraMode.Normal,
            };

            if (desired != _activeMode)
            {
                // Leaving Aiming — deactivate aim camera so Cinemachine blends back to orbital
                if (_activeMode == CameraMode.Aiming)
                    _aimCameraMount?.Deactivate();

                // Entering GroundCursor — preserve mouse position or seed a virtual cursor.
                if (desired == CameraMode.FreeCursor)
                {
                    _cameraMount?.FreezeAtCurrentAngles();
                    _controllerCursorPosition = Mouse.current != null
                        ? Mouse.current.position.ReadValue()
                        : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                    _controllerCursorActive = Mouse.current == null;
                }
                // Entering Frozen (GroundVector) — freeze the camera; mouse delta rotates the
                // aim direction instead. Inherit the current view yaw as the initial aim.
                if (desired == CameraMode.Frozen)
                {
                    _cameraMount?.FreezeAtCurrentAngles();
                    _lastAimYawRad = _cameraMount?.GetCameraYawRad() ?? 0f;
                    _lastAimPitchRad = 0f;
                    // Hidden cursor starts above the character's screen position → aim = camera forward.
                    _aimScreenOffset = Vector2.up * AimScreenDeadZone;
                    _groundVectorDistance = spec?.Params != null
                        && spec.Params.TryGetValue("dash_distance", out float distance) ? distance : 5f;
                    // Wibou's dash velocity is authored as consecutive timeline windows.
                    // Derive the endpoint once on aim entry so the marker follows Ability Lab edits.
                    if (charDef.Class == CharacterClass.Wibou)
                    {
                        var slot = charDef.GetCookedSlotAbility((byte)(_aimingSlot + 1), !playerState.IsGrounded);
                        if (slot?.Timeline.Stages.Count > 0)
                        {
                            float authoredDistance = 0f;
                            foreach (var operation in slot.Timeline.Stages[0].Operations)
                                if (operation is CookedForwardLungeOperation lunge)
                                    authoredDistance += lunge.Speed * lunge.DurationTicks * SlopArena.Shared.Simulation.TickDt;
                            if (authoredDistance > 0f)
                                _groundVectorDistance = authoredDistance;
                        }
                    }
                    _hasChargedDashParameters = false;
                    var cookedAimSlot = charDef.GetCookedSlotAbility((byte)(_aimingSlot + 1), !playerState.IsGrounded);
                    if (cookedAimSlot != null)
                        foreach (var stage in cookedAimSlot.Timeline.Stages)
                        foreach (var operation in stage.Operations)
                            if (operation is CookedStartCapabilityOperation
                                { Parameters: CookedChargedDirectionalDashCapabilityParameters parameters })
                            {
                                _chargedDashParameters = parameters;
                                _hasChargedDashParameters = true;
                                break;
                            }
                }

                // Entering Aiming — activate aim camera, inherit current yaw + zoom distance
                if (desired == CameraMode.Aiming && _characterTransform != null)
                {
                    float yawRad      = _cameraMount?.GetCameraYawRad() ?? 0f;
                    float orbitRadius = _cameraMount?.GetOrbitRadius()  ?? 2.5f;
                    _aimCameraMount?.Activate(_characterTransform, yawRad, orbitRadius);
                    _lastAimYawRad   = yawRad;
                    _lastAimPitchRad = 0f;
                }

                _cameraMount?.SetMode(desired);
                _activeMode = desired;
            }

            // ── 3. Collect aim data ──
            AimContext ctx = AimContext.None;
            bool isCharging = spec != null && spec.Behavior == AbilityBehavior.ChargeAttack;
            _hasGroundAimTarget = false;

            if (aimMode == AimMode.GroundCursor)
            {
                var (yawRad, distCm, aimTarget) = ComputeGroundCursorAim(aimStick);
                _lastAimYawRad = yawRad ?? _lastAimYawRad;
                _lastAimPitchRad = 0f;
                _lastAimDistanceCm = distCm ?? 0;
                _lastAimingSlot = _aimingSlot;
                ctx = new AimContext
                {
                    IsAiming      = true,
                    AimYawRad     = yawRad,
                    AimDistanceCm = distCm,
                };

                // Destination reticle + unchanged trajectory preview.
                if (aimTarget.HasValue)
                {
                    _groundDestinationIndicator?.SetDestination(
                        aimTarget.Value, aimTarget.Value - _characterTransform.position, 0.8f);
                    _trajectoryIndicator?.SetTrajectory(
                        _characterTransform.position, _capsuleHeight, yawRad ?? 0f, (distCm ?? 0) * 0.01f);
                }
                else
                {
                    _groundDestinationIndicator?.Clear();
                    _trajectoryIndicator?.Clear();
                }
            }
            else if (aimMode == AimMode.GroundVector)
            {
                // Screen-space aim: the direction follows the mouse like a hidden cursor
                // anchored at the character's screen position. Horizontal AND vertical mouse
                // movement rotate it naturally (1:1 on screen), instead of a yaw-only delta.
                Vector2 delta = HumanInputActions.MouseLook.ReadValue<Vector2>();
                var cameraSettings = ClientSettingsService.Instance;
                delta *= cameraSettings.CameraInputGain;
                if (cameraSettings.InvertCameraHorizontal) delta.x = -delta.x;
                if (cameraSettings.InvertCameraVertical) delta.y = -delta.y;
                _aimScreenOffset += delta;
                // Stick direction selects the ground direction immediately. Keeping the
                // last offset on neutral preserves the chosen aim through release.
                if (aimStick.sqrMagnitude > 0.01f)
                    _aimScreenOffset = aimStick.normalized * (AimScreenDeadZone * 2f);
                _aimScreenOffset = Vector2.ClampMagnitude(_aimScreenOffset, AimScreenMaxOffset);
                if (_aimScreenOffset.sqrMagnitude > AimScreenDeadZone * AimScreenDeadZone)
                {
                    Vector2 screenDir = _aimScreenOffset.normalized;
                    Vector3 camFwd = _cameraMount?.GetForwardDirection() ?? Vector3.forward;
                    Vector3 camRight = _cameraMount?.GetRightDirection() ?? Vector3.right;
                    Vector3 worldDir = (camFwd * screenDir.y + camRight * screenDir.x).normalized;
                    _lastAimYawRad = Mathf.Atan2(worldDir.x, worldDir.z);
                }
                _lastAimingSlot = _aimingSlot;

                // Use the authored dash travel for the endpoint; width follows the hitbox.
                float dashDistance = _groundVectorDistance;
                float dashWidth = 1.1f;
                if (spec?.Stages is { Length: > 0 } && spec.Stages[0].HitboxEvents is { Length: > 0 })
                    dashWidth = spec.Stages[0].HitboxEvents[0].Radius * 2f;
                if (_hasChargedDashParameters)
                    dashWidth = _chargedDashParameters.FinisherHitbox.Radius * 2f;
                byte? chargeTier = null;
                if (_hasChargedDashParameters)
                {
                    dashDistance = _chargedDashParameters.GetDashDistance(playerState.ChargeTicks);
                    chargeTier = _chargedDashParameters.GetChargeTier(playerState.ChargeTicks);
                }

                float yaw = _lastAimYawRad;
                ushort distCm = (ushort)Mathf.Clamp(dashDistance * 100f, 0f, 6500f);
                ctx = new AimContext
                {
                    IsAiming      = true,
                    AimYawRad     = yaw,
                    AimDistanceCm = distCm,
                };

                // Reticle at the dash endpoint + short wedge showing travel direction.
                if (_characterTransform != null)
                {
                    Vector3 dir = new(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                    Vector3 feetY = _characterTransform.position;
                    Vector3 destination = feetY + dir * dashDistance;
                    _groundDestinationIndicator?.SetDestination(destination, dir, dashWidth, chargeTier,
                        _hasChargedDashParameters
                            ? Mathf.Lerp(0.7f, 1.3f, Mathf.InverseLerp(
                                _chargedDashParameters.MinDistance, _chargedDashParameters.MaxDistance, dashDistance))
                            : 1f);
                    _trajectoryIndicator?.Clear();
                }
            }
            else
            {
                _groundDestinationIndicator?.Clear();
                _trajectoryIndicator?.Clear();

                if (aimMode == AimMode.CameraForward3D && _aimCameraMount != null)
                {
                    var cameraSettings = ClientSettingsService.Instance;
                    // Mouse follows mouse sensitivity; the stick follows stick look speed.
                    Vector2 delta = HumanInputActions.MouseLook.ReadValue<Vector2>() * cameraSettings.CameraInputGain
                        + aimStick * (600f * Time.deltaTime * cameraSettings.StickInputGain);
                    if (cameraSettings.InvertCameraHorizontal) delta.x = -delta.x;
                    if (cameraSettings.InvertCameraVertical) delta.y = -delta.y;
                    _aimCameraMount.Tick(_characterTransform);
                    bool horizontalOnly = charDef.Class == CharacterClass.Manki
                        && (byte)(_aimingSlot + 1) == AbilitySlots.F;
                    _aimCameraMount.ApplyMouseDelta(delta, _aimSensitivity, !horizontalOnly);

                    _lastAimYawRad = _aimCameraMount.GetAimYawRad();
                    _lastAimPitchRad = horizontalOnly ? 0f : _aimCameraMount.GetAimPitchRad();
                    _lastAimingSlot = _aimingSlot;

                    ctx = new AimContext
                    {
                        IsAiming    = true,
                        AimYawRad   = _lastAimYawRad,
                        AimPitchRad = _lastAimPitchRad,
                    };
                }
                else if (isCharging && _aimingSlot > 0 && inputController.IsSlotKeyHeld(_aimingSlot))
                {
                    // ChargeAttack: signal IsAiming=true while key held, no cursor/camera changes
                    ctx = new AimContext { IsAiming = true };
                }
                else if (_lastAimingSlot > 0 && playerState.State is (ActionState.Attacking or ActionState.Aiming)
                    && playerState.AttackSlot == (byte)(_lastAimingSlot + 1))
                {
                    // Key released but server hasn't fired yet — send last known aim
                    // direction AND distance (the sim caches both for the throw).
                    ctx = new AimContext
                    {
                        IsAiming       = false,
                        AimYawRad      = _lastAimYawRad,
                        AimPitchRad    = _lastAimPitchRad,
                        AimDistanceCm  = _lastAimDistanceCm,
                    };
                }
            }

            ShowCrosshair = aimMode is AimMode.GroundCursor or AimMode.CameraForward3D;
            ctx.LocksMovement = ctx.IsAiming && locksMovement;
            return ctx;
        }

        /// <summary>
        /// GroundCursor aim: project the mouse to the ground, clamp to the local range
        /// window, return yaw + distance in cm. Replaces the old AimIndicator.UpdateAim
        /// raycast — identical numerics; the view is no longer in the input path.
        /// </summary>
        private (float? yawRad, ushort? distCm, Vector3? aimTarget) ComputeGroundCursorAim(Vector2 stick)
        {
            if (_characterTransform == null) return (null, null, null);

            var unityCam = _cameraMount?.RenderCamera;
            if (unityCam == null)
            {
                var main = UnityEngine.Camera.main;
                if (main == null) return (null, null, null);
                unityCam = main;
            }

            var mouse = Mouse.current;
            if (stick.sqrMagnitude > 0.01f)
            {
                if (!_controllerCursorActive && mouse != null)
                    _controllerCursorPosition = mouse.position.ReadValue();
                _controllerCursorPosition += stick * (700f * Time.deltaTime * ClientSettingsService.Instance.StickInputGain);
                _controllerCursorPosition.x = Mathf.Clamp(_controllerCursorPosition.x, 0f, Screen.width);
                _controllerCursorPosition.y = Mathf.Clamp(_controllerCursorPosition.y, 0f, Screen.height);
                _controllerCursorActive = true;
            }
            else if (HumanInputActions.MouseLook.ReadValue<Vector2>().sqrMagnitude > 0.01f)
                _controllerCursorActive = false;
            Vector2 mousePos = _controllerCursorActive
                ? _controllerCursorPosition
                : mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var mouseRay = unityCam.ScreenPointToRay(mousePos);

            float groundY = 0f;
            bool foundGround = false;
            Vector3 aimTarget = default;
            if (Physics.Raycast(mouseRay, out var hit, 200f))
            {
                if (hit.point.y < 1.0f && hit.point.y > -0.5f)
                {
                    groundY = hit.point.y;
                    aimTarget = hit.point;
                    foundGround = true;
                }
            }

            if (!foundGround && mouseRay.direction.y < 0f)
            {
                float t = -mouseRay.origin.y / mouseRay.direction.y;
                aimTarget = mouseRay.origin + mouseRay.direction * t;
                aimTarget.y = 0f;
            }
            else if (!foundGround)
            {
                return (null, null, null);
            }

            Vector3 toTarget = aimTarget - _characterTransform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            if (dist < _minRange)
            {
                toTarget = toTarget.normalized * _minRange;
                dist = _minRange;
            }
            else if (dist > _maxRange)
            {
                toTarget = toTarget.normalized * _maxRange;
                dist = _maxRange;
            }
            aimTarget = _characterTransform.position + toTarget;
            aimTarget.y = groundY + 0.05f; // slight offset to avoid z-fight with floor

            _groundAimTarget = aimTarget;
            _hasGroundAimTarget = true;
            float yaw = Mathf.Atan2(toTarget.x, toTarget.z);
            ushort distCm = (ushort)Mathf.Clamp(dist * 100f, 0f, 6500f);
            return (yaw, distCm, aimTarget);
        }

        /// <summary>
        /// Ensure the view components exist. The bracket controller lives on this object;
        /// destination/trajectory are serialized (prefab-authored) when bound, created
        /// empty otherwise — views stay optional and never gate input.
        /// </summary>
        private void EnsureViewComponents()
        {
            if (_targetLockIndicator == null)
                _targetLockIndicator = gameObject.GetComponent<TargetLockIndicator>()
                    ?? gameObject.AddComponent<TargetLockIndicator>();
            if (_groundDestinationIndicator == null)
                _groundDestinationIndicator = gameObject.GetComponentInChildren<GroundDestinationIndicator>();
            if (_trajectoryIndicator == null)
                _trajectoryIndicator = gameObject.GetComponentInChildren<ProjectileTrajectoryIndicator>()
                    ?? FindFirstObjectByType<ProjectileTrajectoryIndicator>();
        }
    }
}
