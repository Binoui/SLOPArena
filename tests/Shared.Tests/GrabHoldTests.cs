using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class GrabHoldTests
{
    internal static (ServerSimulation Sim, CharacterDefinition Def) Capture(float yaw = 0f, ArenaDefinition? arena = null,
        CharacterDefinition? definition = null, CombatTuning? tuning = null)
    {
        var def = definition ?? TestHelpers.EngineDef;
        def = def with { CaptureGeometry = new CookedCaptureGeometry(1.5f, 0.8f, 1.2f, 0.6f,
            new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f)) };
        var sim = new ServerSimulation(arena ?? TestHelpers.TestArena(), combatTuning: tuning);
        var grabber = TestHelpers.PlayerState(x: 20f, z: 20f) with
        {
            PY = TestHelpers.GroundPY(def), FacingYaw = yaw,
        };
        var victim = TestHelpers.PlayerState(x: grabber.PX + MathF.Sin(yaw) * 0.8f,
            z: grabber.PZ + MathF.Cos(yaw) * 0.8f) with
        {
            EntityId = 2, PY = grabber.PY, FacingYaw = yaw + MathF.PI,
        };
        sim.RegisterEntity(1, def, grabber);
        sim.RegisterEntity(2, def, victim);
        Tick(sim, new InputState { GrabPressed = true });
        for (int tick = 0; tick < DefenseConfig.GrabStartupTicks; tick++) Tick(sim);
        Assert.Equal(ActionState.Grabbing, sim.GetState(1).State);
        Assert.Equal(ActionState.Grabbed, sim.GetState(2).State);
        return (sim, def);
    }

    internal static void Tick(ServerSimulation sim, InputState grabber = default, InputState victim = default)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = grabber, [2] = victim });

    [Theory]
    [InlineData(1f)]
    [InlineData(4f)]
    public void RunningGrabCapsCarryAndCapturesAfterNormalStartup(float speedMultiplier)
    {
        var def = TestHelpers.EngineDef;
        def = def with { CaptureGeometry = new CookedCaptureGeometry(1.5f, 0.8f, 1.2f, 0.6f,
            new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f)) };
        var sim = new ServerSimulation(TriangleArena());
        var initial = TestHelpers.PlayerState(x: 20f, z: 20f) with
        {
            PY = TestHelpers.GroundPY(def), State = ActionState.Run,
            VZ = def.Movement.RunSpeed * speedMultiplier,
        };
        sim.RegisterEntity(1, def, initial);
        sim.RegisterEntity(2, def, initial with
        {
            EntityId = 2, State = ActionState.Idle, VZ = 0f, PZ = initial.PZ + 0.8f,
        });

        for (int tick = 0; tick < DefenseConfig.GrabStartupTicks; tick++)
        {
            Tick(sim, new InputState { GrabPressed = tick == 0, MoveY = 1f });
            var state = sim.GetState(1);
            Assert.Equal(ActionState.GrabAttempt, state.State);
            Assert.InRange(state.VZ, 0f, def.Movement.RunSpeed);
        }
        var stopped = sim.GetState(1);
        Assert.Equal(0f, stopped.VZ);
        Assert.InRange(stopped.PZ - initial.PZ, 0.01f,
            def.Movement.RunSpeed * DefenseConfig.GrabBrakeTicks * Simulation.TickDt * 0.5f);
        Tick(sim, new InputState { MoveY = 1f });

        Assert.Equal(ActionState.Grabbing, sim.GetState(1).State);
        Assert.Equal(ActionState.Grabbed, sim.GetState(2).State);
        Assert.Equal(sim.GetState(1).InteractionId, sim.GetState(2).InteractionId);
        Assert.Equal(0f, sim.GetState(1).VZ);
        Assert.Equal(stopped.PZ, sim.GetState(1).PZ);
    }

    [Fact]
    public void GrabStartCarryStopsAtStageWall()
    {
        var def = TestHelpers.EngineDef;
        var arena = TriangleArena();
        arena.CollisionTriangles = new[]
        {
            arena.CollisionTriangles[0], arena.CollisionTriangles[1],
            new CollisionTriangle { AX = 20.5f, AY = 0, AZ = 0, BX = 20.5f, BY = 0, BZ = 40, CX = 20.5f, CY = 10, CZ = 0 },
            new CollisionTriangle { AX = 20.5f, AY = 0, AZ = 40, BX = 20.5f, BY = 10, BZ = 40, CX = 20.5f, CY = 10, CZ = 0 },
        };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        var sim = new ServerSimulation(arena);
        var initial = TestHelpers.PlayerState(x: 20f, z: 20f) with
        {
            PY = TestHelpers.GroundPY(def), State = ActionState.Run, VX = def.Movement.RunSpeed,
        };
        sim.RegisterEntity(1, def, initial);

        for (int tick = 0; tick < DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks; tick++)
        {
            Tick(sim, new InputState { GrabPressed = tick == 0, MoveX = 1f });
            var state = sim.GetState(1);
            Assert.Equal(ActionState.GrabAttempt, state.State);
            Assert.InRange(state.PX, initial.PX, 20.5f - def.CapsuleRadius + 0.001f);
        }
        Assert.True(sim.GetState(1).PX > initial.PX);
        Assert.Equal(0f, sim.GetState(1).VX);
    }

    [Fact]
    public void CaptureHoldsForThreeSecondsThenStartsThrowWithoutEarlyDamage()
    {
        var (sim, _) = Capture();
        ulong interaction = sim.GetState(1).InteractionId;
        for (int tick = 1; tick < DefenseConfig.GrabHoldTicks; tick++)
        {
            Tick(sim);
            Assert.Equal(ActionState.Grabbing, sim.GetState(1).State);
            Assert.Equal(interaction, sim.GetState(2).InteractionId);
            Assert.Equal(0, sim.GetState(2).DamagePercent);
        }
        Tick(sim);
        Assert.Equal(ActionState.Throwing, sim.GetState(1).State);
        Assert.Equal((byte)DefenseInteractionPhase.UpTossing, sim.GetState(2).InteractionPhase);
        Assert.Equal(0, sim.GetState(2).DamagePercent);
    }

    [Theory]
    [InlineData(AbilitySlots.Normal1)]
    [InlineData(AbilitySlots.Normal2)]
    [InlineData(AbilitySlots.Normal3)]
    [InlineData(AbilitySlots.A)]
    [InlineData(AbilitySlots.E)]
    [InlineData(AbilitySlots.R)]
    [InlineData(AbilitySlots.F)]
    public void AnyAttackPressRequestsThrowWithoutActivatingOrBufferingAbility(byte slot)
    {
        var (sim, _) = Capture();
        var state = sim.GetState(1);
        state.SetCooldown(slot, 100);
        sim.SetState(1, state);
        Tick(sim, new InputState { ActiveSlot = slot, MoveY = 1, Jump = true, ShieldHeld = true });
        state = sim.GetState(1);
        Assert.Equal(ActionState.Throwing, state.State);
        Assert.Equal(DefenseConfig.ThrowReleaseTicks, state.StateTicks);
        Assert.Equal(0, state.AttackSlot);
        Assert.Equal(0, state.BufferedSlot);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal(0f, state.VZ);

        for (int tick = 1; tick < DefenseConfig.ThrowReleaseTicks; tick++)
        {
            Tick(sim);
            Assert.NotEqual(0ul, sim.GetState(2).InteractionId);
            Assert.Equal(0, sim.GetState(2).DamagePercent);
        }
        Tick(sim);
        Assert.True(sim.GetState(2).HitstopTicks > 0);
        Assert.Equal(8, sim.GetState(2).DamagePercent);
        Assert.Equal(0ul, sim.GetState(1).InteractionId);
        Assert.Equal(0ul, sim.GetState(2).InteractionId);
        Assert.Equal(ActionState.Throwing, sim.GetState(1).State);
        Assert.Equal((ushort)12, sim.GetState(1).StateTicks);
        Tick(sim);
        Assert.Equal(8, sim.GetState(2).DamagePercent);
    }

    internal static ArenaDefinition TriangleArena()
    {
        var arena = TestHelpers.TestArena();
        arena.MinX = arena.MinZ = 0f;
        arena.MaxX = arena.MaxZ = 40f;
        arena.CollisionTriangles = new[]
        {
            new CollisionTriangle { AX = 0, AY = 0, AZ = 0, BX = 0, BY = 0, BZ = 40, CX = 40, CY = 0, CZ = 0 },
            new CollisionTriangle { AX = 40, AY = 0, AZ = 40, BX = 40, BY = 0, BZ = 0, CX = 0, CY = 0, CZ = 40 },
        };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }
}
