using System;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Client.Camera;
using SlopArena.Client.Input;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// In-match pause menu (issue #77): Esc toggles a small centered panel with
    /// Resume, an optional Arcade Retry, Leave Match (back to stage select) and Quit Game. While paused the
    /// simulation is frozen (Time.timeScale stops FixedUpdate, and the sim ticks
    /// only there), the cursor is released, and camera mouse input is suppressed
    /// via CameraMount.FreeCursor. Shared by TrainingMatch and PvPMatch through
    /// MatchBase. The panel is built into the match scene's existing UIDocument at
    /// runtime — no scene or asset edits.
    /// </summary>
    public class MatchPauseMenu : MonoBehaviour
    {
        private bool _paused;
        private int _pauseChangedFrame = -1;
        private CameraMount? _cameraMount;
        private bool _freezeSimulation = true;
        private InputController? _inputController;
        private Action? _onLeaveMatch;
        private Action? _onRetry;
        private VisualElement? _panel;
        private Button? _resumeButton;
        private SettingsOverlay? _settingsOverlay;

        /// <summary>True while the match menu owns local gameplay input.</summary>
        public bool IsPaused => _paused;
        public static MatchPauseMenu? Active { get; private set; }
        public void Init(CameraMount? cameraMount, InputController? inputController, Action? onLeaveMatch = null, UIDocument? hostDocument = null, bool freezeSimulation = true, Action? onRetry = null)
        {
            _freezeSimulation = freezeSimulation;
            _cameraMount = cameraMount;
            _inputController = inputController;
            _onLeaveMatch = onLeaveMatch;
            _onRetry = onRetry;
            Active = this;
            // Explicit host (issue #210): the match's HUD document is passed in
            // rather than discovered as "whichever UIDocument is first".
            var doc = hostDocument != null ? hostDocument : FindFirstObjectByType<UIDocument>();
            if (doc == null)
            {
                Debug.LogWarning("[PauseMenu] No UIDocument host for the pause menu — pause menu unavailable.");
                return;
            }
            _settingsOverlay = gameObject.GetComponent<SettingsOverlay>() ?? gameObject.AddComponent<SettingsOverlay>();
            BuildPanel(doc.rootVisualElement);
        }

        private void Update()
        {
            // Escape also generates a UI Toolkit cancel event. Whichever path
            // changes pause first consumes the press for this frame.
            if (_pauseChangedFrame == Time.frameCount)
                return;
            if (SteamRoomJoinPrompt.ClosedThisFrame)
                return;
            if (SteamRoomJoinPrompt.IsConfirming)
            {
                if (!ChatInputGate.ExternalOverlaySuppressed)
                {
                    var pad = UnityEngine.InputSystem.Gamepad.current;
                    if (pad?.buttonEast.wasPressedThisFrame == true)
                        SteamRoomJoinPrompt.CancelActive();
                    else if (pad?.startButton.wasPressedThisFrame == true
                        && !pad.buttonSouth.wasPressedThisFrame)
                        GamepadUIBridge.Submit(SteamRoomJoinPrompt.ForwardButton);
                }
                return;
            }
            if (ChatInputGate.SuppressShortcuts)
                return;
            if (SettingsOverlay.ClosedThisFrame)
                return;
            if (SettingsOverlay.Active is { IsOpen: true })
                return;

            if (_paused && UnityEngine.InputSystem.Gamepad.current?.buttonEast.wasPressedThisFrame == true)
            {
                SetPaused(false);
                return;
            }
            if (HumanInputActions.Get("Pause").WasPressedThisFrame())
                SetPaused(!_paused);
        }

        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            _pauseChangedFrame = Time.frameCount;
            // Local Training/Solo pauses stop simulation; online UI suppresses
            // local controls while transport and authoritative progress continue.
            Time.timeScale = paused && _freezeSimulation ? 0f : 1f;

            // Discard buffered input so nothing fires on the first frame after resume.
            _inputController?.RequireReleaseBeforeHumanInput();

            if (_cameraMount != null)
            {
                if (paused) _cameraMount.FreezeAtCurrentAngles();
                // FreeCursor releases the cursor; Normal re-locks it (issue #77).
                _cameraMount.SetMode(paused ? CameraMode.FreeCursor : CameraMode.Normal);
            }
            else
            {
                UnityEngine.Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
                UnityEngine.Cursor.visible = paused;
            }

            if (_panel != null)
                _panel.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;

            if (paused)
                _resumeButton?.Focus();

            // The pause menu obscures chat, so the docked/expanded conversation
            // behind it must not be treated as read (issue #214).
            if (paused) UiModalState.Push();
            else UiModalState.Pop();
        }

        private void BuildPanel(VisualElement root)
        {
            _panel = new VisualElement();
            _panel.AddToClassList("match-pause-cover");
            _panel.style.position = Position.Absolute;
            _panel.style.left = 0;
            _panel.style.right = 0;
            _panel.style.top = 0;
            _panel.style.bottom = 0;
            _panel.style.alignItems = Align.Center;
            _panel.style.justifyContent = Justify.Center;

            // Card, title and button sizes live in HUD.uss so this card matches
            // the frontend MENU: same order, Resume first.
            var box = new VisualElement();
            box.AddToClassList("match-pause-box");

            var title = new Label("PAUSED");
            title.AddToClassList("match-pause-title");
            box.Add(title);

            _resumeButton = MakeButton("RESUME", () => SetPaused(false));
            _resumeButton.AddToClassList("match-pause-action--primary");
            box.Add(_resumeButton);
            if (_onRetry != null)
                box.Add(MakeButton("RETRY", Retry));
            box.Add(MakeButton("SETTINGS", OpenSettings));
            box.Add(MakeButton("LEAVE MATCH", LeaveMatch));
            box.Add(MakeButton("QUIT GAME", QuitGame));

            _panel.Add(box);
            root.Add(_panel);
            _panel.RegisterCallback<NavigationCancelEvent>(evt =>
            {
                evt.StopImmediatePropagation();
                if (_paused && _pauseChangedFrame != Time.frameCount
                    && !ChatInputGate.ExternalOverlaySuppressed)
                    SetPaused(false);
            });
            _panel.style.display = DisplayStyle.None;
        }

        private static Button MakeButton(string label, Action onClick)
        {
            var btn = new Button(onClick) { text = label, name = "pause-" + label.ToLowerInvariant().Replace(' ', '-') };
            btn.AddToClassList("match-pause-action");
            return btn;
        }
        private void OpenSettings()
        {
            var host = _panel?.parent;
            if (_settingsOverlay == null || host == null)
                return;
            UISFX.PlayClick();
            _settingsOverlay.Open(host);
        }


        private void Retry()
        {
            Debug.Log("[PauseMenu] Retry.");
            SetPaused(false); // restore timeScale + cursor before the fresh attempt
            _onRetry?.Invoke();
        }

        private void LeaveMatch()
        {
            Debug.Log("[PauseMenu] Leave match.");
            SetPaused(false); // restore timeScale + cursor before the scene load
            _onLeaveMatch?.Invoke();
        }

        private void QuitGame()
        {
            Debug.Log("[PauseMenu] Quit Game.");
            SetPaused(false); // restore timeScale + cursor before exiting
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy()
        {
            // Never leave the sim frozen if the match scene unloads mid-pause.
            Time.timeScale = 1f;
            if (_paused)
                UiModalState.Pop(); // match scene unload while paused (issue #214)
            if (Active == this) Active = null;
        }
    }
}
