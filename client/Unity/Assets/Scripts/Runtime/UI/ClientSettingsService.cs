using System;
using SlopArena.Shared;
using System.Collections.Generic;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

namespace SlopArena.Client.UI
{
    /// <summary>Persistent owner of player preferences. UI surfaces never write PlayerPrefs.</summary>
    public sealed class ClientSettingsService : MonoBehaviour
    {
        private const string PrefsKey = "sloparena.client-settings.v1";
        private const int RumbleStopAttempts = 3;
        private static ClientSettingsService? _instance;
        [SerializeField] private AudioMixer? _mixer;
        private SettingsPayload _settings = SettingsPayload.Defaults;
        private bool _dirty;
        private bool _unfocused;
        private int _lastUiSceneHandle = -1;
        private readonly Dictionary<PanelSettings, float> _panelBaseScales = new();
        private readonly List<Gamepad> _rumblingPads = new();
        private float _rumbleUntil;
        private int _rumbleStopAttempts;

        [Serializable]
        private sealed class SettingsPayload
        {
            public static SettingsPayload Defaults => new SettingsPayload();
            public int version = 1;
            public float master = 100f;
            public float music = 70f;
            public float sfx = 100f;
            public float ui = 80f;
            public bool muteWhenUnfocused;
            public string bindingOverrides = "";
            public float stickDeadzone = 12.5f;
            public bool vibration = true;
            public float cameraSensitivity = 100f;
            public float stickLookSpeed = 100f;
            public int cameraStyle = (int)CameraStyle.Steady;
            public int controllerCameraStyle = -1;
            public int mouseAutoCameraStyle = (int)CameraStyle.Steady;
            public int controllerAutoCameraStyle = (int)CameraStyle.Steady;
            // 0: saved before ADR-0043; such saves move once to Steady on both devices.
            public int cameraRevision;
            public float cameraTurnSpeed = 20f;
            public bool holdRunDirection = true;
            public bool invertCameraHorizontal;
            public bool invertCameraVertical;
            public int displayMode = (int)FullScreenMode.Windowed;
            public int resolutionWidth;
            public int resolutionHeight;
            public int quality = 2;
            public bool vSync = true;
            public int targetFps = 120;
            public int uiScale = 100;
            public int networkStats;
            public float targetOpacity = 100f;
            public float screenShake = 100f;
            public bool reducedFlashing;
            public bool showOverheadDamage = true;
            public int targetLockMode = (int)TargetLockMode.Always;
            public bool recordSessionLog;
        }

        public static ClientSettingsService Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var host = new GameObject(nameof(ClientSettingsService));
                return host.AddComponent<ClientSettingsService>();
            }
        }

        public float Master => _settings.master;
        public float Music => _settings.music;
        public float Sfx => _settings.sfx;
        public float Ui => _settings.ui;
        public bool MuteWhenUnfocused => _settings.muteWhenUnfocused;
        public string BindingOverrides => _settings.bindingOverrides;
        public float StickDeadzone => _settings.stickDeadzone;
        public bool Vibration => _settings.vibration;
        public float CameraSensitivity => _settings.cameraSensitivity / 100f;
        // The manual delta-driven camera path runs at half the former default gain.
        public float CameraInputGain => CameraSensitivity * 0.5f;
        /// <summary>Right-stick camera speed multiplier (1 = default); the mouse uses <see cref="CameraSensitivity"/>.</summary>
        public float StickLookSpeed => _settings.stickLookSpeed / 100f;
        /// <summary>Stick-driven aim camera and cursor gain, on the same half scale as <see cref="CameraInputGain"/>.</summary>
        public float StickInputGain => StickLookSpeed * 0.5f;
        public CameraStyle CameraStyle => (CameraStyle)_settings.cameraStyle;
        public CameraStyle ControllerCameraStyle => (CameraStyle)_settings.controllerCameraStyle;
        public CameraStyle ResolveCameraStyle(bool controller) => controller ? ControllerCameraStyle : CameraStyle;
        /// <summary>Steady camera's top turn speed, degrees/second.</summary>
        public float CameraTurnSpeed => _settings.cameraTurnSpeed;
        /// <summary>Steady camera: a held run keeps the direction it started with while the camera turns.</summary>
        public bool HoldRunDirection => _settings.holdRunDirection;
        public bool InvertCameraHorizontal => _settings.invertCameraHorizontal;
        public bool InvertCameraVertical => _settings.invertCameraVertical;
        public event Action? Changed;
        public FullScreenMode DisplayMode => (FullScreenMode)_settings.displayMode;
        public int ResolutionWidth => _settings.resolutionWidth;
        public int ResolutionHeight => _settings.resolutionHeight;
        public int Quality => _settings.quality;
        public bool VSync => _settings.vSync;
        public int TargetFps => _settings.targetFps;
        public int UiScale => _settings.uiScale;
        public int NetworkStats => _settings.networkStats;
        public float TargetOpacity => _settings.targetOpacity / 100f;
        public float ScreenShake => _settings.screenShake / 100f;
        public bool ReducedFlashing => _settings.reducedFlashing;
        public bool ShowOverheadDamage => _settings.showOverheadDamage;
        public TargetLockMode AutoLockMode => (TargetLockMode)_settings.targetLockMode;
        /// <summary>Save a local practice session log for Training and Solo matches started afterwards.</summary>
        public bool RecordSessionLog => _settings.recordSessionLog;
        public AudioMixerGroup? FindBus(string name)
        {
            if (_mixer == null) _mixer = Resources.Load<AudioMixer>("Settings/SlopArena");
            if (_mixer == null) return null;
            foreach (var group in _mixer.FindMatchingGroups(name))
                if (group.name == name) return group;
            return null;
        }
        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _settings.stickDeadzone = HumanInputActions.DefaultDeadzone * 100f;
            Load();
            if (_mixer == null) _mixer = Resources.Load<AudioMixer>("Settings/SlopArena");
            HumanInputActions.Initialize(_settings.bindingOverrides);
            ApplyControls();
        }
        public void SetUiScale(int percent) { _settings.uiScale = Mathf.Clamp(percent, 80, 140); ApplyUiScale(); ChangedAndApply(); }
        private void Start()
        {
            Apply();
            ApplyVideo();
            ApplyUiScale();
            ApplySavedDisplay();
        }
        public void SetVideo(int quality, bool vsync, int fps)
        {
            _settings.quality = Mathf.Clamp(quality, 0, 2);
            _settings.vSync = vsync;
            _settings.targetFps = fps;
            int level = _settings.quality == 0 ? 0 : _settings.quality == 1 ? 1 : Array.IndexOf(QualitySettings.names, "Ultra");
            if (level < 0) level = QualitySettings.GetQualityLevel();
            QualitySettings.SetQualityLevel(Mathf.Clamp(level, 0, QualitySettings.names.Length - 1), true);
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            Application.targetFrameRate = fps;
            ChangedAndApply();
        }
        public void SetDisplay(FullScreenMode mode, int width, int height)
        {
            Screen.SetResolution(width, height, mode);
            _settings.displayMode = (int)mode;
            _settings.resolutionWidth = width;
            _settings.resolutionHeight = height;
            _dirty = true;
            Save();
            Changed?.Invoke();
        }


        private void Update()
        {
            int sceneHandle = SceneManager.GetActiveScene().handle;
            if (_lastUiSceneHandle != sceneHandle)
            {
                _lastUiSceneHandle = sceneHandle;
                ApplyUiScale();
            }
            bool menuOwnsInput = SettingsOverlay.Active is { IsOpen: true } || MatchPauseMenu.Active is { IsPaused: true };
            if (!Application.isFocused || !_settings.vibration || menuOwnsInput || Time.unscaledTime >= _rumbleUntil)
                StopRumble();
        }

        public void PlayConfirmedImpact()
        {
            if (!isActiveAndEnabled || !_settings.vibration || !Application.isFocused ||
                SettingsOverlay.Active is { IsOpen: true } || MatchPauseMenu.Active is { IsPaused: true })
                return;
            _rumbleStopAttempts = 0;
            foreach (var pad in Gamepad.all)
            {
                try
                {
                    pad.SetMotorSpeeds(0.18f, 0.32f);
                    if (!_rumblingPads.Contains(pad)) _rumblingPads.Add(pad);
                }
                catch (NotSupportedException) { }
            }
            _rumbleUntil = Time.unscaledTime + 0.08f;
        }

        private void StopRumble()
        {
            _rumbleUntil = 0f;
            if (_rumblingPads.Count == 0) return;
            foreach (var pad in _rumblingPads)
                if (pad != null)
                    try { pad.SetMotorSpeeds(0f, 0f); } catch (NotSupportedException) { }
            // Motor commands have no delivery result. Retry over a few Updates before
            // forgetting a pad, including removed pads whose cached speeds need clearing.
            if (++_rumbleStopAttempts >= RumbleStopAttempts)
            {
                _rumblingPads.Clear();
                _rumbleStopAttempts = 0;
            }
        }

        public void SetMaster(float value) { _settings.master = Clamp(value); ChangedAndApply(); }
        public void SetMusic(float value) { _settings.music = Clamp(value); ChangedAndApply(); }
        public void SetSfx(float value) { _settings.sfx = Clamp(value); ChangedAndApply(); }
        public void SetUi(float value) { _settings.ui = Clamp(value); ChangedAndApply(); }
        public void SetMuteWhenUnfocused(bool value) { _settings.muteWhenUnfocused = value; ChangedAndApply(); }
        public void SetBindingOverrides(string json) { _settings.bindingOverrides = json ?? ""; ChangedAndApply(); }
        public void SetStickDeadzone(float percent)
        {
            _settings.stickDeadzone = Mathf.Clamp(percent, 5f, 50f);
            ApplyControls(); ChangedAndApply();
        }
        public void SetVibration(bool value)
        {
            _settings.vibration = value;
            if (!value) StopRumble();
            ChangedAndApply();
        }
        public void SetCameraSensitivity(float percent) { _settings.cameraSensitivity = Mathf.Clamp(Mathf.Round(percent), 10f, 200f); ChangedAndApply(); }
        public void SetStickLookSpeed(float percent) { _settings.stickLookSpeed = Mathf.Clamp(Mathf.Round(percent), 10f, 200f); ChangedAndApply(); }
        public void SetCameraStyle(CameraStyle value, bool controller = false)
        {
            value = CameraStyleNames.IsValid((int)value) ? value : CameraStyle.Steady;
            if (controller)
            {
                _settings.controllerCameraStyle = (int)value;
                if (value != CameraStyle.Manual) _settings.controllerAutoCameraStyle = (int)value;
            }
            else
            {
                _settings.cameraStyle = (int)value;
                if (value != CameraStyle.Manual) _settings.mouseAutoCameraStyle = (int)value;
            }
            ChangedAndApply();
        }
        public CameraStyle ToggleCameraStyle(bool controller)
        {
            var style = ResolveCameraStyle(controller) == CameraStyle.Manual
                ? (CameraStyle)(controller ? _settings.controllerAutoCameraStyle : _settings.mouseAutoCameraStyle)
                : CameraStyle.Manual;
            SetCameraStyle(style, controller);
            Save();
            return style;
        }
        public void SetCameraTurnSpeed(float degrees) { _settings.cameraTurnSpeed = Mathf.Clamp(Mathf.Round(degrees), 10f, 60f); ChangedAndApply(); }
        public void SetHoldRunDirection(bool value) { _settings.holdRunDirection = value; ChangedAndApply(); }
        public void SetInvertCameraHorizontal(bool value) { _settings.invertCameraHorizontal = value; ChangedAndApply(); }
        public void SetInvertCameraVertical(bool value) { _settings.invertCameraVertical = value; ChangedAndApply(); }
        public void SetNetworkStats(int value) { _settings.networkStats = Mathf.Clamp(value, 0, 2); ChangedAndApply(); }
        public void SetTargetOpacity(float percent) { _settings.targetOpacity = Mathf.Clamp(Mathf.Round(percent), 20f, 100f); ChangedAndApply(); }
        public void SetShowOverheadDamage(bool value) { _settings.showOverheadDamage = value; ChangedAndApply(); }
        public void SetScreenShake(float percent) { _settings.screenShake = Clamp(percent); ChangedAndApply(); }
        public void SetReducedFlashing(bool value) { _settings.reducedFlashing = value; ChangedAndApply(); }
        public void SetRecordSessionLog(bool value) { _settings.recordSessionLog = value; ChangedAndApply(); }
        public void SetAutoLockMode(TargetLockMode value)
        {
            _settings.targetLockMode = value is TargetLockMode.Never or TargetLockMode.Always or TargetLockMode.OnHit
                ? (int)value : (int)TargetLockMode.Always;
            ChangedAndApply();
        }

        public void ResetControls()
        {
            HumanInputActions.ResetOverrides();
            _settings.bindingOverrides = "";
            _settings.stickDeadzone = HumanInputActions.DefaultDeadzone * 100f;
            _settings.vibration = true;
            _settings.stickLookSpeed = 100f;
            ApplyControls(); ChangedAndApply(); Save();
        }

        public void ResetAll()
        {
            HumanInputActions.ResetOverrides();
            _settings = SettingsPayload.Defaults;
            NormalizeCameraStyles();
            _settings.stickDeadzone = HumanInputActions.DefaultDeadzone * 100f;
            _settings.displayMode = (int)Screen.fullScreenMode;
            _settings.resolutionWidth = Screen.width;
            _settings.resolutionHeight = Screen.height;
            ApplyControls();
            _dirty = true;
            Apply();
            ApplyVideo();
            ApplyUiScale();
            Changed?.Invoke();
            Save();
        }

        public void Save()
        {
            if (!_dirty) return;
            _settings.bindingOverrides = HumanInputActions.SaveOverrides();
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_settings));
            PlayerPrefs.Save();
            _dirty = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            _unfocused = !focused;
            if (!focused) StopRumble();
            Apply();
        }

        private void OnDisable() => StopRumble();
        private void OnApplicationPause(bool paused) { if (paused) StopRumble(); }
        private void OnApplicationQuit() { StopRumble(); Save(); }
        private void OnDestroy()
        {
            StopRumble();
            // Panel assets survive Editor Play Mode teardown. Restore their base
            // values so the next settings owner cannot compound the previous scale.
            foreach (var panel in _panelBaseScales)
                if (panel.Key != null) panel.Key.scale = panel.Value;
            if (_instance == this) _instance = null;
        }

        private void Load()
        {
            try
            {
                JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(PrefsKey), _settings);
                if (_settings.version != 1)
                {
                    _settings = SettingsPayload.Defaults;
                    _settings.stickDeadzone = HumanInputActions.DefaultDeadzone * 100f;
                }
                _settings.stickDeadzone = ValidRange(_settings.stickDeadzone, HumanInputActions.DefaultDeadzone * 100f, 5f, 50f);
                _settings.cameraSensitivity = ValidRange(_settings.cameraSensitivity, 100f, 10f, 200f);
                _settings.stickLookSpeed = ValidRange(_settings.stickLookSpeed, 100f, 10f, 200f);
                _settings.cameraTurnSpeed = ValidRange(_settings.cameraTurnSpeed, 20f, 10f, 60f);
                _settings.quality = _settings.quality is >= 0 and <= 2 ? _settings.quality : 2;
                _settings.targetFps = _settings.targetFps is 60 or 120 or 144 or 240 or -1 ? _settings.targetFps : 120;
                _settings.uiScale = _settings.uiScale is 80 or 90 or 100 or 110 or 120 or 130 or 140 ? _settings.uiScale : 100;
                _settings.networkStats = Mathf.Clamp(_settings.networkStats, 0, 2);
                _settings.targetOpacity = ValidRange(_settings.targetOpacity, 100f, 20f, 100f);
                if (_settings.targetLockMode != (int)TargetLockMode.Never &&
                    _settings.targetLockMode != (int)TargetLockMode.Always &&
                    _settings.targetLockMode != (int)TargetLockMode.OnHit)
                    _settings.targetLockMode = (int)TargetLockMode.Always;
                _settings.screenShake = ValidRange(_settings.screenShake, 100f, 0f, 100f);
                if (_settings.displayMode < 0 || _settings.displayMode > (int)FullScreenMode.ExclusiveFullScreen)
                    _settings.displayMode = (int)Screen.fullScreenMode;
            }
            catch (Exception exception)
            {
                _settings = SettingsPayload.Defaults;
                _settings.stickDeadzone = HumanInputActions.DefaultDeadzone * 100f;
                Debug.LogWarning($"[Settings] Ignoring unreadable preferences: {exception.Message}");
            }
            NormalizeCameraStyles();
        }

        private void NormalizeCameraStyles()
        {
            const int steady = (int)CameraStyle.Steady;
            if (_settings.cameraRevision < 1)
            {
                // ADR-0043: Steady replaced Auto Combat, Air Duel, Side Duel and Shoulder, and is
                // the default on both devices; earlier saves held the old defaults, not choices.
                _settings.cameraStyle = _settings.controllerCameraStyle = steady;
                _settings.mouseAutoCameraStyle = _settings.controllerAutoCameraStyle = steady;
                _settings.cameraRevision = 1;
                _dirty = true;
            }
            if (!CameraStyleNames.IsValid(_settings.cameraStyle))
                _settings.cameraStyle = steady;
            if (_settings.controllerCameraStyle == -1)
                _settings.controllerCameraStyle = _settings.cameraStyle;
            if (!CameraStyleNames.IsValid(_settings.controllerCameraStyle))
                _settings.controllerCameraStyle = steady;
            if (!CameraStyleNames.IsValid(_settings.mouseAutoCameraStyle) || _settings.mouseAutoCameraStyle == (int)CameraStyle.Manual)
                _settings.mouseAutoCameraStyle = steady;
            if (!CameraStyleNames.IsValid(_settings.controllerAutoCameraStyle) || _settings.controllerAutoCameraStyle == (int)CameraStyle.Manual)
                _settings.controllerAutoCameraStyle = steady;
            if (_settings.cameraStyle != (int)CameraStyle.Manual)
                _settings.mouseAutoCameraStyle = _settings.cameraStyle;
            if (_settings.controllerCameraStyle != (int)CameraStyle.Manual)
                _settings.controllerAutoCameraStyle = _settings.controllerCameraStyle;
        }


        private void ChangedAndApply() { _dirty = true; Apply(); Changed?.Invoke(); }
        private void Apply()
        {
            SetGain("MasterVolume", _unfocused && _settings.muteWhenUnfocused ? 0f : _settings.master);
            SetGain("MusicVolume", _settings.music);
            SetGain("SfxVolume", _settings.sfx);
            SetGain("UiVolume", _settings.ui);
        }

        private void ApplyControls()
        {
            HumanInputActions.ApplyOverrides(_settings.bindingOverrides);
            InputSystem.settings.defaultDeadzoneMin = Mathf.Clamp(_settings.stickDeadzone / 100f, 0.05f, 0.5f);
        }

        private void SetGain(string parameter, float percent)
        {
            if (_mixer != null)
                _mixer.SetFloat(parameter, percent <= 0f ? -80f : Mathf.Log10(percent / 100f) * 20f);
        }

        private static float Clamp(float value) => Mathf.Clamp(Mathf.Round(value), 0f, 100f);
        private static float Valid(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 100f ? fallback : value;
        private static float ValidRange(float value, float fallback, float min, float max)
            => float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max ? fallback : value;
        private void ApplyVideo()
        {
            int low = Array.IndexOf(QualitySettings.names, "Low");
            int medium = Array.IndexOf(QualitySettings.names, "Medium");
            int ultra = Array.IndexOf(QualitySettings.names, "Ultra");
            int level = _settings.quality switch
            {
                0 => low >= 0 ? low : 0,
                1 => medium >= 0 ? medium : Mathf.Min(1, QualitySettings.names.Length - 1),
                _ => ultra >= 0 ? ultra : QualitySettings.GetQualityLevel()
            };
            QualitySettings.SetQualityLevel(Mathf.Clamp(level, 0, QualitySettings.names.Length - 1), true);
            QualitySettings.vSyncCount = _settings.vSync ? 1 : 0;
            Application.targetFrameRate = _settings.targetFps;
        }

        private void ApplyUiScale()
        {
            foreach (var document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var panelSettings = document.panelSettings;
                if (panelSettings == null) continue;
                if (!_panelBaseScales.TryGetValue(panelSettings, out float baseScale))
                {
                    baseScale = panelSettings.scale;
                    _panelBaseScales.Add(panelSettings, baseScale);
                }
                panelSettings.scale = baseScale * (_settings.uiScale / 100f);
            }
        }

        private void ApplySavedDisplay()
        {
            if (_settings.resolutionWidth <= 0 || _settings.resolutionHeight <= 0)
            {
                _settings.resolutionWidth = Screen.width;
                _settings.resolutionHeight = Screen.height;
                _settings.displayMode = (int)Screen.fullScreenMode;
                return;
            }
            bool available = false;
            foreach (var resolution in Screen.resolutions)
                if (resolution.width == _settings.resolutionWidth && resolution.height == _settings.resolutionHeight)
                {
                    available = true;
                    break;
                }
            if (!available)
            {
                _settings.resolutionWidth = Screen.width;
                _settings.resolutionHeight = Screen.height;
                _settings.displayMode = (int)FullScreenMode.Windowed;
                _dirty = true;
                Save();
                return;
            }
            Screen.SetResolution(_settings.resolutionWidth, _settings.resolutionHeight, (FullScreenMode)_settings.displayMode);
        }
    }
}
