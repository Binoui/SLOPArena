using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
using SlopArena.Shared;
using Steamworks;

namespace SlopArena.Server;

/// <summary>Owns the process-wide Steam game-server login, listener, and all match connections.</summary>
public static class SteamGameplayInputCodec
{
    /// <summary>Parse an input-bundle frame from the bound entity; <paramref name="inputs"/> needs MaxInputs slots.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, ulong boundEntityId, out uint newestTick,
        Span<InputState> inputs, out int count)
    {
        newestTick = 0;
        count = 0;
        if (boundEntityId == 0 || frame.Length < 1 + InputBundle.HeaderSize || frame[0] != SteamGameplayWire.Input ||
            !InputBundle.TryRead(frame.Slice(1), out ulong entityId, out uint newest, inputs, out int read) ||
            entityId != boundEntityId)
            return false;
        newestTick = newest;
        count = read;
        return true;
    }
}

/// <summary>Routes startup controls from an authenticated, match-bound Steam connection.</summary>
public static class SteamGameplayControlRouter
{
    public static bool TryHandle(ReadOnlySpan<byte> frame, ulong boundEntityId, bool reliable,
        Func<NetplayControlPacket, bool> tryMarkReady)
    {
        if (!reliable || boundEntityId == 0 || frame.Length != 1 + NetplayControlPacket.Size ||
            frame[0] != SteamGameplayWire.Control ||
            !NetplayControlPacket.TryDeserialize(frame.Slice(1), out var control) ||
            control.EntityId != boundEntityId)
            return false;

        // Admission already binds the Steam peer; MatchInstance keeps sending its
        // baseline and clock while waiting. Bootstrap retries must not mark it Ready.
        // A well-formed Ready for an older start tick (sent across the countdown/GO edge)
        // is stale, not spoofed: ignore it and let the peer answer the current Bootstrap.
        switch (control.Kind)
        {
            case NetplayControlKind.Bootstrap:
                return true;
            case NetplayControlKind.Ready:
                tryMarkReady(control);
                return true;
            default:
                return false;
        }
    }
}

public sealed class SteamGameServerHost : IAsyncDisposable
{
    private const int MaxMessageBytes = 1024;
    private const int MessagesPerConnectionPerPoll = 8;
    private const int MaxMessagesPerPoll = 2048;
    private readonly int _maxConnections;
    private readonly int _maxReliableOutbound;
    private readonly int _maxUnreliableOutbound;
    private readonly ConcurrentDictionary<HSteamNetConnection, Connection> _connections = new();
    private readonly ConcurrentDictionary<long, Connection> _connectionsById = new();
    private readonly ConcurrentDictionary<(Guid MatchId, ulong EntityId), Connection> _entityConnections = new();
    private readonly ConcurrentQueue<Outbound> _reliableOutbound = new();
    private readonly ConcurrentQueue<Outbound> _unreliableOutbound = new();
    private readonly ConcurrentQueue<Guid> _closeMatches = new();
    private readonly ConcurrentDictionary<Guid, byte> _closeQueued = new();
    private readonly IntPtr[] _messages = new IntPtr[MessagesPerConnectionPerPoll];
    private readonly byte[] _receiveFrame = new byte[MaxMessageBytes];
    private readonly InputState[] _inputBundle = new InputState[InputBundle.MaxInputs];
    private readonly HashSet<HSteamNetConnection> _pendingFlush = new();
    /// <summary>Wakes the Steam loop as soon as a match has queued a tick's frames.</summary>
    private readonly SemaphoreSlim _outboundSignal = new(0, 1);
    private const int LoopIdleMilliseconds = 2;
    private Callback<SteamServersConnected_t>? _loginSuccess;
    private Callback<SteamServerConnectFailure_t>? _loginFailure;
    private Callback<SteamNetConnectionStatusChangedCallback_t>? _connectionStatus;
    private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private MultiMatchOrchestrator? _orchestrator;
    private long _nextConnectionId;
    private int _reliableOutboundCount;
    private int _unreliableOutboundCount;
    private volatile bool _loggedOn;
    private volatile bool _loginFailed;
    private volatile bool _authenticationReady;
    private bool _initialized;

    private sealed class Connection(HSteamNetConnection handle, long id)
    {
        public HSteamNetConnection Handle { get; } = handle;
        public long Id { get; } = id;
        public ulong RemoteSteamId { get; set; }
        public bool Connected { get; set; }
        public Guid MatchId { get; set; }
        public ulong EntityId { get; set; }
        public bool Bound => MatchId != Guid.Empty && EntityId != 0;
        public long ConnectedAt { get; set; }
        public long DeniedAt { get; set; }
    }

    private readonly record struct Outbound(long ConnectionId, Guid MatchId, ulong EntityId, byte[] Frame);

    public SteamGameServerHost(int maxConcurrentMatches)
    {
        _maxConnections = checked(maxConcurrentMatches * 4 + 32);
        _maxReliableOutbound = checked(maxConcurrentMatches * 4 + 16);
        _maxUnreliableOutbound = checked(maxConcurrentMatches * 4 * 16 * 2);
    }

    public ulong CurrentSteamId
    {
        get
        {
            if (!_loggedOn || !SteamGameServer.BLoggedOn()) return 0;
            return (ulong)SteamGameServer.GetSteamID();
        }
    }

    public bool IsReady => _authenticationReady && CurrentSteamId != 0 && _listenSocket != HSteamListenSocket.Invalid;

    public void AttachOrchestrator(MultiMatchOrchestrator orchestrator) => _orchestrator = orchestrator;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (GameServer.InitEx(0, 0, 0, EServerMode.eServerModeAuthentication, "1.0.0.0", out string initError) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            throw new InvalidOperationException($"Steam game-server initialization failed: {initError}");
        _initialized = true;
        _loginSuccess = Callback<SteamServersConnected_t>.CreateGameServer(_ => _loggedOn = true);
        _loginFailure = Callback<SteamServerConnectFailure_t>.CreateGameServer(_ => _loginFailed = true);
        _connectionStatus = Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(OnConnectionStatusChanged);

        SteamGameServer.SetProduct("sloparena");
        SteamGameServer.SetModDir("sloparena");
        SteamGameServer.SetGameDescription("SLOPArena dedicated server");
        SteamGameServer.SetDedicatedServer(true);
        SteamGameServer.LogOnAnonymous();

        long loginDeadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
        while (!cancellationToken.IsCancellationRequested && !_loggedOn && !_loginFailed && Stopwatch.GetTimestamp() < loginDeadline)
        {
            GameServer.RunCallbacks();
            await Task.Delay(10, cancellationToken);
        }
        if (cancellationToken.IsCancellationRequested || _loginFailed || !_loggedOn || !SteamGameServer.BLoggedOn() || CurrentSteamId == 0)
            throw new InvalidOperationException("Steam game-server login failed or timed out.");
        SteamGameServerNetworkingSockets.InitAuthentication();
        SteamGameServerNetworkingUtils.InitRelayNetworkAccess();

        long authenticationDeadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
        while (!cancellationToken.IsCancellationRequested && Stopwatch.GetTimestamp() < authenticationDeadline)
        {
            GameServer.RunCallbacks();
            bool certificatesCurrent = SteamGameServerNetworkingSockets.GetAuthenticationStatus(out _) ==
                ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current;
            bool relayCurrent = SteamGameServerNetworkingUtils.GetRelayNetworkStatus(out _) ==
                ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current;
            if (certificatesCurrent && relayCurrent)
            {
                _authenticationReady = true;
                break;
            }
            await Task.Delay(10, cancellationToken);
        }
        if (!_authenticationReady)
            throw new InvalidOperationException("Steam certificates or relay network are not ready.");

        _listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketP2P(
            SteamMatchDescriptor.GameplayVirtualPort, 0, Array.Empty<SteamNetworkingConfigValue_t>());
        if (_listenSocket == HSteamListenSocket.Invalid)
            throw new InvalidOperationException("Steam virtual-port-0 listener creation failed.");
        Console.WriteLine($"[Steam] Logged on as {CurrentSteamId.ToString(CultureInfo.InvariantCulture)}; listening on P2P virtual port 0.");

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_lifetime.Token));
    }

    public void SendToMatchEntity(Guid matchId, ulong entityId, byte[] frame, bool reliable)
    {
        if (matchId == Guid.Empty || entityId == 0 || frame.Length is < 2 or > MaxMessageBytes ||
            !_entityConnections.TryGetValue((matchId, entityId), out var connection))
            return;
        var item = new Outbound(connection.Id, matchId, entityId, frame);
        if (reliable)
        {
            if (Interlocked.Increment(ref _reliableOutboundCount) > _maxReliableOutbound)
            {
                Interlocked.Decrement(ref _reliableOutboundCount);
                Console.WriteLine("[Steam] Reliable outbound queue full; dropping completion frame.");
                return;
            }
            _reliableOutbound.Enqueue(item);
        }
        else
        {
            if (Interlocked.Increment(ref _unreliableOutboundCount) > _maxUnreliableOutbound)
            {
                Interlocked.Decrement(ref _unreliableOutboundCount);
                return;
            }
            _unreliableOutbound.Enqueue(item);
        }
    }

    /// <summary>Called by a match after queuing a tick's frames; safe from any thread.</summary>
    public void SignalOutbound()
    {
        if (_outboundSignal.CurrentCount != 0) return;
        try { _outboundSignal.Release(); }
        catch (SemaphoreFullException) { }
    }

    public void CloseMatch(Guid matchId)
    {
        if (matchId != Guid.Empty && _closeQueued.TryAdd(matchId, 0))
            _closeMatches.Enqueue(matchId);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        long nextAuthCheck = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            GameServer.RunCallbacks();
            long now = Stopwatch.GetTimestamp();
            if (now >= nextAuthCheck)
            {
                foreach (var peer in _connections.Values)
                {
                    SteamNetConnectionRealTimeStatus_t health = default;
                    SteamNetConnectionRealTimeLaneStatus_t lane = default;
                    if (peer.Bound && peer.Connected &&
                        SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(peer.Handle, ref health, 0, ref lane) == EResult.k_EResultOK)
                        _orchestrator?.RecordSteamHealth(peer.MatchId, peer.EntityId,
                            health.m_nPing >= 0 ? health.m_nPing : null,
                            health.m_flConnectionQualityLocal, health.m_flConnectionQualityRemote);
                }
                _authenticationReady =
                    SteamGameServerNetworkingSockets.GetAuthenticationStatus(out _) ==
                        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current &&
                    SteamGameServerNetworkingUtils.GetRelayNetworkStatus(out _) ==
                        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current;
                nextAuthCheck = now + Stopwatch.Frequency;
            }
            ProcessMatchClosures();
            CloseExpiredConnections();
            ProcessIncomingMessages();
            ProcessOutbound(_reliableOutbound, reliable: true, ref _reliableOutboundCount);
            ProcessOutbound(_unreliableOutbound, reliable: false, ref _unreliableOutboundCount);
            FlushPendingConnections();
            // Wake immediately when a match queues a tick; otherwise poll inputs every few ms.
            try { await _outboundSignal.WaitAsync(LoopIdleMilliseconds, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t change)
    {
        var handle = change.m_hConn;
        var info = change.m_info;
        if (info.m_eState is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer or
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
        {
            if (_connections.TryGetValue(handle, out var closed))
                EndConnection(closed, "Steam peer closed", closeNative: true);
            return;
        }

        if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
        {
            if (!IsReadyForConnections() || _connections.Count >= _maxConnections)
            {
                SteamGameServerNetworkingSockets.CloseConnection(handle, 0, "GameHost is not accepting connections", false);
                return;
            }
            var accepted = SteamGameServerNetworkingSockets.AcceptConnection(handle);
            if (accepted != EResult.k_EResultOK)
            {
                SteamGameServerNetworkingSockets.CloseConnection(handle, 0, "Steam connection rejected", false);
                return;
            }
            var connection = new Connection(handle, Interlocked.Increment(ref _nextConnectionId)) { ConnectedAt = Stopwatch.GetTimestamp() };
            if (!_connections.TryAdd(handle, connection) || !_connectionsById.TryAdd(connection.Id, connection))
            {
                SteamGameServerNetworkingSockets.CloseConnection(handle, 0, "Steam connection tracking failed", false);
                _connections.TryRemove(handle, out _);
            }
            return;
        }

        if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
        {
            if (!_connections.TryGetValue(handle, out var connection))
            {
                SteamGameServerNetworkingSockets.CloseConnection(handle, 0, "Untracked Steam connection", false);
                return;
            }
            ulong remoteSteamId = info.m_identityRemote.GetSteamID64();
            if (remoteSteamId == 0 || !new CSteamID(remoteSteamId).IsValid())
            {
                EndConnection(connection, "Steam peer identity unavailable", closeNative: true);
                return;
            }
            connection.RemoteSteamId = remoteSteamId;
            connection.Connected = true;
        }
    }

    private bool IsReadyForConnections() => _authenticationReady && _loggedOn &&
        SteamGameServer.BLoggedOn() && _listenSocket != HSteamListenSocket.Invalid;

    private void ProcessIncomingMessages()
    {
        int total = 0;
        foreach (var connection in _connections.Values)
        {
            if (!connection.Connected || connection.DeniedAt != 0) continue;
            int count = SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(connection.Handle, _messages, _messages.Length);
            if (count < 0)
            {
                EndConnection(connection, "Steam receive failed", closeNative: true);
                continue;
            }
            int limit = Math.Min(count, _messages.Length);
            for (int i = 0; i < limit; i++)
            {
                IntPtr messagePointer = _messages[i];
                try
                {
                    if (total >= MaxMessagesPerPoll) continue;
                    total++;
                    var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messagePointer);
                    if (message.m_conn != connection.Handle || message.m_cbSize <= 0 || message.m_cbSize > _receiveFrame.Length)
                        continue;
                    int length = message.m_cbSize;
                    Marshal.Copy(message.m_pData, _receiveFrame, 0, length);
                    bool reliable = (message.m_nFlags & Constants.k_nSteamNetworkingSend_Reliable) != 0;
                    ProcessFrame(connection, _receiveFrame.AsSpan(0, length), reliable);
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(messagePointer);
                }
            }
    }

    }
    private void ProcessFrame(Connection connection, ReadOnlySpan<byte> frame, bool reliable)
    {
        if (!connection.Bound)
        {
            if (frame[0] != SteamGameplayWire.Join || !reliable ||
                !SteamGameplayWire.TryParseJoin(frame, out var matchId, out var contentHash))
            {
                Deny(connection, 1);
                return;
            }
            ulong entityId = 0;
            long replacedConnectionId = 0;
            byte denialCode = 1;
            if (_orchestrator is null || !_orchestrator.TryAdmitSteamPlayer(matchId, connection.RemoteSteamId,
                connection.Id, contentHash, out entityId, out replacedConnectionId, out denialCode))
            {
                Deny(connection, denialCode == 0 ? (byte)1 : denialCode);
                return;
            }

            if (replacedConnectionId != 0 && _connectionsById.TryGetValue(replacedConnectionId, out var replaced))
                EndConnection(replaced, "Steam account reconnected", closeNative: true);
            connection.MatchId = matchId;
            connection.EntityId = entityId;
            _entityConnections[(matchId, entityId)] = connection;
            SendControl(connection.Handle, SteamGameplayWire.CreateAck(entityId), reliable: true);
            _pendingFlush.Add(connection.Handle);
            return;
        }

        if (frame[0] == SteamGameplayWire.Control)
        {
            if (!SteamGameplayControlRouter.TryHandle(frame, connection.EntityId, reliable,
                control => _orchestrator?.TryMarkSteamReady(connection.MatchId, connection.Id, control) == true))
                EndConnection(connection, "Invalid or spoofed Steam control frame", closeNative: true);
            return;
        }

        if (reliable || !SteamGameplayInputCodec.TryParse(frame, connection.EntityId, out uint newestTick,
                _inputBundle, out int count))
        {
            EndConnection(connection, "Invalid or spoofed Steam input frame", closeNative: true);
            return;
        }
        _orchestrator?.TryQueueSteamInput(connection.MatchId, connection.Id, newestTick, _inputBundle.AsSpan(0, count));
    }

    private void Deny(Connection connection, byte reason)
    {
        SendControl(connection.Handle, new[] { SteamGameplayWire.Deny, reason }, reliable: true);
        _pendingFlush.Add(connection.Handle);
        connection.DeniedAt = Stopwatch.GetTimestamp();
    }

    private static void SendControl(HSteamNetConnection connection, byte[] frame, bool reliable)
    {
        var pinned = GCHandle.Alloc(frame, GCHandleType.Pinned);
        try
        {
            int flags = reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable;
            SteamGameServerNetworkingSockets.SendMessageToConnection(connection, pinned.AddrOfPinnedObject(),
                (uint)frame.Length, flags, out _);
        }
        finally { pinned.Free(); }
    }

    private void ProcessOutbound(ConcurrentQueue<Outbound> queue, bool reliable, ref int count)
    {
        while (queue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref count);
            if (!_connectionsById.TryGetValue(item.ConnectionId, out var connection) || !connection.Bound ||
                connection.MatchId != item.MatchId || connection.EntityId != item.EntityId)
                continue;
            SendControl(connection.Handle, item.Frame, reliable);
            _pendingFlush.Add(connection.Handle);
        }
    }

    /// <summary>
    /// Frames queue with Steam's Nagle timer so one tick coalesces into few datagrams;
    /// flushing after each drain sends them immediately instead of 5 ms later.
    /// </summary>
    private void FlushPendingConnections()
    {
        if (_pendingFlush.Count == 0) return;
        foreach (var handle in _pendingFlush)
            if (_connections.ContainsKey(handle))
                SteamGameServerNetworkingSockets.FlushMessagesOnConnection(handle);
        _pendingFlush.Clear();
    }

    private void ProcessMatchClosures()
    {
        while (_closeMatches.TryDequeue(out var matchId))
        {
            _closeQueued.TryRemove(matchId, out _);
            foreach (var connection in _connections.Values)
                if (connection.MatchId == matchId)
                    EndConnection(connection, "Match ended", closeNative: true);
        }
    }

    private void CloseExpiredConnections()
    {
        long now = Stopwatch.GetTimestamp();
        foreach (var connection in _connections.Values)
        {
            if (connection.DeniedAt != 0 && Stopwatch.GetElapsedTime(connection.DeniedAt, now) >= TimeSpan.FromSeconds(1))
                EndConnection(connection, "Join denied", closeNative: true);
            else if (!connection.Bound && Stopwatch.GetElapsedTime(connection.ConnectedAt, now) >= TimeSpan.FromSeconds(10))
                EndConnection(connection, "Steam join timed out", closeNative: true);
        }
    }

    private void EndConnection(Connection connection, string reason, bool closeNative)
    {
        if (!_connections.TryRemove(connection.Handle, out _)) return;
        _connectionsById.TryRemove(connection.Id, out _);
        if (connection.Bound)
        {
            var key = (connection.MatchId, connection.EntityId);
            if (_entityConnections.TryGetValue(key, out var current) && ReferenceEquals(current, connection))
                _entityConnections.TryRemove(key, out _);
            _orchestrator?.DisconnectSteamPlayer(connection.MatchId, connection.Id);
            connection.MatchId = Guid.Empty;
            connection.EntityId = 0;
        }
        if (closeNative)
            SteamGameServerNetworkingSockets.CloseConnection(connection.Handle, 0, reason, false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is not null)
        {
            _lifetime.Cancel();
            if (_loop is not null)
            {
                try { await _loop; }
                catch (OperationCanceledException) { }
            }
            _lifetime.Dispose();
        }
        foreach (var connection in _connections.Values)
            EndConnection(connection, "GameHost shutdown", closeNative: true);
        if (_listenSocket != HSteamListenSocket.Invalid)
        {
            SteamGameServerNetworkingSockets.CloseListenSocket(_listenSocket);
            _listenSocket = HSteamListenSocket.Invalid;
        }
        if (_initialized)
        {
            SteamGameServer.LogOff();
            GameServer.Shutdown();
            _initialized = false;
        }
        _connectionStatus?.Dispose();
        _loginFailure?.Dispose();
        _loginSuccess?.Dispose();
        _outboundSignal.Dispose();
    }
}
