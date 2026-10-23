using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class BurstTests
{
    private static InputState BurstInput(InputState input = default) => TestHelpers.ReservedInput(0x40, input);

    [Fact]
    public void SyntheticBurst_DoesNotClearHitstunOrKnockback()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        state.State = ActionState.Hitstun;
        state.HitstunTicks = 10;
        state.KVX = 3f;
        TestHelpers.RegisterPlayer(sim, TestHelpers.EngineDef, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Hitstun, after.State);
        Assert.Equal(9, after.HitstunTicks);
        Assert.InRange(after.KVX, 2.5f, 3f);
        Assert.Equal(0, after.InvincibilityTicks);
        Assert.Equal(0f, after.QueuedKBDirX);
        Assert.Equal(0f, after.QueuedKBDirZ);
        Assert.Equal(0f, after.QueuedKBBase);
    }

    [Fact]
    public void SyntheticBurst_DuringHitstopPreservesQueuedLaunchAndDoesNotCreateSideEffects()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        state.HitstopTicks = 3;
        state.QueuedKBDirX = 1f;
        state.QueuedKBAngle = 20;
        state.QueuedKBBase = 10f;
        state.QueuedKBGrowth = 5f;
        state.QueuedKBStun = 20;
        TestHelpers.RegisterPlayer(sim, TestHelpers.EngineDef, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });

        var after = sim.GetState(1);
        Assert.Equal(2, after.HitstopTicks);
        Assert.Equal(1f, after.QueuedKBDirX);
        Assert.Equal(20, after.QueuedKBAngle);
        Assert.Equal(10f, after.QueuedKBBase);
        Assert.Equal(5f, after.QueuedKBGrowth);
        Assert.Equal(20, after.QueuedKBStun);
        Assert.Equal(0f, after.KVX);
    }

    [Fact]
    public void ReservedBurstBitDoesNotLockJumpAdmission()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.EngineDef, state);

        sim.Tick(new Dictionary<ulong, InputState>
        {
            { 1, BurstInput(new InputState { Jump = true, JumpHeld = true }) },
        });

        Assert.Equal(ActionState.JumpSquat, sim.GetState(1).State);
        Assert.Contains(1UL, sim.LastTickAcceptedActions);
    }

    [Fact]
    public void SyntheticBurst_DoesNotCancelAttackOrSpawnAnAttack()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        state.State = ActionState.Attacking;
        state.AttackSlot = AbilitySlots.Slot1;
        state.AnimLockTicks = 10;
        TestHelpers.RegisterPlayer(sim, TestHelpers.EngineDef, state);

        sim.Tick(new Dictionary<ulong, InputState> { { 1, BurstInput() } });

        var after = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal(AbilitySlots.Slot1, after.AttackSlot);
        Assert.True(after.AnimLockTicks > 0);
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), hitbox => hitbox.OwnerId == 1);
    }
}
