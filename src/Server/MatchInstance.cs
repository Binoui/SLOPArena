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
		private readonly MatchContentCatalog _contentCatalog;
		private readonly List<PlayerSlot> _slots;
		/// <summary>Recent events and hits; each World Frame carries the last three ticks of them.</summary>
		private readonly WorldFrameHistory _frameHistory = new();
		private readonly List<RelayedInput> _relayedInputs = new();
		private UdpClient? _udpServer;
		private volatile bool _running = true;

		private readonly ArenaDefinition _arena;
		private ServerSimulation _sim = null!;
		private uint _serverTick;
		private uint _startTick;

		/// <summary>
		/// The inputs the server actually consumed for the last sim tick, keyed by
		/// entity. Sent back to clients as the InputRelay of each World Frame (ADR-0039):
		/// membership in this dict is the relay signal, so eliminated entities and
		/// disconnected players relay no input. Null until the first playing tick
		/// (countdown frames relay neutral input for connected players).
		/// </summary>
		private Dictionary<ulong, InputState>? _lastTickInputs;
		private readonly Dictionary<ulong, InputState> _relayInputs = new();
		private readonly Dictionary<ulong, InputState> _tickInputs = new();

		private const double TimeoutSeconds = 5.0;

		// Match lifecycle
		private volatile MatchState _matchState = MatchState.Waiting;
		private ushort _countdownTicks;
		private const ushort CountdownDuration = 300; // 5 seconds at 60Hz
		private readonly IMatchRule _rule;
		private readonly MatchFormat _format;
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
		private const int MinimumLoggedInputGap = 3;
		private readonly List<string> _inputGapLogs = new();
		private int _steamInputCount;
		private DateTimeOffset? _noOpponentSinceUtc;
		private readonly TimeProvider _clock;
		private int _cancelNotified;
		private bool _steamResultSent;

		private readonly record struct SteamInput(long ConnectionId, ulong EntityId, uint NewestTick, InputState[] Inputs);
		private readonly ConcurrentQueue<SteamInput> _steamInputs = new();
		private readonly Action? _steamFlush;
		private readonly InputState[] _udpBundle = new InputState[InputBundle.MaxInputs];
		/// <summary>Match-loop failure reported to Master; distinct from host restarts.</summary>
		public const string MatchFailedReason = "match_failed";

		/// <param name="roster">Ordered players (index 0 = host). Each carries an entity ID (1..N) and a character class.</param>
		/// <param name="maxStocks">Stocks per player (default 3, issue #37).</param>
		/// <param name="onMatchResult">Optional callback invoked once when the match ends (match guid, winner steam id).</param>
		public MatchInstance(int port, string matchId, ArenaDefinition arena,
			IReadOnlyList<MatchPlayer> roster, MatchContentCatalog contentCatalog, Action<int> onMatchEnd,
			byte maxStocks = MatchDefaults.DefaultMaxStocks, Action<Guid, long>? onMatchResult = null,
			Action<Guid>? onSteamMatchEnd = null, Action<Guid, string>? onMatchCancelled = null,
			DateTimeOffset? admissionDeadlineUtc = null, string? contentHash = null,
			Action<Guid, ulong, byte[], bool>? steamSend = null, TimeProvider? clock = null,
			MatchContentHandleMap? contentHandleMap = null, Action? steamFlush = null,
			MatchFormat format = MatchFormat.Stock, bool friendlyFire = false, bool ballOnly = false)
		{
			_steamFlush = steamFlush;
			_format = format;
			_port = port;
			_matchId = matchId;
			_arena = arena;
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
			Diagnostics = new MatchDiagnostics(matchId, arena.Name, steamSend is null ? "development-udp" : "steam-p2p", _clock);
			_contentHandleMap = contentHandleMap;
			_rule = format.CreateRule(maxStocks, friendlyFire,
                roster.ToDictionary(p => (ulong)p.EntityId, p => p.Camp), ballOnly);

			_slots = new List<PlayerSlot>(roster.Count);
			foreach (var p in roster)
			{
				var content = _contentCatalog.Resolve(p.CharacterClass)
					?? throw new InvalidDataException($"Roster selector '{p.CharacterClass}' is not in the match content catalog.");
				_slots.Add(new PlayerSlot((ulong)p.EntityId, p.CharacterClass, p.SteamId, content));
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
				slot.Inputs.Reset();
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
						slot.Inputs.Reset();
						slot.LastInputConnectionId = 0;
						Diagnostics.RecordPeer(slot.EntityId, false);
						Log($"Entity {slot.EntityId} Steam connection closed.");
						return;
					}
			}
		}

		/// <summary>Queue a Ready peer's input bundle; arrival timing is classified on the match thread.</summary>
		public bool TryQueueSteamInput(long connectionId, uint newestTick, ReadOnlySpan<InputState> inputs)
		{
			if (_steamSend is null || connectionId <= 0 || !_running || inputs.Length is < 1 or > InputBundle.MaxInputs)
				return false;
			uint serverTick = _serverTick;
			if (newestTick > serverTick && newestTick - serverTick > NetplayClock.HistoryTicks)
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
						_steamInputs.Enqueue(new SteamInput(connectionId, slot.EntityId, newestTick, inputs.ToArray()));
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
			if (_format == MatchFormat.SlopballCoop)
			{
				// Snapshots carry the seed, so clients serve the same sequence without being told.
				_sim.BeginSlopballRun((uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue));
				Log($"Co-op Slopball run: {_slots.Count} fighters, unlimited misses.");
			}
            if (_format == MatchFormat.SlopballVersus) _sim.BeginVersusSlopball();
            if (_format.IsCourse()) _sim.BeginOnlineCourse();

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
					try
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
									slot.Inputs.Reset();
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
							if (_matchState == MatchState.Playing)
								LogInputWindow();
							nextDiagnosticTime = currentTime + 5000;
						}
						if (reset)
							nextTickTime = currentTime;
					}
					catch (Exception ex)
					{
						// One match's failure must not terminate the process and every other match.
						Console.WriteLine($"[Match:{_matchId}] Match loop failed: {ex}");
						Diagnostics.Log(_serverTick, $"Match loop failed: {ex.GetType().Name}: {ex.Message}", "error");
						Stop(MatchFailedReason);
						break;
					}
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

		/// <summary>
		/// Record a finished run of ticks simulated without the peer's input. Late bundles that
		/// arrived during the run show the inputs were delayed in transit (worst slack = how
		/// many ticks too late); none means they were lost or never sent.
		/// </summary>
		private void EndInputGap(PlayerSlot slot)
		{
			int length = slot.GapHeld + slot.GapNeutral;
			if (length >= MinimumLoggedInputGap)
			{
				string arrival = slot.GapLateBundles > 0
					? FormattableString.Invariant($"{slot.GapLateBundles} late bundles, worst {slot.GapWorstSlack} ticks")
					: "no late bundles (lost or never sent)";
				string rescued = slot.GapRescued > 0
					? FormattableString.Invariant($"; {slot.GapRescued} late presses applied")
					: "";
				_inputGapLogs.Add(FormattableString.Invariant(
					$"Input gap entity {slot.EntityId}: {length} ticks from tick {slot.GapStartTick} (held {slot.GapHeld}, neutral {slot.GapNeutral}); {arrival}{rescued}"));
			}
			slot.ResetInputGap();
		}

		/// <summary>Per-peer input arrival since the previous diagnostics line (slack 1 = just in time).</summary>
		private void LogInputWindow()
		{
			foreach (var slot in _slots)
			{
				string slack = slot.WindowMinSlack == int.MaxValue
					? "none"
					: slot.WindowMinSlack.ToString(System.Globalization.CultureInfo.InvariantCulture);
				Log(FormattableString.Invariant(
					$"Inputs entity {slot.EntityId}: bundles={slot.WindowBundles} late={slot.WindowLate} early={slot.WindowEarly} held={slot.WindowHeld} neutral={slot.WindowNeutral} rescued={slot.WindowRescued} dropped={slot.WindowDroppedPresses} minSlack={slack}"));
				slot.WindowBundles = slot.WindowLate = slot.WindowEarly = slot.WindowHeld = slot.WindowNeutral = 0;
				slot.WindowRescued = slot.WindowDroppedPresses = 0;
				slot.WindowMinSlack = int.MaxValue;
			}
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
		/// hardcoded default when the arena has fewer points than players. Arcade
		/// formats place fighters themselves (co-op Slopball spreads the court's start).</summary>
		private SpawnPoint PickSpawn(int spawnIndex)
		{
			if (_format != MatchFormat.Stock)
				return _format.StartFor(_arena, spawnIndex, _slots.Count,
                    _slots.Select(s => (_rule as SlopballVersusMatchRule)?.CampOf(s.EntityId) ?? SlopballCamp.None).ToArray());
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
					if (slot is null) continue;
					lock (_steamGate)
					{
						if (slot.ConnectionId != queued.ConnectionId || !slot.SteamConnected) continue;
						if (slot.LastInputConnectionId != queued.ConnectionId)
						{
							slot.Inputs.Reset();
							slot.LastInputConnectionId = queued.ConnectionId;
						}
						slot.LastPacket = DateTime.UtcNow;
						if (slot.Disconnected)
						{
							slot.Disconnected = false;
							Console.WriteLine($"[Match:{_matchId}] Steam player (entity {slot.EntityId}) reconnected.");
						}
						AcceptInputBundle(slot, queued.NewestTick, queued.Inputs);
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
								controlSlot.Inputs.Reset();
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

					if (!InputBundle.TryRead(data, out ulong entityId, out uint newestTick, _udpBundle, out int count))
						continue;
					var slot = FindSlot(entityId);
					if (slot == null || slot.Disconnected || !slot.Ready || slot.EndPoint?.Equals(remoteEP) != true) continue;
					slot.LastPacket = DateTime.UtcNow;
					AcceptInputBundle(slot, newestTick, _udpBundle.AsSpan(0, count));
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

		/// <summary>
		/// Record how early the bundle's newest input arrived (slack 1 = just in time) and hand
		/// its redundant ticks to the peer's input timeline: ticks ahead of the simulation
		/// queue, late ticks refresh the held controls and may rescue their presses.
		/// </summary>
		private void AcceptInputBundle(PlayerSlot slot, uint newestTick, ReadOnlySpan<InputState> inputs)
		{
			long slack = (long)newestTick - _serverTick;
			slot.WindowBundles++;
			if (slack < 1)
			{
				slot.IntervalLate++;
				slot.WindowLate++;
				if (slot.MissingInputTicks > 0)
				{
					slot.GapLateBundles++;
					slot.GapWorstSlack = (int)Math.Min(slot.GapWorstSlack, slack);
				}
			}
			else if (slack > NetplayClock.HistoryTicks)
			{
				slot.WindowEarly++;
				return;
			}
			else
			{
				slot.IntervalMinSlack = Math.Min(slot.IntervalMinSlack, (int)slack);
				slot.WindowMinSlack = Math.Min(slot.WindowMinSlack, (int)slack);
			}
			var late = slot.Inputs.Accept(_serverTick, newestTick, inputs, NetplayClock.HistoryTicks);
			slot.WindowDroppedPresses += late.DroppedPresses;
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
				// Nothing is simulated during the countdown, but frames carry the server tick so
				// clients anchor on it; the GO frame (StartTick − 1) aligns their prediction.
				_sim.SetTick(_serverTick);
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
					if (slot.Disconnected) _sim.WithdrawCoursePlayer(slot.EntityId);
                    if (slot.Disconnected || _sim.IsEntityInactive(slot.EntityId))
					{
						slot.Inputs.Reset();
						slot.MissingInputTicks = 0;
						slot.ResetInputGap();
						continue;
					}

					var consumed = slot.Inputs.Consume(targetTick);
					slot.WindowRescued += consumed.RescuedPresses;
					slot.GapRescued += consumed.RescuedPresses;
					if (consumed.Source == ConsumedInputSource.Received)
					{
						if (slot.MissingInputTicks > 0)
							EndInputGap(slot);
						slot.MissingInputTicks = 0;
					}
					else
					{
						if (slot.MissingInputTicks++ == 0)
							slot.GapStartTick = targetTick;
						if (consumed.Source == ConsumedInputSource.Held)
						{
							slot.WindowHeld++;
							slot.GapHeld++;
						}
						else
						{
							slot.WindowNeutral++;
							slot.GapNeutral++;
						}
					}
					inputs[slot.EntityId] = consumed.Input;
				}
			}

			foreach (var message in _inputGapLogs)
				Log(message);
			_inputGapLogs.Clear();

			// Relay the inputs as consumed, before the simulation clears presses it acted on.
			_relayInputs.Clear();
			foreach (var (entityId, consumedInput) in inputs)
				_relayInputs[entityId] = consumedInput;
			_lastTickInputs = _relayInputs;
			_sim.SetTick(_serverTick);
			_serverTick = targetTick;
			_sim.Tick(inputs);
			int droppedEvents = _frameHistory.DroppedEvents;
			_frameHistory.Record(_serverTick, _sim.GetPresentationEvents(clear: true), _sim.LastTickHits,
				_sim.LastTickSlopballEvents, _sim.LastTickOnlineCourseEvents);
			if (_frameHistory.DroppedEvents != droppedEvents)
				// Cosmetic only: an unencodable event must not cost the match.
				Diagnostics.ReportError(_serverTick, "Unencodable presentation event dropped.");
			var outcome = _sim.EvaluateMatch();
			if (outcome.IsEnded)
			{
				_matchState = MatchState.Ended;
				_winnerEntityId = outcome.WinnerEntityId;
				_matchResultPacket = BuildMatchResultPacket(outcome);
				_postMatchTicks = PostMatchDuration;
				Log(_format == MatchFormat.SlopballCoop && _sim.Slopball is { } run
					? $"Co-op run over: {run.TotalReturns} returns, best combo {run.BestStreak}."
					: outcome.IsSharedVictory
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

			// One World Frame per tick, the same bytes for every peer (ADR-0039).
			byte[] frame = BuildWorldFrame();
			byte[]? steamFrame = null;
			if (_steamSend is not null)
			{
				steamFrame = new byte[frame.Length + 1];
				steamFrame[0] = SteamGameplayWire.Frame;
				frame.CopyTo(steamFrame, 1);
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
					if (_steamSend is null) _udpServer!.Send(frame, frame.Length, slot.EndPoint!);
					else _steamSend(_steamMatchGuid, slot.EntityId, steamFrame!, false);

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
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Match:{_matchId}] Send error: {ex.Message}");
				Diagnostics.ReportError(_serverTick, "Gameplay send failed.");
			}
			// The whole tick is queued: let the Steam host send it now as one flushed burst.
			_steamFlush?.Invoke();
		}

		/// <summary>
		/// The World Snapshot after the current tick, the input each fighter consumed during it
		/// and the last three ticks' events and hits. Before the first playing tick, connected
		/// players relay neutral input: it is what they consume if their first input is late.
		/// </summary>
		private byte[] BuildWorldFrame()
		{
			_relayedInputs.Clear();
			foreach (var slot in _slots)
			{
				bool hasInput;
				InputState input = default;
				if (_lastTickInputs != null)
					hasInput = _lastTickInputs.TryGetValue(slot.EntityId, out input);
				else
					hasInput = !slot.Disconnected && !_sim.IsEntityInactive(slot.EntityId);
				_relayedInputs.Add(new RelayedInput(slot.EntityId, hasInput, input));
			}
			_relayedInputs.Sort(static (a, b) => a.EntityId.CompareTo(b.EntityId));
			var snapshot = _sim.CaptureSnapshot();
			return new WorldFramePacket(_matchState, snapshot, _relayedInputs,
				_frameHistory.EventsForFrame(snapshot.Tick), _frameHistory.HitsForFrame(snapshot.Tick),
				_frameHistory.SlopballEventsForFrame(snapshot.Tick), _frameHistory.CourseEventsForFrame(snapshot.Tick)).Serialize();
		}

		private void SendNetplayControl(PlayerSlot slot, NetplayControlKind kind)
		{
			var control = new NetplayControlPacket(kind, slot.EntityId, _serverTick, _startTick);
			if (kind == NetplayControlKind.Clock)
			{
				// Report this peer's input arrival since the previous Clock so it can size its lead.
				control = control with
				{
					InputSlack = slot.IntervalMinSlack == int.MaxValue
						? NetplayControlPacket.NoInputSlack
						: (sbyte)Math.Clamp(slot.IntervalMinSlack, -127, NetplayControlPacket.NoInputSlack - 1),
					LateInputs = (byte)Math.Min(slot.IntervalLate, byte.MaxValue),
				};
				slot.IntervalMinSlack = int.MaxValue;
				slot.IntervalLate = 0;
			}
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
				if (_sim.SlopballVersus is { } score)
                {
                    int campOrder = (_sim.CampOf(b.EntityId) == score.Winner ? 1 : 0)
                        .CompareTo(_sim.CampOf(a.EntityId) == score.Winner ? 1 : 0);
                    return campOrder != 0 ? campOrder : a.EntityId.CompareTo(b.EntityId);
                }
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
				ranked[i] = new MatchResultEntry(entry.EntityId,
                    _sim.SlopballVersus is { } volley
                        ? (byte)(_sim.CampOf(entry.EntityId) == volley.Winner ? 1 : 2) : (byte)(i + 1),
                    entry.KOs, entry.Falls);
			}

			SlopballRunResult? run = _sim.Slopball is { } slopball && _format == MatchFormat.SlopballCoop
				? new SlopballRunResult((uint)Math.Min(slopball.TotalReturns, uint.MaxValue),
					(uint)Math.Min(slopball.BestStreak, uint.MaxValue), slopball.Misses)
				: null;
			var versus = _sim.SlopballVersus is { } score
                ? new SlopballVersusResult(score.TealScore, score.ClayScore, score.Winner) : (SlopballVersusResult?)null;
            CourseRunResult? course = _sim.OnlineCourse.HasValue ? CourseRunResult.From(_sim) : null;
            if (course != null)
            {
                ranked = entries.Select(entry => new MatchResultEntry(entry.EntityId,
                    course.Players.Single(p => p.EntityId == entry.EntityId).Place, entry.KOs, entry.Falls)).ToArray();
                Array.Sort(ranked, (a, b) =>
                {
                    int order = (a.Placement == 0 ? 255 : a.Placement).CompareTo(b.Placement == 0 ? 255 : b.Placement);
                    return order != 0 ? order : a.EntityId.CompareTo(b.EntityId);
                });
            }
            return new MatchResultPacket(_serverTick, outcome.IsSharedVictory, ranked, run, versus, course);
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
			/// <summary>Queued input, held controls and late-press rescue (ADR-0039 decision 5).</summary>
			public PeerInputTimeline Inputs { get; } = new();
			// Arrival report for the next Clock control (match thread only).
			public int IntervalMinSlack = int.MaxValue;
			public int IntervalLate;
			// Diagnostics window, logged with the periodic timing line (match thread only).
			public int WindowBundles, WindowLate, WindowEarly, WindowHeld, WindowNeutral;
			public int WindowRescued, WindowDroppedPresses;
			public int WindowMinSlack = int.MaxValue;
			// Current run of ticks simulated without this peer's input (match thread only).
			public uint GapStartTick;
			public int GapHeld, GapNeutral, GapLateBundles, GapRescued;
			public int GapWorstSlack = int.MaxValue;

			public void ResetInputGap()
			{
				GapStartTick = 0;
				GapHeld = GapNeutral = GapLateBundles = GapRescued = 0;
				GapWorstSlack = int.MaxValue;
			}

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
