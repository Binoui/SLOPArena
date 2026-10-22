using UnityEngine;
using Unity.Cinemachine;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
namespace SlopArena.Client.Camera
{
    public enum CameraMode
    {
        Normal,       // Cursor locked, camera orbits freely
        Frozen,       // Cursor locked, camera yaw/pitch held constant
        FreeCursor,   // Cursor unlocked, camera yaw/pitch held constant
        Aiming,       // Cursor locked, orbital camera frozen, AimCameraMount drives aim camera
    }

    [RequireComponent(typeof(CinemachineCamera))]
    [RequireComponent(typeof(CinemachineOrbitalFollow))]
    public class CameraMount : MonoBehaviour
    {
        private CinemachineCamera _cmCam;
        private CinemachineOrbitalFollow _orbital;
        private CinemachineInputAxisController _inputAxisController;

        private Transform _player;
        private Transform _framingAnchor;
        private Transform _lookAnchor;
        private Vector3 _framingOffset;
        private Vector3 _lookOffset;
        private Vector3? _combatTargetPosition;
        private Bounds _fighterBounds;
        private bool _hasFramingSubjects;
        private float _manualRadius;
        private CameraStyle _style;
        private bool _settleArenaPitch;
        private bool _trackingHeight;
        private float _opponentHeight;
        private float _duelSide = 1f;
        private const float AutoManualGraceSeconds = 1.5f;
        private const float FramingResponse = 4f;
        private const float MaxAutomaticDistance = 60f;
        private bool TracksOpponentHeight => _style is CameraStyle.AirDuel or CameraStyle.SideDuel or CameraStyle.Shoulder;

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
            if (_framingAnchor != null)
                Destroy(_framingAnchor.gameObject);
            if (_lookAnchor != null)
                Destroy(_lookAnchor.gameObject);
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
            if (_mode == CameraMode.Normal)
            {
                ApplyStyle(ClientSettingsService.Instance.CameraStyle);
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
            if (_lookAnchor != null && _player != null)
                _lookAnchor.position = _player.position + _lookOffset;

            if (_chatInputSuppressed)
            {
                // Cinemachine may process its axes after Update; restore the last stable
                // pose again so the opening frame cannot move the camera.
                SetCameraYawDeg(_chatSuppressedYaw);
                SetCameraPitchDeg(_chatSuppressedPitch);
                _orbital.RadialAxis.Value = _chatSuppressedRadial;
                return;
            }

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
            _cmCam.Target = new CameraTarget
            {
                TrackingTarget = target,
                LookAtTarget = TracksOpponentHeight ? _lookAnchor : target,
                CustomLookAtTarget = TracksOpponentHeight
            };
        }

        /// <summary>Presentation-only subjects from the current Shared match state.</summary>
        public void SetFramingSubjects(Vector3 playerPosition, Vector3? targetPosition, Bounds fighterBounds)
        {
            _combatTargetPosition = targetPosition;
            // The rig follows the rendered player, including interpolation/correction.
            _fighterBounds = fighterBounds;
            _fighterBounds.center -= playerPosition;
            _hasFramingSubjects = true;
        }

        private void ApplyStyle(CameraStyle style)
        {
            if (_style == style) return;
            _style = style;
            _manualOrbitGraceRemaining = 0f;
            _settleArenaPitch = style == CameraStyle.Arena;
            _trackingHeight = false;
            _opponentHeight = 0f;
            if (style != CameraStyle.Manual && _framingAnchor == null)
            {
                _framingAnchor = new GameObject("Camera Framing") { hideFlags = HideFlags.DontSave }.transform;
                _framingAnchor.position = _player != null ? _player.position : Vector3.zero;
            }
            if (TracksOpponentHeight && _lookAnchor == null)
                _lookAnchor = new GameObject("Camera Look Focus") { hideFlags = HideFlags.DontSave }.transform;
            _lookOffset = _framingOffset;
            if (_lookAnchor != null && _player != null)
                _lookAnchor.position = _player.position + _lookOffset;
            if (style == CameraStyle.SideDuel)
            {
                Vector3? opponent = _lockPlayer != null ? _lockTargetPosition : _combatTargetPosition;
                if (_player != null && opponent.HasValue)
                {
                    Vector3 offset = opponent.Value - _player.position;
                    float targetYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                    _duelSide = Mathf.Abs(Mathf.DeltaAngle(GetCameraYawDeg(), targetYaw + 75f))
                        <= Mathf.Abs(Mathf.DeltaAngle(GetCameraYawDeg(), targetYaw - 75f)) ? 1f : -1f;
                }
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
            Vector3 player = _player.position;
            // Locked objectives (Slopball/course targets) use the same camera path.
            Vector3? opponent = _lockPlayer != null ? _lockTargetPosition : _combatTargetPosition;
            var bounds = new Bounds(player, Vector3.zero);
            if (_style == CameraStyle.Arena && _hasFramingSubjects)
                bounds = new Bounds(player + _fighterBounds.center, _fighterBounds.size);
            else if (opponent.HasValue)
                bounds.Encapsulate(opponent.Value);
            if (_style == CameraStyle.Arena && _lockPlayer != null)
                bounds.Encapsulate(_lockTargetPosition);

            // Leave room for bodies, weapons, jumps and the target indicator.
            bounds.Expand(new Vector3(3f, 4f, 3f));
            Vector3 center = bounds.center;
            if (_style == CameraStyle.AutoCombat)
            {
                center = opponent.HasValue ? Vector3.Lerp(player, opponent.Value, 0.35f) : player;
                if (allowRecenter)
                {
                    if (opponent.HasValue)
                        SetCameraYawDeg(GetAssistedYaw(GetCameraYawDeg(), opponent.Value - player,
                            8f, 3f, 120f, deltaTime));
                    SetCameraPitchDeg(Mathf.LerpAngle(GetCameraPitchDeg(), 20f,
                        1f - Mathf.Exp(-FramingResponse * deltaTime)));
                }
            }
            else if (_settleArenaPitch && allowRecenter)
            {
                SetCameraPitchDeg(Mathf.MoveTowards(GetCameraPitchDeg(), 35f, 60f * deltaTime));
                if (Mathf.Abs(GetCameraPitchDeg() - 35f) < 0.01f) _settleArenaPitch = false;
            }
            if (TracksOpponentHeight)
            {
                bool shoulder = _style == CameraStyle.Shoulder;
                if (allowRecenter)
                {
                    float height = opponent.HasValue ? opponent.Value.y - player.y : 0f;
                    float threshold = shoulder ? 0.75f : 2.5f;
                    _trackingHeight = Mathf.Abs(height) > (_trackingHeight ? threshold * 0.5f : threshold);
                    _opponentHeight = Mathf.Lerp(_opponentHeight, _trackingHeight ? height : 0f,
                        1f - Mathf.Exp(-3f * deltaTime));
                    if (opponent.HasValue)
                    {
                        Vector3 offset = opponent.Value - player;
                        if (_style == CameraStyle.SideDuel)
                            offset = Quaternion.Euler(0f, _duelSide * 75f, 0f) * offset;
                        SetCameraYawDeg(GetAssistedYaw(GetCameraYawDeg(), offset,
                            shoulder ? 3f : 8f, 3f, _style == CameraStyle.SideDuel ? 90f : 120f, deltaTime));
                    }
                    float pitch = shoulder ? 5f : Mathf.Lerp(20f, 8f, Mathf.Clamp01(_opponentHeight / 6f));
                    SetCameraPitchDeg(Mathf.LerpAngle(GetCameraPitchDeg(), pitch,
                        1f - Mathf.Exp(-FramingResponse * deltaTime)));
                }
                center = shoulder ? player : opponent.HasValue ? Vector3.Lerp(player, opponent.Value, 0.5f) : player;
                // Follow near the player's height; only the look target rises with the
                // opponent. Looking up must never place the camera under the floor.
                center.y = player.y + (shoulder ? 1.5f : 0.75f);
                if (shoulder)
                    center += Quaternion.Euler(0f, GetCameraYawDeg(), 0f) * Vector3.right * 1.1f;
            }

            float blend = 1f - Mathf.Exp(-FramingResponse * deltaTime);
            _framingOffset = Vector3.Lerp(_framingOffset, center - player, blend);
            _lookOffset = _framingOffset + Vector3.up * (_opponentHeight * (_style == CameraStyle.Shoulder ? 0.65f : 0.5f));
            Vector3 anchor = player + _framingOffset;
            var orientation = Quaternion.Euler(GetCameraPitchDeg(), GetCameraYawDeg(), 0f);
            var renderCamera = RenderCamera;
            float aspect = renderCamera != null ? renderCamera.aspect : Screen.width / (float)Mathf.Max(1, Screen.height);
            float radial = Mathf.Max(0.1f, _orbital.RadialAxis.Value);
            float minimumRadius = _manualRadius * (_style == CameraStyle.Shoulder ? 0.45f
                : TracksOpponentHeight ? 0.85f : 1f);
            float distance = TracksOpponentHeight
                ? DistanceToFrameWithLookAt(bounds, anchor, player + _lookOffset, orientation,
                    _cmCam.Lens.FieldOfView, aspect, minimumRadius * radial, MaxAutomaticDistance)
                : DistanceToFrame(bounds, anchor, orientation, _cmCam.Lens.FieldOfView, aspect);
            float radius = Mathf.Clamp(distance / radial, minimumRadius, MaxAutomaticDistance / radial);
            // Zoom out promptly for launches; ease back in after fighters close the gap.
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

        internal static float DistanceToFrameWithLookAt(Bounds bounds, Vector3 anchor, Vector3 lookAt,
            Quaternion orbit, float verticalFov, float aspect, float minimum, float maximum)
        {
            // A separate look target changes the actual view angle as the boom moves.
            // Find the nearest distance that contains the padded fighters in that view.
            bool Fits(float distance)
            {
                Vector3 eye = anchor - orbit * Vector3.forward * distance;
                Vector3 direction = lookAt - eye;
                Quaternion rotation = direction.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(direction, Vector3.up) : orbit;
                return DistanceToFrame(bounds, eye, rotation, verticalFov, aspect) <= 0.0001f;
            }
            minimum = Mathf.Clamp(minimum, 0.1f, maximum);
            if (Fits(minimum)) return minimum;
            float low = minimum, high = maximum;
            for (int step = 0; step < 12; step++)
            {
                float middle = (low + high) * 0.5f;
                if (Fits(middle)) high = middle;
                else low = middle;
            }
            return high;
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
            _trackingHeight = false;
            _opponentHeight = 0f;
            _lookOffset = Vector3.zero;
            if (_framingAnchor != null) _framingAnchor.position = target.position;
            if (_lookAnchor != null) _lookAnchor.position = target.position;
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
