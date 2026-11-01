using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class GrabBeelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LatestPressFiresOnTickAfterGroundArrival(bool attackLast)
    {
        var sim = Duel();
        Tick(sim, new() { DashPressed = true });
        Tick(sim, new() { ActiveSlot = attackLast ? (byte)0 : AbilitySlots.Normal1, GrabPressed = attackLast });
        Tick(sim, new() { ActiveSlot = attackLast ? AbilitySlots.Normal1 : (byte)0, GrabPressed = !attackLast });
        Arrive(sim);
        Tick(sim);
        Assert.Equal(attackLast ? ActionState.Attacking : ActionState.GrabAttempt, sim.GetState(1).State);
        Assert.Equal(0, sim.GetState(1).BufferedSlot);
    }

    [Fact]
    public void SameTickAttackBeatsGrabAndFreshRejectedArrivalAttackDiscardsOlderPress()
    {
        var sim = Duel();
        Tick(sim, new() { DashPressed = true });
        Tick(sim, new() { GrabPressed = true, ActiveSlot = AbilitySlots.Normal1 });
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(1).BufferedSlot);
        Arrive(sim);
        var state = sim.GetState(1);
        state.SetCooldown(AbilitySlots.Normal2, 100);
        sim.SetState(1, state);
        Tick(sim, new() { ActiveSlot = AbilitySlots.Normal2 });
        Assert.Equal(0, sim.GetState(1).BufferedSlot);
        Tick(sim);
        Assert.NotEqual(ActionState.Attacking, sim.GetState(1).State);
    }

    [Fact]
    public void AirArrivalDropsGrabWithoutLettingMarkerReachSlotLookup()
    {
        var sim = Duel();
        Tick(sim, new() { DashPressed = true });
        Tick(sim, new() { GrabPressed = true });
        Arrive(sim);
        var s = sim.GetState(1);
        s.IsGrounded = false;
        s.PY += 3f;
        sim.SetState(1, s);
        Tick(sim);
        Assert.Equal(0, sim.GetState(1).BufferedSlot);
        for (int i = 0; i < 150; i++)
        {
            Tick(sim);
            Assert.NotEqual(ActionState.GrabAttempt, sim.GetState(1).State);
        }
    }

    [Fact]
    public void PressOnArrivalMovementTickWaitsUntilFollowingTickAndRestores()
    {
        var sim = Duel();
        Tick(sim, new() { DashPressed = true });
        var before = sim.CaptureSnapshot();
        for (int i = 0; i < 100 && sim.GetState(1).State != ActionState.HomingDashArrive; i++)
        { before = sim.CaptureSnapshot(); Tick(sim); }
        Assert.Equal(ActionState.HomingDashArrive, sim.GetState(1).State);
        sim.RestoreSnapshot(before);
        Tick(sim, new() { GrabPressed = true });
        Assert.Equal(ActionState.HomingDashArrive, sim.GetState(1).State);
        var arrival = sim.CaptureSnapshot();
        Tick(sim);
        Assert.Equal(ActionState.GrabAttempt, sim.GetState(1).State);
        var expected = sim.CaptureSnapshot();
        sim.RestoreSnapshot(arrival);
        Tick(sim);
        Assert.True(expected.ContentEquals(sim.CaptureSnapshot()));
    }

    [Fact]
    public void AcceptedHitOnArrivalClearsHeldGrabBeforeQueuedLaunch()
    {
        var sim = Duel();
        Tick(sim, new() { DashPressed = true });
        Tick(sim, new() { GrabPressed = true });
        var before = sim.CaptureSnapshot();
        for (int i = 0; i < 100 && sim.GetState(1).State != ActionState.HomingDashArrive; i++)
        { before = sim.CaptureSnapshot(); Tick(sim); }
        var arrived = sim.GetState(1);
        sim.RestoreSnapshot(before);
        sim.Resolver.Spawn(new Hitbox { OwnerId = 100, X = arrived.PX, Y = arrived.PY, Z = arrived.PZ,
            Radius = 0.1f, Damage = 6, StunTicks = 1, BaseKnockback = 30, DurationTicks = 1 });
        Tick(sim);
        Assert.Equal(0, sim.GetState(1).BufferedSlot);
        Assert.True(sim.GetState(1).HitstopTicks > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeldGrabReplaysIdenticallyFromFloatOrTravel(bool travel)
    {
        var sim = Duel();
        Tick(sim, new() { DashPressed = true });
        if (travel)
            for (int i = 0; i < 30 && sim.GetState(1).State != ActionState.HomingDash; i++) Tick(sim);
        Tick(sim, new() { GrabPressed = true });
        var start = sim.CaptureSnapshot();
        var expected = new List<WorldSnapshot>();
        for (int i = 0; i < 70; i++) { Tick(sim); expected.Add(sim.CaptureSnapshot()); }
        sim.RestoreSnapshot(start);
        foreach (var snapshot in expected) { Tick(sim); Assert.True(snapshot.ContentEquals(sim.CaptureSnapshot())); }
    }

    private static ServerSimulation Duel()
    {
        var sim = new ServerSimulation(TestHelpers.TestArena());
        var def = TestHelpers.EngineDef;
        var s = TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(def) };
        sim.RegisterEntity(1, def, s);
        sim.RegisterEntity(2, def, s with { EntityId = 2, PZ = 28f });
        Tick(sim);
        return sim;
    }
    private static void Tick(ServerSimulation sim, InputState input = default)
        => sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
    private static void Arrive(ServerSimulation sim)
    {
        for (int i = 0; i < 100 && sim.GetState(1).State != ActionState.HomingDashArrive; i++) Tick(sim);
        Assert.Equal(ActionState.HomingDashArrive, sim.GetState(1).State);
    }
}
