using System;
using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class DownActionTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;
    private static float Run => Def.Movement.RunSpeed;
    private static ServerSimulation Ground(float vx = 0, float vz = 0, ActionState action = ActionState.Idle)
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(50, 50);
        state.PY = Def.CapsuleHeight * .5f;
        state.VX = vx; state.VZ = vz; state.State = action;
        sim.RegisterEntity(1, Def, state);
        return sim;
    }
    private static CharacterState Tick(ServerSimulation sim, InputState input)
    {
        sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
        return sim.GetState(1);
    }
    private static float Speed(CharacterState s) => MathF.Sqrt(s.VX * s.VX + s.VZ * s.VZ);
    private static InputState Hold => new() { Down = true };
    private static InputState Press => new() { Down = true, DownPressed = true };

    [Fact]
    public void Crouch_RequiresEstablishedStationaryTickForSettlement()
    {
        var sim = Ground();
        var entered = Tick(sim, Hold);
        Assert.Equal(ActionState.Crouching, entered.State);
        Assert.False(entered.CrouchSettled);
        Assert.True(Tick(sim, Hold).CrouchSettled);
        var released = Tick(sim, new InputState { MoveX = 1 });
        Assert.Equal(ActionState.Run, released.State);
        Assert.False(released.CrouchSettled);
        TestHelpers.AssertNear(GroundSpeedTuning.StartRatio * Run, released.VX);
    }

    [Theory]
    [InlineData(-.001f, false)]
    [InlineData(0f, true)]
    [InlineData(.001f, true)]
    public void GroundEntry_UsesActualSpeedAndInclusiveThreshold(float offset, bool slide)
    {
        var sim = Ground(Run * DownActionTuning.GroundEntryRatio + offset);
        var state = Tick(sim, Press);
        Assert.Equal(slide ? ActionState.Sliding : ActionState.Crouching, state.State);
        Assert.False(state.CrouchSettled);
    }

    [Fact]
    public void HeldDownWithoutEdge_DoesNotStartSlideOrAccelerateCrouch()
    {
        var sim = Ground(Run);
        var state = Tick(sim, new InputState { Down = true, MoveY = 1 });
        Assert.Equal(ActionState.Crouching, state.State);
        TestHelpers.AssertNear(Run - 36f / 60f, state.VX);
        Assert.Equal(0, state.VZ);
        for (int i = 0; i < 60; i++) state = Tick(sim, new InputState { Down = true, MoveY = 1 });
        Assert.Equal(ActionState.Crouching, state.State);
        Assert.Equal(0, Speed(state));
        Assert.True(state.CrouchSettled);
    }

    [Fact]
    public void BrakingCrouch_DoesNotSettleOnItsStoppingTick()
    {
        var sim = Ground(.3f, .4f, ActionState.Crouching);
        var stopped = Tick(sim, Hold);
        Assert.Equal(0, Speed(stopped));
        Assert.False(stopped.CrouchSettled);
        Assert.True(Tick(sim, Hold).CrouchSettled);
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(.6f, .8f)]
    public void Slide_CapsMagnitudeAndIntegratesDecayedVelocityOnce(float x, float z)
    {
        var sim = Ground(x * 2f * Run, z * 2f * Run);
        var state = Tick(sim, new InputState { Down = true, DownPressed = true, MoveX = -1, FaceToCamera = true, AimYaw = 9000 });
        float expected = Run * (DownActionTuning.EntryCapRatio - DownActionTuning.Default.SlideDecelerationRatio / 60f);
        Assert.Equal(ActionState.Sliding, state.State);
        TestHelpers.AssertNear(x * expected, state.VX);
        TestHelpers.AssertNear(z * expected, state.VZ);
        TestHelpers.AssertNear(50 + state.VX / 60f, state.PX);
        TestHelpers.AssertNear(50 + state.VZ / 60f, state.PZ);
        float previous = Speed(state);
        for (int i = 0; i < 80; i++)
        {
            state = Tick(sim, new InputState { Down = true, MoveX = -1 });
            Assert.True(Speed(state) <= previous + .00001f);
            Assert.True(state.VX >= 0 && state.VZ >= 0);
            previous = Speed(state);
        }
        Assert.Equal(ActionState.Crouching, state.State);
        Assert.Equal(0, previous);
    }

    [Fact]
    public void ShortTap_EntersForSampledTickThenReleaseSteersImmediately()
    {
        var sim = Ground(Run, action: ActionState.Run);
        var slide = Tick(sim, new InputState { DownPressed = true });
        Assert.Equal(ActionState.Sliding, slide.State);
        var released = Tick(sim, new InputState { MoveY = 1 });
        Assert.Equal(ActionState.Run, released.State);
        TestHelpers.AssertNear(0, released.VX);
        TestHelpers.AssertNear(Run, released.VZ);
    }

    [Fact]
    public void Slide_PreservesLastDirection_AndRefreshesGroundResources()
    {
        var sim = Ground(Run, action: ActionState.Run);
        var state = sim.GetState(1);
        state.LastDirX = 1; state.LastDirZ = 0;
        state.JumpsLeft = 0; state.AirDodgesLeft = 0; state.Cooldown1 = 20;
        sim.SetState(1, state);
        state = Tick(sim, Press);
        state = Tick(sim, new InputState { Down = true, MoveY = -1 });
        Assert.Equal(1, state.LastDirX);
        Assert.Equal(0, state.LastDirZ);
        Assert.Equal(Def.Movement.MaxJumps, state.JumpsLeft);
        Assert.Equal(1, state.AirDodgesLeft);
        Assert.Equal(18, state.Cooldown1);
    }

    [Fact]
    public void LockedPress_IsNotBankedAsSlide()
    {
        var sim = Ground(Run);
        var state = sim.GetState(1); state.LandingLagTicks = 4; sim.SetState(1, state);
        Assert.NotEqual(ActionState.Sliding, Tick(sim, Press).State);
        for (int i = 0; i < 8; i++) state = Tick(sim, Hold);
        Assert.Equal(ActionState.Crouching, state.State);
    }

[Fact]
public void AcceptedJump_WinsOverLowPosture()
{
    var sim = Ground(Run, action: ActionState.Sliding);
    var state = Tick(sim, new InputState { Down = true, DownPressed = true, Jump = true, JumpHeld = true, MoveX = 1 });
    Assert.Equal(ActionState.JumpSquat, state.State);
    Assert.False(state.CrouchSettled);
    Assert.Contains(1UL, sim.LastTickAcceptedActions);
    byte remaining = state.JumpsLeft;
    state = Tick(sim, new InputState { Down = true, JumpHeld = true });
    Assert.Equal(remaining, state.JumpsLeft);
}

    [Fact]
    public void GroundNormal_IsAcceptedFromSettledCrouchImmediately()
    {
        var sim = Ground(); Tick(sim, Hold); Assert.True(Tick(sim, Hold).CrouchSettled);
        var state = Tick(sim, new InputState { Down = true, ActiveSlot = AbilitySlots.Slot1 });
        Assert.Equal(ActionState.Attacking, state.State);
        Assert.False(state.CrouchSettled);
        Assert.Contains(1UL, sim.LastTickAcceptedActions);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    public void HeldTouchdown_PreservesContactVectorWithoutExtraStep(float floor)
    {
        ServerSimulation Falling()
        {
            var sim = new ServerSimulation(TestHelpers.TestArena(floor));
            var s = TestHelpers.PlayerState(50, 50);
            s.PY = floor + Def.CapsuleHeight * .5f + .05f;
            s.IsGrounded = false; s.VY = -10; s.VX = Run; s.JumpsLeft = 0;
            sim.RegisterEntity(1, Def, s);
            return sim;
        }
        var held = Falling(); var control = Falling();
        var landed = Tick(held, Hold); var ordinary = Tick(control, default);
        Assert.Contains(1UL, held.LastTickTouchdowns);
        Assert.Equal(ActionState.Sliding, landed.State);
        Assert.NotEqual(ActionState.Sliding, ordinary.State);
        TestHelpers.AssertNear(ordinary.PX, landed.PX);
        TestHelpers.AssertNear(ordinary.VX, landed.VX);
        Assert.False(landed.IsFastFalling);
        Assert.Equal(Def.Movement.MaxJumps, landed.JumpsLeft);
        var next = Tick(held, Hold);
        TestHelpers.AssertNear(landed.VX - DownActionTuning.Default.SlideDecelerationRatio * Run / 60f, next.VX);
        TestHelpers.AssertNear(landed.PX + next.VX / 60f, next.PX);
        Assert.Empty(held.LastTickTouchdowns);
    }

    [Theory]
    [InlineData(-.001f, false)]
    [InlineData(0f, true)]
    [InlineData(.001f, true)]
    public void LandingEntry_UsesInclusivePostAirFrictionThreshold(float offset, bool slide)
    {
        var sim = Ground();
        var s = sim.GetState(1);
        s.IsGrounded = false; s.PY += .05f; s.VY = -10;
        s.VX = Run * DownActionTuning.LandingEntryRatio + Def.Movement.AirFriction / 60f + offset;
        sim.SetState(1, s);
        s = Tick(sim, Hold);
        Assert.Equal(slide ? ActionState.Sliding : ActionState.Crouching, s.State);
        Assert.False(s.CrouchSettled);
    }

    [Fact]
    public void StationaryTouchdown_HasNoSlidePropulsion()
    {
        var sim = Ground();
        var s = sim.GetState(1); s.IsGrounded = false; s.PY += .05f; s.VY = -10;
        sim.SetState(1, s);
        s = Tick(sim, Hold);
        Assert.Equal(ActionState.Crouching, s.State);
        Assert.Equal(0, Speed(s));
        Assert.False(s.CrouchSettled);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void SlideJump_UsesOrdinarySquatAndVerticalForce_WithMagnitudeCap(bool slide, bool fullHop)
    {
        var sim = Ground(Run * 1.5f, Run * 1.5f, slide ? ActionState.Sliding : ActionState.Run);
        var state = Tick(sim, new InputState { Jump = true, JumpHeld = true, Down = true });
        Assert.Equal(ActionState.JumpSquat, state.State);
        Assert.Equal(slide ? JumpTakeoff.Slide : JumpTakeoff.Standing, state.JumpTakeoff);
        byte jumps = state.JumpsLeft;
        int squatTicks = 0;
        while (state.IsGrounded && squatTicks++ < 20)
        {
            state = Tick(sim, new InputState { JumpHeld = fullHop });
            Assert.Equal(jumps, state.JumpsLeft);
        }
        Assert.False(state.IsGrounded);
        Assert.Equal(JumpTakeoff.Standing, state.JumpTakeoff);
        float cap = Run * (slide ? DownActionTuning.JumpCapRatio : 1f);
        Assert.InRange(Speed(state), cap - .3f, cap);
        float force = fullHop ? Def.Movement.JumpForce : Def.Movement.ShortHopForce;
        TestHelpers.AssertNear(force - Def.Movement.Gravity / 60f, state.VY);
        Assert.Equal(Math.Max(Def.Movement.JumpSquatTicks, fullHop ? Simulation.ShortHopWindowTicks : 0), squatTicks);
    }

    [Fact]
    public void SlideJump_NeverRefillsDecayedHorizontalSpeed()
    {
        var sim = Ground(Run * .4f, action: ActionState.Sliding);
        var s = Tick(sim, new InputState { Jump = true, JumpHeld = true });
        for (int i = 0; i < 15 && s.IsGrounded; i++)
            s = Tick(sim, new InputState { JumpHeld = true });
        Assert.False(s.IsGrounded);
        Assert.InRange(Speed(s), 0f, Run * .4f);
    }

    [Fact]
    public void DownAdmissionDiagnostics_UseActualGroundGatesAndClearPerTick()
    {
        var sim = Ground(Run);

        var accepted = Tick(sim, Press);
        Assert.Equal(DownActionAdmissionReason.Accepted, sim.LastTickDownAdmissions[1]);
        Assert.Equal(ActionState.Sliding, accepted.State);

        var released = Tick(sim, default);
        Assert.Equal(DownActionAdmissionReason.Released, sim.LastTickDownAdmissions[1]);
        Assert.Equal(ActionState.Idle, released.State);

        Tick(sim, default);
        Assert.Empty(sim.LastTickDownAdmissions);
    }

    [Fact]
    public void DownAdmissionDiagnostics_DistinguishThresholdLockAndAcceptedAction()
    {
        var below = Ground(Run * (DownActionTuning.GroundEntryRatio - .01f));
        var crouch = Tick(below, Press);
        Assert.Equal(ActionState.Crouching, crouch.State);
        Assert.Equal(DownActionAdmissionReason.BelowThreshold, below.LastTickDownAdmissions[1]);

        var locked = Ground(Run);
        var lockedState = locked.GetState(1);
        lockedState.LandingLagTicks = 3;
        locked.SetState(1, lockedState);
        Tick(locked, Press);
        Assert.Equal(DownActionAdmissionReason.Locked, locked.LastTickDownAdmissions[1]);

        var action = Ground(Run);
        Tick(action, new InputState { Down = true, Jump = true, JumpHeld = true });
        Assert.Equal(DownActionAdmissionReason.ActionAccepted, action.LastTickDownAdmissions[1]);
        Assert.Equal(ActionState.JumpSquat, action.GetState(1).State);
    }
}
