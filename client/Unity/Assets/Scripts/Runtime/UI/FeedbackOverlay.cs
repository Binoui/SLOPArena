using System;
using System.Collections;
using System.Threading;
using SlopArena.Client.Diagnostics;
using SlopArena.Client.Input;
using SlopArena.Client.Network;
using SlopArena.Shared;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Cursor = UnityEngine.Cursor;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public sealed class FeedbackOverlay : MonoBehaviour
    {
        public enum DraftState { Empty, Capturing, Composing, Sending, Retry, Rejected }
        private static FeedbackOverlay _instance;
        private int _closedFrame = -1;
        public static bool OwnsInput => _instance != null && (_instance._visible || _instance._closedFrame == Time.frameCount);
        public DraftState State { get; private set; }
        public int CaptureFrame { get; private set; } = -1;
        public int ComposerFrame { get; private set; } = -1;
        public bool CaptureHasColor { get; private set; }
        public bool CapturedBeforeComposer { get; private set; }
        public FeedbackSubmission SealedSubmission => _sealed;
        private Guid _id;
        private FeedbackContext _context;
        private FeedbackImage _image;
        private FeedbackSubmission _sealed;
        private FeedbackKind _kind;
        private string _message = "";
        private bool _includeImage = true;
        private bool _visible;
        private bool _wasPaused;
        private bool _online;
        private MatchPauseMenu _pause;
        private UIDocument _document;
        private PanelSettings _panelSettings;
        private Texture2D _thumbnail;
        private VisualElement _composer;
        private VisualElement _fields;
        private VisualElement _priorFocus;
        private TextField _text;
        private Label _status;
        private Button _send;
        private int _generation;
        private int _scene;
        private float _captureStarted;
        private CancellationTokenSource _sendCancellation;
        private Callback<GamepadTextInputDismissed_t> _keyboardCallback;
        private bool _nativeKeyboard;
        private CursorLockMode _priorCursor;
        private bool _priorCursorVisible;
        private string _notice = "";
        private float _lastSubmitTime = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            _instance = null;
            var owner = new GameObject("FeedbackOverlay");
            DontDestroyOnLoad(owner);
            _instance = owner.AddComponent<FeedbackOverlay>();
        }

        public static void RequestOpen()
        {
            if (_instance == null || OwnsInput || ChatInputGate.ExternalOverlaySuppressed ||
                ChatInputGate.IsComposing || HumanInputActions.IsCapturing || SettingsOverlay.Active is { IsOpen: true } ||
                SteamRoomJoinPrompt.IsConfirming) return;
            _instance.Open();
        }

        private void Update()
        {
            if (State != DraftState.Empty && _generation != (ChatSession.Instance?.FeedbackAccountGeneration ?? 0))
            { Close(); ClearDraft(); return; }
            if (_visible && _scene != SceneManager.GetActiveScene().handle)
            {
                bool capturing = State == DraftState.Capturing;
                Close();
                if (capturing) ClearDraft();
                return;
            }
            if (State == DraftState.Capturing && Time.realtimeSinceStartup - _captureStarted >= 1f)
            { StopAllCoroutines(); _context = FeedbackContextCapture.Observe(); FinishCapture("Screenshot unavailable. You can still send feedback."); }
            if (Keyboard.current?.f8Key.wasPressedThisFrame == true && !ChatInputGate.SuppressShortcuts && (!UiModalState.Presented || MatchPauseMenu.Active is { IsPaused: true }))
                RequestOpen();
            if (_visible && _composer != null && !_nativeKeyboard && !ChatInputGate.ExternalOverlaySuppressed)
            {
                if (!GamepadUIBridge.IsAvailable(_composer.panel?.focusController?.focusedElement as VisualElement))
                    (_send != null && _send.enabledInHierarchy ? _send : _composer.Query<Button>().Where(b => b.enabledInHierarchy).First())?.Focus();
                GamepadUIBridge.Pump(_composer.panel, cancel: false);
            }
            if (_visible && !_nativeKeyboard && !ChatInputGate.ExternalOverlaySuppressed &&
                (Keyboard.current?.escapeKey.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true)) Close();
        }

        private void Open()
        {
            _visible = true;
            _scene = SceneManager.GetActiveScene().handle;
            _generation = ChatSession.Instance?.FeedbackAccountGeneration ?? 0;
            _pause = MatchPauseMenu.Active;
            _wasPaused = _pause != null && _pause.IsPaused;
            _online = _pause != null && _pause.FeedbackOnlineContinues;
            _priorCursor = Cursor.lockState; _priorCursorVisible = Cursor.visible;
            _priorFocus = FindFirstObjectByType<UIDocument>()?.rootVisualElement.panel?.focusController.focusedElement as VisualElement;
            UiModalState.Push();
            ChatInputGate.Begin();
            if (_pause != null) { if (State != DraftState.Empty) _pause.SetPaused(true); _pause.HideForFeedback(true); }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (State != DraftState.Empty) { BuildComposer(); return; }
            State = DraftState.Capturing; _id = Guid.NewGuid(); _kind = FeedbackKind.Bug;
            CaptureFrame = ComposerFrame = -1; CaptureHasColor = false;
            _captureStarted = Time.realtimeSinceStartup;
            StartCoroutine(Capture());
        }

        private IEnumerator Capture()
        {
            yield return new WaitForEndOfFrame();
            if (!_visible || State != DraftState.Capturing) yield break;
            Texture2D raw = null;
            Texture2D scaled = null;
            RenderTexture target = null;
            var previous = RenderTexture.active;
            try
            {
                _context = FeedbackContextCapture.Observe();
                CaptureFrame = Time.frameCount; CapturedBeforeComposer = _composer == null;
                raw = ScreenCapture.CaptureScreenshotAsTexture();
                float scale = Mathf.Min(1f, 1280f / raw.width, 1280f / raw.height,
                    Mathf.Sqrt(921600f / (raw.width * (float)raw.height)));
                int width = Mathf.Max(1, Mathf.FloorToInt(raw.width * scale));
                int height = Mathf.Max(1, Mathf.FloorToInt(raw.height * scale));
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(raw, target); RenderTexture.active = target;
                scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0); scaled.Apply();
                foreach (var pixel in scaled.GetPixels32())
                    if (pixel.r > 8 || pixel.g > 8 || pixel.b > 8) { CaptureHasColor = true; break; }
                var jpeg = scaled.EncodeToJPG(75);
                if (jpeg.Length > 1048576) jpeg = scaled.EncodeToJPG(40);
                _image = new FeedbackImage(jpeg, width, height);
            }
            catch (Exception) { _notice = "Screenshot unavailable. You can still send feedback."; }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (raw != null) Destroy(raw);
                if (scaled != null) Destroy(scaled);
            }
            FinishCapture(_notice);
        }

        private void FinishCapture(string notice)
        {
            _notice = notice;
            State = DraftState.Composing;
            // Pausing reshapes the HUD (Training expands its panel), so it waits until the frame is captured.
            if (_visible && _pause != null) { _pause.SetPaused(true); _pause.HideForFeedback(true); }
            BuildComposer();
        }

        private void BuildComposer()
        {
            if (_document == null)
            {
                var host = FindFirstObjectByType<UIDocument>();
                _panelSettings = host != null && host.panelSettings != null ? Instantiate(host.panelSettings) : ScriptableObject.CreateInstance<PanelSettings>();
                _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _panelSettings.referenceResolution = new Vector2Int(1280, 720);
                _document = gameObject.AddComponent<UIDocument>();
                _document.panelSettings = _panelSettings;
                _document.sortingOrder = 1000;
                // The frontend's input module and GamepadUIBridge both submit one A press; a second submit would flip the screenshot toggle back.
                _document.rootVisualElement.RegisterCallback<NavigationSubmitEvent>(evt =>
                {
                    if (Time.unscaledTime - _lastSubmitTime < 0.2f) evt.StopImmediatePropagation();
                    else _lastSubmitTime = Time.unscaledTime;
                }, TrickleDown.TrickleDown);
            }
            if (_thumbnail != null) { Destroy(_thumbnail); _thumbnail = null; }
            var root = _document.rootVisualElement;
            root.Clear(); root.style.display = DisplayStyle.Flex;
            root.style.position = Position.Absolute; root.style.left = root.style.right = root.style.top = root.style.bottom = 0;
            root.style.alignItems = Align.Center; root.style.justifyContent = Justify.Center;
            root.style.backgroundColor = new Color(0, 0, 0, .8f);
            var stylesheet = Resources.Load<StyleSheet>("FeedbackOverlay");
            if (stylesheet != null) root.styleSheets.Add(stylesheet);
            _composer = new VisualElement { name = "feedback-composer" }; _composer.AddToClassList("feedback-card");
            root.Add(_composer); ComposerFrame = Time.frameCount;
            _composer.Add(new Label("SEND FEEDBACK"));
            if (_online) _composer.Add(new Label("Online match continues"));
            _fields = new VisualElement(); _composer.Add(_fields);
            var kinds = new VisualElement(); kinds.style.flexDirection = FlexDirection.Row; _fields.Add(kinds);
            foreach (FeedbackKind kind in Enum.GetValues(typeof(FeedbackKind)))
            {
                var choice = kind;
                var button = new Button { text = kind.ToString().ToUpperInvariant() };
                button.clicked += () => { _kind = choice; foreach (var child in kinds.Children()) child.RemoveFromClassList("selected"); button.AddToClassList("selected"); };
                if (_kind == kind) button.AddToClassList("selected");
                kinds.Add(button);
            }
            _text = new TextField("Message (optional)") { multiline = true, maxLength = 2000, value = _message };
            _text.RegisterValueChangedCallback(evt => _message = evt.newValue);
            _text.RegisterCallback<FocusInEvent>(_ => ChatInputGate.Begin());
            _text.RegisterCallback<NavigationSubmitEvent>(evt => { OpenKeyboard(); evt.StopImmediatePropagation(); });
            _text.RegisterCallback<PointerDownEvent>(_ => { if (Gamepad.current != null) OpenKeyboard(); });
            _fields.Add(_text);
            if (_image != null)
            {
                _thumbnail = new Texture2D(2, 2); _thumbnail.LoadImage(_image.Jpeg.ToArray());
                var thumbnail = new Image { image = _thumbnail, scaleMode = ScaleMode.ScaleToFit };
                thumbnail.style.height = 150; _fields.Add(thumbnail);
                var include = new Toggle("Include screenshot") { value = _includeImage };
                include.RegisterValueChangedCallback(evt => { _includeImage = evt.newValue; thumbnail.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None; });
                _fields.Add(include);
            }
            _composer.Add(new Label("Sends your message, this screenshot, game version and match details, linked to your Steam account. Never your log files."));
            _composer.Add(new Label("Screenshots may contain HUD, names and visible chat. Unsent feedback stays here until you quit."));
            _status = new Label(_notice); _composer.Add(_status);
            _send = new Button(Send) { text = State == DraftState.Retry ? "RETRY" : "SEND" }; _composer.Add(_send);
            _composer.Add(new Button(Close) { text = "CLOSE" });
            _composer.Add(new Button(() => { if (State == DraftState.Sending) return; Close(); ClearDraft(); }) { text = "DISCARD" });
            _fields.SetEnabled(_sealed == null);
            _send.SetEnabled(State != DraftState.Sending && State != DraftState.Rejected);
            _send.Focus();
            _lastSubmitTime = Time.unscaledTime;
            _composer.RegisterCallback<NavigationCancelEvent>(evt => { evt.StopImmediatePropagation(); if (!_nativeKeyboard && !ChatInputGate.ExternalOverlaySuppressed) Close(); });
        }

        private void OpenKeyboard()
        {
            if (_nativeKeyboard || _sealed != null) return;
            try
            {
                _keyboardCallback ??= Callback<GamepadTextInputDismissed_t>.Create(result =>
                {
                    if (!_nativeKeyboard) return;
                    if (result.m_bSubmitted && _generation == (ChatSession.Instance?.FeedbackAccountGeneration ?? 0) && SteamUtils.GetEnteredGamepadTextInput(out string entered, 2001))
                    { _message = entered; _text?.SetValueWithoutNotify(entered); }
                    _nativeKeyboard = false; ChatInputGate.EndExternalOverlay();
                });
                _nativeKeyboard = SteamUtils.ShowGamepadTextInput(EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                    EGamepadTextInputLineMode.k_EGamepadTextInputLineModeMultipleLines, "Feedback (optional)", 2000, _message);
                if (_nativeKeyboard) ChatInputGate.BeginExternalOverlay();
            }
            catch (Exception) { _nativeKeyboard = false; _text?.Focus(); }
        }

        private async void Send()
        {
            if (State == DraftState.Sending || State == DraftState.Rejected) return;
            try
            {
                _sealed ??= new FeedbackSubmission(_id, _kind, _message, _context, _includeImage ? _image : null);
                State = DraftState.Sending; _fields.SetEnabled(false); _send.SetEnabled(false); _status.text = "Sending…";
                _sendCancellation?.Dispose(); _sendCancellation = new CancellationTokenSource();
                var session = ChatSession.Instance;
                var result = session == null ? new FeedbackSendResult.Retryable("Steam sign-in unavailable. Your draft is kept.") :
                    await session.SendFeedbackAsync(_sealed, _sendCancellation.Token);
                if (this == null || _generation != (ChatSession.Instance?.FeedbackAccountGeneration ?? 0)) return;
                if (result is FeedbackSendResult.Accepted)
                { ClearDraft(); _status.text = "Feedback sent. Thank you!"; _send.SetEnabled(false); return; }
                State = result is FeedbackSendResult.Rejected ? DraftState.Rejected : DraftState.Retry;
                _notice = result is FeedbackSendResult.Retryable retry ? retry.Reason : "Feedback rejected. Discard this draft to start again.";
                if (_visible) { _status.text = _notice; _send.text = "RETRY"; _send.SetEnabled(State == DraftState.Retry); if (State == DraftState.Retry) _send.Focus(); }
            }
            catch (Exception) { State = DraftState.Retry; _notice = "Could not send. Your draft is kept."; if (_visible) { _status.text = _notice; _send.SetEnabled(true); _send.Focus(); } }
        }

        public void Close()
        {
            if (!_visible) return;
            _visible = false; _closedFrame = Time.frameCount;
            StopAllCoroutines();
            if (State == DraftState.Capturing) ClearDraft();
            if (_document != null) { _document.rootVisualElement.Clear(); _document.rootVisualElement.style.display = DisplayStyle.None; }
            _composer = null;
            if (_thumbnail != null) Destroy(_thumbnail);
            if (_pause != null) { _pause.SetPaused(_wasPaused); _pause.HideForFeedback(false); }
            Cursor.lockState = _priorCursor; Cursor.visible = _priorCursorVisible;
            UiModalState.Pop(); ChatInputGate.End();
            if (_priorFocus?.panel != null) _priorFocus.Focus();
        }

        private void ClearDraft()
        { _sendCancellation?.Cancel(); _sealed = null; _context = null; _image = null; _message = ""; _notice = ""; _includeImage = true; State = DraftState.Empty; }
        private void OnDestroy()
        { Close(); _sendCancellation?.Cancel(); _sendCancellation?.Dispose(); _keyboardCallback?.Dispose(); if (_panelSettings != null) Destroy(_panelSettings); if (_instance == this) _instance = null; }
    }
}
