using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>The rules two adversarial reviews proved broken in ADR-0063 units 4 to 9. A mistake costs only its maker.</summary>
public class MayhemVarietyFixTests
{
    private static readonly string[] Fg = { "fightguy" };
    private static readonly byte[] Normals = { AbilitySlots.Normal1, AbilitySlots.Normal2, AbilitySlots.Normal3 };
    private static string[] Roster { get { var list = new List<string>(); foreach (var f in RosterFighters.All()) list.Add(f); return list.ToArray(); } }

    private static void Hit(ServerSimulation sim, TargetCue target, ulong owner, ulong activation)
        => sim.Resolver.Spawn(new Hitbox { OwnerId = owner, ActivationId = activation, AttackSlot = AbilitySlots.Normal1,
            X = target.Center.X, Y = target.Center.Y, Z = target.Center.Z, Radius = .2f, Damage = 4, DurationTicks = 1 });

    private static void HitFighter(ServerSimulation sim, ulong target, ulong owner, ulong activation, bool unblockable = false)
    {
        var body = sim.GetState(target);
        sim.Resolver.Spawn(new Hitbox { OwnerId = owner, ActivationId = activation, AttackSlot = AbilitySlots.Normal1,
            X = body.PX, Y = body.PY, Z = body.PZ, Radius = .9f, Damage = 4, DurationTicks = 1, Unblockable = unblockable });
    }

    private static string Who(ServerSimulation sim) => string.Join(" | ", MayhemWorld.View(sim).Contestants.Select(c =>
        $"seat{c.Seat} {c.Verdict}{(c.Failure is { } f ? $" {f.Cause}@{f.Tick}" : "")}"));

    private static bool Passed(ServerSimulation sim, int seat, int episode)
        => (MayhemWorld.View(sim).Contestants[seat].PassMask >> episode & 1) != 0;

    private static IEnumerable<(uint Seed, int Episode)> SeedsAiring(ChannelId channel, TwistId twist, int count)
    {
        for (uint seed = 1; seed < 500_000 && count > 0; seed++)
        {
            var plan = MayhemShowPlan.For(seed);
            for (int episode = 0; episode < MayhemShowTuning.Episodes; episode++)
                if (plan.AiringAt(episode) is SoloAiring solo && solo.Draw.Channel == channel && solo.Draw.Twist == twist)
                {
                    count--; yield return (seed, episode); break;
                }
        }
    }

    private static ChannelSetup Setup(ChannelId id, byte twist = 1, int count = 4, uint seed = 101)
    {
        var seats = Enumerable.Range(0, count).Select(i => new ChannelSeat((byte)i, (ulong)i + 1, new(0, 1, 0, 0), .35f, 1.7f)).ToArray();
        return new(new SoloAiring(new(id, new(twist), seed)), 0, 0, SpeedTier.One, 10, 100, 100u + MayhemChannels.Find(id).Timing.PlayTicks,
            MayhemWorld.Arena().Arcade!.Mayhem!, seats);
    }

    private static MayhemFrame Frame(uint tick, params (float X, float Z)[] points) => new(tick, MayhemWorld.Arena().Arcade!.Mayhem!,
        points.Select((p, i) => new MayhemFighterFact((byte)i, (ulong)i + 1, TestHelpers.PlayerState(p.X, p.Z), TestHelpers.EngineDef, true, true)).ToArray());

    private static MayhemFrame Without(MayhemFrame frame, params int[] seats) => frame with { Fighters = frame.Fighters
        .Select(f => seats.Contains(f.Seat) ? f with { Live = false } : f).ToArray() };

    private static MayhemHazards Hazards(ChannelSetup s, uint tick)
    { var h = new MayhemHazards(new(), MayhemShow.HostEntityId, s.Stage); h.Bind(null, tick, 0); return h; }

    private static void Tick(IMayhemChannel c, ChannelSetup s, MayhemFrame f, ChannelMoment moment = ChannelMoment.AfterCombat)
        => c.Tick(new(moment, MayhemPhase.Play, f.Tick, s.PlayStart, s.Deadline, f), Hazards(s, f.Tick));

    // Problem 1: a shared target takes one hit per attacker per tick.

    [Theory]
    [InlineData(FriendlyFireMode.Off)]
    [InlineData(FriendlyFireMode.Shoves)]
    public void SameTickHitsOnAHeadReachItsSeatTieBreak(FriendlyFireMode fire)
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Bonk, SpeedTier.One, count: 3, fire: fire);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        var head = sim.MayhemView!.Scene.Targets[0];
        Hit(sim, head, 1, 1001); Hit(sim, head, 2, 1002);
        MayhemWorld.Step(sim);
        Assert.True(sim.MayhemView.Contestants[0].ObjectiveMet, "Both normals touch the head on one tick, so the lower seat wins it.");
        Assert.False(sim.MayhemView.Contestants[1].ObjectiveMet);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TwoCookedJabsOnOneHeadGoToTheLowerSeat(int attackers)
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Bonk, SpeedTier.One, count: 2, roster: Fg, fire: FriendlyFireMode.Off);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        var target = Assert.Single(sim.MayhemView!.Scene.Targets);
        for (ulong id = 1; id <= 2; id++)
        {
            MayhemDirectorTests.Place(sim, id, target.Center.X + (id == 1 ? -.4f : .4f), target.Center.Z - 1);
            var state = sim.GetState(id); state.FacingYaw = state.AimYaw = 0; sim.SetState(id, state);
        }
        var inputs = new Dictionary<ulong, InputState>();
        for (ulong id = 1; id <= 2; id++)
            if ((attackers >> ((int)id - 1) & 1) != 0) inputs[id] = new InputState { ActiveSlot = AbilitySlots.Normal1, FaceToCamera = true };
        sim.Tick(inputs);
        MayhemWorld.Step(sim, 30);
        int winner = attackers == 2 ? 1 : 0;
        Assert.True(sim.MayhemView.Contestants[winner].ObjectiveMet);
        Assert.False(sim.MayhemView.Contestants[1 - winner].ObjectiveMet);
    }

    [Theory]
    [InlineData(FriendlyFireMode.Off)]
    [InlineData(FriendlyFireMode.Shoves)]
    public void EveryContestantsFinalNormalCountsOnTheDizzyHost(FriendlyFireMode fire)
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.GetHim, SpeedTier.Three, count: 2, fire: fire, twist: new(2));
        var plan = MayhemWorld.Plan(sim);
        MayhemDirectorTests.RunTo(sim, plan.PlayStart(episode) + 1);
        var target = Assert.Single(sim.MayhemView!.Scene.Targets);
        Hit(sim, target, 1, 1001); MayhemWorld.Step(sim);
        Hit(sim, target, 2, 1002); MayhemWorld.Step(sim);
        MayhemDirectorTests.RunTo(sim, target.ActiveUntil - 2);
        Hit(sim, target, 1, 1003); Hit(sim, target, 2, 1004);
        MayhemWorld.Step(sim);
        Assert.All(sim.MayhemView.Scene.HitProgress, p => Assert.Equal(2, p.Hits));
        MayhemDirectorTests.RunTo(sim, plan.Deadline(episode));
        Assert.All(sim.MayhemView.Contestants, c => Assert.Equal(MayhemVerdict.Passed, c.Verdict));
    }

    [Fact]
    public void ARivalsSameTickHitDoesNotEatTheOwnersBoardHit()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.BreakBoards, SpeedTier.One, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        var stack = sim.MayhemView!.Scene.Targets.Single(t => t.OwnerSeat == 0);
        Hit(sim, stack, 1, 1001); Hit(sim, stack, 2, 1002);
        MayhemWorld.Step(sim);
        Assert.Equal(stack.HitsLeft - 1, sim.MayhemView.Scene.Targets.Single(t => t.OwnerSeat == 0).HitsLeft);
    }

    [Fact]
    public void OneAttackerStillLandsOneHitPerSharedTargetPerTick()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.GetHim, SpeedTier.Three, count: 2);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        var target = Assert.Single(sim.MayhemView!.Scene.Targets);
        Hit(sim, target, 1, 1001); Hit(sim, target, 1, 1002);
        MayhemWorld.Step(sim);
        Assert.Equal(1, sim.MayhemView.Scene.HitProgress.Single(p => p.Seat == 0).Hits);
    }

    [Fact]
    public void AFighterStillTakesOneHitPerTick()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 3);
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(episode) + 1);
        HitFighter(sim, 1, 2, 1001); HitFighter(sim, 1, 3, 1002);
        MayhemWorld.Step(sim);
        Assert.Single(sim.LastTickHits, h => h.TargetEntityId == 1);
    }

    // Problem 2: a shield stops a blockable shove.

    [Theory]
    [InlineData(FriendlyFireMode.Shoves)]
    [InlineData(FriendlyFireMode.Off)]
    public void AShieldHeldThroughEverySwingTimingSurvivesTheBlast(FriendlyFireMode fire)
    {
        var lost = new List<string>();
        for (int lead = 0; lead <= 30; lead++)
        {
            var (sim, e) = MayhemWorld.Airing(MayhemChannels.Block, SpeedTier.One, count: 2, roster: Fg, fire: fire, twist: new(1));
            var plan = MayhemWorld.Plan(sim);
            uint press = plan.Deadline(e) - BlockTuning.LastMarginTicks - (uint)lead;
            MayhemDirectorTests.RunTo(sim, plan.PlayStart(e) + 2);
            MayhemDirectorTests.Place(sim, 1, 0, 0); MayhemDirectorTests.Place(sim, 2, 0, .9f);
            var face = sim.GetState(1); face.FacingYaw = 0; sim.SetState(1, face);
            while (sim.CurrentTick < plan.Deadline(e))
            {
                var inputs = new Dictionary<ulong, InputState> { [2] = new InputState { ShieldHeld = true } };
                if (sim.CurrentTick + 1 == press) inputs[1] = new InputState { ActiveSlot = AbilitySlots.Normal1 };
                sim.Tick(inputs);
            }
            if (!Passed(sim, 1, e)) lost.Add($"lead {lead}: {MayhemWorld.View(sim).Contestants[1].Failure?.Cause}");
        }
        Assert.True(lost.Count == 0, $"{fire}: {string.Join(", ", lost)}");
    }

    [Fact]
    public void AShoverCannotCancelAShieldedRivalAndKeepTheirOwnPass()
    {
        var wins = new List<string>();
        foreach (var fighter in new[] { "manki", "fightguy" })
        for (int lead = 4; lead <= 45; lead++)
        {
            var (sim, e) = MayhemWorld.Airing(MayhemChannels.Block, SpeedTier.One, count: 2, roster: new[] { fighter }, twist: new(1));
            var plan = MayhemWorld.Plan(sim);
            uint press = plan.Deadline(e) - BlockTuning.LastMarginTicks - (uint)lead;
            MayhemDirectorTests.RunTo(sim, plan.PlayStart(e) + 2);
            MayhemDirectorTests.Place(sim, 1, 0, 0); MayhemDirectorTests.Place(sim, 2, 0, .9f);
            var face = sim.GetState(1); face.FacingYaw = 0; sim.SetState(1, face);
            while (sim.CurrentTick < plan.Deadline(e))
            {
                var inputs = new Dictionary<ulong, InputState> { [2] = new InputState { ShieldHeld = true } };
                if (sim.CurrentTick + 1 == press) inputs[1] = new InputState { ActiveSlot = AbilitySlots.Normal1 };
                else if (sim.CurrentTick + 1 > press) inputs[1] = new InputState { ShieldHeld = true };
                sim.Tick(inputs);
            }
            if (Passed(sim, 0, e) && !Passed(sim, 1, e)) wins.Add($"{fighter} lead {lead}");
        }
        Assert.True(wins.Count == 0, string.Join(", ", wins));
    }

    private static ServerSimulation Shielding(out ulong attacker, out ulong shield)
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.Block, SpeedTier.One, count: 2, twist: new(1));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 2);
        attacker = 1; shield = 2;
        MayhemDirectorTests.Place(sim, 1, 0, 0); MayhemDirectorTests.Place(sim, 2, 0, .9f);
        for (int i = 0; i < 12; i++) sim.Tick(new() { [2] = new InputState { ShieldHeld = true } });
        Assert.Equal(ActionState.Shielding, sim.GetState(2).State);
        return sim;
    }

    [Fact]
    public void ABlockableShoveOnAShieldIsABlock()
    {
        var sim = Shielding(out ulong attacker, out ulong shield);
        HitFighter(sim, shield, attacker, 1001);
        sim.Tick(new() { [shield] = new InputState { ShieldHeld = true } });
        var state = sim.GetState(shield);
        Assert.Equal(ActionState.Shielding, state.State);
        Assert.Equal(0, state.HitstunTicks);
        Assert.True(Assert.Single(sim.LastTickHits, h => h.TargetEntityId == shield).Blocked);
    }

    [Fact]
    public void AnUnblockableShoveStillMovesAShield()
    {
        var sim = Shielding(out ulong attacker, out ulong shield);
        HitFighter(sim, shield, attacker, 1001, unblockable: true);
        sim.Tick(new() { [shield] = new InputState { ShieldHeld = true } });
        Assert.Equal(ActionState.Hitstun, sim.GetState(shield).State);
    }

    // Problem 3: a swing aimed at a channel target lands whether or not a rival stands to the side.

    private static (ServerSimulation Sim, TargetCue Target, float X, float Z) SwingFrom(ChannelId channel, string fighter, FriendlyFireMode fire, byte twist)
    {
        var (sim, e) = MayhemWorld.Airing(channel, channel == MayhemChannels.GetHim ? SpeedTier.Three : SpeedTier.One, count: 2,
            roster: new[] { fighter }, fire: fire, twist: new(twist));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 2);
        var target = MayhemWorld.View(sim).Scene.Targets.First(t => t.OwnerSeat is null or 0);
        float standOff = channel == MayhemChannels.GetHim ? GetHimTuning.StandOff : channel == MayhemChannels.Bonk ? BonkTuning.StandOff : BreakBoardsTuning.CpuStandOff;
        return (sim, target, target.Center.X, target.Center.Z - (target.Radius + sim.GetDefinition(1)!.CapsuleRadius + standOff));
    }

    private static int Score(ServerSimulation sim, ChannelId channel, TargetCue target)
    {
        var view = MayhemWorld.View(sim);
        if (channel == MayhemChannels.GetHim) return view.Scene.HitProgress[0].Hits;
        if (channel == MayhemChannels.Bonk) return view.Contestants[0].ObjectiveMet ? 1 : 0;
        return target.HitsLeft - view.Scene.Targets.Single(t => t.EntityId == target.EntityId).HitsLeft;
    }

    private static int Swing(ChannelId channel, string fighter, byte slot, FriendlyFireMode fire, float? bearing, float reach, byte twist = 1, bool softTargetRival = false)
    {
        var (sim, target, px, pz) = SwingFrom(channel, fighter, fire, twist);
        MayhemDirectorTests.Place(sim, 1, px, pz);
        if (bearing is { } b)
        {
            float rad = b * MathF.PI / 180f;
            MayhemDirectorTests.Place(sim, 2, px + MathF.Sin(rad) * reach, pz + MathF.Cos(rad) * reach);
        }
        else MayhemDirectorTests.Place(sim, 2, target.Center.X < 0 ? 11 : -11, target.Center.Z < 0 ? 9 : -9);
        var face = sim.GetState(1); face.FacingYaw = 0; sim.SetState(1, face);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = slot, FaceToCamera = true, FacingYaw = 0, AimYaw = 0, TargetEntityId = softTargetRival ? (byte)2 : (byte)0 } });
        MayhemWorld.Step(sim, 120);
        return Score(sim, channel, target);
    }

    [Theory]
    [InlineData(15, FriendlyFireMode.Off)]
    [InlineData(15, FriendlyFireMode.Shoves)]
    [InlineData(8, FriendlyFireMode.Off)]
    [InlineData(8, FriendlyFireMode.Shoves)]
    [InlineData(6, FriendlyFireMode.Off)]
    [InlineData(6, FriendlyFireMode.Shoves)]
    public void AnIdleRivalOffTheLineCostsNoHit(ushort id, FriendlyFireMode fire)
    {
        var channel = new ChannelId(id);
        var lost = new List<string>();
        foreach (var fighter in Roster)
        foreach (byte slot in Normals)
        {
            if (Swing(channel, fighter, slot, fire, null, 0) == 0) continue;
            foreach (float bearing in new[] { 50f, 60f, 70f, 80f, -55f, -65f })
            foreach (float reach in new[] { 1.5f, 2.5f, 3.5f })
                if (Swing(channel, fighter, slot, fire, bearing, reach) == 0) lost.Add($"{fighter} slot {slot} rival {bearing} deg {reach} m");
        }
        Assert.True(lost.Count == 0, $"{lost.Count} stances lose the hit: {string.Join("; ", lost.Take(12))}");
    }

    [Theory]
    [InlineData(15)]
    [InlineData(8)]
    [InlineData(6)]
    public void AClientSoftTargetOnARivalDoesNotPullTheSwingAway(ushort id)
    {
        var channel = new ChannelId(id);
        foreach (byte slot in Normals)
            if (Swing(channel, "wibou", slot, FriendlyFireMode.Shoves, null, 0) > 0)
                Assert.True(Swing(channel, "wibou", slot, FriendlyFireMode.Shoves, 60, 2, softTargetRival: true) > 0, $"slot {slot}");
    }

    private static float YawAfterSwing(ServerSimulation sim, byte slot)
    {
        var face = sim.GetState(1); face.FacingYaw = 0; sim.SetState(1, face);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = slot, FaceToCamera = true, FacingYaw = 0, AimYaw = 0 } });
        MayhemWorld.Step(sim, 12);
        return sim.GetState(1).FacingYaw * 180f / MathF.PI;
    }

    [Fact]
    public void AimCorrectionNeverTurnsASwingOntoADecoy()
        => Assert.True(MathF.Abs(YawBesideHead(MayhemTargetKind.DecoyHead)) < 1f);

    [Fact]
    public void AimCorrectionTurnsASwingOntoARealHead()
        => Assert.True(YawBesideHead(MayhemTargetKind.CardboardHead) > 5f);

    private static float YawBesideHead(MayhemTargetKind kind)
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.Bonk, SpeedTier.One, count: 2, roster: new[] { "wibou" }, twist: new(2));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 2);
        var head = MayhemWorld.View(sim).Scene.Targets.Single(t => t.Kind == kind);
        MayhemDirectorTests.Place(sim, 2, -head.Center.X, -head.Center.Z);
        const float bearing = 45f * MathF.PI / 180f;
        MayhemDirectorTests.Place(sim, 1, head.Center.X - MathF.Sin(bearing) * 2.5f, head.Center.Z - MathF.Cos(bearing) * 2.5f);
        return YawAfterSwing(sim, AbilitySlots.Normal2);
    }

    [Fact]
    public void AimCorrectionIgnoresARivalsBoards()
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.BreakBoards, SpeedTier.One, count: 2, roster: new[] { "wibou" }, twist: new(2));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 2);
        var rival = MayhemWorld.View(sim).Scene.Targets.Single(t => t.OwnerSeat == 1);
        const float bearing = 60f * MathF.PI / 180f;
        MayhemDirectorTests.Place(sim, 1, rival.Center.X - MathF.Sin(bearing) * 1.5f, rival.Center.Z - MathF.Cos(bearing) * 1.5f);
        MayhemDirectorTests.Place(sim, 2, -rival.Center.X, -rival.Center.Z);
        foreach (byte slot in Normals) Assert.True(MathF.Abs(YawAfterSwing(sim, slot)) < 1f, $"slot {slot}");
    }

    [Fact]
    public void FriendlyFireOffNeverCorrectsTowardARival()
    {
        foreach (byte slot in Normals)
        {
            var (sim, e) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2, roster: new[] { "wibou" }, fire: FriendlyFireMode.Off);
            MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 2);
            MayhemDirectorTests.Place(sim, 1, 0, 0); MayhemDirectorTests.Place(sim, 2, 1.5f, 1.5f);
            Assert.True(MathF.Abs(YawAfterSwing(sim, slot)) < 1f, $"slot {slot}");
        }
    }

    [Fact]
    public void ShovesStillCorrectTowardARivalWhenNoChannelTargetIsNear()
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2, roster: new[] { "wibou" });
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 2);
        MayhemDirectorTests.Place(sim, 1, 0, 0); MayhemDirectorTests.Place(sim, 2, 1.5f, 1.5f);
        Assert.True(Normals.Any(slot => { float yaw = YawAfterSwing(sim, slot); MayhemWorld.Step(sim, 90); return yaw > 5f; }));
    }

    // Problem 4: two potatoes need three contestants.

    [Fact]
    public void TwoLiveContestantsKeepOnePotatoToPass()
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.HotPotato, SpeedTier.One, count: 3, roster: Fg, fire: FriendlyFireMode.Off, twist: new(2));
        var plan = MayhemWorld.Plan(sim);
        MayhemDirectorTests.RunTo(sim, plan.PlayStart(e) + 1);
        var holders = MayhemWorld.View(sim).Scene.Carries.Select(c => (int)c.HolderSeat).ToArray();
        Assert.Equal(2, holders.Length);
        int free = Enumerable.Range(0, 3).Single(s => !holders.Contains(s));
        sim.WithdrawMayhemPlayer((ulong)free + 1);
        MayhemWorld.Step(sim);
        byte holder = Assert.Single(MayhemWorld.View(sim).Scene.Carries).HolderSeat;
        ulong a = (ulong)holders[0] + 1, b = (ulong)holders[1] + 1;
        MayhemDirectorTests.Place(sim, a, -1, 0); MayhemDirectorTests.Place(sim, b, 3, 0); MayhemWorld.Step(sim);
        MayhemDirectorTests.Place(sim, a, -1, 0); MayhemDirectorTests.Place(sim, b, -.3f, 0); MayhemWorld.Step(sim);
        Assert.NotEqual(holder, Assert.Single(MayhemWorld.View(sim).Scene.Carries).HolderSeat);
        MayhemDirectorTests.RunTo(sim, plan.Deadline(e));
        Assert.Single(MayhemWorld.View(sim).Contestants, c => !c.Withdrawn && (c.PassMask >> e & 1) != 0);
    }

    [Fact]
    public void AHolderLeavingThreeContestantsRetiresTheirPotato()
    {
        var s = Setup(MayhemChannels.HotPotato, 2, 3); var c = HotPotatoChannel.Start(s);
        var carries = c.Observe().Carries;
        int gone = carries[0].HolderSeat, stays = carries[1].HolderSeat;
        var f = Without(Frame(100, (-5, 0), (0, 0), (5, 0)), gone);
        Tick(c, s, f);
        Assert.Equal(stays, Assert.Single(c.Observe().Carries).HolderSeat);
        Tick(c, s, f with { Tick = 101 });
        Assert.Equal(stays, Assert.Single(c.Observe().Carries).HolderSeat);
    }

    [Fact]
    public void FourContestantsLosingAHolderKeepBothPotatoes()
    {
        var s = Setup(MayhemChannels.HotPotato, 2, 4); var c = HotPotatoChannel.Start(s);
        var carries = c.Observe().Carries;
        var f = Without(Frame(100, (-6, 0), (-2, 0), (2, 0), (6, 0)), carries[0].HolderSeat);
        Tick(c, s, f);
        var after = c.Observe().Carries;
        Assert.Equal(2, after.Count);
        Assert.DoesNotContain(after, x => x.HolderSeat == carries[0].HolderSeat);
    }

    [Fact]
    public void ALoneBystanderIsNeverHandedABombNobodyCanTake()
    {
        var s = Setup(MayhemChannels.HotPotato, 1, 2); var c = HotPotatoChannel.Start(s);
        int holder = c.Observe().Carries[0].HolderSeat;
        var f = Without(Frame(100, (-5, 0), (5, 0)), holder);
        Tick(c, s, f);
        Assert.Empty(c.Observe().Carries);
        var bang = f with { Tick = s.Deadline - HotPotatoTuning.EndMarginTicks };
        Tick(c, s, bang);
        Assert.Equal(JudgementKind.Ready, c.Judge(new(bang.Fighters[1 - holder], bang, true)).Kind);
    }

    // Problem 5: the return lock lasts a moment, not the whole airing.

    private static (HotPotatoChannel Channel, ChannelSetup Setup, int First, int Second, uint PassTick) Passed2()
    {
        var s = Setup(MayhemChannels.HotPotato, count: 2); var c = HotPotatoChannel.Start(s);
        int first = c.Observe().Carries[0].HolderSeat;
        Tick(c, s, Frame(100, (0, 0), (5, 0)));
        Tick(c, s, Frame(101, (0, 0), (0, 0)));
        Assert.Equal(1 - first, c.Observe().Carries[0].HolderSeat);
        return (c, s, first, 1 - first, 101);
    }

    [Fact]
    public void AnImmediateReturnIsBarredForTheLockAndAllowedAfterIt()
    {
        var (c, s, first, second, pass) = Passed2();
        uint end = pass + HotPotatoTuning.ReturnLockTicks;
        Tick(c, s, Frame(end - 2, (0, 0), (5, 0)));
        Tick(c, s, Frame(end - 1, (0, 0), (0, 0)));
        var carry = c.Observe().Carries[0];
        Assert.Equal(second, carry.HolderSeat);
        Assert.Equal((byte)first, carry.LastGiver);
        Tick(c, s, Frame(end, (0, 0), (5, 0)));
        Assert.Null(c.Observe().Carries[0].LastGiver);
        Tick(c, s, Frame(end + 1, (0, 0), (0, 0)));
        Assert.Equal(first, c.Observe().Carries[0].HolderSeat);
    }

    [Fact]
    public void TheReturnLockSurvivesARestore()
    {
        var (c, s, first, second, pass) = Passed2();
        var writer = new SnapshotWriter();
        c.WriteSnapshot(writer);
        var reader = new SnapshotReader(writer.ToArray());
        var context = new ChannelRestore(s.Airing, s.Episode, s.Lane, s.Tier, s.Stage, new ulong[] { 1, 2 }, s.PlayStart, s.Deadline, 3);
        var restored = HotPotatoChannel.Read(reader, context);
        Tick(restored, s, Frame(pass + 1, (0, 0), (5, 0)));
        Tick(restored, s, Frame(pass + 2, (0, 0), (0, 0)));
        Assert.Equal(second, restored.Observe().Carries[0].HolderSeat);
        Assert.Equal((byte)first, restored.Observe().Carries[0].LastGiver);
    }

    [Fact]
    public void TwoContestantsCanPassTheBombBackLaterOn()
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.HotPotato, SpeedTier.One, count: 2, roster: Fg, fire: FriendlyFireMode.Off, twist: new(1));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 1);
        byte first = MayhemWorld.View(sim).Scene.Carries[0].HolderSeat;
        void Touch(bool together) { MayhemDirectorTests.Place(sim, 1, -1, 0); MayhemDirectorTests.Place(sim, 2, together ? -.3f : 3, 0); MayhemWorld.Step(sim); }
        Touch(false); Touch(true); Touch(false);
        Assert.NotEqual(first, MayhemWorld.View(sim).Scene.Carries[0].HolderSeat);
        for (int i = 0; i <= HotPotatoTuning.ReturnLockTicks; i++) Touch(false);
        Touch(true);
        Assert.Equal(first, MayhemWorld.View(sim).Scene.Carries[0].HolderSeat);
    }

    [Fact]
    public void CarefulCpusPassTheBombBackInAFullLengthTwoSeatAiring()
    {
        var stuck = new List<string>();
        int airings = 0;
        foreach (var (seed, episode) in SeedsAiring(MayhemChannels.HotPotato, new(1), 48))
        {
            var sim = MayhemWorld.NewShow(seed, FriendlyFireMode.Off, 2, roster: MayhemWorld.Four); MayhemWorld.SkipToCall(sim, episode);
            var plan = MayhemWorld.Plan(sim);
            if (plan.Deadline(episode) - plan.PlayStart(episode) != HotPotatoTuning.Timing.PlayTicks) continue;
            airings++;
            var cpus = sim.MayhemShow!.Roster.ToDictionary(f => f, _ => new MayhemCpu(12, 0));
            byte? holder = null; int passes = 0;
            while (sim.CurrentTick < plan.Deadline(episode))
            {
                MayhemWorld.StepCpus(sim, cpus);
                var view = MayhemWorld.View(sim);
                if (view.Cursor.Phase != MayhemPhase.Play || view.Scene.Carries.Count == 0) continue;
                byte now = view.Scene.Carries[0].HolderSeat;
                if (holder is { } h && h != now) passes++;
                holder = now;
            }
            if (passes < 2) stuck.Add($"seed {seed}: {passes} passes; {Who(sim)}");
        }
        Assert.True(airings >= 8, $"{airings} airings");
        Assert.True(stuck.Count == 0, string.Join("\n", stuck));
    }

    // Problem 6: a holder leaving as the fuse ends takes the bomb with them.

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, HotPotatoTuning.BangGraceTicks)]
    [InlineData(false, HotPotatoTuning.BangGraceTicks)]
    public void LeavingJustBeforeTheBangToastsNobodyElse(bool withdraw, int ticksBefore)
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.HotPotato, SpeedTier.One, count: 3, roster: Fg, fire: FriendlyFireMode.Off, twist: new(1));
        var plan = MayhemWorld.Plan(sim);
        MayhemDirectorTests.RunTo(sim, plan.PlayStart(e) + 1);
        var carry = MayhemWorld.View(sim).Scene.Carries[0];
        byte holder = carry.HolderSeat;
        MayhemDirectorTests.RunTo(sim, carry.FuseEnd - (uint)ticksBefore);
        if (withdraw) sim.WithdrawMayhemPlayer((ulong)holder + 1); else MayhemWorld.Ko(sim, (ulong)holder + 1);
        MayhemDirectorTests.RunTo(sim, plan.Deadline(e));
        Assert.True(MayhemWorld.View(sim).Contestants.Where(c => c.Seat != holder).All(c => (c.PassMask >> e & 1) != 0), Who(sim));
    }

    [Fact]
    public void AHolderLeavingBeforeTheGraceStillHandsTheBombOn()
    {
        var s = Setup(MayhemChannels.HotPotato, count: 3); var c = HotPotatoChannel.Start(s);
        var carry = c.Observe().Carries[0];
        uint tick = carry.FuseEnd - HotPotatoTuning.BangGraceTicks - 1;
        Tick(c, s, Without(Frame(tick, (-5, 0), (0, 0), (5, 0)), carry.HolderSeat));
        Assert.NotEqual(carry.HolderSeat, Assert.Single(c.Observe().Carries).HolderSeat);
    }

    // Problem 7: only the holder passes it with a hit.

    [Fact]
    public void ShovingTheHolderOnlyShoves()
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.HotPotato, SpeedTier.One, count: 3, roster: Fg, fire: FriendlyFireMode.Shoves, twist: new(1));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 1);
        byte holder = MayhemWorld.View(sim).Scene.Carries[0].HolderSeat;
        byte rival = (byte)((holder + 1) % 3);
        for (ulong id = 1; id <= 3; id++) MayhemDirectorTests.Place(sim, id, ((float)id - 2) * 5, 0);
        HitFighter(sim, (ulong)holder + 1, (ulong)rival + 1, 1001);
        MayhemWorld.Step(sim);
        Assert.Contains(sim.LastTickHits, h => h.TargetEntityId == (ulong)holder + 1);
        Assert.Equal(holder, MayhemWorld.View(sim).Scene.Carries[0].HolderSeat);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABumpPassesWhoeverWalkedIntoWhom(bool holderMoves)
    {
        var s = Setup(MayhemChannels.HotPotato, count: 2); var c = HotPotatoChannel.Start(s);
        int holder = c.Observe().Carries[0].HolderSeat;
        (float, float)[] Points(float gap) => holder == 0 == holderMoves ? new[] { (5 - gap, 0f), (5f, 0f) } : new[] { (5f, 0f), (5 - gap, 0f) };
        Tick(c, s, Frame(100, Points(5)));
        Tick(c, s, Frame(101, Points(0)));
        Assert.Equal(1 - holder, c.Observe().Carries[0].HolderSeat);
    }

    // Problem 8: a Duel's slowest drawer is the slowest of everyone who drew.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADuelsSlowestDrawerLeavingCancelsNobodyElse(bool withdraw)
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.QuickDraw, SpeedTier.One, count: 3, roster: Fg, fire: FriendlyFireMode.Off, twist: new(3));
        var plan = MayhemWorld.Plan(sim);
        MayhemWorld.Step(sim);
        uint draw = MayhemWorld.View(sim).Scene.Watches.Single(w => w.Kind == MayhemWatchKind.Draw).StartTick;
        Dictionary<ulong, InputState> At(uint tick)
        {
            var i = new Dictionary<ulong, InputState>();
            if (tick == draw) i[1] = new InputState { ActiveSlot = AbilitySlots.Normal1 };
            if (tick == draw + 4) i[2] = new InputState { ActiveSlot = AbilitySlots.Normal1 };
            if (tick == draw + 8) i[3] = new InputState { ActiveSlot = AbilitySlots.Normal1 };
            return i;
        }
        MayhemDirectorTests.RunTo(sim, draw + 20, At);
        if (withdraw) sim.WithdrawMayhemPlayer(3); else MayhemWorld.Ko(sim, 3);
        MayhemDirectorTests.RunTo(sim, plan.Deadline(e), At);
        Assert.True(Passed(sim, 0, e) && Passed(sim, 1, e), Who(sim));
    }

    [Fact]
    public void ADuelsSlowestStandingDrawerIsCancelledWhenAFasterDrawerLeaves()
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.QuickDraw, SpeedTier.One, count: 3, roster: Fg, fire: FriendlyFireMode.Off, twist: new(3));
        var plan = MayhemWorld.Plan(sim);
        MayhemWorld.Step(sim);
        uint draw = MayhemWorld.View(sim).Scene.Watches.Single(w => w.Kind == MayhemWatchKind.Draw).StartTick;
        Dictionary<ulong, InputState> At(uint tick)
        {
            var i = new Dictionary<ulong, InputState>();
            if (tick == draw) i[1] = new InputState { ActiveSlot = AbilitySlots.Normal1 };
            if (tick == draw + 4) i[2] = new InputState { ActiveSlot = AbilitySlots.Normal1 };
            if (tick == draw + 8) i[3] = new InputState { ActiveSlot = AbilitySlots.Normal1 };
            return i;
        }
        MayhemDirectorTests.RunTo(sim, draw + 20, At);
        sim.WithdrawMayhemPlayer(1);
        MayhemDirectorTests.RunTo(sim, plan.Deadline(e), At);
        Assert.True(Passed(sim, 1, e) && !Passed(sim, 2, e), Who(sim));
    }

    // Problem 9: the Sweep band crosses the whole deck, either way.

    private static HashSet<int>[] SweepColumns(uint seed)
    {
        var s = Setup(MayhemChannels.Glitch, 2, seed: seed); var c = GlitchChannel.Start(s);
        var rounds = new HashSet<int>[GlitchTuning.Rounds];
        float width = (s.Stage.MaxX - s.Stage.MinX) / GlitchTuning.Columns;
        for (int round = 0; round < GlitchTuning.Rounds; round++)
        {
            rounds[round] = c.Observe().Zones.Select(z => (int)((z.Center.X - s.Stage.MinX) / width)).ToHashSet();
            var after = Frame(c.Window(round).End, (0, 0));
            Tick(c, s, after, ChannelMoment.BeforeActions);
        }
        return rounds;
    }

    [Fact]
    public void SweepArmsEveryColumnInBothDirections()
    {
        var opening = new HashSet<bool>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            var rounds = SweepColumns(seed);
            Assert.Equal(Enumerable.Range(0, GlitchTuning.Columns), rounds.SelectMany(r => r).Distinct().OrderBy(x => x));
            Assert.All(rounds, r => Assert.True(r.Count < GlitchTuning.Columns, "A band always leaves a gap."));
            opening.Add(rounds[0].Contains(0));
        }
        Assert.Equal(2, opening.Count);
    }

    [Fact]
    public void NoColumnIsSafeForAnIdleContestantInSweep()
    {
        int safe = 0;
        foreach (var (seed, episode) in SeedsAiring(MayhemChannels.Glitch, new(2), 12))
        foreach (float x in new[] { -9f, 9f })
        {
            var sim = MayhemWorld.NewShow(seed, FriendlyFireMode.Off, 2); MayhemWorld.SkipToCall(sim, episode);
            var plan = MayhemWorld.Plan(sim);
            MayhemDirectorTests.RunTo(sim, plan.PlayStart(episode) + 1);
            MayhemDirectorTests.Place(sim, 1, x, 0); MayhemDirectorTests.Place(sim, 2, x, 4);
            MayhemDirectorTests.RunTo(sim, plan.Deadline(episode));
            if (MayhemWorld.View(sim).Contestants.All(c => (c.PassMask >> episode & 1) != 0)) safe++;
        }
        Assert.Equal(0, safe);
    }

    // Problem 10: a decoy toasts everyone who hits it.

    [Fact]
    public void EveryContestantWhoBonksADecoyIsToasted()
    {
        var s = Setup(MayhemChannels.Bonk, 2, 2); var c = BonkChannel.Start(s);
        var f = Frame(100, (0, 0), (1, 0));
        var h = Hazards(s, 100);
        c.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, 100, 100, s.Deadline, f), h);
        var decoy = c.Observe().Targets.Single(t => t.Kind == MayhemTargetKind.DecoyHead);
        c.Contact(new(MayhemContactKind.SolidHit, 0, 0, 2, decoy.EntityId, -1, 7, AttackSlot: AbilitySlots.Normal1), h);
        c.Contact(new(MayhemContactKind.SolidHit, 0, 0, 1, decoy.EntityId, -1, 8, AttackSlot: AbilitySlots.Normal1), h);
        c.Tick(new(ChannelMoment.AfterCombat, MayhemPhase.Play, 100, 100, s.Deadline, f), h);
        Assert.All(f.Fighters, x => Assert.Equal(JudgementKind.FailNow, c.Judge(new(x, f, false)).Kind));
    }

    [Theory]
    [InlineData(FriendlyFireMode.Off)]
    [InlineData(FriendlyFireMode.Shoves)]
    public void TwoRealHitsOnADecoyToastBothContestants(FriendlyFireMode fire)
    {
        var (sim, e) = MayhemWorld.Airing(MayhemChannels.Bonk, SpeedTier.One, count: 3, fire: fire, twist: new(2));
        MayhemDirectorTests.RunTo(sim, MayhemWorld.Plan(sim).PlayStart(e) + 1);
        var decoy = sim.MayhemView!.Scene.Targets.Single(t => t.Kind == MayhemTargetKind.DecoyHead);
        Hit(sim, decoy, 1, 1001); Hit(sim, decoy, 2, 1002);
        MayhemWorld.Step(sim);
        Assert.All(sim.MayhemView.Contestants.Take(2), c => Assert.Equal(FailCause.Hazard, c.Failure?.Cause));
        Assert.Null(sim.MayhemView.Contestants[2].Failure);
    }

    [Fact]
    public void AGenuineHeadStillGoesToOneSeat()
    {
        var s = Setup(MayhemChannels.Bonk, 1, 2); var c = BonkChannel.Start(s);
        var f = Frame(100, (0, 0), (1, 0));
        var h = Hazards(s, 100);
        c.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, 100, 100, s.Deadline, f), h);
        var head = Assert.Single(c.Observe().Targets);
        c.Contact(new(MayhemContactKind.SolidHit, 0, 0, 2, head.EntityId, -1, 7, AttackSlot: AbilitySlots.Normal1), h);
        c.Contact(new(MayhemContactKind.SolidHit, 0, 0, 1, head.EntityId, -1, 8, AttackSlot: AbilitySlots.Normal1), h);
        c.Tick(new(ChannelMoment.AfterCombat, MayhemPhase.Play, 100, 100, s.Deadline, f), h);
        Assert.Equal(JudgementKind.Ready, c.Judge(new(f.Fighters[0], f, false)).Kind);
        Assert.Equal(JudgementKind.Incomplete, c.Judge(new(f.Fighters[1], f, false)).Kind);
    }

    // Problem 11: Whack heads pop anywhere on the deck, within a wave's reach.

    private static IReadOnlyList<TargetCue> WhackHeads(uint seed, MayhemFrame frame, uint playTicks = 480)
    {
        var s = Setup(MayhemChannels.Bonk, 1, frame.Fighters.Length, seed) with { Deadline = 100 + playTicks };
        var c = BonkChannel.Start(s);
        c.Tick(new(ChannelMoment.BeforeActions, MayhemPhase.Play, 100, 100, s.Deadline, frame), Hazards(s, 100));
        return c.Observe().Targets;
    }

    [Fact]
    public void WhackHeadsSpreadAcrossTheDeckFromTheSeed()
    {
        var frame = Frame(100, (-6, -5), (6, -5), (-6, 5), (6, 5));
        var stage = frame.Stage;
        var quadrants = new HashSet<(bool, bool)>();
        var distances = new List<float>();
        for (uint seed = 1; seed <= 400; seed++)
        {
            var heads = WhackHeads(seed, frame);
            Assert.Equal(heads, WhackHeads(seed, frame));
            foreach (var head in heads)
            {
                Assert.True(MayhemMath.OnDeck(stage, head.Center.X, head.Center.Z, BonkTuning.WhackMargin), $"{head.Center}");
                quadrants.Add((head.Center.X > 0, head.Center.Z > 0));
                distances.Add(MathF.Sqrt(head.Center.X * head.Center.X + head.Center.Z * head.Center.Z));
            }
            for (int a = 0; a < heads.Count; a++)
            for (int b = a + 1; b < heads.Count; b++)
                Assert.True(MayhemMath.DistanceSquared(heads[a].Center.X, heads[a].Center.Z, heads[b].Center.X, heads[b].Center.Z)
                    >= BonkTuning.WhackGap * BonkTuning.WhackGap, $"seed {seed}");
        }
        Assert.Equal(4, quadrants.Count);
        Assert.True(distances.Min() < 2f, $"nearest {distances.Min()}");
        Assert.True(distances.Max() > 9f, $"farthest {distances.Max()}");
        Assert.True(distances.Select(d => MathF.Round(d, 1)).Distinct().Count() > 40);
    }

    [Theory]
    [InlineData(480u)]
    [InlineData(364u)]
    public void EverySeekerCanRunToEveryWhackHeadInsideAWave(uint playTicks)
    {
        var frame = Frame(100, (-11, -9), (-11, 9), (0, 0));
        float run = TestHelpers.EngineDef.Movement.RunSpeed;
        float reach = run * (playTicks / BonkTuning.Waves - BonkTuning.DuckTicks - BonkTuning.WhackSwingTicks) / 60f;
        for (uint seed = 1; seed <= 200; seed++)
        foreach (var head in WhackHeads(seed, frame, playTicks))
        foreach (var fighter in frame.Fighters)
            Assert.True(MayhemMath.DistanceSquared(head.Center.X, head.Center.Z, fighter.State.PX, fighter.State.PZ) <= reach * reach,
                $"seed {seed}: seat {fighter.Seat} is too far from {head.Center}, reach {reach}");
    }

    [Fact]
    public void DecoyHeadsKeepTheirRing()
    {
        var s = Setup(MayhemChannels.Bonk, 2, 3); var c = BonkChannel.Start(s);
        var f = Frame(100, (-6, 0), (0, 0), (6, 0));
        Tick(c, s, f, ChannelMoment.BeforeActions);
        Assert.All(c.Observe().Targets, t => Assert.Equal(BonkTuning.Spread, MathF.Sqrt(t.Center.X * t.Center.X + t.Center.Z * t.Center.Z), 2));
    }
}
