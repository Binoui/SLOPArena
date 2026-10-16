using System;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class NetplayClockTests
{
    [Fact]
    public void ClockUsesAuthorityElapsedTimeAndBoundedLeadCatchup()
    {
        var clock = new NetplayClock();
        Assert.True(clock.Observe(120, 420, 10, 100));
        Assert.Equal(5u, clock.LeadTicks);
        Assert.Equal(123u, clock.EstimateTick(10));
        Assert.Equal(183u, clock.EstimateTick(11));
        Assert.Equal(188u, clock.DesiredLocalTick(11));
        Assert.Equal(4u, clock.CatchUpSteps(120, 11));
    }
    [Fact]
    public void ClockEstimateDoesNotRegressWhenLatencyEstimateShrinks()
    {
        var clock = new NetplayClock();
        Assert.True(clock.Observe(100, 301, 1, 200));
        Assert.True(clock.Observe(106, 301, 1.1, 20));
        Assert.Equal(112u, clock.EstimateTick(1.1));
        Assert.Equal(3u, clock.LeadTicks);
    }


    [Fact]
    public void ClockRejectsTimeOrTickRegressionAndBoundsLead()
    {
        var clock = new NetplayClock();
        Assert.True(clock.Observe(10, 310, 2, 20_000));
        Assert.Equal(12u, clock.LeadTicks);
        Assert.False(clock.Observe(9, 310, 3, 0));
        Assert.False(clock.Observe(11, 310, 1, 0));
        Assert.False(clock.Observe(11, 0, 3, 0));
        Assert.Equal(610u, clock.EstimateTick(1));
    }

    [Fact]
    public void ControlCodecRequiresExactVersionKindAndEntity()
    {
        var expected = new NetplayControlPacket(NetplayControlKind.Clock, 42, 500, 800, InputSlack: -3, LateInputs: 7);
        var bytes = new byte[NetplayControlPacket.Size];
        expected.Serialize(bytes);
        Assert.True(NetplayControlPacket.TryDeserialize(bytes, out var actual));
        Assert.Equal(expected, actual);
        Assert.False(NetplayControlPacket.TryDeserialize(bytes.AsSpan(0, bytes.Length - 1), out _));
        // Protocol 8 control frames were 18 bytes.
        Assert.False(NetplayControlPacket.TryDeserialize(bytes.AsSpan(0, 18), out _));
        bytes[1]++;
        Assert.False(NetplayControlPacket.TryDeserialize(bytes, out _));
    }

    [Fact]
    public void ControlFramesDefaultToNoInputTimingReport()
    {
        var bootstrap = new NetplayControlPacket(NetplayControlKind.Bootstrap, 1, 0, 0);
        Assert.Equal(NetplayControlPacket.NoInputSlack, bootstrap.InputSlack);
        Assert.Equal((byte)0, bootstrap.LateInputs);
    }

    [Fact]
    public void DelayedSampleExpiresFromTheWindowInsteadOfDraggingTheEstimate()
    {
        // A sample that left the server long before it was read must not keep the
        // estimate high forever; fresher samples take over once it leaves the window.
        var clock = new NetplayClock();
        Assert.True(clock.Observe(600, 1, 10.0, 1_000));   // claims 30 ticks of transit
        Assert.Equal(630u, clock.EstimateTick(10.0));
        double t = 10.0;
        for (int i = 1; i <= 30; i++)
        {
            t = 10.0 + i * 0.25;                            // exact binary fractions
            Assert.True(clock.Observe(600 + (uint)(i * 15), 1, t, 0));
        }
        Assert.Equal(600u + 30 * 15, clock.EstimateTick(t));
    }

    [Fact]
    public void LateInputReportRaisesLeadOnceUntilTheCooldownAndComfortLowersIt()
    {
        var clock = new NetplayClock();
        Assert.True(clock.Observe(100, 1, 1.0, 10));
        uint baseline = clock.LeadTicks;

        clock.ObserveInputTiming(0, 2, 1.0);
        Assert.Equal(baseline + 1, clock.LeadTicks);
        clock.ObserveInputTiming(-1, 1, 1.2);            // still within the propagation cooldown
        Assert.Equal(baseline + 1, clock.LeadTicks);
        clock.ObserveInputTiming(0, 1, 1.6);
        Assert.Equal(baseline + 2, clock.LeadTicks);

        clock.ObserveInputTiming(2, 0, 2.0);             // healthy but not comfortable: hold
        Assert.Equal(baseline + 2, clock.LeadTicks);
        for (int i = 0; i <= 18; i++)                    // 2.0 s .. 6.5 s of comfortable slack
            clock.ObserveInputTiming(4, 0, 2.0 + i * 0.25);
        Assert.Equal(baseline + 1, clock.LeadTicks);

        clock.ObserveInputTiming(NetplayControlPacket.NoInputSlack, 0, 7.0);   // no samples: ignored
        Assert.Equal(baseline + 1, clock.LeadTicks);
    }

    [Fact]
    public void LeadNeverDropsBelowTheMinimum()
    {
        var clock = new NetplayClock();
        Assert.True(clock.Observe(100, 1, 1.0, 0));
        for (double now = 1; now < 60; now += 0.1)
            clock.ObserveInputTiming(20, 0, now);
        Assert.Equal(NetplayClock.MinimumLeadTicks, clock.LeadTicks);
    }

    [Fact]
    public void PacedStepsAdvanceOneTickPerUpdateDespiteSampleAndFrameJitter()
    {
        // Frame-quantised updates sampling a wall-clock frontier used to alternate 0/2
        // steps many times per second. Pacing keeps a single step per update.
        var rng = new Random(11);
        var clock = new NetplayClock();
        const double fixedStep = 1d / 60d;
        const double frame = 1d / 143.88d;
        double t = 5.0, fixedTime = 5.0, nextClock = 5.0;
        uint serverStart = 300, local = 0;
        bool seeded = false;
        int irregular = 0, updates = 0;
        while (t < 65.0)
        {
            t += frame + (rng.NextDouble() - 0.5) * 0.002;
            while (fixedTime + fixedStep <= t)
            {
                fixedTime += fixedStep;
                if (t >= nextClock)
                {
                    uint authority = serverStart + (uint)Math.Floor((nextClock - 5.0) * 60);
                    double arrival = nextClock + 0.005 + rng.NextDouble() * 0.012;
                    if (arrival <= t) { clock.Observe(authority, 1, arrival, 10); nextClock += 0.1; }
                }
                if (!clock.IsSeeded) continue;
                if (!seeded) { local = clock.DesiredLocalTick(t) - 1; seeded = true; }
                uint steps = clock.StepsForUpdate(local, t);
                local += steps;
                updates++;
                if (t > 10 && steps != 1) irregular++;
            }
        }
        Assert.True(updates > 3000);
        Assert.True(irregular <= 10, $"{irregular} irregular local steps in 55 s");
        long ahead = (long)local - (serverStart + (long)Math.Floor((t - 5.0) * 60));
        Assert.InRange(ahead, 1, 6);
    }

    [Fact]
    public void PacerCatchesUpAtOnceWhenFarBehindAndSlowsWhenFarAhead()
    {
        var pacer = new TickPacer();
        Assert.Equal(4u, pacer.Steps(100, 120, NetplayClock.MaximumCatchUpSteps));
        uint steps = 0;
        for (int i = 0; i < 10; i++)
            steps += pacer.Steps(200, 180, NetplayClock.MaximumCatchUpSteps);
        Assert.Equal(5u, steps);
        pacer.Reset();
        Assert.Equal(1u, pacer.Steps(200, 201, NetplayClock.MaximumCatchUpSteps));
    }
}
