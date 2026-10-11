namespace SlopArena.Server;

public sealed record MatchLogEntry(long Sequence, DateTimeOffset AtUtc, uint Tick, string Level, string Message);
public sealed record MatchPeerHealth(string EntityId, bool Connected, DateTimeOffset SampledAtUtc,
    int? PingMilliseconds, float? ReceiveQuality, float? SendQuality);
public sealed record MatchDiagnosticsSnapshot(string MatchId, string Arena, string Transport,
    DateTimeOffset CapturedAtUtc, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc,
    uint ServerTick, long TickSamples, double MeanTickMilliseconds, double MaxTickMilliseconds,
    double MaxScheduleDelayMilliseconds, long SlowTicks, long ScheduleResets,
    int EventLimit, long DroppedEvents, IReadOnlyList<MatchPeerHealth> Peers, IReadOnlyList<MatchLogEntry> Events);

/// <summary>Safe match events and measurements, never raw request bodies or credentials.</summary>
public sealed class MatchDiagnostics
{
    public const int EventLimit = 256;
    private readonly object _gate = new();
    private readonly Queue<MatchLogEntry> _events = new();
    private readonly Dictionary<ulong, MatchPeerHealth> _peers = new();
    private readonly Dictionary<ulong, DateTimeOffset> _lastPeerEvent = new();
    private readonly Dictionary<string, DateTimeOffset> _lastErrorEvent = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _startedAt;
    private DateTimeOffset? _endedAt;
    private uint _tick;
    private long _sequence, _samples, _slowTicks, _resets;
    private double _totalMs, _maxMs, _maxDelay;
    public string MatchId { get; }
    public string Arena { get; }
    public string Transport { get; }
    public DateTimeOffset? EndedAtUtc { get { lock (_gate) return _endedAt; } }

    public MatchDiagnostics(string matchId, string arena, string transport, TimeProvider? clock = null)
    {
        MatchId = matchId; Arena = arena; Transport = transport;
        _clock = clock ?? TimeProvider.System;
        _startedAt = _clock.GetUtcNow();
    }

    public void Log(uint tick, string message, string level = "info")
    {
        lock (_gate)
        {
            if (_events.Count == EventLimit) _events.Dequeue();
            _events.Enqueue(new(++_sequence, _clock.GetUtcNow(), tick, level,
                message.Length > 512 ? message[..512] : message));
        }
    }

    public void RecordTick(uint tick, double elapsedMs, double delayMs, bool scheduleReset)
    {
        lock (_gate)
        {
            _tick = tick; _samples++; _totalMs += elapsedMs;
            _maxMs = Math.Max(_maxMs, elapsedMs);
            _maxDelay = Math.Max(_maxDelay, delayMs);
            if (elapsedMs > 1000d / 60) _slowTicks++;
            if (scheduleReset) _resets++;
        }
    }

    /// <summary>Keep transport failures visible without letting a repeating failure erase the event history.</summary>
    public void ReportError(uint tick, string safeMessage)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (_lastErrorEvent.TryGetValue(safeMessage, out var last) && now - last < TimeSpan.FromSeconds(5))
                return;
            _lastErrorEvent[safeMessage] = now;
            Log(tick, safeMessage, "error");
        }
    }

    public void RecordPeer(ulong entityId, bool connected, int? ping = null,
        float? receiveQuality = null, float? sendQuality = null)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            bool changed = !_peers.TryGetValue(entityId, out var previous) || previous.Connected != connected;
            _peers[entityId] = new(entityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                connected, now, ping,
                Quality(receiveQuality), Quality(sendQuality));
            if (changed || !_lastPeerEvent.TryGetValue(entityId, out var last) || now - last >= TimeSpan.FromSeconds(5))
            {
                _lastPeerEvent[entityId] = now;
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                Log(_tick, FormattableString.Invariant($"Entity {entityId}: connected={connected} RTT={ping?.ToString(culture) ?? "unknown"}ms receiveQuality={Quality(receiveQuality)?.ToString("F3", culture) ?? "unknown"} sendQuality={Quality(sendQuality)?.ToString("F3", culture) ?? "unknown"}"));
            }
        }
    }

    private static float? Quality(float? value) => value is >= 0 and <= 1 ? value : null;
    public void Complete() { lock (_gate) _endedAt ??= _clock.GetUtcNow(); }

    public MatchDiagnosticsSnapshot Snapshot()
    {
        lock (_gate)
            return new(MatchId, Arena, Transport, _clock.GetUtcNow(), _startedAt, _endedAt,
                _tick, _samples, _samples == 0 ? 0 : _totalMs / _samples, _maxMs, _maxDelay,
                _slowTicks, _resets, EventLimit, _sequence - _events.Count,
                _peers.Values.OrderBy(p => p.EntityId).ToArray(), _events.ToArray());
    }
}

/// <summary>Active matches plus the 50 most recently ended, lost on GameHost restart.</summary>
public sealed class MatchDiagnosticsStore
{
    public const int CompletedLimit = 50;
    private readonly object _gate = new();
    private readonly Dictionary<string, MatchDiagnostics> _matches = new(StringComparer.Ordinal);

    public void Add(MatchDiagnostics diagnostics)
    {
        lock (_gate) { _matches[diagnostics.MatchId] = diagnostics; Prune(); }
    }

    public MatchDiagnosticsSnapshot? Get(Guid matchId)
    {
        lock (_gate)
        {
            Prune();
            return _matches.TryGetValue(matchId.ToString("D"), out var match) ? match.Snapshot() : null;
        }
    }

    private void Prune()
    {
        foreach (var key in _matches.Values.Where(m => m.EndedAtUtc.HasValue)
            .OrderByDescending(m => m.EndedAtUtc).Skip(CompletedLimit).Select(m => m.MatchId).ToArray())
            _matches.Remove(key);
    }
}
