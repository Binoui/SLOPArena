using System;
using System.Collections.Generic;
using SlopArena.Shared.AI;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class TrainingCpuBehaviorTests
{
    private static CharacterState Fighter(CharacterDefinition def, ulong id = 1, float x = 0f, float z = 0f)
        => TestHelpers.PlayerState(x, z) with { EntityId = id, PY = TestHelpers.GroundPY(def) };

    private static ArenaDefinition Stage()
    {
        var arena = TestHelpers.TestArena();
        arena.Heightmap = new ArenaHeightmap
        {
            Data = new float[21 * 21], Width = 21, Height = 21,
            OriginX = -10f, OriginZ = -10f, CellSize = 1f,
        };
        arena.MinX = arena.MinZ = -8f;
        arena.MaxX = arena.MaxZ = 8f;
        return arena;
    }

    private static BotMemory Observed(CharacterState target, CpuDifficulty difficulty = CpuDifficulty.Normal)
    {
        var memory = new BotMemory { Difficulty = difficulty };
        for (int tick = 0; tick <= BotDifficultyProfile.ForDifficulty(difficulty).ReactionDelayTicks; tick++)
            memory.ObserveOpponent(target);
        return memory;
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void ShieldModeHoldsGuardAgainstAnIdleOpponent(CharacterClass character)
    {
        var def = TestHelpers.ResolveDef(character);
        var arena = Stage();
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, Fighter(def));
        var target = Fighter(def, 2, z: 3f);
        var memory = new BotMemory();
        var policy = new HeuristicBotPolicy();
        var rng = new Random(42);
        for (int tick = 0; tick < 180; tick++)
        {
            var input = policy.Decide(sim.GetState(1), target, def, rng, memory, arena,
                behavior: CpuBehavior.Shield);
            Assert.True(input.ShieldHeld);
            Assert.False(input.ShieldPressed);
            Assert.Equal(0, input.ActiveSlot);
            sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
            Assert.Equal(ActionState.Shielding, sim.GetState(1).State);
            Assert.Equal(0, sim.GetState(1).Deaths);
        }
    }

    [Fact]
    public void AvoidModeRetreatsWithoutAttackingThenStopsAtSafeDistance()
    {
        var def = TestHelpers.FightGuyDef;
        var self = Fighter(def);
        var target = Fighter(def, 2, z: 1f);
        var policy = new HeuristicBotPolicy();
        var memory = Observed(target);
        var rng = new Random(5);
        var input = policy.Decide(self, target, def, rng, memory, Stage(), behavior: CpuBehavior.Avoid);
        Assert.True(input.MoveY < 0f);
        Assert.Equal(0, input.ActiveSlot);
        Assert.False(input.ShieldHeld);
        self.PZ = -7f;
        input = policy.Decide(self, target, def, rng, memory, Stage(), behavior: CpuBehavior.Avoid);
        Assert.Equal(0f, input.MoveX);
        Assert.Equal(0f, input.MoveY);
        Assert.Equal(0, input.ActiveSlot);
    }

    [Fact]
    public void AvoidModeDoesNotReadOpponentBeforeItsReactionDelay()
    {
        var def = TestHelpers.FightGuyDef;
        var self = Fighter(def);
        var target = Fighter(def, 2, z: 1f);
        var memory = new BotMemory { Difficulty = CpuDifficulty.Easy };
        var policy = new HeuristicBotPolicy();
        var rng = new Random(8);
        int delay = BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks;
        for (int tick = 0; tick < delay; tick++)
        {
            var input = policy.Decide(self, target, def, rng, memory, Stage(), behavior: CpuBehavior.Avoid);
            Assert.Equal(0f, input.MoveX);
            Assert.Equal(0f, input.MoveY);
        }
        Assert.True(policy.Decide(self, target, def, rng, memory, Stage(),
            behavior: CpuBehavior.Avoid).MoveY < 0f);
    }

    [Theory]
    [InlineData(CpuBehavior.Attack)]
    [InlineData(CpuBehavior.Avoid)]
    public void GroundedMovementTurnsAlongEdgeRatherThanRunningOffstage(CpuBehavior behavior)
    {
        var def = TestHelpers.FightGuyDef;
        var self = Fighter(def, x: 7.5f);
        var target = behavior == CpuBehavior.Avoid
            ? Fighter(def, 2, x: 6.5f) : Fighter(def, 2, x: 7.5f, z: -1f);
        bool moved = false;
        for (int seed = 0; seed < 64; seed++)
        {
            var input = new HeuristicBotPolicy().Decide(self, target, def, new Random(seed),
                Observed(target, CpuDifficulty.Easy), Stage(), behavior: behavior);
            if (input.Jump || input.ActiveSlot != 0) continue;
            Assert.True(input.MoveX <= 0f);
            moved |= input.MoveX != 0f || input.MoveY != 0f;
        }
        Assert.True(moved);
    }

    [Theory]
    [InlineData(CpuBehavior.Shield)]
    [InlineData(CpuBehavior.Avoid)]
    public void DefensiveModesStillRecoverWhenLaunchedOffstage(CpuBehavior behavior)
    {
        var def = TestHelpers.FightGuyDef;
        var self = Fighter(def, x: 10f) with { IsGrounded = false, VY = -2f };
        var target = Fighter(def, 2);
        var input = new HeuristicBotPolicy().Decide(self, target, def, new Random(1),
            Observed(target), Stage(), behavior: behavior);
        Assert.True(input.MoveX < 0f);
        Assert.False(input.ShieldHeld);
    }

    [Fact]
    public void AvoidModeDoesNotRunAcrossAGapToASupportedEndpoint()
    {
        var def = TestHelpers.FightGuyDef;
        var arena = Stage();
        for (int z = 0; z < arena.Heightmap.Height; z++)
        {
            arena.Heightmap.Data[z * arena.Heightmap.Width + 11] = float.MinValue;
            arena.Heightmap.Data[z * arena.Heightmap.Width + 12] = float.MinValue;
        }
        var self = Fighter(def);
        var target = Fighter(def, 2, x: -1f);
        var input = new HeuristicBotPolicy().Decide(self, target, def, new Random(1),
            Observed(target), arena, behavior: CpuBehavior.Avoid);
        Assert.Equal(0f, input.MoveX);
        Assert.NotEqual(0f, input.MoveY);
        Assert.Equal(0, input.ActiveSlot);
    }

    [Theory]
    [InlineData(CpuBehavior.Shield)]
    [InlineData(CpuBehavior.Avoid)]
    public void ChangingBehaviorDiscardsAnAttackPlan(CpuBehavior behavior)
    {
        var def = TestHelpers.MankiDef;
        var self = Fighter(def);
        var target = Fighter(def, 2, z: 1f);
        var memory = Observed(target, CpuDifficulty.Hard);
        memory.StartPlan(AbilitySlots.A, true, 0, 0, 100, 12, 0, true);
        var input = new HeuristicBotPolicy().Decide(self, target, def, new Random(2), memory,
            Stage(), behavior: behavior);
        Assert.Equal(0, input.ActiveSlot);
        Assert.False(input.IsAiming);
        Assert.Equal(BotPlanPhase.None, memory.PlanPhase);
    }

    [Fact]
    public void AvoidModeDoesNotActDuringHitstun()
    {
        var def = TestHelpers.FightGuyDef;
        var self = Fighter(def) with { State = ActionState.Hitstun, HitstunTicks = 20 };
        var target = Fighter(def, 2, z: 1f);
        var input = new HeuristicBotPolicy().Decide(self, target, def, new Random(4),
            Observed(target), Stage(), behavior: CpuBehavior.Avoid);
        Assert.Equal(0f, input.MoveX);
        Assert.Equal(0f, input.MoveY);
        Assert.False(input.ShieldPressed);
        Assert.Equal(0, input.ActiveSlot);
    }
}
