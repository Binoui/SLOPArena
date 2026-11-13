using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>The director's rules: the deadline, latching, withdrawal, the reset at every Call, splat, falls and contacts.</summary>
public class MayhemDirectorTests
{
    private static readonly string[] FightGuys = { "fightguy" };

    internal static void RunTo(ServerSimulation sim, uint tick, Func<uint, Dictionary<ulong, InputState>>? inputs = null)
    { while (sim.CurrentTick < tick) sim.Tick(inputs?.Invoke(sim.CurrentTick + 1) ?? new()); }

    internal static void Place(ServerSimulation sim, ulong id, float x, float z)
    { var s = sim.GetState(id); s.PX = x; s.PZ = z; s.VX = s.VZ = 0; sim.SetState(id, s); }

    private static List<MayhemEvent> Verdicts(IEnumerable<MayhemEvent> events)
        => events.Where(e => e.Kind is MayhemEventKind.Pass or MayhemEventKind.Fail).ToList();

    [Fact]
    public void TheDeadlineJudgesTheStateAfterTheLastPlayTickAndCommitsEveryVerdictOnce()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2);
        var plan = MayhemWorld.Plan(sim);
        var log = new List<MayhemEvent>();
        while (sim.CurrentTick < plan.Deadline(episode) - 1) { MayhemWorld.Step(sim); log.AddRange(sim.LastTickMayhemEvents); }
        Assert.Equal(MayhemPhase.Play, MayhemWorld.View(sim).Cursor.Phase);
        Assert.Empty(Verdicts(log));
        var spot = MayhemWorld.View(sim).Scene.Zones[0];
        Place(sim, 1, spot.Center.X, spot.Center.Z);
        var outside = sim.GetState(2);
        float toward = MathF.Sign(spot.Center.X - outside.PX);
        sim.Tick(new() { [2] = new InputState { MoveX = toward } });
        Assert.Equal(plan.Deadline(episode), sim.CurrentTick);
        var view = MayhemWorld.View(sim);
        Assert.Equal(MayhemPhase.Verdict, view.Cursor.Phase);
        Assert.Equal(MayhemVerdict.Passed, view.Contestants[0].Verdict);
        Assert.Equal(MayhemVerdict.Failed, view.Contestants[1].Verdict);
        Assert.Equal(FailCause.OutsideSpot, view.Contestants[1].Failure!.Value.Cause);
        var committed = Verdicts(sim.LastTickMayhemEvents);
        Assert.Equal(2, committed.Count);
        Assert.Contains(committed, e => e.Kind == MayhemEventKind.Pass && e.Subject == 1 && e.Value == MayhemShowPlan.Award(episode));
        Assert.Contains(committed, e => e.Kind == MayhemEventKind.Fail && e.Subject == 2);
        Assert.Equal(outside.PX, sim.GetState(2).PX);
        for (int i = 0; i < 20; i++)
        {
            sim.Tick(new() { [1] = new InputState { MoveX = 1 } });
            Assert.Empty(Verdicts(sim.LastTickMayhemEvents));
        }
        Assert.Equal(spot.Center.X, sim.GetState(1).PX, 3);
    }

    [Fact]
    public void TheCallIsFrozenAndPlayStartsOnItsFirstTick()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 2);
        var plan = MayhemWorld.Plan(sim);
        var start = MayhemWorld.NewShow().GetState(1);
        RunTo(sim, plan.PlayStart(episode) - 1, _ => new() { [1] = new InputState { MoveX = 1, Jump = true, JumpHeld = true } });
        Assert.Equal(MayhemControls.Frozen, sim.MayhemShow!.ControlsFor(1, sim.CurrentTick));
        Assert.Equal((start.PX, start.PZ), (sim.GetState(1).PX, sim.GetState(1).PZ));
        Assert.True(sim.GetState(1).IsGrounded);
        Assert.Equal(MayhemControls.Playing(ExtraVerb.None), sim.MayhemControlsFor(1));
        sim.Tick(new() { [1] = new InputState { MoveX = 1 } });
        Assert.Equal(MayhemPhase.Play, MayhemWorld.View(sim).Cursor.Phase);
        Assert.True(sim.GetState(1).PX > start.PX);
    }

    [Fact]
    public void ReadyIsProvisionalAndAFailureLatchesOnceAndIsFinal()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 5);
        var spot = MayhemWorld.View(sim).Scene.Zones[0];
        Place(sim, 1, spot.Center.X, spot.Center.Z);
        MayhemWorld.Step(sim, 3);
        var ready = MayhemWorld.View(sim).Contestants[0];
        Assert.True(ready.ObjectiveMet);
        Assert.Equal(MayhemVerdict.Pending, ready.Verdict);
        Assert.Equal(0u, ready.PassMask);
        var log = new List<MayhemEvent>();
        MayhemWorld.Ko(sim, 1, -5f);
        MayhemWorld.Step(sim); log.AddRange(sim.LastTickMayhemEvents);
        var failure = MayhemWorld.View(sim).Contestants[0].Failure!.Value;
        Assert.Equal(FailCause.Fell, failure.Cause);
        for (int i = 0; i < 30; i++)
        {
            MayhemWorld.Ko(sim, 1, -5f);
            var pinned = sim.GetState(1);
            sim.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId, ActivationId = MayhemHazards.Activation(episode, 0, 9),
                X = pinned.PX, Y = pinned.PY, Z = pinned.PZ, Radius = 2, DurationTicks = 1 });
            MayhemWorld.Step(sim); log.AddRange(sim.LastTickMayhemEvents);
        }
        Assert.Single(log, e => e.Kind == MayhemEventKind.Fail && e.Subject == 1);
        Assert.Equal(failure, MayhemWorld.View(sim).Contestants[0].Failure);
        RunTo(sim, plan.Deadline(episode), _ => new());
        Assert.Equal(0u, MayhemWorld.View(sim).Contestants[0].PassMask);
        Assert.DoesNotContain(sim.LastTickMayhemEvents, e => e.Kind == MayhemEventKind.Pass && e.Subject == 1);
    }

    [Fact]
    public void TheShowNeverEndsEarlyWhenEveryoneHasFailed()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 3);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 1);
        foreach (ulong id in sim.MayhemShow!.Roster) MayhemWorld.Ko(sim, id, -5f);
        MayhemWorld.Step(sim);
        Assert.All(MayhemWorld.View(sim).Contestants, c => Assert.Equal(MayhemVerdict.Failed, c.Verdict));
        RunTo(sim, plan.Deadline(episode) - 1);
        Assert.Equal(MayhemPhase.Play, MayhemWorld.View(sim).Cursor.Phase);
        Assert.False(sim.EvaluateMatch().IsEnded);
        MayhemWorld.Step(sim);
        Assert.Equal(MayhemPhase.Verdict, MayhemWorld.View(sim).Cursor.Phase);
        Assert.Empty(Verdicts(sim.LastTickMayhemEvents));
        RunTo(sim, plan.CallStart(episode + 1));
        Assert.All(sim.MayhemShow.Roster, id => Assert.False(sim.IsEntityInactive(id)));
        Assert.All(MayhemWorld.View(sim).Contestants, c => Assert.Equal(MayhemVerdict.Pending, c.Verdict));
    }

    [Fact]
    public void AWithdrawalMidChannelNeverShiftsTheOnAirSetOrTheSchedule()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One);
        var (control, _) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 2); RunTo(control, plan.PlayStart(episode) + 2);
        sim.WithdrawMayhemPlayer(2);
        RunTo(sim, plan.Deadline(episode) - 1); RunTo(control, plan.Deadline(episode) - 1);
        var bombs = MayhemWorld.View(sim).Scene.Bombs;
        var expected = MayhemWorld.View(control).Scene.Bombs;
        Assert.Equal(expected.Select(b => (b.Ordinal, b.LaunchTick, b.ImpactTick)), bombs.Select(b => (b.Ordinal, b.LaunchTick, b.ImpactTick)));
        var withdrawn = MayhemWorld.View(sim).Contestants[1];
        Assert.True(withdrawn.OnAir); Assert.True(withdrawn.Withdrawn);
        MayhemWorld.Step(sim);
        Assert.DoesNotContain(sim.LastTickMayhemEvents, e => e.Subject == 2);
        Assert.Equal(0u, MayhemWorld.View(sim).Contestants[1].PassMask);

        var (spot, spotEpisode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One);
        MayhemWorld.Step(spot);
        int spots = MayhemWorld.View(spot).Scene.Zones.Count;
        Assert.Equal(3, spots);
        spot.WithdrawMayhemPlayer(3);
        RunTo(spot, MayhemWorld.Plan(spot).Deadline(spotEpisode) - 1);
        Assert.Equal(spots, MayhemWorld.View(spot).Scene.Zones.Count);
    }

    [Fact]
    public void AnEmptyOnAirSetRunsEveryChannelToTheEndWithoutDividingByZero()
    {
        var sim = MayhemWorld.NewShow(2024, count: 2);
        foreach (ulong id in sim.MayhemShow!.Roster) sim.WithdrawMayhemPlayer(id);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.EndTick);
        var view = MayhemWorld.View(sim);
        Assert.Equal(MayhemPhase.Ended, view.Cursor.Phase);
        Assert.All(view.Contestants, c => { Assert.False(c.OnAir); Assert.Equal(0u, c.PassMask); });
        Assert.Equal(new MatchOutcome(true, 0, true), sim.EvaluateMatch());
    }

    [Fact]
    public void EveryCallResetsEachContestantAndClearsHostAndFighterEffects()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 2, roster: FightGuys);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 3);
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Normal1 } });
        Assert.NotNull(sim.GetActiveMove(1));
        var state = sim.GetState(1); state.DamagePercent = 40; state.BufferedSlot = AbilitySlots.Normal2; state.VX = 3; sim.SetState(1, state);
        var other = sim.GetState(2);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 1, X = 0, Y = 1, Z = 0, Radius = .3f, DurationTicks = 600, VX = 0.01f });
        sim.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId, AttachedEntityId = 2, FuseTicksLeft = 500,
            X = other.PX, Y = other.PY, Z = other.PZ, Radius = .3f, DurationTicks = 600, StickyFuseTicks = 500,
            Explosion = new ProjectileExplosion { Radius = 2, DurationTicks = 2 } });
        sim.Resolver.QueueExplosion(0, 0, 0, new ProjectileExplosion { Radius = 2, DurationTicks = 2 },
            ServerSimulation.MayhemHostEntityId, 0, MayhemHazards.Activation(episode, 0, 3), false, 0);
        MayhemWorld.SkipToCall(sim, episode + 1);
        MayhemWorld.Step(sim);
        Assert.Equal(MayhemPhase.Call, MayhemWorld.View(sim).Cursor.Phase);
        var arena = MayhemWorld.Arena();
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            var reset = sim.GetState(id);
            var spawn = MatchFormat.MankiMayhem.StartFor(arena, (int)id - 1, 2);
            Assert.Equal((spawn.X, spawn.Z), (reset.PX, reset.PZ));
            Assert.Equal(0, reset.DamagePercent); Assert.Equal(0, reset.BufferedSlot);
            Assert.Equal((0f, 0f, 0f), (reset.VX, reset.VY, reset.VZ));
            Assert.Null(sim.GetActiveMove(id));
            Assert.Equal(0UL, reset.InteractionId);
            Assert.False(reset.Flight.Active);
        }
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        Assert.Empty(sim.Resolver.DrainPendingExplosions());
    }

    [Fact]
    public void ASplatIsInactivePinnedAndNeverDamagesLaunchesOrExplodesWhatIsAttached()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 2);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 2);
        var victim = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId, AttachedEntityId = 1, FuseTicksLeft = 400,
            StickyFuseTicks = 400, X = victim.PX, Y = victim.PY, Z = victim.PZ, Radius = .3f, DurationTicks = 600,
            Explosion = new ProjectileExplosion { Radius = 3, DurationTicks = 2 } });
        sim.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId, ActivationId = MayhemHazards.Activation(episode, 0, 0),
            X = victim.PX, Y = victim.PY, Z = victim.PZ, Radius = 1.5f, DurationTicks = 2, Damage = 30, BaseKnockback = 40,
            KnockbackGrowth = 40, KnockbackAngle = 60, StunTicks = 30 });
        MayhemWorld.Step(sim);
        var view = MayhemWorld.View(sim);
        var failure = view.Contestants[0].Failure!.Value;
        Assert.Equal(FailCause.Hazard, failure.Cause);
        Assert.Equal(SplatStyle.Toasted, failure.Style);
        var pinned = sim.GetState(1);
        Assert.Equal(0, pinned.DamagePercent);
        Assert.Equal((0f, 0f, 0f), (pinned.KVX, pinned.KVY, pinned.KVZ));
        Assert.Equal((failure.Anchor.X, failure.Anchor.Y, failure.Anchor.Z), (pinned.PX, pinned.PY, pinned.PZ));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.AttachedEntityId == 1);
        Assert.Empty(sim.Resolver.DrainPendingExplosions());
        Assert.True(sim.IsEntityInactive(1));
        for (int i = 0; i < 20; i++)
        {
            sim.Resolver.Spawn(new Hitbox { OwnerId = 2, X = pinned.PX, Y = pinned.PY, Z = pinned.PZ, Radius = 1, DurationTicks = 1, Damage = 5 });
            sim.Tick(new() { [1] = new InputState { MoveX = 1, Jump = true, JumpHeld = true } });
            Assert.DoesNotContain(sim.LastTickHits, h => h.TargetEntityId == 1);
            Assert.DoesNotContain(sim.LastTickAttackEntities, e => e.Id == 1);
        }
        var later = sim.GetState(1);
        Assert.Equal((pinned.PX, pinned.PY, pinned.PZ), (later.PX, later.PY, later.PZ));
        Assert.Equal(0, later.DamagePercent);
    }

    [Fact]
    public void FallsSplatAtTheLastSafePoseWithoutRespawnStockOrDeath()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 2);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 10);
        var safe = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId, AttachedEntityId = 1, FuseTicksLeft = 400,
            StickyFuseTicks = 400, X = safe.PX, Y = safe.PY, Z = safe.PZ, Radius = .3f, DurationTicks = 600,
            Explosion = new ProjectileExplosion { Radius = 3, DurationTicks = 2 } });
        var off = safe; off.PX = 12.6f; off.IsGrounded = false; sim.SetState(1, off);
        var below = sim.GetState(2); below.PY = -1f; below.IsGrounded = false; sim.SetState(2, below);
        MayhemWorld.Step(sim);
        Assert.Empty(sim.LastTickDeaths);
        var view = MayhemWorld.View(sim);
        Assert.All(view.Contestants, c => Assert.Equal(FailCause.Fell, c.Failure!.Value.Cause));
        var anchor = view.Contestants[0].Failure!.Value.Anchor;
        Assert.Equal((safe.PX, safe.PZ), (anchor.X, anchor.Z));
        var pinned = sim.GetState(1);
        Assert.Equal((safe.PX, safe.PZ), (pinned.PX, pinned.PZ));
        Assert.Equal(0, pinned.Deaths); Assert.Equal(0, pinned.InvincibilityTicks);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.AttachedEntityId == 1);
        Assert.Empty(sim.Resolver.DrainPendingExplosions());
        RunTo(sim, plan.Deadline(episode));
        Assert.All(sim.GetAllStates().Values, s => Assert.Equal(0, s.Deaths));
    }

    [Fact]
    public void HostContactsFailThroughInvincibilityBeforeDamageOrKnockback()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Jump, SpeedTier.One, count: 2);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 2);
        var victim = sim.GetState(1); victim.InvincibilityTicks = 200; sim.SetState(1, victim);
        sim.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId, ActivationId = MayhemHazards.Activation(episode, 0, 0),
            X = victim.PX, Y = victim.PY, Z = victim.PZ, Radius = 1, DurationTicks = 2, Damage = 50, BaseKnockback = 80, StunTicks = 40 });
        MayhemWorld.Step(sim);
        var failure = MayhemWorld.View(sim).Contestants[0].Failure!.Value;
        Assert.Equal((FailCause.Hazard, SplatStyle.Flattened), (failure.Cause, failure.Style));
        Assert.Equal(0, sim.GetState(1).DamagePercent);
        Assert.DoesNotContain(sim.LastTickHits, h => h.TargetEntityId == 1);
        sim.Resolver.Spawn(new Hitbox { OwnerId = ServerSimulation.MayhemHostEntityId, ActivationId = MayhemHazards.Activation(episode + 1, 0, 0),
            X = sim.GetState(2).PX, Y = sim.GetState(2).PY, Z = sim.GetState(2).PZ, Radius = 1, DurationTicks = 2 });
        MayhemWorld.Step(sim);
        Assert.Equal(MayhemVerdict.Pending, MayhemWorld.View(sim).Contestants[1].Verdict);
    }

    [Fact]
    public void ADodgeBombThatTouchesAFighterFailsThemWithinTwoTicks()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Dodge, SpeedTier.One, count: 2);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 2);
        var victim = sim.GetState(1);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = ServerSimulation.MayhemHostEntityId, ActivationId = MayhemHazards.Activation(episode, 0, 20),
            X = victim.PX, Y = victim.PY + 1.6f, Z = victim.PZ, VY = -6f, Radius = MayhemHazardTuning.BombRadius,
            DurationTicks = 60, ContactExplosion = true,
            Explosion = new ProjectileExplosion { Radius = DodgeTuning.BlastRadius, DurationTicks = MayhemHazardTuning.BlastTicks },
        });
        uint touched = 0;
        while (touched == 0 && sim.CurrentTick < plan.PlayStart(episode) + 40)
        {
            MayhemWorld.Step(sim);
            if (!sim.Resolver.GetActiveHitboxes().Any(h => h.ContactExplosion && h.ActivationId == MayhemHazards.Activation(episode, 0, 20)))
                touched = sim.CurrentTick;
        }
        Assert.NotEqual(0u, touched);
        Assert.Equal(victim.PX, sim.GetState(1).PX, 2);
        while (sim.CurrentTick < touched + 2 && MayhemWorld.View(sim).Contestants[0].Failure == null) MayhemWorld.Step(sim);
        var failure = MayhemWorld.View(sim).Contestants[0].Failure;
        Assert.NotNull(failure);
        Assert.InRange(failure!.Value.Tick, touched, touched + 2);
    }

    [Theory]
    [InlineData(SpeedTier.One)]
    [InlineData(SpeedTier.Two)]
    [InlineData(SpeedTier.Three)]
    public void EveryLastBlastEndsBeforeTheDeadline(SpeedTier tier)
    {
        foreach (var channel in new[] { MayhemChannels.Dodge, MayhemChannels.BreakOut, MayhemChannels.PrimeTime })
        {
            if (channel == MayhemChannels.PrimeTime && tier != SpeedTier.Three) continue;
            var (sim, episode) = MayhemWorld.Airing(channel, tier);
            MayhemWorld.Step(sim);
            uint deadline = MayhemWorld.Plan(sim).Deadline(episode);
            var bombs = MayhemWorld.View(sim).Scene.Bombs;
            Assert.NotEmpty(bombs);
            Assert.All(bombs, b => Assert.True(b.ImpactTick + MayhemHazardTuning.BlastTicks + 1 < deadline,
                $"{channel} tier {tier}: bomb {b.Lane}/{b.Ordinal} blast ends at {b.ImpactTick + MayhemHazardTuning.BlastTicks + 1}, deadline {deadline}"));
            RunTo(sim, deadline - 1);
            Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == ServerSimulation.MayhemHostEntityId && h.ContactExplosion);
        }
    }

    [Fact]
    public void AShoveIsABoundedHorizontalPushAtZeroDamageAndNeverAFail()
    {
        var (sim, episode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2, roster: FightGuys);
        var plan = MayhemWorld.Plan(sim);
        RunTo(sim, plan.PlayStart(episode) + 2);
        Place(sim, 1, 0, 0); Place(sim, 2, 0, .9f);
        var face = sim.GetState(1); face.FacingYaw = 0; sim.SetState(1, face);
        var shoved = false;
        for (int i = 0; i < 30 && !shoved; i++)
        {
            sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Normal1 } });
            shoved = sim.LastTickHits.Any(h => h.OwnerEntityId == 1 && h.TargetEntityId == 2);
        }
        Assert.True(shoved);
        var hit = sim.GetState(2);
        Assert.Equal(0, hit.DamagePercent);
        Assert.Equal(0f, hit.KVY);
        Assert.True(hit.HitstunTicks > 0);
        MayhemWorld.Step(sim, 60);
        var after = sim.GetState(2);
        float pushed = MathF.Sqrt(after.PX * after.PX + (after.PZ - .9f) * (after.PZ - .9f));
        Assert.InRange(pushed, .05f, 3f);
        Assert.True(after.PY < 1f);
        Assert.Equal(MayhemVerdict.Pending, MayhemWorld.View(sim).Contestants[1].Verdict);
        Assert.Equal(0, sim.GetState(1).DamagePercent);

        var (off, offEpisode) = MayhemWorld.Airing(MayhemChannels.Spot, SpeedTier.One, count: 2, roster: FightGuys, fire: FriendlyFireMode.Off);
        RunTo(off, MayhemWorld.Plan(off).PlayStart(offEpisode) + 2);
        Place(off, 1, 0, 0); Place(off, 2, 0, .9f);
        var offFace = off.GetState(1); offFace.FacingYaw = 0; off.SetState(1, offFace);
        for (int i = 0; i < 30; i++)
        {
            off.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.Normal1 } });
            Assert.DoesNotContain(off.LastTickHits, h => h.TargetEntityId == 2);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new MankiMayhemMatchRule(FriendlyFireMode.On));
        Assert.False(MatchFormat.MankiMayhem.AdmitsFriendlyFire(FriendlyFireMode.On));
        Assert.True(MatchFormat.MankiMayhem.AdmitsFriendlyFire(FriendlyFireMode.Shoves));
        Assert.True(MatchFormat.MankiMayhem.AdmitsFriendlyFire(FriendlyFireMode.Off));
    }
}
