using System;

namespace SlopArena.Shared.AI;

internal enum CpuMovePurpose : byte { Poke, AntiAir, FastestPunish, Kill, Approach, ComboFollowUp, Pressure, OpenShield }
internal enum CpuActionKind : byte { None, Slot, Grab }

internal readonly struct CpuExecutionBias
{
    public readonly ushort HesitationTicks;
    public readonly float RangeScale;
    public readonly sbyte Side;
    public readonly uint TieBreak;
    public CpuExecutionBias(ushort hesitation, float rangeScale, sbyte side, uint tieBreak)
    { HesitationTicks = hesitation; RangeScale = rangeScale; Side = side; TieBreak = tieBreak; }
}

internal readonly struct RecoveryOpportunity
{
    public readonly uint Episode;
    public readonly int Deadline;
    private RecoveryOpportunity(uint episode, int deadline) { Episode = episode; Deadline = deadline; }
    internal static bool TryFromDelayed(in CpuObservation target, int tick, out RecoveryOpportunity opening)
    {
        int remaining = target.Timing.LockTicks - Math.Max(0, target.AgeTicks - target.HitstopTicks);
        if (target.Timing.Phase != CpuAttackPhase.Recovery || target.Timing.RecoveryEpisode == 0 || remaining <= 0)
        { opening = default; return false; }
        opening = new RecoveryOpportunity(target.Timing.RecoveryEpisode, tick + remaining);
        return true;
    }
}

internal readonly struct CpuHitConfirm
{
    public readonly int Sequence, RemainingTicks;
    private CpuHitConfirm(int sequence, int remainingTicks) { Sequence = sequence; RemainingTicks = remainingTicks; }
    internal static bool TryFromDelayed(in CpuContactObservation contact, int tick, out CpuHitConfirm confirm)
    {
        int remaining = contact.HitstunTicks - Math.Max(0, tick - contact.RecordedTick - contact.HitstopTicks);
        if (contact.Kind != CpuContactKind.Hit || remaining <= 0) { confirm = default; return false; }
        confirm = new CpuHitConfirm(contact.Sequence, remaining); return true;
    }
}

internal readonly struct CpuFacts
{
    public readonly CharacterState Self;
    public readonly CpuObservation Target;
    public readonly CharacterDefinition Definition;
    public readonly ArenaDefinition Arena;
    public readonly CookedSlotDefinition? ActiveMove;
    public readonly BakedAnimationData? Baked;
    public readonly int Tick, OwnWaitTicks;
    public CpuFacts(in CharacterState self, in CpuObservation target, CharacterDefinition definition,
        in ArenaDefinition arena, CookedSlotDefinition? activeMove, int tick, int ownWaitTicks = 0, BakedAnimationData? baked = null)
    {
        Self = self; Target = target; Definition = definition; Arena = arena;
        ActiveMove = activeMove; Baked = baked; Tick = tick; OwnWaitTicks = ownWaitTicks;
    }
}

internal readonly struct CpuMoveRequest
{
    public readonly CpuMovePurpose Purpose;
    public readonly CpuExecutionBias Execution;
    public readonly ushort SafetyMargin;
    private readonly RecoveryOpportunity _recovery;
    private readonly CpuHitConfirm _confirm;
    private CpuMoveRequest(CpuMovePurpose purpose, in CpuExecutionBias execution, RecoveryOpportunity recovery = default, CpuHitConfirm confirm = default, ushort margin = 0)
    {
        Purpose = purpose; Execution = execution;
        _recovery = recovery; _confirm = confirm; SafetyMargin = margin;
    }
    public static CpuMoveRequest Poke(in CpuExecutionBias e) => new(CpuMovePurpose.Poke, e);
    public static CpuMoveRequest AntiAir(in CpuExecutionBias e) => new(CpuMovePurpose.AntiAir, e);
    public static CpuMoveRequest Kill(in CpuExecutionBias e) => new(CpuMovePurpose.Kill, e);
    public static CpuMoveRequest Approach(in CpuExecutionBias e) => new(CpuMovePurpose.Approach, e);
    public static CpuMoveRequest OpenShield(in CpuExecutionBias e) => new(CpuMovePurpose.OpenShield, e);
    public static CpuMoveRequest Punish(in RecoveryOpportunity r, in CpuExecutionBias e, ushort margin)
        => new(CpuMovePurpose.FastestPunish, e, recovery: r, margin: margin);
    public static CpuMoveRequest ComboFollowUp(in CpuHitConfirm c, in CpuExecutionBias e)
        => new(CpuMovePurpose.ComboFollowUp, e, confirm: c);
    public static CpuMoveRequest Pressure(in CpuExecutionBias e) => new(CpuMovePurpose.Pressure, e);
    public bool Fits(int contactTicks, int tick)
        => Purpose == CpuMovePurpose.FastestPunish ? contactTicks + SafetyMargin < _recovery.Deadline - tick
            : Purpose != CpuMovePurpose.ComboFollowUp || contactTicks <= _confirm.RemainingTicks;
}

internal readonly struct CpuMoveChoice
{
    public readonly CpuActionKind Kind;
    public readonly SlotAddress Slot;
    public readonly CpuTargetPoint Point;
    public readonly bool RequiresAim;
    public readonly int CommitmentTicks;
    public readonly bool KitResponse;
    public readonly short AimYaw, AimPitch;
    public readonly ushort AimDistance;
    public byte WireSlot => Kind == CpuActionKind.Slot ? CanonicalSlotProjection.ToWire(Slot) : (byte)0;
    public CpuMoveChoice(CpuActionKind kind, SlotAddress slot, in CpuTargetPoint point, bool requiresAim,
        int commitmentTicks, short yaw, short pitch, ushort distance, bool kitResponse = false)
    {
        KitResponse = kitResponse; Kind = kind; Slot = slot; Point = point; RequiresAim = requiresAim; CommitmentTicks = commitmentTicks;
        AimYaw = yaw; AimPitch = pitch; AimDistance = distance;
    }
}
