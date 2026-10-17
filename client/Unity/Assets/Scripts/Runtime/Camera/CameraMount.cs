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
                _orbital.VerticalAxis.Range = new Vector2(0f, 45f);
                _orbital.VerticalAxis.Wrap = false;
                SetCameraPitchDeg(_orbital.VerticalAxis.Value);
            }
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
                    Mathf.Abs(HumanInputActions.Zoom.ReadValue<float>()) > 0.01f)
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
                Vector2 mouseLook = HumanInputActions.MouseLook.ReadValue<Vector2>();
                Vector2 stickLook = HumanInputActions.StickLook.ReadValue<Vector2>();
                // Controllers get a shorter manual grace and stronger lock assistance.
                bool controller = HumanInputActions.LastUsedGamepad;
                if (mouseLook.sqrMagnitude > 0f || stickLook.sqrMagnitude > 0f)
                    _manualOrbitGraceRemaining = controller ? _lockManualGraceSeconds * 0.5f : _lockManualGraceSeconds;
                else
                {
                    _manualOrbitGraceRemaining = Mathf.Max(0f, _manualOrbitGraceRemaining - Time.deltaTime);
                    if (_manualOrbitGraceRemaining <= 0f)
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

                float zoom = HumanInputActions.Zoom.ReadValue<float>();
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
            _cmCam.Target = new CameraTarget
            {
                TrackingTarget = target,
                LookAtTarget = target
            };
        }

        /// <summary>
        /// Track the authoritative locked target without moving the orbit or look
        /// anchor away from the player. Called every simulation tick while locked.
        /// </summary>
        public void SetLockFocus(Transform player, Vector3 targetPos)
        {
            if (_cmCam == null || player == null) return;
            _lockPlayer = player;
            _lockTargetPosition = targetPos;
            SetTarget(player);
        }

        /// <summary>
        /// Restore the camera to follow and look at the player (unlocked).
        /// Safe to call every tick.
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
