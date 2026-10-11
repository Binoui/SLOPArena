using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
namespace SlopArena.Server
{
	/// <summary>
	/// One match instance with a 60Hz Shared simulation. Development uses UDP;
	/// VPS uses the process-wide Steam transport and never binds a match UDP port.
	/// Runs on its own thread and uses ServerSimulation for authoritative gameplay.
	///
	/// Roster-driven (issue #35): the master server sends the locked-in character
	/// classes + entity IDs via <c>POST /match/start</c>; this instance spawns one
	/// entity per player with the correct <see cref="CharacterClass"/> instead of
	/// hardcoded Manki. Countdown starts once every rostered player has connected.
	/// </summary>
	public class MatchInstance
	{
		private readonly int _port;
		private readonly string _matchId;
		private readonly string _arenaName;
		private readonly MatchContentCatalog _contentCatalog;
		private readonly List<PlayerSlot> _slots;
        private readonly bool _hasWibou;
        private readonly bool _hasProjectileVisuals;
        private readonly List<ProjectileVisualState> _projectileVisuals = new(12);
        private readonly List<ulong> _swordTrailOwners = new(4);
		private UdpClient? _udpServer;
		private volatile bool _running = true;

		private ArenaDefinition _arena;
		private ServerSimulation _sim = null!;
		private uint _serverTick;
		private uint _startTick;

		/// <summary>
		/// The inputs the server actually consumed for the last sim tick, keyed by
		/// entity. Sent back to clients as the input-relay section of each state
		/// broadcast (issue #80, ADR-0010): membership in this dict is the relay
		/// signal, so empty queues, eliminated entities, and disconnected players
		/// all broadcast the explicit no-input marker. Null until the first
		/// playing tick (countdown broadcasts relay nothing).
		/// </summary>
		private Dictionary<ulong, InputState>? _lastTickInputs;
		private readonly Dictionary<ulong, InputState> _tickInputs = new();

		private const double TimeoutSeconds = 5.0;

		// Match lifecycle
		private volatile MatchState _matchState = MatchState.Waiting;
		private ushort _countdownTicks;
		private const ushort CountdownDuration = 300; // 5 seconds at 60Hz
		private readonly IMatchRule _rule;
		private ulong _winnerEntityId;
		private MatchResultPacket? _matchResultPacket;

		private ushort _postMatchTicks;
		private const ushort PostMatchDuration = 180; // 3 seconds before cleanup

		private Thread? _thread;
		private readonly Action<int> _onMatchEnd;
		private readonly Action<Guid, long>? _onMatchResult;
		private readonly Action<Guid>? _onSteamMatchEnd;
		private readonly Action<Guid, string>? _onMatchCancelled;
		private readonly Action<Guid, ulong, byte[], bool>? _steamSend;
		private readonly DateTimeOffset? _admissionDeadlineUtc;
		private readonly string? _contentHash;
		private readonly MatchContentHandleMap? _contentHandleMap;
		private readonly Guid _steamMatchGuid;
		private readonly object _steamGate = new();
		private int _steamInputCount;
		private DateTimeOffset? _noOpponentSinceUtc;
		private readonly TimeProvider _clock;
		private int _cancelNotified;
		private bool _steamResultSent;

		private readonly record struct SteamInput(long ConnectionId, ulong EntityId, uint Tick, InputState Input);
		private readonly ConcurrentQueue<SteamInput> _steamInputs = new();

		/// <param name="roster">Ordered players (index 0 = host). Each carries an entity ID (1..N) and a character class.</param>
		/// <param name="maxStocks">Stocks per player (default 3, issue #37).</param>
		/// <param name="onMatchResult">Optional callback invoked once when the match ends (match guid, winner steam id).</param>
		public MatchInstance(int port, string matchId, string arenaName,
			IReadOnlyList<MatchPlayer> roster, MatchContentCatalog contentCatalog, Action<int> onMatchEnd,
			byte maxStocks = MatchDefaults.DefaultMaxStocks, Action<Guid, long>? onMatchResult = null,
			Action<Guid>? onSteamMatchEnd = null, Action<Guid, string>? onMatchCancelled = null,
			DateTimeOffset? admissionDeadlineUtc = null, string? contentHash = null,
			Action<Guid, ulong, byte[], bool>? steamSend = null, TimeProvider? clock = null,
			MatchContentHandleMap? contentHandleMap = null)
		{
			_port = port;
			_matchId = matchId;
			_arenaName = arenaName;
			_contentCatalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
			_onMatchEnd = onMatchEnd;
			_onMatchResult = onMatchResult;
			_onSteamMatchEnd = onSteamMatchEnd;
			_onMatchCancelled = onMatchCancelled;
			_admissionDeadlineUtc = admissionDeadlineUtc;
			_contentHash = contentHash;
			_steamSend = steamSend;
			_steamMatchGuid = steamSend is null ? Guid.Empty : Guid.Parse(matchId);
			_clock = clock ?? TimeProvider.System;
			Diagnostics = new MatchDiagnostics(matchId, arenaName, steamSend is null ? "development-udp" : "steam-p2p", _clock);
			_contentHandleMap = contentHandleMap;
			_rule = new StockMatchRule(maxStocks);

			_slots = new List<PlayerSlot>(roster.Count);
			foreach (var p in roster)
			{
				var content = _contentCatalog.Resolve(p.CharacterClass)
					?? throw new InvalidDataException($"Roster selector '{p.CharacterClass}' is not in the match content catalog.");
				_slots.Add(new PlayerSlot((ulong)p.EntityId, p.CharacterClass, p.SteamId, content));
                _hasWibou |= p.CharacterClass == CharacterClass.Wibou;
                _hasProjectileVisuals |= p.CharacterClass is CharacterClass.Wibou or CharacterClass.FightGuy;
			}
		}

		/// <summary>True while the match loop is active.</summary>
		public bool IsRunning => _running;
		public string MatchId => _matchId;
		public MatchDiagnostics Diagnostics { get; }
		private void Log(string message)
		{
			Diagnostics.Log(_serverTick, message);
			Console.WriteLine($"[Match:{_matchId}] {message}");
		}
		public bool HasStartedCountdown => _matchState != MatchState.Waiting;
		public uint ServerTick => _serverTick;
		public uint StartTick => _startTick;
		public MatchContentHandleMap? ContentHandleMap => _contentHandleMap;

		/// <summary>Number of rostered players.</summary>
		public int PlayerCount => _slots.Count;

		public void Start()
		{
			_thread = new Thread(Run) { IsBackground = true, Name = $"Match-{_matchId}" };
			_thread.Start();
		}

		public void Stop() => Stop(null, notifyMaster: false);

		public void Stop(string? cancellationReason, bool notifyMaster = true)
		{
			if (cancellationReason is not null && notifyMaster && _matchState != MatchState.Ended &&
				Interlocked.Exchange(ref _cancelNotified, 1) == 0 &&
				Guid.TryParse(_matchId, out var matchGuid))
				_onMatchCancelled?.Invoke(matchGuid, cancellationReason);
			_running = false;
			Diagnostics.Log(_serverTick, cancellationReason is null ? "Stop requested." : $"Canceled: {cancellationReason}");
			Diagnostics.Complete();
			try { _udpServer?.Close(); } catch { }
		}

		internal void StopAndWait()
		{
			Stop();
			var thread = _thread;
			if (thread is not null && thread != Thread.CurrentThread)
				thread.Join();
		}

		/// <summary>Bind a Steam-authenticated account to its roster slot.</summary>
		public bool TryBindSteamPlayer(ulong steamId, long connectionId, string contentHash,
			out ulong entityId, out long replacedConnectionId, out byte denialCode)
		{
			entityId = 0;
			replacedConnectionId = 0;
			denialCode = 1; // unknown match/identity
			if (_steamSend is null || connectionId <= 0 || !string.Equals(contentHash, _contentHash, StringComparison.Ordinal))
				return false;

			lock (_steamGate)
			{
				if (!_running || _matchState == MatchState.Ended)
				{
					denialCode = 2; // ended
					return false;
				}
				if (_matchState == MatchState.Waiting && _admissionDeadlineUtc is DateTimeOffset deadline && _clock.GetUtcNow() >= deadline)
				{
					denialCode = 3; // expired
					return false;
				}
				PlayerSlot? slot = null;
				foreach (var candidate in _slots)
					if (candidate.SteamId > 0 && (ulong)candidate.SteamId == steamId)
					{
						slot = candidate;
						break;
					}
				if (slot is null) return false;
				if (slot.ConnectionId == connectionId)
				{
					denialCode = 4; // duplicate join
					return false;
				}
				if (slot.ConnectionId != 0)
				{
					if (_matchState == MatchState.Waiting)
					{
						denialCode = 5; // occupied during admission
						return false;
					}
					replacedConnectionId = slot.ConnectionId;
				}
				slot.ReadyConnectionId = 0;
				slot.LastInput = default;
				slot.LastInputConnectionId = connectionId;
				slot.ConnectionId = connectionId;
				slot.SteamConnected = true;
				slot.MissingInputTicks = 0;
				slot.Disconnected = false;
				entityId = slot.EntityId;
				Diagnostics.RecordPeer(slot.EntityId, true);
				Log($"Entity {slot.EntityId} Steam connection admitted.");
				denialCode = 0;
				return true;
			}
		}

		public void DisconnectSteamPlayer(long connectionId)
		{
			lock (_steamGate)
			{
				foreach (var slot in _slots)
					if (slot.ConnectionId == connectionId)
					{
						slot.ConnectionId = 0;
						slot.SteamConnected = false;
						slot.ReadyConnectionId = 0;
						slot.MissingInputTicks = 0;
						slot.Disconnected = true;
						slot.Queue.Clear();
						slot.LastInput = default;
						slot.LastInputConnectionId = 0;
						Diagnostics.RecordPeer(slot.EntityId, false);
						Log($"Entity {slot.EntityId} Steam connection closed.");
						return;
					}
			}
		}

		public bool TryQueueSteamInput(long connectionId, uint tick, InputState input)
		{
			if (_steamSend is null || connectionId <= 0 || !_running)
				return false;
			if (tick <= _serverTick || tick - _serverTick > NetplayClock.HistoryTicks)
				return false;
			if (Interlocked.Increment(ref _steamInputCount) > _slots.Count * 64)
			{
				Interlocked.Decrement(ref _steamInputCount);
				return false;
			}
			lock (_steamGate)
			{
				foreach (var slot in _slots)
					if (slot.ConnectionId == connectionId && slot.SteamConnected && slot.ReadyConnectionId == connectionId)
					{
						_steamInputs.Enqueue(new SteamInput(connectionId, slot.EntityId, tick, input));
						return true;
					}
			}
			Interlocked.Decrement(ref _steamInputCount);
			return false;
		}
		public bool TryMarkSteamReady(long connectionId, NetplayControlPacket control)
		{
			lock (_steamGate)
			{
				foreach (var slot in _slots)
					if (slot.ConnectionId == connectionId && slot.SteamConnected &&
						slot.EntityId == control.EntityId && control.Kind == NetplayControlKind.Ready &&
						control.Tick <= _serverTick && control.StartTick == _startTick)
					{
						slot.ReadyConnectionId = connectionId;
						return true;
					}
			}
			return false;
		}
		private void Run()
		{
			Log($"Starting on port {_port} ({_slots.Count} players)");

			var arenaOpt = ArenaRegistry.Get(_arenaName);
			if (!arenaOpt.HasValue)
			{
				Console.WriteLine($"[Match:{_matchId}] Unknown arena '{_arenaName}' — aborting match.");
				Diagnostics.Log(_serverTick, "Arena unavailable; match aborted.", "error");
				Diagnostics.Complete();
				if (_steamSend is not null) Stop("content_unavailable");
				_onMatchEnd(_port);
				if (_steamSend is not null && Guid.TryParse(_matchId, out var arenaFailureMatchId))
					_onSteamMatchEnd?.Invoke(arenaFailureMatchId);
				return;
			}
			_arena = arenaOpt.Value;

			_sim = new ServerSimulation(_arena, _rule);
			for (int i = 0; i < _slots.Count; i++)
			{
				var slot = _slots[i];
				var def = slot.Content.Definition;
				_sim.RegisterEntity(slot.EntityId, def, CreateInitialState(def, i), slot.Content.BakedAnimation);
				// Respawn at the same distributed spawn point as initial spawn (issue #37).
				var respawnSpawn = PickSpawn(i);
				_sim.SetRespawnPosition(slot.EntityId, respawnSpawn.X, respawnSpawn.Y, respawnSpawn.Z, respawnSpawn.Yaw);
				Log($"Slot {i}: entity {slot.EntityId} = {slot.CharacterClass}");
			}

			if (_steamSend is null)
			{
				try
				{
					_udpServer = new UdpClient(_port);
					_udpServer.Client.Blocking = false;
					Log($"Listening on UDP {_port}, waiting for {_slots.Count} players...");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Match:{_matchId}] Error binding port {_port}: {ex.Message}");
					Diagnostics.Log(_serverTick, "Gameplay port binding failed.", "error");
					Diagnostics.Complete();
					_onMatchEnd(_port);
					return;
				}
			}
			else
				Log($"Waiting for Steam joins ({_slots.Count} players).");

			var stopwatch = Stopwatch.StartNew();
			double nextTickTime = 0;
			double nextDiagnosticTime = 5000;
			const double tickDurationMs = 1000.0 / 60.0;

			while (_running)
			{
				double currentTime = stopwatch.Elapsed.TotalMilliseconds;
				if (currentTime >= nextTickTime)
				{
					ReceiveInputs();
					if (_matchState == MatchState.Waiting && _admissionDeadlineUtc is DateTimeOffset deadline &&
						_clock.GetUtcNow() >= deadline)
					{
						Stop("unfilled");
						break;
					}
					if (_matchState == MatchState.Waiting && AllReady())
					{
						_matchState = MatchState.Countdown;
						_countdownTicks = CountdownDuration;
						Log($"All {_slots.Count} players ready — countdown started!");
					}
					if (_steamSend is not null && _matchState is MatchState.Countdown or MatchState.Playing)
					{
						int connected = ConnectedPlayerCount();
						if (connected <= 1)
						{
							var now = _clock.GetUtcNow();
							_noOpponentSinceUtc ??= now;
							if (now - _noOpponentSinceUtc.Value >= TimeSpan.FromSeconds(60))
							{
								Stop("absent");
								break;
							}
						}
						else _noOpponentSinceUtc = null;
					}
					if (_steamSend is null)
					{
						var now = DateTime.UtcNow;
						foreach (var slot in _slots)
						{
							if (slot.EndPoint == null || slot.Disconnected) continue;
							if ((now - slot.LastPacket).TotalSeconds > TimeoutSeconds)
							{
								slot.Disconnected = true;
								slot.ReadyConnectionId = 0;
								slot.Queue.Clear();
								Log($"Player (entity {slot.EntityId}) disconnected — entity goes idle.");
								Diagnostics.RecordPeer(slot.EntityId, false);
							}
						}
					}
					Tick();
					nextTickTime += tickDurationMs;

					bool reset = currentTime > nextTickTime + tickDurationMs * 10;
					Diagnostics.RecordTick(_serverTick, stopwatch.Elapsed.TotalMilliseconds - currentTime,
						Math.Max(0, currentTime - (nextTickTime - tickDurationMs)), reset);
					if (currentTime >= nextDiagnosticTime)
					{
						var metrics = Diagnostics.Snapshot();
						Log(FormattableString.Invariant($"Timing: mean={metrics.MeanTickMilliseconds:F2}ms max={metrics.MaxTickMilliseconds:F2}ms delayMax={metrics.MaxScheduleDelayMilliseconds:F2}ms slowTicks={metrics.SlowTicks} resets={metrics.ScheduleResets}"));
						nextDiagnosticTime = currentTime + 5000;
					}
					if (reset)
						nextTickTime = currentTime;
				}
				else
				{
					int sleepTime = (int)(nextTickTime - currentTime) - 1;
					if (sleepTime > 0)
						Thread.Sleep(sleepTime);
					else
						Thread.Yield();
				}
			}

			try { _udpServer?.Close(); } catch { }
			Log("Stopped.");
			Diagnostics.Complete();
			_onMatchEnd(_port);
			if (_steamSend is not null && Guid.TryParse(_matchId, out var matchGuid))
				_onSteamMatchEnd?.Invoke(matchGuid);
		}

		private bool AllReady()
		{
			lock (_steamGate)
			{
				foreach (var slot in _slots)
					if (_steamSend is null
						? slot.EndPoint == null || !slot.Ready || slot.Disconnected
						: !slot.SteamConnected || slot.ReadyConnectionId != slot.ConnectionId)
						return false;
				return true;
			}
		}

		private int ConnectedPlayerCount()
		{
			lock (_steamGate)
			{
				int count = 0;
				foreach (var slot in _slots)
					if (_steamSend is null ? slot.EndPoint != null && !slot.Disconnected : slot.SteamConnected)
						count++;
				return count;
			}
		}


		private CharacterState CreateInitialState(CharacterDefinition def, int spawnIndex)
		{
			var spawn = PickSpawn(spawnIndex);

			return new CharacterState
			{
				PX = spawn.X,
				PY = spawn.Y,
				PZ = spawn.Z,
				FacingYaw = spawn.Yaw,
				State = ActionState.Idle,
				IsGrounded = true,
				JumpsLeft = def.Movement.MaxJumps,
				AirDodgesLeft = 1,
				DamagePercent = 0,
			};
		}

		/// <summary>Distributed spawn: spawn point by slot index, falling back to a
		/// hardcoded default when the arena has fewer points than players.</summary>
		private SpawnPoint PickSpawn(int spawnIndex)
		{
			if (_arena.SpawnPoints.Length > spawnIndex)
				return _arena.SpawnPoints[spawnIndex];
			return new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = 0f };
		}

		private void ReceiveInputs()
		{
			if (_steamSend is not null)
			{
				while (_steamInputs.TryDequeue(out var queued))
				{
					Interlocked.Decrement(ref _steamInputCount);
					var slot = FindSlot(queued.EntityId);
					if (slot is null || queued.Tick <= _serverTick) continue;
					lock (_steamGate)
					{
						if (slot.ConnectionId != queued.ConnectionId || !slot.SteamConnected) continue;
						if (slot.LastInputConnectionId != queued.ConnectionId)
						{
							slot.Queue.Clear();
							slot.LastInput = default;
							slot.LastInputConnectionId = queued.ConnectionId;
						}
						slot.LastPacket = DateTime.UtcNow;
						if (slot.Disconnected)
						{
							slot.Disconnected = false;
							Console.WriteLine($"[Match:{_matchId}] Steam player (entity {slot.EntityId}) reconnected.");
						}
						slot.Queue.Push(queued.Tick, queued.Input);
					}
				}
				return;
			}
			if (_udpServer == null) return;

			while (true)
			{
				try
				{
					if (_udpServer.Available == 0) break;

					var remoteEP = new IPEndPoint(IPAddress.Any, 0);
					byte[] data = _udpServer.Receive(ref remoteEP);
					if (data.Length == 12 &&
						data[0] == (byte)'P' && data[1] == (byte)'I' &&
						data[2] == (byte)'N' && data[3] == (byte)'G')
					{
						var admitted = false;
						foreach (var admittedSlot in _slots)
							if (!admittedSlot.Disconnected && admittedSlot.EndPoint != null && admittedSlot.EndPoint.Equals(remoteEP))
							{
								admitted = true;
								admittedSlot.LastPacket = DateTime.UtcNow;
								break;
							}
						if (!admitted) continue;
						byte[] pong = new byte[16];
						pong[0] = (byte)'P'; pong[1] = (byte)'O';
						pong[2] = (byte)'N'; pong[3] = (byte)'G';
						data.AsSpan(4, 8).CopyTo(pong.AsSpan(4, 8));
						BinaryPrimitives.WriteUInt32LittleEndian(pong.AsSpan(12, 4), _serverTick);
						_udpServer.Send(pong, pong.Length, remoteEP);
						continue;
					}
					if (NetplayControlPacket.TryDeserialize(data, out var control))
					{
						var controlSlot = FindSlot(control.EntityId);
						if (controlSlot == null) continue;
						if (control.Kind == NetplayControlKind.Bootstrap)
						{
							if (controlSlot.EndPoint != null && !controlSlot.Disconnected &&
								!controlSlot.EndPoint.Equals(remoteEP))
								continue;
							if (controlSlot.EndPoint == null || !controlSlot.EndPoint.Equals(remoteEP))
							{
								controlSlot.Queue.Clear();
								controlSlot.LastInput = default;
								controlSlot.MissingInputTicks = 0;
								controlSlot.Ready = false;
							}
							controlSlot.EndPoint = remoteEP;
							controlSlot.Disconnected = false;
							controlSlot.LastPacket = DateTime.UtcNow;
						}
						else if (control.Kind == NetplayControlKind.Ready &&
							control.Tick <= _serverTick && control.StartTick == _startTick &&
							controlSlot.EndPoint?.Equals(remoteEP) == true && !controlSlot.Disconnected)
						{
							controlSlot.Ready = true;
							controlSlot.LastPacket = DateTime.UtcNow;
						}
						continue;
					}

					if (data.Length != 8 + 4 + InputState.Size) continue;
					ulong entityId = BitConverter.ToUInt64(data, 0);
					uint clientTick = BitConverter.ToUInt32(data, 8);
					InputState inputState;
					try { inputState = InputState.Deserialize(data.AsSpan(12, InputState.Size)); }
					catch (ArgumentException) { continue; }
					catch (InvalidDataException) { continue; }
					var slot = FindSlot(entityId);
					if (slot == null || slot.Disconnected || !slot.Ready || slot.EndPoint?.Equals(remoteEP) != true) continue;
					slot.LastPacket = DateTime.UtcNow;
					if (clientTick <= _serverTick || clientTick - _serverTick > NetplayClock.HistoryTicks) continue;
					slot.Queue.Push(clientTick, inputState);
				}
				catch (SocketException ex)
				{
					if (ex.SocketErrorCode != SocketError.WouldBlock)
					{
						Console.WriteLine($"[Match:{_matchId}] Socket error: {ex.Message}");
						Diagnostics.ReportError(_serverTick, "Gameplay socket receive failed.");
					}
					break;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Match:{_matchId}] Receive error: {ex.Message}");
					Diagnostics.ReportError(_serverTick, "Gameplay receive failed.");
					break;
				}
			}
		}

		private PlayerSlot? FindSlot(ulong entityId)
		{
			foreach (var s in _slots)
				if (s.EntityId == entityId) return s;
			return null;
		}

		private void Tick()
		{
			if (_matchState == MatchState.Waiting)
			{
				SendState();
				return;
			}

			if (_matchState == MatchState.Countdown)
			{
				_serverTick++;
				if (--_countdownTicks == 0)
				{
					_matchState = MatchState.Playing;
					_startTick = _serverTick + 1;
					Log("GO!");
				}
				SendState();
				return;
			}

			if (_matchState == MatchState.Ended)
			{
				if (--_postMatchTicks == 0)
				{
					Log("Post-match complete — stopping.");
					_running = false;
				}
				else SendState();
				return;
			}

			_tickInputs.Clear();
			var inputs = _tickInputs;
			uint targetTick = _serverTick + 1;
			foreach (var slot in _slots)
			{
				lock (_steamGate)
				{
					if (slot.Disconnected || _rule.IsEliminated(_sim.GetState(slot.EntityId)))
					{
						slot.Queue.Clear();
						slot.LastInput = default;
						slot.MissingInputTicks = 0;
						continue;
					}

					slot.Queue.Prune(_serverTick);
					InputState input;
					if (slot.Queue.TryTake(targetTick, out var queuedInput))
					{
						input = queuedInput;
						slot.MissingInputTicks = 0;
					}
					else if (slot.MissingInputTicks++ < 6)
					{
						input = slot.LastInput;
						input.Jump = false;
						input.FaceToCamera = false;
						input.ToggleLock = false;
						input.DownPressed = false;
						input.ShieldPressed = false;
						input.GrabPressed = false;
						input.RetargetPressed = false;
						input.ActiveSlot = 0;
					}
					else
					{
						input = default;
						slot.LastInput = default;
					}

					slot.LastInput = input;
					inputs[slot.EntityId] = input;
				}
			}

			_lastTickInputs = inputs;
			_sim.SetTick(_serverTick);
			_serverTick = targetTick;
			_sim.Tick(inputs);
			var outcome = _rule.Evaluate(_sim.GetAllStates());
			if (outcome.IsEnded)
			{
				_matchState = MatchState.Ended;
				_winnerEntityId = outcome.WinnerEntityId;
				_matchResultPacket = BuildMatchResultPacket(outcome);
				_postMatchTicks = PostMatchDuration;
				Log(outcome.IsSharedVictory
					? "Shared victory — all players eliminated simultaneously."
					: $"Winner: {_winnerEntityId}");

				if (_onMatchResult != null && Guid.TryParse(_matchId, out var matchGuid))
				{
					long winnerSteamId = 0;
					var winnerSlot = FindSlot(_winnerEntityId);
					if (winnerSlot != null) winnerSteamId = winnerSlot.SteamId;
					_onMatchResult(matchGuid, winnerSteamId);
				}
			}
			SendState();
		}


		private void SendState()
		{
			if (_udpServer == null && _steamSend is null) return;

			var packets = new List<(byte[] buffer, int length)>(_slots.Count);
			foreach (var slot in _slots)
			{
				var statePacket = CharacterStatePacket.FromState(_sim.GetState(slot.EntityId), _serverTick);
				statePacket.MatchState = _matchState;

				InputState consumed = default;
				bool hasInput = _lastTickInputs != null && _lastTickInputs.TryGetValue(slot.EntityId, out consumed);
				var packet = new ServerEntityPacket
				{
					EntityId = slot.EntityId,
					Tick = _serverTick,
					State = statePacket,
					HasInput = hasInput,
					Input = consumed,
				};

				int offset = _steamSend is null ? 0 : 1;
				var buf = new byte[packet.WireSize + offset];
				if (offset != 0) buf[0] = SteamGameplayWire.State;
				packet.Serialize(buf.AsSpan(offset));
				packets.Add((buf, packet.WireSize + offset));
			}

			var presentationPackets = new List<(byte[] buffer, int length)>();
			foreach (var evt in _sim.GetPresentationEvents(clear: true))
			{
				var eventPacket = new PresentationEventPacket(
					evt.MatchTick, evt.EntityId, evt.OperationIndex, evt.PresentationId,
					evt.AttackSequence, evt.Source, evt.WorldX, evt.WorldY, evt.WorldZ, evt.WorldYaw,
					evt.Placement);
				int offset = _steamSend is null ? 0 : 1;
				var eventBuffer = new byte[eventPacket.WireSize + offset];
				if (offset != 0) eventBuffer[0] = SteamGameplayWire.Event;
				eventPacket.Serialize(eventBuffer.AsSpan(offset));
				presentationPackets.Add((eventBuffer, eventBuffer.Length));
			}

            byte[]? swordTrailBuffer = null;
            if (_hasWibou)
            {
                _swordTrailOwners.Clear();
                if (_matchState == MatchState.Playing)
                {
                    foreach (var hitbox in _sim.Resolver.GetActiveHitboxes())
                    {
                        if (!hitbox.Active || hitbox.SourceEvent.BoneName != "_weapon_hilt"
                            || hitbox.SourceEvent.EndBoneName != "_weapon_tip"
                            || _sim.GetDefinition(hitbox.OwnerId)?.Class != CharacterClass.Wibou
                            || _swordTrailOwners.Contains(hitbox.OwnerId))
                            continue;
                        _swordTrailOwners.Add(hitbox.OwnerId);
                    }
                }
                var snapshot = new SwordTrailSnapshotPacket(_serverTick, _swordTrailOwners);
                int offset = _steamSend is null ? 0 : 1;
                swordTrailBuffer = new byte[snapshot.WireSize + offset];
                if (offset != 0) swordTrailBuffer[0] = SteamGameplayWire.SwordTrail;
                snapshot.Serialize(swordTrailBuffer.AsSpan(offset));
            }

            byte[]? projectileBuffer = null;
            if (_hasProjectileVisuals)
            {
                _projectileVisuals.Clear();
                foreach (var hitbox in _sim.Resolver.GetActiveHitboxes())
                {
                    var character = _sim.GetDefinition(hitbox.OwnerId)?.Class ?? CharacterClass.None;
                    if (_matchState != MatchState.Playing || hitbox.AttackSlot != AbilitySlots.A || !hitbox.Active
                        || character is not (CharacterClass.Wibou or CharacterClass.FightGuy))
                        continue;
                    _projectileVisuals.Add(new ProjectileVisualState(
                        hitbox.OwnerId, hitbox.ActivationId, hitbox.VisualOperationIndex,
                        character, hitbox.AttackSlot, hitbox.ActivationAirborne,
                        hitbox.X, hitbox.Y, hitbox.Z, hitbox.VX, hitbox.VY, hitbox.VZ, hitbox.Radius));
                }
                var snapshot = new ProjectileVisualPacket(_serverTick, _projectileVisuals);
                int offset = _steamSend is null ? 0 : 1;
                projectileBuffer = new byte[snapshot.WireSize + offset];
                if (offset != 0) projectileBuffer[0] = SteamGameplayWire.Projectile;
                snapshot.Serialize(projectileBuffer.AsSpan(offset));
            }

			byte[]? steamResult = null;
			if (_steamSend is not null && _matchState == MatchState.Ended && _matchResultPacket != null && !_steamResultSent)
			{
				steamResult = new byte[_matchResultPacket.WireSize + 1];
				steamResult[0] = SteamGameplayWire.Result;
				_matchResultPacket.Serialize(steamResult.AsSpan(1));
				_steamResultSent = true;
			}

			try
			{
				foreach (var slot in _slots)
				{
					bool connected = _steamSend is null
						? slot.EndPoint != null && !slot.Disconnected
						: slot.SteamConnected;
					if (!connected) continue;
					foreach (var pkt in packets)
					{
						if (_steamSend is null) _udpServer!.Send(pkt.buffer, pkt.length, slot.EndPoint!);
						else _steamSend(_steamMatchGuid, slot.EntityId, pkt.buffer, false);
					}

					if (_matchState == MatchState.Ended && _matchResultPacket != null)
					{
						if (_steamSend is null)
						{
							var resultBuffer = new byte[_matchResultPacket.WireSize];
							_matchResultPacket.Serialize(resultBuffer);
							_udpServer!.Send(resultBuffer, resultBuffer.Length, slot.EndPoint!);
						}
						else if (steamResult is not null)
							_steamSend(_steamMatchGuid, slot.EntityId, steamResult, true);
					}
					bool ready = _steamSend is null ? slot.Ready : slot.ReadyConnectionId == slot.ConnectionId;
					if (!ready || _serverTick % 6 == 0)
						SendNetplayControl(slot, ready ? NetplayControlKind.Clock : NetplayControlKind.Bootstrap);
					foreach (var evt in presentationPackets)
					{
						if (_steamSend is null) _udpServer!.Send(evt.buffer, evt.length, slot.EndPoint!);
						else _steamSend(_steamMatchGuid, slot.EntityId, evt.buffer, false);
					}
                    if (projectileBuffer != null)
                    {
                        if (_steamSend is null) _udpServer!.Send(projectileBuffer, projectileBuffer.Length, slot.EndPoint!);
                        else _steamSend(_steamMatchGuid, slot.EntityId, projectileBuffer, false);
                    }
                    if (swordTrailBuffer != null)
                    {
                        if (_steamSend is null) _udpServer!.Send(swordTrailBuffer, swordTrailBuffer.Length, slot.EndPoint!);
                        else _steamSend(_steamMatchGuid, slot.EntityId, swordTrailBuffer, false);
                    }
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Match:{_matchId}] Send error: {ex.Message}");
				Diagnostics.ReportError(_serverTick, "Gameplay send failed.");
			}
		}
		private void SendNetplayControl(PlayerSlot slot, NetplayControlKind kind)
		{
			var control = new NetplayControlPacket(kind, slot.EntityId, _serverTick, _startTick);
			int offset = _steamSend is null ? 0 : 1;
			var frame = new byte[NetplayControlPacket.Size + offset];
			if (offset != 0) frame[0] = SteamGameplayWire.Control;
			control.Serialize(frame.AsSpan(offset));
			if (_steamSend is null)
				_udpServer!.Send(frame, frame.Length, slot.EndPoint!);
			else
				_steamSend(_steamMatchGuid, slot.EntityId, frame, true);
		}



		private MatchResultPacket BuildMatchResultPacket(MatchOutcome outcome)
		{
			var entries = new List<MatchResultEntry>(_slots.Count);
			foreach (var slot in _slots)
			{
				var state = _sim.GetState(slot.EntityId);
				entries.Add(new MatchResultEntry(
					slot.EntityId,
					placement: 0,
					_sim.GetKOs(slot.EntityId),
					state.Deaths));
			}

			entries.Sort((a, b) =>
			{
				int winnerOrder = outcome.IsSharedVictory
					? 0
					: (a.EntityId == outcome.WinnerEntityId ? -1 : b.EntityId == outcome.WinnerEntityId ? 1 : 0);
				if (winnerOrder != 0) return winnerOrder;

				int byFalls = a.Falls.CompareTo(b.Falls);
				if (byFalls != 0) return byFalls;
				int byKOs = b.KOs.CompareTo(a.KOs);
				return byKOs != 0 ? byKOs : a.EntityId.CompareTo(b.EntityId);
			});

			var ranked = new MatchResultEntry[entries.Count];
			for (int i = 0; i < entries.Count; i++)
			{
				var entry = entries[i];
				ranked[i] = new MatchResultEntry(entry.EntityId, (byte)(i + 1), entry.KOs, entry.Falls);
			}

			return new MatchResultPacket(_serverTick, outcome.IsSharedVictory, ranked);
		}


		/// <summary>
		/// Per-player state held outside the simulation: the client's UDP endpoint,
		/// its input queue, and the last-seen packet time for timeout detection.
		/// </summary>
		private sealed class PlayerSlot
		{
			public ulong EntityId { get; }
			public CharacterClass CharacterClass { get; }
			public long SteamId { get; }
			public MatchContentEntry Content { get; }
			public IPEndPoint? EndPoint { get; set; }
			public DateTime LastPacket { get; set; } = DateTime.UtcNow;
			private volatile bool _disconnected;
			private volatile bool _steamConnected;
			public bool Disconnected { get => _disconnected; set => _disconnected = value; }
			public bool SteamConnected { get => _steamConnected; set => _steamConnected = value; }
			public long ConnectionId { get; set; }
			public long LastInputConnectionId { get; set; }
			public long ReadyConnectionId { get; set; }
			public bool Ready { get; set; }
			public int MissingInputTicks { get; set; }
			public TickInputBuffer Queue { get; } = new();
			public InputState LastInput;

			public PlayerSlot(ulong entityId, CharacterClass characterClass, long steamId, MatchContentEntry content)
			{
				EntityId = entityId;
				CharacterClass = characterClass;
				SteamId = steamId;
				Content = content;
			}
		}
	}
}
