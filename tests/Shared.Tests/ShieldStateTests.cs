using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public class ShieldStateTests
{
    private static ServerSimulation Make(CharacterState? initial = null)
    {
        var sim = TestHelpers.MakeSim();
        var def = TestHelpers.MankiDef;
        var state = initial ?? TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) };
        sim.RegisterEntity(1, def, state);
        return sim;
    }

    private static CharacterState Tick(ServerSimulation sim, InputState input = default)
    {
        sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
        return sim.GetState(1);
    }

    [Fact]
    public void GroundedShieldStopsRunAndKeepsFacingUnderMovementAndManualTurn()
    {
        var initial = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(TestHelpers.MankiDef),
            State = ActionState.Run, VX = 7f, VZ = 3f, FacingYaw = 0.4f,
        };
        var sim = Make(initial);
        var first = Tick(sim, new InputState { ShieldHeld = true, ShieldPressed = true, MoveX = -1, FaceToCamera = true, AimYaw = 18000 });
        Assert.Equal(ActionState.Shielding, first.State);
        Assert.Equal(0f, first.VX);
        Assert.Equal(0f, first.VZ);
        Assert.Equal(initial.FacingYaw, first.FacingYaw);
        for (int t = 0; t < 100; t++)
        {
            var held = Tick(sim, new InputState { ShieldHeld = true, MoveX = -1, FaceToCamera = true, AimYaw = 18000 });
            Assert.Equal(ActionState.Shielding, held.State);
            Assert.Equal(initial.FacingYaw, held.FacingYaw);
            Assert.Equal((ushort)0, held.DamagePercent);
        }
    }

    [Fact]
    public void ShieldKeepsCombatFacingWhileTargetSelectionAndLockContinue()
    {
        var def = TestHelpers.MankiDef;
        var sim = Make();
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(x: 2f) with
        {
            EntityId = 2, PY = TestHelpers.GroundPY(def),
        });
        var shielding = Tick(sim, new InputState
        {
            ShieldHeld = true, ShieldPressed = true, ToggleLock = true,
            TargetEntityId = 2, FaceToCamera = true, AimYaw = 18000,
        });
        Assert.Equal(ActionState.Shielding, shielding.State);
        Assert.Equal(0f, shielding.FacingYaw);
        Assert.Equal((ulong)2, shielding.TargetEntityId);
        Assert.True(shielding.LockOn);
        shielding = Tick(sim, new InputState
        {
            ShieldHeld = true, TargetEntityId = 2, FaceToCamera = true, AimYaw = 18000,
            MoveX = 1f,
        });
        Assert.Equal(0f, shielding.FacingYaw);
    }

    [Theory]
    [InlineData(ActionState.Run)]
    [InlineData(ActionState.Sliding)]
    public void GrabOverridesMovementAndBrakesIncomingMomentumBeforeActiveTicks(ActionState action)
    {
        var def = TestHelpers.EngineDef;
        var initial = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def),
            State = action, VX = 7f, VZ = 3f, FacingYaw = 0.4f,
        };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, initial);
        var state = Tick(sim, new InputState { GrabPressed = true, MoveX = -1f, MoveY = 1f });

        Assert.Equal(ActionState.GrabAttempt, state.State);
        Assert.InRange(state.VX, 0.01f, initial.VX - 0.01f);
        Assert.InRange(state.VZ, 0.01f, initial.VZ - 0.01f);
        TestHelpers.AssertNear(initial.VX / initial.VZ, state.VX / state.VZ);
        Assert.True(state.PX > initial.PX);
        Assert.True(state.PZ > initial.PZ);
        Assert.Equal(initial.FacingYaw, state.FacingYaw);

        for (int tick = 1; tick < DefenseConfig.GrabBrakeTicks; tick++)
        {
            var previous = state;
            state = Tick(sim, new InputState { MoveX = -1f, MoveY = 1f });
            Assert.InRange(state.VX, 0f, previous.VX);
            Assert.InRange(state.VZ, 0f, previous.VZ);
            Assert.Equal(initial.FacingYaw, state.FacingYaw);
        }
        Assert.Equal(ActionState.GrabAttempt, state.State);
        Assert.Equal(0f, state.VX);
        Assert.Equal(0f, state.VZ);
        var stopped = state;
        for (int tick = DefenseConfig.GrabBrakeTicks;
             tick < DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks; tick++)
        {
            state = Tick(sim, new InputState { MoveX = 1f, MoveY = -1f, FaceToCamera = true, AimYaw = 18000 });
            Assert.Equal(ActionState.GrabAttempt, state.State);
            Assert.Equal(stopped.PX, state.PX);
            Assert.Equal(stopped.PZ, state.PZ);
            Assert.Equal(initial.FacingYaw, state.FacingYaw);
        }
    }

    [Fact]
    public void StationaryGrabDoesNotGainMomentumFromHeldMovement()
    {
        var def = TestHelpers.EngineDef;
        var initial = TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) };
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, initial);

        for (int tick = 0; tick < DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks; tick++)
        {
            var state = Tick(sim, new InputState { GrabPressed = tick == 0, MoveX = 1f, MoveY = 1f });
            Assert.Equal(ActionState.GrabAttempt, state.State);
            Assert.Equal(initial.PX, state.PX);
            Assert.Equal(initial.PZ, state.PZ);
            Assert.Equal(0f, state.VX);
            Assert.Equal(0f, state.VZ);
        }
    }

    [Fact]
    public void SameTickShieldAndJumpBrakeButEnterVulnerableJumpSquat()
    {
        var def = TestHelpers.MankiDef;
        var initial = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def), State = ActionState.Sliding,
            VX = def.Movement.RunSpeed, CrouchSettled = true,
        };
        var state = Tick(Make(initial), new InputState { ShieldHeld = true, ShieldPressed = true, Jump = true, JumpHeld = true });
        Assert.Equal(ActionState.JumpSquat, state.State);
        Assert.Equal((byte)(initial.JumpsLeft - 1), state.JumpsLeft);
        Assert.Equal(0f, state.VX);
        Assert.Equal(0f, state.VZ);
        Assert.False(state.JumpFromSlide);
        Assert.False(state.CrouchSettled);
        Assert.Equal((ushort)0, state.InvincibilityTicks);
    }

    [Fact]
    public void ShieldJumpSkipsDropButPreservesShortHopDecision()
    {
        var sim = Make();
        Tick(sim, new InputState { ShieldHeld = true, ShieldPressed = true });
        var squat = Tick(sim, new InputState { ShieldHeld = true, Jump = true, JumpHeld = true });
        Assert.Equal(ActionState.JumpSquat, squat.State);
        Assert.Equal((ushort)0, squat.ShieldDropTicks);
        for (int t = 0; t < 10 && sim.GetState(1).State == ActionState.JumpSquat; t++)
            Tick(sim, default);
        var airborne = sim.GetState(1);
        Assert.False(airborne.IsGrounded);
        Assert.Equal((byte)1, airborne.AirDodgesLeft);
        Assert.NotEqual(ActionState.AirDodgeMovement, airborne.State);
        Assert.NotEqual(ActionState.AirDodgeRecovery, airborne.State);
    }

    [Fact]
    public void ShieldDropBlocksActionsForSevenTicksAndHeldShieldRaisesOnlyAfterRecovery()
    {
        var sim = Make();
        Tick(sim, new InputState { ShieldHeld = true });
        var release = Tick(sim);
        Assert.Equal(ActionState.ShieldDrop, release.State);
        Assert.Equal((ushort)7, release.ShieldDropTicks);
        for (int tick = 1; tick <= 6; tick++)
        {
            var locked = Tick(sim, new InputState { ShieldHeld = true, GrabPressed = true, Jump = true, ActiveSlot = AbilitySlots.Slot1, MoveX = 1 });
            Assert.Equal(ActionState.ShieldDrop, locked.State);
            Assert.Equal((ushort)(7 - tick), locked.ShieldDropTicks);
            Assert.Equal((byte)0, locked.AttackSlot);
            Assert.Equal(0f, locked.VX);
        }
        var available = Tick(sim, new InputState { ShieldHeld = true });
        Assert.Equal(ActionState.Shielding, available.State);
    }

    [Fact]
    public void ReleaseDuringBlockStunWaitsThenStartsFullDrop()
    {
        var sim = Make();
        var shield = Tick(sim, new InputState { ShieldHeld = true });
        shield.BlockStunTicks = 3;
        sim.SetState(1, shield);
        for (int i = 0; i < 3; i++)
        {
            var blocked = Tick(sim, new InputState { Jump = true, GrabPressed = true });
            Assert.Equal(ActionState.Shielding, blocked.State);
            Assert.Equal((ushort)0, blocked.ShieldDropTicks);
        }
        var released = Tick(sim);
        Assert.Equal(ActionState.ShieldDrop, released.State);
        Assert.Equal((ushort)7, released.ShieldDropTicks);
    }

    [Fact]
    public void BlockFreezeExcludesSdiAndDoesNotOverlapStunOrDrop()
    {
        var sim = Make();
        var shield = Tick(sim, new InputState { ShieldHeld = true });
        shield.BlockHitstopKind = (byte)DefenseBlockHitstopKind.ShieldContact;
        shield.HitstopTicks = 2;
        shield.BlockStunTicks = 3;
        sim.SetState(1, shield);
        for (int i = 0; i < 2; i++)
        {
            var frozen = Tick(sim, new InputState { MoveX = 1, Jump = true });
            Assert.Equal(ActionState.Shielding, frozen.State);
            Assert.Equal((ushort)3, frozen.BlockStunTicks);
            Assert.Equal(shield.PX, frozen.PX);
            Assert.Equal(0f, frozen.DIX);
            Assert.Equal((ushort)0, frozen.ShieldDropTicks);
        }
        for (int i = 0; i < 3; i++)
        {
            var stunned = Tick(sim, new InputState { Jump = true, GrabPressed = true });
            Assert.Equal(ActionState.Shielding, stunned.State);
        }
        var dropped = Tick(sim);
        Assert.Equal(ActionState.ShieldDrop, dropped.State);
        Assert.Equal((ushort)7, dropped.ShieldDropTicks);
    }

    [Fact]
    public void KnockbackAndLandingLagCannotBeClearedByShieldOrSyntheticBurst()
    {
        var def = TestHelpers.MankiDef;
        var initial = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def), KVX = 3f, LandingLagTicks = 4,
        };
        var state = Tick(Make(initial), new InputState { ShieldHeld = true, ShieldPressed = true, Burst = true });
        Assert.NotEqual(ActionState.Shielding, state.State);
        Assert.Equal((ushort)0, state.InvincibilityTicks);
        Assert.Equal((ushort)0, state.BurstCooldownTicks);
        Assert.Equal((ushort)0, state.DamagePercent);
    }

    [Fact]
    public void LosingGroundEndsShieldWithoutStartingAirDodge()
    {
        var def = TestHelpers.MankiDef;
        var initial = TestHelpers.PlayerState() with
        {
            PY = TestHelpers.GroundPY(def) + 4, IsGrounded = false,
            State = ActionState.Shielding, VY = -2f,
        };
        var state = Tick(Make(initial), new InputState { ShieldHeld = true });
        Assert.Equal(ActionState.Idle, state.State);
        Assert.Equal((byte)1, state.AirDodgesLeft);
    }
}
