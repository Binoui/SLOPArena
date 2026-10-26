using System;
using System.Collections.Generic;
using Xunit;
using System.IO;
using System.Security.Cryptography;

namespace SlopArena.Shared.Tests;

public class GroundSpeedPrototypeTests
{
    [Theory]
    [InlineData(GroundSpeedModel.Current, 1, 14f, 1.4f)]
    [InlineData(GroundSpeedModel.Tiers, 1, 5.6f, 1.4f)]
    [InlineData(GroundSpeedModel.Progressive, 21, 14f, 0.665f)]
    [InlineData(GroundSpeedModel.Hybrid, 6, 5.6f, 0.8166667f)]
    public void MeasuresActualTapSpeedAndRamp(GroundSpeedModel model, int cruiseTicks, float halfSpeed, float tapDistance)
    {
        var result = MovementProbe.MeasureGroundSpeed(TestHelpers.EngineDef, TestHelpers.TestArena(), GroundSpeedTuning.For(model));
        Assert.Equal(cruiseTicks, result.TimeToRunTicks);
        Assert.Equal(halfSpeed, result.HalfTiltSpeed, 4);
        Assert.Equal(tapDistance, result.Tap6.HeldDistance, 4);
        Assert.Equal(2, result.RunCurve[2].Tick);
        Assert.Equal(14f, result.RunCurve[30].Speed, 4);
        Assert.Equal(model == GroundSpeedModel.Progressive ? 20 : 24, result.RunStop.StopTicks);
        Assert.Equal(model == GroundSpeedModel.Progressive ? 2.2166667f : 2.6066667f, result.RunStop.StopDistance, 4);
        if (model is GroundSpeedModel.Tiers or GroundSpeedModel.Hybrid)
        {
            Assert.Equal(3, result.WalkStop.StopTicks);
            Assert.Equal(0.0933333f, result.WalkStop.StopDistance, 4);
        }
    }

    [Theory]
    [InlineData(GroundSpeedModel.Tiers, 1)]
    [InlineData(GroundSpeedModel.Hybrid, 3)]
    public void WalkHasNoRushAndStopsInThreeTicks(GroundSpeedModel model, int rampTicks)
    {
        var sim = Create(model);
        for (int tick = 1; tick <= 10; tick++)
        {
            Step(sim, 0.7f);
            Assert.Equal((ushort)0, sim.GetState(1).RushTicks);
            if (tick == rampTicks) Assert.Equal(5.6f, sim.GetState(1).VX, 4);
        }
        Step(sim, 0f); Assert.True(sim.GetState(1).VX > 0f);
        Step(sim, 0f); Assert.True(sim.GetState(1).VX > 0f);
        Step(sim, 0f); Assert.Equal(0f, sim.GetState(1).VX);
    }

    [Theory]
    [InlineData(GroundSpeedModel.Tiers)]
    [InlineData(GroundSpeedModel.Hybrid)]
    public void RunKeepsRushReversalAndTapRelease(GroundSpeedModel model)
    {
        var sim = Create(model);
        Step(sim, 1f); Step(sim, -1f);
        Assert.Equal(-14f, sim.GetState(1).VX, 4);
        Assert.True(sim.GetState(1).RushTicks > 0);
        Step(sim, 0f);
        Assert.Equal(0f, sim.GetState(1).VX);
    }

    [Theory]
    [InlineData(GroundSpeedCurve.Linear, 6, 6)]
    [InlineData(GroundSpeedCurve.Linear, 10, 10)]
    [InlineData(GroundSpeedCurve.Linear, 20, 20)]
    [InlineData(GroundSpeedCurve.Linear, 30, 30)]
    [InlineData(GroundSpeedCurve.Linear, 40, 40)]
    [InlineData(GroundSpeedCurve.SlowStart, 6, 7)]
    [InlineData(GroundSpeedCurve.SlowStart, 10, 11)]
    [InlineData(GroundSpeedCurve.SlowStart, 20, 21)]
    [InlineData(GroundSpeedCurve.SlowStart, 30, 31)]
    [InlineData(GroundSpeedCurve.SlowStart, 40, 41)]
    public void ProgressiveReachesCruiseOnTheExpectedTick(GroundSpeedCurve curve, int ramp, int expected)
    {
        var result = MovementProbe.MeasureProgressive(TestHelpers.EngineDef, TestHelpers.TestArena(),
            GroundSpeedTuning.Progressive(ramp, curve, startRatio: 0f));
        Assert.Equal(expected, result.TimeToRunTicks);
        Assert.True(result.RunCurve[expected - 1].Speed < result.RunSpeed);
        Assert.Equal(result.RunSpeed, result.RunCurve[expected].Speed);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(1f)]
    public void HeldAndCoastingReversalKeepTheSameFractionOfVelocity(float keep)
    {
        foreach (var curve in new[] { GroundSpeedCurve.Linear, GroundSpeedCurve.SlowStart })
        {
            var tuning = GroundSpeedTuning.Progressive(20, curve, keep, startRatio: 0f);
            var held = Create(tuning);
            var coast = Create(tuning);
            var state = held.GetState(1);
            state.VX = 7f; state.LastDirX = 1f;
            held.RegisterEntity(1, TestHelpers.EngineDef, state);
            state.LastDirX = 0f;
            coast.RegisterEntity(1, TestHelpers.EngineDef, state);
            Step(held, -1f); Step(coast, -1f);
            float previous = 7f * keep;
            float accel = 0.7f * (curve == GroundSpeedCurve.Linear ? 1f : 1.6094379f * (0.25f + previous / 14f));
            Assert.Equal(-(previous + accel), held.GetState(1).VX, 5);
            Assert.Equal(held.CaptureSnapshot().Data, coast.CaptureSnapshot().Data);
            Assert.Equal(-MathF.PI / 2f, held.GetState(1).FacingYaw, 5);
        }
    }

    [Fact]
    public void ProgressiveIgnoresTiltAndKeepsSpeedWhenTurningPerpendicularly()
    {
        var sim = Create(GroundSpeedTuning.Progressive(startRatio: 0f));
        var full = Create(GroundSpeedTuning.Progressive(startRatio: 0f));
        for (int tick = 0; tick < 10; tick++) { Step(sim, 0.2f); Step(full, 1f); }
        Assert.Equal(full.CaptureSnapshot().Data, sim.CaptureSnapshot().Data);
        Step(sim, 0f, 1f);
        Assert.Equal(7.7f, sim.GetState(1).VZ, 4);
        Assert.Equal(0f, sim.GetState(1).VX);
        Step(sim, 0f, -1f);
        Assert.Equal(-8.4f, sim.GetState(1).VZ, 4);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(30)]
    public void ProgressiveReleaseBrakesTheVectorInStopTicks(int ticks)
    {
        var sim = Create(GroundSpeedTuning.Progressive(stopTicks: ticks));
        for (int tick = 0; tick < 30; tick++) Step(sim, 0.6f, 0.8f);
        for (int tick = 1; tick <= ticks; tick++)
        {
            Step(sim, 0f);
            var state = sim.GetState(1);
            float expected = 14f * (1f - (float)tick / ticks);
            Assert.Equal(expected * 0.6f, state.VX, 4);
            Assert.Equal(expected * 0.8f, state.VZ, 4);
            if (tick < ticks) Assert.True(state.VX > 0f && state.VZ > 0f);
        }
        Assert.Equal(0f, sim.GetState(1).VX);
        Assert.Equal(0f, sim.GetState(1).VZ);
    }

    [Fact]
    public void ProgressiveKeepsShippedDashCoastAboveRunSpeed()
    {
        foreach (float input in new[] { 0f, 1f, -1f })
        {
            var current = Create(GroundSpeedModel.Current);
            var progressive = Create(GroundSpeedModel.Progressive);
            var state = current.GetState(1);
            state.VX = 20f; state.State = ActionState.Run; state.LastDirX = 1f;
            current.RegisterEntity(1, TestHelpers.EngineDef, state);
            progressive.RegisterEntity(1, TestHelpers.EngineDef, state);
            Step(current, input); Step(progressive, input);
            Assert.Equal(current.GetState(1).VX, progressive.GetState(1).VX);
        }
    }

    [Fact]
    public void ProgressiveCanChangeTuningBetweenTicks()
    {
        var sim = Create(GroundSpeedTuning.Progressive(20, startRatio: 0f));
        Step(sim, 1f);
        Assert.Equal(0.7f, sim.GetState(1).VX, 5);
        sim.SetGroundSpeedTuning(GroundSpeedTuning.Progressive(10, stopTicks: 4, startRatio: 0f));
        Step(sim, 1f);
        Assert.Equal(2.1f, sim.GetState(1).VX, 5);
        Step(sim, 0f);
        Assert.Equal(0f, sim.GetState(1).VX);
    }

    [Fact]
    public void ProgressiveValidatesEveryParameter()
    {
        foreach (int value in new[] { -1, 0, 5, 41, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundSpeedTuning.Progressive(rampTicks: value));
        foreach (int value in new[] { -1, 0, 3, 31, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundSpeedTuning.Progressive(stopTicks: value));
        foreach (float value in new[] { -0.25f, 0.1f, 1.25f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundSpeedTuning.Progressive(turnaroundKeep: value));
        foreach (float value in new[] { -0.1f, 0.1f, 0.25f, 0.7f, 1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundSpeedTuning.Progressive(startRatio: value));
        Assert.Equal(0.4f, GroundSpeedTuning.Progressive().StartRatio);
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundSpeedTuning.Progressive(curve: (GroundSpeedCurve)2));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundSpeedTuning.For((GroundSpeedModel)4));
        Assert.Same(GroundSpeedTuning.Default, GroundSpeedTuning.For(GroundSpeedModel.Current));
        Assert.Equal(GroundSpeedTuning.For(GroundSpeedModel.Progressive), GroundSpeedTuning.Progressive());
        GroundSpeedTuning.Progressive(6, GroundSpeedCurve.SlowStart, 0f, 4);
        GroundSpeedTuning.Progressive(40, GroundSpeedCurve.Linear, 1f, 30);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.2f)]
    [InlineData(0.3f)]
    [InlineData(0.4f)]
    [InlineData(0.5f)]
    [InlineData(0.6f)]
    public void ProgressiveStartsImmediatelyThenRampsAcrossTheRemainingSpan(float start)
    {
        var sim = Create(GroundSpeedTuning.Progressive(startRatio: start));
        int fullTick = start == 0f ? 20 : 21;
        for (int tick = 1; tick <= fullTick; tick++)
        {
            Step(sim, 0.2f);
            float expected = start == 0f ? 14f * tick / 20f
                : 14f * (start + (1f - start) * (tick - 1) / 20f);
            Assert.Equal(expected, sim.GetState(1).VX, 4);
            if (tick < fullTick) Assert.True(sim.GetState(1).VX < 14f);
        }
        Assert.Equal(14f, sim.GetState(1).VX);
    }

    [Theory]
    [InlineData(GroundSpeedCurve.Linear)]
    [InlineData(GroundSpeedCurve.SlowStart)]
    public void ProgressiveStartFloorAppliesAfterBrakingAndToHeldOrCoastingReversals(GroundSpeedCurve curve)
    {
        foreach (float keep in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        foreach (float lastDir in new[] { 0f, 1f })
        {
            var sim = Create(GroundSpeedTuning.Progressive(curve: curve, turnaroundKeep: keep));
            var state = sim.GetState(1);
            state.VX = 4f;
            state.LastDirX = lastDir;
            sim.RegisterEntity(1, TestHelpers.EngineDef, state);
            Step(sim, -1f);
            Assert.Equal(-5.6f, sim.GetState(1).VX, 5);
        }
        var forward = Create(GroundSpeedTuning.Progressive(curve: curve));
        Step(forward, 1f);
        Assert.Equal(5.6f, forward.GetState(1).VX, 5);
        Step(forward, 0f);
        Assert.True(forward.GetState(1).VX < 5.6f);
        Step(forward, 1f);
        Assert.Equal(5.6f, forward.GetState(1).VX, 5);
    }

    [Theory]
    [InlineData(0f, 5.6f)]
    [InlineData(0.25f, 5.6f)]
    [InlineData(0.5f, 7.42f)]
    [InlineData(0.75f, 10.92f)]
    [InlineData(1f, 14f)]
    public void ProgressiveReversalRetainsSpeedAboveTheStartFloor(float keep, float expected)
    {
        var sim = Create(GroundSpeedTuning.Progressive(turnaroundKeep: keep));
        for (int tick = 0; tick < 21; tick++) Step(sim, 1f);
        Assert.Equal(14f, sim.GetState(1).VX);
        Step(sim, -1f);
        Assert.Equal(-expected, sim.GetState(1).VX, 4);
    }

    [Theory]
    [InlineData(GroundSpeedCurve.Linear, 21)]
    [InlineData(GroundSpeedCurve.SlowStart, 22)]
    public void ProgressiveDefaultCurvesReachFullSpeedAfterTheStart(GroundSpeedCurve curve, int fullTick)
    {
        var result = MovementProbe.MeasureProgressive(TestHelpers.EngineDef, TestHelpers.TestArena(),
            GroundSpeedTuning.Progressive(curve: curve));
        Assert.Equal(5.6f, result.RunCurve[1].Speed, 5);
        Assert.Equal(fullTick, result.TimeToRunTicks);
        Assert.Equal(14f, result.RunCurve[fullTick].Speed);
    }

    [Theory]
    [InlineData(GroundSpeedCurve.Linear, "7473E87B438AFE40C0220A3E14AD2F60024082F21BDA92B35CC8D2E8CE469C31")]
    [InlineData(GroundSpeedCurve.SlowStart, "B09C10982C53EB650C4410ABB646FDA38B4F3C2740B6121F7DD1A2D15831A466")]
    public void ZeroStartRetainsPreviousProgressiveSnapshotBytes(GroundSpeedCurve curve, string expected)
    {
        var def = BuiltInContentResolver.Resolve(CharacterClass.FightGuy).Definition;
        var arena = new ArenaDefinition
        {
            Name = "baseline", KillHeight = -20f,
            Heightmap = new ArenaHeightmap { Data = new float[40000], Width = 200, Height = 200,
                CellSize = 1f, OriginX = -100f, OriginZ = -100f },
        };
        using var combined = new MemoryStream();
        foreach (int ramp in new[] { 6, 20, 40 })
        foreach (float keep in new[] { 0f, 0.5f, 1f })
        {
            var sim = new ServerSimulation(arena,
                groundSpeedTuning: GroundSpeedTuning.Progressive(ramp, curve, keep, startRatio: 0f));
            sim.RegisterEntity(1, def, new CharacterState { EntityId = 1, PY = def.CapsuleHeight / 2,
                IsGrounded = true, JumpsLeft = 2, AirDodgesLeft = 1 });
            var inputs = new Dictionary<ulong, InputState>();
            for (int tick = 0; tick < 600; tick++)
            {
                int phase = tick % 120;
                inputs[1] = new InputState
                {
                    MoveX = phase < 12 ? 0.5f : phase < 35 ? 1f : phase < 45 ? -1f : phase < 65 ? 0.70710677f : 0f,
                    MoveY = phase >= 45 && phase < 65 ? 0.70710677f : 0f,
                    Jump = phase == 70, JumpHeld = phase >= 70 && phase < 80,
                    Down = phase >= 100 && phase < 110, DownPressed = phase == 100,
                    ShieldHeld = phase >= 90 && phase < 95, ShieldPressed = phase == 90,
                };
                sim.Tick(inputs);
                combined.Write(sim.CaptureSnapshot().Data);
            }
        }
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(combined.ToArray())));
    }

    [Fact]
    public void CurrentRetainsTheOriginal600TickSnapshotHash()
    {
        var def = BuiltInContentResolver.Resolve(CharacterClass.FightGuy).Definition;
        var arena = new ArenaDefinition { Name = "baseline", KillHeight = -20f,
            Heightmap = new ArenaHeightmap { Data = new float[40000], Width = 200, Height = 200,
                CellSize = 1f, OriginX = -100f, OriginZ = -100f } };
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, new CharacterState { EntityId = 1, PY = def.CapsuleHeight / 2,
            IsGrounded = true, JumpsLeft = 2, AirDodgesLeft = 1 });
        var inputs = new Dictionary<ulong, InputState>();
        using var bytes = new MemoryStream();
        for (int tick = 0; tick < 600; tick++)
        {
            int phase = tick % 120;
            inputs[1] = new InputState
            {
                MoveX = phase < 12 ? 0.5f : phase < 35 ? 1f : phase < 45 ? -1f : phase < 65 ? 0.70710677f : 0f,
                MoveY = phase >= 45 && phase < 65 ? 0.70710677f : 0f,
                Jump = phase == 70, JumpHeld = phase >= 70 && phase < 80,
                Down = phase >= 100 && phase < 110, DownPressed = phase == 100,
                ShieldHeld = phase >= 90 && phase < 95, ShieldPressed = phase == 90,
            };
            sim.Tick(inputs); bytes.Write(sim.CaptureSnapshot().Data);
        }
        Assert.Equal("B2DCFF8A09DC2EB209BFAB054CEAA613E0740FCA207664BE11AF1BD2D6F8D032",
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
    }

    [Theory]
    [InlineData(GroundSpeedModel.Tiers)]
    [InlineData(GroundSpeedModel.Hybrid)]
    public void CrossingTiltThresholdStartsRunRush(GroundSpeedModel model)
    {
        var sim = Create(model);
        for (int tick = 0; tick < 10; tick++) Step(sim, 0.7f);
        Step(sim, 0.7001f);
        Assert.True(sim.GetState(1).RushTicks > 0);
        Step(sim, 0f);
        Assert.Equal(0f, sim.GetState(1).VX);
    }

    [Fact]
    public void TiersRunEntersWalkImmediately()
    {
        var sim = Create(GroundSpeedModel.Tiers);
        Step(sim, 1f);
        Step(sim, 0.5f);
        Assert.Equal(5.6f, sim.GetState(1).VX, 4);
        Assert.Equal((ushort)0, sim.GetState(1).RushTicks);
    }

    [Fact]
    public void TiersFullTiltAndCurrentHaveIdenticalSnapshotBytes()
    {
        var current = Create(GroundSpeedModel.Current);
        var tiers = Create(GroundSpeedModel.Tiers);
        for (int tick = 0; tick < 120; tick++)
        {
            float x = tick < 20 ? 1f : tick < 30 ? -1f : tick < 60 ? 0f : 1f;
            Step(current, x); Step(tiers, x);
            Assert.Equal(current.CaptureSnapshot().Data, tiers.CaptureSnapshot().Data);
        }
    }

    [Theory]
    [InlineData(GroundSpeedModel.Current)]
    [InlineData(GroundSpeedModel.Tiers)]
    [InlineData(GroundSpeedModel.Progressive)]
    [InlineData(GroundSpeedModel.Hybrid)]
    public void SnapshotRestoreRepeatsEveryRampAndStopByte(GroundSpeedModel model)
    {
        var sim = Create(model);
        Step(sim, 0.5f);
        var saved = sim.CaptureSnapshot();
        var expected = new List<byte[]>();
        for (int tick = 0; tick < 40; tick++)
        {
            Step(sim, tick < 12 ? 1f : tick < 15 ? -1f : 0f);
            expected.Add(sim.CaptureSnapshot().Data);
        }
        sim.RestoreSnapshot(saved);
        for (int tick = 0; tick < 40; tick++)
        {
            Step(sim, tick < 12 ? 1f : tick < 15 ? -1f : 0f);
            Assert.Equal(expected[tick], sim.CaptureSnapshot().Data);
        }
    }

    private static ServerSimulation Create(GroundSpeedModel model)
        => Create(GroundSpeedTuning.For(model));

    private static ServerSimulation Create(GroundSpeedTuning tuning)
    {
        var def = TestHelpers.EngineDef;
        var sim = new ServerSimulation(TestHelpers.TestArena(), groundSpeedTuning: tuning);
        sim.RegisterEntity(1, def, new CharacterState
        {
            PX = 30f, PZ = 30f, PY = def.CapsuleHeight * 0.5f, IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1,
        });
        return sim;
    }

    private static void Step(ServerSimulation sim, float x, float y = 0f)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = new InputState { MoveX = x, MoveY = y } });
}
