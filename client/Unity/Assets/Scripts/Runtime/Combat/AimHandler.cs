using SlopArena.Shared;
using SlopArena.Shared.Abilities;
using SlopArena.Client.Camera;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Owns the full aim pipeline for the local player each FixedUpdate tick:
    ///   1. Resolves which CookedSlotDefinition is currently active (just-pressed or held during attack)
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
        /// <summary>The aimed slot's lobbed throw (Round Bomb), read from the cooked slot when the cursor opens.</summary>
        private CookedMankiRoundBombCapabilityParameters _lob;
        private PlayerRenderer _characterRenderer;
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
        private Vector3 _controllerCursorGroundPoint;
        private bool _controllerCursorActive;
        private System.Func<ulong, Vector3?> _resolveAimTarget;
        private const float GroundCursorSpeed = 16f;
        /// <summary>Dead zone radius (px) — below this the aim keeps its last direction.</summary>
        private const float AimScreenDeadZone = 20f;
        /// <summary>Max cursor offset (px) — clamps runaway spin while keeping the angle.</summary>
        private const float AimScreenMaxOffset = 600f;
        /// <summary>Whether the current aimed move needs the HUD crosshair.</summary>
        public bool ShowCrosshair { get; private set; }
        public bool SharedCamera { get; set; }
        public bool ControllerAim { get; set; }

        // Last computed ground-cursor destination, kept for UpdateTargetPresentation-free
        // per-tick view driving in Evaluate.
        private Vector3 _groundAimTarget;
        private bool _hasGroundAimTarget;

        /// <summary>
        /// Wire camera into the aim pipeline once the scene is ready.
        /// Call from OnMatchStart after the camera hierarchy exists.
        /// </summary>
        public void Init(CameraMount cameraMount, UnityEngine.Camera renderCamera, Transform characterTransform, float capsuleHeight,
            System.Func<ulong, Vector3?> resolveAimTarget = null)
        {
            _cameraMount = cameraMount;
            _characterTransform = characterTransform;
            _characterRenderer = characterTransform != null ? characterTransform.GetComponent<PlayerRenderer>() : null;
            _capsuleHeight = capsuleHeight;
            _resolveAimTarget = resolveAimTarget;
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
            _controllerCursorGroundPoint = Vector3.zero;
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
            InputController inputController, CookedSlotDefinition? activeMove)
        {
            if (ChatInputGate.SuppressGameplay || SettingsOverlay.Active is { IsOpen: true } ||
                SettingsOverlay.ClosedThisFrame || HumanInputActions.IsCapturing ||
                MatchPauseMenu.Active is { IsPaused: true })
                return AimContext.None;

            // ── 1. Resolve active aim spec ──
            CookedSlotDefinition? spec = null;
            _aimingSlot = 0;

            // A slot was just pressed and its key is still held down
            if (pendingSlotPress > 0)
            {
                byte slotIdx = (byte)(pendingSlotPress - 1);
                if (inputController.IsSlotKeyHeld(slotIdx))
                {
                    var candidate = charDef.GetCookedSlotAbility((byte)((slotIdx) + 1), !playerState.IsGrounded);
                    if (candidate != null && ((AimMode)candidate.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D or AimMode.GroundVector
                        || candidate.Behavior == AuthoringAbilityBehavior.ChargeAttack))
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
                    var candidate = activeMove;
                    if (candidate != null && ((AimMode)candidate.AimMode is AimMode.GroundCursor or AimMode.CameraForward3D or AimMode.GroundVector
                        || candidate.Behavior == AuthoringAbilityBehavior.ChargeAttack))
                    {
                        spec = candidate;
                        _aimingSlot = slotIdx;
                    }
                }
            }

            AimMode aimMode = (AimMode?)spec?.AimMode ?? AimMode.None;
            bool locksMovement = spec?.AimMovement == AuthoringAimMovementMode.Fixed;
            Vector2 aimStick = inputController.GetAimStick(locksMovement);

            // ── 2. Drive camera mode (transitions only) ──
            CameraMode desired = aimMode switch
            {
                AimMode.GroundCursor    => CameraMode.FreeCursor,
                AimMode.CameraForward3D => SharedCamera ? CameraMode.Normal : CameraMode.Aiming,
                AimMode.GroundVector    => CameraMode.Frozen,
                _                       => CameraMode.Normal,
            };

            if (desired != _activeMode)
            {
                // Leaving Aiming — deactivate aim camera so Cinemachine blends back to orbital
                if (_activeMode == CameraMode.Aiming)
                    _aimCameraMount?.Deactivate();

                if (desired == CameraMode.FreeCursor)
                {
                    _cameraMount?.FreezeAtCurrentAngles();
                    _lob = null;
                    var cookedCursorSlot = spec;
                    if (cookedCursorSlot != null)
                        foreach (var stage in cookedCursorSlot.Timeline.Stages)
                        foreach (var operation in stage.Operations)
                            if (operation is CookedStartCapabilityOperation { Parameters: CookedMankiRoundBombCapabilityParameters lob })
                                _lob = lob;
                    if (_characterTransform != null)
                    {
                        Vector3 facing = new(Mathf.Sin(playerState.FacingYaw), 0f, Mathf.Cos(playerState.FacingYaw));
                        _controllerCursorGroundPoint = ClampGroundCursor(
                            _resolveAimTarget?.Invoke(playerState.TargetEntityId) ?? BodyCentre + facing * 6f,
                            BodyCentre, _lob?.MaxRange ?? _maxRange);
                    }
                    _controllerCursorActive = ControllerAim || HumanInputActions.LastUsedGamepad || Mouse.current == null;
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
                    _groundVectorDistance = 5f;
                    float authoredDistance = 0f;
                    if (spec != null)
                        foreach (var stage in spec.Timeline.Stages)
                        foreach (var operation in stage.Operations)
                            if (operation is CookedForwardLungeOperation lunge)
                                authoredDistance += lunge.Speed * lunge.DurationTicks * SlopArena.Shared.Simulation.TickDt;
                    if (authoredDistance > 0f) _groundVectorDistance = authoredDistance;
                    _hasChargedDashParameters = false;
                    var cookedAimSlot = spec;
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
                    float orbitRadius = _cameraMount?.GetAimOrbitRadius() ?? 2.5f;
                    _aimCameraMount?.Activate(_characterTransform, yawRad, orbitRadius);
                    _lastAimYawRad   = yawRad;
                    _lastAimPitchRad = 0f;
                }

                _cameraMount?.SetMode(desired);
                _activeMode = desired;
            }

            // ── 3. Collect aim data ──
            AimContext ctx = AimContext.None;
            bool isCharging = spec != null && spec.Behavior == AuthoringAbilityBehavior.ChargeAttack;
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
                        BodyCentre, _capsuleHeight, yawRad ?? 0f, (distCm ?? 0) * 0.01f,
                        _lob != null ? aimTarget : null);
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
                Vector2 delta = ControllerAim ? Vector2.zero : HumanInputActions.MouseLook.ReadValue<Vector2>();
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
                if (spec?.Timeline.Stages is { Count: > 0 } && spec.Timeline.Stages[0].ReadHitboxes() is { Length: > 0 })
                    dashWidth = spec.Timeline.Stages[0].ReadHitboxes()[0].Radius * 2f;
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

                if (aimMode == AimMode.CameraForward3D && SharedCamera)
                {
                    _lastAimYawRad = playerState.FacingYaw; _lastAimPitchRad = 0f; _lastAimingSlot = _aimingSlot;
                    ctx = new AimContext { IsAiming = true, AimYawRad = _lastAimYawRad, AimPitchRad = 0f };
                }
                else if (aimMode == AimMode.CameraForward3D && _aimCameraMount != null)
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
        /// GroundCursor aim: project the mouse onto the stage's collision proxy (the y = 0
        /// plane over open water or void), clamp to the range and return yaw + distance in cm.
        /// A lobbed throw sends the foot-height distance whose arc passes through the cursor,
        /// so it lands on the raised island or lower roof the cursor shows.
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
                Vector3 forward = unityCam.transform.forward;
                Vector3 right = unityCam.transform.right;
                forward.y = right.y = 0f;
                _controllerCursorGroundPoint += (forward.normalized * stick.y + right.normalized * stick.x)
                    * (GroundCursorSpeed * Time.deltaTime * ClientSettingsService.Instance.StickInputGain);
                _controllerCursorActive = true;
            }
            else if (!ControllerAim && HumanInputActions.MouseLook.ReadValue<Vector2>().sqrMagnitude > 0.01f)
                _controllerCursorActive = false;
            Vector2 mousePos = mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (_controllerCursorActive)
            {
                _controllerCursorGroundPoint = ClampGroundCursor(_controllerCursorGroundPoint, BodyCentre, _lob?.MaxRange ?? _maxRange);
                _controllerCursorGroundPoint.y = PresentationFloor.TrySample(
                    _controllerCursorGroundPoint.x, _controllerCursorGroundPoint.z, out float floor) ? floor : 0f;
            }
            var cursorRay = _controllerCursorActive
                ? new Ray(unityCam.transform.position, _controllerCursorGroundPoint - unityCam.transform.position)
                : unityCam.ScreenPointToRay(mousePos);

            var resolved = ResolveGroundCursor(cursorRay, BodyCentre, _characterTransform.forward, _capsuleHeight, _lob, _minRange, _maxRange);
            if (!resolved.HasValue) return (null, null, null);
            var (yaw, dist, aimTarget) = resolved.Value;
            if (!_controllerCursorActive)
                _controllerCursorGroundPoint = aimTarget;
            aimTarget.y += 0.05f; // slight offset to avoid z-fight with the surface
            _groundAimTarget = aimTarget;
            _hasGroundAimTarget = true;
            ushort distCm = (ushort)Mathf.Clamp(dist * 100f, 0f, 6500f);
            return (yaw, distCm, aimTarget);
        }

        /// <summary>The simulated body centre (state PY): the renderer root carries the model's Y offset.</summary>
        private Vector3 BodyCentre => _characterTransform.position - Vector3.up * (_characterRenderer != null ? _characterRenderer.ModelYOffset : 0f);

        private static Vector3 ClampGroundCursor(Vector3 point, Vector3 bodyCentre, float maxRange)
        {
            Vector3 offset = point - bodyCentre;
            offset.y = 0f;
            return bodyCentre + Vector3.ClampMagnitude(offset, maxRange);
        }

        /// <summary>
        /// Where a ground-cursor ray points and what to send. The ray hits the authoritative stage:
        /// MatchBase mirrors its collision as a two-sided proxy on the Ignore Raycast layer, so the
        /// cursor sits on the island, roof or platform under it; a steep face stands for the top of
        /// its block, where Shared explodes a thrown bomb; open water or void falls back to the
        /// y = 0 plane. A lobbed throw (<paramref name="lob"/>) sends the foot-height distance whose
        /// unchanged server arc passes through that point and returns where the arc really lands;
        /// anything else sends the clamped horizontal distance.
        /// </summary>
        public static (float yawRad, float distance, Vector3 landing)? ResolveGroundCursor(Ray ray, Vector3 bodyCentre,
            Vector3 fallbackForward, float capsuleHeight, CookedMankiRoundBombCapabilityParameters lob, float minRange, float maxRange)
        {
            Vector3 aimTarget;
            if (Physics.Raycast(ray, out var hit, 400f, Physics.IgnoreRaycastLayer, QueryTriggerInteraction.Ignore))
            {
                aimTarget = hit.point;
                if (hit.normal.y < 0.5f && PresentationFloor.TrySample(hit.point.x, hit.point.z, out float top) && top > aimTarget.y)
                    aimTarget.y = top;
            }
            else if (ray.direction.y < 0f)
            {
                aimTarget = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
                aimTarget.y = 0f;
            }
            else
            {
                return null;
            }

            Vector3 toTarget = aimTarget - bodyCentre;
            toTarget.y = 0f;
            float horizontal = toTarget.magnitude;
            Vector3 direction = horizontal > 0.001f ? toTarget / horizontal : fallbackForward;
            float dist;
            if (lob != null)
            {
                float launchY = bodyCentre.y + MankiRoundBomb.LaunchOffsetY;
                float landing = -capsuleHeight * 0.5f - MankiRoundBomb.LaunchOffsetY;
                float through = CombatMath.LobDistanceThrough(
                    horizontal, aimTarget.y - launchY, lob.LaunchAngle * Mathf.Deg2Rad, landing);
                dist = Mathf.Clamp(float.IsInfinity(through) ? lob.MaxRange : through, minRange, lob.MaxRange);
                aimTarget = PredictLobImpact(bodyCentre, direction, dist, capsuleHeight, lob);
            }
            else
            {
                dist = Mathf.Clamp(horizontal, minRange, maxRange);
                if (!Mathf.Approximately(dist, horizontal))
                {
                    aimTarget = bodyCentre + direction * dist;
                    aimTarget.y = PresentationFloor.TrySample(aimTarget.x, aimTarget.z, out float floor) ? floor : 0f;
                }
            }
            return (Mathf.Atan2(direction.x, direction.z), dist, aimTarget);
        }

        /// <summary>
        /// Where Shared's thrown bomb lands for a sent distance: its arc stepped like SpellResolver
        /// and tested against the floor like CheckGroundCollision (open water or void counts as y = 0).
        /// </summary>
        public static Vector3 PredictLobImpact(Vector3 origin, Vector3 direction, float distance, float capsuleHeight,
            CookedMankiRoundBombCapabilityParameters lob)
        {
            float landing = -capsuleHeight * 0.5f - MankiRoundBomb.LaunchOffsetY;
            CombatMath.ComputeProjectileLaunch(distance, lob.LaunchAngle * Mathf.Deg2Rad, lob.Gravity, landing,
                out _, out float hSpeed, out float vSpeed);
            var position = origin + Vector3.up * MankiRoundBomb.LaunchOffsetY;
            var velocity = direction * hSpeed + Vector3.up * vSpeed;
            for (int tick = 0; tick < lob.MaxFlightTicks; tick++)
            {
                position += velocity * SlopArena.Shared.Simulation.TickDt;
                velocity.y -= lob.Gravity * SlopArena.Shared.Simulation.TickDt;
                float ground = PresentationFloor.TrySample(position.x, position.z, out float floor) ? floor : 0f;
                if (position.y - lob.HitboxRadius <= ground)
                    return new Vector3(position.x, ground, position.z);
            }
            return position;
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
