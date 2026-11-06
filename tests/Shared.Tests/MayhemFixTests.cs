using System;
using System.Linq;
using System.Collections.Generic;
using SlopArena.Shared.Mayhem;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemFixTests
{
    [Theory]
    [InlineData(1UL, 2UL)]
    [InlineData(2UL, 1UL)]
    public void QueuedShoveCannotMutateOrComeFromASplattedContestant(ulong victim, ulong attacker)
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        // Inject the collected batch because the resolver normally admits only one hit per target per tick.
        var process = typeof(ServerSimulation).GetMethod("TryResolveMayhemContact",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        void Contact(ulong owner, ulong target, ulong activation)
        {
            var hit = new SpellResolver.HitResult { OwnerEntityId = owner, TargetEntityId = target,
                ActivationId = activation, Damage = 3, DirX = 1 };
            Assert.Equal(true, process.Invoke(sim, new object[] { hit }));
        }
        Contact(ServerSimulation.MayhemHostEntityId, victim, MayhemHazards.Activation(episode, 0, 0));
        Contact(attacker, victim, 990);
        Contact(victim, attacker, 991);
        Assert.NotNull(MayhemWorld.View(sim).Contestants[(int)victim - 1].Failure);
        Assert.Equal(0, sim.GetState(victim).HitstunTicks);
        Assert.Equal(0, sim.GetState(victim).KVX);
        Assert.Equal(0, sim.GetState(attacker).HitstunTicks);
        Assert.DoesNotContain(sim.LastTickHits, h => h.OwnerEntityId == attacker || h.OwnerEntityId == victim);
    }

    [Fact]
    public void LaneOneBlastWrecksPrimeTimeCageWalls()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.PrimeTime, SpeedTier.Three, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        var wall = MayhemWorld.View(sim).Scene.Solids.First(w => w.Blocks);
        sim.Resolver.QueueExplosion(wall.Center.X, wall.Center.Y, wall.Center.Z,
            new ProjectileExplosion { Radius = 1, DurationTicks = 2 }, ServerSimulation.MayhemHostEntityId,
            0, MayhemHazards.Activation(episode, 1, 0), false, 0);
        MayhemWorld.Step(sim);
        Assert.Equal(0, MayhemWorld.View(sim).Scene.Solids.Single(w => w.EntityId == wall.EntityId).HitsLeft);
    }

    [Theory]
    [InlineData(.4f, .1f, false)]
    [InlineData(.3f, .3f, true)]
    public void SpotlightHasOneHolderWithDistanceThenSeatPriority(float first, float second, bool firstWins)
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).Deadline(episode) - 1);
        var spot = MayhemWorld.View(sim).Scene.Zones[0];
        MayhemDirectorTests.Place(sim, 1, spot.Center.X + first, spot.Center.Z);
        MayhemDirectorTests.Place(sim, 2, spot.Center.X + second, spot.Center.Z);
        Assert.Equal(firstWins, MayhemWorld.View(sim).Contestants[0].ObjectiveMet);
        Assert.Equal(!firstWins, MayhemWorld.View(sim).Contestants[1].ObjectiveMet);
        MayhemWorld.Step(sim);
        Assert.Equal(firstWins, MayhemWorld.View(sim).Contestants[0].Verdict == MayhemVerdict.Passed);
        Assert.Equal(!firstWins, MayhemWorld.View(sim).Contestants[1].Verdict == MayhemVerdict.Passed);
    }

    [Fact]
    public void SpotCpuChoosesTheNextSpotWhenARivalWinsItsNearest()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 3);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        var zones = MayhemWorld.View(sim).Scene.Zones;
        var near = zones[0].Center; var next = zones[1].Center;
        float dx = next.X - near.X, dz = next.Z - near.Z;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        MayhemDirectorTests.Place(sim, 1, near.X + dx / length, near.Z + dz / length);
        MayhemDirectorTests.Place(sim, 2, near.X, near.Z);
        sim.WithdrawMayhemPlayer(3);
        var intent = sim.MayhemCpuIntentFor(1, 0);
        Assert.Equal((next.X, next.Z), (intent.X, intent.Z));
        var slip = sim.MayhemCpuIntentFor(1, 0, 100);
        Assert.Equal((near.X, near.Z), (slip.X, slip.Z));
        var (copy, _) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 3);
        copy.RestoreSnapshot(sim.CaptureSnapshot());
        Assert.Equal(slip, copy.MayhemCpuIntentFor(1, 0, 100));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    public void ObjectiveRestoreRejectsMissingAndDuplicateSeats(ushort id, bool duplicate)
    {
        var stage = MayhemWorld.Arena().Arcade!.Mayhem!;
        var seats = new[] { new ChannelSeat(0, 1, new(-4, .85f, 0, 0), .4f, 1.7f),
            new ChannelSeat(1, 2, new(4, .85f, 0, 0), .4f, 1.7f) };
        var setup = new ChannelSetup(new SoloAiring(new(new(1), new(1), 5)), 0, 0, SpeedTier.One, 100, 190, 490, stage, seats);
        var channel = MayhemChannels.Find(new(id)).Create(setup);
        var w = new SnapshotWriter(); channel.WriteSnapshot(w);
        var data = w.ToArray();
        int header = id == 6 ? 2 : 7, stride = id == 6 ? 27 : 36;
        if (duplicate) Array.Copy(data, header, data, header + stride, stride);
        else
        {
            var list = data.ToList(); list.RemoveRange(header, stride);
            data = list.ToArray(); data[header - 1]--;
        }
        var context = new ChannelRestore(new SoloAiring(new(new(id), new(1), 5)), 0, 0, SpeedTier.One, stage, new ulong[] { 1, 2 }, 190, 490, 3);
        Assert.Throws<System.IO.InvalidDataException>(() => MayhemChannels.Find(new(id)).Read(new SnapshotReader(data), context));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void RestoreKeepsObjectivesForContestantsWithdrawnAfterCall(ushort channel)
    {
        var (sim, episode) = MayhemWorld.Airing(new(channel), SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        sim.WithdrawMayhemPlayer(2);
        var snapshot = sim.CaptureSnapshot();
        var (copy, _) = MayhemWorld.Airing(new(channel), SpeedTier.One, count: 2);
        copy.RestoreSnapshot(snapshot);
        Assert.True(snapshot.ContentEquals(copy.CaptureSnapshot()));
        var scene = MayhemWorld.View(copy).Scene;
        Assert.Equal(channel == 6 ? 2 : 8, channel == 6 ? scene.Targets.Count : scene.Solids.Count);
    }

    [Fact]
    public void EarlyBombBodyImpactPublishesActualTickAndRestoresIt()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 2);
        MayhemWorld.Step(sim);
        var cue = MayhemWorld.View(sim).Scene.Bombs[0];
        MayhemDirectorTests.RunTo(sim, cue.LaunchTick + 1);
        cue = MayhemWorld.View(sim).Scene.Bombs[0];
        var events = new List<MayhemEvent>();
        while (sim.CurrentTick < cue.ImpactTick - 2)
        {
            var bomb = sim.Resolver.GetActiveHitboxes().FirstOrDefault(h => h.ContactExplosion && h.ActivationId == MayhemHazards.Activation(episode, 0, 0));
            if (!bomb.ContactExplosion) break;
            var s = sim.GetState(1); s.PX = bomb.X; s.PZ = bomb.Z; s.PY = bomb.Y; s.IsGrounded = false;
            sim.SetState(1, s);
            MayhemWorld.Step(sim); events.AddRange(sim.LastTickMayhemEvents);
        }
        MayhemWorld.Step(sim); events.AddRange(sim.LastTickMayhemEvents);
        var impacted = MayhemWorld.View(sim).Scene.Bombs[0];
        Assert.True(impacted.ImpactTick < cue.ImpactTick);
        Assert.Equal(BombPhase.Exploded, impacted.Phase);
        Assert.True(impacted.Landing.Y > 0);
        var impact = Assert.Single(events, e => e.Kind == MayhemEventKind.Impact && e.Lane == 0 && e.Operation == 0);
        Assert.Equal(impacted.ImpactTick, impact.MatchTick);
        Assert.True(MayhemEvent.IsValid(impact, ServerSimulation.MayhemHostEntityId));
        var row = new SlopArena.Shared.Replays.ReplayMayhemBomb { Launched = impacted.Launched, Exploded = impacted.Exploded,
            ImpactTick = impacted.ImpactTick, Landing = new(impacted.Landing.X, impacted.Landing.Y, impacted.Landing.Z) };
        var decoded = SlopArena.Shared.Replays.ReplayViewCodec.ReadReplayMayhemBomb(
            SlopArena.Shared.Replays.ReplayViewCodec.WriteReplayMayhemBomb(row));
        Assert.True(decoded.Exploded);
        Assert.Equal(row.Landing, decoded.Landing);
        Assert.Equal(row.ImpactTick, decoded.ImpactTick);
        var (copy, _) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 2);
        copy.RestoreSnapshot(sim.CaptureSnapshot());
        MayhemWorld.Step(copy); MayhemWorld.Step(sim);
        Assert.Equal(MayhemWorld.View(sim).Scene.Bombs, MayhemWorld.View(copy).Scene.Bombs);
    }
}
