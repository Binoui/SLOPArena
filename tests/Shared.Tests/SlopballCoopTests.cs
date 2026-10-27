using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SlopArena.Shared.Tests.SlopballTests;

namespace SlopArena.Shared.Tests;

/// <summary>Online co-op Slopball against the Launcher (ADR-0040): several fighters share one run.</summary>
public class SlopballCoopTests
{
    [Fact]
    public void CoopRunAdmitsSeveralFightersAndASoloChallengeStillAdmitsOne()
    {
        var def = TestHelpers.EngineDef;
        var coop = new ServerSimulation(Court(), new SlopballCoopMatchRule());
        for (ulong id = 1; id <= 4; id++)
            coop.RegisterEntity(id, def, TestHelpers.PlayerState() with { PZ = -3 });
        Assert.Equal(4, coop.GetAllStates().Count);

        var solo = new ServerSimulation(Court(), NoWinMatchRule.Instance);
        solo.RegisterEntity(1, def, TestHelpers.PlayerState() with { PZ = -3 });
        Assert.Throws<ArgumentException>(() => solo.RegisterEntity(2, def, TestHelpers.PlayerState()));
    }

    [Fact]
    public void CoopRuleNeedsASlopballCourt()
    {
        Assert.Throws<ArgumentException>(() => new ServerSimulation(TestHelpers.TestArena(), new SlopballCoopMatchRule()));
        var pvp = new ServerSimulation(TestHelpers.TestArena(), new StockMatchRule(3));
        Assert.Throws<InvalidOperationException>(() => pvp.BeginSlopballRun(7));
    }

    [Fact]
    public void RunServesAfterTheNormalGapWithoutACountdownOrInputRelease()
    {
        var arena = Court();
        var sim = new ServerSimulation(arena, new SlopballCoopMatchRule());
        sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PZ = -3 });
        sim.RegisterEntity(2, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = 2, PZ = -3 });
        sim.BeginSlopballRun(99);
        Assert.Equal(SlopballPhase.WaitingServe, sim.Slopball!.Value.Phase);
        Assert.Equal(SlopballCoopMatchRule.DefaultMissBudget, sim.Slopball.Value.MissBudget);
        Assert.Equal(99u, sim.Slopball.Value.SessionSeed);

        // Held movement acts at once: the GameHost countdown already gated input, so no release is required.
        float x = sim.GetState(1).PX;
        int ticks = 0;
        for (; ticks < 10; ticks++) sim.Tick(new() { [1] = new InputState { MoveX = 1 } });
        Assert.True(sim.GetState(1).PX > x);
        while (!sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Served) && ticks < 1000)
        {
            Assert.Equal(SlopballPhase.WaitingServe, sim.Slopball.Value.Phase);
            sim.Tick(new()); ticks++;
        }
        Assert.Equal(arena.Arcade!.Slopball!.ServeGapTicks, ticks);
        Assert.Equal(SlopballPhase.Live, sim.Slopball.Value.Phase);
        Assert.Throws<InvalidOperationException>(() => sim.BeginSlopballRun(99));
    }

    [Fact]
    public void DefaultRunKeepsServingPastThreeMissesUntilExplicitlyStopped()
    {
        var sim = new ServerSimulation(Court(), MatchFormat.SlopballCoop.CreateRule(3));
        sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = 5, PZ = -5 });
        sim.RegisterEntity(2, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = -5, PZ = -5 });
        sim.BeginSlopballRun(3);
        int misses = 0, serves = 0;
        for (int i = 0; i < 3000 && serves < 7; i++)
        {
            sim.Tick(new());
            misses += sim.LastTickSlopballEvents.Count(e => e.Kind == SlopballEventKind.Missed);
            serves += sim.LastTickSlopballEvents.Count(e => e.Kind == SlopballEventKind.Served);
            Assert.False(sim.Slopball!.Value.Ended);
            Assert.False(sim.EvaluateMatch().IsEnded);
            Assert.DoesNotContain(sim.LastTickSlopballEvents, e => e.Kind == SlopballEventKind.Stopped);
        }
        Assert.Equal(7, serves);
        Assert.Equal(6, misses);
        Assert.Equal(0, sim.Slopball!.Value.MissBudget);
        Assert.True(sim.ArcadeBall!.Value.Active);

        sim.StopSlopball();
        Assert.True(sim.Slopball.Value.Ended);
        Assert.False(sim.ArcadeBall.Value.Active);
        Assert.True(sim.EvaluateMatch().IsEnded);
    }

    [Fact]
    public void MissesSpendTheBudgetThenTheRunStopsAndTheMatchEnds()
    {
        var sim = new ServerSimulation(Court(), new SlopballCoopMatchRule(2));
        sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = 5, PZ = -5 });
        sim.RegisterEntity(2, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = -5, PZ = -5 });
        sim.BeginSlopballRun(3);
        int misses = 0;
        for (int i = 0; i < 2000 && !sim.Slopball!.Value.Ended; i++)
        {
            sim.Tick(new());
            if (sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Missed))
            {
                misses++;
                Assert.Equal((byte)misses, sim.Slopball.Value.Misses);
                if (misses == 1) Assert.False(sim.EvaluateMatch().IsEnded);
            }
        }
        var run = sim.Slopball!.Value;
        Assert.True(run.Ended);
        Assert.Equal(2, misses);
        Assert.Contains(sim.LastTickSlopballEvents, e => e.Kind == SlopballEventKind.Stopped);
        Assert.False(sim.ArcadeBall!.Value.Active);
        var outcome = sim.EvaluateMatch();
        Assert.True(outcome.IsEnded && outcome.IsSharedVictory);
        Assert.Equal(0UL, outcome.WinnerEntityId);
        Assert.False(new SlopballCoopMatchRule().IsEliminated(sim.GetState(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoLockIgnoresTeammatesAndFollowsEachNewBall(bool friendlyFire)
    {
        var sim = new ServerSimulation(Court(), new SlopballCoopMatchRule(friendlyFire: friendlyFire ? FriendlyFireMode.On : FriendlyFireMode.Off));
        sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = 5, PZ = -5 });
        sim.RegisterEntity(2, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = 4, PZ = -5 });
        sim.BeginSlopballRun(3);
        int serves = 0;
        ulong lastBall = 0;
        for (int tick = 0; tick < 1500 && serves < 4; tick++)
        {
            // Even a stale client selection cannot select a teammate.
            sim.Tick(new() { [1] = new InputState { TargetEntityId = 2, LockMode = TargetLockMode.OnHit } });
            Assert.NotEqual(2UL, sim.GetState(1).TargetEntityId);
            Assert.NotEqual(1UL, sim.GetState(2).TargetEntityId);
            if (!sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Served)) continue;
            var ball = sim.ArcadeBall!.Value;
            Assert.NotEqual(lastBall, ball.EntityId);
            foreach (ulong id in new ulong[] { 1, 2 })
            {
                Assert.True(sim.GetState(id).LockOn);
                Assert.Equal(ball.EntityId, sim.GetState(id).TargetEntityId);
            }
            lastBall = ball.EntityId;
            serves++;
        }
        Assert.Equal(4, serves);
    }

    [Fact]
    public void ManualUnlockStaysSuppressedAcrossServesAndCanBeReenabled()
    {
        var sim = new ServerSimulation(Court(), new SlopballCoopMatchRule());
        sim.RegisterEntity(1, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = 5, PZ = -5 });
        sim.RegisterEntity(2, TestHelpers.EngineDef, TestHelpers.PlayerState() with { PX = -5, PZ = -5 });
        sim.BeginSlopballRun(3);
        for (int tick = 0; tick < Court().Arcade!.Slopball!.ServeGapTicks; tick++) sim.Tick(new());
        Assert.True(sim.GetState(1).LockOn);
        sim.Tick(new() { [1] = new InputState { ToggleLock = true } });
        bool servedAgain = false;
        for (int tick = 0; tick < 400; tick++)
        {
            sim.Tick(new());
            Assert.False(sim.GetState(1).LockOn);
            Assert.True(sim.GetState(1).AutoLockSuppressed);
            if (sim.LastTickSlopballEvents.Any(e => e.Kind == SlopballEventKind.Served))
            { servedAgain = true; break; }
        }
        Assert.True(servedAgain);
        sim.Tick(new() { [1] = new InputState { ToggleLock = true } });
        Assert.True(sim.GetState(1).LockOn);
        Assert.Equal(sim.ArcadeBall!.Value.EntityId, sim.GetState(1).TargetEntityId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AttacksPassThroughTeammatesWithoutDamageHitstopOrShieldContact(bool projectile, bool shield)
    {
        var sim = new ServerSimulation(Court(), new SlopballCoopMatchRule());
        var def = TestHelpers.EngineDef;
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(-3, -3) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(0, -3) with { PY = TestHelpers.GroundPY(def) });
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, X = 0, Y = TestHelpers.GroundPY(def), Z = -3, VX = projectile ? 6 : 0,
            Radius = .5f, Shape = HitboxShape.Sphere, Damage = 10, BaseKnockback = 10,
            StunTicks = 20, DurationTicks = 10, FreezesOwner = !projectile,
        });
        sim.Tick(new() { [2] = new InputState { ShieldHeld = shield } });
        Assert.Empty(sim.LastTickHits);
        var teammate = sim.GetState(2);
        Assert.Equal(0f, teammate.DamagePercent);
        Assert.Equal(0, teammate.HitstunTicks);
        Assert.Equal(0, teammate.HitstopTicks);
        Assert.Equal(0, teammate.BlockStunTicks);
        Assert.Equal(0, sim.GetState(1).HitstopTicks);
        Assert.Single(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void EnabledFriendlyFireAllowsTeammateDamageWithoutAutoTargetingThem()
    {
        var sim = new ServerSimulation(Court(), new SlopballCoopMatchRule(friendlyFire: FriendlyFireMode.On));
        var def = TestHelpers.EngineDef;
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(-3, -3) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(0, -3) with { PY = TestHelpers.GroundPY(def) });
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, X = 0, Y = TestHelpers.GroundPY(def), Z = -3, VX = 6,
            Radius = .5f, Shape = HitboxShape.Sphere, Damage = 10, BaseKnockback = 10,
            StunTicks = 20, DurationTicks = 10,
        });
        sim.Tick(new() { [1] = new InputState { LockMode = TargetLockMode.OnHit },
            [2] = new InputState { LockMode = TargetLockMode.OnHit } });
        Assert.Equal(2UL, Assert.Single(sim.LastTickHits).TargetEntityId);
        Assert.True(sim.GetState(2).DamagePercent > 0);
        Assert.True(sim.GetState(2).HitstopTicks > 0);
        Assert.Equal(0UL, sim.GetState(1).TargetEntityId);
        Assert.Equal(0UL, sim.GetState(2).TargetEntityId);
    }

    [Fact]
    public void IgnoringTeammatesStillAllowsTheSameAttackToHitTheBall()
    {
        var sim = new ServerSimulation(Court(), new SlopballCoopMatchRule());
        var def = TestHelpers.EngineDef;
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(5, -5));
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(-5, -5));
        sim.BeginSlopballRun(3);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.Tick(new());
            if (sim.ArcadeBall is { Active: true } active && active.Position.Z < -1.5f) break;
        }
        var ball = sim.ArcadeBall!.Value;
        Assert.True(ball.Active);
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(ball.Position.X - .5f, ball.Position.Z)
            with { PY = TestHelpers.GroundPY(def) });
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = 1, X = ball.Position.X, Y = ball.Position.Y, Z = ball.Position.Z,
            Radius = 2f, Shape = HitboxShape.Sphere, Damage = 5, BaseKnockback = 4, DurationTicks = 10,
        });
        sim.Tick(new());
        var hit = Assert.Single(sim.LastTickHits);
        Assert.Equal(ball.EntityId, hit.TargetEntityId);
        Assert.True(sim.ArcadeBall.Value.DamagePercent > 0);
        Assert.Equal(0f, sim.GetState(2).DamagePercent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrabsCaptureTeammatesOnlyWithFriendlyFireEnabled(bool friendlyFire)
    {
        var sim = new ServerSimulation(Court(), new SlopballCoopMatchRule(friendlyFire: friendlyFire ? FriendlyFireMode.On : FriendlyFireMode.Off));
        var def = TestHelpers.EngineDef;
        def = def with { CaptureGeometry = new CookedCaptureGeometry(1.5f, .8f, 1.2f, .6f,
            new CaptureAnchor(0, .6f, .2f), new CaptureAnchor(0, .6f, .65f)) };
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(0, -3) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(0, -2.2f) with { PY = TestHelpers.GroundPY(def) });
        sim.Tick(new() { [1] = new InputState { GrabPressed = true } });
        Assert.Equal(ActionState.GrabAttempt, sim.GetState(1).State);
        for (int tick = 0; tick < DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks; tick++)
        {
            sim.Tick(new());
            if (!friendlyFire)
            {
                Assert.Equal(0UL, sim.GetState(1).InteractionId);
                Assert.Equal(0UL, sim.GetState(2).InteractionId);
                Assert.NotEqual(ActionState.Grabbed, sim.GetState(2).State);
            }
        }
        Assert.Equal(friendlyFire, sim.GetState(2).InteractionId != 0);
    }

    [Fact]
    public void ReturnsAddToTheTeamTotalAcrossCombos()
    {
        var arena = Court(); var ball = new ArcadeBallSimulation(arena.Arcade!.Ball!, arena);
        var rules = new SlopballSimulation(arena.Arcade.Slopball!, arena.Arcade.Ball!, ball);
        rules.BeginRun(ball, 11, 3);
        void Return()
        {
            Serve(rules, ball);
            ReceiveAndArm(arena, ball, rules);
            ball.Reset(new(0, 3, -1), new(0, 0, 120)); Advance(rules, ball, arena);
            ball.Reset(new(0, .35f, 2), new(0, -120, 0)); Advance(rules, ball, arena);
        }
        Return(); Return();
        Serve(rules, ball);
        ball.Reset(new(0, .35f, -2), new(0, -120, 0)); Advance(rules, ball, arena);
        Return();
        var state = rules.State;
        Assert.Equal(3UL, state.TotalReturns);
        Assert.Equal(1UL, state.Returns);
        Assert.Equal(2UL, state.BestStreak);
        Assert.Equal((byte)1, state.Misses);
        Assert.False(state.Ended);
    }

    [Fact]
    public void CoopStartsSpreadSidewaysAroundTheCourtStart()
    {
        var arena = Court();
        var start = arena.SpawnPoints[0];
        var lone = MatchFormat.SlopballCoop.StartFor(arena, 0, 1);
        Assert.Equal(start.X, lone.X);
        var four = Enumerable.Range(0, 4).Select(i => MatchFormat.SlopballCoop.StartFor(arena, i, 4)).ToArray();
        Assert.Equal(start.X, four.Average(p => p.X), 4);
        Assert.All(four, p => Assert.Equal((start.Y, start.Z, start.Yaw), (p.Y, p.Z, p.Yaw)));
        for (int i = 1; i < 4; i++)
            Assert.Equal(MatchFormats.SlopballCoopStartSpacing, four[i].X - four[i - 1].X, 4);
        var pvp = TestHelpers.TestArena();
        Assert.Equal(pvp.SpawnPoints[1 % pvp.SpawnPoints.Length].X, MatchFormat.Stock.StartFor(pvp, 1, 2).X);
    }

    [Fact]
    public void WireNamesRoundTripAndAMissingFormatIsAStockFight()
    {
        foreach (MatchFormat format in Enum.GetValues(typeof(MatchFormat)))
        {
            Assert.True(MatchFormats.TryParseWire(format.ToWire(), out var parsed));
            Assert.Equal(format, parsed);
        }
        Assert.True(MatchFormats.TryParseWire(null, out var missing));
        Assert.Equal(MatchFormat.Stock, missing);
        Assert.False(MatchFormats.TryParseWire("volleyball", out _));
        Assert.True(MatchFormat.SlopballCoop.Admits(Court()));
        Assert.False(MatchFormat.SlopballCoop.Admits(TestHelpers.TestArena()));
        Assert.False(MatchFormat.Stock.Admits(Court()));
        Assert.IsType<SlopballCoopMatchRule>(MatchFormat.SlopballCoop.CreateRule(3));
        Assert.Equal(FriendlyFireMode.On, Assert.IsType<SlopballCoopMatchRule>(MatchFormat.SlopballCoop.CreateRule(3, friendlyFire: FriendlyFireMode.On)).FriendlyFire);
        Assert.Equal((byte)5, Assert.IsType<StockMatchRule>(MatchFormat.Stock.CreateRule(5)).MaxStocks);
    }
}
