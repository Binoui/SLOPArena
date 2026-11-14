using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using SlopArena.Shared.Replays;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemVarietyTests
{
    public static TheoryData<ushort, SpeedTier, byte> NewCases()
    {
        var cases = new TheoryData<ushort, SpeedTier, byte>();
        foreach (var entry in MayhemChannels.Regulars.Where(e => e.Id.Value > 7))
        foreach (var tier in Enum.GetValues<SpeedTier>())
        foreach (var twist in entry.Twists) cases.Add(entry.Id.Value, tier, twist.Id.Value);
        return cases;
    }

    [Theory]
    [MemberData(nameof(NewCases))]
    public void NewObjectivesFailIdleAndSlippingCpusCanLose(ushort id, SpeedTier tier, byte twist)
    {
        var (idle, episode) = MayhemWorld.Airing(new(id), tier, count: 2, twist: new(twist));
        var idlePasses = MayhemWorld.RunEpisode(idle, episode, s => MayhemWorld.Step(s));
        if (id == MayhemChannels.HotPotato.Value) Assert.Contains(false, idlePasses);
        else Assert.All(idlePasses, p => Assert.False(p));
        var (slip, e) = MayhemWorld.Airing(new(id), tier, roster: MayhemWorld.Four, twist: new(twist));
        var cpus = slip.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(30, 100));
        Assert.Contains(false, MayhemWorld.RunEpisode(slip, e, s => MayhemWorld.StepCpus(s, cpus)));
    }

    private static ChannelSetup Setup(ChannelId id, byte twist = 1, int count = 2)
    {
        var seats = Enumerable.Range(0, count).Select(i => new ChannelSeat((byte)i, (ulong)i + 1, new(0, 1, 0, 0), .4f, 1.7f)).ToArray();
        return new(new SoloAiring(new(id, new(twist), 101)), 0, 0, SpeedTier.One, 10, 100, 1000,
            MayhemWorld.Arena().Arcade!.Mayhem!, seats);
    }
    private static MayhemFrame Frame(uint tick, params (float X, float Z, bool Ground)[] points)
        => new(tick, MayhemWorld.Arena().Arcade!.Mayhem!, points.Select((p, i) => new MayhemFighterFact((byte)i, (ulong)i + 1,
            State(p.X, p.Z, p.Ground), TestHelpers.EngineDef, true, true)).ToArray());
    private static CharacterState State(float x, float z, bool grounded)
    { var state = TestHelpers.PlayerState(x, z); state.IsGrounded = grounded; return state; }
    private static MayhemHazards Hazards(ChannelSetup setup, uint tick)
    {
        var hazards = new MayhemHazards(new List<MayhemEvent>(), MayhemShow.HostEntityId, setup.Stage);
        hazards.Bind(null, tick, 0);
        return hazards;
    }
    private static void After(IMayhemChannel channel, ChannelSetup setup, MayhemFrame frame)
        => channel.Tick(new(ChannelMoment.AfterCombat, MayhemPhase.Play, frame.Tick, setup.PlayStart, setup.Deadline, frame), Hazards(setup, frame.Tick));

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    public void ShortChannelsFinishWithinEightSeconds(ushort id)
    {
        foreach (var tier in Enum.GetValues<SpeedTier>())
        {
            var (sim, episode) = MayhemWorld.Airing(new(id), tier);
            var plan = MayhemWorld.Plan(sim);
            Assert.InRange(plan.Deadline(episode) - plan.PlayStart(episode), 1u, 480u);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GlitchWarningsLeaveAtLeastASecondToReadAndWalkAtEveryTier(byte twist)
    {
        foreach (var tier in Enum.GetValues<SpeedTier>())
        {
            var setup = Setup(MayhemChannels.Glitch, twist);
            setup = setup with { Tier = tier, Deadline = setup.PlayStart + MayhemSpeed.Play(GlitchTuning.Timing, tier) };
            var channel = GlitchChannel.Start(setup);
            uint previous = uint.MaxValue;
            for (int round = 0; round < GlitchTuning.Rounds; round++)
            {
                var window = channel.Window(round);
                Assert.True(window.Armed - window.Warning >= 60);
                Assert.True(window.End - window.Warning < previous);
                previous = window.End - window.Warning;
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SumoFinalRingHasRoomToStandOffCenter(byte twist)
    {
        var setup = Setup(MayhemChannels.Sumo, twist);
        var channel = SumoChannel.Start(setup);
        var ring = Assert.Single(channel.Observe().Zones);
        uint tick = setup.Deadline - 1;
        var center = ring.CenterAt(tick);
        var frame = Frame(tick, (center.X + .25f, center.Z, true));
        After(channel, setup, frame);
        Assert.True(ring.RadiusAt(tick) >= .45f);
        Assert.Equal(JudgementKind.Ready, channel.Judge(new(frame.Fighters[0], frame, true)).Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SumoLatchesLeavingTheRingOnAnyPlayTickEvenInTheAir(bool grounded)
    {
        var setup = Setup(MayhemChannels.Sumo);
        var channel = SumoChannel.Start(setup);
        var outside = Frame(900, (4, 0, grounded), (0, 0, true));
        After(channel, setup, outside);
        var returned = Frame(901, (0, 0, true), (0, 0, true));
        Assert.Equal(FailCause.OutOfRing, channel.Judge(new(returned.Fighters[0], returned, false)).Cause);
        Assert.Equal(JudgementKind.Ready, channel.Judge(new(returned.Fighters[1], returned, false)).Kind);
    }

    [Fact]
    public void SumoUnderOffMovesInsideInsteadOfPlanningAShove()
    {
        var setup = Setup(MayhemChannels.Sumo);
        var channel = SumoChannel.Start(setup);
        var frame = Frame(500, (0, 0, true), (.8f, 0, true));
        var probe = new CpuProbe(frame.Fighters[0], frame with { FriendlyFire = FriendlyFireMode.Off }, MayhemControls.Playing(ExtraVerb.None), 100, 1000, 12);
        Assert.Equal(CpuAction.Move, channel.Cpu(probe).Action);
        Assert.Equal(CpuAction.Normal, channel.Cpu(probe with { Frame = frame }).Action);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SmileSamplesOnlyTheFlashAndKeepsThatPhotograph(byte twist)
    {
        var setup = Setup(MayhemChannels.Smile, twist);
        var channel = SmileChannel.Start(setup);
        var cue = Assert.Single(channel.Observe().Zones);
        var before = Frame(cue.ArmedFrom - 1, (-11, -9, true), (11, 9, true));
        After(channel, setup, before);
        Assert.Equal(JudgementKind.Incomplete, channel.Judge(new(before.Fighters[0], before, false)).Kind);
        var center = cue.CenterAt(cue.ArmedFrom);
        var flash = Frame(cue.ArmedFrom, (center.X, center.Z, true), (11, 9, false));
        After(channel, setup, flash);
        var later = Frame(cue.ArmedFrom + 1, (-11, -9, true), (center.X, center.Z, true));
        After(channel, setup, later);
        Assert.Equal(JudgementKind.Ready, channel.Judge(new(later.Fighters[0], later, false)).Kind);
        Assert.Equal(FailCause.OutOfFrame, channel.Judge(new(later.Fighters[1], later, false)).Cause);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GlitchJudgesGroundedOnlyWhileArmedAndEndsBeforeTheDeadline(byte twist)
    {
        var setup = Setup(MayhemChannels.Glitch, twist);
        var channel = GlitchChannel.Start(setup);
        var panel = channel.Observe().Zones[0];
        var warning = Frame(panel.ArmedFrom - 1, (panel.Center.X, panel.Center.Z, true), (panel.Center.X, panel.Center.Z, false));
        After(channel, setup, warning);
        Assert.Equal(JudgementKind.Ready, channel.Judge(new(warning.Fighters[0], warning, false)).Kind);
        var armed = warning with { Tick = panel.ArmedFrom };
        After(channel, setup, armed);
        Assert.Equal(FailCause.Glitched, channel.Judge(new(armed.Fighters[0], armed, false)).Cause);
        Assert.Equal(JudgementKind.Ready, channel.Judge(new(armed.Fighters[1], armed, false)).Kind);
        Assert.Equal(GlitchTuning.ArmedTicks, panel.ActiveUntil - panel.ArmedFrom);
        Assert.True(channel.Window(GlitchTuning.Rounds - 1).End < setup.Deadline);
        Assert.True(channel.Window(0).Armed - channel.Window(0).Warning > channel.Window(3).Armed - channel.Window(3).Warning);
        Assert.All(channel.Observe().Zones, z => Assert.Equal(0, z.Ordinal / 16));
    }

    [Fact]
    public void RectangleGeometryAndPanelEdgesHaveOneSharedInterpretation()
    {
        var frame = new ZoneCue(0, 0, MayhemZoneKind.SelfieFrame, new(0, 0, 0), 2, 10, 20, new(2, 0, 0), 1, 19, 4, 2);
        Assert.True(frame.Contains(2.5f, 3, 15));
        Assert.False(frame.Contains(2.6f, 3, 15));
        var panel = frame with { Kind = MayhemZoneKind.GlitchPanel };
        Assert.False(panel.Contains(2.5f, 3, 15));
        Assert.True(panel.Contains(-.5f, -3, 15));
    }

    [Fact]
    public void BonkClaimsTheFirstNormalOnceAndBreaksSameTickTiesBySeat()
    {
        var setup = Setup(MayhemChannels.Bonk, count: 4);
        var channel = BonkChannel.Start(setup);
        var frame = Frame(100, (0, 0, true), (0, 0, true), (0, 0, true), (0, 0, true));
        var hazards = Hazards(setup, 100);
        channel.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, 100, 100, 1000, frame), hazards);
        var head = channel.Observe().Targets[0];
        Assert.Equal(3, channel.Observe().Targets.Count);
        Assert.Null(head.OwnerSeat);
        void Hit(ulong who, byte slot) => channel.Contact(new(MayhemContactKind.SolidHit, 0, 0, who, head.EntityId, -1, 999 + who, AttackSlot: slot), hazards);
        Hit(3, AbilitySlots.Normal2); Hit(2, AbilitySlots.Normal3); Hit(1, AbilitySlots.E);
        After(channel, setup, frame);
        Assert.Equal(JudgementKind.Incomplete, channel.Judge(new(frame.Fighters[0], frame, false)).Kind);
        Assert.Equal(JudgementKind.Ready, channel.Judge(new(frame.Fighters[1], frame, false)).Kind);
        Assert.Equal(JudgementKind.Incomplete, channel.Judge(new(frame.Fighters[2], frame, false)).Kind);
        Assert.False(channel.Observe().Targets[0].ActiveAt(100));
        Hit(1, AbilitySlots.Normal1); After(channel, setup, frame);
        Assert.Equal(JudgementKind.Incomplete, channel.Judge(new(frame.Fighters[0], frame, false)).Kind);
    }

    [Fact]
    public void BonkDecoysFailAndInactiveTargetsRejectHits()
    {
        var setup = Setup(MayhemChannels.Bonk, 2);
        var channel = BonkChannel.Start(setup);
        var frame = Frame(100, (0, 0, true), (0, 0, true));
        var hazards = Hazards(setup, 100);
        channel.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, 100, 100, 1000, frame), hazards);
        var decoy = Assert.Single(channel.Observe().Targets, h => h.Kind == MayhemTargetKind.DecoyHead);
        channel.Contact(new(MayhemContactKind.SolidHit, 0, 0, 1, decoy.EntityId, -1, 123, AttackSlot: AbilitySlots.Normal1), hazards);
        After(channel, setup, frame);
        Assert.Equal(FailCause.Hazard, channel.Judge(new(frame.Fighters[0], frame, false)).Cause);
        var real = Assert.Single(channel.Observe().Targets, h => h.Kind == MayhemTargetKind.CardboardHead);
        Assert.False(real.ActiveAt(real.ActiveFrom - 1));
        Assert.True(real.ActiveAt(real.ActiveFrom));
        Assert.False(real.ActiveAt(real.ActiveUntil));
        var duck = frame with { Tick = real.ActiveUntil };
        channel.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, duck.Tick, 100, 1000, duck), hazards);
        channel.Contact(new(MayhemContactKind.SolidHit, 0, 0, 2, real.EntityId, -1, 124, AttackSlot: AbilitySlots.Normal1), hazards);
        After(channel, setup, duck);
        Assert.Equal(JudgementKind.Incomplete, channel.Judge(new(duck.Fighters[1], duck, false)).Kind);
    }
    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(14)]
    public void CarefulCpusMeetTheSoloContestAcrossTwelveSeedsAndEveryCast(ushort id)
    {
        var entry = MayhemChannels.Find(new(id));
        var problems = new List<string>();
        foreach (var twist in entry.Twists)
        foreach (int count in new[] { 2, 3, 4 })
        for (uint sample = 1; sample <= 12; sample++)
        {
            var sim = MayhemWorld.NewShow(sample, count: count, roster: MayhemWorld.Four);
            int episode = 10;
            MayhemWorld.Pin(sim, episode, new SoloAiring(new(entry.Id, twist.Id, MayhemMath.ChannelSeed(sample, episode, 0))));
            MayhemWorld.SkipToCall(sim, episode);
            var cpus = sim.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(12, 0));
            var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.StepCpus(s, cpus));
            bool contest = entry.Contest == ChannelContest.Shared ? passed.All(p => p) : passed.Any(p => p);
            if (!contest) problems.Add($"{entry.CopyKey} twist {twist.Id.Value}, {count} seats seed {sample}: "
                + string.Join(", ", MayhemWorld.View(sim).Contestants.Where(c => c.Failure != null).Select(c => $"{c.Seat} {c.Failure!.Value.Cause}")));
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Theory]
    [InlineData(8, 2)]
    [InlineData(9, 2)]
    [InlineData(10, 3)]
    [InlineData(14, 2)]
    [InlineData(11, 2)]
    [InlineData(12, 2)]
    [InlineData(13, 3)]
    [InlineData(15, 2)]
    public void ReplayCaptureCodecAndDisplayKeepNewCuesWindowsAndStamps(ushort id, byte twist)
    {
        var content = Replays.ReplayWorldTests.Content();
        var entry = content.Catalog.ResolvePackage("fightguy")!;
        var arena = MayhemWorld.Arena();
        var (seed, episode) = MayhemWorld.SeedWith(new(id), id == 15 ? 2 : 0, new(twist));
        var sim = new ServerSimulation(arena, new MankiMayhemMatchRule(FriendlyFireMode.Off));
        for (int i = 0; i < 2; i++)
        {
            var spawn = MatchFormat.MankiMayhem.StartFor(arena, i, 2);
            var state = TestHelpers.PlayerState(spawn.X, spawn.Z); state.EntityId = (ulong)i + 1;
            sim.RegisterEntity(state.EntityId, entry.Definition, state, entry.BakedAnimation);
        }
        sim.BeginMankiMayhem(seed);
        var plan = MayhemWorld.Plan(sim);
        foreach (uint tick in new[] { plan.PlayStart(episode) + 1, plan.Deadline(episode) })
        {
            MayhemDirectorTests.RunTo(sim, tick);
            var roster = new[] { 1UL, 2UL }.Select(f => new ReplayParticipant(f, "cpu", f.ToString(), entry.Identity, "base", "none", "0")).ToArray();
            var header = new ReplayHeader("variety", arena.Name, new(MatchFormat.MankiMayhem.ToWire(), "off", 0, false),
                tick, roster, ReplayBuildIdentity.Current("test", "test"));
            var capture = new ReplayCapture(header, content.Catalog, arena);
            var packet = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(),
                sim.GetPresentationEvents().ToArray(), sim.LastTickHits.ToArray(), mayhemEvents: sim.LastTickMayhemEvents.ToArray());
            var result = capture.Capture(sim, packet);
            Assert.Equal(ReplayCaptureStatus.Accepted, result.Status);
            var decoded = ReplayViewCodec.Decode(ReplayViewCodec.Encode(result.Capture!.View), tick);
            var display = ReplayViewDecoder.Modes(sim, decoded, arena, null!).Mayhem!;
            Assert.Equal(MayhemWorld.View(sim).Scene.Zones, display.Display!.Zones);
            Assert.Equal(MayhemWorld.View(sim).Scene.Targets, display.Display.Targets);
            Assert.Equal(MayhemWorld.View(sim).Scene.Carries, display.Display.Carries);
            Assert.Equal(MayhemWorld.View(sim).Scene.HitProgress, display.Display.HitProgress);
            Assert.Equal(MayhemWorld.View(sim).Scene.Bombs, display.Display.Bombs);
            Assert.Equal(MayhemWorld.View(sim).Scene.Watches, display.Display.Watches);
            Assert.Equal(MayhemWorld.View(sim).Contestants.Select(c => c.Failure), display.Failures);
        }
    }

    [Fact]
    public void BonkTargetsEnterCollisionOnlyDuringTheirWindow()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Bonk, SpeedTier.One, count: 2);
        uint play = MayhemWorld.Plan(sim).PlayStart(episode);
        MayhemDirectorTests.RunTo(sim, play + 1);
        var head = Assert.Single(MayhemWorld.View(sim).Scene.Targets);
        void Swing(ulong activation) => sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, ActivationId = activation, AttackSlot = AbilitySlots.Normal1,
            X = head.Center.X, Y = head.Center.Y, Z = head.Center.Z, Radius = .3f, Damage = 4, DurationTicks = 1,
        });
        MayhemDirectorTests.RunTo(sim, head.ActiveUntil);
        Swing(1001); MayhemWorld.Step(sim);
        Assert.DoesNotContain(sim.LastTickHits, h => h.TargetEntityId == head.EntityId);
        Assert.False(MayhemWorld.View(sim).Contestants[0].ObjectiveMet);
        uint next = play + (MayhemWorld.Plan(sim).Deadline(episode) - play) / BonkTuning.Waves;
        MayhemDirectorTests.RunTo(sim, next + 1);
        head = Assert.Single(MayhemWorld.View(sim).Scene.Targets);
        Swing(1002); MayhemWorld.Step(sim);
        Assert.Contains(sim.LastTickHits, h => h.TargetEntityId == head.EntityId);
        Assert.True(MayhemWorld.View(sim).Contestants[0].ObjectiveMet);
    }

    [Theory]
    [MemberData(nameof(NewCases))]
    public void ReadingJudgementCuesAndCpuDoesNotChangeChannelState(ushort id, SpeedTier tier, byte twist)
    {
        var setup = Setup(new(id), twist) with { Tier = tier };
        var channel = MayhemChannels.Find(new(id)).Create(setup);
        var frame = Frame(100, (0, 0, true), (2, 0, true));
        var hazards = Hazards(setup, 100);
        channel.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, 100, 100, 1000, frame), hazards);
        byte[] Snapshot() { var w = new SnapshotWriter(); channel.WriteSnapshot(w); return w.ToArray(); }
        var before = Snapshot();
        for (int i = 0; i < 3; i++)
        {
            channel.Judge(new(frame.Fighters[0], frame, false));
            channel.Observe();
            channel.Cpu(new(frame.Fighters[0], frame, MayhemControls.Playing(ExtraVerb.None), 100, 1000, 12));
        }
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GlitchPublishesOnlyCurrentRoundWithASafePlaceAndHonorsALateStart(byte twist)
    {
        var setup = Setup(MayhemChannels.Glitch, twist);
        var channel = GlitchChannel.Start(setup, 120);
        Assert.Equal(220u, channel.Window(0).Warning);
        for (int round = 0; round < GlitchTuning.Rounds; round++)
        {
            uint warning = channel.Window(round).Warning;
            var frame = Frame(warning, (0, 0, false));
            channel.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, warning, 100, 1000, frame), Hazards(setup, warning));
            var zones = channel.Observe().Zones;
            Assert.InRange(zones.Count, 1, 8);
            Assert.All(zones, z =>
            {
                Assert.Equal(round, z.Ordinal / 16);
                Assert.True(z.ActiveUntil < setup.Deadline);
            });
        }
        uint end = channel.Window(GlitchTuning.Rounds - 1).End;
        channel.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, end, 100, 1000, Frame(end, (0, 0, true))), Hazards(setup, end));
        Assert.Empty(channel.Observe().Zones);
    }

    [Fact]
    public void BoardStacksKeepTheirExistingCollisionLifetimeEvenAfterBreaking()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakBoards, SpeedTier.One, count: 2);
        var plan = MayhemWorld.Plan(sim);
        MayhemDirectorTests.RunTo(sim, plan.PlayStart(episode) + 1);
        var board = MayhemWorld.View(sim).Scene.Targets[0];
        for (ulong activation = 1; activation <= board.Hits; activation++)
            sim.MayhemShow!.Contact(new(MayhemContactKind.SolidHit, 0, 0, 1, board.EntityId, -1, activation), sim.CurrentTick, sim);
        board = MayhemWorld.View(sim).Scene.Targets[0];
        Assert.Equal(0, board.HitsLeft);
        Assert.Equal(plan.CallStart(episode), board.ActiveFrom);
        Assert.Equal(plan.Deadline(episode), board.ActiveUntil);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, ActivationId = 1000, X = board.Center.X, Y = board.Center.Y,
            Z = board.Center.Z, Radius = .3f, Damage = 4, DurationTicks = 1 });
        MayhemWorld.Step(sim);
        Assert.Contains(sim.LastTickHits, hit => hit.TargetEntityId == board.EntityId);
        Assert.False(board.ActiveAt(plan.Deadline(episode)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SumoAllowsTwoSmallFightersUnderOffButNotTheWholeShovingCast(byte twist)
    {
        var (seed, episode) = MayhemWorld.SeedWith(MayhemChannels.Sumo, 2, new(twist));
        var sim = MayhemWorld.NewShow(seed, FriendlyFireMode.Off, count: 2, roster: new[] { "manki", "bashie" });
        MayhemWorld.SkipToCall(sim, episode);
        MayhemWorld.Step(sim);
        var ring = Assert.Single(MayhemWorld.View(sim).Scene.Zones);
        float diameter = 2 * ring.RadiusAt(MayhemWorld.Plan(sim).Deadline(episode) - 1);
        Assert.True(diameter > 2 * sim.GetDefinition(1).CapsuleRadius);
        var cpus = sim.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(12, 0));
        var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.StepCpus(s, cpus));
        Assert.All(passed, p => Assert.True(p));

        var shoves = MayhemWorld.NewShow(seed, FriendlyFireMode.Shoves, count: 4, roster: MayhemWorld.Four);
        MayhemWorld.SkipToCall(shoves, episode);
        var shovingCpus = shoves.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(12, 0));
        var survivors = MayhemWorld.RunEpisode(shoves, episode, s => MayhemWorld.StepCpus(s, shovingCpus));
        Assert.Contains(true, survivors);
        Assert.Contains(false, survivors);
    }

}
