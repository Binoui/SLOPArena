using System;
using System.Collections.Generic;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class GrabHoldTests
{
    private static (ServerSimulation Sim, CharacterDefinition Def) Capture(float yaw = 0f, ArenaDefinition? arena = null)
    {
        var def = TestHelpers.EngineDef;
        def.CaptureGeometry = new CookedCaptureGeometry(1.5f, 0.8f, 1.2f, 0.6f,
            new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f));
        var sim = new ServerSimulation(arena ?? TestHelpers.TestArena());
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

    private static void Tick(ServerSimulation sim, InputState grabber = default, InputState victim = default)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = grabber, [2] = victim });

    [Theory]
    [InlineData(1f)]
    [InlineData(4f)]
    public void RunningGrabCapsCarryAndCapturesAfterNormalStartup(float speedMultiplier)
    {
        var def = TestHelpers.EngineDef;
        def.CaptureGeometry = new CookedCaptureGeometry(1.5f, 0.8f, 1.2f, 0.6f,
            new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f));
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
        Assert.Equal((byte)DefenseInteractionPhase.Throwing, sim.GetState(2).InteractionPhase);
        Assert.Equal(0, sim.GetState(2).DamagePercent);
    }

    [Theory]
    [InlineData(AbilitySlots.Slot1)]
    [InlineData(AbilitySlots.Slot2)]
    [InlineData(AbilitySlots.Slot3)]
    [InlineData(AbilitySlots.Slot4)]
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
        Assert.Equal(DefenseConfig.ThrowAnimationTicks, state.StateTicks);
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
        Assert.Equal(ActionState.Thrown, sim.GetState(2).State);
        Assert.Equal(6, sim.GetState(2).DamagePercent);
        Assert.Equal(0ul, sim.GetState(1).InteractionId);
        Assert.Equal(0ul, sim.GetState(2).InteractionId);
        Assert.Equal(ActionState.Throwing, sim.GetState(1).State);
        Assert.Equal(DefenseConfig.ThrowAttackerRecoveryTicks, sim.GetState(1).StateTicks);
        Tick(sim);
        Assert.Equal(6, sim.GetState(2).DamagePercent);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0f, -1f)]
    [InlineData(1.5707964f, 1f)]
    [InlineData(1.5707964f, -1f)]
    public void GrabberWalksSlowlyAndVictimFollowsFacingAway(float yaw, float direction)
    {
        var (sim, def) = Capture(yaw);
        var start = sim.GetState(1);
        var grabberInput = new InputState
        {
            MoveX = MathF.Sin(yaw) * direction, MoveY = MathF.Cos(yaw) * direction,
            Jump = true, Down = true, DownPressed = true, ShieldHeld = true, FaceToCamera = true,
            AimYaw = (short)MathF.Round(yaw * (18000f / MathF.PI)),
        };
        for (int tick = 0; tick < 30; tick++)
            Tick(sim, grabberInput, new InputState { MoveX = -1, MoveY = -1, Jump = true, ActiveSlot = AbilitySlots.F });
        var grabber = sim.GetState(1);
        var victim = sim.GetState(2);
        Assert.Equal(ActionState.Grabbing, grabber.State);
        Assert.Equal(ActionState.Grabbed, victim.State);
        float distance = MathF.Sqrt(MathF.Pow(grabber.PX - start.PX, 2) + MathF.Pow(grabber.PZ - start.PZ, 2));
        TestHelpers.AssertNear(def.Movement.RunSpeed * DefenseConfig.GrabWalkSpeedRatio * 0.5f, distance);
        TestHelpers.AssertNear(grabber.PX + MathF.Sin(grabber.FacingYaw) * 0.65f, victim.PX);
        TestHelpers.AssertNear(grabber.PZ + MathF.Cos(grabber.FacingYaw) * 0.65f, victim.PZ);
        Assert.Equal(start.FacingYaw, grabber.FacingYaw);
        Assert.Equal(grabber.FacingYaw, victim.FacingYaw);
        Assert.Equal(grabber.VX, victim.VX);
        Assert.Equal(grabber.VZ, victim.VZ);
    }

    [Fact]
    public void ForwardControlWalksAlongGrabFacingEvenWhenCameraFacesBackward()
    {
        var (sim, def) = Capture();
        var start = sim.GetState(1);
        for (int tick = 0; tick < 30; tick++)
            Tick(sim, new InputState { MoveY = -1, AimYaw = 18000, FaceToCamera = true });
        var grabber = sim.GetState(1);
        TestHelpers.AssertNear(start.PZ + def.Movement.RunSpeed * DefenseConfig.GrabWalkSpeedRatio * 0.5f, grabber.PZ);
        Assert.Equal(start.PX, grabber.PX);
        Assert.Equal(start.FacingYaw, grabber.FacingYaw);
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(9000, 1f)]
    [InlineData(18000, -1f)]
    [InlineData(-9000, -1f)]
    public void SideControlRotatesPairSlowlyInPlaceRegardlessOfCameraYaw(short cameraYaw, float side)
    {
        var (sim, _) = Capture();
        var start = sim.GetState(1);
        float controlYaw = cameraYaw * (MathF.PI / 18000f);
        var input = new InputState
        {
            MoveX = MathF.Cos(controlYaw) * side,
            MoveY = -MathF.Sin(controlYaw) * side,
            AimYaw = cameraYaw,
        };
        for (int tick = 0; tick < 60; tick++)
            Tick(sim, input, new InputState { MoveX = -1, FaceToCamera = true, AimYaw = 18000 });
        var grabber = sim.GetState(1);
        var victim = sim.GetState(2);
        Assert.Equal(ActionState.Grabbing, grabber.State);
        TestHelpers.AssertNear(side * DefenseConfig.GrabTurnDegreesPerSecond * MathF.PI / 180f, grabber.FacingYaw);
        Assert.Equal(start.PX, grabber.PX);
        Assert.Equal(start.PZ, grabber.PZ);
        Assert.Equal(0f, grabber.VX);
        Assert.Equal(0f, grabber.VZ);
        Assert.Equal(grabber.FacingYaw, victim.FacingYaw);
        Assert.Equal(grabber.CapturedYaw, victim.CapturedYaw);
        TestHelpers.AssertNear(grabber.PX + MathF.Sin(grabber.FacingYaw) * 0.65f, victim.PX);
        TestHelpers.AssertNear(grabber.PZ + MathF.Cos(grabber.FacingYaw) * 0.65f, victim.PZ);
    }

    [Theory]
    [InlineData(179f, 1f, -17600)]
    [InlineData(-179f, -1f, 17600)]
    public void HeldRotationWrapsAcross180Degrees(float startDegrees, float side, short expectedYaw)
    {
        var (sim, _) = Capture(startDegrees * MathF.PI / 180f);
        for (int tick = 0; tick < 5; tick++) Tick(sim, new InputState { MoveX = side });
        Assert.Equal(expectedYaw, sim.GetState(1).CapturedYaw);
        Assert.Equal(expectedYaw, sim.GetState(2).CapturedYaw);
    }

    [Fact]
    public void ThrowUsesFacingReachedByHeldRotation()
    {
        var (sim, _) = Capture();
        for (int tick = 0; tick < 90; tick++) Tick(sim, new InputState { MoveX = 1 });
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1, MoveX = -1 });
        for (int tick = 0; tick < DefenseConfig.ThrowReleaseTicks; tick++) Tick(sim);
        var victim = sim.GetState(2);
        Assert.Equal(ActionState.Thrown, victim.State);
        Assert.Equal((short)9000, victim.CapturedYaw);
        Assert.True(victim.KVX > 0f);
        TestHelpers.AssertNear(0f, victim.KVZ);
    }

    [Fact]
    public void RotationStopsBeforeHeldVictimWouldCrossWall()
    {
        var arena = TriangleArena();
        arena.CollisionTriangles = new[]
        {
            arena.CollisionTriangles[0], arena.CollisionTriangles[1],
            new CollisionTriangle { AX = 20.8f, AY = 0, AZ = 0, BX = 20.8f, BY = 0, BZ = 40, CX = 20.8f, CY = 10, CZ = 0 },
            new CollisionTriangle { AX = 20.8f, AY = 0, AZ = 40, BX = 20.8f, BY = 10, BZ = 40, CX = 20.8f, CY = 10, CZ = 0 },
        };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        var (sim, def) = Capture(-MathF.PI * 0.5f, arena);
        for (int tick = 0; tick < 150; tick++) Tick(sim, new InputState { MoveX = 1 });
        var stopped = sim.GetState(1);
        for (int tick = 0; tick < 20; tick++) Tick(sim, new InputState { MoveX = 1 });
        var grabber = sim.GetState(1);
        var victim = sim.GetState(2);
        Assert.Equal(ActionState.Grabbing, grabber.State);
        Assert.Equal(ActionState.Grabbed, victim.State);
        Assert.Equal(stopped.CapturedYaw, grabber.CapturedYaw);
        Assert.InRange(grabber.FacingYaw, 0f, MathF.PI / 3f);
        Assert.True(victim.PX <= 20.8f - def.CapsuleRadius + 0.001f);
        Assert.Equal(grabber.CapturedYaw, victim.CapturedYaw);
        Assert.Equal(grabber.FacingYaw, victim.FacingYaw);
    }

    [Fact]
    public void VictimLandsAtFrame34AndRecoversAtFrame42WhileGrabberFinishesFrame29()
    {
        var (sim, _) = Capture();
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        for (int tick = 1; tick <= DefenseConfig.ThrownAnimationTicks; tick++)
        {
            Tick(sim, new InputState { MoveX = 1, Jump = true }, new InputState { MoveX = -1, Jump = true });
            var victim = sim.GetState(2);
            if (tick < DefenseConfig.ThrowReleaseTicks)
                Assert.Equal(ActionState.Grabbed, victim.State);
            else if (tick < DefenseConfig.ThrownAnimationTicks)
            {
                Assert.Equal(ActionState.Thrown, victim.State);
                Assert.Equal(tick >= DefenseConfig.ThrownLandingTicks, victim.IsGrounded);
            }
            else Assert.Equal(ActionState.Idle, victim.State);
            if (tick < DefenseConfig.ThrowAnimationTicks)
                Assert.Equal(ActionState.Throwing, sim.GetState(1).State);
            else Assert.NotEqual(ActionState.Throwing, sim.GetState(1).State);
        }
    }

    [Theory]
    [InlineData(ActionState.Grabbing)]
    [InlineData(ActionState.Thrown)]
    public void HoldAndThrownStatesSurvivePacketRoundTrip(ActionState action)
    {
        var (sim, _) = Capture();
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        for (int tick = 0; tick < DefenseConfig.ThrowReleaseTicks; tick++) Tick(sim);
        var state = action == ActionState.Grabbing ? Capture().Sim.GetState(1) : sim.GetState(2);
        var packet = CharacterStatePacket.FromState(state);
        var bytes = new byte[CharacterStatePacket.Size];
        packet.Serialize(bytes);
        var restored = CharacterStatePacket.Deserialize(bytes).ToState();
        Assert.Equal(action, restored.State);
        Assert.Equal(state.StateTicks, restored.StateTicks);
        Assert.Equal(state.InteractionId, restored.InteractionId);
        Assert.Equal(state.InteractionTick, restored.InteractionTick);
        Assert.Equal(state.CapturedYaw, restored.CapturedYaw);
        Assert.Equal(action == ActionState.Thrown, ActionStateClassifier.IsPredictable(restored));
    }

    [Fact]
    public void HeldVictimCannotBeWalkedThroughStageWall()
    {
        var arena = TriangleArena();
        arena.CollisionTriangles = new[]
        {
            arena.CollisionTriangles[0], arena.CollisionTriangles[1],
            new CollisionTriangle { AX = 0, AY = 0, AZ = 21.5f, BX = 40, BY = 0, BZ = 21.5f, CX = 0, CY = 10, CZ = 21.5f },
            new CollisionTriangle { AX = 40, AY = 0, AZ = 21.5f, BX = 40, BY = 10, BZ = 21.5f, CX = 0, CY = 10, CZ = 21.5f },
        };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        var (sim, def) = Capture(arena: arena);
        for (int tick = 0; tick < 60; tick++) Tick(sim, new InputState { MoveY = 1 });
        var grabber = sim.GetState(1);
        var victim = sim.GetState(2);
        Assert.Equal(ActionState.Grabbing, grabber.State);
        Assert.Equal(ActionState.Grabbed, victim.State);
        Assert.InRange(victim.PZ, 20.65f, 21.5f - def.CapsuleRadius + 0.001f);
        TestHelpers.AssertNear(grabber.PZ + 0.65f, victim.PZ);
        Assert.Equal(0f, grabber.VZ);
        Assert.Equal(0f, victim.VZ);
    }

    [Fact]
    public void ThrowFlightUsesTriangleGroundAndKeepsFullLandingRecovery()
    {
        var (sim, _) = Capture(arena: TriangleArena());
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.Slot1 });
        for (int tick = 1; tick < DefenseConfig.ThrownLandingTicks; tick++)
        {
            Tick(sim);
            if (tick >= DefenseConfig.ThrowReleaseTicks) Assert.False(sim.GetState(2).IsGrounded);
        }
        Tick(sim);
        Assert.True(sim.GetState(2).IsGrounded);
        Assert.Equal(DefenseConfig.ThrownRecoveryTicks, sim.GetState(2).StateTicks);
        for (int tick = 1; tick < DefenseConfig.ThrownRecoveryTicks; tick++)
        {
            Tick(sim);
            Assert.Equal(ActionState.Thrown, sim.GetState(2).State);
        }
        Tick(sim);
        Assert.Equal(ActionState.Idle, sim.GetState(2).State);
    }

    private static ArenaDefinition TriangleArena()
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
