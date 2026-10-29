using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class GrabAimTests
{
    [Theory]
    [InlineData(60f, 60f)]
    [InlineData(135f, 84f)]
    public void StartupTurnsForSevenTicksAndCaptureHeadingFollows(float bearing, float expected)
    {
        var sim = Duel(bearing);
        for (int tick = 0; tick < 7; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { GrabPressed = tick == 0 } });
            TestHelpers.AssertNear(MathF.Min(expected, (tick + 1) * 12f), sim.GetState(1).CapturedYaw / 100f, 0.02f);
        }
        sim.Tick(new());
        Assert.False(sim.GetState(1).AttackCorrectionOwned);
        if (bearing == 60f) Assert.Equal(ActionState.Grabbing, sim.GetState(1).State);
        else Assert.Equal((short)8400, sim.GetState(1).CapturedYaw);
    }

    [Fact]
    public void TargetIsCapturedOnceAndLastFrozenTickDoesNotTurn()
    {
        var sim = Duel(135f);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { GrabPressed = true } });
        var s = sim.GetState(1);
        s.HitstopTicks = 1;
        sim.SetState(1, s);
        sim.Tick(new());
        Assert.Equal(s.CapturedYaw, sim.GetState(1).CapturedYaw);
        var victim = sim.GetState(2);
        victim.PX = 35f;
        sim.SetState(2, victim);
        sim.Tick(new());
        Assert.False(sim.GetState(1).AttackCorrectionActive);
        victim.PX = 20.8f;
        sim.SetState(2, victim);
        sim.Tick(new());
        Assert.Equal(s.CapturedYaw, sim.GetState(1).CapturedYaw);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreferredTargetWinsOverNearestAndCannotBeChangedAfterPress(bool locked)
    {
        var sim = Duel(-30f);
        var def = TestHelpers.EngineDef;
        var other = sim.GetState(2) with { EntityId = 3, PX = 22.8f, PZ = 21f };
        sim.RegisterEntity(3, def, other);
        var owner = sim.GetState(1);
        sim.SetState(1, owner with { LockOn = locked, TargetEntityId = locked ? 3ul : 0ul });
        sim.Tick(new() { [1] = new() { GrabPressed = true, TargetEntityId = 3 } });
        Assert.Equal(3ul, sim.GetState(1).AttackCorrectionTargetId);
        Assert.Equal((short)1200, sim.GetState(1).CapturedYaw);
        sim.Tick(new() { [1] = new() { TargetEntityId = 2, RetargetPressed = true } });
        Assert.Equal(3ul, sim.GetState(1).AttackCorrectionTargetId);
        Assert.Equal((short)2400, sim.GetState(1).CapturedYaw);
    }

    [Fact]
    public void AcquisitionUsesFourMeterSphereAndDoesNotAcquireLate()
    {
        var sim = Duel(90f);
        var target = sim.GetState(2) with { PX = 24.1f };
        sim.SetState(2, target);
        sim.Tick(new() { [1] = new() { GrabPressed = true } });
        Assert.Equal(0ul, sim.GetState(1).AttackCorrectionTargetId);
        sim.SetState(2, target with { PX = 21f });
        sim.Tick(new());
        Assert.Equal((short)0, sim.GetState(1).CapturedYaw);
    }

    [Fact]
    public void MidCorrectionRestoreReplaysCapturedTargetAndHeading()
    {
        var sim = Duel(135f);
        sim.Tick(new() { [1] = new() { GrabPressed = true } });
        sim.Tick(new());
        var before = sim.CaptureSnapshot();
        for (int i = 0; i < 10; i++) sim.Tick(new());
        var expected = sim.CaptureSnapshot();
        sim.RestoreSnapshot(before);
        for (int i = 0; i < 10; i++) sim.Tick(new());
        Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
    }

    private static ServerSimulation Duel(float bearing)
    {
        var def = TestHelpers.EngineDef with { CaptureGeometry = new CookedCaptureGeometry(1.5f, 0.8f, 1.2f, 0.6f,
            new CaptureAnchor(0f, 0.6f, 0.2f), new CaptureAnchor(0f, 0.6f, 0.65f)) };
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var s = TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(def) };
        sim.RegisterEntity(1, def, s);
        sim.RegisterEntity(2, def, s with { EntityId = 2,
            PX = s.PX + MathF.Sin(bearing * MathF.PI / 180f) * 0.8f,
            PZ = s.PZ + MathF.Cos(bearing * MathF.PI / 180f) * 0.8f });
        return sim;
    }
}
