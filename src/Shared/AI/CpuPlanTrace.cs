namespace SlopArena.Shared.AI;

public enum CpuCombatPlanKind : byte { None, Approach, Space, Punish, Pressure, Defend, Edgeguard }
public enum CpuApproachKind : byte { WalkInPoke, JumpIn, Beeline }
public enum CpuPlanEnd : byte { None, Completed, Blocked, Expired, Rejected, Interrupted, Escaped, Whiff }

public readonly struct CpuPlanTrace
{
    public readonly uint Serial;
    public readonly CpuCombatPlanKind Kind;
    public readonly CpuApproachKind Approach;
    public readonly int StartedTick;
    public readonly byte Phase;
    public readonly CpuPlanEnd LastEnd;
    public readonly int ThreatEvaluations, RecoveryEvaluations, ContactEvaluations;
    public CpuPlanTrace(uint serial, CpuCombatPlanKind kind, CpuApproachKind approach, int startedTick,
        byte phase, CpuPlanEnd lastEnd, int threats = 0, int recoveries = 0, int contacts = 0)
    {
        Serial = serial; Kind = kind; Approach = approach; StartedTick = startedTick;
        Phase = phase; LastEnd = lastEnd;
        ThreatEvaluations = threats; RecoveryEvaluations = recoveries; ContactEvaluations = contacts;
    }
}
