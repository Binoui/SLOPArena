using System;
using System.Globalization;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Shared;
using SlopArena.Client;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    /// <summary>Server discovery, host-and-play, and advanced address entry
    /// (issue #221): a fragment mounted into the FrontendShell hosts — the
    /// room list and compact hosting/address tools in the body, connection/
    /// operation feedback in the lower-right, and the primary host/join
    /// actions outside the conversation cell. The direct-connect form opens
    /// as a page-owned modal in the shell modal host.</summary>
    public class ServerBrowserUI : MonoBehaviour, IFrontendPageController
    {
        private const int DefaultServerPort = 9876;
        private const int HostRegistrationTimeoutMs = 15000;

        /// <summary>
        /// One-shot status shown on the next browser activation (issue #213):
        /// Results sets it when a completed PvP match cannot return to its
        /// LobbyRoom, so the browser explains why the room is gone.
        /// </summary>
        public static string? PendingReturnNotice;

        /// <summary>Notice captured for this activation; shown until the scan settles.</summary>
        private string? _pendingReturnNotice;


        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private MasterServerClient _masterClient;
        private VisualElement _serverList;
        private Label _lblStatus;
        private Button _btnRefresh;
        private Button _btnBack;
        private Button _btnHost;
        private Button _btnHostCancel;
        private TextField _hostIpField;
        private Label _lblHostStatus;
        private TextField _roomNameField;
        private Button _btnCreateRoom;
        private ChatSession? _chatSession;
        private LobbyClient? _roomDirectoryLobby;


        private Button _btnDirectConnect;
        private VisualElement _directConnectModal;
        private TextField _ipField;
        private Label _directConnectStatus;
        private Focusable _focusBeforeModal;
        private Button _modalClose;
        private Button _modalJoin;
        private bool _modalPresented;

        private CancellationTokenSource _lifecycleCts;
        private CancellationTokenSource _refreshCts;
        private CancellationTokenSource _hostCts;
        private CancellationTokenSource _addressCts;
        private CancellationTokenSource? _roomActionCts;
        private bool _alive;
        private bool _refreshing;
        private bool _roomListDirty;

        private bool _joining;
        private bool _hostStarting;
        private bool _hostHandedOff;
        private int _operationVersion;
        private ServerHost _ownedHost;

        private void OnEnable()
        {
            _refreshing = _joining = _hostStarting = false;
            _alive = true;
            _hostHandedOff = false;
            _lifecycleCts = new CancellationTokenSource();

            _serverList = _context.Q<ScrollView>("server-list");
            _lblStatus = _context.Q<Label>("lbl-status");
            if (_lblStatus != null) _lblStatus.enableRichText = false;
            _btnRefresh = _context.Q<Button>("btn-refresh");
            _btnBack = _context.Q<Button>("btn-back");
            _btnHost = _context.Q<Button>("btn-host");
            _btnHostCancel = _context.Q<Button>("btn-host-cancel");
            _hostIpField = _context.Q<TextField>("host-ip-field");
            _lblHostStatus = _context.Q<Label>("lbl-host-status");
            _btnDirectConnect = _context.Q<Button>("btn-direct-connect");
            _directConnectModal = _context.Q<VisualElement>("direct-connect-modal");
            _ipField = _context.Q<TextField>("ip-field");
            _directConnectStatus = _context.Q<Label>("direct-connect-status");
            _roomNameField = _context.Q<TextField>("room-name-field");
            bool showLegacyTools = IsDevelopmentLegacyRouteEnabled();
            var legacyHostPanel = _context.Q<VisualElement>("legacy-host-panel");
            var directConnectPanel = _context.Q<VisualElement>("direct-connect-panel");
            if (legacyHostPanel != null)
                legacyHostPanel.style.display = showLegacyTools ? DisplayStyle.Flex : DisplayStyle.None;
            if (directConnectPanel != null)
                directConnectPanel.style.display = showLegacyTools ? DisplayStyle.Flex : DisplayStyle.None;
            if (_btnHost != null)
                _btnHost.style.display = showLegacyTools ? DisplayStyle.Flex : DisplayStyle.None;
            if (_btnDirectConnect != null)
                _btnDirectConnect.style.display = showLegacyTools ? DisplayStyle.Flex : DisplayStyle.None;


            _btnCreateRoom = _context.Q<Button>("btn-create-room");
            if (_btnCreateRoom != null) _btnCreateRoom.clicked += CreateRoom;


            if (_btnRefresh != null) _btnRefresh.clicked += RefreshServers;
            if (_btnBack != null) _btnBack.clicked += LeaveBrowser;
            if (_btnHost != null) _btnHost.clicked += OnHostClicked;
            if (_btnDirectConnect != null) _btnDirectConnect.clicked += OpenDirectConnect;
            if (_btnHostCancel != null) _btnHostCancel.clicked += CancelHost;
            _modalClose = _context.Q<Button>("btn-modal-close");
            _modalJoin = _context.Q<Button>("btn-modal-join");
            if (_modalClose != null) _modalClose.clicked += CloseDirectConnect;
            if (_modalJoin != null) _modalJoin.clicked += JoinDirectConnect;

            // Page re-entry is a fresh browser (ADR-0032): rows left by a
            // previous visit's scan must not survive their operation.
            _serverList?.Clear();
            if (_directConnectStatus != null)
            {
                _directConnectStatus.text = string.Empty;
                _directConnectStatus.RemoveFromClassList("error");
            }

            if (_btnHostCancel != null)
                _btnHostCancel.style.display = DisplayStyle.None;
            if (_lblHostStatus != null)
            {
                _lblHostStatus.style.display = DisplayStyle.None;
                _lblHostStatus.text = string.Empty;
            }
            if (_directConnectModal != null)
            {
                // The page-modal section lives in the shell modal host
                // (issue #221): its cancel route resolves through the page
                // context registration and the focus router's page-modal
                // layer — no separate page-root registration.
                _directConnectModal.RegisterCallback<KeyDownEvent>(OnModalKeyDown);
            }

            var initial = _btnRefresh ?? _btnHost ?? _btnDirectConnect;
            // Escape returns to Home; Start acts only on a focused join row, if any.
            if (initial != null)
                MenuNavigation.Configure(_context, initial, LeaveBrowser);
            ChatSession.ConfigureMasterServerUrl(ClientSession.MasterServerUrl);
            _chatSession = ChatSession.Instance;
            if (_chatSession != null)
            {
                _chatSession.AccountChanged += OnAccountChanged;
                _chatSession.ActiveLobbyChanged += OnActiveLobbyChanged;
                SetRoomDirectoryLobby(_chatSession.ActiveLobby);
            }
            _masterClient = ChatSession.Instance?.MasterClient;

            // The return explanation is captured for this activation and shown
            // until the scan settles; SetBrowserStatus suppresses loading
            // replacements while it is pending (issue #213).
            _pendingReturnNotice = PendingReturnNotice;
            PendingReturnNotice = null;
            if (_pendingReturnNotice != null)
            {
                Debug.Log($"[ServerBrowser] {_pendingReturnNotice}");
                SetBrowserStatus(_pendingReturnNotice, loading: false);
            }
            RefreshServers();
        }
        private async void RefreshServers()
        {
            if (!_alive || _refreshing || _joining)
                return;

            _refreshing = true;
            _roomListDirty = false;
            int operation = ++_operationVersion;
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
            CancellationToken ct = _refreshCts.Token;

            SetBrowserStatus("Looking for public rooms…", loading: true);

            try
            {
                var chat = ChatSession.Instance;
                SetBrowserStatus("Connecting to the room directory…", loading: true);
                if (chat == null || !await chat.EnsureConnectedAsync() || !IsCurrent(operation, ct))
                {
                    if (IsCurrent(operation, ct))
                        ShowBrowserFailure(chat?.NeedsDisplayName == true
                            ? "Choose a display name before browsing rooms."
                            : "Couldn’t reach the room directory. Check your connection, then retry.");
                    return;
                }
                var lobby = chat.ActiveLobby;
                if (lobby == null)
                {
                    ShowBrowserFailure("Couldn’t reach the room directory. Check your connection, then retry.");
                    return;
                }
                SetRoomDirectoryLobby(lobby);

                var myRoom = await lobby.GetMyRoomAsync();
                if (!IsCurrent(operation, ct))
                    return;
                if (myRoom != null)
                {
                    AdoptRoom(chat, myRoom);
                    FrontendController.Show(FrontendPage.LobbyRoom);
                    return;
                }
                SetBrowserStatus("Scanning public rooms…", loading: true);
                var rooms = await lobby.GetRoomsAsync();
                if (!IsCurrent(operation, ct))
                    return;
                _context.ForwardButton = null;
                _serverList?.Clear();
                if (rooms == null || rooms.Length == 0)
                {
                    _pendingReturnNotice = null;
                    SetBrowserStatus("No public rooms right now. Create a room or retry the scan.", loading: false);
                    return;
                }

                _pendingReturnNotice = null;
                if (_lblStatus != null)
                {
                    _lblStatus.style.display = DisplayStyle.None;
                    _lblStatus.RemoveFromClassList("error");
                }
                foreach (var room in rooms)
                    _serverList?.Add(CreateRoomRow(room));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Leaving the screen or starting a newer scan is an expected cancellation.
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerBrowser] Refresh failed: {ex.Message}");
                if (IsCurrent(operation, ct))
                    ShowBrowserFailure("Couldn’t load public rooms. Check your connection, then retry.");
            }
            finally
            {
                bool refreshAgain = operation == _operationVersion && _roomListDirty &&
                    _alive && !_joining;
                if (operation == _operationVersion)
                    _refreshing = false;
                if (refreshAgain)
                    RefreshServers();
            }

        }

        private static bool IsDevelopmentLegacyRouteEnabled() =>
            Application.isEditor &&
            string.Equals(Environment.GetEnvironmentVariable("SLOPARENA_DEV_UDP"), "1", StringComparison.Ordinal);

        private static bool SupportsMatchTransport(ServerInfo server)
        {
            if (server.ProtocolVersion == SteamMatchDescriptor.CurrentProtocolVersion &&
                !string.IsNullOrEmpty(server.ServerSteamId) &&
                ulong.TryParse(server.ServerSteamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId) &&
                steamId != 0)
                return true;
            return Application.isEditor &&
                string.Equals(Environment.GetEnvironmentVariable("SLOPARENA_DEV_UDP"), "1", StringComparison.Ordinal) &&
                server.ProtocolVersion == 0;
        }

        private VisualElement CreateRoomRow(RoomSummary room)
        {
            var row = new VisualElement { name = "room-row" };
            row.AddToClassList("server-row");
            row.AddToClassList("room-row");
            row.Add(new Label(room.Name) { name = "room-name", enableRichText = false });
            row.Add(new Label($"{room.Phase}  —  {room.MemberCount}/{room.Capacity} players") { name = "room-info" });
            row.Add(new Label($"ID {room.Id}") { name = "room-id" });
            var join = new Button(() => JoinRoom(room))
            {
                text = room.Joinable ? "JOIN" : (room.MemberCount >= room.Capacity ? "FULL" : "UNAVAILABLE"),
                name = "btn-join-room"
            };
            join.AddToClassList("server-join");
            join.SetEnabled(room.Joinable);
            row.Add(join);
            join.RegisterCallback<FocusInEvent>(_ => _context.ForwardButton = join);
            return row;
        }

        private async void JoinRoom(RoomSummary room)
        {
            if (!_alive || _joining || _hostStarting || !room.Joinable)
                return;
            var request = BeginRoomAction();
            if (request == null) return;
            SetBrowserStatus($"Joining {room.Name}…", loading: true);
            try
            {
                var chat = ChatSession.Instance;
                bool connected = chat != null && await chat.EnsureConnectedAsync();
                if (!IsCurrentRoomAction(request)) return;
                var lobby = chat?.ActiveLobby;
                if (!connected || lobby == null)
                    throw new InvalidOperationException(chat?.NeedsDisplayName == true
                        ? "Choose a display name before joining a room."
                        : "Couldn’t connect to the room directory. Retry when online.");

                var currentRoom = await lobby.GetMyRoomAsync();
                if (!IsCurrentRoomAction(request)) return;
                RoomMembershipResult? joinOperation = null;
                RoomSnapshot joined;
                if (currentRoom?.Id == room.Id)
                {
                    joined = currentRoom!;
                }
                else
                {
                    joinOperation = await lobby.JoinRoomAsync(room.Id, request.Token);
                    joined = joinOperation.Value.Room;
                }
                if (!IsCurrentRoomAction(request))
                {
                    if (joinOperation is RoomMembershipResult stale)
                        await LeaveStaleRoomAsync(lobby, stale);
                    return;
                }

                AdoptRoom(chat!, joined);
                FrontendController.Show(FrontendPage.LobbyRoom);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerBrowser] Room join failed: {ex.Message}");
                if (IsCurrentRoomAction(request))
                    ShowBrowserFailure(DescribeRoomError(ex, "Couldn’t join this room. Refresh the list and retry."));
            }
            finally
            {
                FinishRoomAction(request);
            }
        }

        private async void CreateRoom()
        {
            if (!_alive || _joining || _hostStarting)
                return;
            string name = _roomNameField?.value?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                ShowBrowserFailure("Enter a room name before creating it.");
                _roomNameField?.Focus();
                return;
            }
            var request = BeginRoomAction();
            if (request == null) return;
            SetBrowserStatus("Creating your room…", loading: true);
            try
            {
                var chat = ChatSession.Instance;
                bool connected = chat != null && await chat.EnsureConnectedAsync();
                if (!IsCurrentRoomAction(request)) return;
                var lobby = chat?.ActiveLobby;
                if (!connected || lobby == null)
                    throw new InvalidOperationException(chat?.NeedsDisplayName == true
                        ? "Choose a display name before creating a room."
                        : "Couldn’t connect to the room directory. Retry when online.");

                var created = await lobby.CreateRoomAsync(name, request.Token);
                var room = created.Room;
                if (!IsCurrentRoomAction(request))
                {
                    await LeaveStaleRoomAsync(lobby, created);
                    return;
                }
                AdoptRoom(chat!, room);
                FrontendController.Show(FrontendPage.LobbyRoom);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerBrowser] Room creation failed: {ex.Message}");
                if (IsCurrentRoomAction(request))
                    ShowBrowserFailure(DescribeRoomError(ex, "Couldn’t create the room. Check the name and retry."));
            }
            finally
            {
                FinishRoomAction(request);
            }
        }

        private CancellationTokenSource? BeginRoomAction()
        {
            if (_roomActionCts != null)
            {
                ShowBrowserFailure("A previous room operation is still finishing. Retry shortly.");
                return null;
            }
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
            _operationVersion++;
            _refreshing = false;
            var request = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
            _roomActionCts = request;
            _joining = true;
            SetBrowserActionsEnabled(false);
            return request;
        }

        private bool IsCurrentRoomAction(CancellationTokenSource request) =>
            _alive && ReferenceEquals(_roomActionCts, request) && !request.IsCancellationRequested;

        private void FinishRoomAction(CancellationTokenSource request)
        {
            if (!ReferenceEquals(_roomActionCts, request))
                return;
            _roomActionCts = null;
            _joining = false;
            request.Dispose();
            if (_alive)
            {
                SetBrowserActionsEnabled(true);
                if (_roomListDirty)
                    RefreshServers();
            }
        }

        private static async Task LeaveStaleRoomAsync(LobbyClient lobby, RoomMembershipResult operation)
        {
            try
            {
                await lobby.LeaveRoomIfCurrentAsync(operation.Room.Id,
                    operation.OperationGeneration, operation.MembershipGeneration);
            }
            catch (Exception ex) { Debug.LogWarning($"[ServerBrowser] Stale Room cleanup failed: {ex.Message}"); }
        }

        private void SetRoomDirectoryLobby(LobbyClient? lobby)
        {
            if (ReferenceEquals(_roomDirectoryLobby, lobby))
                return;
            if (_roomDirectoryLobby != null)
                _roomDirectoryLobby.RoomDirectoryChanged -= OnRoomDirectoryChanged;
            _roomDirectoryLobby = lobby;
            if (_alive && _roomDirectoryLobby != null)
                _roomDirectoryLobby.RoomDirectoryChanged += OnRoomDirectoryChanged;
        }

        private void OnActiveLobbyChanged(LobbyClient? lobby) => SetRoomDirectoryLobby(lobby);

        private void OnRoomDirectoryChanged()
        {
            if (!_alive)
                return;
            _roomListDirty = true;
            if (!_refreshing && !_joining)
                RefreshServers();
        }

        private void OnAccountChanged()
        {
            _operationVersion++;
            _refreshing = false;
            _roomListDirty = false;
            SetRoomDirectoryLobby(null);
            _refreshCts?.Cancel();
            _roomActionCts?.Cancel();
            ShowBrowserFailure("Steam account changed. Sign in with the current account before using Rooms.");
        }


        /// <summary>Shared accepted-Room handoff for browser and Steam requests.</summary>
        public static void AdoptRoom(ChatSession chat, RoomSnapshot room)
        {
            ClientSession.AuthToken = chat.AuthToken;
            ClientSession.SteamId = chat.SteamId ?? 0;
            ClientSession.Username = chat.Self?.DisplayName;
            ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.Room;
            ClientSession.SelectedRoomId = room.Id;
            ClientSession.SelectedServerId = Guid.Empty;
            ClientSession.SelectedServerName = room.Name;
            chat.UpdateRoomTitle(room);
        }

        private static string DescribeRoomError(Exception ex, string fallback)
        {
            string message = ex.Message;
            if (message.Contains("room_full", StringComparison.OrdinalIgnoreCase))
                return "That room is full. Refresh the list and choose another room.";
            if (message.Contains("room_limit", StringComparison.OrdinalIgnoreCase))
                return "The room limit has been reached. Try joining an existing room.";
            if (message.Contains("invalid_room_name", StringComparison.OrdinalIgnoreCase))
                return "Room names must contain 1–24 characters. Change the name and retry.";
            if (message.Contains("room_not_found", StringComparison.OrdinalIgnoreCase))
                return "That room no longer exists. Refresh the list and choose another room.";
            if (message.Contains("already_in_room", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("lobby", StringComparison.OrdinalIgnoreCase))
                return "Leave your current lobby before joining or creating a Room.";
            if (message.Contains("not_in_room", StringComparison.OrdinalIgnoreCase))
                return "You are no longer in that room. Return to the browser and refresh.";
            return fallback;
        }


        private void JoinServer(ServerInfo server)
        {
            if (!_alive || _joining || _hostStarting)
                return;
            if (!SupportsMatchTransport(server))
            {
                SetBrowserStatus("This room does not support the required Steam match protocol.", loading: false);
                return;
            }
            _joining = true;
            SetBrowserStatus($"Joining {server.Name}…", loading: true);
            SetBrowserActionsEnabled(false);

            var chat = ChatSession.Instance;
            ClientSession.AuthToken = chat?.AuthToken;
            ClientSession.SteamId = chat?.SteamId ?? 0;
            ClientSession.Username = chat?.Self?.DisplayName;
            ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
            ClientSession.SelectedRoomId = Guid.Empty;
            ClientSession.SelectedServerId = server.Id;
            ClientSession.SelectedServerName = server.Name;
            Debug.Log($"[ServerBrowser] Joining server: {server.Name}");
            FrontendController.Show(FrontendPage.LobbyRoom);

        }

        private void OpenDirectConnect()
        {
            if (!_alive || _joining || _hostStarting || _directConnectModal == null ||
                !IsDevelopmentLegacyRouteEnabled())
                return;
            // The shell identity surface owns the topmost modal layer while
            // it is presented; the direct-connect form never stacks over it.
            if (UiModalState.Presented)
                return;
            _focusBeforeModal = _directConnectModal.panel?.focusController?.focusedElement;
            SetPageSectionsEnabled(false);
            _directConnectModal.style.display = DisplayStyle.Flex;
            _modalPresented = true;
            // The modal owns Start; Escape resolves to the page-owned close action.
            _context.SetModalAction(CloseDirectConnect);
            _context.ModalForwardButton = _modalJoin;
            FrontendFocusRouter.NotifyPresentationChanged();
            if (_directConnectStatus != null)
            {
                _directConnectStatus.text = string.Empty;
                _directConnectStatus.RemoveFromClassList("error");
                _directConnectStatus.RemoveFromClassList("success");
            }
            _ipField?.Focus();
        }

        private void OnModalKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Tab || _directConnectModal == null)
                return;

            Focusable[] focusables = { _ipField, _modalClose, _modalJoin };
            int current = Array.IndexOf(focusables, _directConnectModal.panel?.focusController?.focusedElement);
            int direction = evt.shiftKey ? -1 : 1;
            int next = current < 0 ? 0 : (current + direction + focusables.Length) % focusables.Length;
            focusables[next]?.Focus();
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        private void CloseDirectConnect()
        {
            if (_directConnectModal == null)
            {
                return;
            }
            if (_addressCts != null)
            {
                _addressCts.Cancel();
                _addressCts.Dispose();
                _addressCts = null;
                _joining = false;
                _modalJoin?.SetEnabled(true);
            }
            _directConnectModal.style.display = DisplayStyle.None;
            _context.SetModalAction(null);
            _context.ModalForwardButton = null;
            if (_modalPresented)
            {
                _modalPresented = false;
                UiModalState.Pop();
            }
            SetPageSectionsEnabled(true);
            FrontendFocusRouter.Instance?.NotifyModalClosed();
            if (_focusBeforeModal is VisualElement previous && previous.panel != null)
                previous.Focus();
            else
                (_btnDirectConnect ?? _btnRefresh)?.Focus();
        }

        /// <summary>
        /// Disables the page's owned sections (except the page-modal section
        /// that hosts the direct-connect form) while the form is presented,
        /// so nothing behind the modal backdrop is clickable or focusable —
        /// the legacy page-region disable, now over the shell hosts (issue
        /// #221).
        /// </summary>
        private void SetPageSectionsEnabled(bool enabled)
        {
            foreach (var section in _context.OwnedRoots)
            {
                if (section == _directConnectModal
                    || (_directConnectModal != null && section.Contains(_directConnectModal)))
                    continue;
                section.SetEnabled(enabled);
            }
        }

        private async void JoinDirectConnect()
        {
            if (!_alive || _joining || _ipField == null || !IsDevelopmentLegacyRouteEnabled())
                return;
            if (!TryParseServerAddress(_ipField.value, out var ip, out var port, out var error))
            {
                SetAddressStatus(error, true);
                _ipField.Focus();
                return;
            }

            _refreshCts?.Cancel();
            _operationVersion++;
            _refreshing = false;
            _joining = true;
            _addressCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
            var request = _addressCts;
            var ct = request.Token;
            _modalJoin?.SetEnabled(false);
            SetAddressStatus("Finding the registered server…", false);
            try
            {
                var chat = ChatSession.Instance;
                if (chat == null || !await chat.EnsureConnectedAsync())
                {
                    if (_alive && !ct.IsCancellationRequested)
                        SetAddressStatus(chat?.NeedsDisplayName == true
                            ? "Choose a display name before browsing rooms."
                            : "Couldn’t reach the room directory. Check your connection, then retry.", true);
                    return;
                }
                _masterClient = chat.MasterClient;
                if (_masterClient == null)
                {
                    SetAddressStatus("Couldn’t reach the room directory. Check your connection, then retry.", true);
                    return;
                }
                var address = IPAddress.Parse(ip);
                if (!_alive || ct.IsCancellationRequested) return;
                var servers = await _masterClient.GetServersAsync(ct);
                if (!_alive || ct.IsCancellationRequested) return;
                var server = servers?.Find(candidate => candidate.Port == port &&
                    IPAddress.TryParse(candidate.IpAddress, out var candidateIp) &&
                    candidateIp.Equals(address));
                if (server == null)
                {
                    SetAddressStatus(servers == null
                        ? "Couldn’t load the room directory. Retry when you’re online."
                        : "No registered server at that address. Check the address with the host, then retry.", true);
                    return;
                }
                _joining = false;
                JoinServer(server);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerBrowser] Address lookup failed: {ex.Message}");
                if (_alive && !ct.IsCancellationRequested)
                    SetAddressStatus("Couldn’t find the server. Check your connection, then retry.", true);
            }
            finally
            {
                if (ReferenceEquals(_addressCts, request))
                {
                    _addressCts = null;
                    request.Dispose();
                    _joining = false;
                    _modalJoin?.SetEnabled(true);
                }
            }
        }

        private void SetAddressStatus(string text, bool error)
        {
            if (_directConnectStatus == null) return;
            _directConnectStatus.text = text;
            _directConnectStatus.EnableInClassList("error", error);
        }

        private void OnHostClicked()
        {
            if (!_alive || _hostStarting || _joining || !IsDevelopmentLegacyRouteEnabled())
                return;
            _hostStarting = true;
            _hostHandedOff = false;
            _hostCts?.Cancel();
            _hostCts?.Dispose();
            _hostCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCts.Token);
            SetHostBusy(true, "Preparing your room…");
            StartHostingFlow(_hostCts.Token);
        }

        private async void StartHostingFlow(CancellationToken ct)
        {
            var host = ServerHost.Create();
            _ownedHost = host;
            string? authToken = null;
            long steamId = 0;
            try
            {
                var chat = ChatSession.Instance;
                ChatSession.ConfigureMasterServerUrl(ClientSession.MasterServerUrl);
                SetHostStatus("Signing in to the room directory…", ct);
                bool authenticated = chat != null && await chat.EnsureConnectedAsync();
                if (!IsCurrentHost(ct))
                    return;
                if (!authenticated || chat?.AuthToken == null)
                {
                    FinishHostFailure(chat?.NeedsDisplayName == true
                        ? "Choose a display name before hosting a room."
                        : "Couldn’t sign in to the room directory. Retry when you’re online.", ct);
                    return;
                }
                authToken = chat.AuthToken;
                steamId = chat.SteamId ?? 0;

                if (!IsCurrentHost(ct))
                    return;
                var registered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Guid serverId = Guid.Empty;
                string registrationError = string.Empty;
                int? crashCode = null;
                string crashStderr = string.Empty;
                Action<Guid> onRegistered = id => { serverId = id; registered.TrySetResult(true); };
                Action<string> onRegistrationFailed = reason => { registrationError = reason ?? string.Empty; registered.TrySetResult(false); };
                Action<int, string> onCrashed = (code, stderr) => { crashCode = code; crashStderr = stderr ?? string.Empty; registered.TrySetResult(false); };
                host.Registered += onRegistered;
                host.RegistrationFailed += onRegistrationFailed;
                host.Crashed += onCrashed;
                try
                {
                    string publicIp = _hostIpField?.value?.Trim() ?? string.Empty;
                    host.StartHosting(GenerateServerName(), string.IsNullOrEmpty(publicIp) ? null : publicIp);
                    SetHostStatus("Waiting for your room to appear…", ct);
                    var winner = await Task.WhenAny(registered.Task, Task.Delay(HostRegistrationTimeoutMs, ct));
                    if (!IsCurrentHost(ct))
                        return;
                    if (winner != registered.Task)
                    {
                        FinishHostFailure("Your room did not appear in time. Check your connection, then retry.", ct);
                        return;
                    }
                    if (serverId == Guid.Empty)
                    {
                        string detail = crashCode.HasValue
                            ? $"The server stopped (code {crashCode.Value}). {Truncate(crashStderr)}"
                            : $"The room directory rejected the server. {Truncate(registrationError)}";
                        FinishHostFailure(detail, ct);
                        return;
                    }

                    if (!IsCurrentHost(ct))
                        return;
                    // The persistent ServerHost is now owned by LobbyRoomUI. Do not stop it in OnDisable.
                    _hostHandedOff = true;
                    _hostStarting = false;
                    _ownedHost = null;
                    MatchConfig.Mode = GameMode.PvP;
                    MatchConfig.IsHost = true;
                    MatchConfig.ServerIP = "127.0.0.1";
                    MatchConfig.ServerPort = host.AssignedPort;
                    ClientSession.AuthToken = authToken;
                    ClientSession.SteamId = steamId;
                    ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
                    ClientSession.SelectedRoomId = Guid.Empty;
                    ClientSession.SelectedServerId = serverId;
                    ClientSession.SelectedServerName = GenerateServerName();
                    // Host ownership transfer: the persistent ServerHost is now
                    // owned by the Lobby Room page (ADR-0005, ADR-0032). Page
                    // deactivation must not stop the handed-off process.
                    FrontendController.Show(FrontendPage.LobbyRoom);
                }
                finally
                {
                    host.Registered -= onRegistered;
                    host.RegistrationFailed -= onRegistrationFailed;
                    host.Crashed -= onCrashed;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // CancelHost/OnDisable performs the synchronous process cleanup.
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerBrowser] Host flow failed: {ex.Message}");
                if (IsCurrentHost(ct))
                    FinishHostFailure("Couldn’t start your room. Check your connection, then retry.", ct);
            }
            finally
            {
                if (IsCurrentHost(ct) && !_hostHandedOff && _ownedHost != null)
                {
                    _ownedHost.Stop();
                    _ownedHost = null;
                }
            }
        }

        private void CancelHost()
        {
            if (!_hostStarting)
                return;
            _hostCts?.Cancel();
            StopOwnedHost();
            _hostStarting = false;
            SetHostBusy(false, "Host setup cancelled. Start a room when you’re ready.");
        }

        private void FinishHostFailure(string message, CancellationToken operationToken)
        {
            if (!IsCurrentHost(operationToken))
                return;
            SetHostBusy(false, message);
            StopOwnedHost();
            _hostStarting = false;
        }

        private void StopOwnedHost()
        {
            if (_hostHandedOff || _ownedHost == null)
                return;
            _ownedHost.Stop();
            _ownedHost = null;
        }

        private void LeaveBrowser()
        {
            if (!_alive)
                return;
            _refreshCts?.Cancel();
            if (_hostStarting)
            {
                _hostCts?.Cancel();
                StopOwnedHost();
                _hostStarting = false;
            }
            FrontendController.Show(FrontendPage.Home);
        }

        private void SetHostBusy(bool busy, string status)
        {
            if (_btnHost != null) _btnHost.SetEnabled(!busy);
            if (_btnHostCancel != null)
            {
                _btnHostCancel.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
                _btnHostCancel.SetEnabled(busy);
            }
            if (_btnDirectConnect != null) _btnDirectConnect.SetEnabled(!busy);
            if (_btnCreateRoom != null) _btnCreateRoom.SetEnabled(!busy);
            if (_lblHostStatus != null)
            {
                _lblHostStatus.style.display = DisplayStyle.Flex;
                _lblHostStatus.text = status;
            }
        }

        private void SetHostStatus(string status, CancellationToken operationToken)
        {
            if (IsCurrentHost(operationToken) && _lblHostStatus != null)
                _lblHostStatus.text = status;
        }

        private void SetBrowserActionsEnabled(bool enabled)
        {
            _btnRefresh?.SetEnabled(enabled);
            _btnHost?.SetEnabled(enabled && !_hostStarting);
            _btnCreateRoom?.SetEnabled(enabled && !_hostStarting);
            _btnDirectConnect?.SetEnabled(enabled && !_hostStarting);
        }

        private void SetBrowserStatus(string text, bool loading)
        {
            if (_lblStatus == null)
                return;
            // A pending return explanation stays visible until the scan
            // settles (issue #213).
            if (loading && _pendingReturnNotice != null)
                return;
            _lblStatus.style.display = DisplayStyle.Flex;
            _lblStatus.text = text;
            if (loading) _lblStatus.RemoveFromClassList("error");
        }

        private void ShowBrowserFailure(string text)
        {
            if (_lblStatus == null)
                return;
            if (_pendingReturnNotice != null)
            {
                text = $"{_pendingReturnNotice}\n{text}";
                _pendingReturnNotice = null;
            }
            _lblStatus.style.display = DisplayStyle.Flex;
            _lblStatus.text = text;
            _lblStatus.AddToClassList("error");
            _btnRefresh?.SetEnabled(true);
        }

        private bool IsCurrent(int operation, CancellationToken ct) =>
            _alive && operation == _operationVersion && !ct.IsCancellationRequested;

        private bool IsCurrentHost(CancellationToken ct) =>
            _alive && _hostStarting && _hostCts != null &&
            _hostCts.Token == ct && !ct.IsCancellationRequested;

        private void OnDisable()
        {
            _operationVersion++;
            _alive = false;
            if (_btnRefresh != null) _btnRefresh.clicked -= RefreshServers;
            if (_btnBack != null) _btnBack.clicked -= LeaveBrowser;
            if (_btnCreateRoom != null) _btnCreateRoom.clicked -= CreateRoom;
            if (_btnHost != null) _btnHost.clicked -= OnHostClicked;
            if (_btnDirectConnect != null) _btnDirectConnect.clicked -= OpenDirectConnect;
            if (_btnHostCancel != null) _btnHostCancel.clicked -= CancelHost;
            if (_modalClose != null) _modalClose.clicked -= CloseDirectConnect;
            if (_modalJoin != null) _modalJoin.clicked -= JoinDirectConnect;
            if (_chatSession != null)
            {
                _chatSession.AccountChanged -= OnAccountChanged;
                _chatSession.ActiveLobbyChanged -= OnActiveLobbyChanged;
            }
            SetRoomDirectoryLobby(null);
            _chatSession = null;
            if (_modalPresented)
            {
                _modalPresented = false;
                UiModalState.Pop();
            }
            _addressCts?.Cancel();
            _addressCts?.Dispose();
            _roomActionCts?.Cancel();
            _addressCts = null;
            _lifecycleCts?.Cancel();
            _refreshCts?.Cancel();
            if (_hostStarting)
            {
                StopOwnedHost();
                _hostStarting = false;
            }
            _masterClient = null;
            // Page re-entry re-runs OnEnable against this same component, so
            // disposed sources must not survive the deactivation cycle
            // (issue #212): a stale disposed CTS would throw on the next
            // Cancel and break Back-during-pending.
            _lifecycleCts?.Dispose();
            _refreshCts?.Dispose();
            _hostCts?.Dispose();
            _lifecycleCts = null;
            _refreshCts = null;
            _hostCts = null;
        }

        private static bool TryParseServerAddress(string raw, out string host, out int port, out string error)
        {
            host = string.Empty;
            port = DefaultServerPort;
            error = "Enter an IPv4 address, with an optional port (for example 192.168.1.20:9876).";
            string value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                error = "Enter the server address before joining.";
                return false;
            }

            if (value[0] == '[')
            {
                int close = value.IndexOf(']');
                if (close <= 1)
                {
                    error = "Use an IPv4 address with an optional port, like 192.168.1.20:9876.";
                    return false;
                }
                host = value.Substring(1, close - 1);
                if (value.Length > close + 1)
                {
                    if (value[close + 1] != ':' || !int.TryParse(value.Substring(close + 2), out port))
                    {
                        error = "The optional port must be a number from 1 to 65535.";
                        return false;
                    }
                }
            }
            else
            {
                int firstColon = value.IndexOf(':');
                int lastColon = value.LastIndexOf(':');
                if (firstColon >= 0 && firstColon == lastColon)
                {
                    string maybePort = value.Substring(lastColon + 1);
                    if (!int.TryParse(maybePort, out port))
                    {
                        error = "The optional port must be a number from 1 to 65535.";
                        return false;
                    }
                    host = value.Substring(0, lastColon);
                }
                else
                {
                    host = value;
                }
            }

            if (port < 1 || port > 65535)
            {
                error = "The port must be between 1 and 65535.";
                return false;
            }
            if (!IPAddress.TryParse(host, out var address))
            {
                error = "Enter a valid IPv4 address. Hostnames are not supported by this client yet.";
                return false;
            }
            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                error = "This client uses IPv4 addresses. Enter an IPv4 address with an optional port.";
                return false;
            }
            return true;
        }

        private static string GenerateServerName() =>
            $"{Environment.MachineName}'s Server";

        private static string Truncate(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : (value.Length > 200 ? value.Substring(0, 200) + "…" : value);
    }
}
