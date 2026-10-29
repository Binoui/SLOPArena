using System;

namespace SlopArena.Shared.AI;

/// <summary>
/// Immutable opponent information made available to one CPU after its reaction delay.
/// It contains simulation observations only; input commands are never captured.
/// </summary>
public readonly struct CpuObservation
{
    public readonly int ObservationTick;
    public readonly float PX, PY, PZ;
    public readonly float VX, VY, VZ;
    public readonly float ThreatReach;
    public readonly ActionState State;
    public readonly ushort StateTicks;
    public readonly bool IsGrounded;
    public readonly ushort DamagePercent;
    public readonly ushort AttackElapsedTicks;
    public readonly byte AttackSlot, ComboStage;
    public readonly ushort AnimLockTicks, LandingLagTicks, ChargeTicks;
    public readonly ushort HitstunTicks, HitstopTicks;

    private CpuObservation(int observationTick, in CharacterState state, float threatReach)
    {
        ObservationTick = observationTick;
        ThreatReach = threatReach;
        PX = state.PX;
        PY = state.PY;
        PZ = state.PZ;
        VX = state.VX;
        VY = state.VY;
        VZ = state.VZ;
        State = state.State;
        StateTicks = state.StateTicks;
        IsGrounded = state.IsGrounded;
        DamagePercent = state.DamagePercent;
        AttackElapsedTicks = state.AttackElapsedTicks;
        AttackSlot = state.AttackSlot;
        ComboStage = state.ComboStage;
        AnimLockTicks = state.AnimLockTicks;
        LandingLagTicks = state.LandingLagTicks;
        ChargeTicks = state.ChargeTicks;
        HitstunTicks = state.HitstunTicks;
        HitstopTicks = state.HitstopTicks;
    }

    internal static CpuObservation Capture(int observationTick, in CharacterState state, float threatReach)
        => new(observationTick, state, threatReach);

    public bool IsThreatening
        => State is ActionState.Attacking or ActionState.Aiming or ActionState.Warping
            || AnimLockTicks > 0
            || LandingLagTicks > 0;
}

internal readonly struct CpuHitObservation
{
    public readonly int Sequence;
    public readonly int RecordedTick;
    public readonly int AvailableTick;
    public readonly byte Slot;
    public readonly bool Airborne;
    public readonly ushort HitstunTicks;
    public readonly ushort HitstopTicks;
    public readonly float PX, PY, PZ;
    public readonly float VX, VY, VZ;

    public CpuHitObservation(int sequence, int recordedTick, int availableTick, byte slot, bool airborne,
        ushort hitstunTicks, ushort hitstopTicks, in CharacterState target)
    {
        Sequence = sequence;
        RecordedTick = recordedTick;
        AvailableTick = availableTick;
        Slot = slot;
        Airborne = airborne;
        HitstunTicks = hitstunTicks;
        HitstopTicks = hitstopTicks;
        PX = target.PX;
        PY = target.PY;
        PZ = target.PZ;
        VX = target.VX;
        VY = target.VY;
        VZ = target.VZ;
    }
}

/// <summary>Execution phase for one selected CPU action.</summary>
internal enum BotPlanPhase : byte
{
    None,
    PendingPress,
    AimHold,
}

/// <summary>How a CPU moves between decisions.</summary>
internal enum Footwork : byte
{
    /// <summary>Close to the engage band around the observed opponent, then stand inside it.</summary>
    Engage,
    /// <summary>Back away from the observed opponent for a window.</summary>
    Retreat,
    /// <summary>Circle the observed opponent on <see cref="BotMemory.Side"/> for a window.</summary>
    Circle,
}

internal enum BotPlanKind : byte
{
    Ordinary,
    TrueCombo,
    PressureString,
    Recovery,
    /// <summary>Universal grab: press, wait out the attempt, hold for the reaction delay, throw forward.</summary>
    Grab,
}

/// <summary>
/// Persistent per-entity bot state that spans ticks. Held by the runner/controller — never
/// written into <see cref="CharacterState"/>, so the prediction wire is untouched.
/// </summary>
public sealed class BotMemory
{
    private const int MaximumReactionDelayTicks = 24;
    private const int CombatHistoryWindowTicks = 90;
    private const int CombatHistoryCapacity = 8;
    /// <summary>A four-seat match has at most three opponents per CPU.</summary>
    private const int MaxOpponents = 3;
    private const int OpponentDwellTicks = 120;
    /// <summary>Another fighter takes over only when at least 20 percent nearer (0.8 squared).</summary>
    private const float CurrentOpponentBias = 0.64f;

    /// <summary>Delayed observations of one fighter, so the reaction delay applies per fighter.</summary>
    private sealed class OpponentTrack
    {
        public ulong EntityId;
        public readonly CpuObservation[] History = new CpuObservation[MaximumReactionDelayTicks + 1];
        public int Count, WriteIndex;
        public byte Deaths;
        public int SeenAt = int.MinValue;
    }

    private readonly OpponentTrack[] _tracks = new OpponentTrack[MaxOpponents];
    private int _trackCount;
    private readonly CpuHitObservation[] _combatHistory = new CpuHitObservation[CombatHistoryCapacity];
    private readonly CpuHitObservation[] _pendingHits = new CpuHitObservation[CombatHistoryCapacity];
    /// <summary>
    /// One step per CPU tick. The first observation after a decision opens a tick; observing a fighter
    /// already seen in the open tick opens the next one (callers that observe without deciding).
    /// </summary>
    private int _clock;
    private bool _tickOpen;
    private ulong _lastObservedId;
    private bool _opponentChosen, _hasOpponent;
    private ulong _opponentId;
    private byte _opponentDeaths;
    private int _dwellUntil;
    private int _chosenAt = int.MinValue;
    private int _nextHitSequence;
    private int _lastEvaluatedHitSequence;
    private int _pendingHitCount;

    public BotMemory()
    {
        for (int i = 0; i < _tracks.Length; i++)
            _tracks[i] = new OpponentTrack();
    }

    /// <summary>
    /// The fighter this CPU fights: the chosen one once <see cref="ChooseOpponent"/> has run (0 when none is
    /// observable), otherwise the last fighter observed.
    /// </summary>
    public ulong OpponentId => !_opponentChosen ? _lastObservedId : _hasOpponent ? _opponentId : 0;

    /// <summary>Opponent switches and re-choices after the opponent died, across lives. Diagnostic.</summary>
    public int OpponentSwitches { get; private set; }

    /// <summary>Named CPU difficulty selected for this bot.</summary>
    public CpuDifficulty Difficulty = CpuDifficulty.Normal;
    internal CpuBehavior Behavior;

    /// <summary>Ticks remaining before a fresh attack/pressure decision.</summary>
    public int DecisionTicksRemaining;

    /// <summary>Preferred lateral side, -1 or +1 once rolled: the circling side and the first detour tried.</summary>
    internal sbyte Side;

    /// <summary>The held footwork. Retreat and Circle count down once per decision call, then return to Engage.</summary>
    internal Footwork Footwork;
    internal int FootworkTicksLeft;

    /// <summary>Perceived-reach error, rolled on decision ticks only and reused until the next one.</summary>
    internal float RangeScale = 1f;

    /// <summary>Inside the engage band: approach resumes only once the opponent is past its outer edge.</summary>
    internal bool Engaged;

    /// <summary>A special was fired from outside normal reach since the CPU was last inside the engage band.</summary>
    internal bool FiredOnApproach;

    /// <summary>
    /// A ledge climb in progress: ticks since the jump press (0 when not climbing), the heading held toward
    /// the ledge, whether the climb has left the ground, and the life it started in.
    /// </summary>
    internal ushort ClimbTicks;
    internal float ClimbX, ClimbZ;
    internal bool ClimbLeftGround;
    internal byte ClimbDeaths;

    internal bool Climbing => ClimbTicks > 0;

    /// <summary>
    /// One ordinary input plan selected by the policy. It carries both the initial press and
    /// any required aim/hold/release sequence without entering CharacterState.
    /// </summary>
    internal BotPlanPhase PlanPhase;
    internal BotPlanKind PlanKind;
    internal bool PlanPressIssued;
    internal byte PlanSlot;
    internal ushort PlanTicks;
    internal ushort PlanHoldTicks;
    internal short PlanAimYaw;
    internal short PlanAimPitch;
    internal ushort PlanAimDistance;
    internal byte PlanDeaths;
    internal bool PlanWasAirborne;

    /// <summary>
    /// Record one fighter's current simulation state, keyed by its entity id. Call once per tick for every
    /// fighter this CPU may fight, before <see cref="ChooseOpponent"/> and the decision. The policy can only
    /// retrieve it after the selected difficulty's delay; a fighter whose deaths changed starts a fresh history.
    /// </summary>
    public void ObserveOpponent(in CharacterState state, float threatReach = float.PositiveInfinity)
    {
        var track = Track(state.EntityId) ?? AddTrack(state.EntityId);
        if (!_tickOpen || track.SeenAt == _clock)
        {
            _clock++;
            _tickOpen = true;
            ReleasePendingHits();
        }
        if (track.Count > 0 && track.Deaths != state.Deaths)
            track.Count = track.WriteIndex = 0;
        track.Deaths = state.Deaths;
        track.SeenAt = _clock;
        track.History[track.WriteIndex] = CpuObservation.Capture(_clock, state, threatReach);
        track.WriteIndex = (track.WriteIndex + 1) % track.History.Length;
        if (track.Count < track.History.Length)
            track.Count++;
        _lastObservedId = state.EntityId;
    }

    /// <summary>
    /// The free-for-all opponent rule. Candidates are the fighters observed this tick whose delayed observation
    /// is available, scored by delayed 3D distance; the current opponent's score is scaled by 0.64 and ties go
    /// to the lower entity id. A valid current opponent (observed this tick, not dead since chosen) is kept
    /// while the 120-tick dwell runs or while the CPU is committed. Losing the opponent, a switch, or a
    /// re-choice after the opponent died forgets the engagement; the first choice does not. Idempotent within
    /// a tick.
    /// </summary>
    public ulong ChooseOpponent(in CharacterState self)
    {
        if (_tickOpen && _chosenAt == _clock)
            return OpponentId;
        _chosenAt = _tickOpen ? _clock : int.MinValue;
        var current = _opponentChosen && _hasOpponent ? Track(_opponentId) : null;
        bool valid = current != null && ObservedThisTick(current) && current.Deaths == _opponentDeaths;
        bool committed = self.State is ActionState.Attacking or ActionState.Aiming or ActionState.GrabAttempt
                or ActionState.Grabbing or ActionState.Throwing
            || self.InteractionId != 0 || self.HitstunTicks > 0 || self.HitstopTicks > 0
            || self.BlockStunTicks > 0 || PlanPhase != BotPlanPhase.None;
        _opponentChosen = true;
        if (valid && (_clock < _dwellUntil || committed))
            return _opponentId;

        OpponentTrack? best = null;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < _trackCount; i++)
        {
            var track = _tracks[i];
            if (!ObservedThisTick(track) || !TryGetDelayed(track, out var seen))
                continue;
            float dx = seen.PX - self.PX, dy = seen.PY - self.PY, dz = seen.PZ - self.PZ;
            float score = dx * dx + dy * dy + dz * dz;
            if (valid && track == current)
                score *= CurrentOpponentBias;
            if (score < bestScore || score == bestScore && track.EntityId < best!.EntityId)
            {
                best = track;
                bestScore = score;
            }
        }
        if (best == null)
        {
            if (_hasOpponent)
                ForgetEngagement();
            _hasOpponent = false;
            return 0;
        }
        if (best != current || !valid)
        {
            if (_hasOpponent)
            {
                OpponentSwitches++;
                ForgetEngagement();
            }
            _hasOpponent = true;
            _opponentId = best.EntityId;
            _opponentDeaths = best.Deaths;
            _dwellUntil = _clock + OpponentDwellTicks;
        }
        return _opponentId;
    }

    /// <summary>Close the tick: the next observation starts a new one. Called once at the end of every decision.</summary>
    internal void EndTick() => _tickOpen = false;

    private bool ObservedThisTick(OpponentTrack track) => _tickOpen && track.SeenAt == _clock;

    private OpponentTrack? Track(ulong entityId)
    {
        for (int i = 0; i < _trackCount; i++)
            if (_tracks[i].EntityId == entityId)
                return _tracks[i];
        return null;
    }

    private OpponentTrack AddTrack(ulong entityId)
    {
        OpponentTrack track;
        if (_trackCount < _tracks.Length)
            track = _tracks[_trackCount++];
        else
        {
            track = _tracks[0];
            for (int i = 1; i < _tracks.Length; i++)
                if (_tracks[i].SeenAt < track.SeenAt)
                    track = _tracks[i];
        }
        track.EntityId = entityId;
        track.Count = track.WriteIndex = 0;
        track.SeenAt = int.MinValue;
        return track;
    }

    /// <summary>
    /// Drop what was about the previous engagement: plan, footwork, climb, decision countdown, pending and
    /// unevaluated hits. A live recovery or grab plan is kept.
    /// </summary>
    private void ForgetEngagement()
    {
        if (PlanKind is not (BotPlanKind.Recovery or BotPlanKind.Grab))
            ClearPlan();
        EndFootwork();
        EndClimb();
        Engaged = false;
        FiredOnApproach = false;
        DecisionTicksRemaining = 0;
        _pendingHitCount = 0;
        _lastEvaluatedHitSequence = _nextHitSequence;
    }

    private void ReleasePendingHits()
    {
        while (_pendingHitCount > 0 && _pendingHits[0].AvailableTick <= _clock)
        {
            var hit = _pendingHits[0];
            _combatHistory[hit.Sequence % _combatHistory.Length] = hit;
            _nextHitSequence = hit.Sequence;
            for (int i = 1; i < _pendingHitCount; i++)
                _pendingHits[i - 1] = _pendingHits[i];
            _pendingHitCount--;
        }
    }

    /// <summary>
    /// Record an authoritative resolver hit by this CPU. Hits on anyone but the current opponent are ignored,
    /// so callers pass every unblocked hit they own. The event is withheld until the selected CPU
    /// reaction delay expires, then retained long enough to survive hitstop and action locks.
    /// </summary>
    public void RecordOpponentHit(byte slot, bool airborne, in CharacterState target,
        ushort hitstunTicks, ushort hitstopTicks)
    {
        if (_trackCount == 0 || _opponentChosen && !_hasOpponent || target.EntityId != OpponentId)
            return;
        int delay = BotDifficultyProfile.ForDifficulty(Difficulty).ReactionDelayTicks;
        if (_pendingHitCount == _pendingHits.Length)
        {
            for (int i = 1; i < _pendingHitCount; i++)
                _pendingHits[i - 1] = _pendingHits[i];
            _pendingHitCount--;
        }
        int sequence = _nextHitSequence + _pendingHitCount + 1;
        _pendingHits[_pendingHitCount++] = new CpuHitObservation(
            sequence, _clock, _clock + delay, slot, airborne,
            hitstunTicks, hitstopTicks, target);
    }

    /// <summary>Compatibility overload for callers that only have the post-hit target state.</summary>
    public void RecordOpponentHit(byte slot, in CharacterState target)
        => RecordOpponentHit(slot, !target.IsGrounded, target, target.HitstunTicks, target.HitstopTicks);

    public void RecordOpponentHit(byte slot, in CharacterState target,
        ushort hitstunTicks, ushort hitstopTicks)
        => RecordOpponentHit(slot, !target.IsGrounded, target, hitstunTicks, hitstopTicks);
    /// <summary>The current opponent's observation from the reaction delay ago; false when none is available.</summary>
    internal bool TryGetDelayedOpponent(out CpuObservation observation)
    {
        var track = _opponentChosen && !_hasOpponent ? null : Track(OpponentId);
        if (track != null)
            return TryGetDelayed(track, out observation);
        observation = default;
        return false;
    }

    private bool TryGetDelayed(OpponentTrack track, out CpuObservation observation)
    {
        int delay = BotDifficultyProfile.ForDifficulty(Difficulty).ReactionDelayTicks;
        if (track.Count <= delay)
        {
            observation = default;
            return false;
        }

        int index = track.WriteIndex - 1 - delay;
        if (index < 0) index += track.History.Length;
        observation = track.History[index];
        return true;
    }

    internal bool TryGetNewHit(out CpuHitObservation hit)
    {
        for (int i = 0; i < _combatHistory.Length; i++)
        {
            int index = (_nextHitSequence - i) % _combatHistory.Length;
            if (index < 0) index += _combatHistory.Length;
            var candidate = _combatHistory[index];
            if (candidate.Sequence == 0)
                break;
            if (candidate.Sequence <= _lastEvaluatedHitSequence)
                break;
            if (_clock - candidate.AvailableTick > CombatHistoryWindowTicks)
                break;
            hit = candidate;
            return true;
        }
        hit = default;
        return false;
    }

    internal int RemainingHitstun(in CpuHitObservation hit)
    {
        int elapsed = Math.Max(0, _clock - hit.RecordedTick - hit.HitstopTicks);
        return Math.Max(0, hit.HitstunTicks - elapsed);
    }

    internal void MarkHitEvaluated(in CpuHitObservation hit)
        => _lastEvaluatedHitSequence = Math.Max(_lastEvaluatedHitSequence, hit.Sequence);

    internal void QueueFollowUp(byte slot, BotPlanKind kind, short aimYaw, short aimPitch,
        ushort aimDistance, ushort holdTicks, byte deaths, bool wasGrounded)
    {
        EndFootwork();
        PlanKind = kind;
        PlanPhase = holdTicks > 0 ? BotPlanPhase.AimHold : BotPlanPhase.PendingPress;
        PlanPressIssued = false;
        PlanSlot = slot;
        PlanTicks = 0;
        PlanHoldTicks = holdTicks;
        PlanAimYaw = aimYaw;
        PlanAimPitch = aimPitch;
        PlanAimDistance = aimDistance;
        PlanDeaths = deaths;
        PlanWasAirborne = !wasGrounded;
    }
    /// <summary>
    /// Reset runner-owned transient state before a match or CPU life starts: the engagement, every fighter's
    /// history and the opponent lock. The selected difficulty and <see cref="OpponentSwitches"/> are preserved.
    /// </summary>
    public void Reset()
    {
        DecisionTicksRemaining = 0;
        Side = 0;
        EndFootwork();
        RangeScale = 1f;
        Engaged = false;
        FiredOnApproach = false;
        EndClimb();
        ClearPlan();
        _trackCount = 0;
        _clock = 0;
        _tickOpen = false;
        _lastObservedId = 0;
        _opponentChosen = _hasOpponent = false;
        _opponentId = 0;
        _opponentDeaths = 0;
        _dwellUntil = 0;
        _chosenAt = int.MinValue;
        _nextHitSequence = 0;
        _lastEvaluatedHitSequence = 0;
        _pendingHitCount = 0;
        Array.Clear(_combatHistory, 0, _combatHistory.Length);
    }

    internal void StartPlan(byte slot, bool requiresAim, short aimYaw, short aimPitch,
        ushort aimDistance, ushort holdTicks, byte deaths, bool wasGrounded,
        bool pressIssued = true, BotPlanKind kind = BotPlanKind.Ordinary)
    {
        EndFootwork();
        PlanPhase = requiresAim ? BotPlanPhase.AimHold : BotPlanPhase.PendingPress;
        PlanKind = kind;
        PlanPressIssued = pressIssued;
        PlanSlot = slot;
        PlanTicks = 0;
        PlanHoldTicks = holdTicks;
        PlanAimYaw = aimYaw;
        PlanAimPitch = aimPitch;
        PlanAimDistance = aimDistance;
        PlanDeaths = deaths;
        PlanWasAirborne = !wasGrounded;
    }

    internal void HoldFootwork(Footwork footwork, int ticks)
    {
        Footwork = footwork;
        FootworkTicksLeft = ticks;
    }

    internal void EndFootwork()
    {
        Footwork = Footwork.Engage;
        FootworkTicksLeft = 0;
    }

    internal void StartClimb(float x, float z, byte deaths)
    {
        ClimbTicks = 1;
        ClimbX = x;
        ClimbZ = z;
        ClimbLeftGround = false;
        ClimbDeaths = deaths;
    }

    internal void EndClimb()
    {
        ClimbTicks = 0;
        ClimbX = ClimbZ = 0f;
        ClimbLeftGround = false;
    }

    internal void ClearPlan()
    {
        PlanPhase = BotPlanPhase.None;
        PlanKind = BotPlanKind.Ordinary;
        PlanPressIssued = false;
        PlanSlot = 0;
        PlanTicks = 0;
        PlanHoldTicks = 0;
        PlanAimYaw = 0;
        PlanAimPitch = 0;
        PlanAimDistance = 0;
        PlanDeaths = 0;
        PlanWasAirborne = false;
    }
}
