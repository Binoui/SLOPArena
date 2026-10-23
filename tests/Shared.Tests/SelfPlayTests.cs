using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;
using SlopArena.Shared.AI;

/// <summary>
/// Issue #148 — self-play match invariants: determinism (same seed → identical match),
/// termination, both sides act, swing accounting is consistent, and no NaN positions.
/// </summary>
public class SelfPlayTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;

    /// <summary>Crossroads-style 60×60 flat proxy (top +20, sides ±40, bottom −10) — same as the tool.</summary>
    private static ArenaDefinition KillArena()
    {
        const int w = 60, h = 60;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "kill-proxy",
            DisplayName = "Kill Proxy",
            KillHeight = -10f,
            MinX = -30f, MaxX = 30f, MinZ = -30f, MaxZ = 30f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            // Origin at -30 so the floor covers [-30,30] — matches the bounds (bots spawn at x=±12).
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = -30f, OriginZ = -30f },
        };
    }

    private static MatchRecord Run(int seed, int maxTicks = 2000,
        CpuDifficulty difficulty = CpuDifficulty.Normal)
        => SelfPlayMatch.Run(Def, KillArena(), seed, TestHelpers.LoadBakedData(Def), maxTicks,
            difficulty: difficulty);
    [Fact]
    public void SameSeed_TerminatesWithIdenticalMatch()
    {
        var a = Run(42, maxTicks: 1500);
        var b = Run(42, maxTicks: 1500);

        Assert.Equal(a.DurationTicks, b.DurationTicks);
        Assert.Equal(a.TimedOut, b.TimedOut);
        Assert.Equal(a.WinnerEntityId, b.WinnerEntityId);
        Assert.Equal(a.Entity1Deaths, b.Entity1Deaths);
        Assert.Equal(a.Entity2Deaths, b.Entity2Deaths);
        Assert.Equal(a.Swings.Count, b.Swings.Count);
        Assert.Equal(a.Hits.Count, b.Hits.Count);
    }

    [Fact]
    public void Run_ReturnsWithoutException_AndIsBounded()
    {
        var rec = Run(7, maxTicks: 500);

        Assert.True(rec.DurationTicks <= 500, $"match ran {rec.DurationTicks} ticks, past the cap");
        Assert.True(rec.TimedOut || rec.WinnerEntityId is SelfPlayMatch.EntityA or SelfPlayMatch.EntityB,
            "match must either time out or declare a winner");
    }
    [Fact]
    public void Run_CompletedMatchDurationIncludesEndingTick()
    {
        var arena = KillArena();
        arena.KillMinX = -1f;
        arena.KillMaxX = 1f;
        var rec = SelfPlayMatch.Run(Def, arena, seed: 3,
            baked: TestHelpers.LoadBakedData(Def), maxTicks: 20, stocks: 1);

        Assert.False(rec.TimedOut);
        Assert.Equal(1, rec.DurationTicks);
    }

    [Fact]
    public void BothSides_Act()
    {
        var rec = Run(42, maxTicks: 4000);

        var attackers = rec.Swings.Select(s => s.Attacker).Distinct().ToHashSet();
        Assert.Contains(SelfPlayMatch.EntityA, attackers);
        Assert.Contains(SelfPlayMatch.EntityB, attackers);
        // At least one swing connected on each side (real fighting, not one-sided whiffing).
        Assert.Contains(SelfPlayMatch.EntityA, rec.Swings.Where(s => s.Connected).Select(s => s.Attacker));
        Assert.Contains(SelfPlayMatch.EntityB, rec.Swings.Where(s => s.Connected).Select(s => s.Attacker));
        Assert.Equal(rec.ActionAttempts.Count, rec.Swings.Count);
        Assert.InRange(rec.AcceptedActions.Count, 1, rec.ActionAttempts.Count);
        Assert.NotEmpty(rec.Hits);
    }


    [Fact]
    public void WhiffSwings_RecordFacingFrameGeometry()
    {
        var rec = Run(42, maxTicks: 4000);

        var whiffs = rec.Swings.Where(s => s.Accepted && !s.Connected).ToList();
        if (whiffs.Count == 0) return; // not guaranteed in a short run; the invariant below is the contract
        foreach (var w in whiffs)
        {
            // Facing-frame forward coordinate must be finite (the whiff spot map consumes it).
            Assert.True(float.IsFinite(w.RelForward), "whiff RelForward must be finite");
            Assert.True(float.IsFinite(w.RelHeight), "whiff RelHeight must be finite");
        }
    }

    [Fact]
    public void NoNaNPoisitions_InSamples()
    {
        var rec = Run(42, maxTicks: 2000);
        foreach (var s in rec.Samples)
        {
            Assert.True(float.IsFinite(s.PX), $"NaN PX at tick {s.Tick}");
            Assert.True(float.IsFinite(s.PY), $"NaN PY at tick {s.Tick}");
            Assert.True(float.IsFinite(s.PZ), $"NaN PZ at tick {s.Tick}");
        }
    }

    [Fact]
    public void SameSeedAndDifficulty_ReproducesCompletePublicTrace()
    {
        var a = Run(42, maxTicks: 1200, difficulty: CpuDifficulty.Normal);
        var b = Run(42, maxTicks: 1200, difficulty: CpuDifficulty.Normal);

        Assert.Equal(a.DurationTicks, b.DurationTicks);
        Assert.Equal(a.TimedOut, b.TimedOut);
        Assert.Equal(a.WinnerEntityId, b.WinnerEntityId);
        Assert.Equal(a.SharedVictory, b.SharedVictory);
        Assert.Equal(a.Entity1Deaths, b.Entity1Deaths);
        Assert.Equal(a.Entity2Deaths, b.Entity2Deaths);
        Assert.Equal(a.Entity1Damage, b.Entity1Damage);
        Assert.Equal(a.Entity2Damage, b.Entity2Damage);
        Assert.Equal(
            a.ActionAttempts.Select(x => (x.EntityId, x.Tick, x.ActiveSlot, x.Air)),
            b.ActionAttempts.Select(x => (x.EntityId, x.Tick, x.ActiveSlot, x.Air)));
        Assert.Equal(
            a.AcceptedActions.Select(x => (x.EntityId, x.Tick, x.ActiveSlot, x.Air)),
            b.AcceptedActions.Select(x => (x.EntityId, x.Tick, x.ActiveSlot, x.Air)));
        Assert.Equal(
            a.Inputs.Select(x => (x.Tick, x.EntityId, Input: x.Input)),
            b.Inputs.Select(x => (x.Tick, x.EntityId, Input: x.Input)));
        Assert.Equal(
            a.Swings.Select(x => (x.Attacker, x.Target, x.ActiveSlot, x.Air, x.StartTick,
                x.WindowTicks, x.Accepted, x.Connected, x.ActivationId, x.RelSide, x.RelForward, x.RelHeight)),
            b.Swings.Select(x => (x.Attacker, x.Target, x.ActiveSlot, x.Air, x.StartTick,
                x.WindowTicks, x.Accepted, x.Connected, x.ActivationId, x.RelSide, x.RelForward, x.RelHeight)));
        Assert.Equal(
            a.Hits.Select(x => (x.Attacker, x.Target, x.AttackSlot, x.ActivationId, x.Air, x.Damage, x.Tick)),
            b.Hits.Select(x => (x.Attacker, x.Target, x.AttackSlot, x.ActivationId, x.Air, x.Damage, x.Tick)));
        Assert.Equal(
            a.Combos.Select(x => (x.Attacker, x.Target, x.Hits, x.StartTick, x.EndTick,
                x.IsTrueCombo, x.IsPressureString)),
            b.Combos.Select(x => (x.Attacker, x.Target, x.Hits, x.StartTick, x.EndTick,
                x.IsTrueCombo, x.IsPressureString)));
        Assert.Equal(
            a.Samples.Select(x => (x.Tick, x.EntityId, x.PX, x.PY, x.PZ)),
            b.Samples.Select(x => (x.Tick, x.EntityId, x.PX, x.PY, x.PZ)));
    }
    [Fact]
    public void Recorder_UninterruptedHitstun_IsTrueCombo()
    {
        var sim = RecorderSimulation();
        var recorder = new MatchRecorder();
        var target = sim.GetState(100);
        target.State = ActionState.Hitstun;
        target.HitstunTicks = 8;
        sim.SetState(100, target);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 0, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();

        target.HitstunTicks = 4;
        sim.SetState(100, target);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 3, new Dictionary<ulong, InputState>(), Def);

        var record = recorder.Finish(4, 1, new MatchOutcome(false, 0, false));
        var combo = Assert.Single(record.Combos);
        Assert.True(combo.IsTrueCombo);
        Assert.False(combo.IsPressureString);
    }

    [Fact]
    public void Recorder_ActionableGap_IsPressureString()
    {
        var sim = RecorderSimulation();
        var recorder = new MatchRecorder();
        var target = sim.GetState(100);
        target.State = ActionState.Hitstun;
        target.HitstunTicks = 1;
        sim.SetState(100, target);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 0, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();

        target.State = ActionState.Idle;
        target.HitstunTicks = 0;
        sim.SetState(100, target);
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [SelfPlayMatch.EntityA] = default,
            [SelfPlayMatch.EntityB] = default,
        });
        recorder.RecordTick(sim, 1, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 2, new Dictionary<ulong, InputState>(), Def);

        var record = recorder.Finish(3, 1, new MatchOutcome(false, 0, false));
        var combo = Assert.Single(record.Combos);
        Assert.False(combo.IsTrueCombo);
        Assert.True(combo.IsPressureString);
    }

    [Fact]
    public void Recorder_NoActionGapBeyondLegacyWindow_RemainsTrueCombo()
    {
        var sim = RecorderSimulation();
        var recorder = new MatchRecorder();
        var target = sim.GetState(100);
        target.State = ActionState.Hitstun;
        target.HitstunTicks = 120;
        sim.SetState(100, target);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 0, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();

        target.HitstunTicks = 1;
        sim.SetState(100, target);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 100, new Dictionary<ulong, InputState>(), Def);

        var record = recorder.Finish(101, 1, new MatchOutcome(false, 0, false));
        var combo = Assert.Single(record.Combos);
        Assert.Equal(2, combo.Hits);
        Assert.True(combo.IsTrueCombo);
        Assert.False(combo.IsPressureString);
    }

    [Fact]
    public void Recorder_InterruptionAndStockBoundary_StartNewExchanges()
    {
        var arena = TestHelpers.TestArena();
        arena.KillMaxX = 10f;
        var sim = RecorderSimulation(arena);
        var recorder = new MatchRecorder();
        var target = sim.GetState(100);
        target.State = ActionState.Hitstun;
        target.HitstunTicks = 8;
        sim.SetState(100, target);

        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 0, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 1, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();

        recorder.RecordInterruption();
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 2, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 3, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();

        target = sim.GetState(100);
        target.PX = 20f;
        sim.SetState(100, target);
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [SelfPlayMatch.EntityA] = default,
            [SelfPlayMatch.EntityB] = default,
        });
        Assert.Single(sim.LastTickDeaths);
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 4, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();
        target = sim.GetState(100);
        target.State = ActionState.Hitstun;
        target.HitstunTicks = 8;
        sim.SetState(100, target);
        sim.Tick(new Dictionary<ulong, InputState>());
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 5, new Dictionary<ulong, InputState>(), Def);
        sim.Tick(new Dictionary<ulong, InputState>());
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 6, new Dictionary<ulong, InputState>(), Def);

        var record = recorder.Finish(7, 1, new MatchOutcome(false, 0, false));
        Assert.Equal(3, record.Combos.Count);
        Assert.Equal(new[] { 2, 2, 2 }, record.Combos.Select(combo => combo.Hits));
        Assert.All(record.Combos, combo => Assert.True(combo.IsTrueCombo));
    }

    [Fact]
    public void Recorder_ReverseHit_InterruptsOpenExchange()
    {
        var sim = RecorderSimulation();
        var recorder = new MatchRecorder();

        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 0, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 1, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();

        sim.LastTickHits.Add(RecorderHit(SelfPlayMatch.EntityB, SelfPlayMatch.EntityA));
        recorder.RecordTick(sim, 2, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 3, new Dictionary<ulong, InputState>(), Def);
        sim.LastTickHits.Clear();
        sim.LastTickHits.Add(RecorderHit());
        recorder.RecordTick(sim, 4, new Dictionary<ulong, InputState>(), Def);

        var record = recorder.Finish(5, 1, new MatchOutcome(false, 0, false));
        Assert.Equal(new[] { 2, 2 }, record.Combos.Select(combo => combo.Hits));
    }

    [Fact]
    public void Recorder_DistinguishesAttemptedAndAcceptedActions()
    {
        var sim = RecorderSimulation();
        var recorder = new MatchRecorder();
        var inputs = new Dictionary<ulong, InputState>
        {
            [SelfPlayMatch.EntityA] = new InputState { ActiveSlot = AbilitySlots.A },
        };
        recorder.RecordPresses(sim, 0, inputs, Def);
        sim.Tick(inputs);
        recorder.RecordTick(sim, 0, inputs, Def);

        var record = recorder.Finish(1, 1, new MatchOutcome(false, 0, false));
        Assert.Single(record.ActionAttempts);
        Assert.Single(record.AcceptedActions);
        Assert.True(record.Swings[0].Accepted);
    }

    [Fact]
    public void Recorder_RecordsRejectedAttemptWithoutAcceptedAction()
    {
        var sim = RecorderSimulation();
        var attacker = sim.GetState(SelfPlayMatch.EntityA);
        attacker.State = ActionState.Hitstun;
        attacker.HitstunTicks = 1;
        sim.SetState(SelfPlayMatch.EntityA, attacker);
        var recorder = new MatchRecorder();
        var inputs = new Dictionary<ulong, InputState>
        {
            [SelfPlayMatch.EntityA] = new InputState { ActiveSlot = AbilitySlots.A },
        };

        recorder.RecordPresses(sim, 0, inputs, Def);
        sim.Tick(inputs);
        recorder.RecordTick(sim, 0, inputs, Def);

        var record = recorder.Finish(1, 1, new MatchOutcome(false, 0, false));
        Assert.Single(record.ActionAttempts);
        Assert.Empty(record.AcceptedActions);
        Assert.False(record.Swings[0].Accepted);
    }

    private static ServerSimulation RecorderSimulation(ArenaDefinition? arena = null)
    {
        var sim = TestHelpers.MakeSim(arena);
        var attacker = TestHelpers.PlayerState();
        attacker.PY = Def.CapsuleHeight * 0.5f;
        var target = TestHelpers.NpcState(z: 1f);
        target.PY = Def.CapsuleHeight * 0.5f;
        TestHelpers.RegisterPlayer(sim, Def, attacker);
        TestHelpers.RegisterNpc(sim, Def, target);
        return sim;
    }

    private static SpellResolver.HitResult RecorderHit(
        ulong owner = SelfPlayMatch.EntityA, ulong target = SelfPlayMatch.EntityB)
        => new()
        {
            OwnerEntityId = owner,
            TargetEntityId = target,
            AttackSlot = AbilitySlots.Slot1,
            Damage = 1f,
            StunTicks = 8,
        };
}
