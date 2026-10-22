#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using SlopArena.Client;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using SlopArena.Shared;
using Steamworks;

namespace SlopArena.Client.Network
{
    /// <summary>
    /// Game-wide owner for the authenticated guest, SignalR transport, chat
    /// store, reconnect/renewal lifecycle, and main-thread delivery. A single
    /// instance survives scene changes; screens never create chat identities or
    /// own the lobby connection.
    /// </summary>
    public sealed class ChatSession : MonoBehaviour
    {
        public const string DisplayNamePlayerPrefsKey = "SlopArena.Chat.DisplayName";
        public const int MaxConversations = 32;
        public const int MaxMessagesPerConversation = 50;
        public const int MaxMutedPlayers = 256;
#if UNITY_EDITOR
        public const string EditorSteamEnabledPrefsKey = "SlopArena.Steam.EnabledInEditor";
#endif

        private const string EditorSteamDisabledStatus = "Steam is disabled in the Editor. Local play remains available. For online testing, enable Tools > SLOPArena > Steam > Enable in Editor before entering Play Mode.";
        private const float InitialRetrySeconds = 2f;
        private const float MaxRetrySeconds = 30f;
        private const int MaxAutomaticRetries = 5;
        private const float TokenRenewalLeadSeconds = 60f;
        private const string SteamBackendIdentity = "sloparena-playtest";
        private const int SteamTicketTimeoutSeconds = 15;
        private const float InviteOpenTimeoutSeconds = 5f;
        private const float InviteDebounceSeconds = 1f;

        private static ChatSession? _instance;
        private readonly List<ChatConversation> _conversations = new();
        private readonly List<ChatPlayer> _onlinePlayers = new();
        private readonly HashSet<string> _mutedPlayerIds = new(StringComparer.Ordinal);
        private readonly object _taskSync = new();
        private MasterServerClient? _masterClient;
        private readonly Dictionary<string, ChatPlayer> _mutedProfiles = new(StringComparer.Ordinal);
        private LobbyClient? _lobby;
        private Task<bool>? _connectTask;
        private Task<bool>? _authTask;
        private Task<bool>? _renewTask;
        private Callback<GetTicketForWebApiResponse_t>? _ticketCallback;
        private Callback<GameRichPresenceJoinRequested_t>? _roomJoinCallback;
        private Callback<NewUrlLaunchParameters_t>? _urlLaunchCallback;
        private Callback<GameOverlayActivated_t>? _overlayCallback;
        private TaskCompletionSource<string?>? _pendingTicket;
        private HAuthTicket _ticketHandle;
        private bool _ownsSteamApi;
        private RoomSteamPresence? _roomPresence;
        private SteamRoomJoinCoordinator? _roomJoin;
        private bool _developmentGuest;
        private bool _steamDisabledInEditor;
        private ulong _authenticatedSteamId;
        private bool _inviteOverlayPending;
        private bool _steamOverlayActive;
        private bool _overlayInputHeld;
        private float _inviteRequestedAt;
        private float _nextInviteAt;
        private int _accountGeneration;
        private string _masterServerUrl = ClientSession.DefaultMasterServerUrl;
        private string _savedDisplayName = string.Empty;
        private string _status = "Offline";
        private string? _selfPlayerId;
        private ChatConversation? _activeConversation;
        private bool _authAttempted;
        private bool _nameApplied;
        private bool _savedNameRejected;
        private int _nameGeneration;
        private bool _directoryAvailable;
        private bool _chatStateReady;
        private bool _destroyed;
        private float _nextRetryAt;
        private float _nextRenewalAt;
        private float _retryDelay = InitialRetrySeconds;
        // Per-launch budget: never reset on success, so a flapping connection cannot retry forever.
        private int _automaticRetries;
        private Guid? _joinedRoomId;
        private Guid _roomTitleId;
        private string _roomTitleName = string.Empty;
        public static ChatSession? Instance => _instance;
        public event Action? Changed;
        public event Action? AccountChanged;

        public event Action? RoomInviteStateChanged;
        public event Action<string>? RoomInviteFeedback;
        public event Action<LobbyClient?>? ActiveLobbyChanged;

        /// <summary>
        /// Raised with the conversation key when the bounded history buffer
        /// (issue #214) trims its oldest message to stay at
        /// <see cref="MaxMessagesPerConversation"/>. Presentation uses this to
        /// return an evicted reading anchor to newest with an explanation.
        /// </summary>
        public event Action<string>? ConversationHistoryTrimmed;

        /// <summary>
        /// Raised once for each message that arrives live (pushed by Master or
        /// accepted from this client's own send), before <see cref="Changed"/>.
        /// Backlog, reconnect snapshots and duplicates never raise it, so
        /// presentation can time transient previews from local arrival.
        /// </summary>
        public event Action<ChatMessage>? MessageArrived;

        public ChatPlayer? Self { get; private set; }
        public bool IsConnected => _nameApplied && _chatStateReady && _lobby?.IsConnected == true;
        public bool NeedsDisplayName => string.IsNullOrEmpty(_savedDisplayName);
        public string SavedDisplayName => _savedDisplayName;
        public bool SavedNameRejected => _savedNameRejected;
        public bool DirectoryAvailable => _directoryAvailable;
        public string Status => _status;
        public Guid? JoinedRoomId => _joinedRoomId;
        public IReadOnlyList<ChatPlayer> OnlinePlayers => _onlinePlayers;
        public IReadOnlyList<ChatConversation> Conversations => _conversations;
        public ChatConversation? ActiveConversation => _activeConversation;
        public IReadOnlyCollection<ChatPlayer> MutedPlayers => _mutedProfiles.Values;
        public bool IsSending => _activeConversation?.IsSending == true;
        public MasterServerClient? MasterClient => _masterClient;
        public LobbyClient? ActiveLobby => _lobby;
        public string? AuthToken => _masterClient?.Token;
        public long? SteamId => _masterClient?.SteamId;
        public bool HasPendingRoomJoin => _roomJoin?.HasPendingJoin == true;
        public bool InviteOverlayBusy => _inviteOverlayPending || _steamOverlayActive ||
            Time.unscaledTime < _nextInviteAt || ChatInputGate.ExternalOverlaySuppressed;
        public void CancelPendingRoomJoin()
        {
            _roomJoin?.Cancel();
            NotifyChanged();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null || SteamProbeClient.IsRequested())
                return;
            if (!Application.isEditor)
                ClientSession.MasterServerUrl = ClientSession.DefaultMasterServerUrl;
            // Guest access is an explicit Editor-only development choice, never a packaged fallback.
            // Set SLOPARENA_DEV_GUEST=1 before Editor launch for a local guest session.
            var overrideUrl = Application.isEditor
                ? Environment.GetEnvironmentVariable("SLOPARENA_MASTER_URL")
                : null;
            if (overrideUrl != null)
            {
                if (!Uri.TryCreate(overrideUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
                    uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                    throw new InvalidOperationException("SLOPARENA_MASTER_URL must be an HTTPS origin without credentials, path, query, or fragment.");
                ClientSession.MasterServerUrl = uri.GetLeftPart(UriPartial.Authority);
            }
            var guestFlag = Application.isEditor &&
                Environment.GetEnvironmentVariable("SLOPARENA_DEV_GUEST") == "1";
            var go = new GameObject(nameof(ChatSession));
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ChatSession>();
            _instance._developmentGuest = guestFlag;
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
#if UNITY_EDITOR
            // Steam can keep tracking the Editor process after Play Mode stops.
            _steamDisabledInEditor = !UnityEditor.EditorPrefs.GetBool(EditorSteamEnabledPrefsKey, false);
#endif
            _masterServerUrl = ClientSession.MasterServerUrl;
            _savedDisplayName = PlayerPrefs.GetString(DisplayNamePlayerPrefsKey, string.Empty).Trim();
            _activeConversation = GetOrCreateConversation("global", "GLOBAL")!;
            _status = NeedsDisplayName ? "Choose a display name to connect." : "Connecting…";
        }

        private void Start()
        {
            if (!_developmentGuest && _steamDisabledInEditor)
            {
                SetStatus(EditorSteamDisabledStatus);
                return;
            }
            if (!_developmentGuest)
            {
                try
                {
                    if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
                    {
                        SetStatus("Steam is unavailable. Local play remains available.");
                        return;
                    }
                    _ownsSteamApi = true;
                    _roomPresence = new RoomSteamPresence();
                    _roomPresence.Reset(true, Time.unscaledTime);
                    _ticketCallback = Callback<GetTicketForWebApiResponse_t>.Create(OnWebTicket);
                    _roomJoin = new SteamRoomJoinCoordinator(this);
                    _roomJoinCallback = Callback<GameRichPresenceJoinRequested_t>.Create(
                        request => OnRoomJoinRequested(request.m_rgchConnect));
                    _urlLaunchCallback = Callback<NewUrlLaunchParameters_t>.Create(
                        _ => ReadSteamLaunchRoom());
                    _overlayCallback = Callback<GameOverlayActivated_t>.Create(OnSteamOverlayActivated);
                    ReadSteamLaunchRoom();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Steam initialization failed: {ex.GetType().Name}");
                    SetStatus("Steam is unavailable. Local play remains available.");
                    return;
                }
            }
            if (!NeedsDisplayName)
                _ = EnsureConnectedAsync();
        }

        private void Update()
        {
            if (_destroyed)
                return;
            _lobby?.Pump(256);
            if (_ownsSteamApi)
            {
                SteamAPI.RunCallbacks();
                ulong observedSteamId = SteamUser.GetSteamID().m_SteamID;
                if (_authenticatedSteamId != 0 && observedSteamId != 0 &&
                    observedSteamId != _authenticatedSteamId)
                    ClearAccountSession();
            }
            _roomPresence?.Refresh(_ownsSteamApi, !_developmentGuest &&
                _masterClient?.IsAuthenticated == true &&
                _authenticatedSteamId != 0 &&
                (ulong)(_masterClient.SteamId ?? 0) == _authenticatedSteamId,
                IsConnected && _masterClient?.TokenExpiresAt > DateTimeOffset.UtcNow,
                Time.unscaledTime);
            _roomJoin?.Tick();
            if (_inviteOverlayPending && !_steamOverlayActive &&
                Time.unscaledTime - _inviteRequestedAt >= InviteOpenTimeoutSeconds)
            {
                _inviteOverlayPending = false;
                SetSteamOverlayInput(false);
                NotifyInviteFeedback("Steam's invite dialog did not open. Enable the Steam overlay and retry.");
                NotifyInviteStateChanged();
            }
            if (_lobby?.HasPendingOverflow == true)
            {
                _lobby.ClearPendingOverflow();
                _chatStateReady = false;
                SetStatus(_automaticRetries >= MaxAutomaticRetries
                    ? "CHAT OVERFLOW — RETRY PAUSED"
                    : "Chat event backlog overflowed; resyncing from Master.");
            }

            if ((_automaticRetries < MaxAutomaticRetries || IsConnected) &&
                _masterClient?.TokenExpiresAt is DateTimeOffset expires &&
                DateTimeOffset.UtcNow >= expires.AddSeconds(-TokenRenewalLeadSeconds))
            {
                if (Time.unscaledTime >= _nextRenewalAt && (_renewTask == null || _renewTask.IsCompleted))
                {
                    _nextRenewalAt = Time.unscaledTime + MaxRetrySeconds;
                    _renewTask = RenewTokenAsync();
                }
            }

            if (!NeedsDisplayName && !_savedNameRejected && !IsConnected && _connectTask == null &&
                _automaticRetries < MaxAutomaticRetries &&
                Time.unscaledTime >= _nextRetryAt && _authAttempted)
            {
                _automaticRetries++;
                _nextRetryAt = Time.unscaledTime + _retryDelay;
                _retryDelay = Math.Min(MaxRetrySeconds, _retryDelay * 2f);
                _ = EnsureConnectedAsync();
            }
        }

        /// <summary>Set the endpoint before the first auth attempt.</summary>
        public static void ConfigureMasterServerUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            var normalized = url.TrimEnd('/');
            if (!Application.isEditor &&
                !string.Equals(normalized, ClientSession.DefaultMasterServerUrl, StringComparison.OrdinalIgnoreCase))
                return;
            if (_instance != null &&
                _instance._authAttempted &&
                !string.Equals(_instance._masterServerUrl, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _instance.SetStatus("The launch session cannot switch Master servers after authentication starts.");
                return;
            }
            ClientSession.MasterServerUrl = normalized;
            if (_instance == null)
                return;
            if (string.Equals(_instance._masterServerUrl, normalized, StringComparison.OrdinalIgnoreCase))
                return;
            _instance._masterServerUrl = normalized;
            if (!_instance._authAttempted)
            {
                _instance._masterClient?.Dispose();
                _instance._masterClient = null;
            }
        }

        public async Task<bool> EnsureConnectedAsync()
        {
            if (_destroyed || NeedsDisplayName)
            {
                SetStatus("Choose a display name to connect.");
                return false;
            }
            if (IsConnected && _nameApplied)
                return true;
            Task<bool>? task;
            lock (_taskSync)
            {
                if (_connectTask == null)
                {
                    _nextRetryAt = Math.Max(_nextRetryAt, Time.unscaledTime + InitialRetrySeconds);
                    _connectTask = ConnectCoreAsync();
                }
                task = _connectTask;
            }
            try
            {
                bool connected = await task;
                if (!connected && _automaticRetries >= MaxAutomaticRetries && !_destroyed)
                    SetStatus("RETRY PAUSED");
                return connected;
            }
            finally
            {
                lock (_taskSync)
                {
                    if (ReferenceEquals(_connectTask, task) && task.IsCompleted)
                        _connectTask = null;
                }
            }
        }

        public async Task<bool> SetDisplayNameAsync(string name)
        {
            if (!ChatTextValidation.TryValidateDisplayName(name, out var normalized, out var validationError))
            {
                SetStatus(validationError);
                return false;
            }
            EnsureMasterClient();
            if (!await EnsureAuthenticatedAsync())
                return false;
            var profile = await _masterClient!.SetDisplayNameAsync(normalized);
            if (profile == null)
            {
                SetStatus(_masterClient.LastStatusCode == 409
                    ? "You cannot rename while joined to a GameServer."
                    : "The Master rejected that display name. Try again.");
                return false;
            }
            ApplySelf(ToChatPlayer(profile));
            _savedDisplayName = normalized;
            // A rename accepted while an initial connect apply is still in
            // flight makes the apply loop re-apply this latest name. Keep
            // _nameApplied false so the trailing EnsureConnectedAsync runs
            // that versioned apply instead of trusting the connected
            // fast-path.
            _nameGeneration++;
            PlayerPrefs.SetString(DisplayNamePlayerPrefsKey, normalized);
            PlayerPrefs.Save();
            _nameApplied = false;
            SetStatus(IsConnected ? "Connected" : "Connecting…");
            return await EnsureConnectedAsync();
        }

        public Task<bool> RenameAsync(string name) => SetDisplayNameAsync(name);

        /// <summary>
        /// Validate and persist the display name locally without waiting for
        /// Master acceptance (issue #209). Solo and Training remain available
        /// offline; the saved name is applied remotely on the next connection,
        /// and a remote rejection is surfaced without claiming connection
        /// success.
        /// </summary>
        public async Task<bool> AcceptDisplayNameLocallyAsync(string name)
        {
            if (!ChatTextValidation.TryValidateDisplayName(name, out var normalized, out var validationError))
            {
                SetStatus(validationError);
                return false;
            }
            bool changed = !string.Equals(_savedDisplayName, normalized, StringComparison.Ordinal);
            _savedDisplayName = normalized;
            _savedNameRejected = false;
            if (changed)
            {
                _nameGeneration++;
                _nameApplied = false;
            }
            PlayerPrefs.SetString(DisplayNamePlayerPrefsKey, normalized);
            PlayerPrefs.Save();
            SetStatus(changed ? "Display name saved locally. Connecting…" : "Display name saved locally.");
            NotifyChanged();
            try
            {
                await EnsureConnectedAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("Connection problem after saving your display name. Local play remains available.");
            }
            return true;
        }

        public async Task RefreshDirectoryAsync()
        {
            if (!await EnsureConnectedAsync())
                return;
            var online = await _lobby!.GetOnlinePlayersAsync();
            if (online == null)
            {
                _directoryAvailable = false;
                SetStatus("Online directory unavailable.");
                return;
            }
            ReplaceOnlinePlayers(online);
            _directoryAvailable = true;
            SetStatus("Connected");
            NotifyChanged();
        }

        public bool SelectConversation(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            var conversation = _conversations.FirstOrDefault(c => c.Key == key);
            if (conversation == null)
                return false;
            _activeConversation = conversation;
            // Read state is presentation-owned (issue #214): selecting alone
            // does not acknowledge; the overlay marks read only when the
            // selected conversation is visible, focused, unobscured and at
            // newest.
            RefreshConversationStates();
            NotifyChanged();
            return true;
        }

        public bool OpenDirect(ChatPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.PlayerId) ||
                string.Equals(player.PlayerId, _selfPlayerId, StringComparison.Ordinal))
                return false;
            var conversation = GetOrCreateConversation(
                DirectKey(player.PlayerId), FormatPlayerTitle(player));
            if (conversation == null)
                return false;
            conversation.Title = FormatPlayerTitle(player);
            _activeConversation = conversation;
            // Read state is presentation-owned (issue #214): selecting alone
            // does not acknowledge; the overlay marks read only when the
            // selected conversation is visible, focused, unobscured and at
            // newest.
            RefreshConversationStates();
            NotifyChanged();
            return true;
        }

        public void SetDraft(string text)
        {
            if (_activeConversation == null)
                return;
            _activeConversation.Draft = text ?? string.Empty;
            RefreshConversationStates();
            NotifyChanged();
        }

        public Task SendDraftAsync()
        {
            var conversation = _activeConversation;
            return conversation == null
                ? Task.CompletedTask
                : SendAsync(conversation, conversation.Draft, fromDraft: true);
        }

        /// <summary>
        /// Send a fixed quick line (controller chat) to one conversation without
        /// touching its draft. Limits and outcomes are the same as a typed send.
        /// </summary>
        public Task SendQuickLineAsync(string key, string text)
        {
            var conversation = _conversations.FirstOrDefault(c => c.Key == key);
            return conversation == null
                ? Task.CompletedTask
                : SendAsync(conversation, text, fromDraft: false);
        }

        private async Task SendAsync(ChatConversation conversation, string text, bool fromDraft)
        {
            if (conversation.IsSending)
                return;
            var key = conversation.Key;
            string kept = fromDraft ? " Your draft was kept." : string.Empty;
            if (!ChatTextValidation.TryValidateMessage(text, out var normalized, out var validationError))
            {
                conversation.Feedback = validationError;
                NotifyChanged();
                return;
            }
            if (!IsConnected)
            {
                conversation.Feedback = fromDraft
                    ? "Not sent while offline. Your draft was kept; send it when ready."
                    : "Not sent while offline.";
                NotifyChanged();
                return;
            }
            if (!IsDestinationAvailable(key))
            {
                conversation.Feedback = key.StartsWith("direct:", StringComparison.Ordinal)
                    ? "That player is offline. The message was not queued."
                    : "That Room is no longer joined.";
                NotifyChanged();
                return;
            }

            conversation.IsSending = true;
            conversation.Feedback = "Sending…";
            RefreshConversationStates();
            NotifyChanged();
            ChatMessage? accepted = null;
            try
            {
                if (key == "global")
                    accepted = await _lobby!.SendGlobalAsync(normalized);
                else if (TryRoomKey(key, out var roomId))
                    accepted = await _lobby!.SendServerAsync(roomId, normalized);
                else if (key.StartsWith("direct:", StringComparison.Ordinal))
                    accepted = await _lobby!.SendDirectAsync(key.Substring("direct:".Length), normalized);
                else
                    conversation.Feedback = "Unknown chat destination.";

                if (accepted != null)
                {
                    if (!_conversations.Contains(conversation) ||
                        (TryRoomKey(key, out var sentRoomId) && _joinedRoomId != sentRoomId))
                        return;
                    if (AddMessage(accepted))
                        RaiseMessageArrived(accepted);
                    if (fromDraft && conversation.Draft == text)
                        conversation.Draft = string.Empty;
                    conversation.Feedback = "Sent";
                }
                else if (!IsConnected)
                {
                    conversation.Feedback = "Send status is uncertain because the connection dropped." + kept;
                }
                else
                {
                    conversation.Feedback = "Send was not confirmed." + kept;
                }
            }
            catch (Microsoft.AspNetCore.SignalR.HubException ex)
            {
                string reason = ex.Message switch
                {
                    var message when message.Contains("rate_limited") => "Too many messages. Wait a few seconds before retrying.",
                    var message when message.Contains("recipient_offline") => "That player is offline. The message was not queued.",
                    var message when message.Contains("not_in_room") => "This Room is no longer joined.",
                    var message when message.Contains("not_connected") => "The connection was lost before the message was accepted.",
                    var message when message.Contains("chat_capacity") => "Chat is temporarily busy. Try again later.",
                    _ => "The Master rejected this message."
                };
                conversation.Feedback = reason + kept;
            }
            catch (Exception)
            {
                conversation.Feedback = fromDraft
                    ? "Send status is uncertain. Your draft was kept and will not be resent automatically."
                    : "Send status is uncertain. It will not be resent automatically.";
            }
            finally
            {
                conversation.IsSending = false;
                RefreshConversationStates();
                NotifyChanged();
            }
        }

        public bool IsMuted(string playerId) =>
            !string.IsNullOrEmpty(playerId) && _mutedPlayerIds.Contains(playerId);

        public void SetMuted(string playerId, bool muted)
        {
            if (string.IsNullOrEmpty(playerId))
                return;
            if (muted)
            {
                if (!_mutedPlayerIds.Contains(playerId) && _mutedPlayerIds.Count >= MaxMutedPlayers)
                {
                    if (_activeConversation != null)
                        _activeConversation.Feedback = "Mute list is full.";
                    NotifyChanged();
                    return;
                }
                _mutedPlayerIds.Add(playerId);
                var profile = FindKnownPlayer(playerId);
                if (profile != null)
                    _mutedProfiles[playerId] = profile;
            }
            else
            {
                _mutedPlayerIds.Remove(playerId);
                _mutedProfiles.Remove(playerId);
            }
            NotifyChanged();
        }

        public void MarkActiveRead()
        {
            if (_activeConversation == null)
                return;
            _activeConversation.Unread = 0;
        }

        /// <summary>Acknowledge one conversation that the presenter shows at its newest messages.</summary>
        public void MarkRead(string key)
        {
            var conversation = _conversations.FirstOrDefault(c => c.Key == key);
            if (conversation != null)
                conversation.Unread = 0;
        }

        public bool IsMessageVisible(ChatMessage message) =>
            message != null && message.Sender != null && !IsMuted(message.Sender.PlayerId);

        private async Task<bool> ConnectCoreAsync()
        {
            int accountGeneration = _accountGeneration;
            EnsureMasterClient();
            if (!await EnsureAuthenticatedAsync() || accountGeneration != _accountGeneration)
                return false;
            if (!_nameApplied)
            {
                // Re-apply while a local save races this connect, so the latest
                // locally accepted name is the one the Master sees.
                while (true)
                {
                    int generation = _nameGeneration;
                    var profile = await _masterClient!.SetDisplayNameAsync(_savedDisplayName);
                    if (accountGeneration != _accountGeneration)
                        return false;
                    if (profile == null)
                    {
                        _nameApplied = false;
                        int? statusCode = _masterClient.LastStatusCode;
                        // A 4xx against an outdated generation says nothing about
                        // the latest local name; keep retrying that instead.
                        if (_nameGeneration == generation && statusCode is 400 or 409 or 422)
                        {
                            _savedNameRejected = true;
                            SetStatus("Master rejected your saved display name. Correct it in CHAT; local play remains available.");
                        }
                        else
                        {
                            SetStatus("Could not apply your saved display name yet. Local play remains available.");
                        }
                        return false;
                    }
                    ApplySelf(ToChatPlayer(profile));
                    if (_nameGeneration == generation)
                    {
                        _nameApplied = true;
                        break;
                    }
                }
            }

            if (_lobby == null)
            {
                _lobby = new LobbyClient(
                    _masterServerUrl,
                    () => _masterClient?.Token,
                    _developmentGuest ? 0 : SteamMatchDescriptor.CurrentProtocolVersion);
                SubscribeLobby(_lobby);
                ClientSession.ActiveLobby = _lobby;
                _roomPresence?.Attach(_lobby);
                ActiveLobbyChanged?.Invoke(_lobby);
            }
            var lobby = _lobby;
            if (!lobby.IsConnected && !await lobby.ConnectAsync())
            {
                if (accountGeneration != _accountGeneration)
                    return false;
                _chatStateReady = false;
                SetStatus("Master connection unavailable. Local play remains available.");
                return false;
            }
            if (accountGeneration != _accountGeneration)
                return false;
            var snapshot = await lobby.GetChatStateAsync();
            if (accountGeneration != _accountGeneration)
                return false;
            if (snapshot == null)
            {
                _chatStateReady = false;
                SetStatus("Master chat admission is unavailable. Retry when online.");
                return false;
            }
            ApplySnapshot(snapshot);
            _chatStateReady = true;
            _retryDelay = InitialRetrySeconds;
            _nextRetryAt = Time.unscaledTime + InitialRetrySeconds;
            SetStatus("Connected");
            await RefreshDirectoryCoreAsync();
            return true;
        }

        private async Task<bool> EnsureAuthenticatedAsync()
        {
            if (_masterClient!.IsAuthenticated)
                return true;
            _authAttempted = true;
            if (!_developmentGuest && !_ownsSteamApi)
            {
                SetStatus(_steamDisabledInEditor ? EditorSteamDisabledStatus :
                    "Steam is unavailable. Local play remains available.");
                return false;
            }
            SetStatus(_developmentGuest ? "Signing in as a development guest…" : "Signing in with Steam…");
            Task<bool>? task;
            lock (_taskSync)
            {
                _authTask ??= AuthenticateCoreAsync();
                task = _authTask;
            }
            int generation = _accountGeneration;
            bool authenticated;
            try { authenticated = await task; }
            finally
            {
                lock (_taskSync)
                {
                    if (ReferenceEquals(_authTask, task) && task.IsCompleted) _authTask = null;
                }
            }
            if (generation != _accountGeneration)
                return false;
            if (!_developmentGuest && authenticated)
            {
                ulong verifiedSteamId = (ulong)(_masterClient?.SteamId ?? 0);
                ulong observedSteamId = SteamUser.GetSteamID().m_SteamID;
                if ((_authenticatedSteamId != 0 && verifiedSteamId != _authenticatedSteamId) ||
                    (observedSteamId != 0 && observedSteamId != verifiedSteamId))
                {
                    ClearAccountSession();
                    return false;
                }
            }
            if (!authenticated)
            {
                SetStatus("Could not authenticate with Master. Local play remains available.");
                return false;
            }
            _authenticatedSteamId = _developmentGuest ? 0 : (ulong)(_masterClient?.SteamId ?? 0);
            ClientSession.AuthToken = _masterClient.Token;
            ClientSession.SteamId = _masterClient.SteamId ?? 0;
            return true;
        }

        private async Task<bool> AuthenticateCoreAsync()
        {
            if (_developmentGuest)
                return await _masterClient!.AuthenticateGuestAsync();
            var ticket = await RequestWebTicketAsync();
            try { return ticket != null && await _masterClient!.AuthenticateSteamAsync(ticket); }
            finally { CancelWebTicket(); }
        }

        private async Task<bool> RenewTokenAsync()
        {
            if (_masterClient == null || !_masterClient.IsAuthenticated)
                return false;
            var ticket = _developmentGuest ? null : await RequestWebTicketAsync();
            bool renewed;
            try
            {
                renewed = (_developmentGuest || ticket != null) &&
                    await _masterClient.RefreshAsync(ticket);
            }
            finally { CancelWebTicket(); }
            if (renewed)
            {
                ClientSession.AuthToken = _masterClient.Token;
                SetStatus(IsConnected ? "Connected" : _status);
                NotifyChanged();
                return true;
            }
            if (_masterClient.TokenExpiresAt is DateTimeOffset expires && expires <= DateTimeOffset.UtcNow)
            {
                _masterClient.ClearAuthentication();
                ClientSession.AuthToken = null;
                _chatStateReady = false;
                _nameApplied = false;
                ActiveLobbyChanged?.Invoke(null);
                ClientSession.ActiveLobby = null;
                _roomPresence?.Attach(null);
                _ = _lobby?.DisconnectAsync();
                _lobby = null;
                SetStatus("Authentication expired. Reconnect requires Steam.");
            }
            return false;
        }

        private async Task<string?> RequestWebTicketAsync()
        {
            if (!_ownsSteamApi || !SteamUser.BLoggedOn() || _pendingTicket != null)
                return null;
            _pendingTicket = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ticketHandle = SteamUser.GetAuthTicketForWebApi(SteamBackendIdentity);
            try
            {
                var completed = await Task.WhenAny(_pendingTicket.Task,
                    Task.Delay(TimeSpan.FromSeconds(SteamTicketTimeoutSeconds)));
                return completed == _pendingTicket.Task ? await _pendingTicket.Task : null;
            }
            finally
            {
                _pendingTicket = null;
            }
        }

        private void CancelWebTicket()
        {
            if (!_ownsSteamApi || _ticketHandle == HAuthTicket.Invalid)
                return;
            SteamUser.CancelAuthTicket(_ticketHandle);
            _ticketHandle = HAuthTicket.Invalid;
        }

        private void OnWebTicket(GetTicketForWebApiResponse_t response)
        {
            if (_pendingTicket == null || response.m_hAuthTicket != _ticketHandle)
                return;
            _pendingTicket.TrySetResult(response.m_eResult == EResult.k_EResultOK &&
                response.m_cubTicket > 0 && response.m_cubTicket <= response.m_rgubTicket.Length
                    ? BitConverter.ToString(response.m_rgubTicket, 0, response.m_cubTicket).Replace("-", "")
                    : null);
        }

        /// <summary>A Room locator can be invited only from its latest confirmed, joinable snapshot.</summary>
        public static string? InviteUnavailableReason(RoomSnapshot? room, Guid joinedRoomId,
            bool latest, bool steamReady, bool sessionUsable, bool overlayAvailable, bool busy)
        {
            if (room == null || room.Id == Guid.Empty || joinedRoomId != room.Id || !latest)
                return "Waiting for confirmed Room membership.";
            if (!room.Joinable || room.Phase != "Lobby" || room.Capacity <= room.MemberCount)
                return "This Room is full or preparing for a match. Invite when it is joinable.";
            if (!steamReady || !sessionUsable)
                return "Steam or your Room session is unavailable. Reconnect to invite.";
            if (!overlayAvailable)
                return "Steam overlay is unavailable. Enable it in Steam, or ask friends to use Join Game.";
            if (busy)
                return "Finish the Steam overlay and release held controls before inviting again.";
            return null;
        }

        public string? GetRoomInviteUnavailableReason(RoomSnapshot? room)
        {
            Guid joinedRoomId = _lobby?.JoinedRoomId ?? Guid.Empty;
            bool latest = _roomPresence?.IsCurrentRoom(room) == true;
            var roomReason = InviteUnavailableReason(room, joinedRoomId, latest,
                true, true, true, false);
            if (roomReason != null) return roomReason;
            bool steamReady = _ownsSteamApi && !_developmentGuest && !_destroyed;
            bool sessionUsable = false;
            bool overlayAvailable = false;
            if (steamReady)
            {
                try
                {
                    sessionUsable = _masterClient?.IsSteamAuthenticated == true &&
                        _masterClient.TokenExpiresAt > DateTimeOffset.UtcNow &&
                        _authenticatedSteamId != 0 &&
                        SteamUser.GetSteamID().m_SteamID == _authenticatedSteamId && IsConnected;
                    if (sessionUsable)
                        overlayAvailable = SteamUtils.IsOverlayEnabled();
                }
                catch (Exception) { /* Steam may be temporarily unavailable. */ }
            }
            return InviteUnavailableReason(room, joinedRoomId, latest,
                steamReady, sessionUsable, overlayAvailable, InviteOverlayBusy);
        }

        /// <summary>Opening a dialog is not confirmation that an invitation was sent.</summary>
        public static bool TryOpenInviteDialog(Guid roomId, bool overlayAvailable,
            Action<string> openDialog, float now, ref float nextAllowedAt, out string feedback)
        {
            if (roomId == Guid.Empty || !overlayAvailable)
            {
                feedback = "Steam overlay is unavailable. Enable it in Steam and retry.";
                return false;
            }
            if (now < nextAllowedAt)
            {
                feedback = "Wait before reopening the Steam invite dialog.";
                return false;
            }
            try
            {
                openDialog(RoomConnection.Format(roomId));
                nextAllowedAt = now + InviteDebounceSeconds;
                feedback = "Steam invite dialog requested. Choose a friend there; delivery is not confirmed.";
                return true;
            }
            catch (Exception)
            {
                feedback = "Steam could not open the invite dialog. Check the overlay and retry.";
                return false;
            }
        }

        public bool TryInviteRoom(RoomSnapshot? room, out string feedback)
        {
            feedback = GetRoomInviteUnavailableReason(room) ?? string.Empty;
            if (feedback.Length != 0) return false;
            if (!TryOpenInviteDialog(room!.Id, true,
                SteamFriends.ActivateGameOverlayInviteDialogConnectString,
                Time.unscaledTime, ref _nextInviteAt, out feedback))
                return false;
            _inviteOverlayPending = true;
            _inviteRequestedAt = Time.unscaledTime;
            SetSteamOverlayInput(true);
            NotifyInviteStateChanged();
            return true;
        }

        private void OnSteamOverlayActivated(GameOverlayActivated_t activation)
        {
            _steamOverlayActive = activation.m_bActive != 0;
            _inviteOverlayPending = false;
            if (_steamOverlayActive)
                SetSteamOverlayInput(true);
            else
            {
                SetSteamOverlayInput(false);
                _nextInviteAt = Math.Max(_nextInviteAt, Time.unscaledTime + InviteDebounceSeconds);
            }
            NotifyInviteStateChanged();
        }
        private void SetSteamOverlayInput(bool active)
        {
            if (active)
            {
                ChatInputGate.BeginExternalOverlay();
                if (_overlayInputHeld) return;
                UiModalState.Push();
                _overlayInputHeld = true;
            }
            else
            {
                ChatInputGate.EndExternalOverlay();
                if (!_overlayInputHeld) return;
                UiModalState.Pop();
                _overlayInputHeld = false;
            }
        }

        private void NotifyInviteStateChanged()
        {
            try { RoomInviteStateChanged?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void NotifyInviteFeedback(string message)
        {
            try { RoomInviteFeedback?.Invoke(message); }
            catch (Exception ex) { Debug.LogException(ex); }
        }


        private void OnRoomJoinRequested(string? payload)
        {
            if (_destroyed) return;
            _roomJoin?.Receive(payload);
            NotifyChanged();
        }

        private void ReadSteamLaunchRoom()
        {
            if (!_ownsSteamApi || _destroyed) return;
            try
            {
                // Steam's API, not the OS command line, owns both cold and updated launches.
                const int bufferSize = 1024;
                int count = SteamApps.GetLaunchCommandLine(out var payload, bufferSize);
                if (count == 0) return;
                if (count < 0 || count >= bufferSize - 1 || payload == null)
                    _roomJoin?.RejectInvalid();
                else
                    OnRoomJoinRequested(payload);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatSession] Steam launch parameters unavailable: {ex.GetType().Name}");
            }
        }

        private void ClearAccountSession()
        {
            _roomPresence?.Reset(_ownsSteamApi, Time.unscaledTime);
            _inviteOverlayPending = false;
            _nextInviteAt = 0;
            if (!_steamOverlayActive)
                SetSteamOverlayInput(false);
            NotifyInviteStateChanged();
            _roomJoin?.Cancel();
            _accountGeneration++;
            _authenticatedSteamId = 0;
            _pendingTicket?.TrySetResult(null);
            CancelWebTicket();
            var previousLobby = _lobby;
            bool leaveRoom = ClientSession.SelectedOnlineMode == ClientSession.OnlineSelection.Room &&
                ClientSession.SelectedRoomId != Guid.Empty;
            ClientSession.ClearActiveMatchForAccountChange();
            _masterClient?.ClearAuthentication();
            ClientSession.ClearAccountData();
            if (previousLobby != null)
            {
                UnsubscribeLobby(previousLobby);
                if (leaveRoom)
                    _ = LeaveRoomThenDisconnectAsync(previousLobby);
                else
                    _ = previousLobby.DisconnectAsync();
            }
            _lobby = null;
            _authTask = null;
            _renewTask = null;
            _nameApplied = false;
            _chatStateReady = false;
            _directoryAvailable = false;
            _joinedRoomId = null;
            _roomTitleId = Guid.Empty;
            _roomTitleName = string.Empty;
            Self = null;
            _selfPlayerId = null;
            _onlinePlayers.Clear();
            _mutedProfiles.Clear();
            _mutedPlayerIds.Clear();
            _conversations.Clear();
            _activeConversation = GetOrCreateConversation("global", "GLOBAL");
            SetStatus("Steam account changed. Connecting with the current account…");
            AccountChanged?.Invoke();
        }

        private static async Task LeaveRoomThenDisconnectAsync(LobbyClient lobby)
        {
            try
            {
                Task leave = lobby.LeaveRoomAsync();
                if (await Task.WhenAny(leave, Task.Delay(2000)) == leave)
                    await leave;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatSession] Room leave during account switch failed: {ex.Message}");
            }
            await lobby.DisconnectAsync();
        }

        private void EnsureMasterClient()
        {
            if (_masterClient != null)
                return;
            _masterClient = new MasterServerClient(_masterServerUrl);
        }

        private async Task RefreshDirectoryCoreAsync()
        {
            var online = await _lobby!.GetOnlinePlayersAsync();
            if (online == null)
            {
                _directoryAvailable = false;
                return;
            }
            ReplaceOnlinePlayers(online);
            _directoryAvailable = true;
            RefreshConversationStates();
        }

        private void SubscribeLobby(LobbyClient lobby)
        {
            lobby.ChatMessageReceived += OnChatMessage;
            lobby.ChatPresenceChanged += OnPresence;
            lobby.ChatServerChanged += OnServerChanged;
            lobby.RoomUpdated += UpdateRoomTitle;
            lobby.Connected += OnLobbyConnected;
            lobby.Disconnected += OnLobbyDisconnected;
            lobby.Error += OnLobbyError;
            lobby.MatchStarted += OnMatchStarted;

            lobby.MatchAborted += OnMatchAborted;
            lobby.MatchStartedRejected += OnMatchStartedRejected;
        }
        private void UnsubscribeLobby(LobbyClient lobby)
        {
            lobby.ChatMessageReceived -= OnChatMessage;
            lobby.ChatPresenceChanged -= OnPresence;
            lobby.ChatServerChanged -= OnServerChanged;
            lobby.RoomUpdated -= UpdateRoomTitle;
            lobby.Connected -= OnLobbyConnected;
            lobby.Disconnected -= OnLobbyDisconnected;
            lobby.Error -= OnLobbyError;
            lobby.MatchStarted -= OnMatchStarted;
            lobby.MatchAborted -= OnMatchAborted;
            lobby.MatchStartedRejected -= OnMatchStartedRejected;
        }

        private void OnMatchStarted(MatchStartedConfig config)
        {
            if (config.RoomId is not Guid roomId ||
                ClientSession.SelectedOnlineMode != ClientSession.OnlineSelection.Room ||
                ClientSession.SelectedRoomId != roomId ||
                _lobby?.JoinedRoomId != roomId ||
                config.MatchId == Guid.Empty)
                return;
            ClientSession.ApplyMatchStarted(config);
        }


        private void OnLobbyConnected()
        {
            if (_connectTask == null)
                _ = RefreshAfterReconnectAsync();
        }

        private async Task RefreshAfterReconnectAsync()
        {
            if (_lobby == null || !_lobby.IsConnected)
                return;
            var snapshot = await _lobby.GetChatStateAsync();
            if (snapshot != null)
            {
                ApplySnapshot(snapshot);
                _chatStateReady = true;
                SetStatus("Connected");
            }
            await RefreshDirectoryCoreAsync();
            NotifyChanged();
        }

        private void OnLobbyDisconnected(Exception? exception)
        {
            _chatStateReady = false;
            SetStatus(_automaticRetries >= MaxAutomaticRetries
                ? "RETRY PAUSED"
                : "Master connection dropped. Retrying without resending messages.");
            NotifyChanged();
        }

        private void OnLobbyError(string message)
        {
            if (message.IndexOf("not_connected", StringComparison.OrdinalIgnoreCase) >= 0)
                _chatStateReady = false;
            NotifyChanged();
        }

        private void OnChatMessage(ChatMessage message)
        {
            if (message == null || message.MessageId == Guid.Empty)
                return;
            if (string.Equals(message.Channel, "server", StringComparison.OrdinalIgnoreCase))
            {
                if (message.RoomId is not Guid roomId || _lobby?.JoinedRoomId != roomId)
                    return;
                if (_joinedRoomId != roomId)
                {
                    SetCurrentRoom(roomId);
                    GetOrCreateConversation(RoomKey(roomId), RoomTitle(roomId));
                }
            }
            if (AddMessage(message))
                RaiseMessageArrived(message);
            NotifyChanged();
        }

        private void RaiseMessageArrived(ChatMessage message)
        {
            try { MessageArrived?.Invoke(message); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnPresence(ChatPresence presence)
        {
            if (presence?.Player == null)
                return;
            int index = _onlinePlayers.FindIndex(p => p.PlayerId == presence.Player.PlayerId);
            if (presence.Online)
            {
                if (index < 0) _onlinePlayers.Add(presence.Player);
                else _onlinePlayers[index] = presence.Player;
                if (_mutedPlayerIds.Contains(presence.Player.PlayerId))
                    _mutedProfiles[presence.Player.PlayerId] = presence.Player;
                if (Self?.PlayerId == presence.Player.PlayerId)
                    Self = presence.Player;
            }
            else if (index >= 0)
            {
                _onlinePlayers.RemoveAt(index);
            }
            _directoryAvailable = true;
            RefreshConversationTitles();
            RefreshConversationStates();
            NotifyChanged();
        }

        private void OnServerChanged(ServerChatState state)
        {
            Guid? roomId = state?.RoomId;
            if (roomId is Guid id && _lobby?.JoinedRoomId != id)
                return;
            SetCurrentRoom(roomId);
            if (roomId is Guid currentRoomId)
            {
                GetOrCreateConversation(RoomKey(currentRoomId), RoomTitle(currentRoomId));
                foreach (var message in state.Messages ?? Array.Empty<ChatMessage>())
                {
                    if (message.RoomId == currentRoomId)
                        AddMessage(message);
                }
            }
            RefreshConversationStates();
            NotifyChanged();
        }

        public void UpdateRoomTitle(RoomSnapshot room)
        {
            if (room == null ||
                (room.Id != _joinedRoomId && room.Id != ClientSession.SelectedRoomId))
                return;
            _roomTitleId = room.Id;
            _roomTitleName = room.Name;
            if (ClientSession.SelectedRoomId == room.Id)
                ClientSession.SelectedServerName = room.Name;
            var conversation = _conversations.FirstOrDefault(c => c.Key == RoomKey(room.Id));
            if (conversation == null)
                return;
            conversation.Title = RoomTitle(room.Id);
            NotifyChanged();
        }
        private void OnMatchAborted(MatchAbortedNotification notification)
        {
            if (!ClientSession.ApplyMatchAborted(notification.MatchId, notification.Reason))
                return;
            SetStatus(notification.Reason == "leader_returned_to_lobby"
                ? "The leader returned the group to the lobby."
                : $"Match aborted ({notification.Reason}). Returned to the lobby.");
            NotifyChanged();
        }

        private void OnMatchStartedRejected()
        {
            ClientSession.RejectMatchStart("Master sent an invalid match descriptor or content map.");
            SetStatus("The Master rejected this match descriptor. Returned to the lobby.");
            NotifyChanged();
        }


        private void ApplySnapshot(ChatSnapshot snapshot)
        {
            if (snapshot?.Self != null)
                ApplySelf(snapshot.Self);
            var global = GetOrCreateConversation("global", "GLOBAL");
            if (global != null)
                foreach (var message in snapshot?.GlobalMessages ?? Array.Empty<ChatMessage>())
                    AddMessage(message);
            Guid? roomId = snapshot?.Server?.RoomId;
            if (roomId is Guid id && _lobby?.JoinedRoomId != id)
                roomId = null;
            SetCurrentRoom(roomId);
            if (roomId is Guid currentRoomId)
            {
                GetOrCreateConversation(RoomKey(currentRoomId), RoomTitle(currentRoomId));
                foreach (var message in snapshot.Server.Messages ?? Array.Empty<ChatMessage>())
                {
                    if (message.RoomId == currentRoomId)
                        AddMessage(message);
                }
            }
            RefreshConversationStates();
            NotifyChanged();

        }
        /// <summary>Store a message once; false for duplicates and unroutable messages.</summary>
        private bool AddMessage(ChatMessage message)
        {
            if (message == null || message.MessageId == Guid.Empty)
                return false;
            string key;
            string title;
            switch ((message.Channel ?? string.Empty).ToLowerInvariant())
            {
                case "global":
                    key = "global";
                    title = "GLOBAL";
                    break;
                case "server" when message.RoomId is Guid roomId && _joinedRoomId == roomId:
                    key = RoomKey(roomId);
                    title = RoomTitle(roomId);
                    break;
                case "direct":
                    string? other = message.Sender?.PlayerId == _selfPlayerId
                        ? message.RecipientId
                        : message.Sender?.PlayerId;
                    if (string.IsNullOrEmpty(other)) return false;
                    key = DirectKey(other);
                    title = ResolveDirectTitle(other, message.Sender);
                    break;
                default:
                    return false;
            }
            var conversation = GetOrCreateConversation(key, title);
            if (conversation == null)
                return false;
            conversation.Title = string.IsNullOrEmpty(conversation.Title) ? title : conversation.Title;
            if (conversation.MutableMessages.Any(existing => existing.MessageId == message.MessageId))
                return false;
            conversation.MutableMessages.Add(message);
            bool trimmed = false;
            while (conversation.MutableMessages.Count > MaxMessagesPerConversation)
            {
                conversation.MutableMessages.RemoveAt(0);
                trimmed = true;
            }
            if (trimmed)
                ConversationHistoryTrimmed?.Invoke(conversation.Key);
            bool incoming = message.Sender?.PlayerId != _selfPlayerId;
            if (incoming && IsMessageVisible(message))
                conversation.Unread++;
            return true;
        }

        private ChatConversation? GetOrCreateConversation(string key, string title)
        {
            var existing = _conversations.FirstOrDefault(c => c.Key == key);
            if (existing != null)
                return existing;
            if (_conversations.Count >= MaxConversations)
            {
                var evict = _conversations.FirstOrDefault(c =>
                    c.Key != _activeConversation?.Key && !c.IsSending && string.IsNullOrEmpty(c.Draft));
                if (evict == null)
                    return null;
                _conversations.Remove(evict);
            }
            var conversation = new ChatConversation(key, title);
            _conversations.Add(conversation);
            return conversation;
        }

        private void ReplaceOnlinePlayers(IEnumerable<ChatPlayer> players)
        {
            _onlinePlayers.Clear();
            foreach (var player in players ?? Array.Empty<ChatPlayer>())
            {
                if (player == null || string.IsNullOrEmpty(player.PlayerId) || player.PlayerId == _selfPlayerId)
                    continue;
                if (_onlinePlayers.All(existing => existing.PlayerId != player.PlayerId))
                    _onlinePlayers.Add(player);
                if (_mutedPlayerIds.Contains(player.PlayerId))
                    _mutedProfiles[player.PlayerId] = player;
            }
            RefreshConversationTitles();
        }

        private static ChatPlayer ToChatPlayer(GuestUserInfo profile) =>
            new ChatPlayer(
                string.IsNullOrEmpty(profile.PlayerId)
                    ? profile.SteamId.ToString(CultureInfo.InvariantCulture)
                    : profile.PlayerId,
                profile.Username,
                profile.SessionTag);

        private void ApplySelf(ChatPlayer player)
        {
            if (player == null)
                return;
            Self = player;
            _selfPlayerId = player.PlayerId;
            ClientSession.SteamId = long.TryParse(player.PlayerId, out var id) ? id : ClientSession.SteamId;
            ClientSession.Username = player.DisplayName;
            _nameApplied = true;
            _savedNameRejected = false;
            RefreshConversationStates();
        }

        private ChatPlayer? FindKnownPlayer(string playerId)
        {
            var online = _onlinePlayers.FirstOrDefault(player => player.PlayerId == playerId);
            if (online != null)
                return online;
            foreach (var conversation in _conversations)
                foreach (var message in conversation.Messages)
                {
                    if (message.Sender?.PlayerId == playerId)
                        return message.Sender;
                }
            return null;
        }

        private void RefreshConversationTitles()
        {
            foreach (var conversation in _conversations.Where(c => c.Key.StartsWith("direct:", StringComparison.Ordinal)))
            {
                var id = conversation.Key.Substring("direct:".Length);
                var player = _onlinePlayers.FirstOrDefault(p => p.PlayerId == id);
                if (player != null)
                    conversation.Title = FormatPlayerTitle(player);
            }
        }

        private void RefreshConversationStates()
        {
            foreach (var conversation in _conversations)
            {
                conversation.CanSend = IsConnected &&
                    ChatTextValidation.TryValidateMessage(conversation.Draft, out _, out _) &&
                    IsDestinationAvailable(conversation.Key);
            }
        }

        private bool IsDestinationAvailable(string key)
        {
            if (key == "global") return true;
            if (TryRoomKey(key, out var roomId)) return _joinedRoomId == roomId;
            if (key.StartsWith("direct:", StringComparison.Ordinal))
            {
                if (!_directoryAvailable) return false;
                var id = key.Substring("direct:".Length);
                return _onlinePlayers.Any(player => player.PlayerId == id);
            }
            return false;
        }

        private void SetCurrentRoom(Guid? roomId)
        {
            if (roomId == Guid.Empty) roomId = null;
            Guid? previousRoomId = _joinedRoomId;
            _joinedRoomId = roomId;
            for (int i = _conversations.Count - 1; i >= 0; i--)
            {
                if (TryRoomKey(_conversations[i].Key, out var existingRoomId) &&
                    existingRoomId != roomId)
                    _conversations.RemoveAt(i);
            }
            // Joining a Room makes its Server Chat the destination, so "gg" in
            // the lobby or match reaches the other members, not everyone online.
            // The player can still switch destination afterwards.
            if (roomId is Guid joinedRoomId && previousRoomId != joinedRoomId &&
                GetOrCreateConversation(RoomKey(joinedRoomId), RoomTitle(joinedRoomId)) is { } room)
                _activeConversation = room;
            if (_activeConversation != null && !_conversations.Contains(_activeConversation))
                _activeConversation = GetOrCreateConversation("global", "GLOBAL");
        }

        private static string RoomKey(Guid roomId) => $"room:{roomId:D}";
        private static string DirectKey(string playerId) => $"direct:{playerId}";
        private static bool TryRoomKey(string key, out Guid roomId)
        {
            roomId = default;
            return key.StartsWith("room:", StringComparison.Ordinal) &&
                Guid.TryParse(key.Substring("room:".Length), out roomId);
        }
        private string RoomTitle(Guid roomId) =>
            roomId == _roomTitleId && !string.IsNullOrEmpty(_roomTitleName)
                ? $"ROOM // {_roomTitleName}"
                : roomId == ClientSession.SelectedRoomId && !string.IsNullOrEmpty(ClientSession.SelectedServerName)
                    ? $"ROOM // {ClientSession.SelectedServerName}"
                    : $"ROOM // {roomId:D}";
        private string ResolveDirectTitle(string playerId, ChatPlayer fallback) =>
            FormatPlayerTitle(_onlinePlayers.FirstOrDefault(p => p.PlayerId == playerId) ?? fallback ?? new ChatPlayer(playerId, playerId, string.Empty));
        private static string FormatPlayerTitle(ChatPlayer player) =>
            string.IsNullOrEmpty(player.SessionTag) ? player.DisplayName : $"{player.DisplayName} // {player.SessionTag}";

        private void SetStatus(string status)
        {
            _status = status ?? string.Empty;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private void OnDestroy()
        {
            if (_instance != this)
                return;
            _destroyed = true;
            _roomPresence?.Reset(_ownsSteamApi, Time.unscaledTime);
            _roomJoin?.Cancel();
            SetSteamOverlayInput(false);
            if (_lobby != null)
                UnsubscribeLobby(_lobby);
            _lobby?.DisconnectAsync();
            _masterClient?.Dispose();
            _lobby = null;
            _masterClient = null;
            _ticketCallback?.Dispose();
            _roomJoinCallback?.Dispose();
            _urlLaunchCallback?.Dispose();
            _overlayCallback?.Dispose();
            if (_ownsSteamApi)
                SteamAPI.Shutdown();
            _instance = null;
        }
    }
}
