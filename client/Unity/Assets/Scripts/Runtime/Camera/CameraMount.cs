using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
namespace SlopArena.Client.Camera
{
    public enum CameraMode
    {
        Normal,       // Cursor locked, camera orbits freely
        Frozen,       // Cursor locked, camera yaw/pitch held constant
        FreeCursor,   // Cursor unlocked, camera yaw/pitch held constant
        Aiming,       // Cursor locked, orbital camera frozen, AimCameraMount drives aim camera
    }

    /// <summary>A rendered opponent offered to automatic framing. Launched = airborne in hitstun or post-hitstun flight.</summary>
    public readonly struct FramingSubject
    {
        public readonly ulong Id;
        public readonly Vector3 Position;
        public readonly bool Launched;
        public FramingSubject(ulong id, Vector3 position, bool launched) { Id = id; Position = position; Launched = launched; }
    }

    /// <summary>Couch play (ADR-0041): one shared camera for every Local Player. Off keeps the player's style.</summary>
    public enum CouchCamera { Off, SideOn, SideOnHigh, Fixed }

    [RequireComponent(typeof(CinemachineCamera))]
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    public class CameraMount : MonoBehaviour
    {
        private CinemachineCamera _cmCam;
        private CinemachineOrbitalFollow _orbital;
        private CinemachineInputAxisController _inputAxisController;

        private Transform _player;
        private Transform _framingAnchor;
        private Vector3 _framingOffset;
        private Vector3? _combatTargetPosition;
        private Bounds _fighterBounds;
        private bool _hasFramingSubjects;
        private float _manualRadius;
        private CameraStyle _style;
        private bool _settleArenaPitch;
        private const float AutoManualGraceSeconds = 1.5f;
        private const float FramingResponse = 4f;
        private const float MaxAutomaticDistance = 60f;
        // Steady (ADR-0043), from the owner's 2026-10-26 prototype pick.
        private const float SteadyPitch = 35f;
        private const float SteadyTurnCone = 60f;
        private const float SteadyHeightBand = 3f;
        private const float SteadyHeightFollow = 1f;
        private const float SteadyLaunchSeconds = 6f;

        [Header("Lock Camera Assistance")]
        [SerializeField] private bool _assistLockedTarget = true;
        [SerializeField, Range(0f, 90f)] private float _lockYawDeadZone = 20f;
        [SerializeField, Min(0f)] private float _lockYawSpeed = 60f;
        [SerializeField, Min(0f)] private float _lockYawResponse = 4f;
        [SerializeField, Min(0f)] private float _lockManualGraceSeconds = 0.75f;
        private Transform? _lockPlayer;
        private Vector3 _lockTargetPosition;
        private float _manualOrbitGraceRemaining;

        [Header("Controller Stick")]
        [Tooltip("Right-stick yaw at full deflection and 100% Stick Look Speed, degrees/second.")]
        [SerializeField, Min(0f)] private float _stickYawSpeed = 300f;
        [Tooltip("Lock assistance dead zone while the last input came from a controller.")]
        [SerializeField, Range(0f, 90f)] private float _controllerLockDeadZone = 8f;
        [SerializeField, Min(0f)] private float _controllerLockSpeed = 200f;
        private float _stickHeldSeconds;
        // Practice session log: manual camera rotation since the last TakeLookSample.
        private float _sampleMouseDegrees;
        private float _sampleStickDegrees;
        private float _sampleStickPeak;

        private CameraMode _mode = CameraMode.Normal;
        private float _frozenYaw;
        private float _frozenPitch = 15f;
        private bool _chatInputSuppressed;
        private float _chatSuppressedYaw;
        private float _chatSuppressedPitch;
        private float _chatSuppressedRadial;
        private float _lastStableYaw;
        private float _lastStablePitch;
        private float _lastStableRadial;
        private bool _hasStableAngles;
        private bool _cameraToggleReleaseRequired;
        private Label _cameraNote;
        public CouchCamera Couch { get; set; }
        private bool _couchYawSet;

        // Steady camera state.
        private readonly List<FramingSubject> _subjects = new();
        private readonly Dictionary<ulong, float> _launchStarted = new();
        private bool _playerGrounded = true;
        private float? _courtNetZ;
        private Vector3? _courtBall;
        private float _groundY;
        private bool _hasGroundY;
        private float _steadyRaise;
        private float _yawVelocity;
        private bool _steadyTurning;
        private OffscreenBubbles? _bubbles;
        public bool HoldsMoveDirection => _mode == CameraMode.Normal && Couch == CouchCamera.Off
            && _style == CameraStyle.Steady && ClientSettingsService.Instance.HoldRunDirection;

        private void Awake()
        {
            _cmCam = GetComponent<CinemachineCamera>();
            _orbital = GetComponent<CinemachineOrbitalFollow>();
            _inputAxisController = GetComponent<CinemachineInputAxisController>();
            if (_inputAxisController != null)
                _inputAxisController.enabled = false;
            if (!TryGetComponent<CameraObstruction>(out _))
                gameObject.AddComponent<CameraObstruction>();
            // Clamp pitch so camera stays above the stage floor level
            if (_orbital != null)
            {
                _manualRadius = _orbital.Radius;
                _orbital.VerticalAxis.Range = new Vector2(0f, 45f);
                _orbital.VerticalAxis.Wrap = false;
                SetCameraPitchDeg(_orbital.VerticalAxis.Value);
            }
        }

        private void OnDestroy()
        {
            _cameraNote?.RemoveFromHierarchy();
            _bubbles?.Dispose();
            if (_framingAnchor != null)
                Destroy(_framingAnchor.gameObject);
        }
        
        /// <summary>
        /// The real Unity Camera that this mount drives.
        /// </summary>
        public UnityEngine.Camera RenderCamera => GetComponentInChildren<UnityEngine.Camera>();
        private void Start()
        {
            SetMode(CameraMode.Normal);
        }

        private void Update()
        {
            if (_orbital == null) return;

            bool suppressCameraInput = ChatInputGate.SuppressGameplay ||
                SettingsOverlay.Active is { IsOpen: true } || SettingsOverlay.ClosedThisFrame ||
                HumanInputActions.IsCapturing || MatchPauseMenu.Active is { IsPaused: true };
            if (suppressCameraInput)
            {
                _cameraToggleReleaseRequired |= HumanInputActions.Get("CameraToggle").IsPressed();
                if (!_chatInputSuppressed)
                {
                    _chatSuppressedYaw = _hasStableAngles ? _lastStableYaw : GetCameraYawDeg();
                    _chatSuppressedPitch = _hasStableAngles ? _lastStablePitch : GetCameraPitchDeg();
                    _chatSuppressedRadial = _hasStableAngles ? _lastStableRadial : _orbital.RadialAxis.Value;
                    _chatInputSuppressed = true;
                }
                SetCameraYawDeg(_chatSuppressedYaw);
                SetCameraPitchDeg(_chatSuppressedPitch);
                _orbital.RadialAxis.Value = _chatSuppressedRadial;
                return;
            }

            if (_chatInputSuppressed)
            {
                if (HumanInputActions.StickLook.ReadValue<Vector2>().sqrMagnitude > 0.01f ||
                    HumanInputActions.MouseLook.ReadValue<Vector2>().sqrMagnitude > 0.01f ||
                    Mathf.Abs(HumanInputActions.ZoomDelta) > 0.01f)
                {
                    SetCameraYawDeg(_chatSuppressedYaw);
                    SetCameraPitchDeg(_chatSuppressedPitch);
                    _orbital.RadialAxis.Value = _chatSuppressedRadial;
                    return;
                }
                _chatInputSuppressed = false;
            }

            // Mouse input is a per-frame delta; stick input is a rate.
            if (_mode == CameraMode.Normal && Couch != CouchCamera.Off)
            {
                var couchToggle = HumanInputActions.Get("CameraToggle");
                if (_cameraToggleReleaseRequired)
                {
                    if (!couchToggle.IsPressed()) _cameraToggleReleaseRequired = false;
                }
                else if (couchToggle.WasPerformedThisFrame())
                {
                    Couch = Couch == CouchCamera.Fixed ? CouchCamera.SideOn : Couch + 1;
                    _couchYawSet = false;
                    ShowCameraNote($"Couch camera: {CouchName(Couch)}");
                }
                ApplyStyle(CameraStyle.Arena);
                _manualOrbitGraceRemaining = 0f;
            }
            else if (_mode == CameraMode.Normal)
            {
                var preferences = ClientSettingsService.Instance;
                var toggle = HumanInputActions.Get("CameraToggle");
                if (_cameraToggleReleaseRequired)
                {
                    if (!toggle.IsPressed()) _cameraToggleReleaseRequired = false;
                }
                else if (toggle.WasPerformedThisFrame())
                {
                    var style = preferences.ToggleCameraStyle(HumanInputActions.LastUsedGamepad);
                    ShowCameraNote($"Camera: {style.DisplayName()}");
                }
                ApplyStyle(preferences.ResolveCameraStyle(HumanInputActions.LastUsedGamepad));
                Vector2 mouseLook = HumanInputActions.MouseLook.ReadValue<Vector2>();
                Vector2 stickLook = HumanInputActions.StickLook.ReadValue<Vector2>();
                // Controllers get a shorter manual grace and stronger lock assistance.
                bool controller = HumanInputActions.LastUsedGamepad;
                bool manualLook = mouseLook.sqrMagnitude > 0.0001f || stickLook.sqrMagnitude > 0.0001f;
                if (manualLook)
                {
                    _manualOrbitGraceRemaining = _style == CameraStyle.Manual
                        ? controller ? _lockManualGraceSeconds * 0.5f : _lockManualGraceSeconds
                        : AutoManualGraceSeconds;
                    _settleArenaPitch = false;
                }
                else
                {
                    _manualOrbitGraceRemaining = Mathf.Max(0f, _manualOrbitGraceRemaining - Time.deltaTime);
                    if (_manualOrbitGraceRemaining <= 0f && _style == CameraStyle.Manual)
                        UpdateLockOrbit(Time.deltaTime, controller);
                }
                var settings = ClientSettingsService.Instance;
                Vector2 stickDegrees = StickDegrees(stickLook, settings.StickLookSpeed, Time.deltaTime);
                Vector2 delta = mouseLook * settings.CameraInputGain + stickDegrees;
                _sampleMouseDegrees += (Mathf.Abs(mouseLook.x) + Mathf.Abs(mouseLook.y)) * settings.CameraInputGain;
                _sampleStickDegrees += Mathf.Abs(stickDegrees.x) + Mathf.Abs(stickDegrees.y);
                _sampleStickPeak = Mathf.Max(_sampleStickPeak, Mathf.Min(1f, stickLook.magnitude));
                _orbital.HorizontalAxis.Value += delta.x * (settings.InvertCameraHorizontal ? -1f : 1f);
                SetCameraPitchDeg(GetCameraPitchDeg() - delta.y * (settings.InvertCameraVertical ? -1f : 1f));

                float zoom = HumanInputActions.ZoomDelta;
                if (Mathf.Abs(zoom) > 0.001f)
                    _orbital.RadialAxis.Value -= zoom * 0.05f;
            }
            else if (_mode == CameraMode.Frozen)
            {
                SetCameraYawDeg(_frozenYaw);
                SetCameraPitchDeg(_frozenPitch);
            }
            else if (_mode == CameraMode.FreeCursor)
            {
                SetCameraYawDeg(_frozenYaw);
                SetCameraPitchDeg(_frozenPitch);
            }

        }

        public static string CouchName(CouchCamera couch) => couch switch
        {
            CouchCamera.SideOn => "Side-on",
            CouchCamera.SideOnHigh => "Side-on high",
            CouchCamera.Fixed => "Fixed",
            _ => "Off",
        };

        private void ShowCameraNote(string text)
        {
            _cameraNote?.RemoveFromHierarchy();
            var root = FindFirstObjectByType<HUDManager>()?.Document?.rootVisualElement;
            if (root == null) return;
            var note = new Label(text)
            {
                name = "camera-status",
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute, left = new Length(50f, LengthUnit.Percent),
                    top = new Length(14f, LengthUnit.Percent), marginLeft = -180f, width = 360f, height = 36f,
                    unityTextAlign = TextAnchor.MiddleCenter, fontSize = 20f, unityFontStyleAndWeight = FontStyle.Bold,
                    color = Color.white, backgroundColor = new Color(0.08f, 0.075f, 0.065f, 0.92f),
                    borderBottomColor = new Color(1f, 0.8f, 34f / 255f), borderBottomWidth = 2f,
                }
            };
            root.Add(note);
            _cameraNote = note;
            note.schedule.Execute(note.RemoveFromHierarchy).StartingIn(1200);
        }

        private void LateUpdate()
        {
            if (_orbital == null) return;

            // PlayerRenderer interpolates in Update. Frame that finished pose before
            // CinemachineBrain's LateUpdate (execution order 100), including the anchors.
            if (_mode == CameraMode.Normal && !_chatInputSuppressed)
                UpdateAutomaticFraming(Time.deltaTime, _manualOrbitGraceRemaining <= 0f);

            // Frozen/aim/menu modes retain their offsets while following the player.
            if (_framingAnchor != null && _player != null)
                _framingAnchor.position = _player.position + _framingOffset;

            if (_chatInputSuppressed)
            {
                // Cinemachine may process its axes after Update; restore the last stable
                // pose again so the opening frame cannot move the camera.
                SetCameraYawDeg(_chatSuppressedYaw);
                SetCameraPitchDeg(_chatSuppressedPitch);
                _orbital.RadialAxis.Value = _chatSuppressedRadial;
                return;
            }

            UpdateBubbles();
            _lastStableYaw = GetCameraYawDeg();
            _lastStablePitch = GetCameraPitchDeg();
            _lastStableRadial = _orbital.RadialAxis.Value;
            _hasStableAngles = true;
        }
        public void SetMode(CameraMode mode)
        {
            _mode = mode;
            if (mode != CameraMode.Normal)
                _manualOrbitGraceRemaining = _lockManualGraceSeconds;
            switch (mode)
            {
                case CameraMode.Normal:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    break;
                case CameraMode.Frozen:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    break;
                case CameraMode.FreeCursor:
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    break;
                case CameraMode.Aiming:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    // Freeze orbital at current angles so it's ready to blend back to
                    // the right position when aiming ends.
                    FreezeAtCurrentAngles();
                    break;
            }
        }

        public void FreezeAtCurrentAngles()
        {
            _frozenYaw = GetCameraYawDeg();
            _frozenPitch = GetCameraPitchDeg();
        }

        /// <summary>
        /// Accumulate mouse delta into the frozen camera orbit angles.
        /// Only meaningful in Frozen mode — updates _frozenYaw and _frozenPitch.
        /// deltaDeg = delta pixels * sensitivity (already scaled).
        /// </summary>
        public void OrbitFrozen(Vector2 deltaDeg)
        {
            if (_mode != CameraMode.Frozen || _orbital == null) return;
            var settings = ClientSettingsService.Instance;
            deltaDeg *= settings.CameraInputGain;
            _frozenYaw += deltaDeg.x * (settings.InvertCameraHorizontal ? -1f : 1f);
            _frozenPitch -= deltaDeg.y * (settings.InvertCameraVertical ? -1f : 1f);
            _frozenPitch = _orbital.VerticalAxis.ClampValue(_frozenPitch);
        }


        public void SetTarget(Transform target)
        {
            _player = target;
            ApplyTrackingTarget();
        }

        private void ApplyTrackingTarget()
        {
            if (_cmCam == null || _player == null) return;
            Transform target = _style == CameraStyle.Manual ? _player : _framingAnchor;
            if (target == null) return;
            _cmCam.Target = new CameraTarget { TrackingTarget = target, LookAtTarget = target };
        }

        /// <summary>Presentation-only subjects from the current Shared match state.</summary>
        public void SetFramingSubjects(Vector3 playerPosition, Vector3? targetPosition, Bounds fighterBounds,
            bool playerGrounded = true, IReadOnlyList<FramingSubject>? opponents = null)
        {
            _combatTargetPosition = targetPosition;
            _playerGrounded = playerGrounded;
            _subjects.Clear();
            if (opponents != null) _subjects.AddRange(opponents);
            // The rig follows the rendered player, including interpolation/correction.
            _fighterBounds = fighterBounds;
            _fighterBounds.center -= playerPosition;
            _hasFramingSubjects = true;
        }

        /// <summary>Slopball courts: the net plane (z) and the ball, framed by the Steady camera.</summary>
        public void SetCourt(float? netZ, Vector3? ball)
        {
            _courtNetZ = netZ;
            _courtBall = ball;
        }

        private void ApplyStyle(CameraStyle style)
        {
            if (_style == style) return;
            _style = style;
            _hasGroundY = false;
            _yawVelocity = 0f;
            _steadyTurning = false;
            _steadyRaise = 0f;
            _manualOrbitGraceRemaining = 0f;
            _settleArenaPitch = style == CameraStyle.Arena;
            if (style != CameraStyle.Manual && _framingAnchor == null)
            {
                _framingAnchor = new GameObject("Camera Framing") { hideFlags = HideFlags.DontSave }.transform;
                _framingAnchor.position = _player != null ? _player.position : Vector3.zero;
            }
            if (style == CameraStyle.Manual)
            {
                _framingOffset = Vector3.zero;
                _orbital.Radius = _manualRadius;
            }
            ApplyTrackingTarget();
        }

        private void UpdateAutomaticFraming(float deltaTime, bool allowRecenter)
        {
            if (_style == CameraStyle.Manual || _player == null) return;
            if (Couch != CouchCamera.Off)
            {
                UpdateCouchFraming(deltaTime);
                return;
            }
            if (_style == CameraStyle.Steady)
            {
                UpdateSteadyFraming(deltaTime, allowRecenter);
                return;
            }
            // Arena: frame every fighter from a settled, elevated pitch; the direction stays the player's.
            Vector3 player = _player.position;
            var bounds = _hasFramingSubjects
                ? new Bounds(player + _fighterBounds.center, _fighterBounds.size) : new Bounds(player, Vector3.zero);
            if (!_hasFramingSubjects && _combatTargetPosition.HasValue)
                bounds.Encapsulate(_combatTargetPosition.Value);
            if (_lockPlayer != null)
                bounds.Encapsulate(_lockTargetPosition);
            // Leave room for bodies, weapons, jumps and the target indicator.
            bounds.Expand(new Vector3(3f, 4f, 3f));
            if (_settleArenaPitch && allowRecenter)
            {
                SetCameraPitchDeg(Mathf.MoveTowards(GetCameraPitchDeg(), 35f, 60f * deltaTime));
                if (Mathf.Abs(GetCameraPitchDeg() - 35f) < 0.01f) _settleArenaPitch = false;
            }
            float blend = 1f - Mathf.Exp(-FramingResponse * deltaTime);
            _framingOffset = Vector3.Lerp(_framingOffset, bounds.center - player, blend);
            Vector3 anchor = player + _framingOffset;
            var orientation = Quaternion.Euler(GetCameraPitchDeg(), GetCameraYawDeg(), 0f);
            var renderCamera = RenderCamera;
            float aspect = renderCamera != null ? renderCamera.aspect : Screen.width / (float)Mathf.Max(1, Screen.height);
            float radial = Mathf.Max(0.1f, _orbital.RadialAxis.Value);
            float distance = DistanceToFrame(bounds, anchor, orientation, _cmCam.Lens.FieldOfView, aspect);
            float radius = Mathf.Clamp(distance / radial, _manualRadius, MaxAutomaticDistance / radial);
            // Zoom out promptly for launches; ease back in after fighters close the gap.
            _orbital.Radius = Mathf.Lerp(_orbital.Radius, radius,
                1f - Mathf.Exp(-(radius > _orbital.Radius ? 10f : 2f) * deltaTime));
        }

        /// <summary>
        /// Steady (ADR-0043): high and centred on the player at a fixed distance. Turns only when
        /// the target leaves a wide cone, with eased speed; on a Slopball court it looks down the
        /// court instead. Height follows landings, not ordinary jumps; a launched fighter or a high
        /// ball lifts the focus, and off-screen bubbles cover what the view still cannot hold.
        /// </summary>
        private void UpdateSteadyFraming(float deltaTime, bool allowRecenter)
        {
            Vector3 player = _player.position;
            if (!_hasGroundY) { _groundY = player.y; _hasGroundY = true; }
            float above = player.y - _groundY;
            float groundGoal = _playerGrounded ? player.y
                : above > SteadyHeightBand ? player.y - SteadyHeightBand
                : above < -.5f ? player.y + .5f : _groundY;
            _groundY = Mathf.Lerp(_groundY, groundGoal, 1f - Mathf.Exp(-SteadyHeightFollow * deltaTime));

            bool court = _courtNetZ.HasValue;
            Vector3? focus = court ? _courtBall : _lockPlayer != null ? _lockTargetPosition : _combatTargetPosition;
            // Your own jumps stay inside the height band; only other subjects lift the view.
            float highest = _groundY + 2f;
            if (focus.HasValue) highest = Mathf.Max(highest, focus.Value.y + 1.5f);
            float now = Time.time;
            foreach (var subject in _subjects)
            {
                if (!subject.Launched)
                {
                    _launchStarted.Remove(subject.Id);
                    continue;
                }
                if (!_launchStarted.TryGetValue(subject.Id, out float started))
                    _launchStarted[subject.Id] = started = now;
                Vector3 offset = subject.Position - player;
                if (now - started <= SteadyLaunchSeconds && offset.x * offset.x + offset.z * offset.z < 400f)
                    highest = Mathf.Max(highest, subject.Position.y + 1.5f);
            }
            float height = Mathf.Max(0f, highest - (_groundY + 2f));

            float yaw = GetCameraYawDeg();
            float? desiredYaw = null;
            if (court && allowRecenter)
                desiredYaw = player.z < _courtNetZ!.Value ? 0f : 180f;
            else if (focus.HasValue && allowRecenter)
            {
                Vector3 offset = focus.Value - player;
                // Within 3 m (scrambles, cross-ups) no new turn starts.
                if (offset.x * offset.x + offset.z * offset.z > 9f)
                {
                    float targetYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                    float error = Mathf.DeltaAngle(yaw, targetYaw);
                    float settle = SteadyTurnCone * .4f;
                    if (Mathf.Abs(error) > SteadyTurnCone) _steadyTurning = true;
                    else if (Mathf.Abs(error) <= settle) _steadyTurning = false;
                    if (_steadyTurning) desiredYaw = targetYaw - Mathf.Sign(error) * settle;
                }
            }
            if (!allowRecenter) _steadyTurning = false;
            float turnSpeed = ClientSettingsService.Instance.CameraTurnSpeed;
            float rate = court ? Mathf.Max(turnSpeed, 90f) : turnSpeed;
            float wanted = desiredYaw.HasValue
                ? Mathf.Clamp(Mathf.DeltaAngle(yaw, desiredYaw.Value) * 1.5f, -rate, rate) : 0f;
            // Ease in and out: the turn speed itself is smoothed.
            _yawVelocity = Mathf.Lerp(_yawVelocity, wanted, 1f - Mathf.Exp(-3f * deltaTime));
            if (!allowRecenter) _yawVelocity = 0f;
            SetCameraYawDeg(yaw + _yawVelocity * deltaTime);
            if (allowRecenter)
                SetCameraPitchDeg(Mathf.LerpAngle(GetCameraPitchDeg(), SteadyPitch, 1f - Mathf.Exp(-3f * deltaTime)));

            _steadyRaise = Mathf.Lerp(_steadyRaise, Mathf.Min(height * .4f, 6f),
                1f - Mathf.Exp(-(height * .4f > _steadyRaise ? 6f : 2f) * deltaTime));
            // Vertical position is absolute (ground reference), so a jump does not bob the view;
            // the fighter's root sits near its chest, so this centres the body on screen.
            _framingOffset = new Vector3(0f, _groundY + .2f + _steadyRaise - player.y, 0f);
            _orbital.Radius = Mathf.Lerp(_orbital.Radius, _manualRadius, 1f - Mathf.Exp(-1.5f * deltaTime));
        }

        private void UpdateBubbles()
        {
            bool show = _mode == CameraMode.Normal && _style == CameraStyle.Steady && Couch == CouchCamera.Off
                && _player != null;
            if (!show)
            {
                _bubbles?.Hide();
                return;
            }
            _bubbles ??= new OffscreenBubbles();
            var camera = RenderCamera;
            var root = FindFirstObjectByType<HUDManager>()?.Document?.rootVisualElement;
            if (camera == null || root == null) return;
            _bubbles.Begin(root);
            foreach (var subject in _subjects)
                _bubbles.Show(camera, subject.Position + Vector3.up, subject.Position.y - _groundY,
                    _player!.position, OffscreenBubbles.FighterColor);
            _bubbles.End();
        }

        private void UpdateCouchFraming(float deltaTime)
        {
            Vector3 player = _player.position;
            Vector3 line = Vector3.zero;
            if (_combatTargetPosition.HasValue)
            {
                line = _combatTargetPosition.Value - player;
                line.y = 0f;
            }
            float yaw = GetCameraYawDeg();
            float sideYaw = yaw;
            if (line.sqrMagnitude > 0.25f)
            {
                // Look across the fighters' line from the side nearer the current view, so a cross-up never flips it.
                float lineYaw = Mathf.Atan2(line.x, line.z) * Mathf.Rad2Deg;
                sideYaw = Mathf.Abs(Mathf.DeltaAngle(yaw, lineYaw + 90f)) <= Mathf.Abs(Mathf.DeltaAngle(yaw, lineYaw - 90f))
                    ? lineYaw + 90f : lineYaw - 90f;
            }
            if (Couch == CouchCamera.Fixed)
            {
                if (!_couchYawSet && line.sqrMagnitude > 0.25f)
                {
                    SetCameraYawDeg(sideYaw);
                    _couchYawSet = true;
                }
            }
            else
                SetCameraYawDeg(GetAssistedYaw(yaw, Quaternion.Euler(0f, sideYaw, 0f) * Vector3.forward,
                    8f, 3f, 90f, deltaTime));
            float pitch = Couch switch { CouchCamera.SideOnHigh => 40f, CouchCamera.Fixed => 30f, _ => 20f };
            float blend = 1f - Mathf.Exp(-FramingResponse * deltaTime);
            SetCameraPitchDeg(Mathf.LerpAngle(GetCameraPitchDeg(), pitch, blend));

            // Fit a box aligned with the view; a world box around a diagonal pair is far too wide.
            var view = Quaternion.Euler(0f, GetCameraYawDeg(), 0f);
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            if (_combatTargetPosition.HasValue)
                bounds.Encapsulate(Quaternion.Inverse(view) * (_combatTargetPosition.Value - player));
            bounds.Expand(new Vector3(3f, 4f, 3f));
            _framingOffset = Vector3.Lerp(_framingOffset, view * bounds.center, blend);
            var renderCamera = RenderCamera;
            float aspect = renderCamera != null ? renderCamera.aspect : Screen.width / (float)Mathf.Max(1, Screen.height);
            float radial = Mathf.Max(0.1f, _orbital.RadialAxis.Value);
            float distance = DistanceToFrame(bounds, Quaternion.Inverse(view) * _framingOffset,
                Quaternion.Euler(GetCameraPitchDeg(), 0f, 0f), _cmCam.Lens.FieldOfView, aspect);
            float radius = Mathf.Clamp(distance / radial, _manualRadius, MaxAutomaticDistance / radial);
            _orbital.Radius = Mathf.Lerp(_orbital.Radius, radius,
                1f - Mathf.Exp(-(radius > _orbital.Radius ? 10f : 2f) * deltaTime));
        }

        internal static float DistanceToFrame(Bounds bounds, Vector3 anchor, Quaternion orientation,
            float verticalFov, float aspect)
        {
            float vertical = Mathf.Tan(Mathf.Clamp(verticalFov, 1f, 179f) * 0.5f * Mathf.Deg2Rad) * 0.8f;
            float horizontal = vertical * Mathf.Max(0.1f, aspect);
            var inverse = Quaternion.Inverse(orientation);
            float distance = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                var local = inverse * (point - anchor);
                distance = Mathf.Max(distance, Mathf.Max(Mathf.Abs(local.x) / horizontal - local.z,
                    Mathf.Abs(local.y) / vertical - local.z));
            }
            return distance;
        }

        /// <summary>
        /// Track the authoritative locked target. Manual keeps the player anchor;
        /// automatic styles can include this subject in their framing.
        /// </summary>
        public void SetLockFocus(Transform player, Vector3 targetPos)
        {
            if (_cmCam == null || player == null) return;
            _lockPlayer = player;
            _lockTargetPosition = targetPos;
            SetTarget(player);
        }

        /// <summary>
        /// Clear lock assistance. Automatic framing can still use the match subjects.
        /// </summary>
        public void ClearLockFocus(Transform player)
        {
            _lockPlayer = null;
            if (player != null) SetTarget(player);
        }


        /// <summary>
        /// Snap orbit to face the target from behind at a comfortable angle.
        /// Call after SetTarget to avoid the camera starting at a random orientation.
        /// </summary>
        public void ResetView(Transform target)
        {
            if (_orbital == null) return;
            _orbital.HorizontalAxis.Value = target.eulerAngles.y;
            SetCameraPitchDeg(15f);
            _framingOffset = Vector3.zero;
            _combatTargetPosition = null;
            _hasFramingSubjects = false;
            _manualOrbitGraceRemaining = 0f;
            _settleArenaPitch = _style == CameraStyle.Arena;
            _hasGroundY = false;
            _yawVelocity = 0f;
            _steadyTurning = false;
            _launchStarted.Clear();
            if (_framingAnchor != null) _framingAnchor.position = target.position;
            _orbital.Radius = _manualRadius;
        }

        /// <summary>Cut to the target's new position on the next update (Arcade retry) instead of damping across the map.</summary>
        public void Cut()
        {
            if (_cmCam != null) _cmCam.PreviousStateIsValid = false;
        }


        public float GetCameraYawDeg()
        {
            return _orbital != null ? _orbital.HorizontalAxis.Value : 0f;
        }

        public void SetCameraYawDeg(float yawDeg)
        {
            if (_orbital != null)
                _orbital.HorizontalAxis.Value = yawDeg;
        }

        public void SetCameraPitchDeg(float pitchDeg)
        {
            if (_orbital != null)
                // Spherical orbit uses Value directly, so Range alone cannot prevent a flip.
                _orbital.VerticalAxis.Value = _orbital.VerticalAxis.ClampValue(pitchDeg);
        }

        public float GetCameraPitchDeg()
        {
            return _orbital != null ? _orbital.VerticalAxis.Value : 0f;
        }
        public float GetOrbitRadius()
        {
            if (_orbital == null) return 2.5f;
            // Actual camera distance = base Radius multiplied by scroll-adjusted RadialAxis
            return _orbital.Radius * _orbital.RadialAxis.Value;
        }
        /// <summary>Group framing must not pull the dedicated ranged-aim camera farther away.</summary>
        public float GetAimOrbitRadius()
        {
            return _orbital != null ? _manualRadius * _orbital.RadialAxis.Value : 2.5f;
        }
        public float GetCameraYawRad()
        {
            return GetCameraYawDeg() * Mathf.Deg2Rad;
        }

        public Vector3 GetForwardDirection()
        {
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            return fwd.normalized;
        }

        public Vector3 GetRightDirection()
        {
            Vector3 right = transform.right;
            right.y = 0f;
            return right.normalized;
        }

        public CameraMode Mode => _mode;
        public CameraStyle Style => _style;
        public bool AssistsLockedTarget => _assistLockedTarget;
        public float LockYawDeadZoneDegrees => _lockYawDeadZone;
        public float LockYawSpeedDegrees => _lockYawSpeed;
        public Vector2 PitchRangeDegrees => _orbital != null ? _orbital.VerticalAxis.Range : Vector2.zero;
        /// <summary>Right-stick yaw at full deflection with the current Stick Look Speed, degrees/second.</summary>
        public float StickYawRateDegrees => _stickYawSpeed * ClientSettingsService.Instance.StickLookSpeed;

        /// <summary>
        /// Manual camera rotation applied since the previous call: mouse and right-stick
        /// degrees (yaw + pitch) and the largest stick deflection. Resets the accumulators.
        /// </summary>
        public (float mouseDegrees, float stickDegrees, float stickPeak) TakeLookSample()
        {
            var sample = (_sampleMouseDegrees, _sampleStickDegrees, _sampleStickPeak);
            _sampleMouseDegrees = _sampleStickDegrees = _sampleStickPeak = 0f;
            return sample;
        }

        /// <summary>
        /// Right-stick orbit: precise near the centre (exponential curve) and fast at full
        /// deflection, ramping to top speed after a quarter second held near the edge.
        /// </summary>
        private Vector2 StickDegrees(Vector2 stick, float speed, float deltaTime)
        {
            float magnitude = Mathf.Min(1f, stick.magnitude);
            if (magnitude < 0.0001f)
            {
                _stickHeldSeconds = 0f;
                return Vector2.zero;
            }
            _stickHeldSeconds = magnitude > 0.9f ? _stickHeldSeconds + deltaTime : 0f;
            float ramp = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(_stickHeldSeconds / 0.25f));
            float rate = _stickYawSpeed * speed * Mathf.Pow(magnitude, 1.8f) * ramp;
            Vector2 direction = stick / stick.magnitude;
            return new Vector2(direction.x * rate, direction.y * rate * 0.6f) * deltaTime;
        }

        private void UpdateLockOrbit(float deltaTime, bool controller)
        {
            if (!_assistLockedTarget || _lockPlayer == null) return;
            float yaw = GetCameraYawDeg();
            SetCameraYawDeg(GetAssistedYaw(yaw, _lockTargetPosition - _lockPlayer.position,
                controller ? _controllerLockDeadZone : _lockYawDeadZone,
                controller ? _lockYawResponse * 2f : _lockYawResponse,
                controller ? _controllerLockSpeed : _lockYawSpeed, deltaTime));
        }

        private static float GetAssistedYaw(float yaw, Vector3 offset, float deadZone,
            float response, float speed, float deltaTime)
        {
            // Do not swing the movement basis around when fighters overlap/cross up.
            if (offset.x * offset.x + offset.z * offset.z < 0.25f) return yaw;
            float targetYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            float error = Mathf.DeltaAngle(yaw, targetYaw);
            if (Mathf.Abs(error) <= deadZone) return yaw;
            float correction = (error - Mathf.Sign(error) * deadZone)
                * (1f - Mathf.Exp(-response * deltaTime));
            float maxStep = speed * deltaTime;
            return yaw + Mathf.Clamp(correction, -maxStep, maxStep);
        }
    }
}
