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
            byte slot = policy.Decide(self, target, def, new Random(seed), memory, baked).ActiveSlot;
            if (slot != 0)
                presses[slot] = presses.GetValueOrDefault(slot) + 1;
        }
        return presses;
    }

    [Fact]
    public void AMultiHitGroupScoresLikeOneHit()
    {
        var single = Kit((AbilitySlots.Slot1, new[] { Strike(8f) }), (AbilitySlots.Slot3, new[] { Strike(14f, group: 1) }));
        var sliced = Kit((AbilitySlots.Slot1, new[] { Strike(8f) }),
            (AbilitySlots.Slot3, Enumerable.Range(0, 10).Select(i => Strike(14f, group: 1, tick: (ushort)(3 + i))).ToArray()));
        var policy = new HeuristicBotPolicy();
        var self = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(single) };
        var target = TestHelpers.NpcState(0f, 0.8f) with { PY = TestHelpers.GroundPY(single) };
        for (int seed = 0; seed < 200; seed++)
        {
            var a = new BotMemory();
            var b = new BotMemory();
            for (int tick = 0; tick <= 18; tick++)
            {
                a.ObserveOpponent(target);
                b.ObserveOpponent(target);
            }
            Assert.Equal(policy.Decide(self, target, single, new Random(seed), a).ActiveSlot,
                policy.Decide(self, target, sliced, new Random(seed), b).ActiveSlot);
        }
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
        var def = Kit((AbilitySlots.Slot1, new[] { Strike(8f) }), (AbilitySlots.Slot3, new[] { Strike(8f) }),
            (AbilitySlots.Slot4, new[] { Strike(8f) }));
        var presses = Presses(def, 0.8f, 1200);
        int total = presses.Values.Sum();
        foreach (byte slot in new[] { AbilitySlots.Slot1, AbilitySlots.Slot3, AbilitySlots.Slot4 })
        {
            float share = presses.GetValueOrDefault(slot) / (float)total;
            Assert.True(MathF.Abs(share - 1f / 3f) <= 0.05f, $"slot {slot} share {share:P1} of {total}");
        }
    }

    [Theory]
    [InlineData(CpuDifficulty.Normal)]
    [InlineData(CpuDifficulty.Hard)]
    public void ASpecialInReachTakesItsShareBesideANormal(CpuDifficulty difficulty)
    {
        var def = Kit((AbilitySlots.Slot1, new[] { Strike(8f) }), (AbilitySlots.A, new[] { Strike(8f) }));
        var presses = Presses(def, 0.8f, 4000, difficulty);
        int total = presses.Values.Sum();
        float share = presses.GetValueOrDefault(AbilitySlots.A) / (float)total;
        float expected = BotDifficultyProfile.ForDifficulty(difficulty).SpecialChance;
        Assert.True(MathF.Abs(share - expected) <= 0.03f, $"special share {share:P1} of {total}, expected {expected:P0}");
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
        var def = Kit((AbilitySlots.Slot1, new[] { Strike(8f) }), (AbilitySlots.R, new[] { Strike(10f, reach: 6f) }));
        var presses = Presses(def, 4f, 400);
        int total = presses.Values.Sum();
        Assert.Equal(0, presses.GetValueOrDefault(AbilitySlots.Slot1));
        float share = presses.GetValueOrDefault(AbilitySlots.R) / 400f;
        float attackChance = BotDifficultyProfile.ForDifficulty(CpuDifficulty.Normal).AttackChance;
        Assert.True(MathF.Abs(share - attackChance) <= 0.08f, $"special pressed on {share:P1} of decisions");

        var self = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) };
        var target = TestHelpers.NpcState(0f, 4f) with { PY = TestHelpers.GroundPY(def) };
        var memory = new BotMemory();
        for (int tick = 0; tick <= 18; tick++)
            memory.ObserveOpponent(target);
        var policy = new HeuristicBotPolicy();
        var rng = new Random(5);
        int approaching = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            var input = policy.Decide(self, target, def, rng, memory);
            if (input.ActiveSlot == 0 && input.MoveY > 0.9f)
                approaching++;
        }
        Assert.True(approaching >= 40, $"approached on {approaching} of 60 ticks");
    }
}
