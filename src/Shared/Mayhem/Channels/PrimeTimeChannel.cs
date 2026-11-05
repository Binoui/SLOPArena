namespace SlopArena.Shared.Mayhem;

/// <summary>PRIME TIME! tuning. Provisional.</summary>
public static class PrimeTimeTuning
{
    /// <summary>The DODGE! volley starts this long into play, once the cages have had a chance to break.</summary>
    public const ushort DodgeFirstLaunchTicks = 140;
}

/// <summary>
/// PRIME TIME! (boss): BREAK OUT! in lane 0 and DODGE! in lane 1 at the top tier, one clock, one verdict.
/// A failure from either child wins; Ready needs both. The children never become episodes or award ratings.
/// </summary>
internal sealed class PrimeTimeChannel : IMayhemChannel
{
    private const byte EscapeLane = 0, BombLane = 1;
    private readonly BreakOutChannel _escape;
    private readonly DodgeChannel _bombs;

    private PrimeTimeChannel(BreakOutChannel escape, DodgeChannel bombs) { _escape = escape; _bombs = bombs; }

    internal static PrimeTimeChannel Start(ChannelSetup setup)
    {
        var top = setup with { Tier = SpeedTier.Three };
        var escape = BreakOutChannel.Start(top with { Lane = EscapeLane, Seed = MayhemMath.Sample(setup.Seed, EscapeLane, 1) | 1 });
        var bombs = DodgeChannel.Start(top with { Lane = BombLane, Seed = MayhemMath.Sample(setup.Seed, BombLane, 1) | 1 },
            PrimeTimeTuning.DodgeFirstLaunchTicks);
        return new PrimeTimeChannel(escape, bombs);
    }

    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        _escape.Tick(tick, hazards);
        _bombs.Tick(tick, hazards);
    }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
        => contact.Kind is MayhemContactKind.SolidHit or MayhemContactKind.HostBlast || contact.Lane == EscapeLane
            ? _escape.Contact(contact, hazards) : _bombs.Contact(contact, hazards);

    public MayhemJudgement Judge(in ContestantProbe probe)
    {
        var escape = _escape.Judge(probe);
        var bombs = _bombs.Judge(probe);
        if (escape.Kind == JudgementKind.FailNow) return escape;
        if (bombs.Kind == JudgementKind.FailNow) return bombs;
        if (escape.Kind == JudgementKind.Incomplete) return escape;
        return bombs;
    }

    public MayhemCpuIntent Cpu(in CpuProbe probe)
    {
        var self = probe.Self.State;
        if (!_escape.Escaped(probe.Self.Seat, self.PX, self.PZ)) return _escape.Cpu(probe);
        var dangers = _bombs.Dangers(probe.Tick, probe.Self.Definition.CapsuleRadius);
        _escape.AddDangers(dangers, probe.Self.Definition.CapsuleRadius);
        return DodgeChannel.Avoid(probe.Frame.Stage, self, dangers);
    }

    public ChannelScene Observe() => ChannelScene.Concat(_escape.Observe(), _bombs.Observe());

    public void WriteSnapshot(SnapshotWriter w)
    {
        _escape.WriteSnapshot(w);
        _bombs.WriteSnapshot(w);
    }

    internal static PrimeTimeChannel Read(SnapshotReader r, ChannelRestore context)
    {
        var escape = BreakOutChannel.Read(r, context with { Id = MayhemChannels.BreakOut, Lane = EscapeLane });
        var bombs = DodgeChannel.Read(r, context with { Id = MayhemChannels.Dodge, Lane = BombLane });
        if (!escape.HasLane(EscapeLane) || !bombs.HasLane(BombLane)) throw SnapshotReader.Malformed("PRIME TIME! lanes");
        return new PrimeTimeChannel(escape, bombs);
    }
}
