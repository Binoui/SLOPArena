using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class GroundSpeedTests
{
    [Fact]
    public void StartsAtFortyPercentThenReachesCruiseAfterTwentyIncrements()
    {
        var sim = Create();
        for (int tick = 1; tick <= 30; tick++)
        {
            Step(sim, 1f);
            float expected = tick >= 21 ? 14f : 5.6f + (tick - 1) * 0.42f;
            Assert.Equal(expected, sim.GetState(1).VX, 4);
            Assert.Equal(ActionState.Run, sim.GetState(1).State);
        }
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void TiltMagnitudeDoesNotChangeAnyMovementSample(float tilt)
    {
        var full = Create();
        var partial = Create();
        for (int tick = 0; tick < 60; tick++)
        {
            Step(full, tick < 25 ? 1f : tick < 35 ? -1f : 0f);
            Step(partial, tick < 25 ? tilt : tick < 35 ? -tilt : 0f);
            Assert.Equal(full.CaptureSnapshot().Data, partial.CaptureSnapshot().Data);
        }
    }

    [Theory]
    [InlineData(0f, 5.6f)]
    [InlineData(2f, 5.6f)]
    [InlineData(7f, 7.42f)]
    [InlineData(14f, 14f)]
    public void TurnsRetainSpeedContinueRampingAndFaceInput(float speed, float expected)
    {
        foreach (bool previousInput in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        {
            var sim = Create();
            var state = sim.GetState(1);
            state.VX = speed;
            state.LastDirX = previousInput ? 1f : 0f;
            sim.RegisterEntity(1, TestHelpers.EngineDef, state);
            Step(sim, reverse ? -1f : 0f, reverse ? 0f : 1f);
            state = sim.GetState(1);
            Assert.Equal(reverse ? -expected : 0f, state.VX, 4);
            Assert.Equal(reverse ? 0f : expected, state.VZ, 4);
            Assert.Equal(reverse ? -MathF.PI / 2f : 0f, state.FacingYaw, 5);
        }
    }

    [Fact]
    public void ContinuousDirectionsReachCruiseOnTickTwentyOne()
    {
        foreach (float runSpeed in new[] { 10f, 11.5f, 12f, 14f })
        for (int degrees = 0; degrees < 360; degrees += 5)
        {
            var sim = Create();
            var def = TestHelpers.EngineDef;
            def = def with { Movement = def.Movement with { RunSpeed = runSpeed } };
            sim.RegisterEntity(1, def, sim.GetState(1));
            float angle = degrees * MathF.PI / 180f;
            for (int tick = 1; tick <= 21; tick++)
            {
                Step(sim, MathF.Sin(angle), MathF.Cos(angle));
                float expected = tick == 21 ? runSpeed : runSpeed * (.4f + .03f * (tick - 1));
                Assert.True(MathF.Abs(Speed(sim.GetState(1)) - expected) < .0001f,
                    $"run={runSpeed}, direction={degrees}, tick={tick}, speed={Speed(sim.GetState(1))}, expected={expected}");
            }
        }
    }

    [Fact]
    public void ReversalOverridesTangentDirectionMemory()
    {
        var sim = Create();
        float diagonal = -1f / MathF.Sqrt(2f);
        var state = sim.GetState(1);
        state.VX = 7f;
        state.LastDirX = state.LastDirZ = diagonal;
        sim.RegisterEntity(1, TestHelpers.EngineDef, state);
        Step(sim, diagonal, diagonal);
        Assert.Equal(7.42f * diagonal, sim.GetState(1).VX, 4);
        Assert.Equal(7.42f * diagonal, sim.GetState(1).VZ, 4);
    }

    [Fact]
    public void ReapplyingInputBelowTheStartFloorStartsAtFortyPercent()
    {
        var sim = Create();
        Step(sim, 1f);
        for (int tick = 0; tick < 5; tick++) Step(sim, 0f);
        Assert.InRange(sim.GetState(1).VX, 0f, 5.59f);
        Step(sim, 1f);
        Assert.Equal(5.6f, sim.GetState(1).VX, 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseBrakesAlongVelocityAndStopsAfterTwentyTicks(bool diagonal)
    {
        var sim = Create();
        float axis = diagonal ? 1f / MathF.Sqrt(2f) : 1f;
        for (int tick = 0; tick < 30; tick++) Step(sim, axis, diagonal ? axis : 0f);
        var origin = sim.GetState(1);
        for (int tick = 1; tick <= 20; tick++)
        {
            Step(sim, 0f);
            var state = sim.GetState(1);
            Assert.Equal(MathF.Max(0f, 14f - tick * 0.7f), Speed(state), 4);
            if (diagonal) Assert.Equal(state.VX, state.VZ, 5);
        }
        var stopped = sim.GetState(1);
        float dx = stopped.PX - origin.PX, dz = stopped.PZ - origin.PZ;
        Assert.Equal(2.2166667f, MathF.Sqrt(dx * dx + dz * dz), 4);
    }

    [Theory]
    [InlineData(6, 0.665f, 1.3066667f)]
    [InlineData(12, 1.582f, 2.7416667f)]
    [InlineData(18, 2.751f, 4.578f)]
    public void ShortPushesIncludeEveryHeldAndReleaseTick(int ticks, float held, float total)
    {
        var metrics = MovementProbe.MeasureGroundSpeed(TestHelpers.EngineDef, TestHelpers.TestArena());
        var tap = ticks == 6 ? metrics.Tap6 : ticks == 12 ? metrics.Tap12 : metrics.Tap18;
        Assert.Equal(held, tap.HeldDistance, 4);
        Assert.Equal(total, tap.TotalDistance, 4);
        Assert.Equal(21, metrics.TimeToRunTicks);
        Assert.Equal(20, metrics.RunStop.StopTicks);
        Assert.Equal(1, metrics.Reversal.RecoveryTicks);
        Assert.Equal(14f, metrics.Reversal.FirstReverseSpeed);
    }

    [Theory]
    [InlineData(0f, 20f, 19.4f)]
    [InlineData(1f, 20f, 19.8666667f)]
    [InlineData(-1f, 20f, -14f)]
    public void DashOverspeedKeepsTheShippedCoastAndPivot(float input, float initial, float expected)
    {
        var sim = Create();
        var state = sim.GetState(1);
        state.VX = initial;
        sim.RegisterEntity(1, TestHelpers.EngineDef, state);
        Step(sim, input);
        Assert.Equal(expected, sim.GetState(1).VX, 4);
    }

    [Fact]
    public void ReservedRushAndLegacyAccelerationStatsDoNotAffectGroundMovement()
    {
        var sim = Create();
        var reference = Create();
        var state = sim.GetState(1);
        state.RushTicks = 600;
        var def = TestHelpers.EngineDef;
        var movement = def.Movement;
        movement.RushTicks = 1;
        movement.RunAccelerationA = movement.RunAccelerationB = 10000f;
        def = def with { Movement = movement };
        sim.RegisterEntity(1, def, state);
        for (int tick = 0; tick < 40; tick++)
        {
            float input = tick < 15 ? 1f : tick < 20 ? -1f : 0f;
            Step(sim, input); Step(reference, input);
            Assert.Equal(reference.GetState(1).VX, sim.GetState(1).VX);
            Assert.Equal((ushort)600, sim.GetState(1).RushTicks);
        }
    }

    [Fact]
    public void SnapshotRestoreRepeatsEveryRampTurnAndStopByte()
    {
        var sim = Create();
        Step(sim, 1f);
        var saved = sim.CaptureSnapshot();
        var expected = new List<byte[]>();
        for (int tick = 0; tick < 60; tick++)
        {
            Step(sim, tick < 12 ? 1f : tick < 20 ? -1f : 0f);
            expected.Add(sim.CaptureSnapshot().Data);
        }
        sim.RestoreSnapshot(saved);
        for (int tick = 0; tick < 60; tick++)
        {
            Step(sim, tick < 12 ? 1f : tick < 20 ? -1f : 0f);
            Assert.Equal(expected[tick], sim.CaptureSnapshot().Data);
        }
    }

    private static ServerSimulation Create()
    {
        var def = TestHelpers.EngineDef;
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, def, new CharacterState
        {
            PX = 30f, PZ = 30f, PY = def.CapsuleHeight * 0.5f, IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
        });
        return sim;
    }

    private static float Speed(CharacterState state) => MathF.Sqrt(state.VX * state.VX + state.VZ * state.VZ);
    private static void Step(ServerSimulation sim, float x, float y = 0f)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { MoveX = x, MoveY = y } });
}
