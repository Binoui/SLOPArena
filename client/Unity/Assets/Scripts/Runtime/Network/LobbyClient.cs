#nullable enable
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SlopArena.Shared;

namespace SlopArena.Client.Network
{
    public sealed class RoomMemberInfo
    {
        public long SteamId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsLeader { get; set; }
        public string? CharacterSelection { get; set; }
        public bool LockedIn { get; set; }
        public string Camp { get; set; } = "";
        public byte Colour { get; set; }
    }

    public class RoomSummary
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public long LeaderSteamId { get; set; }
        public int MemberCount { get; set; }
        public int Capacity { get; set; }
        public bool Joinable { get; set; }
    }

    public sealed class RoomSnapshot : RoomSummary
    {
        public Guid? ActiveMatchId { get; set; }
        public bool FriendlyFire { get; set; }
        public bool BallOnly { get; set; }
        public string? ArenaName { get; set; }
        /// <summary>The pick's format: <c>stock</c> or an Arcade format such as <c>slopball-coop</c> (ADR-0040).</summary>
        public string Format { get; set; } = MatchFormats.StockWire;
        public string[] AdmittedArenas { get; set; } = Array.Empty<string>();
        public RoomArcadeEntry[] AdmittedArcade { get; set; } = Array.Empty<RoomArcadeEntry>();
        public string[] AdmittedCharacters { get; set; } = Array.Empty<string>();
        public RoomMemberInfo[] Members { get; set; } = Array.Empty<RoomMemberInfo>();

        public bool AdmitsArcade(string format, string arena)
            => Array.Exists(AdmittedArcade, entry => entry != null &&
                string.Equals(entry.Format, format, StringComparison.Ordinal) &&
                string.Equals(entry.Arena, arena, StringComparison.Ordinal));
    }

    /// <summary>An Arcade format and its arena that Master admits for Rooms.</summary>
    public sealed class RoomArcadeEntry
    {
        public string? Fighter { get; set; }
        public string Format { get; set; } = string.Empty;
        public string Arena { get; set; } = string.Empty;
    }
    public readonly struct RoomMembershipResult
    {
        public RoomSnapshot Room { get; }
        public long OperationGeneration { get; }
        public int MembershipGeneration { get; }

        public RoomMembershipResult(RoomSnapshot room, long operationGeneration, int membershipGeneration)
        {
            Room = room;
            OperationGeneration = operationGeneration;
            MembershipGeneration = membershipGeneration;
        }
    }


    public sealed class MatchAbortedNotification
    {
        public Guid MatchId { get; }
        public string Reason { get; }
        public MatchAbortedNotification(Guid matchId, string reason)
        {
            MatchId = matchId;
            Reason = reason;
        }
    }

    /// <summary>
    /// One authenticated SignalR connection to the master server. It carries
    /// lobby control and all chat channels; callers keep this instance alive
    /// across scene changes and only the persistent ChatSession pumps it.
    /// </summary>
    public sealed class LobbyClient
    {
        private const int MaxPendingActions = 1024;
        private HubConnection? _conn;
        private readonly string _masterServerUrl;
        private readonly Func<string?> _authTokenProvider;
        private readonly int _protocolVersion;
        private Guid _activeMatchId;
        private Guid _joinedServerId;
        private readonly object _roomMembershipSync = new();
        private Guid _joinedRoomId;
        private readonly SemaphoreSlim _roomOperationGate = new(1, 1);
        private long _roomOperationGeneration;

        private volatile bool _roomLeavePending;
        private volatile bool _roomChatResyncPending;
        private volatile bool _roomChatResyncInFlight;
        private int _roomMembershipGeneration;
        private readonly ConcurrentQueue<Action> _pending = new();
        private int _pendingCount;
        private int _overflowed;

        /// <summary>True while the hub connection is open.</summary>
        public bool IsConnected => _conn != null && _conn.State == HubConnectionState.Connected;

        private bool _resumeServerOnly;
        private bool _leavePending;
        /// <summary>Server target retained for reconnect revalidation.</summary>
        public Guid JoinedServerId => _joinedServerId;
        /// <summary>Current Master Room membership for Server Chat.</summary>
        public Guid JoinedRoomId => GetJoinedRoomId();

        /// <summary>True if background pushes exceeded the bounded main-thread queue.</summary>
        public bool HasPendingOverflow => Volatile.Read(ref _overflowed) != 0;
        public void ClearPendingOverflow() => Interlocked.Exchange(ref _overflowed, 0);

        // ── Real-time lobby events (raised on the main thread, after Pump) ──
        public event Action<LobbyPlayerInfo>? PlayerJoined;
        public event Action<long>? PlayerLeft;
        public event Action<LobbySnapshot>? LobbyUpdated;
        public event Action<MatchStartingConfig>? MatchStarting;
        public event Action<MatchStartingConfig>? StageSelect;
        public event Action<LobbyPlayerInfo>? CharacterSelected;
        public event Action<MatchStartedConfig>? MatchStarted;
        public event Action? MatchStartedRejected;
        public event Action<MatchAbortedNotification>? MatchAborted;
        public event Action? Connected;
        public event Action<Exception?>? Disconnected;
        public event Action<string>? Error;
        public event Action<RoomSnapshot>? RoomUpdated;
        public event Action? RoomDirectoryChanged;

        public event Action<Guid>? RoomDeleted;
        public event Action<Guid>? RoomMembershipRevoked;


        // ── Chat pushes (raised on the main thread, after Pump) ──
        public event Action<ChatMessage>? ChatMessageReceived;
        public event Action<ChatPresence>? ChatPresenceChanged;
        public event Action<ServerChatState>? ChatServerChanged;
        public event Action<ChatSnapshot>? ChatStateReceived;
        public event Action<ChatPlayer[]>? OnlinePlayersReceived;

        /// <param name="masterServerUrl">Master server base URL (e.g. http://localhost:5000).</param>
        /// <param name="authToken">Guest JWT to send as bearer auth on the connection.</param>
        /// <param name="protocolVersion">Current gameplay protocol for Steam-authenticated play; 0 for explicit Editor development guests.</param>
        public LobbyClient(string masterServerUrl, string authToken)
            : this(masterServerUrl, () => authToken, SteamMatchDescriptor.CurrentProtocolVersion)
        {
        }

        /// <summary>Construct with a live token provider so renewal updates the hub credential.</summary>
        public LobbyClient(string masterServerUrl, Func<string?> authTokenProvider, int protocolVersion = SteamMatchDescriptor.CurrentProtocolVersion)
        {
            if (protocolVersion != SteamMatchDescriptor.CurrentProtocolVersion &&
                !(protocolVersion == 0 && UnityEngine.Application.isEditor))
                throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            _masterServerUrl = masterServerUrl.TrimEnd('/');
            _authTokenProvider = authTokenProvider ?? throw new ArgumentNullException(nameof(authTokenProvider));
            _protocolVersion = protocolVersion;
        }

        /// <summary>
        /// Build the connection, register handlers, and start it. Returns false
        /// (without throwing) on a connection failure.
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (IsConnected) return true;
            if (_conn?.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting)
                return false;
            if (string.IsNullOrEmpty(_authTokenProvider()))
            {
                Enqueue(() => Error?.Invoke("Not authenticated; cannot connect to the room directory."));
                return false;
            }
            if (_conn == null)
            {
                _conn = new HubConnectionBuilder()
                    // Master refuses Room entry from a build whose protocol differs (ADR-0040).
                    .WithUrl($"{_masterServerUrl}/lobby?protocol={_protocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}", options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_authTokenProvider());
                        // Unity Mono/Proton requires the existing long-poll transport.
                        options.Transports = HttpTransportType.LongPolling;
                    })
                    .WithAutomaticReconnect()
                    .Build();
                RegisterHandlers();
                _conn.Closed += ex =>
                {
                    Enqueue(() => Disconnected?.Invoke(ex));
                    return Task.CompletedTask;
                };
                _conn.Reconnecting += ex =>
                {
                    Enqueue(() => Disconnected?.Invoke(ex));
                    return Task.CompletedTask;
                };
                _conn.Reconnected += async _ =>
                {
                    await RestoreMembershipAsync();
                    Enqueue(() => Connected?.Invoke());
                };
            }
            try
            {
                await _conn.StartAsync();
                await RestoreMembershipAsync();
                Enqueue(() => Connected?.Invoke());
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] SignalR connect failed: {ex.Message}");
                Enqueue(() => Error?.Invoke($"Failed to connect: {ex.Message}"));
                return false;
            }
        }

        private async Task RestoreMembershipAsync()
        {
            if (_leavePending)
            {
                await LeaveLobbyAsync();
                return;
            }
            if (GetJoinedRoomId() != Guid.Empty)
            {
                try
                {
                    await GetMyRoomAsync();
                }
                catch (Exception ex)
                {
                    Enqueue(() => Error?.Invoke($"Room membership could not be revalidated: {ex.Message}"));
                }
                return;
            }
            if (_joinedServerId == Guid.Empty) return;
            try
            {
                await _conn!.InvokeCoreAsync(
                    _resumeServerOnly ? "ResumeServer" : "JoinLobby",
                    new object?[] { _joinedServerId, _protocolVersion });
            }
            catch (Exception ex)
            {
                Enqueue(() => Error?.Invoke($"Server membership could not be revalidated: {ex.Message}"));
            }
        }

        private void RegisterHandlers()
        {
            _conn!.On<JsonElement>("PlayerJoined", element =>
            {
                var player = LobbyPayloadCodec.TryParsePlayer(element);
                if (player is not null) Enqueue(() => PlayerJoined?.Invoke(player));
            });
            _conn.On<long>("PlayerLeft", steamId => Enqueue(() => PlayerLeft?.Invoke(steamId)));
            _conn.On<JsonElement>("LobbyUpdated", element =>
            {
                var snap = LobbyPayloadCodec.TryParseSnapshot(element);
                if (snap is not null) Enqueue(() => LobbyUpdated?.Invoke(snap));
            });
            _conn.On<JsonElement>("RoomUpdated", element =>
            {
                var room = Deserialize<RoomSnapshot>(element);
                if (room != null) Enqueue(() => RoomUpdated?.Invoke(room));
            });
            _conn.On("RoomDirectoryChanged", () =>
                Enqueue(() => RoomDirectoryChanged?.Invoke()));

            _conn.On<JsonElement>("RoomDeleted", element =>
            {
                if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var roomId))
                    Enqueue(() => RoomDeleted?.Invoke(roomId));
            });
            _conn.On<JsonElement>("RoomMembershipRevoked", element =>
            {
                if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var roomId))
                    Enqueue(() =>
                    {
                        if (GetJoinedRoomId() == roomId)
                        {
                            SetJoinedRoomId(Guid.Empty);
                            ChatServerChanged?.Invoke(new ServerChatState());
                            _roomChatResyncPending = false;
                            _roomChatResyncInFlight = false;
                        }
                        RoomMembershipRevoked?.Invoke(roomId);
                    });
            });


            _conn.On<JsonElement>("MatchStarting", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarting(element);
                if (cfg is not null) Enqueue(() => MatchStarting?.Invoke(cfg));
            });
            _conn.On<JsonElement>("StageSelect", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarting(element);
                if (cfg is not null) Enqueue(() => StageSelect?.Invoke(cfg));
            });
            _conn.On<JsonElement>("CharacterSelected", element =>
            {
                var player = LobbyPayloadCodec.TryParsePlayer(element);
                if (player is not null) Enqueue(() => CharacterSelected?.Invoke(player));
            });
            _conn.On<JsonElement>("MatchStarted", element =>
            {
                var cfg = LobbyPayloadCodec.TryParseMatchStarted(element);
                if (cfg == null)
                {
                    Enqueue(() =>
                    {
                        _resumeServerOnly = false;
                        _activeMatchId = Guid.Empty;
                        MatchStartedRejected?.Invoke();
                    });
                    return;
                }
                int roomGeneration = Volatile.Read(ref _roomMembershipGeneration);
                Guid joinedRoomId = GetJoinedRoomId();
                if ((joinedRoomId != Guid.Empty && cfg.RoomId != joinedRoomId) ||
                    (cfg.RoomId is Guid receivedRoomId && receivedRoomId != joinedRoomId))
                    return;
                _resumeServerOnly = true;
                Enqueue(() =>
                {
                    // A Room launch is addressed only to members of that Room.
                    // Ignore a queued push after membership changes, and never
                    // let a Room payload enter the legacy physical-lobby flow.
                    if (roomGeneration != Volatile.Read(ref _roomMembershipGeneration) ||
                        (joinedRoomId != Guid.Empty && cfg.RoomId != joinedRoomId) ||
                        (cfg.RoomId is Guid roomId && roomId != GetJoinedRoomId()))
                        return;
                    _resumeServerOnly = true;
                    _activeMatchId = cfg.MatchId;
                    MatchStarted?.Invoke(cfg);

                });
            });
            _conn.On<JsonElement>("MatchAborted", element =>
            {
                var aborted = TryParseMatchAborted(element);
                if (aborted == null) return;
                Enqueue(() =>
                {
                    if (_activeMatchId != aborted.MatchId)
                        return;
                    _activeMatchId = Guid.Empty;
                    _resumeServerOnly = false;
                    MatchAborted?.Invoke(aborted);
                });
            });
            _conn.On<JsonElement>("ChatMessage", element =>
            {
                var message = Deserialize<ChatMessage>(element);
                if (message is not null) Enqueue(() =>
                {
                    if (string.Equals(message.Channel, "server", StringComparison.OrdinalIgnoreCase)
                        && (_roomLeavePending || _roomChatResyncPending ||
                            message.RoomId != GetJoinedRoomId()))
                        return;
                    ChatMessageReceived?.Invoke(message);
                });
            });
            _conn.On<JsonElement>("ChatPresenceChanged", element =>
            {
                var presence = Deserialize<ChatPresence>(element);
                if (presence is not null) Enqueue(() => ChatPresenceChanged?.Invoke(presence));
            });
            _conn.On<JsonElement>("ChatServerChanged", element =>
            {
                var state = Deserialize<ServerChatState>(element);
                if (state is null) return;
                int membershipGeneration = Volatile.Read(ref _roomMembershipGeneration);
                Enqueue(() => ApplyServerChatState(state, membershipGeneration));
            });
        }

        private void ApplyServerChatState(ServerChatState state, int membershipGeneration)
        {
            if (_roomLeavePending) return;
            Guid currentRoomId = GetJoinedRoomId();
            if (state.RoomId is not Guid roomId)
            {
                if (membershipGeneration != Volatile.Read(ref _roomMembershipGeneration))
                    return;
                if (currentRoomId != Guid.Empty)
                {
                    StartRoomChatResync();
                    return;
                }
                _roomChatResyncPending = false;
                _roomChatResyncInFlight = false;
                ChatServerChanged?.Invoke(state);
                return;
            }
            if (_roomChatResyncPending)
            {
                StartRoomChatResync();
                return;
            }
            if (currentRoomId == roomId)
            {
                ChatServerChanged?.Invoke(state);
                return;
            }
            if (membershipGeneration != Volatile.Read(ref _roomMembershipGeneration))
                return;
            StartRoomChatResync();
        }

        private void StartRoomChatResync()
        {
            _roomChatResyncPending = true;
            if (_roomChatResyncInFlight)
                return;
            _roomChatResyncInFlight = true;
            _ = ResyncRoomChatStateAsync();
        }

        private async Task ResyncRoomChatStateAsync()
        {
            bool authoritativeState = false;
            try
            {
                var room = await GetMyRoomAsync();
                if (_roomLeavePending)
                    return;
                Guid currentRoomId = GetJoinedRoomId();
                if (currentRoomId == Guid.Empty)
                {
                    authoritativeState = room == null;
                    return;
                }
                var snapshot = await GetChatStateAsync();
                if (_roomLeavePending || snapshot?.Server?.RoomId is not Guid roomId ||
                    roomId == Guid.Empty || GetJoinedRoomId() != roomId)
                    return;
                ServerChatState state = snapshot.Server;
                Enqueue(() =>
                {
                    if (!_roomLeavePending && GetJoinedRoomId() == roomId)
                        ChatServerChanged?.Invoke(state);
                });
                authoritativeState = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] Room chat resync failed: {ex.Message}");
            }
            finally
            {
                _roomChatResyncInFlight = false;
                if (authoritativeState)
                    _roomChatResyncPending = false;
            }
        }

        public Task<RoomSummary[]> GetRoomsAsync() => InvokeRoomAsync<RoomSummary[]>("GetRooms");

        public async Task<RoomSnapshot?> GetMyRoomAsync()
        {
            await _roomOperationGate.WaitAsync();
            try
            {
                int membershipGeneration = Volatile.Read(ref _roomMembershipGeneration);
                Guid previousRoomId = GetJoinedRoomId();
                var room = await InvokeRoomAsync<RoomSnapshot?>("GetMyRoom");
                if (!TrySetJoinedRoomId(room?.Id ?? Guid.Empty, membershipGeneration) &&
                    GetJoinedRoomId() != (room?.Id ?? Guid.Empty))
                    return room;
                Interlocked.Increment(ref _roomOperationGeneration);
                if (room == null)
                {
                    int revokedGeneration = Volatile.Read(ref _roomMembershipGeneration);
                    Enqueue(() =>
                    {
                        if (revokedGeneration != Volatile.Read(ref _roomMembershipGeneration) ||
                            GetJoinedRoomId() != Guid.Empty)
                            return;
                        ChatServerChanged?.Invoke(new ServerChatState());
                        if (!_roomChatResyncInFlight)
                            _roomChatResyncPending = false;
                        if (previousRoomId != Guid.Empty)
                            RoomMembershipRevoked?.Invoke(previousRoomId);
                    });
                }
                else
                {
                    Enqueue(() => RoomUpdated?.Invoke(room));
                }
                return room;
            }
            finally
            {
                _roomOperationGate.Release();
            }
        }

        public async Task<RoomMembershipResult> CreateRoomAsync(
            string name, CancellationToken cancellationToken = default)
        {
            await _roomOperationGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_joinedServerId != Guid.Empty)
                    throw new InvalidOperationException("Leave the physical server lobby before creating a room.");
                int membershipGeneration = Volatile.Read(ref _roomMembershipGeneration);
                var room = await InvokeRoomAsync<RoomSnapshot>("CreateRoom", name);
                TrySetJoinedRoomId(room.Id, membershipGeneration);
                long operationGeneration = Interlocked.Increment(ref _roomOperationGeneration);
                return new RoomMembershipResult(room, operationGeneration,
                    Volatile.Read(ref _roomMembershipGeneration));
            }
            finally
            {
                _roomOperationGate.Release();
            }
        }

        public async Task<RoomMembershipResult> JoinRoomAsync(
            Guid roomId, CancellationToken cancellationToken = default)
        {
            await _roomOperationGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_joinedServerId != Guid.Empty)
                    throw new InvalidOperationException("Leave the physical server lobby before joining a room.");
                int membershipGeneration = Volatile.Read(ref _roomMembershipGeneration);
                var room = await InvokeRoomAsync<RoomSnapshot>("JoinRoom", roomId);
                TrySetJoinedRoomId(room.Id, membershipGeneration);
                long operationGeneration = Interlocked.Increment(ref _roomOperationGeneration);
                return new RoomMembershipResult(room, operationGeneration,
                    Volatile.Read(ref _roomMembershipGeneration));
            }
            finally
            {
                _roomOperationGate.Release();
            }
        }

        public async Task LeaveRoomAsync()
        {
            await _roomOperationGate.WaitAsync();
            try
            {
                await LeaveRoomCoreAsync(Volatile.Read(ref _roomMembershipGeneration));
            }
            finally
            {
                _roomOperationGate.Release();
            }
        }

        public async Task<bool> LeaveRoomIfCurrentAsync(
            Guid roomId, long operationGeneration, int membershipGeneration)
        {
            await _roomOperationGate.WaitAsync();
            try
            {
                if (roomId == Guid.Empty ||
                    Volatile.Read(ref _roomOperationGeneration) != operationGeneration ||
                    Volatile.Read(ref _roomMembershipGeneration) != membershipGeneration ||
                    GetJoinedRoomId() != roomId)
                    return false;
                await LeaveRoomCoreAsync(membershipGeneration);
                return true;
            }
            finally
            {
                _roomOperationGate.Release();
            }
        }

        private async Task LeaveRoomCoreAsync(int membershipGeneration)
        {
            if (_conn is null || !IsConnected)
                throw new InvalidOperationException("Not connected to the room directory. Retry when online.");
            _roomLeavePending = true;
            try
            {
                await _conn.InvokeCoreAsync("LeaveRoom", Array.Empty<object?>());
                if (TrySetJoinedRoomId(Guid.Empty, membershipGeneration))
                {
                    Interlocked.Increment(ref _roomOperationGeneration);
                    int clearedGeneration = Volatile.Read(ref _roomMembershipGeneration);
                    Enqueue(() =>
                    {
                        if (clearedGeneration != Volatile.Read(ref _roomMembershipGeneration) ||
                            GetJoinedRoomId() != Guid.Empty)
                            return;
                        ChatServerChanged?.Invoke(new ServerChatState());
                        _roomChatResyncPending = false;
                        _roomChatResyncInFlight = false;
                    });
                }
            }
            finally
            {
                _roomLeavePending = false;
            }
        }

        private Guid GetJoinedRoomId()
        {
            lock (_roomMembershipSync)
                return _joinedRoomId;
        }

        private void SetJoinedRoomId(Guid roomId)
        {
            lock (_roomMembershipSync)
            {
                if (_joinedRoomId == roomId)
                    return;
                _joinedRoomId = roomId;
                Interlocked.Increment(ref _roomMembershipGeneration);
                Interlocked.Increment(ref _roomOperationGeneration);
            }
        }

        private bool TrySetJoinedRoomId(Guid roomId, int expectedGeneration)
        {
            lock (_roomMembershipSync)
            {
                if (_roomMembershipGeneration != expectedGeneration)
                    return false;
                if (_joinedRoomId != roomId)
                {
                    _joinedRoomId = roomId;
                    Interlocked.Increment(ref _roomMembershipGeneration);
                    Interlocked.Increment(ref _roomOperationGeneration);
                }
                return true;
            }
        }


        private async Task<T> InvokeRoomAsync<T>(string method, params object?[] args)
        {
            if (_conn is null || !IsConnected)
                throw new InvalidOperationException("Not connected to the room directory. Retry when online.");
            try
            {
                return await _conn.InvokeCoreAsync<T>(method, args);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] {method} rejected: {ex.Message}");
                throw;
            }
        }

        /// <summary>Join the lobby for the given game server.</summary>
        public async Task JoinLobbyAsync(Guid serverId)
        {
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke("Not connected; cannot JoinLobby."));
                return;
            }
            if (_leavePending)
            {
                await LeaveLobbyAsync();
                if (_leavePending) return;
            }
            var previousServerId = _joinedServerId;
            bool previousResumeOnly = _resumeServerOnly;
            // An interrupted reply is not a rejected admission. Revalidate it
            // as chat-only on reconnect rather than allocating a waiting slot.
            _joinedServerId = serverId;
            _resumeServerOnly = true;
            try
            {
                await _conn.InvokeCoreAsync("JoinLobby", new object?[] { serverId, _protocolVersion });
                _resumeServerOnly = false;
            }
            catch (Exception ex)
            {
                if (ex is HubException && !ex.Message.Contains("lobby_full"))
                {
                    _joinedServerId = previousServerId;
                    _resumeServerOnly = previousResumeOnly;
                }
                UnityEngine.Debug.LogWarning($"[LobbyClient] JoinLobby rejected: {ex.Message}");
                Enqueue(() => Error?.Invoke($"JoinLobby rejected: {ex.Message}"));
            }
        }
        /// <summary>
        /// Revalidate an already admitted GameServer after reconnect without
        /// re-entering its waiting roster.
        /// </summary>
        public async Task ResumeServerAsync(Guid serverId)
        {
            if (_leavePending) return;
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke("Not connected; cannot ResumeServer."));
                return;
            }
            try
            {
                await _conn.InvokeCoreAsync("ResumeServer", new object?[] { serverId, _protocolVersion });
                _joinedServerId = serverId;
                _resumeServerOnly = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] ResumeServer rejected: {ex.Message}");
                Enqueue(() => Error?.Invoke($"ResumeServer rejected: {ex.Message}"));
            }
        }

        /// <summary>Leave the current physical lobby; it does not grant Server Chat.</summary>
        public async Task LeaveLobbyAsync()
        {
            _leavePending = true;
            _joinedServerId = Guid.Empty;
            _resumeServerOnly = false;
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke("Server leave will complete after reconnect."));
                return;
            }
            try
            {
                await _conn.InvokeCoreAsync("LeaveLobby", Array.Empty<object?>());
                _leavePending = false;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] LeaveLobby not confirmed: {ex.Message}");
                Enqueue(() => Error?.Invoke($"LeaveLobby rejected: {ex.Message}"));
            }
        }

        public Task HostStartAsync() => InvokeSafe("HostStart");
        public Task<RoomSnapshot> RoomStartCharacterSelectAsync() =>
            InvokeRoomAsync<RoomSnapshot>("RoomStartCharacterSelect");
        public Task<RoomSnapshot> RoomSelectCharacterAsync(string character) =>
            InvokeRoomAsync<RoomSnapshot>("RoomSelectCharacter", character);
        public Task<RoomSnapshot> RoomStartStageSelectAsync() =>
            InvokeRoomAsync<RoomSnapshot>("RoomStartStageSelect");
        public Task<RoomSnapshot> RoomChooseArenaAsync(string arena) =>
            InvokeRoomAsync<RoomSnapshot>("RoomChooseArena", arena);
        /// <summary>The leader's pick: <c>stock</c> with a PvP arena, or an admitted Arcade format and its arena.</summary>
        public Task<RoomSnapshot> RoomChoosePickAsync(string format, string arena) =>
            InvokeRoomAsync<RoomSnapshot>("RoomChoosePick", format, arena);
        public Task<RoomSnapshot> RoomSetFriendlyFireAsync(bool enabled) =>
            InvokeRoomAsync<RoomSnapshot>("RoomSetFriendlyFire", enabled);
        public Task<RoomSnapshot> RoomSetBallOnlyAsync(bool enabled) =>
            InvokeRoomAsync<RoomSnapshot>("RoomSetBallOnly", enabled);
        public Task<RoomSnapshot> RoomSelectColourAsync(byte colour) =>
            InvokeRoomAsync<RoomSnapshot>("RoomSelectColour", colour);

        public Task<RoomSnapshot> RoomSelectCampAsync(string camp) =>
            InvokeRoomAsync<RoomSnapshot>("RoomSelectCamp", camp);
        public Task RoomStartMatchAsync() =>
            InvokeRoomAsync<JsonElement>("RoomStartMatch");
        public Task RoomReturnToLobbyAsync(Guid matchId) =>
            InvokeRoomAsync<object?>("RoomReturnToLobby", matchId);
        public Task SelectCharacterAsync(string characterClass) => InvokeSafe("SelectCharacter", characterClass);
        public Task StartStageSelectAsync() => InvokeSafe("StartStageSelect");
        public Task StartMatchAsync(string arenaName) => InvokeSafe("StartMatch", arenaName);

        public async Task<ChatSnapshot?> GetChatStateAsync()
        {
            int membershipGeneration = Volatile.Read(ref _roomMembershipGeneration);
            var snapshot = await InvokeChatAsync<ChatSnapshot>("GetChatState");
            if (snapshot == null)
                return null;
            snapshot.Server ??= new ServerChatState();
            if (_roomLeavePending || membershipGeneration != Volatile.Read(ref _roomMembershipGeneration))
            {
                Guid preservedRoomId = GetJoinedRoomId();
                snapshot.Server = new ServerChatState
                {
                    RoomId = preservedRoomId == Guid.Empty ? (Guid?)null : preservedRoomId
                };
                return snapshot;
            }
            bool authoritativeRoomState = false;
            Guid returnedRoomId = snapshot.Server.RoomId ?? Guid.Empty;
            Guid currentRoomId = GetJoinedRoomId();
            if (currentRoomId == Guid.Empty)
            {
                if (TrySetJoinedRoomId(returnedRoomId, membershipGeneration))
                    authoritativeRoomState = true;
                else
                {
                    currentRoomId = GetJoinedRoomId();
                    snapshot.Server = new ServerChatState
                    {
                        RoomId = currentRoomId == Guid.Empty ? (Guid?)null : currentRoomId
                    };
                }
            }
            else if (returnedRoomId == Guid.Empty)
            {
                if (TrySetJoinedRoomId(Guid.Empty, membershipGeneration))
                    authoritativeRoomState = true;
                else
                {
                    currentRoomId = GetJoinedRoomId();
                    snapshot.Server = new ServerChatState
                    {
                        RoomId = currentRoomId == Guid.Empty ? (Guid?)null : currentRoomId
                    };
                }
            }
            else if (returnedRoomId != currentRoomId)
            {
                if (TrySetJoinedRoomId(returnedRoomId, membershipGeneration))
                    authoritativeRoomState = true;
                else
                {
                    currentRoomId = GetJoinedRoomId();
                    snapshot.Server = new ServerChatState
                    {
                        RoomId = currentRoomId == Guid.Empty ? (Guid?)null : currentRoomId
                    };
                }
            }
            else
            {
                authoritativeRoomState = true;
            }
            if (authoritativeRoomState && !_roomChatResyncInFlight)
                _roomChatResyncPending = false;
            return snapshot;
        }
        public Task<ChatPlayer[]?> GetOnlinePlayersAsync() => InvokeChatAsync<ChatPlayer[]>("GetOnlinePlayers");
        public Task<ChatMessage?> SendGlobalAsync(string text) => SendChatAsync("SendGlobal", text);
        public Task<ChatMessage?> SendServerAsync(Guid roomId, string text) =>
            _roomLeavePending || _roomChatResyncPending || roomId != GetJoinedRoomId()
                ? Task.FromException<ChatMessage?>(new InvalidOperationException("not_in_room"))
                : SendChatAsync("SendServer", roomId, text);
        public Task<ChatMessage?> SendDirectAsync(string playerId, string text) =>
            SendChatAsync("SendDirect", playerId, text);


        private Task<ChatMessage?> SendChatAsync(string method, params object?[] args)
        {
            if (_conn is null || !IsConnected)
                return Task.FromException<ChatMessage?>(new InvalidOperationException("not_connected"));
            return _conn.InvokeCoreAsync<ChatMessage?>(method, args);
        }

        private async Task InvokeSafe(string method, params object?[] args)
        {
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke($"Not connected; cannot {method}."));
                return;
            }
            try
            {
                await _conn.InvokeCoreAsync(method, args);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[LobbyClient] {method} rejected: {ex}");
                Enqueue(() => Error?.Invoke($"{method} rejected: {ex.Message}"));
            }
        }

        private async Task<T?> InvokeChatAsync<T>(string method, params object?[] args)
        {
            if (_conn is null || !IsConnected)
            {
                Enqueue(() => Error?.Invoke($"not_connected: cannot {method}."));
                return default;
            }
            try
            {
                return await _conn.InvokeCoreAsync<T>(method, args);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyClient] {method} rejected: {ex.Message}");
                Enqueue(() => Error?.Invoke($"{method} rejected: {ex.Message}"));
                return default;
            }
        }

        private static T? Deserialize<T>(JsonElement element)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions);
            }
            catch
            {
                return default;
            }
        }
        private static MatchAbortedNotification? TryParseMatchAborted(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("matchId", out var match) || match.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(match.GetString(), out var matchId) || matchId == Guid.Empty ||
                !element.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String)
                return null;

            string? value = reason.GetString();
            return value is "unfilled" or "absent" or "host_restart" or "host_shutdown" or "content_unavailable" or "host_unavailable"
                    or "match_failed"
                ? new MatchAbortedNotification(matchId, value)
                : null;
        }


        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private void Enqueue(Action action)
        {
            while (true)
            {
                int count = Volatile.Read(ref _pendingCount);
                if (count >= MaxPendingActions)
                {
                    Interlocked.Exchange(ref _overflowed, 1);
                    return;
                }
                if (Interlocked.CompareExchange(ref _pendingCount, count + 1, count) == count)
                    break;
            }
            _pending.Enqueue(action);
        }

        /// <summary>Drain queued hub events onto the calling (main) thread.</summary>
        public void Pump(int maxActions = MaxPendingActions)
        {
            if (maxActions < 1) return;
            int pumped = 0;
            while (pumped++ < maxActions && _pending.TryDequeue(out var action))
            {
                try { action(); }
                finally { Interlocked.Decrement(ref _pendingCount); }
            }
        }

        /// <summary>Stop the connection if open. Safe to call during application shutdown.</summary>
        public async Task DisconnectAsync()
        {
            if (_conn != null)
            {
                try { await _conn.DisposeAsync(); }
                catch { }
                _conn = null;
            }
        }
    }
}
