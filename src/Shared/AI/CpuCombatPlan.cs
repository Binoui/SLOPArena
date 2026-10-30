namespace SlopArena.Shared.AI;

internal enum CpuCombatPhase : byte { Closing, JumpHold, AirStrike, DashPress, DashTravel, Arrival, Waiting, Executing, Result, Settle, Watch, Hold }
internal enum CpuExecutionResult : byte { Continuing, Accepted, Rejected }

internal struct CpuCombatPlan
{
    public CpuCombatPlanKind Kind { get; private set; }
    public CpuApproachKind Approach { get; private set; }
    public CpuCombatPhase Phase { get; private set; }
    public CpuExecutionBias Execution { get; private set; }
    public int Started { get; private set; }
    public int ReadyAt { get; private set; }
    public int Expires { get; private set; }
    public byte Life { get; private set; }
    public ulong Opponent { get; private set; }
    public RecoveryOpportunity Opening { get; private set; }
    public CpuMoveChoice Pending { get; private set; }
    public CpuContactObservation Contact { get; private set; }
    public int Remaining { get; private set; }
    public int Issued { get; private set; }
    public uint Threat { get; private set; }
    public float Margin { get; private set; }
    public float ProgressX { get; private set; }
    public float ProgressZ { get; private set; }
    public int ProgressAt { get; private set; }
    public bool RangedUsed { get; private set; }
    public bool RangedChecked { get; private set; }
    public bool RangedAction { get; private set; }
    public int ContactLead { get; private set; }
    public bool PressureOnly { get; private set; }
    public bool DodgeIssued { get; private set; }
    public bool SpacingShot { get; private set; }

    private CpuCombatPlan(CpuCombatPlanKind kind, in CpuFacts f, in CpuExecutionBias bias, ulong opponent, int limit)
    {
        this = default; Kind = kind; Execution = bias; Opponent = opponent; Life = f.Self.Deaths;
        Started = f.Tick; ReadyAt = f.Tick + f.OwnWaitTicks + bias.HesitationTicks; Expires = f.Tick + limit;
        ProgressX = f.Self.PX; ProgressZ = f.Self.PZ; ProgressAt = f.Tick;
        Margin = .3f + (bias.TieBreak % 501) / 1000f;
    }
    public static CpuCombatPlan ApproachPlan(CpuApproachKind variant, in CpuFacts f, in CpuExecutionBias e, ulong opponent)
        => new(CpuCombatPlanKind.Approach, f, e, opponent, variant == CpuApproachKind.WalkInPoke ? 120 : 90)
        { Approach = variant, Phase = variant == CpuApproachKind.Beeline ? CpuCombatPhase.DashPress : CpuCombatPhase.Closing };
    public static CpuCombatPlan Space(in CpuFacts f, in CpuExecutionBias e, ulong opponent)
        => new(CpuCombatPlanKind.Space, f, e, opponent, 60) { Phase = CpuCombatPhase.Settle };
    public static CpuCombatPlan Punish(in RecoveryOpportunity opening, in CpuMoveChoice move, in CpuFacts f, in CpuExecutionBias e, ulong opponent)
        => new(CpuCombatPlanKind.Punish, f, e, opponent, 120) { Opening = opening, Pending = move, ContactLead = System.Math.Max(0, move.Point.ContactTicks - f.OwnWaitTicks - e.HesitationTicks), Phase = CpuCombatPhase.Waiting };
    public static CpuCombatPlan Pressure(in CpuContactObservation contact, int budget, in CpuFacts f, in CpuExecutionBias e, ulong opponent)
        => new(CpuCombatPlanKind.Pressure, f, e, opponent, 120) { Contact = contact, Remaining = budget, Phase = CpuCombatPhase.Waiting };
    public static CpuCombatPlan Defend(uint episode, in CpuFacts f, in CpuExecutionBias e, ulong opponent)
        => new(CpuCombatPlanKind.Defend, f, e, opponent, 60) { Threat = episode, Phase = CpuCombatPhase.Hold };
    public static CpuCombatPlan Edgeguard(in CpuFacts f, in CpuExecutionBias e, ulong opponent)
        => new(CpuCombatPlanKind.Edgeguard, f, e, opponent, 60) { Phase = CpuCombatPhase.Watch };
    public void BeginAction(int tick)
    { Phase = CpuCombatPhase.Executing; Pending = default; Issued = tick; if (Kind == CpuCombatPlanKind.Pressure) Remaining--; }
    public void Accepted() => Phase = CpuCombatPhase.Result;
    public void NextFollowUp(in CpuContactObservation contact, int tick)
    { Contact = contact; PressureOnly = false; Phase = CpuCombatPhase.Waiting; ReadyAt = tick; }
    public void Settle(int tick)
    { Phase = CpuCombatPhase.Watch; ReadyAt = tick + 18 + (int)(Execution.TieBreak % 13); }
    public void Jump(int tick) { Phase = CpuCombatPhase.JumpHold; Issued = tick; }
    public void Airborne() => Phase = CpuCombatPhase.AirStrike;
    public void Dash(int tick) { Phase = CpuCombatPhase.DashTravel; Issued = tick; }
    public void Arrived() { Phase = CpuCombatPhase.Arrival; ReadyAt = 0; }
    public void Progress(in CharacterState self, int tick) { ProgressX = self.PX; ProgressZ = self.PZ; ProgressAt = tick; }
    public void ResumeClosing() { Phase = CpuCombatPhase.Closing; RangedAction = false; }
    public void RememberEntry(in CpuMoveChoice choice) { RangedChecked = true; Pending = choice; }
    public void DiscardPending() => Pending = default;
    public void CheckRanged() => RangedChecked = true;
    public void UseRanged() { RangedUsed = RangedAction = true; }
    public void TryPressure() => PressureOnly = true;
    public void Dodge() => DodgeIssued = true;
    public void WatchAfterAction(int tick) { SpacingShot = true; Phase = CpuCombatPhase.Watch; ReadyAt = tick + 18; }
}
