using System.Collections.Generic;
using System.Linq;
using SlopArena.HitReliabilityReport;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class HitReliabilityReportTests
{
    private static readonly List<SpellResolver.EntityData> Pillar = new()
    {
        new SpellResolver.EntityData
        {
            Id = 2, Shape = HitboxShape.Capsule, Active = true, Radius = 0.3f,
            PosX = 0f, PosY = 0.2f, PosZ = 2f, EndX = 0f, EndY = 1.4f, EndZ = 2f,
        },
    };

    private static Hitbox Blade(float tipX) => new()
    {
        Shape = HitboxShape.Capsule, Radius = 0.1f,
        X = 0f, Y = 1f, Z = 0f, EndX = tipX, EndY = 1f, EndZ = 2f,
    };

    [Fact]
    public void Intervals_MergeContiguousStepsAndKeepGaps()
    {
        var bands = Reliability.Intervals(new[] { 0.30f, 0.35f, 0.40f, 1.00f, 1.05f }, 0.05f);

        Assert.Equal(2, bands.Count);
        Assert.Equal((0.30f, 0.40f), bands[0]);
        Assert.Equal((1.00f, 1.05f), bands[1]);
    }

    [Fact]
    public void Swept_FindsATargetPassedBetweenTwoTestedTicks()
    {
        // The tip sweeps from 2 m left to 2 m right of a pillar 2 m ahead in one tick.
        var ticks = new[] { Blade(-2f), Blade(2f) };

        Assert.True(Reliability.Swept(ticks, Pillar));
        Assert.All(ticks, tick => Assert.True(SpellResolver.SurfaceGap(tick, Pillar[0], out _, out _, out _) > 0f));
    }

    [Fact]
    public void Swept_IgnoresATargetTheSwingNeverCrosses()
    {
        Assert.False(Reliability.Swept(new[] { Blade(-2f), Blade(-1.5f) }, Pillar));
    }

    [Fact]
    public void Flag_ReportsFastClipsPointBlankGapsLowReachAndSamplingLoss()
    {
        var move = new MoveReliability { Slot = "air.3", Air = true, PlaybackSpeed = 1.8f, ActiveTicks = 8 };
        move.Vertical.Add(new VerticalBand { Target = "Manki", Lowest = 0.2f, Highest = 2f });
        move.Vertical.Add(new VerticalBand { Target = "FightGuy", Lowest = -0.3f, Highest = 2f });
        move.Sampling.Add(new SamplingRow { SweptPositions = 100, TestedPositions = 40, TargetFeetHeight = -1f });
        move.Sampling.Add(new SamplingRow { SweptPositions = 3, TestedPositions = 0, TargetFeetHeight = -1.5f });

        Reliability.Flag(move);

        Assert.Contains(move.Flags, x => x.Contains("1.80×"));
        Assert.Contains(move.Flags, x => x.StartsWith("cannot reach below its feet vs Manki"));
        Assert.Contains(move.Flags, x => x == "reaches only 0.3 m below its feet vs FightGuy");
        Assert.Contains(move.Flags, x => x.StartsWith("60% of swept positions untested"));
        Assert.DoesNotContain(move.Flags, x => x.Contains("-1.5 m"));
    }

    [Fact]
    public void Flag_ReportsPointBlankWhiffOnlyAtTheDirectBearing()
    {
        var move = new MoveReliability { Slot = "ground.2", ActiveTicks = 10, ContactTicksAtProbe = 3 };
        move.Reach.Add(new ReachBand { Target = "Manki", BearingDegrees = 0f, Intervals = { (0.75f, 2.15f) } });
        move.Reach.Add(new ReachBand { Target = "Manki", BearingDegrees = 30f, Intervals = { (0.80f, 2.0f) } });

        Reliability.Flag(move);

        Assert.Equal("misses Manki closer than 0.75 m", Assert.Single(move.Flags));
    }
}
