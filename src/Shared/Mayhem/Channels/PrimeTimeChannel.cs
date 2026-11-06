namespace SlopArena.Shared.Mayhem;

/// <summary>PRIME TIME! tuning. Provisional.</summary>
public static class PrimeTimeTuning
{
    /// <summary>The classic's DODGE! volley starts this long into play, once the cages have had a chance to break.</summary>
    public const ushort DodgeLeadInTicks = 140;
}

/// <summary>
/// PRIME TIME! (boss): one pair row's goal child in lane 0 and hazard child in lane 1, on one clock with one verdict.
/// A failure from either child wins, the goal's first; Ready needs both. The children never become episodes or award ratings.
/// </summary>
internal sealed class PrimeTimeChannel : IMayhemChannel
{
    private const byte GoalLane = 0, HazardLane = 1;
    private readonly PrimePairEntry _row;
    private readonly IMayhemChannel _goal, _hazard;

    private PrimeTimeChannel(PrimePairEntry row, IMayhemChannel goal, IMayhemChannel hazard)
    { _row = row; _goal = goal; _hazard = hazard; }

    internal static PrimeTimeChannel Start(ChannelSetup setup)
    {
        var remix = (RemixAiring)setup.Airing;
        var row = PrimePairs.Find(remix.Pair);
        setup = setup with { Tier = SpeedTier.Three };
        return new PrimeTimeChannel(row,
            MayhemChannels.Find(row.Goal).Create(setup with { Airing = new SoloAiring(remix.Goal), Lane = GoalLane }),
            PrimePairs.StartHazard(row, remix.Goal, setup with { Airing = new SoloAiring(remix.Hazard), Lane = HazardLane }));
    }

    public void Tick(in ChannelTick tick, MayhemHazards hazards)
    {
        _goal.Tick(tick, hazards);
        _hazard.Tick(tick, hazards);
    }

    public ChannelContactResult Contact(in MayhemContact contact, MayhemHazards hazards)
    {
        switch (contact.Kind)
        {
            case MayhemContactKind.HazardBody:
            case MayhemContactKind.BombImpact:
                return Source(contact).Contact(contact, hazards);
            case MayhemContactKind.SolidHit:
                return Owner(contact.Target)?.Contact(contact, hazards) ?? ChannelContactResult.Ignored;
            case MayhemContactKind.HostBlast:
                var source = Source(contact);
                var result = source.Contact(contact, hazards);
                if (Owner(contact.Target) is { } owner && owner != source)
                {
                    var struck = owner.Contact(contact, hazards);
                    if (result.FailSeat < 0) result = struck;
                }
                return result;
        }
        return ChannelContactResult.Ignored;
    }

    private IMayhemChannel Source(in MayhemContact contact) => contact.Lane == GoalLane ? _goal : _hazard;

    /// <summary>The child whose scene holds the object. At most one child of a pair has solids or targets.</summary>
    private IMayhemChannel? Owner(ulong entity)
    {
        foreach (var child in new[] { _goal, _hazard })
        {
            var scene = child.Observe();
            foreach (var wall in scene.Solids) if (wall.EntityId == entity) return child;
            foreach (var target in scene.Targets) if (target.EntityId == entity) return child;
        }
        return null;
    }

    public MayhemJudgement Judge(in ContestantProbe probe)
    {
        var goal = _goal.Judge(probe);
        var hazard = _hazard.Judge(probe);
        if (goal.Kind == JudgementKind.FailNow) return goal;
        if (hazard.Kind == JudgementKind.FailNow) return hazard;
        return goal.Kind == JudgementKind.Incomplete ? goal : hazard;
    }

    public MayhemCpuIntent Cpu(in CpuProbe probe) => _row.Cpu(_goal, _hazard, probe);

    public ChannelScene Observe() => ChannelScene.Concat(_goal.Observe(), _hazard.Observe());

    public void WriteSnapshot(SnapshotWriter w)
    {
        _goal.WriteSnapshot(w);
        _hazard.WriteSnapshot(w);
    }

    /// <summary>The pair and both draws come from the plan; each child validates its own bytes against them.</summary>
    internal static PrimeTimeChannel Read(SnapshotReader r, ChannelRestore context)
    {
        var remix = (RemixAiring)context.Airing;
        var row = PrimePairs.Find(remix.Pair);
        context = context with { Tier = SpeedTier.Three };
        var goal = MayhemChannels.Find(row.Goal).Read(r, context with { Airing = new SoloAiring(remix.Goal), Lane = GoalLane });
        var hazard = MayhemChannels.Find(row.Hazard).Read(r, context with { Airing = new SoloAiring(remix.Hazard), Lane = HazardLane });
        return new PrimeTimeChannel(row, goal, hazard);
    }
}
