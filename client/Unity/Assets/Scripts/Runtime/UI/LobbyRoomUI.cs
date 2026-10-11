#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared;
using SlopArena.Client.Network;
using SlopArena.Client.Input;
using SlopArena.Client;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Live lobby room page (issue #221): a fragment mounted into the
    /// FrontendShell hosts — roster and room details in the body, readiness/
    /// host state in the lower-right summary, and the leave/continue controls
    /// respecting authority outside the conversation cell. Driven by the
    /// existing master-server lobby connection.
    /// </summary>
    public class LobbyRoomUI : MonoBehaviour, IFrontendPageController
    {
        public static string? PendingRoomNotice;

        private const int MaxSlots = 4;
        private const float LobbyTimeoutSeconds = 12f;

        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private LobbyClient? _lobby;
        private ChatSession? _chatSession;
        private VisualElement? _playerList;
        private Button? _btnStart;
        private Button? _btnLeave;
        private Button? _btnInvite;
        private Button? _btnRetry;
        private Button? _backButton;
        private Label? _lblStatus;
        private Label? _lblServer;
        private Label? _lblInviteHint;
        private LobbySnapshot? _snapshot;
        private CancellationTokenSource? _lifecycleCts;
        private RoomSnapshot? _roomSnapshot;
        private bool _roomMode;

        private Coroutine? _lobbyWatchdog;
        private Coroutine? _startWatchdog;
        private bool _alive;
        private bool _awaitingLobby;
        private bool _startPending;
        private bool _leaving;
        private int _attempt;
        private float _nextInviteRefreshAt;
        private bool _overlayBlocked;
        private VisualElement? _focusBeforeOverlay;

        private void OnEnable()
        {
            _alive = true;
            _leaving = false;
            _overlayBlocked = false;
            _focusBeforeOverlay = null;
            _nextInviteRefreshAt = 0;
            // Page re-entry is a fresh room (ADR-0032): a roster or start
            // attempt left by a previous visit must not outlive its page.
            _snapshot = null;
            _startPending = false;
            _roomMode = ClientSession.SelectedOnlineMode == ClientSession.OnlineSelection.Room;
            _roomSnapshot = null;
            _lifecycleCts = new CancellationTokenSource();
            int generation = ++_attempt;
            _playerList = _context.Q<VisualElement>("player-list");
            _btnStart = _context.Q<Button>("btn-start");
            _btnLeave = _context.Q<Button>("btn-leave");
            _btnInvite = _context.Q<Button>("btn-invite");
            _btnRetry = _context.Q<Button>("btn-retry");
            _backButton = _context.Q<Button>("btn-back");
            _lblStatus = _context.Q<Label>("lbl-status");
            _lblInviteHint = _context.Q<Label>("lbl-invite-hint");
            _lblServer = _context.Q<Label>("lbl-server");
            if (_lblServer != null) _lblServer.enableRichText = false;
            if (_lblInviteHint != null) _lblInviteHint.enableRichText = false;

            if (_lblServer != null)
                _lblServer.text = string.IsNullOrEmpty(ClientSession.SelectedServerName)
                    ? (_roomMode ? ClientSession.SelectedRoomId.ToString() : ClientSession.SelectedServerId.ToString())
                    : ClientSession.SelectedServerName;
            if (_backButton != null) _backButton.clicked += Leave;
            if (_btnLeave != null) _btnLeave.clicked += Leave;
            if (_btnInvite != null)
            {
                _btnInvite.style.display = _roomMode ? DisplayStyle.Flex : DisplayStyle.None;
                _btnInvite.SetEnabled(false);
                _btnInvite.clicked += InviteFriend;
            }
            if (_btnRetry != null)
            {
                _btnRetry.clicked += RetryConnection;
                _btnRetry.style.display = DisplayStyle.None;
            }
            if (_btnStart != null)
            {
                _btnStart.clicked += OnStartClicked;
                _btnStart.style.display = DisplayStyle.None;
            }
            // Leave is always visible; never focus a hidden recovery action first.
            MenuNavigation.Configure(_context, _btnLeave ?? _btnStart ?? _btnRetry, Leave, _btnStart);

            RenderPlayers();
            var chat = ChatSession.Instance;
            _chatSession = chat;
            if (chat != null)
            {
                chat.AccountChanged += OnAccountChanged;
                chat.ActiveLobbyChanged += OnActiveLobbyChanged;
                chat.RoomInviteStateChanged += OnInviteStateChanged;
                chat.RoomInviteFeedback += OnInviteFeedback;
            }
            if (chat == null)
            {
                SetStatus("Chat session unavailable. Return to the server browser.", true);
                SetRetryVisible(false);
                return;
            }

            _lobby = chat.ActiveLobby;
            if (_lobby != null)
                SubscribeLobby(_lobby);
            OnInviteStateChanged();
            _awaitingLobby = true;
            SetStatus(chat.NeedsDisplayName
                ? "Choose a display name before joining a room."
                : (_lobby?.IsConnected == true ? "Rejoining the room…" : "Connecting to the room…"), false);
            SetRetryVisible(false);
            StartLobbyWatchdog(generation);
            ConnectAndJoin(generation, _lifecycleCts.Token);
        }

        private void Update()
        {
            if (!_alive || !_roomMode || Time.unscaledTime < _nextInviteRefreshAt) return;
            _nextInviteRefreshAt = Time.unscaledTime + 0.5f;
            OnInviteStateChanged();
        }

        private void SubscribeLobby(LobbyClient lobby)
        {
            if (_roomMode)
            {
                lobby.Connected += OnConnected;
                lobby.Disconnected += OnDisconnected;
                lobby.RoomUpdated += OnRoomUpdated;
                lobby.RoomDeleted += OnRoomDeleted;
                lobby.RoomMembershipRevoked += OnRoomMembershipRevoked;
                return;
            }
            lobby.Connected += OnConnected;
            lobby.PlayerJoined += OnPlayerJoined;
            lobby.PlayerLeft += OnPlayerLeft;
            lobby.LobbyUpdated += OnLobbyUpdated;
            lobby.MatchStarting += OnMatchStarting;
            lobby.StageSelect += OnServerStageSelect;
            lobby.MatchStarted += OnServerMatchStarted;
            lobby.Error += OnError;
            lobby.Disconnected += OnDisconnected;
        }

        private async void ConnectAndJoin(int generation, CancellationToken ct)
        {
            if (_lobby == null)
            {
                var chat = ChatSession.Instance;
                if (chat == null || !await chat.EnsureConnectedAsync())
                {
                    if (IsCurrent(generation, ct))
                        FailLobby(chat?.NeedsDisplayName == true
                            ? "Choose a display name before joining a room."
                            : "Couldn’t connect to the room directory. Retry, or return to the server browser.");
                    return;
                }
                var activeLobby = chat.ActiveLobby;
                if (!ReferenceEquals(_lobby, activeLobby))
                {
                    if (_lobby != null) UnsubscribeLobby(_lobby);
                    _lobby = activeLobby;
                    if (_lobby != null)
                        SubscribeLobby(_lobby);
                }
            }
            if (_lobby == null)
                return;
            try
            {
                bool connected = _lobby.IsConnected || await _lobby.ConnectAsync();
                if (!IsCurrent(generation, ct)) return;
                if (!connected)
                {
                    FailLobby("Couldn’t connect to the room directory. Retry, or return to the server browser.");
                    return;
                }

                if (_roomMode)
                {
                    var room = await _lobby.GetMyRoomAsync();
                    if (!IsCurrent(generation, ct)) return;
                    if (room == null || room.Id != ClientSession.SelectedRoomId)
                    {
                        ReturnToBrowser("You are no longer a member of this room. Return to the browser and join another room.");
                        return;
                    }
                    OnRoomUpdated(room);
                }
                else
                {
                    await _lobby.JoinLobbyAsync(ClientSession.SelectedServerId);
                    if (!IsCurrent(generation, ct)) return;
                    SetStatus("Connected. Waiting for the room roster…", false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LobbyRoom] Connection failed: {ex.Message}");
                if (IsCurrent(generation, ct))
                    FailLobby("Couldn’t join this room. Retry, or return to the server browser.");
            }
        }

        private void StartLobbyWatchdog(int generation)
        {
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            _lobbyWatchdog = StartCoroutine(LobbyWatchdog(generation));
        }

        private System.Collections.IEnumerator LobbyWatchdog(int generation)
        {
            yield return new WaitForSeconds(LobbyTimeoutSeconds);
            if (_alive && generation == _attempt && _awaitingLobby)
                FailLobby("The room did not answer in time. Retry the connection or return to the browser.");
        }

        private void RetryConnection()
        {
            if (!_alive || _leaving)
                return;
            _attempt++;
            var replacement = ChatSession.Instance?.ActiveLobby;
            if (!ReferenceEquals(_lobby, replacement))
            {
                if (_lobby != null) UnsubscribeLobby(_lobby);
                _lobby = replacement;
                if (_lobby != null) SubscribeLobby(_lobby);
            }
            _awaitingLobby = true;
            _startPending = false;
            SetRetryVisible(false);
            SetStatus("Retrying the room connection…", false);
            StartLobbyWatchdog(_attempt);
            ConnectAndJoin(_attempt, _lifecycleCts!.Token);
        }


        private void OnActiveLobbyChanged(LobbyClient? lobby)
        {
            if (!_alive || !_roomMode || ReferenceEquals(_lobby, lobby))
                return;
            if (_lobby != null)
                UnsubscribeLobby(_lobby);
            _lobby = lobby;
            _roomSnapshot = null;
            RenderPlayers();
            if (lobby != null)
                SubscribeLobby(lobby);
            _awaitingLobby = true;
            SetRetryVisible(false);
            SetStatus(lobby == null
                ? "Room authentication expired. Reconnecting…"
                : "Reconnecting to your Room…", false);
        }

        private void OnConnected()
        {
            if (!_alive) return;
            if (_roomMode)
            {
                if (_roomSnapshot == null)
                {
                    _awaitingLobby = true;
                    ConnectAndJoin(_attempt, _lifecycleCts?.Token ?? CancellationToken.None);
                }
                return;
            }
            if (!_awaitingLobby) return;
            SetStatus("Connected. Waiting for the room roster…", false);

        }

        private void OnPlayerJoined(LobbyPlayerInfo player)
        {
            if (_alive)
                Debug.Log($"[LobbyRoom] {player.Name} (SteamId {player.SteamId}) joined.");
        }

        private void OnPlayerLeft(long steamId)
        {
            if (_alive)
                Debug.Log($"[LobbyRoom] SteamId {steamId} left.");
        }

        private void OnLobbyUpdated(LobbySnapshot snapshot)
        {
            if (!_alive) return;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            _snapshot = snapshot;
            SetRetryVisible(false);
            RenderPlayers();
        }
        private void OnRoomUpdated(RoomSnapshot room)
        {
            if (!_alive || !_roomMode || room.Id != ClientSession.SelectedRoomId)
                return;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            _startPending = false;
            if (_startWatchdog != null) StopCoroutine(_startWatchdog);
            _roomSnapshot = room;
            ClientSession.SelectedServerName = room.Name;
            ChatSession.Instance?.UpdateRoomTitle(room);
            if (_lblServer != null) _lblServer.text = room.Name;
            SetRetryVisible(false);
            RenderPlayers();
            if (!string.IsNullOrWhiteSpace(PendingRoomNotice))
            {
                SetStatus(PendingRoomNotice, false);
                PendingRoomNotice = null;
            }
            if (string.Equals(room.Phase, "Character Select", StringComparison.Ordinal))
            {
                MatchConfig.Mode = GameMode.PvP;
                FrontendController.Show(FrontendPage.FighterSelect);
            }
            else if (string.Equals(room.Phase, "Stage Select", StringComparison.Ordinal))
            {
                MatchConfig.Mode = GameMode.PvP;
                FrontendController.Show(FrontendPage.StageSelect);
            }
            else if (string.Equals(room.Phase, "Match Starting", StringComparison.Ordinal) ||
                string.Equals(room.Phase, "In Match", StringComparison.Ordinal))
            {
                MatchConfig.Mode = GameMode.PvP;
                FrontendController.Show(FrontendPage.StageSelect);
            }
        }

        private void OnRoomDeleted(Guid roomId)
        {
            if (_alive && _roomMode && roomId == ClientSession.SelectedRoomId)
                ReturnToBrowser("This room has closed.");
        }

        private void OnRoomMembershipRevoked(Guid roomId)
        {
            if (_alive && _roomMode && roomId == ClientSession.SelectedRoomId)
                ReturnToBrowser("Your Room membership ended.");
        }

        private void OnAccountChanged()
        {
            if (_alive && _roomMode)
                ReturnToBrowser("Your Steam account changed. Reconnect before joining a Room.");
        }

        private void ReturnToBrowser(string notice)
        {
            PendingRoomNotice = null;
            ClientSession.SelectedRoomId = Guid.Empty;
            ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
            ServerBrowserUI.PendingReturnNotice = notice;
            _alive = false;
            FrontendController.Show(FrontendPage.ServerBrowser);
        }

        private void OnMatchStarting(MatchStartingConfig config)
        {
            if (!_alive || _leaving) return;
            _awaitingLobby = false;
            ClientSession.LobbyRoster = new LobbySnapshot(config.ServerId, config.Players);
            MatchConfig.Mode = GameMode.PvP;
            // Host authority comes from the authoritative roster at every
            // server-driven transition (issue #213), not from a local hosting
            // flag; CharSelectController recomputes the same way.
            ClientSession.IsLobbyHost = IsRosterHost(config.Players);
            FrontendController.Show(FrontendPage.FighterSelect);
        }

        private void OnServerStageSelect(MatchStartingConfig config)
        {
            // A player who locked in and backed out to this room is still in
            // the match-start flow: the host's stage-select push advances this
            // page too (issue #213). LobbyRoomUI shows no dialogs, but page
            // deactivation still drops any departed-page state.
            if (!_alive || _leaving) return;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            ClientSession.LobbyRoster = new LobbySnapshot(config.ServerId, config.Players);
            MatchConfig.Mode = GameMode.PvP;
            ClientSession.IsLobbyHost = IsRosterHost(config.Players);
            FrontendController.Show(FrontendPage.StageSelect);
        }

        private void OnServerMatchStarted(MatchStartedConfig config)
        {
            if (config.RoomId != null)
                return;

            if (!_alive || _leaving) return;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            ClientSession.ApplyMatchStarted(config);
        }

        private static bool IsRosterHost(IReadOnlyList<LobbyPlayerInfo> players)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].SteamId == ClientSession.SteamId && players[i].IsHost)
                    return true;
            }
            return false;
        }

        private void OnError(string message)
        {
            if (!_alive) return;
            if (_startPending)
            {
                _startPending = false;
                if (_startWatchdog != null) StopCoroutine(_startWatchdog);
                RenderPlayers();
            }
            FailLobby(string.IsNullOrWhiteSpace(message)
                ? "The room rejected that request. Retry, or return to the browser."
                : message);
        }

        private void OnDisconnected(Exception? ex)
        {
            if (!_alive) return;
            if (_startPending)
            {
                _startPending = false;
                if (_startWatchdog != null) StopCoroutine(_startWatchdog);
                RenderPlayers();
            }
            if (_roomMode)
            {
                _roomSnapshot = null;
                RenderPlayers();
            }
            _awaitingLobby = true;
            SetRetryVisible(true);
            SetStatus(ex == null
                ? "The room connection closed. Retry, or return to the browser."
                : "The room connection dropped. Retry, or return to the browser.", true);
        }

        private void InviteFriend()
        {
            if (!_alive || !_roomMode || _leaving || !_context.Valid) return;
            if (_chatSession == null)
            {
                SetStatus("Steam session unavailable. Reconnect to invite.", true);
                return;
            }
            bool opened = _chatSession.TryInviteRoom(_roomSnapshot, out var feedback);
            SetStatus(feedback, !opened);
            OnInviteStateChanged();
        }

        private void OnInviteFeedback(string message)
        {
            if (_alive && _roomMode)
                SetStatus(message, true);
            OnInviteStateChanged();
        }

        private void OnInviteStateChanged()
        {
            if (!_alive || !_roomMode) return;
            bool blocked = ChatInputGate.ExternalOverlaySuppressed;
            VisualElement? restore = null;
            if (_overlayBlocked != blocked)
            {
                if (blocked)
                {
                    var focused = _context.Shell?.Root?.panel?.focusController?.focusedElement as VisualElement;
                    _focusBeforeOverlay = null;
                    foreach (var root in _context.OwnedRoots)
                        if (focused != null && root.Contains(focused))
                            _focusBeforeOverlay = focused;
                }
                foreach (var root in _context.OwnedRoots)
                    root.SetEnabled(!blocked);
                _overlayBlocked = blocked;
                if (!blocked)
                {
                    restore = _focusBeforeOverlay;
                    _focusBeforeOverlay = null;
                }
            }
            RenderInviteState();
            if (restore != null)
            {
                if (restore.panel != null && restore.enabledInHierarchy)
                    restore.Focus();
                else if (_btnLeave?.enabledInHierarchy == true)
                    _btnLeave.Focus();
            }

        }

        private void RenderInviteState()
        {
            if (_btnInvite == null || !_roomMode) return;
            string? reason = _leaving
                ? "Leaving this Room. Wait before inviting."
                : _chatSession?.GetRoomInviteUnavailableReason(_roomSnapshot) ??
                    "Steam session is unavailable. Reconnect to invite.";
            _btnInvite.SetEnabled(reason == null && !_overlayBlocked);
            _btnInvite.tooltip = reason ?? "Open Steam's friend invitation dialog for this Room.";
            if (_lblInviteHint != null)
            {
                _lblInviteHint.text = reason ?? string.Empty;
                _lblInviteHint.style.display = reason == null ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void OnStartClicked()
        {
            if (!_alive || _lobby == null || _startPending || !_lobby.IsConnected)
                return;
            if (_roomMode)
            {
                if (_roomSnapshot == null || !IsLocalRoomLeader(_roomSnapshot) ||
                    !string.Equals(_roomSnapshot.Phase, "Lobby", StringComparison.Ordinal))
                    return;
                _startPending = true;
                _btnStart?.SetEnabled(false);
                SetStatus("Opening character select…", false);
                if (_startWatchdog != null) StopCoroutine(_startWatchdog);
                _startWatchdog = StartCoroutine(StartWatchdog());
                _ = StartRoomCharacterSelect();
                return;
            }
            _startPending = true;
            _btnStart?.SetEnabled(false);
            SetStatus("Starting the match…", false);
            if (_startWatchdog != null) StopCoroutine(_startWatchdog);
            _startWatchdog = StartCoroutine(StartWatchdog());
            _ = StartMatchRequest();
        }

        private async Task StartRoomCharacterSelect()
        {
            try
            {
                var room = await _lobby!.RoomStartCharacterSelectAsync();
                if (_alive) OnRoomUpdated(room);
            }
            catch (Exception ex)
            {
                if (_alive) OnError($"Couldn’t start character select: {ex.Message}");
            }
        }

        private static bool IsLocalRoomLeader(RoomSnapshot room)
        {
            var members = room.Members ?? Array.Empty<RoomMemberInfo>();
            for (int i = 0; i < members.Length; i++)
                if (members[i].SteamId == ClientSession.SteamId)
                    return members[i].IsLeader;
            return false;
        }
        private async Task StartMatchRequest()
        {
            try { await _lobby!.HostStartAsync(); }
            catch (Exception ex)
            {
                if (_alive) OnError($"Couldn’t start the match: {ex.Message}");
            }
        }

        private System.Collections.IEnumerator StartWatchdog()
        {
            yield return new WaitForSeconds(LobbyTimeoutSeconds);
            if (_alive && _startPending)
            {
                _startPending = false;
                RenderPlayers();
                SetStatus(_roomMode
                    ? "Character select did not open in time. Check the Room, then try again."
                    : "The match did not start in time. Check the room, then try again.", true);
            }
        }

        private void RenderPlayers()
        {
            if (_playerList == null) return;
            _playerList.Clear();
            if (_roomMode)
            {
                var members = _roomSnapshot?.Members ?? Array.Empty<RoomMemberInfo>();
                int capacity = Math.Min(MaxSlots, Math.Max(1, _roomSnapshot?.Capacity ?? MaxSlots));
                for (int i = 0; i < capacity; i++)
                    _playerList.Add(CreateRoomSlot(i, members));
                bool leader = _roomSnapshot != null && IsLocalRoomLeader(_roomSnapshot);
                bool lobbyPhase = string.Equals(_roomSnapshot?.Phase, "Lobby", StringComparison.Ordinal);
                if (_btnStart != null)
                {
                    _btnStart.style.display = leader && lobbyPhase ? DisplayStyle.Flex : DisplayStyle.None;
                    _btnStart.text = "CHOOSE FIGHTERS";
                    _btnStart.SetEnabled(leader && lobbyPhase && members.Length >= 2 &&
                        !_startPending && _lobby?.IsConnected == true);
                }
                if (_roomSnapshot != null)
                    SetStatus($"{_roomSnapshot.Phase} — {_roomSnapshot.MemberCount}/{_roomSnapshot.Capacity} members.", false);
                RenderInviteState();
                return;
            }
            var players = _snapshot?.Players ?? Array.Empty<LobbyPlayerInfo>();
            bool isLocalHost = false;
            for (int i = 0; i < players.Count; i++)
                isLocalHost |= players[i].SteamId == ClientSession.SteamId && players[i].IsHost;

            for (int i = 0; i < MaxSlots; i++)
                _playerList.Add(CreateSlot(i, players));

            if (_btnStart != null)
            {
                _btnStart.style.display = isLocalHost ? DisplayStyle.Flex : DisplayStyle.None;
                _btnStart.SetEnabled(isLocalHost && players.Count >= 2 && !_startPending);
            }
            if (isLocalHost)
                SetStatus(players.Count >= 2 ? "Your room is ready. Start when everyone is here." : "Waiting for another player to join…", false);
            else if (players.Count > 0)
                SetStatus("Connected. Waiting for the host to start…", false);
        }

        private VisualElement CreateSlot(int index, IReadOnlyList<LobbyPlayerInfo> players)
        {
            var slot = new VisualElement { name = "player-slot" };
            slot.AddToClassList("player-slot");
            var slotIndex = new Label($"P{index + 1}") { name = "slot-index" };
            slotIndex.AddToClassList("slot-index");
            var name = new Label("Open slot") { name = "slot-name", enableRichText = false };
            name.AddToClassList("slot-name");
            if (index < players.Count)
            {
                var player = players[index];
                name.text = player.Name;
                if (player.IsHost)
                {
                    var badge = new Label("HOST") { name = "host-badge" };
                    badge.AddToClassList("host-badge");
                    slot.Add(badge);
                }
            }
            slot.Add(slotIndex);
            slot.Add(name);
            return slot;
        }
        private VisualElement CreateRoomSlot(int index, IReadOnlyList<RoomMemberInfo> members)
        {
            var slot = new VisualElement { name = "player-slot" };
            slot.AddToClassList("player-slot");
            slot.Add(new Label($"P{index + 1}") { name = "slot-index" });
            var name = new Label(index < members.Count ? members[index].Name : "Open slot")
            {
                name = "slot-name",
                enableRichText = false
            };
            slot.Add(name);
            if (index < members.Count && members[index].IsLeader)
                slot.Add(new Label("LEADER") { name = "leader-badge" });
            if (index < members.Count)
            {
                var member = members[index];
                if (!string.IsNullOrWhiteSpace(member.CharacterSelection))
                    slot.Add(new Label(member.CharacterSelection.ToUpperInvariant()) { name = "room-character" });
                slot.Add(new Label(member.LockedIn ? "LOCKED" : "WAITING")
                    { name = "room-member-status" });
            }
            return slot;
        }


        private void FailLobby(string message)
        {
            if (!_alive) return;
            _attempt++;
            _awaitingLobby = false;
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            SetStatus(message, true);
            SetRetryVisible(true);
        }

        private void SetStatus(string message, bool error)
        {
            if (_lblStatus == null) return;
            _lblStatus.text = message;
            if (error) _lblStatus.AddToClassList("error");
            else _lblStatus.RemoveFromClassList("error");
        }

        private void SetRetryVisible(bool visible)
        {
            if (_btnRetry == null) return;
            _btnRetry.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _btnRetry.SetEnabled(visible && !_leaving);
        }

        private bool IsCurrent(int generation, CancellationToken ct) =>
            _alive && !_leaving && generation == _attempt && !ct.IsCancellationRequested;

        private async void Leave()
        {
            if (!_alive || _leaving) return;
            _leaving = true;
            RenderInviteState();
            _attempt++;
            if (!_roomMode) _lifecycleCts?.Cancel();
            if (_roomMode)
            {
                try
                {
                    if (_lobby == null)
                        throw new InvalidOperationException("Not connected to the room directory.");
                    await _lobby.LeaveRoomAsync();
                    ClientSession.SelectedRoomId = Guid.Empty;
                    ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LobbyRoom] Room leave failed: {ex.Message}");
                    if (ex.Message.Contains("not_in_room", StringComparison.OrdinalIgnoreCase))
                    {
                        ClientSession.SelectedRoomId = Guid.Empty;
                        ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
                        _alive = false;
                        FrontendController.Show(FrontendPage.ServerBrowser);
                        return;
                    }
                    _leaving = false;
                    SetStatus("Couldn’t leave the room. Check your connection and retry.", true);
                    _btnLeave?.SetEnabled(true);
                    return;
                }
            }
            else
            {
                if (MatchConfig.IsHost)
                    ServerHost.Instance?.Stop();
                if (_lobby != null)
                    await Task.WhenAny(_lobby.LeaveLobbyAsync(), Task.Delay(2000));
            }
            _alive = false;
            FrontendController.Show(FrontendPage.ServerBrowser);
        }

        private void OnDisable()
        {
            _alive = false;
            _attempt++;
            _lifecycleCts?.Cancel();
            if (_lobbyWatchdog != null) StopCoroutine(_lobbyWatchdog);
            if (_startWatchdog != null) StopCoroutine(_startWatchdog);
            if (_backButton != null) _backButton.clicked -= Leave;
            if (_btnLeave != null) _btnLeave.clicked -= Leave;
            if (_btnInvite != null) _btnInvite.clicked -= InviteFriend;
            if (_btnRetry != null) _btnRetry.clicked -= RetryConnection;
            if (_btnStart != null) _btnStart.clicked -= OnStartClicked;
            if (_chatSession != null)
            {
                _chatSession.AccountChanged -= OnAccountChanged;
                _chatSession.ActiveLobbyChanged -= OnActiveLobbyChanged;
                _chatSession.RoomInviteStateChanged -= OnInviteStateChanged;
                _chatSession.RoomInviteFeedback -= OnInviteFeedback;
            }
            _chatSession = null;
            _focusBeforeOverlay = null;
            _overlayBlocked = false;
            if (_lobby != null) UnsubscribeLobby(_lobby);
            _lifecycleCts?.Dispose();
            _lifecycleCts = null;
        }

        private void UnsubscribeLobby(LobbyClient lobby)
        {
            if (_roomMode)
            {
                lobby.Connected -= OnConnected;
                lobby.Disconnected -= OnDisconnected;
                lobby.RoomUpdated -= OnRoomUpdated;
                lobby.RoomDeleted -= OnRoomDeleted;
                lobby.RoomMembershipRevoked -= OnRoomMembershipRevoked;
                return;
            }
            lobby.Connected -= OnConnected;
            lobby.PlayerJoined -= OnPlayerJoined;
            lobby.PlayerLeft -= OnPlayerLeft;
            lobby.LobbyUpdated -= OnLobbyUpdated;
            lobby.MatchStarting -= OnMatchStarting;
            lobby.StageSelect -= OnServerStageSelect;
            lobby.MatchStarted -= OnServerMatchStarted;
            lobby.Error -= OnError;
            lobby.Disconnected -= OnDisconnected;
        }
    }
}
