#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
using SlopArena.Shared;
using Steamworks;
using UnityEngine;

namespace SlopArena.Client.Network
{
    public class NetworkClient : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string _serverIp = "127.0.0.1";
        [SerializeField] private int _serverPort = 9876;
        private const int MaxSteamMessages = 64;
        /// <summary>Client frames are inputs and controls.</summary>
        private const int MaxSteamFrameBytes = 1024;
        /// <summary>A World Frame with its wire tag; Steam reassembles fragments above the MTU.</summary>
        private const int MaxSteamReceiveBytes = 1 + WorldFramePacket.HeaderSize + WorldFramePacket.MaxBodyBytes;
        /// <summary>Two seconds of frames; older ones are superseded anyway.</summary>
        private const int MaxQueuedFrames = 120;
        private const double SteamConnectionTimeoutSeconds = 30;
        private const double SteamJoinTimeoutSeconds = 15;
        private static readonly IntPtr[] SteamMessages = new IntPtr[MaxSteamMessages];
        private static NetworkClient? _activeMatch;


        private UI.MatchTransport _transport;
        private SteamMatchDescriptor? _steamDescriptor;
        private HSteamNetConnection _steamConnection = HSteamNetConnection.Invalid;
        private IntPtr _steamSendBuffer;
        private bool _hasSteamConnection;
        private bool _steamJoinSent;
        private bool _steamAdmitted;
        private bool _steamEverAdmitted;
        private bool _steamResultReceived;
        private bool _steamFailureReported;
        private bool _steamRemoteVerified;
        private long _steamAttemptStartedAt;
        private long _steamAdmissionDeadline;
        private long _steamJoinSentAt;
        private long _steamNextReconnectAt;
        private long _steamNextPingAt;
        private double _steamReconnectDelaySeconds = 1;
        private readonly byte[] _steamReceiveFrame = new byte[MaxSteamReceiveBytes];
        private readonly byte[] _steamInputFrame = new byte[1 + InputBundle.MaxSize];
        private string _connectionFailure = string.Empty;
        private readonly byte[] _steamControlFrame = new byte[1 + NetplayControlPacket.Size];
        private readonly byte[] _udpControlFrame = new byte[NetplayControlPacket.Size];
        private long _nextBootstrapAt;
        private readonly byte[] _udpInputFrame = new byte[InputBundle.MaxSize];
        private uint _latestServerStartTick;
        private int _readyGeneration = -1;
        private long _nextReadyAt;
        private volatile bool _clockReceived;
        private readonly ConcurrentQueue<ControlFrame> _controlQueue = new();
        public readonly struct ControlFrame
        {
            public ControlFrame(NetplayControlPacket Packet, double ReceivedAtSeconds, double RoundTripMilliseconds)
            {
                this.Packet = Packet;
                this.ReceivedAtSeconds = ReceivedAtSeconds;
                this.RoundTripMilliseconds = RoundTripMilliseconds;
            }

            public NetplayControlPacket Packet { get; }
            public double ReceivedAtSeconds { get; }
            public double RoundTripMilliseconds { get; }
        }

        private volatile UdpClient? _udp;
        private IPEndPoint _serverEp = new(IPAddress.Loopback, 9876);
        private ulong _entityId = 1;
        private volatile bool _connected;
        private Thread? _receiveThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<MatchResultPacket> _matchResultQueue = new();
        private readonly ConcurrentQueue<WorldFramePacket> _frameQueue = new();
        private int _queuedFrames;

        private long _nextPingNonce;
        private long _lastPingSentAt;
        private long _lastPingReceivedAt;
        private long _lastPingMsBits;
        private long _lastPingTick;
        private long _lastPingRequestAt;
        private long _lastServerPacketAt;
        private readonly byte[] _pingRequest = new byte[12];
        public float? LastPingMilliseconds
        {
            get
            {
                long receivedAt = Interlocked.Read(ref _lastPingReceivedAt);
                return receivedAt != 0 && ElapsedSeconds(receivedAt, Stopwatch.GetTimestamp()) <= 3
                    ? (float)BitConverter.Int64BitsToDouble(Interlocked.Read(ref _lastPingMsBits))
                    : null;
            }
        }
        public int ConnectionGeneration { get; private set; }
        public uint LastPingServerTick => unchecked((uint)Interlocked.Read(ref _lastPingTick));
        public string ServerEndpoint => _steamDescriptor != null
            ? $"Steam P2P {_steamDescriptor.ServerSteamId}"
            : _serverEp.ToString();
        private static readonly byte[] PongMagic = { (byte)'P', (byte)'O', (byte)'N', (byte)'G' };
        public ulong EntityId { get => _entityId; set => _entityId = value; }
        public bool IsServerConnected => _connected;
        public uint LastServerTick { get; private set; }
        public static double MonotonicSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        private static double ElapsedSeconds(long start, long end)
            => (end - start) / (double)Stopwatch.Frequency;
        private static double ElapsedMilliseconds(long start, long end)
            => (end - start) * 1000d / Stopwatch.Frequency;
        

        // ── Lifecycle ──

        private void Awake()
        {
            // A scene load alone never opens a socket. PvPMatch explicitly selects
            // its Steam route or the Editor-only development UDP route.
            _serverEp = new IPEndPoint(IPAddress.Loopback, 9876);
        }

        private void CreateSocket()
        {
            try
            {
                _udp?.Close();
                _udp = new UdpClient();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkClient] Failed to create socket: {ex.Message}");
                _udp = null;
            }
        }

        private void StartReceiveThread()
        {
            if (_running) return;
            _running = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "NetworkClient Receive"
            };
            _receiveThread.Start();
        }

        private void OnDestroy()
        {
            StopSteamTransport();
            StopUdpTransport();
            _transport = UI.MatchTransport.None;
        }

        public static void DisconnectActiveMatch()
        {
            var active = _activeMatch;
            if (active == null)
                return;
            active.StopSteamTransport();
            active.StopUdpTransport();
            active._transport = UI.MatchTransport.None;
            active._connected = false;
            active.ClearReceiveQueues();
            if (_activeMatch == active)
                _activeMatch = null;
        }

        private void StopSteamTransport()
        {
            if (_hasSteamConnection)
            {
                try { SteamNetworkingSockets.CloseConnection(_steamConnection, 0, "match transport closed", false); }
                catch { }
            }
            _hasSteamConnection = false;
            _steamConnection = HSteamNetConnection.Invalid;
            _steamJoinSent = false;
            _steamAdmitted = false;
            _connected = false;
            if (_steamSendBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_steamSendBuffer);
                _steamSendBuffer = IntPtr.Zero;
            }
            ClearReceiveQueues();
            if (_activeMatch == this)
                _activeMatch = null;

            if (_transport == UI.MatchTransport.SteamP2P)
                _transport = UI.MatchTransport.None;
            _steamDescriptor = null;
            Interlocked.Exchange(ref _lastPingReceivedAt, 0);
        }

        private void StopUdpTransport()
        {
            _running = false;
            var socket = _udp;
            _udp = null;
            socket?.Close();
            _receiveThread?.Join(500);
            _receiveThread = null;
            if (_activeMatch == this)
                _activeMatch = null;

        }

        /// <summary>Connect only through the explicit Editor development UDP profile.</summary>
        public void Connect(string ip, int port)
        {
            if (!Application.isEditor ||
                UI.MatchConfig.Transport != UI.MatchTransport.DevelopmentUdp ||
                Environment.GetEnvironmentVariable("SLOPARENA_DEV_UDP") != "1" ||
                port <= 0)
            {
                Debug.LogError("[NetworkClient] Raw UDP is restricted to explicit Editor development matches.");
                return;
            }
            IPEndPoint endpoint;
            try
            {
                endpoint = ResolveDevelopmentEndpoint(ip, port);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                _connectionFailure = $"Could not resolve development server '{ip}': {ex.Message}";
                Debug.LogError($"[NetworkClient] {_connectionFailure}");
                return;
            }
            StopSteamTransport();
            StopUdpTransport();
            ClearReceiveQueues();
            _transport = UI.MatchTransport.DevelopmentUdp;
            _connected = false;
            Interlocked.Exchange(ref _lastPingReceivedAt, 0);
            Interlocked.Exchange(ref _lastPingRequestAt, 0);
            Interlocked.Exchange(ref _lastServerPacketAt, 0);
            ConnectionGeneration++;
            ResetServerClockState();
            Interlocked.Exchange(ref _lastPingSentAt, 0);
            _serverIp = ip;
            _serverPort = port;
            _serverEp = endpoint;
            _connectionFailure = string.Empty;
            CreateSocket();
            StartReceiveThread();
            _activeMatch = this;
            _clockReceived = false;
            _nextBootstrapAt = 0;
            SendControl(new NetplayControlPacket(NetplayControlKind.Bootstrap, _entityId, 0, 0));

        }

        internal static IPEndPoint ResolveDevelopmentEndpoint(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("A server address is required.", nameof(host));
            if (port is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            var addresses = IPAddress.TryParse(host, out var literal)
                ? new[] { literal }
                : Dns.GetHostAddresses(host);
            foreach (var address in addresses)
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    return new IPEndPoint(address, port);
            throw new ArgumentException("The development server needs an IPv4 address.", nameof(host));
        }

        public void ConnectSteam(SteamMatchDescriptor descriptor)
        {
            if (UI.MatchConfig.Transport != UI.MatchTransport.SteamP2P ||
                UI.MatchConfig.SteamDescriptor != descriptor ||
                descriptor == null)
            {
                FailSteamTransport("Steam match descriptor is not the active validated match.");
                return;
            }
            StopSteamTransport();
            StopUdpTransport();
            ClearReceiveQueues();
            _transport = UI.MatchTransport.SteamP2P;
            ResetServerClockState();
            _steamDescriptor = descriptor;
            _steamFailureReported = false;
            _steamEverAdmitted = false;
            _steamResultReceived = false;
            _steamJoinSent = false;
            _steamAdmitted = false;
            _steamRemoteVerified = false;
            _steamReconnectDelaySeconds = 1;
            _steamAttemptStartedAt = Stopwatch.GetTimestamp();
            _steamAdmissionDeadline = _steamAttemptStartedAt +
                SteamMatchDescriptor.MaximumAdmissionWindowSeconds * Stopwatch.Frequency;
            _connectionFailure = string.Empty;
            _activeMatch = this;
            try
            {
                _steamSendBuffer = Marshal.AllocHGlobal(MaxSteamFrameBytes);
                SteamNetworkingUtils.InitRelayNetworkAccess();
                SteamNetworkingSockets.InitAuthentication();
            }
            catch (Exception exception)
            {
                FailSteamTransport($"Steam networking initialization failed: {exception.Message}");
            }
            _clockReceived = false;
            _nextBootstrapAt = 0;
        }

        // ── Send / Receive ──

        /// <summary>
        /// Send the newest simulated inputs (oldest first, consecutive ticks ending at
        /// <paramref name="newestTick"/>). Each datagram repeats recent ticks so one lost
        /// datagram never drops a press; it leaves immediately instead of waiting for Nagle.
        /// </summary>
        public void SendInputs(uint newestTick, ReadOnlySpan<InputState> inputs)
        {
            if (inputs.Length == 0) return;
            if (_transport == UI.MatchTransport.SteamP2P)
            {
                if (!_steamAdmitted || !_hasSteamConnection || _steamSendBuffer == IntPtr.Zero || _steamResultReceived)
                    return;
                _steamInputFrame[0] = SteamGameplayWire.Input;
                int size = InputBundle.Write(_steamInputFrame.AsSpan(1), _entityId, newestTick, inputs);
                SendSteamFrame(_steamInputFrame, 1 + size, Constants.k_nSteamNetworkingSend_UnreliableNoNagle);
                return;
            }
            if (_transport != UI.MatchTransport.DevelopmentUdp || _udp == null) return;

            int udpSize = InputBundle.Write(_udpInputFrame, _entityId, newestTick, inputs);
            try
            {
                _udp.Send(_udpInputFrame, udpSize, _serverEp);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkClient] Send failed: {ex.Message}");
                _running = false;
                _udp?.Close();
                _udp = null;
                _connected = false;
                Interlocked.Exchange(ref _lastPingReceivedAt, 0);
            }
        }

        public void ReceiveControlPackets(List<ControlFrame> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            while (_controlQueue.TryDequeue(out var frame))
            {
                if (frame.Packet.EntityId == _entityId && frame.Packet.Tick >= LastServerTick)
                {
                    LastServerTick = frame.Packet.Tick;
                    _latestServerStartTick = frame.Packet.StartTick;
                }
                destination.Add(frame);
            }
        }
        public void SendReady()
        {
            if (_readyGeneration != ConnectionGeneration)
            {
                _readyGeneration = ConnectionGeneration;
                _nextReadyAt = 0;
            }
            long now = Stopwatch.GetTimestamp();
            if (now < _nextReadyAt) return;
            _nextReadyAt = now + Stopwatch.Frequency / 2;
            SendControl(new NetplayControlPacket(
                NetplayControlKind.Ready, _entityId, LastServerTick, _latestServerStartTick));
        }

        private void ResetServerClockState()
        {
            LastServerTick = 0;
            _latestServerStartTick = 0;
            _readyGeneration = -1;
            _nextReadyAt = 0;
        }

        private void SendControl(NetplayControlPacket packet)
        {
            if (!packet.IsValid) return;
            if (_transport == UI.MatchTransport.SteamP2P)
            {
                if (!_steamAdmitted || !_hasSteamConnection || _steamResultReceived) return;
                _steamControlFrame[0] = SteamGameplayWire.Control;
                packet.Serialize(_steamControlFrame.AsSpan(1));
                SendSteamFrame(_steamControlFrame, _steamControlFrame.Length, Constants.k_nSteamNetworkingSend_ReliableNoNagle);
                return;
            }
            if (_transport != UI.MatchTransport.DevelopmentUdp || _udp == null) return;
            packet.Serialize(_udpControlFrame);
            try { _udp.Send(_udpControlFrame, _udpControlFrame.Length, _serverEp); }
            catch (SocketException) { _connected = false; }
        }


        private void EnqueueControl(ReadOnlySpan<byte> payload, double receivedAtSeconds)
        {
            if (!NetplayControlPacket.TryDeserialize(payload, out var packet)) return;
            double rtt = LastPingMilliseconds ?? 0;
            _controlQueue.Enqueue(new ControlFrame(packet, receivedAtSeconds, rtt));
            if (packet.Kind == NetplayControlKind.Clock && packet.EntityId == _entityId)
                _clockReceived = true;
        }


        private bool SendSteamFrame(byte[] frame, int length, int sendFlags)
        {
            if (!_hasSteamConnection || _steamSendBuffer == IntPtr.Zero || length is < 1 or > MaxSteamFrameBytes)
                return false;
            try
            {
                Marshal.Copy(frame, 0, _steamSendBuffer, length);
                var result = SteamNetworkingSockets.SendMessageToConnection(
                    _steamConnection, _steamSendBuffer, (uint)length, sendFlags, out _);
                if (result == EResult.k_EResultOK)
                    return true;
                HandleSteamConnectionLoss($"Steam send failed ({result}).");
            }
            catch (Exception exception)
            {
                HandleSteamConnectionLoss($"Steam send failed: {exception.Message}");
            }
            return false;
        }

        /// <summary>Drain the World Frames received since the previous call, in arrival order.</summary>
        public void ReceiveWorldFrames(List<WorldFramePacket> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            while (_frameQueue.TryDequeue(out var frame))
            {
                Interlocked.Decrement(ref _queuedFrames);
                destination.Add(frame);
                if (frame.Tick > LastServerTick) LastServerTick = frame.Tick;
            }
        }

        private void EnqueueFrame(ReadOnlySpan<byte> payload)
        {
            if (!WorldFramePacket.TryDeserialize(payload, out var frame) || frame == null)
                return;
            _frameQueue.Enqueue(frame);
            if (Interlocked.Increment(ref _queuedFrames) > MaxQueuedFrames && _frameQueue.TryDequeue(out _))
                Interlocked.Decrement(ref _queuedFrames);
            Interlocked.Exchange(ref _lastServerPacketAt, Stopwatch.GetTimestamp());
        }

        /// <summary>Drain authoritative final match snapshots received from the server.</summary>
        public List<MatchResultPacket> ReceiveMatchResults()
        {
            var result = new List<MatchResultPacket>();
            while (_matchResultQueue.TryDequeue(out var entry))
                result.Add(entry);
            return result;
        }


        // ── Receive loop ──

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buf = _udp.Receive(ref ep);
                    if (buf.Length == 16 && ep.Equals(_serverEp) &&
                        buf.AsSpan(0, 4).SequenceEqual(PongMagic))
                    {
                        long now = Stopwatch.GetTimestamp();
                        long requestAt = Interlocked.Read(ref _lastPingSentAt);
                        long nonce = BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(4, 8));
                        if (nonce == Interlocked.Read(ref _nextPingNonce) && requestAt != 0)
                        {
                            double milliseconds = ElapsedMilliseconds(requestAt, now);
                            Interlocked.Exchange(ref _lastPingMsBits, BitConverter.DoubleToInt64Bits(milliseconds));
                            Interlocked.Exchange(ref _lastPingTick, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(12, 4)));
                            Interlocked.Exchange(ref _lastPingReceivedAt, now);
                            _connected = true;
                        }
                        continue;
                    }
                    if (!ep.Equals(_serverEp)) continue;

                    if (WorldFramePacket.TryPeekTick(buf, out _))
                    {
                        EnqueueFrame(buf);
                        _connected = true;
                        continue;
                    }
                    if (MatchResultPacket.TryDeserialize(buf, out var matchResult))
                    {
                        _matchResultQueue.Enqueue(matchResult!);
                        continue;
                    }
                    if (buf.Length == NetplayControlPacket.Size)
                        EnqueueControl(buf, MonotonicSeconds);
                }
                catch
                {
                    if (_running) break;
                }
            }
        }

        public event Action<string>? ConnectionFailed;
        public string ConnectionFailure => _connectionFailure;

        private void UpdateSteam()
        {
            if (_steamResultReceived)
            {
                if (_hasSteamConnection)
                    ReceiveSteamMessages();
                return;
            }
            var descriptor = _steamDescriptor;
            if (descriptor == null)
            {
                FailSteamTransport("Steam match descriptor is unavailable.");
                return;
            }

            long now = Stopwatch.GetTimestamp();
            // Local monotonic deadline from receipt: a skewed PC clock must not reject a
            // live match. The GameHost still enforces the authoritative admission deadline.
            if (!_steamEverAdmitted && now >= _steamAdmissionDeadline)
            {
                FailSteamTransport("Steam match admission expired before joining.");
                return;
            }

            if (_hasSteamConnection)
            {
                if (!SteamNetworkingSockets.GetConnectionInfo(_steamConnection, out var info))
                {
                    HandleSteamConnectionLoss("Steam connection information is unavailable.");
                    return;
                }
                if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally ||
                    info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer)
                {
                    HandleSteamConnectionLoss($"Steam connection closed: {info.m_szEndDebug}");
                    return;
                }
                if (info.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                {
                    if (_steamAdmitted)
                        HandleSteamConnectionLoss("Steam match connection is no longer connected.");
                    else if (ElapsedSeconds(_steamAttemptStartedAt, now) >= SteamConnectionTimeoutSeconds)
                        HandleSteamConnectionLoss("Steam match connection establishment timed out.");
                    return;
                }

                ulong actualServerId = info.m_identityRemote.GetSteamID64();
                if (actualServerId != descriptor.ServerSteamId)
                {
                    FailSteamTransport($"Steam host identity mismatch; expected {descriptor.ServerSteamId}, got {actualServerId}.");
                    return;
                }
                _steamRemoteVerified = true;
                if (!_steamJoinSent)
                    SendSteamJoin();
                if (!_hasSteamConnection)
                    return;

                ReceiveSteamMessages();
                if (!_hasSteamConnection)
                    return;
                if (_steamJoinSent && !_steamAdmitted &&
                    ElapsedSeconds(_steamJoinSentAt, now) >= SteamJoinTimeoutSeconds)
                {
                    HandleSteamConnectionLoss("GameHost did not acknowledge the reliable match join.");
                    return;
                }
                if (_steamAdmitted)
                    UpdateSteamPing(now);
                return;
            }

            if (_steamEverAdmitted && now < _steamNextReconnectAt)
                return;
            if (!_steamEverAdmitted && ElapsedSeconds(_steamAttemptStartedAt, now) >= SteamConnectionTimeoutSeconds)
            {
                FailSteamTransport("Steam authentication, relay, or GameHost connection did not become ready in time.");
                return;
            }

            try
            {
                if (!SteamUser.BLoggedOn())
                    return;
                var authentication = SteamNetworkingSockets.GetAuthenticationStatus(out _);
                var relay = SteamNetworkingUtils.GetRelayNetworkStatus(out _);
                if (authentication != ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current ||
                    relay != ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current)
                    return;

                var remoteIdentity = default(SteamNetworkingIdentity);
                remoteIdentity.SetSteamID64(descriptor.ServerSteamId);
                _steamConnection = SteamNetworkingSockets.ConnectP2P(
                    ref remoteIdentity, descriptor.VirtualPort, 0, null!);
                if (_steamConnection == HSteamNetConnection.Invalid)
                {
                    HandleSteamConnectionLoss("Steam ConnectP2P returned an invalid connection.");
                    return;
                }
                _hasSteamConnection = true;
                _steamJoinSent = false;
                _steamRemoteVerified = false;
                _steamAttemptStartedAt = now;
            }
            catch (Exception exception)
            {
                HandleSteamConnectionLoss($"Steam networking failed: {exception.Message}");
            }
        }

        private void SendSteamJoin()
        {
            if (_steamJoinSent || !_steamRemoteVerified || _steamDescriptor == null)
                return;
            byte[] frame;
            try { frame = SteamGameplayWire.CreateJoin(_steamDescriptor); }
            catch (Exception exception)
            {
                FailSteamTransport($"Steam join descriptor is invalid: {exception.Message}");
                return;
            }
            if (SendSteamFrame(frame, frame.Length, Constants.k_nSteamNetworkingSend_ReliableNoNagle))
            {
                _steamJoinSent = true;
                _steamJoinSentAt = Stopwatch.GetTimestamp();
            }
        }

        private void ReceiveSteamMessages()
        {
            int count;
            try
            {
                count = SteamNetworkingSockets.ReceiveMessagesOnConnection(
                    _steamConnection, SteamMessages, MaxSteamMessages);
            }
            catch (Exception exception)
            {
                HandleSteamConnectionLoss($"Steam receive failed: {exception.Message}");
                return;
            }
            if (count < 0)
            {

                HandleSteamConnectionLoss($"Steam receive failed ({count}).");
                return;
            }

            // Convert Steam's receive timestamps to our monotonic clock so clock samples
            // carry their real arrival time, not the frame that happened to read them.
            double readAt = MonotonicSeconds;
            long steamNow = count > 0 ? SteamNetworkingUtils.GetLocalTimestamp().m_SteamNetworkingMicroseconds : 0;
            for (int i = 0; i < count; i++)
            {
                IntPtr pointer = SteamMessages[i];
                SteamMessages[i] = IntPtr.Zero;
                try
                {
                    if (!_hasSteamConnection)
                        continue;
                    var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointer);
                    if (message.m_cbSize < 1 || message.m_cbSize > _steamReceiveFrame.Length)
                        continue; // larger than any World Frame: not ours
                    long ageMicroseconds = steamNow - message.m_usecTimeReceived.m_SteamNetworkingMicroseconds;
                    double receivedAt = readAt - Math.Clamp(ageMicroseconds, 0L, 1_000_000L) / 1_000_000d;
                    Marshal.Copy(message.m_pData, _steamReceiveFrame, 0, message.m_cbSize);
                    ProcessSteamFrame(_steamReceiveFrame.AsSpan(0, message.m_cbSize), receivedAt);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[NetworkClient] Ignoring invalid Steam match message: {exception.Message}");
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(pointer);
                }
            }
        }

        /// <summary>
        /// Read pending match messages now. Called at the start of each fixed update so the
        /// simulation consumes the freshest snapshots instead of those read last frame.
        /// </summary>
        public void PollTransport()
        {
            if (_transport == UI.MatchTransport.SteamP2P && _hasSteamConnection && _steamAdmitted)
                ReceiveSteamMessages();
        }

        private void ProcessSteamFrame(ReadOnlySpan<byte> frame, double receivedAtSeconds)
        {
            if (frame.Length < 1)
                return;
            if (frame[0] == SteamGameplayWire.Ack)
            {
                if (!_steamJoinSent || !SteamGameplayWire.TryParseAck(frame, out var entityId))
                    return;
                if (entityId != _entityId)
                {
                    FailSteamTransport($"GameHost acknowledged entity {entityId}, expected {_entityId}.");
                    return;
                }
                ConnectionGeneration++;
                _steamAdmitted = true;
                _steamEverAdmitted = true;
                _steamReconnectDelaySeconds = 1;
                _connected = true;
                return;
            }
            if (frame[0] == SteamGameplayWire.Deny)
            {
                int code = frame.Length == 2 ? frame[1] : 0;
                FailSteamTransport($"GameHost denied match admission (code {code}).");
                return;
            }
            if (!_steamAdmitted || frame.Length <= 1)
                return;
            var payload = frame.Slice(1);
            if (frame[0] == SteamGameplayWire.Control)
            {
                if (payload.Length == NetplayControlPacket.Size)
                    EnqueueControl(payload, receivedAtSeconds);
                return;
            }
            switch (frame[0])
            {
                case SteamGameplayWire.Frame:
                    EnqueueFrame(payload);
                    break;
                case SteamGameplayWire.Result:
                    if (!_steamResultReceived && MatchResultPacket.TryDeserialize(payload, out var result))
                    {
                        _matchResultQueue.Enqueue(result!);
                        _steamResultReceived = true;
                    }
                    break;
            }
        }

        /// <summary>
        /// Steam's live view of this connection for late-input diagnostics: a growing send
        /// queue or pending bytes point at this machine's uplink; healthy values point past it.
        /// </summary>
        public string DescribeTransport()
        {
            if (_transport != UI.MatchTransport.SteamP2P || !_hasSteamConnection)
                return $"transport={_transport}";
            var status = default(SteamNetConnectionRealTimeStatus_t);
            var lane = default(SteamNetConnectionRealTimeLaneStatus_t);
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(_steamConnection, ref status, 0, ref lane) != EResult.k_EResultOK)
                return "steam=unavailable";
            return FormattableString.Invariant(
                $"steamPing={status.m_nPing}ms quality={status.m_flConnectionQualityLocal:F2}/{status.m_flConnectionQualityRemote:F2} ") +
                FormattableString.Invariant(
                $"out={status.m_flOutPacketsPerSec:F0}pkt/s in={status.m_flInPacketsPerSec:F0}pkt/s sendRate={status.m_nSendRateBytesPerSecond / 1024}KB/s ") +
                FormattableString.Invariant(
                $"pending={status.m_cbPendingUnreliable}/{status.m_cbPendingReliable}B queue={(long)status.m_usecQueueTime / 1000.0:F1}ms jitter={status.m_usecMaxJitter / 1000.0:F1}ms");
        }

        private void UpdateSteamPing(long now)
        {
            if (now < _steamNextPingAt)
                return;
            _steamNextPingAt = now + Stopwatch.Frequency;
            var status = default(SteamNetConnectionRealTimeStatus_t);
            var lane = default(SteamNetConnectionRealTimeLaneStatus_t);
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(
                    _steamConnection, ref status, 0, ref lane) != EResult.k_EResultOK ||
                status.m_nPing < 0 || status.m_nPing > 60000)
                return;
            Interlocked.Exchange(ref _lastPingMsBits, BitConverter.DoubleToInt64Bits(status.m_nPing));
            Interlocked.Exchange(ref _lastPingTick, LastServerTick);
            Interlocked.Exchange(ref _lastPingReceivedAt, now);
        }

        private void HandleSteamConnectionLoss(string reason)
        {
            if (_hasSteamConnection)
            {
                try { SteamNetworkingSockets.CloseConnection(_steamConnection, 0, reason, false); }
                catch { }
            }
            _hasSteamConnection = false;
            _steamConnection = HSteamNetConnection.Invalid;
            _steamJoinSent = false;
            _steamAdmitted = false;
            _steamRemoteVerified = false;
            _connected = false;
            Interlocked.Exchange(ref _lastPingReceivedAt, 0);
            _clockReceived = false;
            _nextBootstrapAt = 0;
            ClearTransientReceiveQueues();
            if (_steamResultReceived)
                return;
            if (_steamEverAdmitted && _steamDescriptor != null)
            {
                _steamNextReconnectAt = Stopwatch.GetTimestamp() +
                    (long)(_steamReconnectDelaySeconds * Stopwatch.Frequency);
                _steamReconnectDelaySeconds = Math.Min(5, _steamReconnectDelaySeconds * 2);
                Debug.LogWarning($"[NetworkClient] {reason} Reconnecting to the same Steam match.");
                return;
            }
            FailSteamTransport(reason);
        }

        private void FailSteamTransport(string reason)
        {
            _connectionFailure = reason;
            Debug.LogError($"[NetworkClient] {reason}");
            bool notify = !_steamFailureReported;
            _steamFailureReported = true;
            StopSteamTransport();
            if (notify)
                ConnectionFailed?.Invoke(reason);
        }

        private void ClearReceiveQueues()
        {
            ClearTransientReceiveQueues();
            while (_matchResultQueue.TryDequeue(out _)) { }
        }
        private void ClearTransientReceiveQueues()
        {
            while (_frameQueue.TryDequeue(out _)) Interlocked.Decrement(ref _queuedFrames);
            while (_controlQueue.TryDequeue(out _)) { }
        }

        // ── Socket retry ──
        private void SendPingRequest()
        {
            if (!_connected || _udp == null) return;
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref _lastPingRequestAt);
            if (ElapsedSeconds(previous, now) < 1.0) return;
            if (Interlocked.CompareExchange(ref _lastPingRequestAt, now, previous) != previous) return;
            long nonce = Interlocked.Increment(ref _nextPingNonce);
            _pingRequest[0] = (byte)'P'; _pingRequest[1] = (byte)'I';
            _pingRequest[2] = (byte)'N'; _pingRequest[3] = (byte)'G';
            BinaryPrimitives.WriteInt64LittleEndian(_pingRequest.AsSpan(4), nonce);
            Interlocked.Exchange(ref _lastPingSentAt, now);
            try { _udp.Send(_pingRequest, _pingRequest.Length, _serverEp); }
            catch { Interlocked.Exchange(ref _lastPingReceivedAt, 0); _connected = false; }
        }

        private void Update()
        {
            if (_transport == UI.MatchTransport.SteamP2P)
            {
                long steamNow = Stopwatch.GetTimestamp();
                if (_steamAdmitted && !_clockReceived && steamNow >= _nextBootstrapAt)
                {
                    SendControl(new NetplayControlPacket(NetplayControlKind.Bootstrap, _entityId, 0, 0));
                    _nextBootstrapAt = steamNow + Stopwatch.Frequency / 2;
                }
                UpdateSteam();
                return;
            }
            if (_transport != UI.MatchTransport.DevelopmentUdp)
                return;
            if (_udp == null && !_running)
            {
                CreateSocket();
                StartReceiveThread();
            }
            long now = Stopwatch.GetTimestamp();
            if (!_clockReceived && now >= _nextBootstrapAt)
            {
                SendControl(new NetplayControlPacket(NetplayControlKind.Bootstrap, _entityId, 0, 0));
                _nextBootstrapAt = now + Stopwatch.Frequency / 2;
            }
            long lastPingAt = Interlocked.Read(ref _lastPingReceivedAt);
            if (lastPingAt != 0 && ElapsedSeconds(lastPingAt, now) > 3)
            {
                Interlocked.Exchange(ref _lastPingReceivedAt, 0);
                _connected = false;
            }
            if (_lastServerPacketAt != 0 && ElapsedSeconds(_lastServerPacketAt, now) > 3)
                _connected = false;
            SendPingRequest();
        }

    }
}
