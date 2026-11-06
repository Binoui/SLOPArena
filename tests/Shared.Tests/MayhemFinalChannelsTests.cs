using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

public class MayhemFinalChannelsTests
{
    private static ChannelSetup Setup(ChannelId id, byte twist = 1, int count = 4)
    {
        var seats = Enumerable.Range(0, count).Select(i => new ChannelSeat((byte)i, (ulong)i + 1, new(0, 1, 0, 0), .35f, 1.7f)).ToArray();
        return new(new SoloAiring(new(id, new(twist), 101)), 0, 0, SpeedTier.One, 10, 100, 100u + MayhemChannels.Find(id).Timing.PlayTicks,
            MayhemWorld.Arena().Arcade!.Mayhem!, seats);
    }
    private static MayhemFrame Frame(uint tick, params (float X, float Z)[] points) => new(tick, MayhemWorld.Arena().Arcade!.Mayhem!,
        points.Select((p, i) => new MayhemFighterFact((byte)i, (ulong)i + 1, TestHelpers.PlayerState(p.X, p.Z), TestHelpers.EngineDef, true, true)).ToArray());
    private static MayhemHazards Hazards(ChannelSetup s, uint tick)
    { var h = new MayhemHazards(new(), MayhemShow.HostEntityId, s.Stage); h.Bind(null, tick, 0); return h; }
    private static void Tick(IMayhemChannel c, ChannelSetup s, MayhemFrame f, ChannelMoment moment = ChannelMoment.AfterCombat)
        => c.Tick(new(moment, MayhemPhase.Play, f.Tick, s.PlayStart, s.Deadline, f), Hazards(s, f.Tick));
    private static MayhemJudgement Judge(IMayhemChannel c, MayhemFrame f, int seat = 0, bool deadline = false) => c.Judge(new(f.Fighters[seat], f, deadline));
    private static MayhemFrame Swing(MayhemFrame f, params int[] seats) => f with { Fighters = f.Fighters.Select(x => {
        if (!seats.Contains((int)x.Seat)) return x;
        var s = x.State; s.AttackSequence++; return x with { State = s };
    }).ToArray() };

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void DrawLatchesFirstSwingAndEarlyOrFakeWordsMisfire(byte twist)
    {
        var setup = Setup(MayhemChannels.QuickDraw, twist);
        var c = QuickDrawChannel.Start(setup);
        var cues = c.Observe().Watches; var draw = cues.Single(w => w.Kind == MayhemWatchKind.Draw);
        var before = Frame(twist == 2 ? cues[0].StartTick : draw.StartTick - 1, (0, 0), (1, 0));
        Tick(c, setup, before, ChannelMoment.BeforeActions); var after = Swing(before, 0); Tick(c, setup, after);
        Assert.Equal(FailCause.Misfire, Judge(c, after).Cause);
        var onTime = Frame(draw.StartTick, (0, 0), (1, 0));
        Tick(c, setup, onTime, ChannelMoment.BeforeActions); onTime = Swing(onTime, 1); Tick(c, setup, onTime);
        Assert.Equal(JudgementKind.Ready, Judge(c, onTime, 1).Kind);
        Assert.Equal(FailCause.Misfire, Judge(c, onTime).Cause);
        var idle = QuickDrawChannel.Start(setup);
        Assert.Equal(FailCause.TooSlow, Judge(idle, onTime, 1, true).Cause);
        var p = new CpuProbe(onTime.Fighters[1], onTime with { Tick = draw.StartTick - 1 }, MayhemControls.Playing(ExtraVerb.None), setup.PlayStart, setup.Deadline, 30);
        Assert.Equal(CpuAction.Wait, idle.Cpu(p).Action);
        Assert.Equal(CpuAction.Normal, idle.Cpu(p with { Frame = onTime }).Action);
    }
    [Fact]
    public void DuelTiesPassAndMissingDrawDoesNotCancelTheLastFinisher()
    {
        var s = Setup(MayhemChannels.QuickDraw, 3); var c = QuickDrawChannel.Start(s);
        uint draw = c.Observe().Watches[0].StartTick;
        var f = Frame(draw, (0, 0), (1, 0), (2, 0), (3, 0));
        Tick(c, s, f, ChannelMoment.BeforeActions); Tick(c, s, Swing(f, 0, 1, 2, 3));
        for (int i = 0; i < 4; i++) Assert.Equal(JudgementKind.Ready, Judge(c, f, i, true).Kind);
        c = QuickDrawChannel.Start(s);
        Tick(c, s, f, ChannelMoment.BeforeActions); Tick(c, s, Swing(f, 0));
        f = f with { Tick = draw + 1 }; Tick(c, s, f, ChannelMoment.BeforeActions); Tick(c, s, Swing(f, 1, 2));
        Assert.Equal(JudgementKind.Ready, Judge(c, f, 2, true).Kind);
        f = f with { Tick = draw + 2 }; Tick(c, s, f, ChannelMoment.BeforeActions); Tick(c, s, Swing(f, 3));
        Assert.Equal(JudgementKind.Incomplete, Judge(c, f, 3, true).Kind);
        Assert.Equal(JudgementKind.Ready, Judge(c, f, 2, true).Kind);
    }
    [Theory]
    [InlineData(FriendlyFireMode.Off)] [InlineData(FriendlyFireMode.Shoves)]
    public void PotatoTransfersOncePerTouchWithoutChainingOrImmediateReturn(FriendlyFireMode fire)
    {
        var s = Setup(MayhemChannels.HotPotato, count: 3); var c = HotPotatoChannel.Start(s);
        byte start = c.Observe().Carries[0].HolderSeat;
        var f = Frame(100, (0, 0), (0, 0), (0, 0)) with { FriendlyFire = fire };
        Tick(c, s, f, ChannelMoment.BeforeActions); Tick(c, s, f);
        var carry = Assert.Single(c.Observe().Carries);
        Assert.NotEqual(start, carry.HolderSeat); Assert.Equal(start, carry.LastGiver);
        Tick(c, s, f with { Tick = 101 }, ChannelMoment.BeforeActions); Tick(c, s, f with { Tick = 101 });
        Assert.Equal(carry, c.Observe().Carries[0]);
        f = Frame(102, (-5, 0), (0, 0), (5, 0)) with { FriendlyFire = fire }; Tick(c, s, f);
        var pair = f.Fighters.Select(x => x with { State = TestHelpers.PlayerState(x.Seat == start || x.Seat == carry.HolderSeat ? 0 : 5, 0) }).ToArray();
        f = f with { Tick = 103, Fighters = pair }; Tick(c, s, f);
        Assert.Equal(carry, c.Observe().Carries[0]);
    }
    [Fact]
    public void PotatoShovePassesAtRangeAndWithdrawnHolderMovesToNearestLiveSeat()
    {
        var s = Setup(MayhemChannels.HotPotato, count: 3); var c = HotPotatoChannel.Start(s);
        byte holder = c.Observe().Carries[0].HolderSeat; byte recipient = (byte)((holder + 1) % 3);
        var f = Frame(100, (-5, 0), (0, 0), (5, 0)); Tick(c, s, f, ChannelMoment.BeforeActions);
        c.Contact(new(MayhemContactKind.FighterHit, 0, 0, (ulong)holder + 1, (ulong)recipient + 1, recipient, 9), Hazards(s, 100));
        Tick(c, s, f); Assert.Equal(recipient, c.Observe().Carries[0].HolderSeat);
        f = f with { Tick = 101, Fighters = f.Fighters.Select(x => x.Seat == recipient ? x with { Live = false } : x).ToArray() }; Tick(c, s, f);
        Assert.NotEqual(recipient, c.Observe().Carries[0].HolderSeat);
        Assert.Null(c.Observe().Carries[0].LastGiver);
    }
    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void TwoPotatoesKeepDistinctHoldersAndBangBeforeDeadline(int count)
    {
        var s = Setup(MayhemChannels.HotPotato, 2, count); var c = HotPotatoChannel.Start(s);
        var carries = c.Observe().Carries; Assert.Equal(count >= 3 ? 2 : 1, carries.Count);
        Assert.Equal(carries.Count, carries.Select(x => x.HolderSeat).Distinct().Count());
        uint fuse = carries[0].FuseEnd; Assert.True(fuse < s.Deadline);
        var f = Frame(fuse, Enumerable.Range(0, count).Select(i => ((float)i * 3, 0f)).ToArray()); Tick(c, s, f);
        foreach (var carry in carries) Assert.Equal(FailCause.Hazard, Judge(c, f, carry.HolderSeat).Cause);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void BarrageSamplesShieldAtEveryBlastAndRingRemainsUnblockable(byte twist)
    {
        var s = Setup(MayhemChannels.Block, twist); var c = BlockChannel.Start(s);
        Assert.All(c.Observe().Bombs, b => Assert.Equal(MayhemBombKind.Barrage, b.Kind));
        foreach (var blast in c.Observe().Bombs)
        {
            Assert.True(blast.ImpactTick < s.Deadline);
            var f = Frame(blast.ImpactTick, (0, 0), (1, 0)); var shield = f.Fighters[0].State; shield.State = ActionState.Shielding;
            f.Fighters[0] = f.Fighters[0] with { State = shield }; Tick(c, s, f);
            Assert.Equal(JudgementKind.Ready, Judge(c, f).Kind); Assert.Equal(FailCause.Hazard, Judge(c, f, 1).Cause);
        }
        if (twist == 3) Assert.Equal(0, c.Contact(new(MayhemContactKind.HazardBody, 0, 0, MayhemShow.HostEntityId, 1, 0, 99), Hazards(s, 200)).FailSeat);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void DizzyHostCountsDistinctOutOfOrderActivationsPerSeatAndQuotaDoesNotStopWake(byte twist)
    {
        var s = Setup(MayhemChannels.GetHim, twist); var c = GetHimChannel.Start(s);
        var f = Frame(100, (0, 0), (1, 0)); Tick(c, s, f, ChannelMoment.BeforeActions);
        var target = Assert.Single(c.Observe().Targets); Assert.NotEqual(MayhemShow.HostEntityId, target.EntityId); Assert.Null(target.OwnerSeat);
        void Hit(ulong who, ulong activation) => c.Contact(new(MayhemContactKind.SolidHit, 0, 0, who, target.EntityId, -1, activation, AttackSlot: AbilitySlots.Normal1), Hazards(s, f.Tick));
        Hit(1, 900); Hit(1, 900); Assert.Equal(1, c.Observe().HitProgress[0].Hits);
        Hit(1, 800); Assert.Equal(GetHimTuning.Quota, c.Observe().HitProgress[0].Hits);
        Assert.Equal(JudgementKind.Ready, Judge(c, f).Kind); Assert.Equal(FailCause.HeGotUp, Judge(c, f, 1, true).Cause);
        Assert.Equal(0, c.Contact(new(MayhemContactKind.HazardBody, 0, 0, MayhemShow.HostEntityId, 1, 0, 99), Hazards(s, 500)).FailSeat);
        f = f with { Tick = target.ActiveUntil }; Tick(c, s, f, ChannelMoment.BeforeActions); Hit(2, 1000);
        Assert.Equal(0, c.Observe().HitProgress[1].Hits); Assert.False(target.ActiveAt(f.Tick));
        Assert.All(c.Observe().Rings, r => Assert.True(r.SlamTick + r.LifetimeTicks < s.Deadline));
        Assert.All(c.Observe().Bombs, b => Assert.True(b.ImpactTick + MayhemHazardTuning.FlightGraceTicks < s.Deadline));
    }
    [Fact]
    public void ThirteenRegularsDealTwelveUniqueSlotsAndGetHimClosesEveryShow()
    {
        Assert.Equal(13, MayhemChannels.Regulars.Count);
        for (uint seed = 1; seed <= 20000; seed++)
        {
            var p = MayhemShowPlan.For(seed);
            var regulars = Enumerable.Range(0, MayhemShowTuning.Episodes).Where(e => !MayhemShowPlan.IsBoss(e)).Select(p.ChannelAt).ToArray();
            Assert.Equal(12, regulars.Distinct().Count()); Assert.Equal(MayhemChannels.GetHim, p.ChannelAt(14));
            Assert.IsType<RemixAiring>(p.AiringAt(4)); Assert.IsType<RemixAiring>(p.AiringAt(9));
        }
    }
    [Theory]
    [InlineData(11)] [InlineData(12)] [InlineData(13)] [InlineData(15)]
    public void CarefulFinalChannelsMeetTheirContestOnEveryTwistAndCast(ushort id)
    {
        var entry = MayhemChannels.Find(new(id));
        var problems = new List<string>();
        foreach (var twist in entry.Twists)
        foreach (int count in new[] { 2, 3, 4 })
        for (uint seed = 1; seed <= 12; seed++)
        {
            var sim = MayhemWorld.NewShow(seed, count: count, roster: MayhemWorld.Four);
            int episode = id == 15 ? 14 : 10;
            MayhemWorld.Pin(sim, episode, new SoloAiring(new(entry.Id, twist.Id, MayhemMath.ChannelSeed(seed, episode, 0))));
            MayhemWorld.SkipToCall(sim, episode);
            var cpus = sim.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(12, 0));
            var passed = MayhemWorld.RunEpisode(sim, episode, s => MayhemWorld.StepCpus(s, cpus));
            bool contest = entry.ContestFor(twist.Id) == ChannelContest.Shared ? passed.All(p => p) : passed.Any(p => p);
            if (!contest) problems.Add($"{entry.CopyKey} {twist.CopyKey}, {count} seats seed {seed}. "
                + string.Join(", ", MayhemWorld.View(sim).Contestants.Where(c => c.Failure != null).Select(c => $"{c.Seat} {c.Failure!.Value.Cause}")));
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void FinaleIdleMissesQuotaAndSlippingCpusCanLose(byte twist)
    {
        var (idle, e) = MayhemWorld.Airing(MayhemChannels.GetHim, SpeedTier.Three, count: 2, twist: new(twist));
        Assert.All(MayhemWorld.RunEpisode(idle, e, s => MayhemWorld.Step(s)), p => Assert.False(p));
        var (slip, episode) = MayhemWorld.Airing(MayhemChannels.GetHim, SpeedTier.Three, roster: MayhemWorld.Four, twist: new(twist));
        var cpus = slip.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(30, 100));
        Assert.Contains(false, MayhemWorld.RunEpisode(slip, episode, s => MayhemWorld.StepCpus(s, cpus)));
    }

    public static TheoryData<CharacterClass> Roster() => RosterFighters.All();
    [Theory]
    [MemberData(nameof(Roster))]
    public void CarefulFinaleRosterFitsTheBoundedClock(CharacterClass fighter)
    {
        foreach (var twist in MayhemChannels.Find(MayhemChannels.GetHim).Twists)
        {
            var (sim, e) = MayhemWorld.Airing(MayhemChannels.GetHim, SpeedTier.Three, count: 2, roster: new[] { fighter }, twist: twist.Id);
            var cpus = sim.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(12, 0));
            var passed = MayhemWorld.RunEpisode(sim, e, s => MayhemWorld.StepCpus(s, cpus));
            Assert.True(passed.All(p => p), $"{fighter} {twist.CopyKey}. " + string.Join(", ", MayhemWorld.View(sim).Contestants.Select(c => c.Failure?.Cause)));
        }
    }

    [Theory]
    [InlineData(FriendlyFireMode.Off)] [InlineData(FriendlyFireMode.Shoves)]
    public void EngineShovesPassPotatoesOnlyUnderShovesAndBumpsWorkUnderEitherRule(FriendlyFireMode fire)
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.HotPotato, SpeedTier.One, count: 3, fire: fire);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 1);
        byte holder = sim.MayhemView!.Scene.Carries[0].HolderSeat;
        byte other = (byte)((holder + 1) % 3);
        for (ulong id = 1; id <= 3; id++) MayhemDirectorTests.Place(sim, id, ((float)id - 2) * 4, 0);
        var target = sim.GetState((ulong)other + 1);
        sim.Resolver.Spawn(new Hitbox { OwnerId = (ulong)holder + 1, ActivationId = 1001, X = target.PX, Y = target.PY, Z = target.PZ,
            Radius = .3f, Damage = 4, DurationTicks = 1 });
        MayhemWorld.Step(sim);
        Assert.Equal(fire == FriendlyFireMode.Shoves ? other : holder, sim.MayhemView.Scene.Carries[0].HolderSeat);
        holder = sim.MayhemView.Scene.Carries[0].HolderSeat;
        other = Enumerable.Range(0, 3).Select(i => (byte)i).First(i => i != holder && i != sim.MayhemView.Scene.Carries[0].LastGiver);
        for (ulong id = 1; id <= 3; id++)
        {
            var body = sim.GetState(id); body.KVX = body.KVZ = 0; body.HitstunTicks = 0; body.State = ActionState.Idle; sim.SetState(id, body);
            MayhemDirectorTests.Place(sim, id, id == (ulong)holder + 1 || id == (ulong)other + 1 ? 0 : 6, 0);
        }
        MayhemWorld.Step(sim);
        Assert.Equal(other, sim.MayhemView.Scene.Carries[0].HolderSeat);
    }
    [Theory]
    [InlineData(11, 1)] [InlineData(11, 2)] [InlineData(11, 3)]
    [InlineData(12, 1)] [InlineData(12, 2)]
    [InlineData(13, 1)] [InlineData(13, 2)] [InlineData(13, 3)]
    [InlineData(15, 1)] [InlineData(15, 2)]
    public void NewChannelRestoresAtEveryTickWithWithdrawalsAndSlips(ushort id, byte twist)
    {
        var (sim, e) = MayhemWorld.Airing(new(id), SpeedTier.Three, roster: MayhemWorld.Four, twist: new(twist));
        var (copy, _) = MayhemWorld.Airing(new(id), SpeedTier.Three, roster: MayhemWorld.Four, twist: new(twist), reverse: true);
        var plan = MayhemWorld.Plan(sim); var cpus = sim.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(18, 40));
        MayhemWorld.Step(sim);
        while (sim.CurrentTick < plan.Deadline(e) + 1)
        {
            if (sim.CurrentTick == plan.CallStart(e)) sim.WithdrawMayhemPlayer(4);
            if (sim.CurrentTick == plan.PlayStart(e) + 30) sim.WithdrawMayhemPlayer(3);
            sim.ClearPresentationEvents(); copy.RestoreSnapshot(sim.CaptureSnapshot());
            var inputs = cpus.ToDictionary(x => x.Key, x => x.Value.Decide(sim, x.Key));
            sim.Tick(inputs); copy.Tick(inputs);
            Assert.True(sim.CaptureSnapshot().ContentEquals(copy.CaptureSnapshot()), $"{id}/{twist}@{sim.CurrentTick}");
            Assert.Equal(sim.LastTickMayhemEvents, copy.LastTickMayhemEvents);
            Assert.Equal(sim.MayhemView!.Scene.Carries, copy.MayhemView!.Scene.Carries);
            Assert.Equal(sim.MayhemView.Scene.HitProgress, copy.MayhemView.Scene.HitProgress);
        }
    }

}
