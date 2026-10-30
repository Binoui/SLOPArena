using System;

namespace SlopArena.Shared.AI;

/// <summary>
/// Immutable opponent information made available to one CPU after its reaction delay.
/// It contains simulation observations only; input commands are never captured.
/// </summary>
public readonly struct CpuObservation
{
    public readonly int ObservationTick;
    internal readonly int AgeTicks;
    internal readonly CpuTimingSnapshot Timing;
    public readonly float PX, PY, PZ;
    public readonly float VX, VY, VZ;
    public readonly float ThreatReach;
    public readonly ActionState State;
    public readonly ushort StateTicks;
    public readonly bool IsGrounded;
    public readonly bool InPostHitstunFlight;
    public readonly ushort DamagePercent;
    public readonly ushort AttackElapsedTicks;
    public readonly byte AttackSlot, ComboStage;
    public readonly ushort AnimLockTicks, LandingLagTicks, ChargeTicks;
    public readonly ushort HitstunTicks, HitstopTicks;

    private CpuObservation(int observationTick, in CharacterState state, float threatReach, in CpuTimingSnapshot timing)
    {
        ObservationTick = observationTick;
        AgeTicks = 0;
        Timing = timing;
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
        InPostHitstunFlight = state.InPostHitstunFlight;
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

    private CpuObservation(in CpuObservation source, int ageTicks)
    {
        this = source;
        AgeTicks = Math.Max(0, ageTicks);
    }

    internal CpuObservation WithAge(int ageTicks) => new(this, ageTicks);

    internal static CpuObservation Capture(int observationTick, in CharacterState state, float threatReach,
        CpuTimingSnapshot timing = default) => new(observationTick, state, threatReach, timing);

    public bool IsThreatening
        => State is ActionState.Attacking or ActionState.Aiming or ActionState.Warping
            || AnimLockTicks > 0
            || LandingLagTicks > 0;
}

public enum CpuContactKind : byte { Hit, Block }

internal readonly struct CpuContactObservation
{
    public readonly CpuContactKind Kind;
    public readonly int Sequence;
    public readonly int RecordedTick;
    public readonly int AvailableTick;
    public readonly byte Slot;
    public readonly bool Airborne;
    public readonly ushort HitstunTicks;
    public readonly ushort HitstopTicks;
    public readonly float PX, PY, PZ;
    public readonly float VX, VY, VZ;

    public CpuContactObservation(CpuContactKind kind, int sequence, int recordedTick, int availableTick, byte slot, bool airborne,
        ushort hitstunTicks, ushort hitstopTicks, in CharacterState target)
    {
        Kind = kind;
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

internal enum BotPlanKind : byte
{
    Ordinary,
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
        public uint NextEpisode, AttackEpisode, LandingEpisode;
        public ActionState PreviousState;
        public byte PreviousSlot;
        public ushort PreviousElapsed, PreviousLanding;
    }

    private readonly OpponentTrack[] _tracks = new OpponentTrack[MaxOpponents];
    private int _trackCount;
    private readonly CpuContactObservation[] _combatHistory = new CpuContactObservation[CombatHistoryCapacity];
    private readonly CpuContactObservation[] _pendingContacts = new CpuContactObservation[CombatHistoryCapacity];
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
    private int _nextContactSequence, _recordedContactSequence;
    private int _lastEvaluatedContactSequence;
    private int _pendingContactCount;

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
    internal CpuCombatPlan Combat;
    internal uint PlanSerial, EvaluatedThreat, EvaluatedRecovery;
    internal int ThreatEvaluations, RecoveryEvaluations, ContactEvaluations;
    internal CpuPlanEnd LastEnd;
    internal bool SpaceNext;
    internal byte WalkBag, JumpBag, DashBag;
    internal CpuApproachKind? LastApproach;
    public CpuPlanTrace CombatTrace => new(PlanSerial, Combat.Kind, Combat.Approach, Combat.Started,
        (byte)Combat.Phase, LastEnd, ThreatEvaluations, RecoveryEvaluations, ContactEvaluations);
    internal void BeginCombat(in CpuCombatPlan plan) { Combat = plan; Side = plan.Execution.Side; PlanSerial++; }
    internal void EndCombat(CpuPlanEnd reason, bool space = false)
    { Combat = default; LastEnd = reason; SpaceNext = space; }
    internal void ForgetCombat()
    { EndCombat(CpuPlanEnd.Interrupted); EvaluatedThreat = EvaluatedRecovery = 0; WalkBag = JumpBag = DashBag = 0; LastApproach = null; }

    internal CpuBehavior Behavior;

    /// <summary>Preferred detour side, initialized at plan entry and updated when steering accepts the other side.</summary>
    internal sbyte Side;
    internal int WalkProbesThisTick;
    internal int DetourCursor;
    internal bool Recovering;
    internal byte RecoveryDeaths;

    /// <summary>Inside the engage band: approach resumes only once the opponent is past its outer edge.</summary>
    internal bool Engaged;

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
        => ObserveOpponent(state, threatReach, CpuAttackModel.WithoutActivation(state));

    internal void ObserveOpponent(in CharacterState state, float threatReach, in CpuTimingSnapshot timing)
    {
        var track = Track(state.EntityId) ?? AddTrack(state.EntityId);
        if (!_tickOpen || track.SeenAt == _clock)
        {
            _clock++;
            _tickOpen = true;
            ReleasePendingContacts();
        }
        if (track.Count > 0 && track.Deaths != state.Deaths)
        {
            track.Count = track.WriteIndex = 0;
            track.PreviousState = ActionState.Idle; track.PreviousSlot = 0;
            track.PreviousElapsed = track.PreviousLanding = 0;
        }
        track.Deaths = state.Deaths;
        track.SeenAt = _clock;
        bool attacking = IsAttackEpisode(state.State);
        bool previousAttack = IsAttackEpisode(track.PreviousState);
        if (attacking && (!previousAttack || state.AttackSlot != track.PreviousSlot
            || state.AttackSlot != 0 && state.AttackElapsedTicks < track.PreviousElapsed))
            track.AttackEpisode = ++track.NextEpisode;
        if (state.LandingLagTicks > 0 && (track.PreviousLanding == 0 || state.LandingLagTicks > track.PreviousLanding))
            track.LandingEpisode = ++track.NextEpisode;
        uint recovery = timing.Phase == CpuAttackPhase.Recovery
            ? state.LandingLagTicks > 0 ? track.LandingEpisode : track.AttackEpisode : 0;
        var captured = new CpuTimingSnapshot(timing.Phase, timing.LockTicks, attacking ? track.AttackEpisode : 0, recovery);
        track.History[track.WriteIndex] = CpuObservation.Capture(_clock, state, threatReach, captured);
        track.PreviousState = state.State; track.PreviousSlot = state.AttackSlot;
        track.PreviousElapsed = state.AttackElapsedTicks; track.PreviousLanding = state.LandingLagTicks;
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
    internal int Clock => _clock;

    internal void EndTick() => _tickOpen = false;

    private static bool IsAttackEpisode(ActionState state)
        => state is ActionState.Attacking or ActionState.Aiming or ActionState.HomingDashFloat
            or ActionState.HomingDash or ActionState.HomingDashArrive or ActionState.HomingDashMiss;

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
        track.NextEpisode = track.AttackEpisode = track.LandingEpisode = 0;
        track.PreviousState = ActionState.Idle; track.PreviousSlot = 0;
        track.PreviousElapsed = track.PreviousLanding = 0;
        track.SeenAt = int.MinValue;
        return track;
    }

    /// <summary>
    /// Drop what was about the previous engagement: plans, climb, pending and
    /// unevaluated contacts. A live recovery or grab plan is kept.
    /// </summary>
    private void ForgetEngagement()
    {
        if (PlanKind is not (BotPlanKind.Recovery or BotPlanKind.Grab))
            ClearPlan();

        EndClimb();
        ForgetCombat();
        Engaged = false;

        DetourCursor = 0;

        _pendingContactCount = 0;
        _lastEvaluatedContactSequence = _nextContactSequence;
    }

    private void ReleasePendingContacts()
    {
        while (_pendingContactCount > 0 && _pendingContacts[0].AvailableTick <= _clock)
        {
            var hit = _pendingContacts[0];
            _combatHistory[hit.Sequence % _combatHistory.Length] = hit;
            _nextContactSequence = hit.Sequence;
            for (int i = 1; i < _pendingContactCount; i++)
                _pendingContacts[i - 1] = _pendingContacts[i];
            _pendingContactCount--;
        }
    }

    /// <summary>
    /// Record an authoritative resolver contact by this CPU. Contacts on anyone but the current opponent are ignored,
    /// so callers pass every hit or block they own. The event is withheld until the selected CPU
    /// reaction delay expires, then retained long enough to survive hitstop and action locks.
    /// </summary>
    public void RecordOpponentContact(byte slot, CpuContactKind kind, in CharacterState target, ushort hitstopTicks)
    {
        if (_trackCount == 0 || _opponentChosen && !_hasOpponent || target.EntityId != OpponentId)
            return;
        int delay = BotDifficultyProfile.ForDifficulty(Difficulty).ReactionDelayTicks;
        if (_pendingContactCount == _pendingContacts.Length)
        {
            for (int i = 1; i < _pendingContactCount; i++)
                _pendingContacts[i - 1] = _pendingContacts[i];
            _pendingContactCount--;
        }
        int sequence = ++_recordedContactSequence;
        _pendingContacts[_pendingContactCount++] = new CpuContactObservation(kind,
            sequence, _clock, _clock + delay, slot, !target.IsGrounded,
            kind == CpuContactKind.Hit ? target.HitstunTicks : (ushort)0, hitstopTicks, target);
    }

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
        var recorded = track.History[index];
        observation = recorded.WithAge(_clock - recorded.ObservationTick);
        return true;
    }

    internal bool TryGetNewContact(out CpuContactObservation hit)
    {
        for (int i = 0; i < _combatHistory.Length; i++)
        {
            int index = (_nextContactSequence - i) % _combatHistory.Length;
            if (index < 0) index += _combatHistory.Length;
            var candidate = _combatHistory[index];
            if (candidate.Sequence == 0)
                break;
            if (candidate.Sequence <= _lastEvaluatedContactSequence)
                break;
            if (_clock - candidate.AvailableTick > CombatHistoryWindowTicks)
                break;
            hit = candidate;
            return true;
        }
        hit = default;
        return false;
    }

    internal CpuObservation ContactObservation(in CpuContactObservation hit)
    {
        var state = new CharacterState
        {
            PX = hit.PX, PY = hit.PY, PZ = hit.PZ, VX = hit.VX, VY = hit.VY, VZ = hit.VZ,
            IsGrounded = !hit.Airborne, State = hit.Kind == CpuContactKind.Hit ? ActionState.Hitstun : ActionState.Shielding,
            HitstunTicks = hit.HitstunTicks, HitstopTicks = hit.HitstopTicks,
        };
        return CpuObservation.Capture(hit.RecordedTick, state, float.PositiveInfinity)
            .WithAge(_clock - hit.RecordedTick);
    }

    internal int RemainingHitstun(in CpuContactObservation hit)
    {
        if (hit.Kind != CpuContactKind.Hit) return 0;
        int elapsed = Math.Max(0, _clock - hit.RecordedTick - hit.HitstopTicks);
        return Math.Max(0, hit.HitstunTicks - elapsed);
    }

    internal void MarkContactEvaluated(in CpuContactObservation hit)
        => _lastEvaluatedContactSequence = Math.Max(_lastEvaluatedContactSequence, hit.Sequence);

    /// <summary>
    /// Reset runner-owned transient state before a match or CPU life starts: the engagement, every fighter's
    /// history and the opponent lock. The selected difficulty and <see cref="OpponentSwitches"/> are preserved.
    /// </summary>
    public void Reset()
    {
        ForgetCombat();
        ThreatEvaluations = RecoveryEvaluations = ContactEvaluations = 0; PlanSerial = 0;
        Side = 0;
        DetourCursor = WalkProbesThisTick = 0;
        Recovering = false; RecoveryDeaths = 0;

        Engaged = false;

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
        _nextContactSequence = _recordedContactSequence = 0;
        _lastEvaluatedContactSequence = 0;
        _pendingContactCount = 0;
        Array.Clear(_combatHistory, 0, _combatHistory.Length);
    }

    internal void StartPlan(byte slot, bool requiresAim, short aimYaw, short aimPitch,
        ushort aimDistance, ushort holdTicks, byte deaths, bool wasGrounded,
        bool pressIssued = true, BotPlanKind kind = BotPlanKind.Ordinary)
    {
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
