using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.Mayhem;

/// <summary>
/// A pair's CPU: the goal child's intent, made safe against the hazard child. Pure. A slipping probe slips at whichever
/// child the CPU is working on.
/// </summary>
internal delegate MayhemCpuIntent PairPolicy(IMayhemChannel goal, IMayhemChannel hazard, in CpuProbe probe);

/// <summary>One allowed PRIME TIME! pair. A pair joins the list only once the remix certification passes it.</summary>
public sealed class PrimePairEntry
{
    internal PrimePairEntry(PrimePairId id, ChannelId goal, ChannelId hazard, string copyKey, ChannelTiming timing,
        ushort hazardLeadIn, PairPolicy cpu, bool leadInFollowsGoal = false)
    {
        Id = id; Goal = goal; Hazard = hazard; CopyKey = copyKey; Timing = timing; HazardLeadIn = hazardLeadIn; Cpu = cpu;
        LeadInFollowsGoal = leadInFollowsGoal;
    }

    public PrimePairId Id { get; }
    public ChannelId Goal { get; }
    public ChannelId Hazard { get; }
    /// <summary><c>mayhem.pair.&lt;goal&gt;-&lt;hazard&gt;</c>.</summary>
    public string CopyKey { get; }
    /// <summary>Base timing at tier one; a child twist that needs longer lengthens the airing.</summary>
    public ChannelTiming Timing { get; }
    /// <summary>Play ticks before the hazard child's first threat.</summary>
    public ushort HazardLeadIn { get; }
    /// <summary>
    /// The coupled timing certification asked for: a goal twist that plays longer than the row also delays the hazard by
    /// that much, so the hazard never lands while the goal's longer set-up still pins contestants.
    /// </summary>
    public bool LeadInFollowsGoal { get; }
    internal PairPolicy Cpu { get; }
}

/// <summary>The explicit PRIME TIME! list. No pair airs unless it is a row here.</summary>
public static class PrimePairs
{
    public static readonly PrimePairId BreakOutDodge = new(1), SpotJump = new(2), SmileJump = new(3);

    /// <summary>The channels that may fill the hazard lane, each with its late start.</summary>
    private static readonly (ChannelId Channel, Func<ChannelSetup, ushort, IMayhemChannel> Start)[] Hazards =
    {
        (MayhemChannels.Dodge, DodgeChannel.Start),
        (MayhemChannels.Jump, JumpChannel.Start),
        (MayhemChannels.Glitch, GlitchChannel.Start),
    };

    public static readonly IReadOnlyList<PrimePairEntry> All = Validated(new[]
    {
        Row<BreakOutChannel, DodgeChannel>(BreakOutDodge, MayhemChannels.BreakOut, MayhemChannels.Dodge, new(90, 360),
            PrimeTimeTuning.DodgeLeadInTicks, EscapeThenDodge, leadInFollowsGoal: true),
        Row<SpotChannel, JumpChannel>(SpotJump, MayhemChannels.Spot, MayhemChannels.Jump, new(90, 360),
            JumpTuning.FirstSlamTicks, HoldTheSpotOverRings),
        Row<SmileChannel, JumpChannel>(SmileJump, MayhemChannels.Smile, MayhemChannels.Jump, new(90, 480),
            JumpTuning.FirstSlamTicks, PoseOverRings),
    });

    public static PrimePairEntry Find(PrimePairId id)
        => All.FirstOrDefault(row => row.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id), $"Unknown PRIME TIME! pair {id.Value}.");

    /// <summary>None plus X gives X. Two different verbs cannot share one gate.</summary>
    internal static ExtraVerb FoldExtra(ExtraVerb goal, ExtraVerb hazard)
        => goal == ExtraVerb.None || goal == hazard ? hazard
            : hazard == ExtraVerb.None ? goal
            : throw new InvalidOperationException($"A remix cannot switch on both {goal} and {hazard}.");

    internal static IMayhemChannel StartHazard(PrimePairEntry row, ChannelDraw goal, ChannelSetup setup)
        => Hazards.Single(h => h.Channel == row.Hazard).Start(setup, LeadIn(row, goal, setup.Tier));

    /// <summary>The hazard's lead-in for this goal draw, in play ticks at <paramref name="tier"/>.</summary>
    public static ushort LeadIn(PrimePairEntry row, ChannelDraw goal, SpeedTier tier)
    {
        if (!row.LeadInFollowsGoal) return row.HazardLeadIn;
        int extra = MayhemSpeed.Play(MayhemChannels.Find(row.Goal).TimingFor(goal.Twist), tier) - MayhemSpeed.Play(row.Timing, tier);
        return (ushort)(row.HazardLeadIn + Math.Max(0, extra));
    }

    internal static IReadOnlyList<PrimePairEntry> Validated(PrimePairEntry[] rows)
    {
        foreach (var row in rows)
        {
            var goal = MayhemChannels.Find(row.Goal);
            var hazard = MayhemChannels.Find(row.Hazard);
            if (goal.Role != ChannelRole.Regular || hazard.Role != ChannelRole.Regular || row.Goal == row.Hazard
                || !Hazards.Any(h => h.Channel == row.Hazard) || row.CopyKey != CopyKeyFor(goal, hazard))
                throw new InvalidOperationException($"PRIME TIME! pair {row.Id.Value} is not a goal and a hazard.");
            foreach (var g in goal.Twists)
                foreach (var h in hazard.Twists)
                    FoldExtra(goal.ExtraFor(g.Id), hazard.ExtraFor(h.Id));
        }
        if (rows.Select(r => r.Id).Distinct().Count() != rows.Length || rows.Select(r => (r.Goal, r.Hazard)).Distinct().Count() != rows.Length)
            throw new InvalidOperationException("PRIME TIME! pairs repeat.");
        return Array.AsReadOnly(rows);
    }

    private static string CopyKeyFor(ChannelEntry goal, ChannelEntry hazard)
        => $"mayhem.pair.{goal.CopyKey["mayhem.channel.".Length..]}-{hazard.CopyKey["mayhem.channel.".Length..]}";

    private static PrimePairEntry Row<TGoal, THazard>(PrimePairId id, ChannelId goal, ChannelId hazard, ChannelTiming timing,
        ushort hazardLeadIn, Func<TGoal, THazard, CpuProbe, MayhemCpuIntent> cpu, bool leadInFollowsGoal = false)
        where TGoal : IMayhemChannel where THazard : IMayhemChannel
        => new(id, goal, hazard, CopyKeyFor(MayhemChannels.Find(goal), MayhemChannels.Find(hazard)), timing, hazardLeadIn,
            (IMayhemChannel g, IMayhemChannel h, in CpuProbe probe) => cpu((TGoal)g, (THazard)h, probe), leadInFollowsGoal);

    /// <summary>
    /// Boxed in, the walls fence every step, so the escape runs as BREAK OUT! plays it, slip and all; once a way out is
    /// open, every bomb is stepped around, and a slip notices them late. The cage bombs count from the call: their landings
    /// are published and every contestant knows they fall, so no way out leads into a footprint that lights up later.
    /// </summary>
    private static MayhemCpuIntent EscapeThenDodge(BreakOutChannel goal, DodgeChannel hazard, CpuProbe probe)
    {
        var intent = goal.Cpu(probe);
        var cages = goal.Observe();
        var scene = ChannelScene.Concat(cages with { Bombs = cages.Bombs.Select(bomb => bomb with { Launched = true }).ToArray() }, hazard.Observe());
        var self = probe.Self;
        return SafeStep.Boxed(scene.Solids, self.State.PX, self.State.PZ, self.Definition.CapsuleRadius)
            ? intent : SafeStep.Around(intent, scene, probe);
    }

    private static MayhemCpuIntent PoseOverRings(SmileChannel goal, JumpChannel hazard, CpuProbe probe)
        => SafeStep.Around(goal.Cpu(probe), hazard.Observe(), probe);

    private static MayhemCpuIntent HoldTheSpotOverRings(SpotChannel goal, JumpChannel hazard, CpuProbe probe)
        => SafeStep.Around(goal.Cpu(probe), hazard.Observe(), probe);
}
