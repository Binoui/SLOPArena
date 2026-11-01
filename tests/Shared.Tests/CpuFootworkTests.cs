using System;
using System.Collections.Generic;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Movement reads as intent: a retreat or a circle lasts a window, and the CPU does not alternate approach and
/// stop at the edge of its reach. The CPU and its target hold still so only the policy's choices vary.
/// </summary>
public sealed class CpuFootworkTests
{
    private enum Stick { Stand, Approach, Retreat, Circle, Action }

    private static ArenaDefinition Floor()
    {
        var arena = TestHelpers.TestArena();
        arena.Heightmap = new ArenaHeightmap
        {
            Data = new float[41 * 41], Width = 41, Height = 41,
            OriginX = -20f, OriginZ = -20f, CellSize = 1f,
        };
        arena.MinX = arena.MinZ = -18f;
        arena.MaxX = arena.MaxZ = 18f;
        return arena;
    }

    private static List<Stick> Trace(CpuDifficulty difficulty, int seed, float distance, int ticks,
        bool specialsCooling = false)
    {
        var def = TestHelpers.FightGuyDef;
        var self = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def), DamagePercent = 90 };
        if (specialsCooling)
            foreach (byte slot in new[] { AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F })
                self.SetCooldown(slot, 999);
        var target = TestHelpers.NpcState(0f, distance) with { PY = TestHelpers.GroundPY(def) };
        var memory = new BotMemory { Difficulty = difficulty };
        var policy = new HeuristicBotPolicy();
        var rng = new Random(seed);
        var arena = Floor();
        var trace = new List<Stick>(ticks);
        for (int tick = 0; tick < ticks; tick++)
        {
            var input = policy.Decide(self, target, def, rng, memory, arena);
            trace.Add(Classify(input));
        }
        return trace;
    }

    private static Stick Classify(in InputState input)
    {
        if (input.ActiveSlot != 0 || input.Jump || input.ShieldHeld || input.GrabPressed)
            return Stick.Action;
        if (input.MoveX == 0f && input.MoveY == 0f)
            return Stick.Stand;
        float toward = input.MoveY / MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY);
        return toward > 0.7f ? Stick.Approach : toward < -0.7f ? Stick.Retreat : Stick.Circle;
    }

    /// <summary>
    /// Every Retreat and Circle lasts at least 18 ticks unless an action ends it. A jump in place does not end
    /// footwork, so a window continues across it. Hard's attack rolls every 4 ticks end most of its windows.
    /// </summary>
    [Theory]
    [InlineData(CpuDifficulty.Normal)]
    [InlineData(CpuDifficulty.Hard)]
    public void RetreatLastsAWindowUnlessAnActionInterruptsIt(CpuDifficulty difficulty)
    {
        int retreats = 0, circles = 0;
        for (int seed = 0; seed < 16; seed++)
        {
            var trace = Trace(difficulty, seed, 1.2f, 600);
            for (int start = 0; start < trace.Count;)
            {
                var kind = trace[start];
                int end = start;
                while (true)
                {
                    while (end < trace.Count && trace[end] == kind)
                        end++;
                    int next = end;
                    while (next < trace.Count && trace[next] == Stick.Action)
                        next++;
                    if (kind is not (Stick.Retreat or Stick.Circle) || next == end
                        || next == trace.Count || trace[next] != kind)
                        break;
                    end = next;
                }
                if (kind is Stick.Retreat or Stick.Circle && end < trace.Count)
                {
                    int length = end - start;
                    Assert.True(length >= 18 || trace[end] == Stick.Action,
                        $"seed {seed}: a {kind} lasted {length} ticks from tick {start}, then {trace[end]}");
                }
                if (kind == Stick.Retreat) retreats++;
                if (kind == Stick.Circle) circles++;
                start = end;
            }
        }
        Assert.True(retreats > 0, $"retreats {retreats}, circles {circles}");
    }

    [Theory]
    [InlineData(CpuDifficulty.Normal)]
    [InlineData(CpuDifficulty.Hard)]
    public void ApproachDoesNotStutterAtTheEdgeOfReach(CpuDifficulty difficulty)
    {
        var def = TestHelpers.FightGuyDef;
        float reach = MathF.Max(HeuristicBotPolicy.ForwardReach(def, AbilitySlots.Normal1, false),
            MathF.Max(HeuristicBotPolicy.ForwardReach(def, AbilitySlots.Normal2, false),
                HeuristicBotPolicy.ForwardReach(def, AbilitySlots.Normal3, false))) + 0.2f;
        for (int seed = 0; seed < 16; seed++)
        {
            // Specials cool down so a special fired on the way in does not count as a resumed approach.
            var trace = Trace(difficulty, seed, reach * 0.96f, 120, specialsCooling: true);
            int resumes = 0;
            for (int tick = 1; tick < trace.Count; tick++)
                if (trace[tick] == Stick.Approach && trace[tick - 1] != Stick.Approach)
                    resumes++;
            Assert.True(resumes <= 1, $"seed {seed}: approach resumed {resumes} times in 120 ticks");
        }
    }
}
