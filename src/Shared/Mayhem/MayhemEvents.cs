namespace SlopArena.Shared.Mayhem;

public enum MayhemEventKind : byte
{
    // Show and phase edges: host subject, operation 0. SpeedUp's value is the act it opens.
    Intro, Call, Play, Verdict, SpeedUp, Ended,
    // Host hazard cues: operation is the source ordinal within the lane.
    WindUp, Launch, Slam, WatchWarning, WatchStart, WatchEnd,
    // Hits on a channel solid or target: subject is the object, operation packs the attacker and activation.
    // Value is the hits left after it.
    SolidHit, SolidBroken, TargetHit, TargetBroken,
    // Verdicts: subject is the contestant. Pass carries its award; Fail packs cause and splat style.
    Pass, Fail, Impact,
}

/// <summary>Stable across re-simulation: every field is a function of the state that emitted it.</summary>
public readonly record struct MayhemEventKey(uint MatchTick, ushort Episode, byte Lane, ulong Operation,
    MayhemEventKind Kind, ulong Subject);

public readonly record struct MayhemEvent(uint MatchTick, ushort Episode, byte Lane, ulong Operation,
    MayhemEventKind Kind, ulong Subject, ushort Value = 0)
{
    /// <summary>Episode field of show-level events (Intro, SpeedUp, Ended).</summary>
    public const ushort ShowEpisode = ushort.MaxValue;
    public const byte MaxLane = 1;
    /// <summary>Focused Mayhem targets (board stacks) take identities after the runtime solids.</summary>
    public const ulong FirstTargetEntityId = RuntimeSolidSet.FirstEntityId + 64;
    public const int MaxTargets = 8;

    public MayhemEventKey Key => new(MatchTick, Episode, Lane, Operation, Kind, Subject);

    public FailCause FailCause => (FailCause)(Value & 0xFF);
    public SplatStyle SplatStyle => (SplatStyle)(Value >> 8);

    public static bool IsTargetId(ulong id) => id >= FirstTargetEntityId && id < FirstTargetEntityId + MaxTargets;

    /// <summary>The operation of a hit on a solid or target: one per attacker activation and object.</summary>
    public static ulong HitOperation(ulong attacker, ulong activation) => (attacker & 0xFFFF) << 48 | (activation & 0xFFFF_FFFF_FFFFUL);

    internal static ushort FailValue(FailCause cause, SplatStyle style) => (ushort)((byte)cause | (byte)style << 8);

    /// <summary>Range checks shared by the World Frame codec and the rollback ledger.</summary>
    public static bool IsValid(in MayhemEvent e, ulong hostId)
    {
        if (e.Kind > MayhemEnumLimits.Event || e.Lane > MaxLane) return false;
        bool show = e.Kind is MayhemEventKind.Intro or MayhemEventKind.SpeedUp or MayhemEventKind.Ended;
        if (show ? e.Episode != ShowEpisode : e.Episode >= MayhemShowTuning.Episodes) return false;
        return e.Kind switch
        {
            MayhemEventKind.Intro or MayhemEventKind.Ended => e.Subject == hostId && e.Operation == 0 && e.Value == 0 && e.Lane == 0,
            MayhemEventKind.SpeedUp => e.Subject == hostId && e.Operation == 0 && e.Value is >= 1 and < MayhemShowTuning.Acts && e.Lane == 0,
            MayhemEventKind.Call or MayhemEventKind.Play or MayhemEventKind.Verdict
                => e.Subject == hostId && e.Operation == 0 && e.Value == 0 && e.Lane == 0,
            MayhemEventKind.WindUp or MayhemEventKind.Launch or MayhemEventKind.Slam or MayhemEventKind.WatchWarning
                or MayhemEventKind.WatchStart or MayhemEventKind.WatchEnd or MayhemEventKind.Impact
                => e.Subject == hostId && e.Operation < 256 && e.Value == 0,
            MayhemEventKind.SolidHit or MayhemEventKind.SolidBroken
                => RuntimeSolidSet.IsWallId(e.Subject) && e.Value <= byte.MaxValue && (e.Kind != MayhemEventKind.SolidBroken || e.Value == 0),
            MayhemEventKind.TargetHit or MayhemEventKind.TargetBroken
                => IsTargetId(e.Subject) && e.Value <= byte.MaxValue && (e.Kind != MayhemEventKind.TargetBroken || e.Value == 0),
            MayhemEventKind.Pass => IsFighterId(e.Subject, hostId) && e.Operation == 0 && e.Lane == 0
                && e.Value == MayhemShowPlan.Award(e.Episode),
            MayhemEventKind.Fail => IsFighterId(e.Subject, hostId) && e.Operation == 0 && e.Lane == 0
                && (e.Value & 0xFF) <= (int)MayhemEnumLimits.Failure && e.Value >> 8 <= (int)MayhemEnumLimits.Splat,
        };
    }

    private static bool IsFighterId(ulong id, ulong hostId) => id is > 0 and < ArcadeBallSimulation.FirstEntityId && id != hostId;
}
