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
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlopballCoopMatchRule(0));
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
        Assert.Equal((byte)5, Assert.IsType<StockMatchRule>(MatchFormat.Stock.CreateRule(5)).MaxStocks);
    }
}
