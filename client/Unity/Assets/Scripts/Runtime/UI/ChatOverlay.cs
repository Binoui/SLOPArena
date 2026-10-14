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
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class ChatOverlay : MonoBehaviour
    {
        private const int MaxMessageScalars = 500;
        private const double CompactLifetimeSeconds = 8d;

        public const string DockOpenPlayerPrefsKey = "SlopArena.Chat.DockOpen";
        private const string WidthPlayerPrefsKey = "SlopArena.Chat.WidthPx";
        private const string HeightPlayerPrefsKey = "SlopArena.Chat.HeightPx";

        private static ChatOverlay? _instance;

        /// <summary>The persistent presenter instance; the focus router uses
        /// it for shell-page cancel layers and social presentation.</summary>
        public static ChatOverlay? Instance => _instance;

        private VisualElement? _root;
        private VisualElement? _frame;
        private VisualElement? _collapsed;
        private VisualElement? _panel;
        private VisualElement? _main;
        private VisualElement? _onlineList;
        private VisualElement? _compactFeed;
        private Label? _historyFeedback;
        private ScrollView? _history;
        private Button? _open;
        private Button? _close;
        private Button? _newest;
        private Button? _refreshDirectory;
        private Button? _send;
        private Button? _globalTab;
        private Button? _serverTab;
        private Button? _directTab;
        private Label? _onlineFeedback;
        private Label? _unread;
        private TextField? _draft;
        private VisualElement? _resizeGrip;
        private int _resizePointerId = -1;
        private Vector2 _resizeStartPosition;
        private float _resizeStartWidthPx;
        private float _resizeStartHeightPx;
        private float _preferredWidthPx;
        private float _preferredHeightPx;
        private bool _resizeDirty;

        private ChatSession? _session;
        private IVisualElementScheduledItem? _compactTick;
        private bool _viewBound;
        private bool _expanded;
        private bool _suppressDraftChanged;
        private string _lastConversationKey = string.Empty;
        private string _lastDraft = string.Empty;
        private bool _showDirectory;
        private bool _enterConsumed;
        private bool _pinToNewest = true;
        private bool _ownsCursor;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        // Social presentation preference (issues #214/#220). On shell-hosted
        // pages this is the remembered manual minimization: true keeps the
        // compact cell visible, false keeps it minimized. Manual
        // minimize/restore rewrites it; resizing never does.
        private bool _dockOpen;
        // Per-conversation scroll anchors, launch-scoped presentation state:
        // key -> pinned-to-newest and last scroll offset survive page changes,
        // gameplay and frontend recreation via this persistent overlay.
        private readonly Dictionary<string, bool> _pinnedNewest = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector2> _scrollOffsets = new(StringComparer.Ordinal);
        // Conversations whose reading anchor was evicted by the bounded
        // buffer (issue #214): the next render returns them to newest with
        // the explanation already written to the conversation Feedback.
        private readonly HashSet<string> _evictionReset = new(StringComparer.Ordinal);
        // Frontend and gameplay are the only presentation hosts. Page changes
        // retain the same shell attachment; gameplay uses the HUD document.
        private bool _hostIsShell;
        // Zero-size focusable element parked on the gameplay HUD root when
        // the panel collapses (issue #216): focus never rests on the chat
        // surface while the panel hides, so UI Toolkit's focus fixup — which
        // re-focuses the visible strip at the panel's next dirty pass and
        // would re-arm the gate as if the player had navigated there — never
        // has a lost focus to repair.
        private VisualElement? _focusParker;


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
            }
            _session = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            FinishResize();
            _ownsCursor = false;
            // Frontend recreation must not reset the conversation view
            // (issue #214): scene loads keep the dock/expanded state and
            // re-attach to the explicit host. Entering gameplay is the one
            // deliberate collapse — match entry ends interactive chat while
            // ChatSession keeps drafts and selection. The transient expanded
            // state is reset with it (issue #220).
            if (!FrontendController.IsFrontendActive && ChatInputGate.IsGameplayScene)
            {
                SetExpanded(false, false);
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
        /// change. Compact ↔ expanded is presentation-only on this one
        /// subtree (issue #220) — the compact cell is visible unless the
        /// player explicitly minimized it, and the remembered preference
        /// decides which presentation re-entry restores.
        /// </summary>
        private void AttachToShell(VisualElement socialHost)
        {
            if (_root!.parent == socialHost)
                return;
            _root.RemoveFromHierarchy();
            socialHost.Add(_root);
            _hostIsShell = true;
            _expanded = _dockOpen;
            FrontendController.Shell?.SetSocialCellSize(_preferredWidthPx, _preferredHeightPx);
            ApplyPresentation();
            FrontendFocusRouter.NotifyPresentationChanged();
        }

        private void AttachToGameplayHost(Scene scene)
        {
            var host = FindFirstObjectByType<HUDManager>()?.Document;
            if (host == null || !host.isActiveAndEnabled ||
                host.gameObject.scene != scene || host.rootVisualElement == null)
            {
                if (_root!.panel != null)
                    _root.RemoveFromHierarchy();
                _hostIsShell = false;
                return;
            }
            if (_root!.parent != host.rootVisualElement)
            {
                _root.RemoveFromHierarchy();
                host.rootVisualElement.Add(_root);
            }
            _hostIsShell = false;
            _root.BringToFront();
            ApplyPresentation();
        }


        private void OnFrontendPageChanged(FrontendPage page)
        {
            // The shell attachment is stable across every page activation.
            AttachToScene(SceneManager.GetActiveScene());
        }

        private void LateUpdate()
        {
            if (!_ownsCursor) return;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
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
                GamepadUIBridge.Pump(_root?.panel);
            if (!enterHeld || _enterConsumed)
                return;
            _enterConsumed = true;
            if (!ChatInputGate.IsGameplayScene)
                return;
            if (UiModalState.Presented)
            {
                // The pause menu wins over chat (issue #216): chat neither
                // opens over the modal nor keeps strip focus, so the pause
                // owner's Escape is never swallowed by an armed gate.
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


        /// <summary>Gameplay chat owns Back only while its panel or focus is active.</summary>
        private bool OwnsBackPress()
        {
            if (UiModalState.Presented || !ChatInputGate.IsGameplayScene)
                return false;
            return _expanded || IsFocusInsideChatSurface();
        }


        /// <summary>Shell routing owns frontend focus; gameplay chat is
        /// focusable only while deliberately open.</summary>
        private void ApplyRegionFocusability()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive)
            {
                FrontendFocusRouter.Instance?.ApplyRegionFocusability();
                return;
            }
            bool social = ChatInputGate.IsGameplayScene && _expanded;
            _root?.Query<Button>().ForEach(button => button.focusable = social);
            _root?.Query<TextField>().ForEach(field => field.focusable = social);
            if (_history != null)
                _history.focusable = social;
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
            _root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!_hostIsShell)
                    ApplyGameplayFrameSize();
            });
            var frame = _root.Q<VisualElement>("chat-root");
            if (frame != null) frame.pickingMode = PickingMode.Ignore;
            _frame = frame;
            _collapsed = _root.Q<VisualElement>("chat-collapsed");
            _panel = _root.Q<VisualElement>("chat-panel");
            _main = _root.Q<VisualElement>("chat-main");
            _onlineList = _root.Q<VisualElement>("online-list");
            _compactFeed = _root.Q<VisualElement>("chat-public-feed");
            _historyFeedback = _root.Q<Label>("history-feedback");
            _history = _root.Q<ScrollView>("chat-history");
            _open = _root.Q<Button>("chat-open");
            _close = _root.Q<Button>("chat-close");
            _newest = _root.Q<Button>("chat-newest");
            _refreshDirectory = _root.Q<Button>("directory-refresh");
            _send = _root.Q<Button>("chat-send");
            _globalTab = _root.Q<Button>("chat-tab-global");
            _serverTab = _root.Q<Button>("chat-tab-server");
            _directTab = _root.Q<Button>("chat-tab-direct");
            _onlineFeedback = _root.Q<Label>("online-feedback");
            _unread = _root.Q<Label>("chat-unread");
            _draft = _root.Q<TextField>("chat-draft");
            _resizeGrip = _root.Q<VisualElement>("chat-resize-grip");
            if (_resizeGrip != null)
            {
                _resizeGrip.RegisterCallback<PointerDownEvent>(BeginResize);
                _resizeGrip.RegisterCallback<PointerMoveEvent>(MoveResize);
                _resizeGrip.RegisterCallback<PointerUpEvent>(EndResize);
                _resizeGrip.RegisterCallback<PointerCaptureOutEvent>(_ => FinishResize());
            }

            if (_open != null) _open.clicked += OnOpenButtonClicked;
            if (_close != null) _close.clicked += OnCloseButtonClicked;
            if (_newest != null) _newest.clicked += ScrollToNewest;
            if (_refreshDirectory != null) _refreshDirectory.clicked += RefreshDirectory;
            if (_send != null) _send.clicked += () => SendDraft(ChatInputGate.IsGameplayScene);
            if (_globalTab != null) _globalTab.clicked += SelectGlobal;
            if (_serverTab != null) _serverTab.clicked += SelectServer;
            if (_directTab != null) _directTab.clicked += SelectDirectTab;
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
                    TryMarkActiveRead();
                };
            }

            if (_draft != null)
            {
                _draft.maxLength = -1;
                _draft.RegisterValueChangedCallback(OnDraftChanged);
                RegisterEditorFocus(_draft);
            }

            _root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            // Explicit chat interaction arms the gameplay input gate (issue
            // #216): any chat control gaining focus during a match suppresses
            // fighter input until focus leaves the chat surface. The
            // collapsed strip is unfocusable, so combat focus only reaches
            // the panel after the player explicitly opens it. Focus events
            // bubble, so these handlers cover the whole surface.
            _root.RegisterCallback<FocusInEvent>(OnChatSurfaceFocusIn);
            _root.RegisterCallback<FocusOutEvent>(OnChatSurfaceFocusOut);
            // Gamepad B (and keyboard cancel) with focus inside the chat
            // surface: one press leaves the interaction (issue #215). The page
            // root is not an ancestor of the chat surface, so this handler is
            // the only cancel path for chat-focused presses.
            _root.RegisterCallback<NavigationCancelEvent>(OnRootNavigationCancel);
            _compactTick = _compactFeed?.schedule.Execute(UpdateCompactFeed).Every(200);
            _viewBound = true;
            SetExpanded(false, false);
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
            RenderSession();
        }

        private void OnSessionChanged()
        {
            if (this == null || !isActiveAndEnabled || _suppressDraftChanged)
                return;
            RenderSession();
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

            ChatConversation? active = _session.ActiveConversation;

            int directUnread = 0;
            foreach (ChatConversation conversation in _session.Conversations)
            {
                if (conversation.Key.StartsWith("direct:", StringComparison.Ordinal))
                    directUnread += Math.Max(0, conversation.Unread);
            }
            if (_unread != null)
            {
                _unread.text = directUnread > 0 ? directUnread.ToString() : string.Empty;
                _unread.EnableInClassList("chat-unread--visible", directUnread > 0);
            }
            _main?.SetDisplayed(true);

            RenderTabs(active, _session.IsConnected);
            RenderDirectory(active);
            RenderHistory(active);
            if (!_session.IsConnected && string.IsNullOrEmpty(active?.Feedback))
                SetText(_historyFeedback, _session.Status);
            RenderCompactFeed();
            // Dynamic rows are new Buttons with default focusability; re-apply
            // the active region's gating after every rebuild (issue #215).
            ApplyRegionFocusability();
        }

        private void RenderTabs(ChatConversation? active, bool connected)
        {
            string key = active?.Key ?? string.Empty;
            bool isGlobal = !_showDirectory && string.Equals(key, "global", StringComparison.Ordinal);
            bool isServer = !_showDirectory && key.StartsWith("room:", StringComparison.Ordinal);
            bool isDirect = _showDirectory || key.StartsWith("direct:", StringComparison.Ordinal);

            _globalTab?.EnableInClassList("chat-tab--active", isGlobal);
            _serverTab?.EnableInClassList("chat-tab--active", isServer);
            _directTab?.EnableInClassList("chat-tab--active", isDirect);
            _serverTab?.SetEnabled(connected && (_session?.JoinedRoomId.HasValue == true || isServer));
            if (_directTab != null)
            {
                bool inDirectConversation = !_showDirectory && key.StartsWith("direct:", StringComparison.Ordinal);
                _directTab.text = inDirectConversation ? active?.Title ?? "DIRECT" : "DIRECT";
                _directTab.tooltip = inDirectConversation ? active?.Title ?? "Direct messages" : "Direct messages";
            }
            _directTab?.SetEnabled(true);
        }


        /// <summary>
        /// Conversation management stays available from the Direct channel,
        /// without a separate maximized presentation.
        /// </summary>
        private VisualElement BuildConversationRow(ChatConversation conversation, ChatConversation? active)
        {
            var row = new VisualElement();
            row.AddToClassList("conversation-row");
            row.EnableInClassList("conversation-row--active", active != null && conversation.Key == active.Key);

            var select = new Button(() =>
            {
                _showDirectory = false;
                _session.SelectConversation(conversation.Key);
                RenderSession();
            });
            select.text = conversation.Title;
            select.enableRichText = false;
            select.AddToClassList("conversation-select");
            row.Add(select);

            if (conversation.Unread > 0 && active?.Key != conversation.Key)
            {
                var unread = new Label(conversation.Unread.ToString());
                unread.enableRichText = false;
                unread.AddToClassList("conversation-unread");
                row.Add(unread);
            }
            return row;
        }

        private void RenderDirectory(ChatConversation? active)
        {
            bool showDirectory = _showDirectory;
            _onlineList?.SetDisplayed(showDirectory);
            _refreshDirectory?.SetDisplayed(showDirectory);
            _onlineFeedback?.SetDisplayed(showDirectory);
            // The directory view replaces the history while it is shown, so
            // read acknowledgement is suppressed for it (issue #218).
            _history?.SetDisplayed(!showDirectory);
            if (!showDirectory || _onlineList == null || _session == null)
                return;

            _onlineList.Clear();

            // Conversation management lives behind the explicit DIRECT view
            // (issue #220): switching conversations never needs a persistent
            // list next to the messages.
            var conversationsLabel = new Label("CONVERSATIONS") { enableRichText = false };
            conversationsLabel.AddToClassList("chat-section-label");
            _onlineList.Add(conversationsLabel);
            foreach (ChatConversation conversation in _session.Conversations)
                _onlineList.Add(BuildConversationRow(conversation, active));

            var mutedLabel = new Label("MUTED PLAYERS") { enableRichText = false };
            mutedLabel.AddToClassList("chat-section-label");
            _onlineList.Add(mutedLabel);
            foreach (var player in _session.MutedPlayers)
            {
                var unmute = new Button(() => _session.SetMuted(player.PlayerId, false))
                {
                    text = $"UNMUTE {FormatIdentity(player)}",
                    enableRichText = false
                };
                unmute.AddToClassList("chat-mute");
                _onlineList.Add(unmute);
            }
            if (!_session.DirectoryAvailable)
            {
                SetText(_onlineFeedback, string.IsNullOrEmpty(_session.Status)
                    ? "ONLINE DIRECTORY UNAVAILABLE"
                    : _session.Status);
                return;
            }

            SetText(_onlineFeedback, "SELECT A PLAYER FOR A DIRECT MESSAGE");
            ChatPlayer? self = _session.Self;
            foreach (ChatPlayer player in _session.OnlinePlayers)
            {
                if (self != null && player.PlayerId == self.PlayerId)
                    continue;

                var row = new VisualElement();
                row.AddToClassList("online-row");
                var select = new Button(() =>
                {
                    _showDirectory = false;
                    _session.OpenDirect(player);
                    if (ChatInputGate.IsGameplayScene)
                        SetExpanded(true, false);
                });
                select.text = FormatIdentity(player);
                select.enableRichText = false;
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

        private void RenderHistory(ChatConversation? active)
        {
            if (_history == null || _draft == null || _session == null)
                return;

            bool changedConversation = active?.Key != _lastConversationKey;
            // A pending eviction reset (issue #214) wins over the live scroller
            // state: the live anchor still reflects the evicted reading
            // position, so return to newest instead of re-capturing it.
            bool evictionReset = active != null && _evictionReset.Remove(active.Key);
            if (!evictionReset && !changedConversation && active != null)
            {
                // Capture the live anchor before rebuilding so a re-render of
                // the same conversation keeps its reading position (issue #214).
                UpdateScrollAnchor();
            }
            string key = active?.Key ?? string.Empty;
            bool atNewest = evictionReset || (changedConversation ? IsPinnedNewest(key) : _pinToNewest);
            if (evictionReset)
                _pinnedNewest[key] = true;
            Vector2 oldOffset = changedConversation ? GetScrollOffset(key) : _history.scrollOffset;
            _history.Clear();
            bool hasMessages = false;

            if (active == null)
            {
                if (_historyFeedback != null)
                {
                    _historyFeedback.RemoveFromClassList("chat-feedback--success");
                    _historyFeedback.RemoveFromClassList("chat-feedback--pending");
                    _history.Add(_historyFeedback);
                }
                SetText(_historyFeedback, _session.IsConnected ? string.Empty : _session.Status);
                _newest?.SetDisplayed(false);
                return;
            }

            foreach (ChatMessage message in active.Messages)
            {
                if (!_session.IsMessageVisible(message))
                    continue;
                _history.Add(BuildMessageRow(message));
                hasMessages = true;
            }
            if (!hasMessages && _session.IsConnected)
            {
                var empty = new Label("NO MESSAGES YET");
                empty.AddToClassList("chat-empty");
                _history.Add(empty);
            }
            if (_historyFeedback != null)
                _history.Add(_historyFeedback);

            _lastConversationKey = active.Key;
            if (_draft.value != active.Draft)
                _draft.SetValueWithoutNotify(active.Draft);
            _lastDraft = active.Draft;
            _pinToNewest = atNewest;

            bool canSend = active.CanSend && !active.IsSending && _session.IsConnected;
            _draft.SetEnabled(!active.IsSending);
            _send?.SetEnabled(canSend && IsMessageValid(active.Draft));
            if (_send != null)
                _send.text = active.IsSending ? "SENDING" : "SEND";
            SetText(_historyFeedback, active.Feedback);
            _historyFeedback?.EnableInClassList("chat-feedback--success",
                string.Equals(active.Feedback, "Sent", StringComparison.Ordinal));
            _historyFeedback?.EnableInClassList("chat-feedback--pending", active.IsSending);

            _history.schedule.Execute(() =>
            {
                if (atNewest)
                    ScrollToNewest();
                else
                    _history.scrollOffset = oldOffset;
                // The directory view replaces history while it is shown, so
                // the return-to-newest affordance stays hidden with it
                // (issue #220).
                _newest?.SetDisplayed(!atNewest && hasMessages && !_showDirectory);
                TryMarkActiveRead();
            }).StartingIn(0);

        }

        private VisualElement BuildMessageRow(ChatMessage message)
        {
            // Per-line mute buttons are gone (issue #220): personal mute is
            // the explicit sender action in the directory view.
            var row = new VisualElement();
            row.AddToClassList("chat-message");
            var text = new Label($"{message.Sender.DisplayName} [{message.Sender.SessionTag}]: {message.Text}");
            text.enableRichText = false;
            text.AddToClassList("chat-message__text");
            row.Add(text);
            return row;
        }

        private void RenderCompactFeed()
        {
            if (_compactFeed == null || _session == null)
                return;

            var publicMessages = new List<ChatMessage>();
            foreach (ChatConversation conversation in _session.Conversations)
            {
                if (conversation.Key != "global"
                    && (!conversation.Key.StartsWith("room:", StringComparison.Ordinal)
                        || !_session.JoinedRoomId.HasValue
                        || conversation.Key != $"room:{_session.JoinedRoomId.Value}"))
                    continue;
                foreach (ChatMessage message in conversation.Messages)
                {
                    if (_session.IsMessageVisible(message))
                        publicMessages.Add(message);
                }
            }

            publicMessages.Sort((left, right) => left.SentAt.CompareTo(right.SentAt));
            _compactFeed.Clear();
            int first = Math.Max(0, publicMessages.Count - 3);
            for (int i = first; i < publicMessages.Count; i++)
            {
                ChatMessage message = publicMessages[i];
                var line = new Label($"{ChannelLabel(message.Channel)} // {FormatIdentity(message.Sender)}: {message.Text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ')}");
                line.enableRichText = false;
                line.userData = message.SentAt;
                line.AddToClassList("chat-public-line");
                _compactFeed.Add(line);
            }
            UpdateCompactFeed();
        }

        private void UpdateCompactFeed()
        {
            if (_compactFeed == null)
                return;

            bool anyVisible = false;
            foreach (VisualElement child in _compactFeed.Children())
            {
                if (child is not Label line || line.userData is not DateTimeOffset sentAt)
                    continue;
                double age = (DateTimeOffset.UtcNow - sentAt).TotalSeconds;
                float opacity = Mathf.Clamp01(1f - (float)(age / CompactLifetimeSeconds));
                line.style.opacity = opacity;
                line.style.display = opacity <= 0f ? DisplayStyle.None : DisplayStyle.Flex;
                anyVisible |= opacity > 0f;
            }
            _compactFeed.style.display = anyVisible && !_expanded && ChatInputGate.IsGameplayScene
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

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
        /// The collapsed strip is unfocusable, so focus only lands here
        /// inside the panel the player explicitly opened — navigation within
        /// it suppresses fighter input just like typing does.</summary>
        private void OnChatSurfaceFocusIn(FocusInEvent evt)
        {
            if (!ChatInputGate.IsGameplayScene)
                return;
            ChatInputGate.Begin();
        }

        /// <summary>Focus leaving the chat surface ends an unexpanded
        /// gameplay navigation session. An open panel keeps the gate until it
        /// is closed — SetExpanded/HandleEscape own that transition.</summary>
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
        /// invisible focusable element on the HUD root before the panel
        /// hides (issue #216). Focus then survives the panel hide, so no
        /// rescue pass can re-focus the strip and re-arm the gate.</summary>
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
                SetText(_historyFeedback, "MESSAGE MUST BE 500 UNICODE CHARACTERS OR FEWER");
                ScrollToNewest();
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
            finally { RenderSession(); }
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

        private void SelectGlobal()
        {
            _showDirectory = false;
            if (_session?.SelectConversation("global") == true)
                RenderSession();
        }

        private void SelectServer()
        {
            _showDirectory = false;
            if (_session?.JoinedRoomId is Guid roomId
                && _session.SelectConversation($"room:{roomId}"))
                RenderSession();
        }

        private void SelectDirectTab()
        {
            _showDirectory = true;
            RenderSession();
            RefreshDirectory();
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
                ReleaseCursor();
            }
            if (ChatInputGate.IsGameplayScene)
            {
                if (expanded)
                {
                    ChatInputGate.Begin();
                    if (!_ownsCursor)
                    {
                        _previousCursorLock = UnityEngine.Cursor.lockState;
                        _previousCursorVisible = UnityEngine.Cursor.visible;
                        _ownsCursor = true;
                    }
                }
                else
                {
                    // Park gameplay focus off the chat surface BEFORE the
                    // panel hides: the fixup that re-focuses the strip runs
                    // only when focus was lost to the hide (issue #216).
                    ParkFocusOutsideChat();
                }
            }
            ApplyPresentation();
            if (expanded)
                RenderSession();
            if (focusComposer)
                _root?.schedule.Execute(() => _draft?.Focus()).StartingIn(0);
        }

        /// <summary>Restore the shell cell or open the gameplay chat.</summary>
        private void OnOpenButtonClicked()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                RestoreFromMinimized();
                return;
            }
            OpenChat();
        }

        /// <summary>Minimize the shell cell or close the gameplay chat.</summary>
        private void OnCloseButtonClicked()
        {
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                MinimizeChat();
                return;
            }
            SetExpanded(false, false);
        }

        /// <summary>Open gameplay chat after a deliberate action.</summary>
        private void OpenChat()
        {
            // The topmost modal wins over chat (issue #216): a presented
            // modal — e.g. the pause menu or the direct-connect dialog — is
            // never covered by an opened conversation.
            if (UiModalState.Presented)
                return;
            SetExpanded(true, true);
        }

        /// <summary>Close gameplay chat without rewriting the menu preference.</summary>
        private void CloseChat()
        {
            SetExpanded(false, false);
        }

        /// <summary>
        /// Restore the compact cell from the minimized strip (issue #220):
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
            _root?.schedule.Execute(() => _draft?.Focus()).StartingIn(0);
        }

        /// <summary>
        /// Explicit manual minimization (issue #220): the compact cell hides
        /// into the strip inside the absolute host; the preference is
        /// remembered and never rewritten by resizing.
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


        /// <summary>Gameplay Escape closes chat; frontend Back belongs to the shell router.</summary>
        private void HandleEscape()
        {
            SetExpanded(false, false);
        }

        private void ReleaseCursor()
        {
            if (!_ownsCursor)
                return;
            UnityEngine.Cursor.lockState = _previousCursorLock;
            UnityEngine.Cursor.visible = _previousCursorVisible;
            _ownsCursor = false;
        }


        /// <summary>Render the stable shell conversation or the gameplay overlay.</summary>
        private void ApplyPresentation()
        {
            if (_root == null)
                return;

            // Shell-hosted presentation (issues #219/#220): the presenter
            // fills the absolute bottom-left host; the host owns its user-sized
            // geometry and density. No page space is reserved, so resizing
            // and presentation changes do not rebalance the scene underneath.
            // Open or hidden on the same user-sized subtree.
            if (_hostIsShell && FrontendController.IsFrontendActive && !ChatInputGate.IsGameplayScene)
            {
                if (_frame != null)
                {
                    _frame.style.position = Position.Absolute;
                    _frame.style.left = 0;
                    _frame.style.right = 0;
                    _frame.style.top = 0;
                    _frame.style.bottom = 0;
                    _frame.style.width = StyleKeyword.Auto;
                    _frame.style.height = StyleKeyword.Auto;
                    _frame.pickingMode = PickingMode.Ignore;
                }
                // Both scenes use the same high-contrast chat control language.
                _panel?.EnableInClassList("chat-panel--menu", true);
                _root?.EnableInClassList("chat-root--menu", true);
                _root?.EnableInClassList("chat-root--gameplay", false);
                _panel?.SetDisplayed(_expanded);
                _collapsed?.SetDisplayed(!_expanded);
                ApplyRegionFocusability();
                return;
            }

            // Gameplay keeps its HUD overlay geometry; no frontend page is
            // resized, replaced, or discovered through a document root. The
            // frame mirrors from the right side to the left; the bottom
            // anchor keeps the existing top-right grip drag directions.
            if (_frame != null)
            {
                _frame.style.position = Position.Absolute;
                _frame.style.left = 24;
                _frame.style.top = StyleKeyword.Auto;
                _frame.style.right = StyleKeyword.Auto;
                _frame.style.bottom = 94;
                ApplyGameplayFrameSize();
            }
            _panel?.EnableInClassList("chat-panel--menu", false);
            _root?.EnableInClassList("chat-root--menu", false);
            _root?.EnableInClassList("chat-root--gameplay", true);
            _panel?.EnableInClassList("chat-panel--solid", false);
            _panel?.SetDisplayed(_expanded);
            _collapsed?.SetDisplayed(!_expanded);
            if (_close != null) _close.tooltip = "Hide chat";
            ApplyRegionFocusability();
        }

        private float PhysicalPixelsPerPoint()
        {
            float panelScale = _root?.panel?.scaledPixelsPerPoint ?? 1f;
            float uiScale = ClientSettingsService.Instance.UiScale / 100f;
            return panelScale / Mathf.Max(0.01f, uiScale);
        }

        private void ApplyGameplayFrameSize()
        {
            if (_frame == null || _root?.panel == null)
                return;
            if (_root.resolvedStyle.width <= 0f || _root.resolvedStyle.height <= 0f)
                return;
            float factor = PhysicalPixelsPerPoint();
            float maxWidth = Mathf.Max(1f, _root.resolvedStyle.width - 48f);
            float maxHeight = Mathf.Max(1f, _root.resolvedStyle.height - 110f);
            float width = _preferredWidthPx > 0f ? _preferredWidthPx / factor : 540f;
            float height = _preferredHeightPx > 0f ? _preferredHeightPx / factor : 620f;
            _frame.style.width = Mathf.Clamp(width, Mathf.Min(400f / factor, maxWidth), maxWidth);
            _frame.style.height = Mathf.Clamp(height, Mathf.Min(300f / factor, maxHeight), maxHeight);
        }

        private void BeginResize(PointerDownEvent evt)
        {
            if (evt.button != 0 || _resizePointerId >= 0 || _resizeGrip == null || _root?.panel == null
                || !_expanded || UiModalState.Presented)
                return;
            var source = _hostIsShell ? FrontendController.Shell?.SocialHost : _frame;
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
            var viewport = _hostIsShell ? FrontendController.Shell?.Root : _root;
            if (viewport == null)
                return;
            float factor = PhysicalPixelsPerPoint();
            float widthLimit = Mathf.Max(120f, Mathf.Min(960f, viewport.resolvedStyle.width * factor - 48f));
            float heightLimit = Mathf.Max(120f, Mathf.Min(720f,
                viewport.resolvedStyle.height * factor * (_hostIsShell ? 0.65f : 1f)
                - (_hostIsShell ? 0f : 120f)));
            float deltaX = (evt.position.x - _resizeStartPosition.x) * factor;
            float deltaY = (_resizeStartPosition.y - evt.position.y) * factor;
            _preferredWidthPx = Mathf.Clamp(_resizeStartWidthPx + deltaX,
                Mathf.Min(300f, widthLimit), widthLimit);
            _preferredHeightPx = Mathf.Clamp(_resizeStartHeightPx + deltaY,
                Mathf.Min(180f, heightLimit), heightLimit);
            _resizeDirty = true;
            if (_hostIsShell)
                FrontendController.Shell?.SetSocialCellSize(_preferredWidthPx, _preferredHeightPx);
            else
                ApplyGameplayFrameSize();
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

        /// <summary>The conversation must be visibly laid out, not minimized,
        /// detached, replaced by a directory, or a passive gameplay preview.</summary>
        private bool IsConversationShown()
        {
            return _expanded && !_showDirectory && _root?.panel != null
                && _history?.contentRect.height > 0f;
        }

        /// <summary>Read requires a visible laid-out conversation, focused
        /// application, unobscured modal state, and the newest messages.</summary>
        private bool ShouldMarkActiveRead()
        {
            if (_session?.ActiveConversation == null || !IsConversationShown()
                || !Application.isFocused || UiModalState.Presented)
                return false;
            return _pinToNewest || IsAtNewest();
        }

        private void TryMarkActiveRead()
        {
            if (_session?.ActiveConversation?.Unread > 0 && ShouldMarkActiveRead())
                _session.MarkActiveRead();
        }

        /// <summary>Store the active conversation's reading anchor.</summary>
        private void UpdateScrollAnchor()
        {
            var active = _session?.ActiveConversation;
            if (active == null || _history == null)
                return;
            bool pinned = IsAtNewest();
            _pinToNewest = pinned;
            _pinnedNewest[active.Key] = pinned;
            _scrollOffsets[active.Key] = _history.scrollOffset;
        }

        private bool IsPinnedNewest(string key) =>
            _pinnedNewest.TryGetValue(key, out bool pinned) ? pinned : true;

        private Vector2 GetScrollOffset(string key) =>
            _scrollOffsets.TryGetValue(key, out Vector2 offset) ? offset : Vector2.zero;

        /// <summary>
        /// The bounded history buffer evicted older messages the player was
        /// reading (issue #214). Record a pending return-to-newest for the
        /// next render; the explanation lives on the conversation Feedback.
        /// The normal read rule applies afterwards.
        /// </summary>
        private void OnConversationHistoryTrimmed(string key)
        {
            if (_session == null)
                return;
            if (IsPinnedNewest(key))
                return;
            _evictionReset.Add(key);
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
            return _history.verticalScroller.value >= _history.verticalScroller.highValue - 1f;
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


        private static string FormatIdentity(ChatPlayer player)
        {
            return string.IsNullOrEmpty(player.SessionTag)
                ? player.DisplayName
                : $"{player.DisplayName} [{player.SessionTag}]";
        }

        private static string ChannelLabel(string channel)
        {
            return string.Equals(channel, "server", StringComparison.OrdinalIgnoreCase)
                ? "SERVER"
                : "GLOBAL";
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
