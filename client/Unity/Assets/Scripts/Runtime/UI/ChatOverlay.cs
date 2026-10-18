#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SlopArena.Client.Input;
using SlopArena.Client.Network;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Shared chat surface. The overlay owns presentation and focus only; all
    /// identity, routing, history, drafts, mute state, and send outcomes live
    /// in the persistent ChatSession.
    ///
    /// Menus dock one stream (filters, presence, a destination chip) or a slim
    /// minimized tab. Gameplay keeps a quiet ticker and a one-line composer;
    /// Room lines from the people in the match become HUD speech slips.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class ChatOverlay : MonoBehaviour
    {
        private const int MaxMessageScalars = 500;
        private const float TickerLifetimeSeconds = 8f;
        private const float TickerFadeSeconds = 2f;
        private const float SystemLineSeconds = 5f;
        private const int TickerLines = 3;
        private const int ContextLines = 5;
        private const int MaxArrivals = 16;
        private const int MaxStreamRows = 100;
        private const int MaxLineTextElements = 140;
        private const int MaxRowNameElements = 16;
        private const float QuickLineCooldownSeconds = 1.5f;
        // Controller players cannot type; the free D-pad directions send these to the Room.
        private static readonly string[] QuickLines = { "GG", "NICE", "ONE MORE?" };

        public const string DockOpenPlayerPrefsKey = "SlopArena.Chat.DockOpen";
        private const string WidthPlayerPrefsKey = "SlopArena.Chat.WidthPx";
        private const string HeightPlayerPrefsKey = "SlopArena.Chat.HeightPx";

        private enum StreamFilter { All, Room, Global, Direct }

        /// <summary>A live message (or local status line) for the gameplay ticker.</summary>
        private sealed class Arrival
        {
            public ChatMessage? Message;
            public string? SystemText;
            public float At;
        }

        private static ChatOverlay? _instance;

        /// <summary>The persistent presenter instance; the focus router uses
        /// it for shell-page cancel layers and social presentation.</summary>
        public static ChatOverlay? Instance => _instance;

        private VisualElement? _root;
        private VisualElement? _frame;
        // Gameplay lane.
        private VisualElement? _lane;
        private VisualElement? _ticker;
        private VisualElement? _hint;
        private Label? _hintKeys;
        private Label? _hintDm;
        private VisualElement? _laneCompose;
        // Menu, minimized.
        private VisualElement? _collapsed;
        private Button? _open;
        private Label? _unread;
        private Label? _peek;
        private Label? _presence;
        // Menu, open.
        private VisualElement? _panel;
        private VisualElement? _main;
        private VisualElement? _directory;
        private ScrollView? _onlineList;
        private Label? _onlineFeedback;
        private ScrollView? _history;
        private Label? _historyFeedback;
        private Button? _newest;
        private Button? _refreshDirectory;
        private Button? _online;
        private Label? _onlineText;
        private VisualElement? _onlineDot;
        private readonly Dictionary<StreamFilter, (Button Button, Label Count)> _filters = new();
        private Button? _close;
        // Composer: docked in the menu panel, moved into the lane in gameplay.
        private VisualElement? _compose;
        private Button? _to;
        private TextField? _draft;
        private Label? _keys;
        private Button? _send;
        private VisualElement? _resizeGrip;
        private int _resizePointerId = -1;
        private Vector2 _resizeStartPosition;
        private float _resizeStartWidthPx;
        private float _resizeStartHeightPx;
        private float _preferredWidthPx;
        private float _preferredHeightPx;
        private bool _resizeDirty;

        private ChatSession? _session;
        private HUDManager? _hud;
        private IVisualElementScheduledItem? _tickerTick;
        private bool _viewBound;
        private bool _expanded;
        private bool _suppressDraftChanged;
        private string _lastViewKey = string.Empty;
        private string _lastDraft = string.Empty;
        private string _streamSignature = string.Empty;
        private string _tickerSignature = string.Empty;
        private bool _showDirectory;
        private bool _enterConsumed;
        private bool _pinToNewest = true;
        private StreamFilter _filter = StreamFilter.All;

        // Social presentation preference (issues #214/#220). On shell-hosted
        // pages this is the remembered manual minimization: true keeps the
        // docked stream visible, false keeps it minimized. Manual
        // minimize/restore rewrites it; resizing never does.
        private bool _dockOpen;
        // Per-view scroll anchors, launch-scoped presentation state:
        // view -> pinned-to-newest and last scroll offset survive page changes,
        // gameplay and frontend recreation via this persistent overlay.
        private readonly Dictionary<string, bool> _pinnedNewest = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector2> _scrollOffsets = new(StringComparer.Ordinal);
        // Views whose reading anchor was evicted by the bounded buffer
        // (issue #214): the next render returns them to newest with the
        // explanation already written to the conversation Feedback.
        private readonly HashSet<string> _evictionReset = new(StringComparer.Ordinal);
        // Frontend and gameplay are the only presentation hosts. Page changes
        // retain the same shell attachment; gameplay uses the HUD document.
        private bool _hostIsShell;
        // Zero-size focusable element parked on the gameplay HUD root when
        // the composer closes (issue #216): focus never rests on the chat
        // surface while it hides, so UI Toolkit's focus fixup cannot re-arm
        // the input gate as if the player had navigated there.
        private VisualElement? _focusParker;
        // Gameplay ticker: live arrivals timed from local receipt, and the
        // messages already printed as speech slips over a fighter.
        private readonly List<Arrival> _arrivals = new();
        private readonly HashSet<Guid> _spoken = new();
        private float _lastSpeechAt = float.NegativeInfinity;
        private float _nextQuickLineAt;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            ChatOverlay? existing = FindFirstObjectByType<ChatOverlay>();
            if (existing != null)
            {
                _instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            var gameObject = new GameObject("ChatOverlay");
            gameObject.SetActive(false);
            gameObject.AddComponent<ChatOverlay>();
            DontDestroyOnLoad(gameObject);
            gameObject.SetActive(true);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            _dockOpen = PlayerPrefs.GetInt(DockOpenPlayerPrefsKey, 1) == 1;
            _preferredWidthPx = PlayerPrefs.GetFloat(WidthPlayerPrefsKey, 0f);
            _preferredHeightPx = PlayerPrefs.GetFloat(HeightPlayerPrefsKey, 0f);
        }

        private void OnEnable()
        {
            BindView();
            AttachToScene(SceneManager.GetActiveScene());
            TryBindSession();
            SceneManager.sceneLoaded += OnSceneLoaded;
            FrontendController.PageChanged += OnFrontendPageChanged;
        }

        private void OnDisable()
        {
            FinishResize();
            UnbindSession();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            FrontendController.PageChanged -= OnFrontendPageChanged;
            if (_expanded)
                ChatInputGate.End();
        }

        private void OnDestroy()
        {
            UnbindSession();
            if (_instance == this)
                _instance = null;
            _root?.RemoveFromHierarchy();
            ChatInputGate.End();
        }

        private void UnbindSession()
        {
            // Event fields remain managed objects after Unity destroys a publisher.
            // Unity's overloaded null check would skip unsubscribe during a scene retry.
            if (_session is not null)
            {
                _session.Changed -= OnSessionChanged;
                _session.ConversationHistoryTrimmed -= OnConversationHistoryTrimmed;
                _session.MessageArrived -= OnMessageArrived;
            }
            _session = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            FinishResize();
            // Frontend recreation must not reset the conversation view
            // (issue #214): scene loads keep the dock state and re-attach to
            // the explicit host. Entering gameplay is the one deliberate
            // collapse — match entry ends interactive chat while ChatSession
            // keeps drafts. A Room match starts addressed to the Room.
            if (!FrontendController.IsFrontendActive && ChatInputGate.IsGameplayScene)
            {
                SetExpanded(false, false);
                if (_session?.JoinedRoomId is Guid roomId)
                    SelectDestination(RoomKey(roomId));
            }
            AttachToScene(scene);
        }

        private void AttachToScene(Scene scene)
        {
            if (_root == null) return;
            if (FrontendController.IsFrontendActive)
            {
                if (FrontendController.Shell?.TryGetChatHosts(out var socialHost, out _) == true)
                    AttachToShell(socialHost);
                return;
            }
            AttachToGameplayHost(scene);
        }

        /// <summary>
        /// The one shell attach (issue #219): the presenter moves into the
        /// absolute social host and stays there for every shell-hosted page
        /// change. Docked ↔ minimized is presentation-only on this one
        /// subtree (issue #220); the remembered preference decides which
        /// presentation re-entry restores.
        /// </summary>
        private void AttachToShell(VisualElement socialHost)
        {
            if (_root!.parent == socialHost)
                return;
            _root.RemoveFromHierarchy();
            socialHost.Add(_root);
            _hostIsShell = true;
            _hud = null;
            _expanded = _dockOpen;
            FrontendController.Shell?.SetSocialCellSize(_preferredWidthPx, _preferredHeightPx);
            ApplyPresentation();
            RenderSession();
            FrontendFocusRouter.NotifyPresentationChanged();
        }

        private void AttachToGameplayHost(Scene scene)
        {
            var hud = FindFirstObjectByType<HUDManager>();
            var host = hud?.Document;
            if (host == null || !host.isActiveAndEnabled ||
                host.gameObject.scene != scene || host.rootVisualElement == null)
            {
                if (_root!.panel != null)
                    _root.RemoveFromHierarchy();
                _hostIsShell = false;
                _hud = null;
                return;
            }
            if (_root!.parent != host.rootVisualElement)
            {
                _root.RemoveFromHierarchy();
                host.rootVisualElement.Add(_root);
            }
            _hostIsShell = false;
            _hud = hud;
            _root.BringToFront();
            ApplyPresentation();
            RenderSession();
        }


        private void OnFrontendPageChanged(FrontendPage page)
        {
            // The shell attachment is stable across every page activation.
            AttachToScene(SceneManager.GetActiveScene());
        }

        private void Update()
        {
            TryBindSession();
            // Evaluate the gate every frame so an armed release requirement
            // clears as soon as every held control is released (issue #215):
            // nothing evaluates it in menus, and a stale arm would turn the
            // next fresh back press into a dead one.
            _ = ChatInputGate.SuppressShortcuts;
            var keyboard = Keyboard.current;
            bool enterHeld = keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed);
            if (!enterHeld) _enterConsumed = false;
            if (keyboard?.escapeKey.wasPressedThisFrame == true && OwnsBackPress())
            {
                HandleEscape();
                return;
            }
            // The shell router owns frontend input; the gameplay HUD uses this bridge.
            if (!FrontendController.IsFrontendActive)
            {
                GamepadUIBridge.Pump(_root?.panel);
                PollQuickLines();
            }
            if (!enterHeld || _enterConsumed)
                return;
            _enterConsumed = true;
            if (!ChatInputGate.IsGameplayScene)
                return;
            if (UiModalState.Presented)
            {
                // The pause menu wins over chat (issue #216): chat neither
                // opens over the modal nor keeps focus, so the pause owner's
                // Escape is never swallowed by an armed gate.
                if (IsFocusInsideChatSurface())
                {
                    (_root?.panel?.focusController?.focusedElement as VisualElement)?.Blur();
                    ChatInputGate.End();
                }
                return;
            }
            if (!_expanded && !IsEditorFocused())
            {
                SetExpanded(true, true);
            }
            else if (_expanded && IsEditorFocused(_draft))
            {
                SendDraft(true);
            }
        }


        /// <summary>Gameplay chat owns Back only while its composer or focus is active.</summary>
        private bool OwnsBackPress()
        {
            if (UiModalState.Presented || !ChatInputGate.IsGameplayScene)
                return false;
            return _expanded || IsFocusInsideChatSurface();
        }


        /// <summary>Shell routing owns frontend focus; gameplay chat is
        /// focusable only while the composer is deliberately open.</summary>
        private void ApplyRegionFocusability()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive)
            {
                FrontendFocusRouter.Instance?.ApplyRegionFocusability();
                return;
            }
            bool composing = ChatInputGate.IsGameplayScene && _expanded;
            _root?.Query<Button>().ForEach(button => button.focusable = false);
            if (_draft != null)
                _draft.focusable = composing;
            if (_history != null)
                _history.focusable = false;
        }

        private void BindView()
        {
            if (_viewBound)
                return;

            _root = Resources.Load<VisualTreeAsset>("UI/Chat").CloneTree();
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.top = 0;
            _root.style.bottom = 0;
            _root.pickingMode = PickingMode.Ignore;
            _frame = _root.Q<VisualElement>("chat-root");
            if (_frame != null) _frame.pickingMode = PickingMode.Ignore;
            _lane = _root.Q<VisualElement>("chat-lane");
            _ticker = _root.Q<VisualElement>("chat-ticker");
            _hint = _root.Q<VisualElement>("chat-hint");
            _hintKeys = _root.Q<Label>("chat-hint-keys");
            _hintDm = _root.Q<Label>("chat-hint-dm");
            _laneCompose = _root.Q<VisualElement>("chat-lane-compose");
            _collapsed = _root.Q<VisualElement>("chat-collapsed");
            _open = _root.Q<Button>("chat-open");
            _unread = _root.Q<Label>("chat-unread");
            _peek = _root.Q<Label>("chat-peek");
            _presence = _root.Q<Label>("chat-presence");
            _panel = _root.Q<VisualElement>("chat-panel");
            _main = _root.Q<VisualElement>("chat-main");
            _directory = _root.Q<VisualElement>("chat-directory");
            _onlineList = _root.Q<ScrollView>("online-list");
            _onlineFeedback = _root.Q<Label>("online-feedback");
            _history = _root.Q<ScrollView>("chat-history");
            _historyFeedback = _root.Q<Label>("history-feedback");
            _newest = _root.Q<Button>("chat-newest");
            _refreshDirectory = _root.Q<Button>("directory-refresh");
            _online = _root.Q<Button>("chat-online");
            _close = _root.Q<Button>("chat-close");
            _compose = _root.Q<VisualElement>("chat-compose");
            _to = _root.Q<Button>("chat-to");
            _draft = _root.Q<TextField>("chat-draft");
            _keys = _root.Q<Label>("chat-keys");
            _send = _root.Q<Button>("chat-send");
            _resizeGrip = _root.Q<VisualElement>("chat-resize-grip");
            if (_resizeGrip != null)
            {
                _resizeGrip.RegisterCallback<PointerDownEvent>(BeginResize);
                _resizeGrip.RegisterCallback<PointerMoveEvent>(MoveResize);
                _resizeGrip.RegisterCallback<PointerUpEvent>(EndResize);
                _resizeGrip.RegisterCallback<PointerCaptureOutEvent>(_ => FinishResize());
            }

            BindFilter(StreamFilter.All, "chat-filter-all", "ALL");
            BindFilter(StreamFilter.Room, "chat-filter-room", "ROOM");
            BindFilter(StreamFilter.Global, "chat-filter-global", "GLOBAL");
            BindFilter(StreamFilter.Direct, "chat-filter-direct", "DMS");
            if (_online != null)
            {
                _onlineDot = new VisualElement { pickingMode = PickingMode.Ignore };
                _onlineDot.AddToClassList("chat-presence-dot");
                _onlineText = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                _online.Add(_onlineDot);
                _online.Add(_onlineText);
                _online.clicked += ToggleDirectory;
            }

            if (_open != null) _open.clicked += OnOpenButtonClicked;
            if (_close != null) _close.clicked += OnCloseButtonClicked;
            if (_newest != null) _newest.clicked += ScrollToNewest;
            if (_refreshDirectory != null) _refreshDirectory.clicked += RefreshDirectory;
            if (_send != null) _send.clicked += () => SendDraft(ChatInputGate.IsGameplayScene);
            if (_to != null) _to.clicked += () => CycleDestination(1);
            if (_keys != null) _keys.text = "TAB CHANNEL · ESC CLOSE";
            if (_history != null)
            {
                // Controller navigation of the retained history (issue #215):
                // a focused scroll view scrolls with directional input.
                _history.focusable = true;
                _history.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    if (_pinToNewest) ScrollToNewest();
                });
                _history.verticalScroller.valueChanged += _ =>
                {
                    UpdateScrollAnchor();
                    TryMarkViewRead();
                };
            }

            if (_draft != null)
            {
                _draft.maxLength = -1;
                // Reopening the composer continues the kept draft instead of selecting it.
                _draft.selectAllOnFocus = false;
                _draft.selectAllOnMouseUp = false;
                _draft.textEdition.placeholder = "Say something…";
                _draft.RegisterValueChangedCallback(OnDraftChanged);
                // Tab changes the destination instead of moving focus.
                _draft.RegisterCallback<KeyDownEvent>(OnDraftKeyDown, TrickleDown.TrickleDown);
                _draft.RegisterCallback<NavigationMoveEvent>(OnDraftNavigationMove, TrickleDown.TrickleDown);
                RegisterEditorFocus(_draft);
            }

            _root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            // Explicit chat interaction arms the gameplay input gate (issue
            // #216): any chat control gaining focus during a match suppresses
            // fighter input until focus leaves the chat surface. Gameplay
            // chat is unfocusable until the player opens the composer.
            // Focus events bubble, so these handlers cover the whole surface.
            _root.RegisterCallback<FocusInEvent>(OnChatSurfaceFocusIn);
            _root.RegisterCallback<FocusOutEvent>(OnChatSurfaceFocusOut);
            // Gamepad B (and keyboard cancel) with focus inside the chat
            // surface: one press leaves the interaction (issue #215). The page
            // root is not an ancestor of the chat surface, so this handler is
            // the only cancel path for chat-focused presses.
            _root.RegisterCallback<NavigationCancelEvent>(OnRootNavigationCancel);
            _tickerTick = _ticker?.schedule.Execute(UpdateTicker).Every(200);
            _viewBound = true;
            SetExpanded(false, false);
        }

        private void BindFilter(StreamFilter filter, string name, string text)
        {
            var button = _root?.Q<Button>(name);
            if (button == null)
                return;
            var title = new Label(text) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("chat-filter__name");
            var count = new Label { pickingMode = PickingMode.Ignore };
            count.AddToClassList("chat-filter__count");
            button.Add(title);
            button.Add(count);
            button.clicked += () => SetFilter(filter);
            _filters[filter] = (button, count);
        }

        private void RegisterEditorFocus(TextField field)
        {
            field.RegisterCallback<FocusInEvent>(_ => ChatInputGate.Begin());
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (!IsEditorFocused() && !(_expanded && ChatInputGate.IsGameplayScene))
                    ChatInputGate.End();
            });
        }

        private void TryBindSession()
        {
            if (_session != null)
                return;

            ChatSession? session = ChatSession.Instance;
            if (session == null)
                return;

            _session = session;
            _session.Changed += OnSessionChanged;
            _session.ConversationHistoryTrimmed += OnConversationHistoryTrimmed;
            _session.MessageArrived += OnMessageArrived;
            RenderSession();
        }

        private void OnSessionChanged()
        {
            if (this == null || !isActiveAndEnabled || _suppressDraftChanged)
                return;
            RenderSession();
        }

        /// <summary>
        /// Route one live message. In a match, Room lines from the people
        /// playing become speech slips over their fighters; everything public
        /// may reach the ticker. Muted senders reach neither.
        /// </summary>
        private void OnMessageArrived(ChatMessage message)
        {
            if (_session == null || !_session.IsMessageVisible(message))
                return;
            if (IsDirect(message))
                return; // Direct Messages only raise the quiet unread count.
            if (!_hostIsShell && _hud != null && IsRoomMessage(message) &&
                _hud.ShowChatSpeech(message.Sender.PlayerId, message.Sender.DisplayName, message.Text))
            {
                _spoken.Add(message.MessageId);
                _lastSpeechAt = Time.unscaledTime;
            }
            AddArrival(new Arrival { Message = message, At = Time.unscaledTime });
        }

        private void AddArrival(Arrival arrival)
        {
            _arrivals.Add(arrival);
            while (_arrivals.Count > MaxArrivals)
            {
                if (_arrivals[0].Message is { } dropped)
                    _spoken.Remove(dropped.MessageId);
                _arrivals.RemoveAt(0);
            }
        }

        private void RenderSession()
        {
            if (_root == null)
                return;

            if (_session == null)
            {
                SetText(_historyFeedback, "CHAT IS STARTING");
                return;
            }

            RenderComposer();
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                RenderHead();
                RenderCollapsed();
                RenderDirectory();
                RenderStream();
            }
            else
            {
                _tickerSignature = string.Empty;
                UpdateTicker();
            }
            // Dynamic rows are new Buttons with default focusability; re-apply
            // the active region's gating after every rebuild (issue #215).
            ApplyRegionFocusability();
        }

        // ── Destinations ────────────────────────────────────────────────────

        private string? RoomConversationKey =>
            _session?.JoinedRoomId is Guid roomId ? RoomKey(roomId) : null;

        private static string RoomKey(Guid roomId) => $"room:{roomId:D}";

        private static bool IsDirectKey(string key) => key.StartsWith("direct:", StringComparison.Ordinal);

        private static bool IsDirect(ChatMessage message) =>
            string.Equals(message.Channel, "direct", StringComparison.OrdinalIgnoreCase);

        private bool IsRoomMessage(ChatMessage message) =>
            string.Equals(message.Channel, "server", StringComparison.OrdinalIgnoreCase) &&
            message.RoomId is Guid roomId && _session?.JoinedRoomId == roomId;

        private ChatConversation? Conversation(string? key) =>
            key == null ? null : _session?.Conversations.FirstOrDefault(c => c.Key == key);

        /// <summary>Room, then Global, then recent online Direct conversations.</summary>
        private List<string> Destinations()
        {
            var keys = new List<string>();
            if (_session == null)
                return keys;
            if (RoomConversationKey is { } room)
                keys.Add(room);
            keys.Add("global");
            var online = new HashSet<string>(_session.OnlinePlayers.Select(p => p.PlayerId), StringComparer.Ordinal);
            foreach (var conversation in _session.Conversations
                         .Where(c => IsDirectKey(c.Key) && online.Contains(c.Key.Substring("direct:".Length)))
                         .OrderByDescending(LastActivity))
                keys.Add(conversation.Key);
            string? active = _session.ActiveConversation?.Key;
            if (active != null && !keys.Contains(active))
                keys.Add(active);
            return keys;
        }

        private static DateTimeOffset LastActivity(ChatConversation conversation) =>
            conversation.Messages.Count > 0 ? conversation.Messages[conversation.Messages.Count - 1].SentAt : DateTimeOffset.MinValue;

        private void CycleDestination(int direction)
        {
            if (_session?.ActiveConversation == null)
                return;
            var keys = Destinations();
            if (keys.Count < 2)
                return;
            int index = keys.IndexOf(_session.ActiveConversation.Key);
            int next = index < 0 ? 0 : (index + direction + keys.Count) % keys.Count;
            SelectDestination(keys[next]);
            UISFX.PlayClick();
        }

        /// <summary>
        /// Address the composer to another conversation. Text typed so far
        /// moves with the destination: the chip changes who receives it.
        /// </summary>
        private void SelectDestination(string key)
        {
            if (_session == null || _session.ActiveConversation?.Key == key)
                return;
            string text = _draft?.value ?? string.Empty;
            bool carry = text.Length > 0;
            _suppressDraftChanged = true;
            try
            {
                if (carry) _session.SetDraft(string.Empty);
                if (!_session.SelectConversation(key))
                {
                    if (carry) _session.SetDraft(text);
                    return;
                }
                if (carry) _session.SetDraft(text);
            }
            finally
            {
                _suppressDraftChanged = false;
            }
            RenderSession();
        }

        private string DestinationLabel(ChatConversation? conversation)
        {
            if (conversation == null) return "GLOBAL";
            if (conversation.Key == "global") return "GLOBAL";
            if (conversation.Key.StartsWith("room:", StringComparison.Ordinal)) return "ROOM";
            string name = conversation.Title;
            int separator = name.IndexOf(" // ", StringComparison.Ordinal);
            if (separator > 0) name = name.Substring(0, separator);
            return ChatSpeechSlips.Shorten(name, 14);
        }

        private void RenderComposer()
        {
            if (_session == null || _draft == null)
                return;
            var active = _session.ActiveConversation;
            if (_to != null)
            {
                string label = DestinationLabel(active);
                _to.text = _hostIsShell ? $"TO: {label}" : label;
                _to.enableRichText = false;
            }
            if (active == null)
                return;
            if (_draft.value != active.Draft)
                _draft.SetValueWithoutNotify(active.Draft);
            _lastDraft = active.Draft;
            bool canSend = active.CanSend && !active.IsSending && _session.IsConnected;
            _draft.SetEnabled(!active.IsSending);
            _send?.SetEnabled(canSend && IsMessageValid(active.Draft));
            if (_send != null)
                _send.text = active.IsSending ? "…" : "SEND";
        }

        // ── Menu: head, minimized tab, stream and directory ───────────────

        private void SetFilter(StreamFilter filter)
        {
            _showDirectory = false;
            _filter = filter;
            switch (filter)
            {
                case StreamFilter.Room when RoomConversationKey is { } room:
                    SelectDestination(room);
                    break;
                case StreamFilter.Global:
                    SelectDestination("global");
                    break;
                case StreamFilter.Direct when _session?.ActiveConversation is { } active && !IsDirectKey(active.Key):
                    var latest = _session.Conversations.Where(c => IsDirectKey(c.Key)).OrderByDescending(LastActivity).FirstOrDefault();
                    if (latest != null)
                        SelectDestination(latest.Key);
                    break;
            }
            RenderSession();
        }

        private void ToggleDirectory()
        {
            _showDirectory = !_showDirectory;
            RenderSession();
            if (_showDirectory)
                RefreshDirectory();
        }

        private void RenderHead()
        {
            if (_session == null)
                return;
            var room = Conversation(RoomConversationKey);
            var global = Conversation("global");
            int directUnread = _session.Conversations.Where(c => IsDirectKey(c.Key)).Sum(c => Math.Max(0, c.Unread));
            if (_filter == StreamFilter.Room && room == null)
                _filter = StreamFilter.All;
            SetFilterState(StreamFilter.All, 0, true);
            SetFilterState(StreamFilter.Room, room?.Unread ?? 0, room != null);
            SetFilterState(StreamFilter.Global, global?.Unread ?? 0, true);
            SetFilterState(StreamFilter.Direct, directUnread, true);

            if (_onlineText != null)
                _onlineText.text = PresenceText();
            _onlineDot?.EnableInClassList("chat-presence-dot--offline", !_session.IsConnected);
            _online?.EnableInClassList("chat-online--active", _showDirectory);
        }

        private void SetFilterState(StreamFilter filter, int unread, bool available)
        {
            if (!_filters.TryGetValue(filter, out var entry))
                return;
            entry.Button.SetEnabled(available);
            entry.Button.EnableInClassList("chat-filter--active", !_showDirectory && _filter == filter);
            entry.Count.text = unread > 99 ? "99+" : unread.ToString();
            entry.Count.EnableInClassList("chat-filter__count--visible", unread > 0 && filter != StreamFilter.All);
        }

        private string PresenceText()
        {
            if (_session == null || !_session.IsConnected)
                return "OFFLINE";
            if (!_session.DirectoryAvailable)
                return "PLAYERS";
            string? self = _session.Self?.PlayerId;
            int others = _session.OnlinePlayers.Count(p => p.PlayerId != self);
            return $"{others + 1} ONLINE";
        }

        private void RenderCollapsed()
        {
            if (_session == null)
                return;
            int unread = _session.Conversations.Sum(c => Math.Max(0, c.Unread));
            if (_unread != null)
            {
                _unread.text = unread > 99 ? "99+ NEW" : $"{unread} NEW";
                _unread.EnableInClassList("chat-unread--visible", unread > 0);
            }
            ChatMessage? latest = null;
            foreach (var conversation in _session.Conversations)
            {
                if (conversation.Unread <= 0 || IsDirectKey(conversation.Key))
                    continue;
                for (int i = conversation.Messages.Count - 1; i >= 0; i--)
                {
                    var message = conversation.Messages[i];
                    if (IsSelf(message.Sender) || !_session.IsMessageVisible(message))
                        continue;
                    if (latest == null || message.SentAt > latest.SentAt)
                        latest = message;
                    break;
                }
            }
            if (_peek != null)
            {
                _peek.enableRichText = false;
                _peek.text = latest == null ? string.Empty : $"{latest.Sender.DisplayName}  {OneLine(latest.Text)}";
                _peek.EnableInClassList("chat-peek--visible", latest != null);
            }
            if (_presence != null)
                _presence.text = PresenceText();
        }

        /// <summary>The conversations a view shows, in no particular order.</summary>
        private List<ChatConversation> ViewConversations(StreamFilter filter)
        {
            var result = new List<ChatConversation>();
            if (_session == null)
                return result;
            string? room = RoomConversationKey;
            foreach (var conversation in _session.Conversations)
            {
                bool isRoom = conversation.Key == room;
                bool isGlobal = conversation.Key == "global";
                bool isDirect = IsDirectKey(conversation.Key);
                if (filter switch
                    {
                        StreamFilter.Room => isRoom,
                        StreamFilter.Global => isGlobal,
                        StreamFilter.Direct => isDirect,
                        _ => isRoom || isGlobal || isDirect,
                    })
                    result.Add(conversation);
            }
            return result;
        }

        private string ViewKey => $"view:{_filter}";

        private void RenderStream()
        {
            if (_history == null || _session == null)
                return;
            _history.SetDisplayed(!_showDirectory);
            if (_showDirectory)
            {
                _newest?.SetDisplayed(false);
                return;
            }

            var conversations = ViewConversations(_filter);
            var rows = new List<(ChatMessage Message, string Key)>();
            foreach (var conversation in conversations)
                foreach (var message in conversation.Messages)
                    if (_session.IsMessageVisible(message))
                        rows.Add((message, conversation.Key));
            rows.Sort((a, b) =>
            {
                int byTime = a.Message.SentAt.CompareTo(b.Message.SentAt);
                return byTime != 0 ? byTime : a.Message.Sequence.CompareTo(b.Message.Sequence);
            });
            if (rows.Count > MaxStreamRows)
                rows.RemoveRange(0, rows.Count - MaxStreamRows);

            var active = _session.ActiveConversation;
            string feedback = active?.Feedback ?? string.Empty;
            if (string.IsNullOrEmpty(feedback) && !_session.IsConnected)
                feedback = _session.Status;
            string viewKey = ViewKey;
            string signature = $"{viewKey}|{rows.Count}|{(rows.Count > 0 ? rows[rows.Count - 1].Message.MessageId : Guid.Empty)}|{_session.MutedPlayers.Count}|{_session.Self?.PlayerId}|{_session.IsConnected}";

            bool changedView = viewKey != _lastViewKey;
            // A pending eviction reset (issue #214) wins over the live scroller
            // state: the live anchor still reflects the evicted reading
            // position, so return to newest instead of re-capturing it.
            bool evictionReset = _evictionReset.Remove(viewKey);
            if (!evictionReset && !changedView)
                UpdateScrollAnchor(); // keep the reading position across re-renders (issue #214)
            bool atNewest = evictionReset || (changedView ? IsPinnedNewest(viewKey) : _pinToNewest);
            if (evictionReset)
                _pinnedNewest[viewKey] = true;
            Vector2 oldOffset = changedView ? GetScrollOffset(viewKey) : _history.scrollOffset;

            if (signature != _streamSignature || changedView)
            {
                _streamSignature = signature;
                _history.Clear();
                var names = DuplicateNames(rows.Select(r => r.Message.Sender));
                foreach (var (message, key) in rows)
                    _history.Add(BuildRow(message, key, names, ticker: false));
                if (rows.Count == 0)
                {
                    var empty = new Label(EmptyViewText()) { enableRichText = false };
                    empty.AddToClassList("chat-empty");
                    _history.Add(empty);
                }
                if (_historyFeedback != null)
                    _history.Add(_historyFeedback);
            }
            _lastViewKey = viewKey;
            _pinToNewest = atNewest;
            SetText(_historyFeedback, feedback);
            _historyFeedback?.EnableInClassList("chat-feedback--success",
                string.Equals(feedback, "Sent", StringComparison.Ordinal));
            _historyFeedback?.EnableInClassList("chat-feedback--pending", active?.IsSending == true);

            bool hasRows = rows.Count > 0;
            _history.schedule.Execute(() =>
            {
                if (atNewest)
                    ScrollToNewest();
                else
                    _history.scrollOffset = oldOffset;
                _newest?.SetDisplayed(!atNewest && hasRows && !_showDirectory);
                TryMarkViewRead();
            }).StartingIn(0);
        }

        private string EmptyViewText() => _filter switch
        {
            StreamFilter.Room => "NO ROOM MESSAGES YET",
            StreamFilter.Direct => "NO DIRECT MESSAGES. OPEN THE PLAYER LIST TO START ONE.",
            _ => _session?.IsConnected == true ? "NO MESSAGES YET" : string.Empty,
        };

        private void RenderDirectory()
        {
            _directory?.EnableInClassList("chat-directory--visible", _showDirectory);
            if (!_showDirectory || _onlineList == null || _session == null)
                return;

            _onlineList.Clear();
            if (!_session.DirectoryAvailable)
            {
                SetText(_onlineFeedback, string.IsNullOrEmpty(_session.Status)
                    ? "ONLINE DIRECTORY UNAVAILABLE"
                    : _session.Status);
            }
            else
            {
                SetText(_onlineFeedback, "PICK A PLAYER TO MESSAGE THEM");
                ChatPlayer? self = _session.Self;
                var players = _session.OnlinePlayers.Where(p => self == null || p.PlayerId != self.PlayerId).ToList();
                var names = DuplicateNames(players);
                foreach (ChatPlayer player in players)
                {
                    var row = new VisualElement();
                    row.AddToClassList("online-row");
                    var select = new Button(() => OpenDirect(player))
                    {
                        text = FormatIdentity(player, names.Contains(player.DisplayName)),
                        enableRichText = false
                    };
                    select.AddToClassList("online-player");
                    row.Add(select);

                    var mute = new Button(() =>
                    {
                        _session.SetMuted(player.PlayerId, !_session.IsMuted(player.PlayerId));
                        RenderSession();
                    });
                    mute.text = _session.IsMuted(player.PlayerId) ? "UNMUTE" : "MUTE";
                    mute.AddToClassList("chat-mute");
                    row.Add(mute);
                    _onlineList.Add(row);
                }
            }

            if (_session.MutedPlayers.Count > 0)
            {
                var mutedLabel = new Label("MUTED PLAYERS") { enableRichText = false };
                mutedLabel.AddToClassList("chat-section-label");
                _onlineList.Add(mutedLabel);
                foreach (var player in _session.MutedPlayers)
                {
                    var unmute = new Button(() => _session.SetMuted(player.PlayerId, false))
                    {
                        text = $"UNMUTE {FormatIdentity(player, true)}",
                        enableRichText = false
                    };
                    unmute.AddToClassList("chat-mute");
                    _onlineList.Add(unmute);
                }
            }
        }

        private void OpenDirect(ChatPlayer player)
        {
            if (_session == null)
                return;
            string text = _draft?.value ?? string.Empty;
            bool carry = text.Length > 0;
            _suppressDraftChanged = true;
            try
            {
                if (carry) _session.SetDraft(string.Empty);
                if (!_session.OpenDirect(player))
                {
                    if (carry) _session.SetDraft(text);
                    return;
                }
                if (carry) _session.SetDraft(text);
            }
            finally
            {
                _suppressDraftChanged = false;
            }
            _showDirectory = false;
            if (_filter is StreamFilter.Room or StreamFilter.Global)
                _filter = StreamFilter.All;
            RenderSession();
            if (_hostIsShell)
                FocusComposerLater();
        }

        // ── Message rows (menu stream, gameplay ticker and context) ────────

        private VisualElement BuildRow(ChatMessage message, string conversationKey,
            HashSet<string> duplicateNames, bool ticker)
        {
            var row = new VisualElement();
            row.AddToClassList("chat-row");
            if (ticker)
            {
                row.AddToClassList("chat-line");
                row.pickingMode = PickingMode.Ignore;
            }
            else
            {
                // Clicking a line replies where it was said.
                row.RegisterCallback<ClickEvent>(_ => SelectDestination(conversationKey));
            }

            string channel = conversationKey == "global" ? "GLOBAL" : IsDirectKey(conversationKey) ? "DM" : "ROOM";
            var chip = new Label(channel) { enableRichText = false, pickingMode = PickingMode.Ignore };
            chip.AddToClassList("chat-row__chip");
            chip.EnableInClassList("chat-row__chip--room", channel == "ROOM");
            chip.EnableInClassList("chat-row__chip--dm", channel == "DM");
            row.Add(chip);

            bool self = IsSelf(message.Sender);
            string shownName = ChatSpeechSlips.Shorten(message.Sender.DisplayName, MaxRowNameElements);
            var name = new Label(duplicateNames.Contains(message.Sender.DisplayName) && !string.IsNullOrEmpty(message.Sender.SessionTag)
                ? $"{shownName} [{message.Sender.SessionTag}]"
                : shownName)
            {
                enableRichText = false,
                pickingMode = PickingMode.Ignore
            };
            name.AddToClassList("chat-row__name");
            name.EnableInClassList("chat-row__name--you", self);
            if (!self && ticker && channel == "ROOM" && _hud != null &&
                _hud.TryGetSpeakerColor(message.Sender.PlayerId, out var identity))
                name.style.color = identity;
            row.Add(name);

            if (channel == "DM")
            {
                string toText = self ? $"TO {DestinationLabel(Conversation(conversationKey))}" : "TO YOU";
                var to = new Label(toText) { enableRichText = false, pickingMode = PickingMode.Ignore };
                to.AddToClassList("chat-row__to");
                row.Add(to);
            }

            string text = ticker ? ChatSpeechSlips.Shorten(OneLine(message.Text), MaxLineTextElements) : message.Text;
            var body = new Label(text) { enableRichText = false, pickingMode = PickingMode.Ignore };
            body.AddToClassList("chat-row__text");
            row.Add(body);
            return row;
        }

        private static VisualElement BuildSystemRow(string text)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("chat-row");
            row.AddToClassList("chat-line");
            row.AddToClassList("chat-row--system");
            var body = new Label(ChatSpeechSlips.Shorten(OneLine(text), MaxLineTextElements))
            {
                enableRichText = false,
                pickingMode = PickingMode.Ignore
            };
            body.AddToClassList("chat-row__text");
            row.Add(body);
            return row;
        }

        /// <summary>Display names used by more than one identity in a set (Session Tag needed).</summary>
        private static HashSet<string> DuplicateNames(IEnumerable<ChatPlayer> players)
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var player in players)
            {
                if (player == null) continue;
                if (owners.TryGetValue(player.DisplayName, out var owner))
                {
                    if (owner != player.PlayerId)
                        duplicates.Add(player.DisplayName);
                }
                else
                {
                    owners[player.DisplayName] = player.PlayerId;
                }
            }
            return duplicates;
        }

        private bool IsSelf(ChatPlayer? player) =>
            player != null && _session?.Self != null &&
            string.Equals(player.PlayerId, _session.Self.PlayerId, StringComparison.Ordinal);

        private static string OneLine(string text) =>
            (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

        // ── Gameplay: ticker, hint, context and quick lines ─────────────────

        private bool IsGameplayPresentation =>
            !(_hostIsShell && FrontendController.IsFrontendActive) && ChatInputGate.IsGameplayScene;

        /// <summary>Rebuild the lane when its content changes; fade lines every tick.</summary>
        private void UpdateTicker()
        {
            if (_ticker == null || _session == null || !IsGameplayPresentation)
                return;

            float now = Time.unscaledTime;
            var lines = new List<(VisualElement Row, float At, bool Fades)>();
            string signature;
            if (_expanded)
            {
                // Composing: the last few lines from the Room, Global and the
                // addressed Direct conversation, fully visible for context.
                var keys = new List<string> { "global" };
                if (RoomConversationKey is { } room) keys.Add(room);
                if (_session.ActiveConversation is { } active && IsDirectKey(active.Key)) keys.Add(active.Key);
                var context = new List<(ChatMessage Message, string Key)>();
                foreach (var key in keys)
                    if (Conversation(key) is { } conversation)
                        foreach (var message in conversation.Messages)
                            if (_session.IsMessageVisible(message))
                                context.Add((message, key));
                context.Sort((a, b) => a.Message.SentAt.CompareTo(b.Message.SentAt));
                if (context.Count > ContextLines)
                    context.RemoveRange(0, context.Count - ContextLines);
                string feedback = _session.ActiveConversation?.Feedback ?? string.Empty;
                if (string.Equals(feedback, "Sent", StringComparison.Ordinal) ||
                    string.Equals(feedback, "Sending…", StringComparison.Ordinal))
                    feedback = string.Empty;
                if (string.IsNullOrEmpty(feedback) && !_session.IsConnected)
                    feedback = _session.Status;
                signature = $"ctx|{string.Join(",", context.Select(c => c.Message.MessageId))}|{feedback}";
                if (signature != _tickerSignature)
                {
                    var names = DuplicateNames(context.Select(c => c.Message.Sender));
                    foreach (var (message, key) in context)
                        lines.Add((BuildRow(message, key, names, ticker: true), now, false));
                    if (!string.IsNullOrEmpty(feedback))
                        lines.Add((BuildSystemRow(feedback), now, false));
                }
                // The open composer shows these at their newest messages.
                if (Application.isFocused && !UiModalState.Presented)
                    foreach (var key in keys)
                        _session.MarkRead(key);
            }
            else
            {
                var visible = new List<Arrival>();
                string? room = RoomConversationKey;
                for (int i = _arrivals.Count - 1; i >= 0 && visible.Count < TickerLines; i--)
                {
                    var arrival = _arrivals[i];
                    float lifetime = arrival.SystemText != null ? SystemLineSeconds : TickerLifetimeSeconds;
                    if (now - arrival.At >= lifetime)
                        break;
                    if (arrival.Message is { } message)
                    {
                        bool isPublic = message.Channel.Equals("global", StringComparison.OrdinalIgnoreCase) ||
                            (IsRoomMessage(message) && room != null);
                        if (!isPublic || _spoken.Contains(message.MessageId) || !_session.IsMessageVisible(message))
                            continue;
                    }
                    visible.Add(arrival);
                }
                visible.Reverse();
                signature = $"tick|{string.Join(",", visible.Select(a => a.Message?.MessageId.ToString() ?? a.SystemText))}";
                if (signature != _tickerSignature)
                {
                    var names = DuplicateNames(visible.Where(a => a.Message != null).Select(a => a.Message!.Sender));
                    foreach (var arrival in visible)
                    {
                        var row = arrival.Message is { } message
                            ? BuildRow(message, message.Channel.Equals("global", StringComparison.OrdinalIgnoreCase) ? "global" : room ?? "global", names, ticker: true)
                            : BuildSystemRow(arrival.SystemText!);
                        lines.Add((row, arrival.At, true));
                    }
                }
            }

            if (signature != _tickerSignature)
            {
                _tickerSignature = signature;
                _ticker.Clear();
                foreach (var (row, at, fades) in lines)
                {
                    row.userData = fades ? at : (object?)null;
                    _ticker.Add(row);
                }
            }

            // Fade the passive ticker; the composer context stays fully visible.
            bool anyLine = false;
            foreach (var child in _ticker.Children())
            {
                anyLine = true;
                if (child.userData is not float at)
                {
                    child.style.opacity = 1f;
                    continue;
                }
                float lifetime = child.ClassListContains("chat-row--system") ? SystemLineSeconds : TickerLifetimeSeconds;
                float remaining = lifetime - (now - at);
                child.style.opacity = Mathf.Clamp01(remaining / TickerFadeSeconds);
            }
            RenderHint(anyLine);
        }

        private void RenderHint(bool tickerVisible)
        {
            if (_hint == null || _session == null)
                return;
            int directUnread = _session.Conversations.Where(c => IsDirectKey(c.Key)).Sum(c => Math.Max(0, c.Unread));
            bool recentSpeech = Time.unscaledTime - _lastSpeechAt < TickerLifetimeSeconds;
            bool show = !_expanded && (tickerVisible || recentSpeech || directUnread > 0);
            _hint.EnableInClassList("chat-hint--visible", show);
            if (!show)
                return;
            bool pad = HumanInputActions.LastUsedGamepad && Gamepad.current != null;
            if (_hintKeys != null)
                _hintKeys.text = pad
                    ? QuickLinesAvailable ? "D-PAD  ↑ GG  ← NICE  ↓ ONE MORE?" : "KEYBOARD  ENTER CHAT"
                    : "ENTER  CHAT";
            if (_hintDm != null)
            {
                _hintDm.text = $"DM {directUnread}";
                _hintDm.EnableInClassList("chat-hint__dm--visible", directUnread > 0);
            }
        }

        private bool QuickLinesAvailable =>
            _session != null && _session.IsConnected && RoomConversationKey != null &&
            MatchConfig.Mode == GameMode.PvP && ChatInputGate.IsGameplayScene;

        /// <summary>D-pad up/left/down send a quick line to the Room during an online match.</summary>
        private void PollQuickLines()
        {
            var pad = Gamepad.current;
            if (pad == null || _expanded || _session == null || !QuickLinesAvailable)
                return;
            int index = pad.dpad.up.wasPressedThisFrame ? 0
                : pad.dpad.left.wasPressedThisFrame ? 1
                : pad.dpad.down.wasPressedThisFrame ? 2
                : -1;
            if (index < 0 || UiModalState.Presented || ChatInputGate.SuppressShortcuts ||
                HumanInputActions.IsCapturing || SettingsOverlay.Active is { IsOpen: true } ||
                MatchPauseMenu.Active is { IsPaused: true } || Time.unscaledTime < _nextQuickLineAt)
                return;
            _nextQuickLineAt = Time.unscaledTime + QuickLineCooldownSeconds;
            _ = SendQuickLineAsync(RoomConversationKey!, QuickLines[index]);
        }

        private async Task SendQuickLineAsync(string key, string text)
        {
            if (_session == null)
                return;
            try { await _session.SendQuickLineAsync(key, text); }
            catch (Exception exception) { Debug.LogException(exception); }
            string feedback = Conversation(key)?.Feedback ?? string.Empty;
            if (feedback.Length > 0 && !string.Equals(feedback, "Sent", StringComparison.Ordinal))
            {
                AddArrival(new Arrival { SystemText = feedback, At = Time.unscaledTime });
                _tickerSignature = string.Empty;
                UpdateTicker();
            }
        }

        // ── Input ───────────────────────────────────────────────────────────

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (IsEnter(evt.keyCode) && ChatInputGate.IsGameplayScene)
            {
                evt.StopImmediatePropagation();
                return;
            }
            if (evt.keyCode == KeyCode.Escape && OwnsBackPress())
            {
                evt.StopImmediatePropagation();
                HandleEscape();
                return;
            }

            if (!IsEnter(evt.keyCode) || !_expanded || !IsEditorFocused(_draft))
                return;

            evt.StopImmediatePropagation();
            SendDraft(false);
        }

        /// <summary>Tab in the composer changes the destination instead of moving focus.</summary>
        private void OnDraftKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Tab && evt.character != '\t')
                return;
            if (evt.keyCode == KeyCode.Tab)
                CycleDestination(evt.shiftKey ? -1 : 1);
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        private void OnDraftNavigationMove(NavigationMoveEvent evt)
        {
            if (evt.direction is not (NavigationMoveEvent.Direction.Next or NavigationMoveEvent.Direction.Previous))
                return;
            _root?.panel?.focusController?.IgnoreEvent(evt);
            evt.StopPropagation();
        }

        /// <summary>Cancel with focus inside the chat surface (issue #215):
        /// one press leaves the interaction; it never reaches a page Back
        /// handler because the page root is not an ancestor of the chat
        /// surface.</summary>
        private void OnRootNavigationCancel(NavigationCancelEvent evt)
        {
            if (!OwnsBackPress())
                return;
            evt.StopImmediatePropagation();
            HandleEscape();
        }

        /// <summary>Combat chat interaction is explicit (issue #216): any
        /// chat control gaining focus during gameplay arms the input gate.
        /// Gameplay chat is unfocusable until the composer opens.</summary>
        private void OnChatSurfaceFocusIn(FocusInEvent evt)
        {
            if (!ChatInputGate.IsGameplayScene)
                return;
            ChatInputGate.Begin();
        }

        /// <summary>Focus leaving the chat surface ends an unexpanded
        /// gameplay navigation session. An open composer keeps the gate until
        /// it is closed — SetExpanded/HandleEscape own that transition.</summary>
        private void OnChatSurfaceFocusOut(FocusOutEvent evt)
        {
            if (!ChatInputGate.IsGameplayScene || _expanded)
                return;
            if (evt.relatedTarget is VisualElement next && _root != null && _root.Contains(next))
                return;
            ChatInputGate.End();
        }

        private bool IsFocusInsideChatSurface()
        {
            return _root?.panel?.focusController?.focusedElement is VisualElement focused
                && _root.Contains(focused);
        }

        /// <summary>Move gameplay focus off the chat surface onto a parked,
        /// invisible focusable element on the HUD root before the composer
        /// hides (issue #216). Focus then survives the hide, so no rescue
        /// pass can re-focus chat and re-arm the gate.</summary>
        private void ParkFocusOutsideChat()
        {
            if (!ChatInputGate.IsGameplayScene)
                return;
            var hostRoot = FindFirstObjectByType<HUDManager>()?.Document?.rootVisualElement;
            if (hostRoot == null)
                return;
            if (_focusParker == null)
            {
                _focusParker = new VisualElement { name = "chat-focus-parker", focusable = true };
                _focusParker.pickingMode = PickingMode.Ignore;
                _focusParker.style.width = 0;
                _focusParker.style.height = 0;
            }
            if (_focusParker.parent != hostRoot)
            {
                _focusParker.RemoveFromHierarchy();
                hostRoot.Add(_focusParker);
            }
            _focusParker.Focus();
        }


        private void OnDraftChanged(ChangeEvent<string> evt)
        {
            if (_suppressDraftChanged) return;
            if (ChatTextValidation.CountUnicodeScalars(evt.newValue) > MaxMessageScalars)
            {
                _draft?.SetValueWithoutNotify(_lastDraft);
                if (_session?.ActiveConversation is { } active)
                {
                    active.Feedback = "MESSAGE MUST BE 500 UNICODE CHARACTERS OR FEWER";
                    RenderSession();
                }
                return;
            }
            _lastDraft = evt.newValue;
            _suppressDraftChanged = true;
            _session?.SetDraft(evt.newValue);
            _suppressDraftChanged = false;
            _send?.SetEnabled(_session?.ActiveConversation?.CanSend == true && IsMessageValid(evt.newValue));
        }

        private async void SendDraft(bool closeAfterSend)
        {
            if (_session == null || _session.ActiveConversation == null) return;
            var conversation = _session.ActiveConversation;
            _session.SetDraft(_draft?.value ?? string.Empty);
            if (closeAfterSend) SetExpanded(false, false);
            if (conversation.IsSending) return;
            try { await _session.SendDraftAsync(); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally
            {
                // A failed send from the closed combat composer still needs a word.
                string feedback = conversation.Feedback;
                if (closeAfterSend && !_expanded && feedback.Length > 0 &&
                    !string.Equals(feedback, "Sent", StringComparison.Ordinal))
                    AddArrival(new Arrival { SystemText = feedback, At = Time.unscaledTime });
                RenderSession();
            }
        }

        private async void RefreshDirectory()
        {
            if (_session == null)
                return;
            try
            {
                await _session.RefreshDirectoryAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                RenderSession();
            }
        }

        private void SetExpanded(bool expanded, bool focusComposer)
        {
            _expanded = expanded;
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                ApplyPresentation();
                return;
            }
            if (!expanded)
            {
                if (IsFocusInsideChatSurface())
                    (_root?.panel?.focusController?.focusedElement as VisualElement)?.Blur();
                _draft?.Blur();
                ChatInputGate.End();
            }
            if (ChatInputGate.IsGameplayScene)
            {
                if (expanded)
                    ChatInputGate.Begin();
                else
                    // Park gameplay focus off the chat surface BEFORE the
                    // composer hides: the fixup that re-focuses chat runs
                    // only when focus was lost to the hide (issue #216).
                    ParkFocusOutsideChat();
            }
            ApplyPresentation();
            RenderSession();
            if (focusComposer)
                FocusComposerLater();
        }

        /// <summary>Focus the composer next frame with the caret after any kept draft.</summary>
        private void FocusComposerLater()
        {
            _root?.schedule.Execute(() =>
            {
                if (_draft == null)
                    return;
                _draft.Focus();
                int end = _draft.value?.Length ?? 0;
                _draft.SelectRange(end, end);
            }).StartingIn(0);
        }

        /// <summary>Restore the docked stream from the minimized tab.</summary>
        private void OnOpenButtonClicked()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
                RestoreFromMinimized();
        }

        /// <summary>Minimize the docked stream.</summary>
        private void OnCloseButtonClicked()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                MinimizeChat();
                return;
            }
            SetExpanded(false, false);
        }

        /// <summary>
        /// Restore the docked stream from the minimized tab (issue #220):
        /// a manual restore is remembered across launches.
        /// </summary>
        private void RestoreFromMinimized()
        {
            if (UiModalState.Presented)
                return;
            _expanded = true;
            _dockOpen = true;
            PlayerPrefs.SetInt(DockOpenPlayerPrefsKey, 1);
            PlayerPrefs.Save();
            ApplyPresentation();
            RenderSession();
            FrontendFocusRouter.NotifyPresentationChanged();
            FocusComposerLater();
        }

        /// <summary>
        /// Explicit manual minimization (issue #220): the dock hides into the
        /// slim tab inside the absolute host; the preference is remembered
        /// and never rewritten by resizing.
        /// </summary>
        private void MinimizeChat()
        {
            _expanded = false;
            _dockOpen = false;
            PlayerPrefs.SetInt(DockOpenPlayerPrefsKey, 0);
            PlayerPrefs.Save();
            _draft?.Blur();
            ApplyPresentation();
            RenderSession();
            FrontendFocusRouter.NotifyPresentationChanged();
            FrontendFocusRouter.Instance?.RestorePageRegion();
        }


        /// <summary>Blur the chat's text fields without changing state; the
        /// focus router calls this when leaving the social interaction.</summary>
        public void BlurChatEditors()
        {
            _draft?.Blur();
            if (FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
                ChatInputGate.End();
        }


        /// <summary>Gameplay Escape closes the composer, keeping the draft.</summary>
        private void HandleEscape()
        {
            SetExpanded(false, false);
        }


        /// <summary>Docked stream or minimized tab in menus; ticker and composer line in gameplay.</summary>
        private void ApplyPresentation()
        {
            if (_root == null)
                return;
            bool menu = _hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene;
            _frame?.EnableInClassList("chat-root--menu", menu);
            _frame?.EnableInClassList("chat-root--gameplay", !menu);
            if (menu)
            {
                // The host owns the user-sized geometry; no page space is
                // reserved, so resizing never rebalances the page underneath.
                if (_compose != null && _main != null && _compose.parent != _main)
                    _main.Add(_compose);
                _panel?.SetDisplayed(_expanded);
                _collapsed?.SetDisplayed(!_expanded);
                _laneCompose?.EnableInClassList("chat-lane-compose--open", false);
            }
            else
            {
                if (_compose != null && _laneCompose != null && _compose.parent != _laneCompose)
                    _laneCompose.Add(_compose);
                _panel?.SetDisplayed(false);
                _collapsed?.SetDisplayed(false);
                _laneCompose?.EnableInClassList("chat-lane-compose--open", _expanded);
                _tickerSignature = string.Empty;
            }
            if (_draft != null)
                _draft.textEdition.placeholder = menu ? "Say something…" : "Say something, Enter to send";
            ApplyRegionFocusability();
        }

        private float PhysicalPixelsPerPoint()
        {
            float panelScale = _root?.panel?.scaledPixelsPerPoint ?? 1f;
            float uiScale = ClientSettingsService.Instance.UiScale / 100f;
            return panelScale / Mathf.Max(0.01f, uiScale);
        }

        private void BeginResize(PointerDownEvent evt)
        {
            if (evt.button != 0 || _resizePointerId >= 0 || _resizeGrip == null || _root?.panel == null
                || !_expanded || !_hostIsShell || UiModalState.Presented)
                return;
            var source = FrontendController.Shell?.SocialHost;
            if (source == null)
                return;
            float factor = PhysicalPixelsPerPoint();
            _resizePointerId = evt.pointerId;
            _resizeStartPosition = new Vector2(evt.position.x, evt.position.y);
            _resizeStartWidthPx = source.resolvedStyle.width * factor;
            _resizeStartHeightPx = source.resolvedStyle.height * factor;
            _resizeDirty = false;
            _resizeGrip.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void MoveResize(PointerMoveEvent evt)
        {
            if (_resizePointerId != evt.pointerId || _resizeGrip?.HasPointerCapture(evt.pointerId) != true
                || _root == null)
                return;
            var viewport = FrontendController.Shell?.Root;
            if (viewport == null)
                return;
            float factor = PhysicalPixelsPerPoint();
            float widthLimit = Mathf.Max(120f, Mathf.Min(960f, viewport.resolvedStyle.width * factor - 48f));
            float heightLimit = Mathf.Max(120f, Mathf.Min(720f, viewport.resolvedStyle.height * factor * 0.65f));
            float deltaX = (evt.position.x - _resizeStartPosition.x) * factor;
            float deltaY = (_resizeStartPosition.y - evt.position.y) * factor;
            _preferredWidthPx = Mathf.Clamp(_resizeStartWidthPx + deltaX,
                Mathf.Min(300f, widthLimit), widthLimit);
            _preferredHeightPx = Mathf.Clamp(_resizeStartHeightPx + deltaY,
                Mathf.Min(180f, heightLimit), heightLimit);
            _resizeDirty = true;
            FrontendController.Shell?.SetSocialCellSize(_preferredWidthPx, _preferredHeightPx);
            evt.StopPropagation();
        }

        private void EndResize(PointerUpEvent evt)
        {
            if (_resizePointerId != evt.pointerId)
                return;
            FinishResize();
            evt.StopPropagation();
        }

        private void FinishResize()
        {
            if (_resizePointerId < 0)
                return;
            int pointerId = _resizePointerId;
            _resizePointerId = -1;
            if (_resizeGrip?.HasPointerCapture(pointerId) == true)
                _resizeGrip.ReleasePointer(pointerId);
            if (!_resizeDirty)
                return;
            _resizeDirty = false;
            PlayerPrefs.SetFloat(WidthPlayerPrefsKey, _preferredWidthPx);
            PlayerPrefs.SetFloat(HeightPlayerPrefsKey, _preferredHeightPx);
            PlayerPrefs.Save();
        }

        /// <summary>The stream must be visibly laid out, not minimized,
        /// detached, replaced by the directory, or a gameplay preview.</summary>
        private bool IsStreamShown()
        {
            return _hostIsShell && _expanded && !_showDirectory && _root?.panel != null
                && _history?.contentRect.height > 0f;
        }

        /// <summary>Read requires a visible laid-out stream, focused
        /// application, unobscured modal state, and the newest messages.</summary>
        private bool ShouldMarkViewRead()
        {
            if (_session == null || !IsStreamShown() || !Application.isFocused || UiModalState.Presented)
                return false;
            return _pinToNewest || IsAtNewest();
        }

        private void TryMarkViewRead()
        {
            if (_session == null || !ShouldMarkViewRead())
                return;
            bool changed = false;
            foreach (var conversation in ViewConversations(_filter))
            {
                if (conversation.Unread <= 0)
                    continue;
                _session.MarkRead(conversation.Key);
                changed = true;
            }
            if (changed)
            {
                RenderHead();
                RenderCollapsed();
            }
        }

        /// <summary>Store the current view's reading anchor while the stream is laid out.</summary>
        private void UpdateScrollAnchor()
        {
            if (_history == null || !IsStreamShown())
                return;
            bool pinned = IsAtNewest();
            _pinToNewest = pinned;
            _pinnedNewest[ViewKey] = pinned;
            _scrollOffsets[ViewKey] = _history.scrollOffset;
        }

        private bool IsPinnedNewest(string key) =>
            _pinnedNewest.TryGetValue(key, out bool pinned) ? pinned : true;

        private Vector2 GetScrollOffset(string key) =>
            _scrollOffsets.TryGetValue(key, out Vector2 offset) ? offset : Vector2.zero;

        /// <summary>
        /// The bounded history buffer evicted older messages the player was
        /// reading (issue #214). Record a pending return-to-newest for the
        /// next render of every view showing that conversation; the
        /// explanation lives on the conversation Feedback.
        /// </summary>
        private void OnConversationHistoryTrimmed(string key)
        {
            if (_session == null)
                return;
            bool anyReading = false;
            foreach (StreamFilter filter in Enum.GetValues(typeof(StreamFilter)))
            {
                string view = $"view:{filter}";
                if (IsPinnedNewest(view) || ViewConversations(filter).All(c => c.Key != key))
                    continue;
                _evictionReset.Add(view);
                anyReading = true;
            }
            if (!anyReading)
                return;
            var conversation = _session.Conversations.FirstOrDefault(c => c.Key == key);
            if (conversation != null && string.IsNullOrEmpty(conversation.Feedback))
                conversation.Feedback =
                    "Older messages left the history buffer (the newest 50 are kept). Returned to the newest messages.";
        }

        private void ScrollToNewest()
        {
            if (_history == null || _history.childCount == 0)
                return;
            _history.verticalScroller.value = Mathf.Max(0f, _history.verticalScroller.highValue);
            _newest?.SetDisplayed(false);
        }

        private bool IsAtNewest()
        {
            if (_history == null)
                return true;
            float high = _history.verticalScroller.highValue;
            // Nothing to scroll (or no layout yet) means the newest line is in view.
            return !(high > 0f) || _history.verticalScroller.value >= high - 1f;
        }

        private bool IsEditorFocused()
        {
            if (_root?.panel?.focusController?.focusedElement is not VisualElement focused)
                return false;
            return IsWithin(focused, _draft);
        }

        private bool IsEditorFocused(TextField? field)
        {
            return _root?.panel?.focusController?.focusedElement is VisualElement focused && IsWithin(focused, field);
        }

        private static bool IsWithin(VisualElement focused, VisualElement? field)
        {
            return field != null && (focused == field || field.Contains(focused));
        }


        private static bool IsEnter(KeyCode keyCode)
        {
            return keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter;
        }

        private static bool IsMessageValid(string value)
        {
            return ChatTextValidation.TryValidateMessage(value, out _, out _);
        }

        /// <summary>Display Name, plus the Session Tag when another identity shares the name.</summary>
        private static string FormatIdentity(ChatPlayer player, bool withTag)
        {
            return !withTag || string.IsNullOrEmpty(player.SessionTag)
                ? player.DisplayName
                : $"{player.DisplayName} [{player.SessionTag}]";
        }

        private static void SetText(Label? label, string? text)
        {
            if (label == null)
                return;
            label.enableRichText = false;
            label.text = text ?? string.Empty;
            label.SetDisplayed(!string.IsNullOrEmpty(text));
        }
    }

    internal static class ChatOverlayVisualElementExtensions
    {
        public static void SetDisplayed(this VisualElement element, bool displayed)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
