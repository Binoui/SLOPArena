using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Fast-fall is a descending-only edge that latches until interruption.</summary>
public class FastFallTests
{
    private static readonly CharacterDefinition Def = CreateClassicDef();
    private static readonly MovementStats Move = Def.Movement;

    private static CharacterDefinition CreateClassicDef()
    {
        var def = TestHelpers.EngineDef;
        def = def with { Movement = def.Movement with { FloatWindowTicks = 0 } };
        return def;
    }

    private static ServerSimulation SimFalling(float py = 15f, float vy = -5f)
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = py;
        state.VY = vy;
        state.IsGrounded = false;
        state.AirTimeTicks = 60;
        TestHelpers.RegisterPlayer(sim, Def, state);
        return sim;
    }

    private static InputState DownEdge(bool held = true)
    {
        var input = TestHelpers.Input(down: held);
        input.DownPressed = true;
        return input;
    }

    private static void Tick(ServerSimulation sim, InputState input)
        => sim.Tick(new Dictionary<ulong, InputState> { { 1, input } });

    [Fact]
    public void RisingEdge_IsDropped()
    {
        var sim = SimFalling(vy: 4f);
        Tick(sim, DownEdge());
        var state = sim.GetState(1);

        Assert.False(state.IsFastFalling);
        Assert.True(state.VY > 0f);
    }

    [Fact]
    public void ApexEdge_IsDropped()
    {
        var sim = SimFalling(vy: 0f);
        Tick(sim, DownEdge());

        Assert.False(sim.GetState(1).IsFastFalling);
    }

    [Fact]
    public void DescendingEdge_LatchesThroughRelease()
    {
        var sim = SimFalling();
        Tick(sim, DownEdge());
        Tick(sim, default);
        var state = sim.GetState(1);

        Assert.True(state.IsFastFalling);
        TestHelpers.AssertNear(-Move.FastFallSpeed, state.VY, 0.001f);
    }

    [Fact]
    public void OrdinaryAerialAttack_CanFastFall()
    {
        var sim = SimFalling();
        var state = sim.GetState(1);
        state.State = ActionState.Attacking;
        state.AttackSlot = 1;
        state.AnimLockTicks = 10;
        sim.SetState(1, state);

        Tick(sim, DownEdge());

        Assert.True(sim.GetState(1).IsFastFalling);
    }

    [Fact]
    public void VerticalMotionOwner_BlocksEdge()
    {
        var state = TestHelpers.PlayerState();
        state.PY = 15f;
        state.VY = -5f;
        state.IsGrounded = false;
        state.AirTimeTicks = 60;

        Simulation.SimulateTick(ref state, Def, DownEdge(), TestHelpers.TestArena(),
            out _, out _, DownActionTuning.Default, verticalMotionOwned: true);

        Assert.False(state.IsFastFalling);
        Assert.True(state.VY > -Move.FastFallSpeed);
    }

    [Fact]
    public void Hitstop_DropsFreshEdge_ButPreservesExistingLatch()
    {
        var dropped = SimFalling();
        var droppedState = dropped.GetState(1);
        droppedState.HitstopTicks = 2;
        dropped.SetState(1, droppedState);
        Tick(dropped, DownEdge());
        Tick(dropped, default);
        Assert.False(dropped.GetState(1).IsFastFalling);

        var frozen = SimFalling();
        var frozenState = frozen.GetState(1);
        frozenState.HitstopTicks = 1;
        frozenState.IsFastFalling = true;
        frozen.SetState(1, frozenState);
        Tick(frozen, default);
        Assert.True(frozen.GetState(1).IsFastFalling);
        Tick(frozen, default);
        TestHelpers.AssertNear(-Move.FastFallSpeed, frozen.GetState(1).VY, 0.001f);
    }

[Fact]
public void AcceptedJump_ResetsMovementInterruptions()
{
    var jump = TestHelpers.PlayerState();
    jump.PY = TestHelpers.GroundPY(Def);
    jump.IsFastFalling = true;
    jump.JumpTakeoff = JumpTakeoff.Slide;
    jump.SlideAttackCarryActive = true;
    jump.CrouchSettled = true;
    Simulation.SimulateTick(ref jump, Def, TestHelpers.Input(jump: true),
        TestHelpers.TestArena(), out _, out bool jumpAccepted,
        DownActionTuning.Default, false);
    Assert.True(jumpAccepted);
    Assert.False(jump.IsFastFalling);
    Assert.Equal(JumpTakeoff.Standing, jump.JumpTakeoff);
    Assert.False(jump.SlideAttackCarryActive);
    Assert.False(jump.CrouchSettled);
}

    [Fact]
    public void AcceptedHit_ResetsFastFallAndInterruptionFlags()
    {
        var state = SimFalling().GetState(1);
        state.IsFastFalling = true;
        state.JumpTakeoff = JumpTakeoff.Slide;
        state.SlideAttackCarryActive = true;
        state.CrouchSettled = true;
        state.QueuedCrouchBrace = true;

        Simulation.ApplyKnockback(ref state, 0f, 1f, 45, 8f, 0f, 1f, 10, Def.Weight);

        Assert.False(state.IsFastFalling);
        Assert.Equal(JumpTakeoff.Standing, state.JumpTakeoff);
        Assert.False(state.SlideAttackCarryActive);
        Assert.False(state.CrouchSettled);
        Assert.False(state.QueuedCrouchBrace);
    }
}
