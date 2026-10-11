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
    public readonly ActionState State;
    public readonly ushort StateTicks;
    public readonly bool IsGrounded;
    public readonly ushort DamagePercent;
    public readonly ushort AttackElapsedTicks;
    public readonly byte AttackSlot, ComboStage;
    public readonly ushort AnimLockTicks, LandingLagTicks, ChargeTicks;
    public readonly ushort HitstunTicks, HitstopTicks;

    private CpuObservation(int observationTick, in CharacterState state)
    {
        ObservationTick = observationTick;
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

    internal static CpuObservation Capture(int observationTick, in CharacterState state)
        => new(observationTick, state);

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

internal enum BotPlanKind : byte
{
    Ordinary,
    TrueCombo,
    PressureString,
    Recovery,
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
    private readonly CpuObservation[] _opponentHistory = new CpuObservation[MaximumReactionDelayTicks + 1];
    private readonly CpuHitObservation[] _combatHistory = new CpuHitObservation[CombatHistoryCapacity];
    private readonly CpuHitObservation[] _pendingHits = new CpuHitObservation[CombatHistoryCapacity];
    private int _opponentHistoryCount;
    private int _opponentHistoryWriteIndex;
    private int _observationTick;
    private int _nextHitSequence;
    private int _lastEvaluatedHitSequence;
    private int _pendingHitCount;

    /// <summary>Named CPU difficulty selected for this bot.</summary>
    public CpuDifficulty Difficulty = CpuDifficulty.Normal;
    internal CpuBehavior Behavior;

    /// <summary>Ticks remaining before a fresh attack/pressure decision.</summary>
    public int DecisionTicksRemaining;

    /// <summary>Stable lateral direction for range-holding: -1 or +1 once selected.</summary>
    public sbyte StrafeDirection;

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
    /// Record the opponent's current simulation state. The policy can only retrieve it after
    /// the selected difficulty's delay has elapsed.
    /// </summary>
    public void ObserveOpponent(in CharacterState state)
    {
        _observationTick++;
        _opponentHistory[_opponentHistoryWriteIndex] =
            CpuObservation.Capture(_observationTick, state);
        _opponentHistoryWriteIndex = (_opponentHistoryWriteIndex + 1) % _opponentHistory.Length;
        if (_opponentHistoryCount < _opponentHistory.Length)
            _opponentHistoryCount++;
        while (_pendingHitCount > 0 && _pendingHits[0].AvailableTick <= _observationTick)
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
    /// Record an authoritative resolver hit. The event is withheld until the selected CPU
    /// reaction delay expires, then retained long enough to survive hitstop and action locks.
    /// </summary>
    public void RecordOpponentHit(byte slot, bool airborne, in CharacterState target,
        ushort hitstunTicks, ushort hitstopTicks)
    {
        int delay = BotDifficultyProfile.ForDifficulty(Difficulty).ReactionDelayTicks;
        if (_pendingHitCount == _pendingHits.Length)
        {
            for (int i = 1; i < _pendingHitCount; i++)
                _pendingHits[i - 1] = _pendingHits[i];
            _pendingHitCount--;
        }
        int sequence = _nextHitSequence + _pendingHitCount + 1;
        _pendingHits[_pendingHitCount++] = new CpuHitObservation(
            sequence, _observationTick, _observationTick + delay, slot, airborne,
            hitstunTicks, hitstopTicks, target);
    }

    /// <summary>Compatibility overload for callers that only have the post-hit target state.</summary>
    public void RecordOpponentHit(byte slot, in CharacterState target)
        => RecordOpponentHit(slot, !target.IsGrounded, target, target.HitstunTicks, target.HitstopTicks);

    public void RecordOpponentHit(byte slot, in CharacterState target,
        ushort hitstunTicks, ushort hitstopTicks)
        => RecordOpponentHit(slot, !target.IsGrounded, target, hitstunTicks, hitstopTicks);
    internal bool TryGetDelayedOpponent(out CpuObservation observation)
    {
        int delay = BotDifficultyProfile.ForDifficulty(Difficulty).ReactionDelayTicks;
        if (_opponentHistoryCount <= delay)
        {
            observation = default;
            return false;
        }

        int index = _opponentHistoryWriteIndex - 1 - delay;
        if (index < 0) index += _opponentHistory.Length;
        observation = _opponentHistory[index];
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
            if (_observationTick - candidate.AvailableTick > CombatHistoryWindowTicks)
                break;
            hit = candidate;
            return true;
        }
        hit = default;
        return false;
    }

    internal int RemainingHitstun(in CpuHitObservation hit)
    {
        int elapsed = Math.Max(0, _observationTick - hit.RecordedTick - hit.HitstopTicks);
        return Math.Max(0, hit.HitstunTicks - elapsed);
    }

    internal void MarkHitEvaluated(in CpuHitObservation hit)
        => _lastEvaluatedHitSequence = Math.Max(_lastEvaluatedHitSequence, hit.Sequence);

    internal void QueueFollowUp(byte slot, BotPlanKind kind, short aimYaw, short aimPitch,
        ushort aimDistance, ushort holdTicks, byte deaths, bool wasGrounded)
    {
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
    /// Reset runner-owned transient state before a match or CPU life starts. The selected
    /// difficulty is intentionally preserved across respawn and life resets.
    /// </summary>
    public void Reset()
    {
        DecisionTicksRemaining = 0;
        StrafeDirection = 0;
        ClearPlan();
        _opponentHistoryCount = 0;
        _opponentHistoryWriteIndex = 0;
        _observationTick = 0;
        _nextHitSequence = 0;
        _lastEvaluatedHitSequence = 0;
        _pendingHitCount = 0;
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
