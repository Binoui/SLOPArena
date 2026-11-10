using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.Mayhem;

public readonly record struct TwistId(byte Value);
public readonly record struct PrimePairId(byte Value);
public readonly record struct ChannelDraw(ChannelId Channel, TwistId Twist, uint Seed);
public enum ChannelContest : byte { Shared, Scarce }
public sealed record TwistEntry(TwistId Id, string CopyKey, ChannelTiming? Timing = null, ExtraVerb? Extra = null);

public abstract record MayhemAiring
{
    private protected MayhemAiring(ExtraVerb extra, ChannelTiming timing) { Extra = extra; Timing = timing; }
    public abstract ChannelId Channel { get; }
    public ExtraVerb Extra { get; }
    public ChannelTiming Timing { get; }
    /// <summary>The seed every CPU slip roll of this airing uses.</summary>
    internal abstract uint SlipSeed { get; }

    private protected static ExtraVerb ExtraOf(ChannelDraw draw) => MayhemChannels.Find(draw.Channel).ExtraFor(draw.Twist);
    private protected static ChannelTiming TimingOf(ChannelDraw draw) => MayhemChannels.Find(draw.Channel).TimingFor(draw.Twist);
    private protected static string TwistCopyKeyOf(ChannelDraw draw) => MayhemChannels.Find(draw.Channel).Twist(draw.Twist).CopyKey;
}

public sealed record SoloAiring : MayhemAiring
{
    public SoloAiring(ChannelDraw draw) : base(ExtraOf(draw), TimingOf(draw)) => Draw = draw;
    public ChannelDraw Draw { get; }
    public override ChannelId Channel => Draw.Channel;
    public string TwistCopyKey => TwistCopyKeyOf(Draw);
    internal override uint SlipSeed => Draw.Seed;
}

/// <summary>
/// A PRIME TIME! remix: the pair row's goal child in lane 0 and hazard child in lane 1, each with its own twist and
/// seed. Timing is the row's, lengthened to fit either child's twist; the extra verb folds the children's.
/// </summary>
public sealed record RemixAiring : MayhemAiring
{
    public RemixAiring(PrimePairId pair, ChannelDraw goal, ChannelDraw hazard)
        : base(PrimePairs.FoldExtra(ExtraOf(goal), ExtraOf(hazard)), Longest(PrimePairs.Find(pair).Timing, TimingOf(goal), TimingOf(hazard)))
    {
        var row = PrimePairs.Find(pair);
        if (goal.Channel != row.Goal || hazard.Channel != row.Hazard)
            throw new ArgumentException($"{row.CopyKey} pairs {row.Goal} with {row.Hazard}.");
        Pair = pair; Goal = goal; Hazard = hazard;
    }

    public PrimePairId Pair { get; }
    public ChannelDraw Goal { get; }
    public ChannelDraw Hazard { get; }
    public override ChannelId Channel => MayhemChannels.PrimeTime;
    public string PairCopyKey => PrimePairs.Find(Pair).CopyKey;
    public string GoalTwistCopyKey => TwistCopyKeyOf(Goal);
    public string HazardTwistCopyKey => TwistCopyKeyOf(Hazard);
    internal override uint SlipSeed => Goal.Seed;

    private static ChannelTiming Longest(params ChannelTiming[] timings)
        => new(timings.Max(t => t.CallTicks), timings.Max(t => t.PlayTicks));
}

internal static class TwistRows
{
    internal static IReadOnlyList<TwistEntry> For<T>(string channel, params TwistEntry[] rows) where T : struct, Enum
    {
        var values = Enum.GetValues(typeof(T)).Cast<T>().Select(v => Convert.ToByte(v)).OrderBy(v => v).ToArray();
        if (!values.SequenceEqual(rows.Select(r => r.Id.Value).OrderBy(v => v))
            || rows.Select(r => r.CopyKey).Distinct(StringComparer.Ordinal).Count() != rows.Length
            || rows.Any(r => !r.CopyKey.StartsWith($"mayhem.twist.{channel}.", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Twist rows do not match {typeof(T).Name}.");
        return Array.AsReadOnly(rows);
    }
}
