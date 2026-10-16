namespace SlopArena.Shared.Rollback;

/// <summary>
/// Estimates the authoritative 60 Hz timeline, sizes the input lead and paces local
/// prediction. Each Clock sample yields a server/local tick offset; the estimate keeps the
/// largest offset in a short window (the sample delayed least on its way here), so
/// receive-path delays and a stale latency reading expire instead of accumulating. The lead starts from half the
/// round trip plus two safety ticks and adapts to the arrival slack the GameServer reports.
/// </summary>
public sealed class NetplayClock
{
    public const uint HistoryTicks = 30;
    public const uint MinimumLeadTicks = 2;
    public const uint MaximumLeadTicks = 12;
    public const uint MaximumCatchUpSteps = 4;
    public const double SampleWindowSeconds = 2.0;
    private const int SampleCapacity = 64;
    private const int MaximumLeadBonus = 6;
    private const int MinimumLeadBonus = -2;
    private const double LeadIncreaseCooldownSeconds = 0.5;
    private const double ComfortableSlackSeconds = 4.0;

    private readonly double[] _sampleTimes = new double[SampleCapacity];
    private readonly double[] _sampleOffsets = new double[SampleCapacity];
    private readonly TickPacer _pacer = new();
    private int _sampleStart;
    private int _sampleCount;
    private double _offset;
    private uint _lastAuthorityTick;
    private double _observedAt;
    private uint _startTick;
    private bool _seeded;
    private uint _baseLead = MinimumLeadTicks;
    private int _leadBonus;
    private double _lastLeadIncreaseAt = double.NegativeInfinity;
    private double _comfortableSince = double.NaN;

    public uint StartTick => _startTick;
    public bool IsSeeded => _seeded;
    /// <summary>Server-feedback adjustment applied on top of the latency-derived lead.</summary>
    public int LeadBonus => _leadBonus;
    public uint LeadTicks => (uint)System.Math.Min(System.Math.Max((int)_baseLead + _leadBonus,
        (int)MinimumLeadTicks), (int)MaximumLeadTicks);

    /// <summary>Record a Clock sample; RTT compensation is split between clock and input lead.</summary>
    public bool Observe(uint authorityTick, uint startTick, double receivedAtSeconds, double roundTripMilliseconds)
    {
        if (!double.IsFinite(receivedAtSeconds) || receivedAtSeconds < 0 ||
            !double.IsFinite(roundTripMilliseconds) || roundTripMilliseconds < 0 ||
            (_seeded && (receivedAtSeconds < _observedAt || authorityTick < _lastAuthorityTick ||
                (_startTick != 0 && (startTick == 0 || startTick < _startTick)))))
            return false;

        double oneWayTicks = System.Math.Min(roundTripMilliseconds * 0.03, 1_000_000d);
        AddSample(receivedAtSeconds, authorityTick + oneWayTicks - receivedAtSeconds * 60d);
        double roundedOneWay = System.Math.Ceiling(oneWayTicks);
        _baseLead = roundedOneWay >= MaximumLeadTicks - 2 ? MaximumLeadTicks : (uint)roundedOneWay + 2;
        _lastAuthorityTick = authorityTick;
        _startTick = startTick;
        _observedAt = receivedAtSeconds;
        _seeded = true;
        return true;
    }

    /// <summary>
    /// Adapt the lead to the GameServer's arrival report: any late input raises it at once
    /// (rate-limited while the change propagates); several seconds of comfortable slack
    /// lower it one tick at a time.
    /// </summary>
    public void ObserveInputTiming(sbyte minimumSlack, byte lateInputs, double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || (minimumSlack == NetplayControlPacket.NoInputSlack && lateInputs == 0))
            return;
        if (lateInputs > 0 || minimumSlack < 1)
        {
            _comfortableSince = double.NaN;
            if (_leadBonus < MaximumLeadBonus && nowSeconds - _lastLeadIncreaseAt >= LeadIncreaseCooldownSeconds)
            {
                _leadBonus++;
                _lastLeadIncreaseAt = nowSeconds;
            }
            return;
        }
        if (minimumSlack < 3)
        {
            _comfortableSince = double.NaN;
            return;
        }
        if (double.IsNaN(_comfortableSince))
            _comfortableSince = nowSeconds;
        else if (nowSeconds - _comfortableSince >= ComfortableSlackSeconds)
        {
            if (_leadBonus > MinimumLeadBonus)
                _leadBonus--;
            _comfortableSince = nowSeconds;
        }
    }

    /// <summary>Estimated authority tick at <paramref name="nowSeconds"/> (never earlier than the last sample).</summary>
    public uint EstimateTick(double nowSeconds)
    {
        if (!_seeded)
            return 0;
        double at = double.IsFinite(nowSeconds) && nowSeconds > _observedAt ? nowSeconds : _observedAt;
        double ticks = System.Math.Floor(at * 60d + _offset);
        if (ticks <= 0) return 0;
        return ticks >= uint.MaxValue ? uint.MaxValue : (uint)ticks;
    }

    /// <summary>Desired prediction frontier: the estimated authority tick plus the input lead.</summary>
    public uint DesiredLocalTick(double nowSeconds)
    {
        uint estimate = EstimateTick(nowSeconds);
        uint lead = LeadTicks;
        return estimate > uint.MaxValue - lead ? uint.MaxValue : estimate + lead;
    }

    /// <summary>Unpaced distance to the frontier, bounded to <see cref="MaximumCatchUpSteps"/>.</summary>
    public uint CatchUpSteps(uint localTick, double nowSeconds)
    {
        uint desired = DesiredLocalTick(nowSeconds);
        if (desired <= localTick) return 0;
        return System.Math.Min(desired - localTick, MaximumCatchUpSteps);
    }

    /// <summary>
    /// Paced steps for one local update: normally exactly one, with smoothed single-step
    /// corrections and an immediate bounded catch-up when far behind.
    /// </summary>
    public uint StepsForUpdate(uint localTick, double nowSeconds)
        => _seeded ? _pacer.Steps(localTick, DesiredLocalTick(nowSeconds), MaximumCatchUpSteps) : 0;

    /// <summary>Call when the local timeline is (re)seeded: the next update aligns with the frontier.</summary>
    public void ResetPacing() => _pacer.Reset(alignNext: true);

    private void AddSample(double at, double offset)
    {
        while (_sampleCount > 0 && at - _sampleTimes[_sampleStart] > SampleWindowSeconds)
        {
            _sampleStart = (_sampleStart + 1) % SampleCapacity;
            _sampleCount--;
        }
        if (_sampleCount == SampleCapacity)
        {
            _sampleStart = (_sampleStart + 1) % SampleCapacity;
            _sampleCount--;
        }
        int index = (_sampleStart + _sampleCount) % SampleCapacity;
        _sampleTimes[index] = at;
        _sampleOffsets[index] = offset;
        _sampleCount++;

        double best = double.NegativeInfinity;
        for (int i = 0; i < _sampleCount; i++)
        {
            double candidate = _sampleOffsets[(_sampleStart + i) % SampleCapacity];
            if (candidate > best) best = candidate;
        }
        _offset = best;
    }
}
