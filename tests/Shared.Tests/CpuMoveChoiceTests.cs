using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Which move a CPU presses: damage counts per connect, each normal is listed once, specials share the pool
/// at the difficulty's special chance, and a reaching special fires on the way in. Each case sweeps seeds over
/// one fresh decision against an idle target and counts the presses.
/// </summary>
public sealed class CpuMoveChoiceTests
{
    private static HitboxEvent Strike(float damage, float reach = 1f, byte group = 0, ushort tick = 3)
        => new()
        {
            TriggerTick = tick, DurationTicks = 1, Shape = HitboxShape.Sphere, Radius = 0.4f,
            OffY = 0.5f, OffZ = reach - 0.4f, Damage = damage, HitGroup = group, StunTicks = 20,
            Knockback = new KnockbackData { BaseKnockback = 4f, KnockbackGrowth = 1f },
        };

    private static CharacterDefinition Kit(params (byte Slot, HitboxEvent[] Hits)[] moves)
    {
        var def = TestHelpers.EngineDef;
        foreach (var (slot, hits) in moves)
            def = TestHelpers.WithMove(def, slot, false, "Fixture " + slot, 30, hits: hits);
        return def;
    }

    private static Dictionary<byte, int> Presses(CharacterDefinition def, float distance, int seeds,
        CpuDifficulty difficulty = CpuDifficulty.Normal, BakedAnimationData? baked = null)
    {
        var self = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) };
        var target = TestHelpers.NpcState(0f, distance) with { PY = TestHelpers.GroundPY(def) };
        var policy = new HeuristicBotPolicy();
        var presses = new Dictionary<byte, int>();
        for (int seed = 0; seed < seeds; seed++)
        {
            var memory = new BotMemory { Difficulty = difficulty };
            for (int tick = 0; tick <= BotDifficultyProfile.ForDifficulty(difficulty).ReactionDelayTicks; tick++)
                memory.ObserveOpponent(target);
            var rng = new Random(seed);
            byte slot = 0;
            for (int tick = 0; tick <= BotDifficultyProfile.ForDifficulty(difficulty).HesitationMax + 1 && slot == 0; tick++)
                slot = policy.Decide(self, target, def, rng, memory, baked).ActiveSlot;
            if (slot != 0)
                presses[slot] = presses.GetValueOrDefault(slot) + 1;
        }
        return presses;
    }

    [Fact]
    public void AMultiHitGroupScoresLikeOneHit()
    {
        var single = Kit((AbilitySlots.Normal1, new[] { Strike(8f) }), (AbilitySlots.Normal2, new[] { Strike(14f, group: 1) }));
        var sliced = Kit((AbilitySlots.Normal1, new[] { Strike(8f) }),
            (AbilitySlots.Normal2, Enumerable.Range(0, 10).Select(i => Strike(14f, group: 1, tick: (ushort)(3 + i))).ToArray()));
        var policy = new HeuristicBotPolicy();
        var self = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(single) };
        var target = TestHelpers.NpcState(0f, 0.8f) with { PY = TestHelpers.GroundPY(single) };
        int opportunities = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var a = new BotMemory();
            var b = new BotMemory();
            for (int tick = 0; tick <= 18; tick++)
            {
                a.ObserveOpponent(target);
                b.ObserveOpponent(target);
            }
            var rngA = new Random(seed); var rngB = new Random(seed);
            byte slotA = 0, slotB = 0;
            for (int tick = 0; tick < 90 && (slotA == 0 || slotB == 0); tick++)
            {
                if (slotA == 0) slotA = policy.Decide(self, target, single, rngA, a).ActiveSlot;
                if (slotB == 0) slotB = policy.Decide(self, target, sliced, rngB, b).ActiveSlot;
            }
            Assert.Equal(slotA, slotB);
            if (slotA != 0) opportunities++;
        }
        Assert.True(opportunities > 100, $"only {opportunities} comparable poke opportunities");
    }

    [Fact]
    public void BashieMixesHerNormals()
    {
        var bashie = BuiltInContentResolver.Resolve(CharacterClass.Bashie);
        var presses = Presses(bashie.Definition, 0.6f, 400, baked: bashie.BakedAnimation);
        int total = presses.Values.Sum();
        Assert.True(total > 100, $"only {total} presses");
        foreach (var (slot, count) in presses)
            Assert.True(count <= total * 0.6f, $"slot {slot} took {count} of {total} presses");
    }

    [Fact]
    public void ThreeEqualNormalsAreEachPressedAThirdOfTheTime()
    {
        var def = Kit((AbilitySlots.Normal1, new[] { Strike(8f) }), (AbilitySlots.Normal2, new[] { Strike(8f) }),
            (AbilitySlots.Normal3, new[] { Strike(8f) }));
        var presses = Presses(def, 0.8f, 1200);
        int total = presses.Values.Sum();
        foreach (byte slot in new[] { AbilitySlots.Normal1, AbilitySlots.Normal2, AbilitySlots.Normal3 })
        {
            float share = presses.GetValueOrDefault(slot) / (float)total;
            Assert.True(MathF.Abs(share - 1f / 3f) <= 0.05f, $"slot {slot} share {share:P1} of {total}");
        }
    }

    [Theory]
    [InlineData(CpuDifficulty.Normal)]
    [InlineData(CpuDifficulty.Hard)]
    public void ComparablePokesKeepBothTheNormalAndTheSpecialEligible(CpuDifficulty difficulty)
    {
        var def = Kit((AbilitySlots.Normal1, new[] { Strike(8f) }), (AbilitySlots.A, new[] { Strike(8f) }));
        var presses = Presses(def, 0.8f, 4000, difficulty);
        int total = presses.Values.Sum();
        float share = presses.GetValueOrDefault(AbilitySlots.A) / (float)total;
        Assert.True(total > 500, $"only {total} poke opportunities");
        Assert.InRange(share, 0.15f, 0.85f);
    }

    [Fact]
    public void ALoneSpecialInReachIsUsed()
    {
        var def = Kit((AbilitySlots.A, new[] { Strike(8f) }));
        Assert.True(Presses(def, 0.8f, 64).GetValueOrDefault(AbilitySlots.A) > 0);
    }

    [Fact]
    public void AReachingSpecialFiresOnTheWayInWhileTheCpuKeepsApproaching()
    {
        var def = Kit((AbilitySlots.Normal1, new[] { Strike(8f) }), (AbilitySlots.R, new[] { Strike(10f, reach: 6f) }));
        var presses = Presses(def, 4f, 400);
        int total = presses.Values.Sum();
        Assert.Equal(0, presses.GetValueOrDefault(AbilitySlots.Normal1));
        Assert.True(presses.GetValueOrDefault(AbilitySlots.R) > 0);
        bool exercised = false;
        for (int seed = 0; seed < 32 && !exercised; seed++)
        {
            var sim = TestHelpers.MakeSim();
            sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
            sim.RegisterEntity(100, def, TestHelpers.NpcState(0, 4) with { PY = TestHelpers.GroundPY(def) });
            var policy = new HeuristicBotPolicy(); var memory = new BotMemory(); var rng = new Random(seed);
            int ranged = 0; bool normal = false;
            var outsideByPlan = new Dictionary<uint, int>();
            for (int tick = 0; tick < 180; tick++)
            {
                var input = policy.Decide(sim.GetState(1), sim.GetState(100), def, rng, memory,
                    activeMove: sim.GetActiveMove(1)?.Slot);
                if (input.ActiveSlot == AbilitySlots.R)
                {
                    ranged++;
                    if (sim.GetState(100).PZ - sim.GetState(1).PZ > 1.7f)
                    {
                        uint serial = memory.CombatTrace.Serial;
                        outsideByPlan[serial] = outsideByPlan.GetValueOrDefault(serial) + 1;
                        Assert.Equal(1, outsideByPlan[serial]);
                    }
                }
                if (input.ActiveSlot == AbilitySlots.Normal1) normal = true;
                sim.Tick(new Dictionary<ulong, InputState> { [1] = input, [100] = default });
                if (normal) break;
            }
            if (ranged == 0 || !normal) continue;
            exercised = true;
            Assert.True(sim.GetState(1).PZ > 1f, "walk-in must close after its reaching special");
        }
        Assert.True(exercised);
    }
}
