using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>The registry, restore equivalence, the roster pass table, snapshot validation and each channel's rule.</summary>
public class MayhemChannelTests
{
    private readonly ITestOutputHelper _output;
    public MayhemChannelTests(ITestOutputHelper output) => _output = output;

    private static readonly SpeedTier[] Tiers = { SpeedTier.One, SpeedTier.Two, SpeedTier.Three };

    private static IEnumerable<(ChannelId Channel, SpeedTier Tier)> Cases()
    {
        foreach (var entry in MayhemChannels.All)
            foreach (var tier in Tiers)
                if (entry.Role == ChannelRole.Regular || tier == SpeedTier.Three) yield return (entry.Id, tier);
    }

    public static TheoryData<ushort, SpeedTier> ChannelTiers()
    {
        var data = new TheoryData<ushort, SpeedTier>();
        foreach (var (channel, tier) in Cases()) data.Add(channel.Value, tier);
        return data;
    }

    private static ChannelSetup SampleSetup(MankiMayhemDefinition stage)
    {
        var arena = MayhemWorld.Arena();
        var seats = Enumerable.Range(0, 2).Select(i =>
        {
            var spawn = MatchFormat.MankiMayhem.StartFor(arena, i, 2);
            return new ChannelSeat((byte)i, (ulong)i + 1, new MayhemPose(spawn.X, .85f, spawn.Z, spawn.Yaw), .4f, 1.7f);
        }).ToArray();
        return new ChannelSetup(5, 0, 0, SpeedTier.One, 100, 190, 490, stage, seats);
    }

    [Fact]
    public void EveryConcreteChannelIsRegisteredExactlyOnce()
    {
        var stage = MayhemWorld.Arena().Arcade!.Mayhem!;
        var concrete = typeof(IMayhemChannel).Assembly.GetTypes()
            .Where(t => typeof(IMayhemChannel).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).ToHashSet();
        var registered = MayhemChannels.All.Select(e => e.Create(SampleSetup(stage)).GetType()).ToList();
        Assert.Equal(concrete.OrderBy(t => t.Name), registered.Distinct().OrderBy(t => t.Name));
        Assert.Equal(registered.Count, registered.Distinct().Count());
        Assert.Equal(MayhemChannels.All.Count, MayhemChannels.All.Select(e => e.Id).Distinct().Count());
        Assert.Equal(MayhemChannels.All.Count, MayhemChannels.All.Select(e => e.CopyKey).Distinct().Count());
        Assert.DoesNotContain(MayhemChannels.All, e => e.Id == ChannelId.None);
        Assert.All(concrete, t => Assert.True(t.IsSealed && t.IsNotPublic, $"{t.Name} must be internal sealed"));
    }

    [Theory]
    [MemberData(nameof(ChannelTiers))]
    public void RestoringAtEveryTickEqualsTheContinuousEpisode(ushort channelId, SpeedTier tier)
    {
        var channel = new ChannelId(channelId);
        var (continuous, episode) = MayhemWorld.Airing(channel, tier, roster: MayhemWorld.Four);
        var (restored, _) = MayhemWorld.Airing(channel, tier, roster: MayhemWorld.Four, reverse: true);
        var cpus = MayhemWorld.Cpus(continuous, 6);
        MayhemWorld.StepCpus(continuous, cpus);
        uint end = MayhemWorld.Plan(continuous).Deadline(episode) + 3;
        int events = 0;
        while (continuous.CurrentTick < end)
        {
            continuous.ClearPresentationEvents();
            restored.RestoreSnapshot(continuous.CaptureSnapshot());
            var inputs = new Dictionary<ulong, InputState>();
            foreach (var (id, cpu) in cpus) inputs[id] = cpu.Decide(continuous, id);
            continuous.Tick(new(inputs)); restored.Tick(new(inputs));
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()),
                $"{MayhemChannels.Find(channel).CopyKey} tier {tier} diverged at tick {continuous.CurrentTick}");
            Assert.Equal(continuous.LastTickMayhemEvents, restored.LastTickMayhemEvents);
            Assert.Equal(continuous.GetPresentationEvents(), restored.GetPresentationEvents());
            events += continuous.LastTickMayhemEvents.Count;
        }
        Assert.True(events >= 4, $"only {events} events");
        Assert.Equal(MayhemPhase.Verdict, MayhemWorld.View(continuous).Cursor.Phase);
    }

    public static TheoryData<CharacterClass> Roster() => RosterFighters.All();

    [Theory]
    [MemberData(nameof(Roster))]
    public void EveryRosterFighterPassesEveryChannelAtEveryTierAndFailsTheObjectivesIdle(CharacterClass fighter)
    {
        var cast = new[] { fighter };
        var failures = new List<string>();
        foreach (var (channel, tier) in Cases())
        {
            var (sim, episode) = MayhemWorld.Airing(channel, tier, count: 2, roster: cast);
            var cpus = MayhemWorld.Cpus(sim, 0);
            if (channel == MayhemChannels.Spot) cpus.Remove(2);
            var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.StepCpus(s, cpus));
            if (channel == MayhemChannels.Spot ? !passed[0] : !passed.All(p => p))
                failures.Add($"{MayhemChannels.Find(channel).CopyKey} tier {tier}: " + string.Join(", ",
                    MayhemWorld.View(sim).Contestants.Where(c => c.Failure != null).Select(c => $"{c.EntityId} {c.Failure!.Value.Cause}@{c.Failure.Value.Tick}")));
        }
        foreach (var (channel, cause) in new[] { (MayhemChannels.Spot, FailCause.OutsideSpot),
            (MayhemChannels.BreakOut, FailCause.Hazard), (MayhemChannels.BreakBoards, FailCause.BoardsRemain) })
            foreach (var tier in Tiers)
            {
                var (sim, episode) = MayhemWorld.Airing(channel, tier, count: 2, roster: cast);
                var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.Step(s));
                if (passed.Any(p => p)) failures.Add($"{MayhemChannels.Find(channel).CopyKey} tier {tier} passed idle");
                Assert.All(MayhemWorld.View(sim).Contestants, c => Assert.Equal(cause, c.Failure!.Value.Cause));
            }
        _output.WriteLine($"{fighter}: {(failures.Count == 0 ? "all pass" : string.Join("; ", failures))}");
        Assert.True(failures.Count == 0, $"{fighter}: {string.Join("; ", failures)}");
    }

    [Fact]
    public void SnapshotsKeepFailuresThroughVerdictSpeedUpAndEnded()
    {
        var sim = MayhemWorld.NewShow(9, count: 2);
        var plan = MayhemWorld.Plan(sim);
        var copy = MayhemWorld.NewShow(9, count: 2, reverse: true);
        var phases = new HashSet<MayhemPhase>();
        while (sim.CurrentTick < plan.EndTick + 2)
        {
            MayhemWorld.Step(sim);
            var view = MayhemWorld.View(sim);
            if (view.Cursor.Phase is not (MayhemPhase.Verdict or MayhemPhase.SpeedUp or MayhemPhase.Ended)) continue;
            if (!view.Contestants.Any(c => c.Failure != null) || !phases.Add(view.Cursor.Phase)) continue;
            var snapshot = sim.CaptureSnapshot();
            copy.RestoreSnapshot(snapshot);
            Assert.True(snapshot.ContentEquals(copy.CaptureSnapshot()));
            Assert.Equal(view.Contestants, MayhemWorld.View(copy).Contestants);
        }
        Assert.Equal(new[] { MayhemPhase.Verdict, MayhemPhase.SpeedUp, MayhemPhase.Ended }.Order(), phases.Order());
    }

    /// <summary>The director section with fields replaced, laid out as <see cref="MayhemShow.WriteSnapshot"/> writes it.</summary>
    private sealed record Section(uint Seed, ulong[] Roster, byte Withdrawn, byte OnAir, uint[] Masks, byte[]? FailureCause,
        float SafeX, ushort Channel, byte Episode, byte[] Payload)
    {
        public byte[] Bytes()
        {
            var w = new SnapshotWriter();
            w.WriteUInt(Seed); w.WriteByte((byte)Roster.Length);
            foreach (ulong id in Roster) w.WriteULong(id);
            w.WriteByte(Withdrawn); w.WriteByte(OnAir);
            for (int seat = 0; seat < Roster.Length; seat++)
            {
                w.WriteUInt(seat < Masks.Length ? Masks[seat] : 0);
                bool failed = FailureCause != null && seat == 0;
                w.WriteBool(failed);
                if (failed) { w.WriteByte(FailureCause![0]); w.WriteByte(0); w.WriteUInt(1); for (int i = 0; i < 4; i++) w.WriteFloat(0); }
                w.WriteFloat(SafeX); w.WriteFloat(.85f); w.WriteFloat(0); w.WriteFloat(0);
            }
            w.WriteUShort(Channel);
            if (Channel != 0) { w.WriteByte(Episode); foreach (byte b in Payload) w.WriteByte(b); }
            return w.ToArray();
        }
    }

    [Fact]
    public void SnapshotValidationRejectsMalformedShows()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakOut, SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 5);
        var w = new SnapshotWriter(); sim.MayhemShow!.WriteSnapshot(w);
        var live = w.ToArray();
        int prefix = 4 + 1 + 16 + 2 + 2 * (4 + 1 + 16) + 2 + 1;
        var payload = live.Skip(prefix).ToArray();
        uint seed = sim.MayhemShow.Plan!.Seed;
        var valid = new Section(seed, new ulong[] { 1, 2 }, 0, 3, new uint[2], null, -5, MayhemChannels.BreakOut.Value, (byte)episode, payload);
        var stage = MayhemWorld.Arena().Arcade!.Mayhem!;
        void Accept(Section section) => new MayhemShow(stage).ReadSnapshot(new SnapshotReader(section.Bytes()), new ulong[] { 1, 2 }, sim.CurrentTick);
        void Reject(Section section, string why) => Assert.Throws<InvalidDataException>(() => Accept(section));
        Accept(valid);
        Reject(valid with { Channel = 99 }, "unknown channel id");
        Reject(valid with { Channel = MayhemChannels.Dodge.Value }, "channel the plan does not air");
        Reject(valid with { Channel = 0 }, "missing channel in Play");
        Reject(valid with { Masks = new[] { 1u << episode, 0u } }, "pass bit for an episode not yet committed");
        Reject(valid with { Masks = new[] { 1u << 20, 0u } }, "pass bit past the show");
        Reject(valid with { Withdrawn = 1 << 3 }, "withdrawn bit past the roster");
        Reject(valid with { OnAir = 1 << 2 }, "on-air bit past the roster");
        Reject(valid with { Payload = payload.Select((b, i) => i == 0 ? (byte)2 : b).ToArray() }, "lane out of range");
        Reject(valid with { SafeX = float.NaN }, "non-finite safe pose");
        Reject(valid with { Payload = Corrupt(payload, 16, float.NaN) }, "non-finite cage geometry");
        Reject(valid with { Roster = new ulong[] { 2, 1 } }, "unsorted roster");
        Reject(valid with { Roster = new ulong[] { 1, 3 } }, "roster mismatch");
        Reject(valid with { FailureCause = new byte[] { 200 } }, "unknown failure cause");
        Accept(valid with { FailureCause = new[] { (byte)FailCause.Hazard } });
        var call = MayhemWorld.Plan(sim).CallStart(episode) + 1;
        Assert.Throws<InvalidDataException>(() => new MayhemShow(stage).ReadSnapshot(
            new SnapshotReader((valid with { FailureCause = new[] { (byte)FailCause.Hazard } }).Bytes()), new ulong[] { 1, 2 }, call));
    }

    private static byte[] Corrupt(byte[] payload, int offset, float value)
    {
        var copy = payload.ToArray();
        BitConverter.GetBytes(value).CopyTo(copy, offset);
        return copy;
    }

    [Fact]
    public void RepeatedHitsOnOneWallInOneTickCarryTheAttackerAndActivationInTheirKeys()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakOut, SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        var wall = MayhemWorld.View(sim).Scene.Solids.First(w => w.HitBudget == RuntimeSolidTuning.WallHits);
        var show = sim.MayhemShow!;
        // The resolver lands one hit per object per tick; the director still keys same-tick hits apart if several arrive.
        void Hit(ulong attacker, ulong activation) => show.Contact(new MayhemContact(MayhemContactKind.SolidHit, 0, 0, attacker,
            wall.EntityId, -1, activation), sim.CurrentTick, sim);
        int before = show.Events.Count;
        Hit(1, 501); Hit(1, 501); Hit(2, 502); Hit(1, 503);
        var hits = show.Events.Skip(before).ToList();
        Assert.Equal(3, hits.Count);
        Assert.Equal(3, hits.Select(e => e.Key).Distinct().Count());
        Assert.All(hits, e => Assert.Equal(sim.CurrentTick, e.MatchTick));
        Assert.Equal(new[] { MayhemEvent.HitOperation(1, 501), MayhemEvent.HitOperation(2, 502), MayhemEvent.HitOperation(1, 503) },
            hits.Select(e => e.Operation));
        Assert.Equal(MayhemEventKind.SolidBroken, hits[^1].Kind);
        Assert.All(hits, e => Assert.True(MayhemEvent.IsValid(e, ServerSimulation.MayhemHostEntityId)));

        var (boards, boardEpisode) = MayhemWorld.Airing(MayhemChannels.BreakBoards, SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(boards, MayhemWorld.Plan(boards).PlayStart(boardEpisode) + 2);
        var stack = MayhemWorld.View(boards).Scene.Targets.Single(t => t.OwnerSeat == 0);
        int start = boards.MayhemShow!.Events.Count;
        foreach (ulong activation in new ulong[] { 601, 601, 602 })
            boards.MayhemShow.Contact(new MayhemContact(MayhemContactKind.SolidHit, 0, 0, 1, stack.EntityId, -1, activation), boards.CurrentTick, boards);
        var boardHits = boards.MayhemShow.Events.Skip(start).ToList();
        Assert.Equal(2, boardHits.Count);
        Assert.Equal(2, boardHits.Select(e => e.Key).Distinct().Count());
    }

    [Fact]
    public void PrimeTimeChildrenUseLanesZeroAndOneAndNeedBothObjectives()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.PrimeTime, SpeedTier.Three, count: 2);
        var plan = MayhemWorld.Plan(sim);
        MayhemWorld.Step(sim);
        var bombs = MayhemWorld.View(sim).Scene.Bombs;
        Assert.Equal(new byte[] { 0, 1 }, bombs.Select(b => b.Lane).Distinct().Order());
        Assert.NotEmpty(MayhemWorld.View(sim).Scene.Solids);
        var launches = new List<MayhemEvent>();
        var lanes = new HashSet<byte>();
        while (sim.CurrentTick < plan.Deadline(episode) - 1)
        {
            MayhemWorld.Step(sim);
            launches.AddRange(sim.LastTickMayhemEvents.Where(e => e.Kind == MayhemEventKind.Launch));
            foreach (var h in sim.Resolver.GetActiveHitboxes().Where(h => h.OwnerId == ServerSimulation.MayhemHostEntityId))
                if (MayhemHazards.TryDecode(h.ActivationId, out int ep, out byte lane, out _) && ep == episode) lanes.Add(lane);
        }
        Assert.Equal(new byte[] { 0, 1 }, launches.Select(e => e.Lane).Distinct().Order());
        Assert.Equal(launches.Count, launches.Select(e => e.Key).Distinct().Count());
        Assert.Equal(new byte[] { 0, 1 }, lanes.Order());

        var (out1, outEpisode) = MayhemWorld.Airing(MayhemChannels.PrimeTime, SpeedTier.Three, count: 2);
        MayhemDirectorTests.RunTo(out1, MayhemWorld.Plan(out1).PlayStart(outEpisode) + 1);
        MayhemDirectorTests.Place(out1, 1, 0, 0);
        var probe = MayhemWorld.View(out1).Contestants[0];
        Assert.True(probe.ObjectiveMet);
        Assert.False(MayhemWorld.View(out1).Contestants[1].ObjectiveMet);
    }

    [Fact]
    public void NoRunningCatchesMovementWhileWatchedButNotJumpsShovesOrStillness()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.NoRunning, SpeedTier.One, count: 4, roster: new[] { CharacterClass.FightGuy });
        MayhemWorld.Step(sim);
        var watch = MayhemWorld.View(sim).Scene.Watches[0];
        MayhemDirectorTests.RunTo(sim, watch.StartTick + 4);
        var log = new List<MayhemEvent>();
        bool shoved = false;
        for (int i = 0; i < 20; i++)
        {
            if (i == 2)
            {
                var target = sim.GetState(4);
                sim.Resolver.Spawn(new Hitbox { OwnerId = 3, ActivationId = 900, X = target.PX, Y = target.PY, Z = target.PZ,
                    Radius = .6f, Damage = 4, DurationTicks = 1, StunTicks = 10 });
            }
            sim.Tick(new()
            {
                [1] = new InputState { MoveX = 1 },
                [2] = new InputState { Jump = i == 0, JumpHeld = true },
            });
            shoved |= sim.LastTickHits.Any(h => h.OwnerEntityId == 3 && h.TargetEntityId == 4);
            log.AddRange(sim.LastTickMayhemEvents);
        }
        Assert.True(shoved);
        var fails = log.Where(e => e.Kind == MayhemEventKind.Fail).ToList();
        var caught = Assert.Single(fails);
        Assert.Equal(1UL, caught.Subject);
        Assert.Equal(FailCause.Caught, caught.FailCause);
        Assert.True(caught.MatchTick >= watch.StartTick && caught.MatchTick < watch.EndTick);

        var (calm, calmEpisode) = MayhemWorld.Airing(MayhemChannels.NoRunning, SpeedTier.One, count: 2);
        MayhemWorld.Step(calm);
        var first = MayhemWorld.View(calm).Scene.Watches[0];
        MayhemDirectorTests.RunTo(calm, first.WarningTick - 1, _ => new() { [1] = new InputState { MoveX = .6f } });
        MayhemDirectorTests.RunTo(calm, MayhemWorld.Plan(calm).Deadline(calmEpisode));
        Assert.All(MayhemWorld.View(calm).Contestants, c => Assert.Equal(MayhemVerdict.Passed, c.Verdict));
    }

    [Fact]
    public void ASlippingCpuSwingsSlowlyAtItsOwnBoardsInsteadOfStandingStill()
    {
        var casts = new[]
        {
            (Seed: 101u, Cast: new[] { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk }),
            (Seed: 8020u, Cast: new[] { CharacterClass.Bashie, CharacterClass.Nyx, CharacterClass.Salamander, CharacterClass.FightGuy }),
        };
        foreach (var (seed, cast) in casts)
        {
            var sim = MayhemWorld.NewShow(seed, roster: cast);
            var plan = MayhemWorld.Plan(sim);
            var cpus = sim.MayhemShow!.Roster.ToDictionary(id => id, _ => new MayhemCpu(18, slipPercent: 100));
            var hits = new int[cast.Length];
            int verdicts = 0;
            while (!sim.EvaluateMatch().IsEnded)
            {
                MayhemWorld.StepCpus(sim, cpus);
                foreach (var e in sim.LastTickMayhemEvents.Where(e => e.Episode < MayhemShowTuning.Episodes
                    && plan.ChannelAt(e.Episode) == MayhemChannels.BreakBoards))
                {
                    if (e.Kind == MayhemEventKind.Call) Array.Clear(hits);
                    if (e.Kind is MayhemEventKind.TargetHit or MayhemEventKind.TargetBroken)
                        hits[MayhemWorld.View(sim).Scene.Targets.Single(t => t.EntityId == e.Subject).OwnerSeat]++;
                    if (e.Kind is MayhemEventKind.Pass or MayhemEventKind.Fail)
                    {
                        int seat = sim.MayhemShow.SeatOf(e.Subject);
                        Assert.True(hits[seat] > 0, $"{cast[seat]} stood still in episode {e.Episode} of seed {seed}");
                        Assert.Equal(MayhemEventKind.Fail, e.Kind);
                        verdicts++;
                    }
                }
            }
            Assert.True(verdicts > 0, $"seed {seed} never aired BREAK THE BOARDS!");
        }
    }

    [Fact]
    public void OnlyTheOwnersSwingsBreakTheirBoardsOncePerActivationAndHostBlastsNever()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakBoards, SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        var stack = MayhemWorld.View(sim).Scene.Targets.Single(t => t.OwnerSeat == 0);
        void Swing(ulong owner, ulong activation) => sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = owner, ActivationId = activation, X = stack.Center.X, Y = stack.Center.Y, Z = stack.Center.Z,
            Radius = .3f, Damage = 4, DurationTicks = 1,
        });
        byte Left() => MayhemWorld.View(sim).Scene.Targets.Single(t => t.EntityId == stack.EntityId).HitsLeft;
        Swing(2, 700); MayhemWorld.Step(sim);
        Assert.Equal(stack.Hits, Left());
        sim.Resolver.QueueExplosion(stack.Center.X, stack.Center.Y, stack.Center.Z, new ProjectileExplosion { Radius = 3, DurationTicks = 2 },
            ServerSimulation.MayhemHostEntityId, 0, MayhemHazards.Activation(episode, 0, 0), false, 0);
        MayhemWorld.Step(sim, 3);
        Assert.Equal(stack.Hits, Left());
        Swing(1, 701); Swing(1, 701); MayhemWorld.Step(sim);
        Assert.Equal(stack.Hits - 1, Left());
        Swing(1, 701); MayhemWorld.Step(sim);
        Assert.Equal(stack.Hits - 1, Left());
        for (ulong a = 702; Left() > 0; a++) { Swing(1, a); MayhemWorld.Step(sim); }
        Assert.Contains(sim.LastTickMayhemEvents, e => e.Kind == MayhemEventKind.TargetBroken && e.Subject == stack.EntityId);
        Assert.True(MayhemWorld.View(sim).Contestants[0].ObjectiveMet);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).Deadline(episode));
        var view = MayhemWorld.View(sim);
        Assert.Equal(MayhemVerdict.Passed, view.Contestants[0].Verdict);
        Assert.Equal(FailCause.BoardsRemain, view.Contestants[1].Failure!.Value.Cause);
    }

    [Fact]
    public void TheCrackedWallBreaksInTwoActivationsAndAWreckedCageLetsNobodyStayInside()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakOut, SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 2);
        var cracked = MayhemWorld.View(sim).Scene.Solids.First(w => w.CageEntityId == 1 && w.HitBudget == RuntimeSolidTuning.CrackedWallHits);
        for (ulong a = 1; a <= 2; a++)
        {
            sim.Resolver.Spawn(new Hitbox { OwnerId = 1, ActivationId = 800 + a, X = cracked.Center.X, Y = 1, Z = cracked.Center.Z,
                Radius = .4f, Damage = 3, DurationTicks = 1 });
            MayhemWorld.Step(sim);
        }
        var broken = MayhemWorld.View(sim).Scene.Solids.Single(w => w.EntityId == cracked.EntityId);
        Assert.Equal(0, broken.HitsLeft);
        Assert.False(broken.Blocks);
        Assert.Contains(sim.LastTickMayhemEvents, e => e.Kind == MayhemEventKind.SolidBroken && e.Subject == cracked.EntityId);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).Deadline(episode));
        Assert.All(MayhemWorld.View(sim).Contestants, c => Assert.Equal(MayhemVerdict.Failed, c.Verdict));
    }
}
