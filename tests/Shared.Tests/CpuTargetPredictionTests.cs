using System;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CpuTargetPredictionTests
{
    private static CharacterDefinition Kit(ushort startup = 12, float reach = 4f)
        => TestHelpers.WithMove(TestHelpers.EngineDef, AbilitySlots.Normal1, false, "Prediction strike", 45,
            hits: new[] { new HitboxEvent { TriggerTick = startup, DurationTicks = 1,
                Shape = HitboxShape.Sphere, Radius = 0.4f, OffZ = reach - 0.4f, Damage = 8f } });

    private static InputState Decide(HeuristicBotPolicy policy, CharacterDefinition def, CharacterState target,
        CpuDifficulty difficulty, int seed, float newestX = 0f)
    {
        var memory = new BotMemory { Difficulty = difficulty };
        int delay = BotDifficultyProfile.ForDifficulty(difficulty).ReactionDelayTicks;
        for (int tick = 0; tick <= delay; tick++)
            memory.ObserveOpponent(tick == delay ? target with { PX = newestX } : target);
        var rng = new Random(seed);
        InputState input = default;
        for (int tick = 0; tick <= BotDifficultyProfile.ForDifficulty(difficulty).HesitationMax + 1; tick++)
        {
            input = policy.Decide(TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) }, def, rng, memory);
            if (input.ActiveSlot != 0) break;
            policy.ObserveOpponent(memory, target, def);
        }
        return input;
    }

    [Theory]
    [InlineData(CpuDifficulty.Easy)]
    [InlineData(CpuDifficulty.Normal)]
    [InlineData(CpuDifficulty.Hard)]
    public void AimLeadsTheDelayedVelocityWithoutReadingNewInformation(CpuDifficulty difficulty)
    {
        var def = Kit();
        var target = TestHelpers.NpcState(0f, 1f) with { PY = TestHelpers.GroundPY(def), VX = 3f };
        var policy = new HeuristicBotPolicy();
        int attacks = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            var input = Decide(policy, def, target, difficulty, seed, newestX: 8f);
            var unseen = Decide(policy, def, target, difficulty, seed, newestX: -8f);
            Assert.Equal(input, unseen);
            if (input.ActiveSlot != AbilitySlots.Normal1) continue;
            attacks++;
            Assert.True(input.AimYaw > 1000, $"stale yaw was aimed at again: {input.AimYaw}");
            Assert.True(input.AimDistance > 100, "distance must include the lead along X");
        }
        Assert.True(attacks > 5, $"only {attacks} attacks");
        var observation = CpuObservation.Capture(0, target, 0f).WithAge(100);
        var capped = CpuTargetPrediction.AtActivation(default, observation, default, new CpuMoveTiming(100));
        Assert.Equal(1.8f, capped.X, 4);
        var fast = CpuObservation.Capture(0, target with { VX = 100f }, 0f).WithAge(18);
        Assert.Equal(3f, CpuTargetPrediction.AtActivation(default, fast, default, new CpuMoveTiming(12)).X, 4);
        var edge = new ArenaDefinition { MinX = -1f, MaxX = 1f, MinZ = -2f, MaxZ = 2f };
        Assert.InRange(CpuTargetPrediction.AtActivation(default, fast, edge, new CpuMoveTiming(12)).X, 0f, 1f);
        var stunned = CpuObservation.Capture(0, target with { State = ActionState.Hitstun, HitstunTicks = 2 }, 0f).WithAge(18);
        Assert.Equal(0.1f, CpuTargetPrediction.AtActivation(default, stunned, default, new CpuMoveTiming(12)).X, 4);
        var stopped = CpuObservation.Capture(0, target with { HitstopTicks = 30 }, 0f).WithAge(12);
        Assert.Equal(0f, CpuTargetPrediction.AtActivation(default, stopped, default, new CpuMoveTiming(6)).X);
        var launched = CpuObservation.Capture(0, target with { InPostHitstunFlight = true }, 0f).WithAge(18);
        Assert.Equal(0f, CpuTargetPrediction.AtActivation(default, launched, default, new CpuMoveTiming(12)).X);
        var approach = Decide(policy, def, target with { PZ = 10f }, difficulty, 1);
        Assert.True(approach.MoveX > 0f, "approach must head toward the forecast rather than the old line");
    }

    [Fact]
    public void AnApproachingTargetCanEnterReachDuringReactionAndStartup()
    {
        var def = Kit(12, 2f);
        var target = TestHelpers.NpcState(0f, 4f) with { PY = TestHelpers.GroundPY(def), VZ = -6f };
        var policy = new HeuristicBotPolicy();
        int attacks = 0;
        for (int seed = 0; seed < 200; seed++)
            if (Decide(policy, def, target, CpuDifficulty.Normal, seed).ActiveSlot == AbilitySlots.Normal1) attacks++;
        Assert.True(attacks > 50, $"only {attacks} strikes anticipated the incoming target");
        int arrivingAtSelf = 0;
        for (int seed = 0; seed < 200; seed++)
            if (Decide(policy, def, target with { PZ = 2.4f, VZ = -4.8f }, CpuDifficulty.Normal, seed).ActiveSlot == AbilitySlots.Normal1) arrivingAtSelf++;
        Assert.True(arrivingAtSelf > 50, $"only {arrivingAtSelf} attacks met the target arriving at the CPU");
        var mixed = TestHelpers.WithMove(Kit(0, 2f), AbilitySlots.Normal2, false, "Incoming slow strike", 45,
            hits: new[] { new HitboxEvent { TriggerTick = 20, DurationTicks = 1,
                Shape = HitboxShape.Sphere, Radius = 0.4f, OffZ = 1.6f, Damage = 8f } });
        int slow = 0;
        for (int seed = 0; seed < 200; seed++)
            if (Decide(policy, mixed, target with { PZ = 4.5f }, CpuDifficulty.Normal, seed).ActiveSlot == AbilitySlots.Normal2) slow++;
        Assert.True(slow > 20, $"only {slow} slow strikes could enter range after the fast strike");
    }

    [Fact]
    public void MoveChoiceUsesEachMovesOwnStartup()
    {
        var def = TestHelpers.WithMove(Kit(0, 1.6f), AbilitySlots.Normal2, false, "Slow prediction strike", 60,
            hits: new[] { new HitboxEvent { TriggerTick = 30, DurationTicks = 1,
                Shape = HitboxShape.Sphere, Radius = 0.4f, OffZ = 1.2f, Damage = 8f } });
        var target = TestHelpers.NpcState(0f, 0.2f) with { PY = TestHelpers.GroundPY(def), VZ = 4f };
        var policy = new HeuristicBotPolicy();
        int fast = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            byte slot = Decide(policy, def, target, CpuDifficulty.Normal, seed).ActiveSlot;
            Assert.NotEqual(AbilitySlots.Normal2, slot);
            if (slot == AbilitySlots.Normal1) fast++;
        }
        Assert.True(fast > 100, $"only {fast} fast strikes");
        var observation = CpuObservation.Capture(0, target with { PX = 0f, PZ = 1f, VX = 1f, VZ = 0f }, 0f).WithAge(3);
        var projectile = CpuTargetPrediction.AtActivation(default, observation, default, new CpuMoveTiming(4, 10, 60f));
        Assert.Equal(6, projectile.ContactTicks);
        Assert.Equal(0.15f, projectile.X, 4);
        var fixedTravel = CpuTargetPrediction.AtActivation(default, observation, default, new CpuMoveTiming(4, 5));
        Assert.Equal(9, fixedTravel.ContactTicks);
        Assert.Equal(0.2f, fixedTravel.X, 4);
    }
}
