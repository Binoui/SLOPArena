using System;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemTwistTests
{
    [Theory]
    [MemberData(nameof(MayhemChannelTests.ChannelTiers), MemberType = typeof(MayhemChannelTests))]
    public void EveryTwistFinishesItsHazardsBeforeTheDeadline(ushort channel, SpeedTier tier, byte twist)
    {
        var (sim, episode) = MayhemWorld.Airing(new(channel), tier, twist: new(twist));
        MayhemWorld.Step(sim);
        uint deadline = MayhemWorld.Plan(sim).Deadline(episode);
        var scene = MayhemWorld.View(sim).Scene;
        Assert.All(scene.Bombs, b => Assert.True(b.ImpactTick + MayhemHazardTuning.BlastTicks + 1 < deadline));
        Assert.All(scene.Rings, r => Assert.True(r.SlamTick + r.LifetimeTicks < deadline));
    }

    [Fact]
    public void EveryNewTwistLetsASlippingCpuLose()
    {
        foreach (var entry in MayhemChannels.Regulars)
        foreach (var twist in entry.Twists.Where(t => t.Id.Value != 1))
        {
            var (sim, episode) = MayhemWorld.Airing(entry.Id, SpeedTier.One, roster: MayhemWorld.Four, twist: twist.Id);
            var cpus = sim.MayhemShow!.Roster.ToDictionary(id => id, _ => new MayhemCpu(0, 100));
            var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.StepCpus(s, cpus));
            Assert.True(passed.Any(p => !p), $"{twist.CopyKey} never loses with four slipping CPUs");
        }
    }

    [Fact]
    public void BombRainRowsKeepAReachableGapAndBigOneHasOneLargeBlast()
    {
        var (rain, _) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, twist: new(2));
        MayhemWorld.Step(rain);
        var rows = MayhemWorld.View(rain).Scene.Bombs.GroupBy(b => b.LaunchTick).ToArray();
        Assert.Equal(DodgeTuning.RainRows, rows.Length);
        foreach (var row in rows)
        {
            var bombs = row.OrderBy(b => b.Landing.X).ToArray();
            Assert.Equal(4, bombs.Length);
            Assert.Single(bombs.Select(b => b.Landing.Z).Distinct());
            Assert.Contains(bombs.Zip(bombs.Skip(1)), pair => pair.Second.Landing.X - pair.First.Landing.X
                - pair.First.BlastRadius - pair.Second.BlastRadius > 2);
        }
        var stage = MayhemWorld.Arena().Arcade!.Mayhem!;
        var gaps = rows.Select(row => Enumerable.Range(1, 3).Select(c => (c - 2) * DodgeTuning.RainSpacing)
            .Single(x => row.All(b => MathF.Abs(b.Landing.X - x) > .01f))).ToArray();
        Assert.Equal(3, gaps.Distinct().Count());
        for (int row = 0; row < rows.Length; row++)
        for (int xi = 0; xi <= 240; xi++)
        for (int zi = 0; zi <= 200; zi++)
        {
            float x = stage.MinX + xi * .1f, z = stage.MinZ + zi * .1f;
            float near = stage.MinZ + row * (stage.MaxZ - stage.MinZ) / rows.Length;
            float far = stage.MinZ + (row + 1) * (stage.MaxZ - stage.MinZ) / rows.Length;
            if (z < near || z > far || MathF.Abs(x - gaps[row]) < DodgeTuning.RainSpacing / 2) continue;
            Assert.Contains(rows[row], b => MayhemMath.DistanceSquared(x, z, b.Landing.X, b.Landing.Z) <= b.BlastRadius * b.BlastRadius);
        }
        var (careful, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.Three, twist: new(2));
        var cpus = MayhemWorld.Cpus(careful, 0);
        Assert.All(MayhemWorld.RunEpisode(careful, episode, s => MayhemWorld.StepCpus(s, cpus)), p => Assert.True(p));
        var (big, _) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, twist: new(3));
        MayhemWorld.Step(big);
        Assert.True(Assert.Single(MayhemWorld.View(big).Scene.Bombs).BlastRadius > DodgeTuning.BlastRadius * 2);
    }

    [Fact]
    public void CrossfirePublishesTwoSlamPointsWhoseRingsCross()
    {
        var (sim, _) = MayhemWorld.Airing(MayhemChannels.Jump, SpeedTier.One, twist: new(2));
        MayhemWorld.Step(sim);
        var rings = MayhemWorld.View(sim).Scene.Rings;
        var first = rings.Where(r => r.SlamTick == rings[0].SlamTick).ToArray();
        Assert.Equal(2, first.Length);
        Assert.NotEqual(first[0].Center, first[1].Center);
        float separation = MathF.Sqrt(MayhemMath.DistanceSquared(first[0].Center.X, first[0].Center.Z, first[1].Center.X, first[1].Center.Z));
        Assert.True(first.All(r => r.Speed * r.LifetimeTicks / 60 > separation));
    }

    [Fact]
    public void AnyWallBreaksAndTheBombLandsOnASide()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakOut, SpeedTier.One, twist: new(2));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        var scene = MayhemWorld.View(sim).Scene;
        Assert.All(scene.Solids, w => Assert.Equal(RuntimeSolidTuning.CrackedWallHits, w.HitBudget));
        var cage = scene.Solids[0].CageCenter;
        Assert.True(scene.Bombs.Any(b => MayhemMath.DistanceSquared(b.Landing.X, b.Landing.Z, cage.X, cage.Z) > 1));
        foreach (var wall in scene.Solids.ToArray())
        for (ulong hit = 1; hit <= wall.HitBudget; hit++)
            sim.MayhemShow!.Contact(new(MayhemContactKind.SolidHit, 0, 0, wall.CageEntityId, wall.EntityId, -1, 1000 + hit), sim.CurrentTick, sim);
        Assert.All(MayhemWorld.View(sim).Scene.Solids, w => Assert.False(w.Blocks));
    }

    [Fact]
    public void DoubleCageHasThirtyTwoDistinctWallsAndCarefulCpusBreakBothRings()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakOut, SpeedTier.One, roster: MayhemWorld.Four, twist: new(3));
        var cpus = MayhemWorld.Cpus(sim, 0);
        MayhemWorld.StepCpus(sim, cpus);
        var walls = MayhemWorld.View(sim).Scene.Solids;
        Assert.Equal(32, walls.Count);
        Assert.Equal(32, walls.Select(w => w.EntityId).Distinct().Count());
        Assert.True(walls.All(w => w.EntityId < MayhemEvent.FirstTargetEntityId));
        var broken = new System.Collections.Generic.HashSet<ulong>();
        var passed = MayhemWorld.RunEpisode(sim, episode, s =>
        {
            MayhemWorld.StepCpus(s, cpus);
            foreach (var e in s.LastTickMayhemEvents.Where(e => e.Kind == MayhemEventKind.SolidBroken)) broken.Add(e.Subject);
        });
        Assert.All(passed, p => Assert.True(p));
        foreach (var seat in Enumerable.Range(0, 4))
        {
            Assert.Contains(broken, id => id >= RuntimeSolidSet.FirstEntityId + (ulong)(seat * 4) && id < RuntimeSolidSet.FirstEntityId + (ulong)(seat * 4 + 4));
            Assert.Contains(broken, id => id >= RuntimeSolidSet.FirstEntityId + (ulong)((seat + 4) * 4) && id < RuntimeSolidSet.FirstEntityId + (ulong)((seat + 4) * 4 + 4));
        }
    }

    [Fact]
    public void AnalyticZonesShareInterpolatedGeometryAndClampOutsideTheirWindow()
    {
        var zone = new ZoneCue(0, 0, MayhemZoneKind.Spotlight, new(0, 0, 0), 2, 100, 200, new(10, 0, 0), 1, 150);
        Assert.Equal(new ArcadePoint(5, 0, 0), zone.CenterAt(150));
        Assert.Equal(1.5f, zone.RadiusAt(150));
        Assert.True(zone.Contains(6.5f, 0, 150));
        Assert.False(zone.Contains(6.6f, 0, 150));
        Assert.Equal(zone.Center, zone.CenterAt(0));
        Assert.Equal(zone.EndCenter, zone.CenterAt(uint.MaxValue));
        Assert.False(zone.ArmedAt(149));
        Assert.True(zone.ArmedAt(150));
        Assert.False(zone.ArmedAt(200));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SpotlightVerdictUsesTheFinalAnalyticPlace(byte twist)
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2, twist: new(twist));
        uint deadline = MayhemWorld.Plan(sim).Deadline(episode);
        MayhemDirectorTests.RunTo(sim, deadline - 1);
        var zones = MayhemWorld.View(sim).Scene.Zones;
        var last = Assert.Single(zones.Where(z => z.ArmedAt(deadline - 1)));
        var center = last.CenterAt(deadline - 1);
        MayhemDirectorTests.Place(sim, 1, center.X, center.Z);
        MayhemWorld.Step(sim);
        Assert.Equal(MayhemVerdict.Passed, MayhemWorld.View(sim).Contestants[0].Verdict);
        if (twist == 2) Assert.NotEqual(last.Center, last.EndCenter);
        else
        {
            Assert.Equal(SpotTuning.MusicalRounds, zones.Count);
            Assert.All(zones.Take(zones.Count - 1), z => Assert.Equal(z.ActiveUntil, z.ArmedFrom));
        }
    }

    [Fact]
    public void FakeLookNeverJudgesMovementButTheFollowingRealLookDoes()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.NoRunning, SpeedTier.One, count: 2, twist: new(2));
        MayhemWorld.Step(sim);
        var watches = MayhemWorld.View(sim).Scene.Watches;
        var fake = watches.First(w => w.Kind == MayhemWatchKind.FakeLook);
        MayhemDirectorTests.RunTo(sim, fake.WarningTick);
        MayhemDirectorTests.RunTo(sim, fake.EndTick + 2, _ => new() { [1] = new() { MoveX = 1 } });
        Assert.All(MayhemWorld.View(sim).Contestants, c => Assert.Null(c.Failure));
        var look = watches.First(w => w.Kind == MayhemWatchKind.Look);
        MayhemDirectorTests.RunTo(sim, look.StartTick);
        MayhemDirectorTests.RunTo(sim, look.StartTick + 20, _ => new() { [1] = new() { MoveX = -1 } });
        Assert.Equal(FailCause.Caught, MayhemWorld.View(sim).Contestants[0].Failure?.Cause);
        Assert.Null(MayhemWorld.View(sim).Contestants[1].Failure);
    }

    [Fact]
    public void RapidHasShorterAndMoreFrequentRealLooks()
    {
        var (sim, _) = MayhemWorld.Airing(MayhemChannels.NoRunning, SpeedTier.One, twist: new(3));
        MayhemWorld.Step(sim);
        var watches = MayhemWorld.View(sim).Scene.Watches;
        Assert.True(watches.Count >= 3);
        Assert.All(watches, w =>
        {
            Assert.Equal(MayhemWatchKind.Look, w.Kind);
            Assert.True(w.EndTick - w.StartTick < NoRunningTuning.WatchTicks[0]);
            Assert.True(w.StartTick - w.WarningTick < NoRunningTuning.WarningTicks[0]);
        });
    }

    [Fact]
    public void ScatteredStacksKeepTheirColourOwnerAndRejectRivals()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakBoards, SpeedTier.One, twist: new(2));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        var targets = MayhemWorld.View(sim).Scene.Targets;
        Assert.Equal(4, targets.Select(t => t.OwnerSeat).Distinct().Count());
        Assert.True(targets.Max(t => t.Center.X) - targets.Min(t => t.Center.X) > 8);
        var stack = targets[0];
        sim.MayhemShow!.Contact(new(MayhemContactKind.SolidHit, 0, 0, 2, stack.EntityId, -1, 999), sim.CurrentTick, sim);
        Assert.Equal(stack.HitsLeft, MayhemWorld.View(sim).Scene.Targets[0].HitsLeft);
        sim.MayhemShow.Contact(new(MayhemContactKind.SolidHit, 0, 0, 1, stack.EntityId, -1, 1000), sim.CurrentTick, sim);
        Assert.Equal(stack.HitsLeft - 1, MayhemWorld.View(sim).Scene.Targets[0].HitsLeft);
    }
}
