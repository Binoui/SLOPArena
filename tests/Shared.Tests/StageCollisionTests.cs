using System;
using System.Collections.Generic;

using Xunit;
namespace SlopArena.Shared.Tests;

public sealed class StageCollisionTests
{
    private static CharacterDefinition Def => TestHelpers.MankiDef;

    [Fact]
    public void ElevatedLandingSlide_UsesWallTangent_ThroughJumpSquat()
    {
        var def = Def;
        var arena = FiniteArena(
            new[] { Floor(2f, -20f, 20f, -20f, 20f), FloorOther(2f, -20f, 20f, -20f, 20f),
                WallX(2f, 2f, 10f, -20f, 20f), WallXOther(2f, 2f, 10f, -20f, 20f) },
            (2f, -20f, 20f, -20f, 20f));
        var initial = Grounded(2f - def.CapsuleRadius - .01f, 0);
        initial.PY += 2.05f;
        initial.IsGrounded = false;
        initial.VY = -10;
        initial.VX = initial.VZ = def.Movement.RunSpeed;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, initial);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { Down = true } });
        var landed = sim.GetState(1);
        Assert.Contains(1UL, sim.LastTickTouchdowns);
        Assert.Equal(ActionState.Sliding, landed.State);
        TestHelpers.AssertNear(0f, landed.VX, .01f);
        Assert.True(landed.VZ > .25f * def.Movement.RunSpeed);
        Assert.InRange(landed.PY, 2f + def.CapsuleHeight * .5f - .03f, 2f + def.CapsuleHeight * .5f + .03f);
        sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { Jump = true, JumpHeld = true, Down = true } });
        for (int tick = 0; tick < 10 && sim.GetState(1).IsGrounded; tick++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { JumpHeld = true } });
        var jumped = sim.GetState(1);
        Assert.False(jumped.IsGrounded);
        TestHelpers.AssertNear(0, jumped.VX, .01f);
        Assert.True(jumped.VZ <= landed.VZ);
    }

    [Fact]
    public void SlideNormal_WallContactKeepsOnlyLiveTangentVelocity()
    {
        var def = Def;
        var arena = FiniteArena(
            new[] { Floor(0, -20, 20, -20, 20), FloorOther(0, -20, 20, -20, 20),
                WallX(2, 0, 10, -20, 20), WallXOther(2, 0, 10, -20, 20) },
            (0, -20, 20, -20, 20));
        var initial = Grounded(2 - def.CapsuleRadius - .02f, 0);
        initial.State = ActionState.Sliding;
        initial.VX = def.Movement.RunSpeed * .8f;
        initial.VZ = def.Movement.RunSpeed * .6f;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, initial);
        sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = new() { Down = true, ActiveSlot = AbilitySlots.Slot1 },
        });
        var contact = sim.GetState(1);
        Assert.True(contact.SlideAttackCarryActive);
        TestHelpers.AssertNear(0, contact.VX, .01f);
        Assert.True(contact.VZ > 0);
        for (int tick = 0; tick < 5; tick++)
        {
            sim.Tick(new Dictionary<ulong, InputState> { [1] = default });
            var state = sim.GetState(1);
            TestHelpers.AssertNear(0, state.VX, .01f);
            Assert.True(state.PX <= 2 - def.CapsuleRadius + .02f);
            Assert.True(state.VZ <= contact.VZ);
        }
    }

    [Fact]
    public void SlidingOffFinitePlatform_ClearsPostureWithoutFastFall()
    {
        var arena = FiniteArena(
            new[] { Floor(0f, -2f, 2f, -2f, 2f), FloorOther(0f, -2f, 2f, -2f, 2f) },
            (0f, -2f, 2f, -2f, 2f));
        var state = Grounded(1.8f, 0);
        state.State = ActionState.Sliding;
        state.VX = Def.Movement.RunSpeed;
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, Def, state);
        for (int tick = 0; tick < 10 && sim.GetState(1).IsGrounded; tick++)
            sim.Tick(new Dictionary<ulong, InputState> { [1] = new() { Down = true } });
        state = sim.GetState(1);
        Assert.False(state.IsGrounded);
        Assert.Equal(ActionState.Idle, state.State);
        Assert.False(state.CrouchSettled);
        Assert.False(state.IsFastFalling);
    }

    [Fact]
    public void WalkingIntoTallWall_StopsWithoutClimbing()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), WallX(2f, 0f, 4f));
        var state = Grounded(0f, 0f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 120f;

        Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);

        Assert.InRange(state.PX, 2f - Def.CapsuleRadius - 0.03f, 2f - Def.CapsuleRadius + 0.03f);
        Assert.InRange(state.PY, Def.CapsuleHeight * 0.5f - 0.01f, Def.CapsuleHeight * 0.5f + 0.01f);
    }
    [Fact]
    public void WalkingAcrossTriangleSeam_DoesNotStopAtRestingContact()
    {
        var arena = Arena(
            Floor(0f, -10f, 10f, -10f, 10f),
            FloorOther(0f, -10f, 10f, -10f, 10f));
        var state = Grounded(0f, 0f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 12f;

        for (int i = 0; i < 3; i++)
            Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);


        Assert.True(state.PX > 0.5f);
        Assert.InRange(state.PY, Def.CapsuleHeight * 0.5f - 0.02f,
            Def.CapsuleHeight * 0.5f + 0.02f);
    }

    [Fact]
    public void WalkingOffFlatLedge_DoesNotSnapToLowerFlatFloor()
    {
        var arena = FiniteArena(
            new[]
            {
                Floor(0f, -10f, 0f, -10f, 10f),
                FloorOther(0f, -10f, 0f, -10f, 10f),
                Floor(-0.3f, 0f, 10f, -10f, 10f),
                FloorOther(-0.3f, 0f, 10f, -10f, 10f),
            },
            (0f, -10f, 0f, -10f, 10f),
            (-0.3f, 0f, 10f, -10f, 10f));
        var state = Grounded(-0.25f, 0f);
        var input = TestHelpers.Input(moveX: 1f);

        for (int tick = 0; tick < 8; tick++)
        {
            Simulation.SimulateTick(ref state, Def, input, arena, out _, out _, DownActionTuning.Default, false);
            if (!state.IsGrounded)
            {
                Assert.True(state.PY > TestHelpers.GroundPY(Def) - 0.05f,
                    "Walking off a flat ledge must become airborne before dropping to the lower floor.");
                return;
            }
        }

        Assert.Fail("A flat ledge must not acquire the slope-following ground snap.");
    }

    [Fact]
    public void HeldRunAcrossCoplanarFloorSeam_PreservesGroundedTraversal()
    {
        const float seamOffset = 0.008f;
        var arena = FiniteArena(
            new[]
            {
                Floor(0f, -10f, 0f, -10f, 10f),
                FloorOther(0f, -10f, 0f, -10f, 10f),
                Floor(0f, 0f, 10f, -10f, 10f),
                FloorOther(0f, 0f, 10f, -10f, 10f),
            },
            (0f, -10f, 10f, -10f, 10f));
        var state = TestHelpers.PlayerState(-seamOffset, 0f);
        state.PY = TestHelpers.GroundPY(Def);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, Def, state);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = TestHelpers.Input(moveX: 1f),
        };

        for (int tick = 0; tick < 30; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded, $"tick={tick}: lost support at x={current.PX:F4}");
            Assert.InRange(MathF.Abs(current.PY - TestHelpers.GroundPY(Def)), 0f, 0.03f);
        }

        Assert.True(sim.GetState(1).PX > 1f);
    }

    [Fact]
    public void HeldRunAcrossContinuousRamp_RemainsGroundedUphillAndDownhill()
    {
        const float slope = 0.8f;
        const float startX = -5f;
        const float startZ = 8f;
        var arena = FiniteArena(
            new[]
            {
                SlopeFloor(slope, -10f, 10f, -10f, 10f),
                SlopeFloorOther(slope, -10f, 10f, -10f, 10f),
            },
            (0f, -10f, 10f, -10f, 10f));
        var state = TestHelpers.PlayerState(startX, startZ);
        float supportOffset = Def.CapsuleHeight * 0.5f - Def.CapsuleRadius
            + Def.CapsuleRadius * MathF.Sqrt(1f + slope * slope);
        state.PY = slope * (startX + 10f) + supportOffset;
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, Def, state);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = TestHelpers.Input(moveX: 1f),
        };

        for (int tick = 0; tick < 30; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded, $"uphill tick={tick}: lost support at x={current.PX:F3}");
            float expectedY = slope * (current.PX + 10f) + supportOffset;
            Assert.InRange(MathF.Abs(current.PY - expectedY), 0f, 0.05f);
        }

        float uphillX = sim.GetState(1).PX;
        Assert.True(uphillX > startX + 1f);

        inputs[1] = TestHelpers.Input(moveX: -1f);
        for (int tick = 0; tick < 30; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.IsGrounded, $"downhill tick={tick}: lost support at x={current.PX:F3}");
            float expectedY = slope * (current.PX + 10f) + supportOffset;
            Assert.InRange(MathF.Abs(current.PY - expectedY), 0f, 0.05f);
        }

        var downhill = sim.GetState(1);
        Assert.True(downhill.PX < uphillX - 1f);
    }


    [Fact]
    public void HorizontalContactWithPlatformEdge_DoesNotCreateUpwardMomentum()
    {
        var arena = Arena(
            Floor(0f, -10f, 10f, -10f, 10f),
            FloorOther(0f, -10f, 10f, -10f, 10f),
            WallX(2f, 0f, 1f),
            PlatformTop(2f, 6f, -2f, 2f));
        var state = Grounded(1.4f, 0f);
        state.PY = 1.5f;
        state.IsGrounded = false;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 20f;

        Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);

        Assert.True(state.PY <= 1.501f);
        Assert.True(state.VY <= 0.001f);
    }

    [Fact]
    public void KnockbackIntoPlatformEdge_DoesNotCreateUpwardMomentum()
    {
        var arena = Arena(
            Floor(0f, -10f, 10f, -10f, 10f),
            FloorOther(0f, -10f, 10f, -10f, 10f),
            WallX(2f, 0f, 1f),
            PlatformTop(2f, 6f, -2f, 2f));
        var state = Grounded(1.4f, 0f);
        state.PY = 1.5f;
        state.IsGrounded = false;
        Simulation.ApplyKnockback(ref state, 1f, 0f, 0, 30f, 0f, 0f, 30, 100f);

        for (int i = 0; i < 4; i++)
            Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);

        Assert.True(state.PY <= 1.501f);
        Assert.True(state.VY <= 0.001f);
    }


    [Fact]
    public void DiagonalWallContact_SlidesAndSettlesAtCorner()
    {
        var arena = Arena(
            Floor(0f, -10f, 10f, -10f, 10f),
            WallX(2f, 0f, 4f), WallZ(2f, 0f, 4f));
        var state = Grounded(0f, 0f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 120f;
        state.VZ = 60f;

        Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);

        Assert.True(state.PX <= 2f - Def.CapsuleRadius + 0.03f);
        Assert.True(state.PZ <= 2f - Def.CapsuleRadius + 0.03f);
        Assert.True(state.PX > 1f && state.PZ > 0.5f);
    }

    [Fact]
    public void FallingOntoRaisedPlatform_LandsAtPlatformTop()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), Floor(2f, -2f, 2f, -2f, 2f));
        var state = Grounded(0f, 0f);
        state.PY = 5f;
        state.IsGrounded = false;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.AirTimeTicks = 30;
        state.VY = -180f;

        for (int i = 0; i < 20; i++)
            Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);

        Assert.True(state.IsGrounded);
        Assert.InRange(state.PY, 2f + Def.CapsuleHeight * 0.5f - 0.02f,
            2f + Def.CapsuleHeight * 0.5f + 0.02f);
    }

    [Fact]
    public void MovingUnderElevatedPlatform_PreservesClearance()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), Floor(2f, -2f, 2f, -2f, 2f));
        var state = Grounded(0f, -4f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 30f;

        Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);

        Assert.True(state.IsGrounded);
        Assert.InRange(state.PY, Def.CapsuleHeight * 0.5f - 0.02f, Def.CapsuleHeight * 0.5f + 0.02f);
        Assert.True(state.PX > 0.2f);
    }

    [Fact]
    public void JumpIntoPlatformUnderside_StopsUpwardAndStaysAirborne()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), Floor(2f, -2f, 2f, -2f, 2f));
        var state = Grounded(0f, 0f);
        state.PY = 1f;
        state.IsGrounded = false;
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.AirTimeTicks = 30;
        state.VY = 120f;

        Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);

        Assert.False(state.IsGrounded);
        Assert.Equal(0f, state.VY);
        Assert.InRange(state.PY, 2f - Def.CapsuleHeight * 0.5f - 0.03f,
            2f - Def.CapsuleHeight * 0.5f + 0.03f);
    }

    [Fact]
    public void RisingContactWithPlatformLip_DoesNotLaunchTheFighter()
    {
        // A solid 0.5 m thin platform whose top sits near FightGuy's single-jump apex.
        // Jumping at its inner lip catches the rounded capsule; contact must never add
        // upward speed, refresh the float window or pop the fighter above the top.
        var def = TestHelpers.FightGuyDef;
        var triangles = new List<CollisionTriangle> { Floor(0f, -20f, 40f, -20f, 20f), FloorOther(0f, -20f, 40f, -20f, 20f) };
        triangles.AddRange(Box(7f, 13f, 1.5f, 2f, 5.5f, 10.5f));
        var arena = FiniteArena(triangles.ToArray(), (0f, -20f, 40f, -20f, 20f), (2f, 7f, 13f, 5.5f, 10.5f));
        var state = TestHelpers.PlayerState(5.75f, 8f);
        state.PY = TestHelpers.GroundPY(def);
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>();
        for (int tick = 0; tick < 140; tick++)
        {
            var before = sim.GetState(1);
            inputs[1] = TestHelpers.Input(moveX: before.PX < 9f ? 1f : 0f,
                jump: tick == 0, jumpHeld: tick < 10);
            sim.Tick(inputs);
            var after = sim.GetState(1);
            if (tick > 8 && before.VY > 0f)
                Assert.True(after.VY <= before.VY + 0.001f,
                    $"tick={tick}: platform accelerated ascent {before.VY} -> {after.VY}");
            Assert.True(after.PY <= TestHelpers.GroundPY(def, 2f) + 0.1f,
                $"tick={tick}: launched above platform to {after.PY}");
            if (after.VY > 0f && tick > 8)
                Assert.True(after.AirTimeTicks >= def.Movement.FloatWindowTicks,
                    $"tick={tick}: rising contact refreshed float");
        }
    }

[Fact]
public void Knockback_DoesNotTunnelThroughThinWall()
{
    var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), WallX(2f, 0f, 4f));
    var knockback = Grounded(0f, 0f);
    knockback.State = ActionState.Idle;
    knockback.IsGrounded = false;
    knockback.KVX = 240f;
    Simulation.SimulateTick(ref knockback, Def, default, arena, out _, out _, DownActionTuning.Default, false);

    Assert.True(knockback.PX < 2f);
}

    [Fact]
    public void WalkingOffFinitePlatform_StartsFalling()
    {
        var arena = Arena(Floor(0f, -2f, 2f, -2f, 2f));
        var state = Grounded(1.8f, 0f);
        state.State = ActionState.Attacking;
        state.AnimLockTicks = 10;
        state.VX = 30f;


        Simulation.SimulateTick(ref state, Def, default, arena, out _, out _, DownActionTuning.Default, false);

        Assert.False(state.IsGrounded);
        Assert.True(state.PX > 2f);
    }

    [Fact]
    public void TriangleMovement_IsDeterministicAcrossIdenticalRuns()
    {
        var arena = Arena(Floor(0f, -10f, 10f, -10f, 10f), WallX(2f, 0f, 4f));
        var first = Grounded(0f, 0f);
        first.State = ActionState.Attacking;
        first.AnimLockTicks = 20;
        first.VX = 17f;
        first.VZ = 4f;
        var second = first;

        for (int i = 0; i < 30; i++)
        {
            Simulation.SimulateTick(ref first, Def, default, arena, out _, out _, DownActionTuning.Default, false);
            Simulation.SimulateTick(ref second, Def, default, arena, out _, out _, DownActionTuning.Default, false);
        }

        Assert.Equal(first.PX, second.PX);
        Assert.Equal(first.PY, second.PY);
        Assert.Equal(first.PZ, second.PZ);
        Assert.Equal(first.VX, second.VX);
        Assert.Equal(first.VY, second.VY);
        Assert.Equal(first.VZ, second.VZ);
        Assert.Equal(first.IsGrounded, second.IsGrounded);
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void HeldRunIntoWall_StopsAndCanMoveAway(CharacterClass cls)
    {
        const float wallX = 2f;
        var def = TestHelpers.ResolveDef(cls);
        var arena = FiniteArena(
            new[] { Floor(0f, -20f, 20f, -20f, 20f), FloorOther(0f, -20f, 20f, -20f, 20f),
                WallX(wallX, 0f, 10f, -20f, 20f), WallXOther(wallX, 0f, 10f, -20f, 20f) },
            (0f, -20f, 20f, -20f, 20f));
        var state = TestHelpers.PlayerState(0f, 0f);
        state.PY = TestHelpers.GroundPY(def);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState>
        {
            [1] = TestHelpers.Input(moveX: 1f),
        };

        bool contacted = false;
        float contactX = 0f;
        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.PX <= wallX - def.CapsuleRadius + 0.03f,
                Trace(cls, tick, inputs[1], current, "wall penetration"));
            Assert.True(MathF.Abs(current.PY - TestHelpers.GroundPY(def)) <= 0.03f,
                Trace(cls, tick, inputs[1], current, "wall climb"));
            Assert.True(current.IsGrounded,
                Trace(cls, tick, inputs[1], current, "lost groundedness"));
            if (!contacted && current.PX >= wallX - def.CapsuleRadius - 0.03f)
            {
                contacted = true;
                contactX = current.PX;
            }
        }

        Assert.True(contacted, $"{cls}: held run never reached wall contact; final state={Trace(cls, 119, inputs[1], sim.GetState(1), "contact")}");
        inputs[1] = TestHelpers.Input(moveX: -1f);
        for (int tick = 120; tick < 150; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.PX <= wallX - def.CapsuleRadius + 0.03f,
                Trace(cls, tick, inputs[1], current, "reverse penetration"));
            Assert.True(MathF.Abs(current.PY - TestHelpers.GroundPY(def)) <= 0.03f,
                Trace(cls, tick, inputs[1], current, "reverse climb"));
            Assert.True(current.IsGrounded,
                Trace(cls, tick, inputs[1], current, "reverse lost groundedness"));
        }

        var away = sim.GetState(1);
        Assert.True(contactX - away.PX >= 0.5f,
            $"{cls}: reverse did not move at least 0.5m away; contactX={contactX:F3}, final={Trace(cls, 149, inputs[1], away, "reverse")}");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void HeldDiagonalRunAlongWall_PreservesTangentialMovement(CharacterClass cls)
    {
        const float wallX = 2f;
        var def = TestHelpers.ResolveDef(cls);
        var arena = FiniteArena(
            new[] { TraversalFloor(0f, -20f, 20f, -20f, 20f), TraversalFloorOther(0f, -20f, 20f, -20f, 20f),
                WallX(wallX, 0f, 10f, -20f, 20f), WallXOther(wallX, 0f, 10f, -20f, 20f) },
            (0f, -20f, 20f, -20f, 20f));
        var state = TestHelpers.PlayerState(0f, -4f);
        state.PY = TestHelpers.GroundPY(def);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var input = TestHelpers.Input(moveX: 1f, moveY: 1f);
        var inputs = new Dictionary<ulong, InputState> { [1] = input };
        var expectedDirection = ExpectedInputDirection(input);
        float expectedZ = expectedDirection.z;
        bool contacted = false;
        int contactTick = -1;
        float contactZ = 0f;

        for (int tick = 0; tick < 90; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.True(current.PX <= wallX - def.CapsuleRadius + 0.03f,
                Trace(cls, tick, input, current, "diagonal wall penetration"));
            Assert.True(MathF.Abs(current.PY - TestHelpers.GroundPY(def)) <= 0.03f,
                Trace(cls, tick, input, current, "diagonal wall climb"));
            Assert.True(current.IsGrounded,
                Trace(cls, tick, input, current, "diagonal lost groundedness"));
            if (!contacted && current.PX >= wallX - def.CapsuleRadius - 0.03f)
            {
                contacted = true;
                contactTick = tick;
                contactZ = current.PZ;
            }
            if (contacted)
            {
                float tangentTravel = expectedZ * (current.PZ - contactZ);
                if (tangentTravel >= 1f) break;
            }
        }

        var final = sim.GetState(1);
        Assert.True(contacted, $"{cls}: diagonal run never reached wall contact; contactTick={contactTick}; final={Trace(cls, 89, input, final, "contact")}");
        Assert.True(expectedZ * (final.PZ - contactZ) >= 1f,
            $"{cls}: diagonal run lost tangential travel; contactTick={contactTick}, contactZ={contactZ:F3}, final={Trace(cls, 89, input, final, "tangent")}");
    }


    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Manki)]
    [InlineData(CharacterClass.Wibou)]
    [InlineData(CharacterClass.Bonk)]
    public void RunOffTrianglePlatform_FallsWithoutHoverOrSelfGrab(CharacterClass cls)
    {
        const float platformY = 6f;
        var def = TestHelpers.ResolveDef(cls);
        var arena = FiniteArena(
            new[] { Floor(platformY, -8f, 2f, -8f, 8f), FloorOther(platformY, -8f, 2f, -8f, 8f) },
            (platformY, -8f, 2f, -8f, 8f));
        var state = TestHelpers.PlayerState(0f, 0f);
        state.PY = TestHelpers.GroundPY(def, platformY);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var input = TestHelpers.Input(moveX: 1f);
        var inputs = new Dictionary<ulong, InputState> { [1] = input };
        int leaveTick = -1;
        int fallTick = -1;

        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick(inputs);
            var current = sim.GetState(1);
            Assert.NotEqual(ActionState.LedgeHang, current.State);
            if (leaveTick < 0 && !current.IsGrounded)
                leaveTick = tick;
            if (leaveTick >= 0)
            {
                Assert.True(current.VY <= 0.001f,
                    Trace(cls, tick, input, current, "walk-off launched upward"));
                if (current.VY < 0f && fallTick < 0)
                {
                    fallTick = tick;
                    Assert.True(tick - leaveTick <= 2,
                        Trace(cls, tick, input, current, "walk-off fall delayed"));
                    Assert.True(current.PY - def.CapsuleHeight * 0.5f < platformY - 0.001f,
                        Trace(cls, tick, input, current, "walk-off height did not drop"));
                }
                if (current.PY - def.CapsuleHeight * 0.5f <= platformY - 1f)
                {
                    Assert.True(fallTick >= 0, Trace(cls, tick, input, current, "walk-off never entered fall"));
                    return;
                }
            }
        }

        var final = sim.GetState(1);
        Assert.True(leaveTick >= 0, $"{cls}: never left finite triangle platform; final={Trace(cls, 119, input, final, "leave")}");
        Assert.True(fallTick >= 0, $"{cls}: no negative velocity within two ticks; final={Trace(cls, 119, input, final, "fall")}");
        Assert.True(final.PY - def.CapsuleHeight * 0.5f <= platformY - 1f,
            $"{cls}: did not fall 1m below platform; final={Trace(cls, 119, input, final, "depth")}");
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy, 0f)]
    [InlineData(CharacterClass.FightGuy, 0.5f)]
    [InlineData(CharacterClass.Manki, 0f)]
    [InlineData(CharacterClass.Manki, 0.5f)]
    [InlineData(CharacterClass.Wibou, 0f)]
    [InlineData(CharacterClass.Wibou, 0.5f)]
    [InlineData(CharacterClass.Bonk, 0f)]
    [InlineData(CharacterClass.Bonk, 0.5f)]
    public void HeldJumpBetweenTrianglePlatforms_LandsAndKeepsMoving(CharacterClass cls, float destinationY)
    {
        const float sourceY = 0f;
        var def = TestHelpers.ResolveDef(cls);
        var triangles = new List<CollisionTriangle>
        {
            Floor(sourceY, -10f, 0f, -8f, 8f),
            FloorOther(sourceY, -10f, 0f, -8f, 8f),
            Floor(destinationY, 1f, 30f, -8f, 8f),
            FloorOther(destinationY, 1f, 30f, -8f, 8f),
        };
        if (destinationY > sourceY)
        {
            triangles.Add(WallX(1f, sourceY, destinationY, -8f, 8f));
            triangles.Add(WallXOther(1f, sourceY, destinationY, -8f, 8f));
            triangles.Add(WallX(30f, sourceY, destinationY, -8f, 8f));
            triangles.Add(WallXOther(30f, sourceY, destinationY, -8f, 8f));
            triangles.Add(WallZ(-8f, sourceY, destinationY, 1f, 30f));
            triangles.Add(WallZOther(-8f, sourceY, destinationY, 1f, 30f));
            triangles.Add(WallZ(8f, sourceY, destinationY, 1f, 30f));
            triangles.Add(WallZOther(8f, sourceY, destinationY, 1f, 30f));
        }
        var arena = FiniteArena(
            triangles.ToArray(),
            (sourceY, -10f, 0f, -8f, 8f),
            (destinationY, 1f, 30f, -8f, 8f));
        var state = TestHelpers.PlayerState(-1f, 0f);
        state.PY = TestHelpers.GroundPY(def, sourceY);
        state.IsGrounded = true;
        var sim = TestHelpers.MakeSim(arena);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        bool tookOff = false;
        bool crossedGap = false;
        bool landed = false;
        int landingTick = -1;
        float landedX = 0f;

        for (int tick = 0; tick < 180; tick++)
        {
            var before = sim.GetState(1);
            bool jumpPress = tick == 0;
            float moveX = !tookOff || before.PX < 4f ? 1f : 0f;
            inputs[1] = TestHelpers.Input(moveX: moveX, jump: jumpPress, jumpHeld: true);
            sim.Tick(inputs);
            var current = sim.GetState(1);
            if (!tookOff && !current.IsGrounded && current.VY > 0f)
                tookOff = true;
            if (tookOff && !current.IsGrounded && current.PX > 1f)
                crossedGap = true;
            if (tookOff && !crossedGap && before.PX < 1f && current.PX >= 1f)
                crossedGap = true;
            if (tookOff && current.IsGrounded && !landed)
            {
                Assert.True(current.PX >= 1f - 0.03f,
                    Trace(cls, tick, inputs[1], current, "landed before destination"));
                landed = true;
                landingTick = tick;
                landedX = current.PX;
                Assert.True(MathF.Abs(current.PY - TestHelpers.GroundPY(def, destinationY)) <= 0.03f,
                    Trace(cls, tick, inputs[1], current, "destination landing height"));
            }
            if (tookOff && !landed && current.PX >= 1f - 0.03f)
            {
                Assert.True(current.PY - def.CapsuleHeight * 0.5f >= destinationY - 0.25f,
                    Trace(cls, tick, inputs[1], current, "unexpected below-surface teleport"));
            }
            if (landed && tick - landingTick >= 20)
                break;
        }

        var final = sim.GetState(1);
        Assert.True(tookOff, $"{cls} destinationY={destinationY:F1}: no takeoff; final={Trace(cls, 179, inputs[1], final, "takeoff")}");
        Assert.True(crossedGap, $"{cls} destinationY={destinationY:F1}: gap was not crossed airborne; final={Trace(cls, 179, inputs[1], final, "gap")}");
        Assert.True(landed, $"{cls} destinationY={destinationY:F1}: no destination landing; final={Trace(cls, 179, inputs[1], final, "landing")}");
        Assert.True(final.IsGrounded && MathF.Abs(final.PY - TestHelpers.GroundPY(def, destinationY)) <= 0.03f,
            Trace(cls, landingTick + 20, inputs[1], final, "post-landing support"));
        Assert.True(final.PX - landedX >= 0.5f,
            $"{cls} destinationY={destinationY:F1}: post-landing movement was insufficient; landedX={landedX:F3}, final={Trace(cls, landingTick + 20, inputs[1], final, "post-landing travel")}");
    }

    private static string Trace(CharacterClass cls, int tick, InputState input, CharacterState state, string contract)
        => $"{cls} tick={tick} contract={contract} input=({input.MoveX:F2},{input.MoveY:F2}) " +
           $"pos=({state.PX:F3},{state.PY:F3},{state.PZ:F3}) " +
           $"vel=({state.VX:F3},{state.VY:F3},{state.VZ:F3}) grounded={state.IsGrounded} state={state.State}";

    private static (float x, float z) ExpectedInputDirection(InputState input)
    {
        float x = input.MoveX;
        float z = input.MoveY;
        float length = MathF.Sqrt(x * x + z * z);
        return length > 0.001f ? (x / length, z / length) : (0f, 0f);
    }


    private static CharacterState Grounded(float x, float z)
    {
        var state = TestHelpers.PlayerState(x, z);
        state.PY = Def.CapsuleHeight * 0.5f;
        state.IsGrounded = true;
        return state;
    }

    private static ArenaDefinition FiniteArena(
        CollisionTriangle[] triangles,
        params (float y, float minX, float maxX, float minZ, float maxZ)[] surfaces)
    {
        const int width = 240;
        const int height = 160;
        const float cellSize = 0.25f;
        const float originX = -20f;
        const float originZ = -20f;
        var data = new float[width * height];
        for (int z = 0; z < height; z++)
        for (int x = 0; x < width; x++)
        {
            float worldX = originX + x * cellSize;
            float worldZ = originZ + z * cellSize;
            float top = float.MinValue;
            foreach (var surface in surfaces)
            {
                if (worldX >= surface.minX && worldX <= surface.maxX
                    && worldZ >= surface.minZ && worldZ <= surface.maxZ)
                    top = MathF.Max(top, surface.y);
            }
            data[z * width + x] = top;
        }
        var arena = new ArenaDefinition
        {
            Name = "finite-triangle-test",
            DisplayName = "Finite Triangle Test",
            KillHeight = -20f,
            MinX = -20f,
            MaxX = 40f,
            MinZ = -20f,
            MaxZ = 20f,
            Heightmap = new ArenaHeightmap
            {
                Width = width,
                Height = height,
                CellSize = cellSize,
                OriginX = originX,
                OriginZ = originZ,
                Data = data,
            },
            CollisionTriangles = triangles,
        };
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    private static ArenaDefinition Arena(params CollisionTriangle[] triangles)
    {
        var arena = TestHelpers.TestArena();
        arena.MinX = -20f;
        arena.MaxX = 20f;
        arena.MinZ = -20f;
        arena.MaxZ = 20f;
        arena.CollisionTriangles = triangles;
        arena.SpatialGrid = ArenaCollision.BuildSpatialGrid(in arena);
        return arena;
    }

    private static CollisionTriangle Floor(float y, float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = minX, AY = y, AZ = minZ,
            BX = minX, BY = y, BZ = maxZ,
            CX = maxX, CY = y, CZ = minZ,
        };
    private static CollisionTriangle FloorOther(float y, float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = maxX, AY = y, AZ = maxZ,
            BX = maxX, BY = y, BZ = minZ,
            CX = minX, CY = y, CZ = maxZ,
        };

    private static CollisionTriangle SlopeFloor(float slope,
        float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = minX, AY = 0f, AZ = minZ,
            BX = minX, BY = 0f, BZ = maxZ,
            CX = maxX, CY = slope * (maxX - minX), CZ = minZ,
        };

    private static CollisionTriangle SlopeFloorOther(float slope,
        float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = maxX, AY = slope * (maxX - minX), AZ = maxZ,
            BX = maxX, BY = slope * (maxX - minX), BZ = minZ,
            CX = minX, CY = 0f, CZ = maxZ,
        };
    private static CollisionTriangle TraversalFloor(float y, float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = minX, AY = y, AZ = minZ,
            BX = minX, BY = y, BZ = maxZ,
            CX = maxX, CY = y, CZ = maxZ,
        };

    private static CollisionTriangle TraversalFloorOther(float y, float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = maxX, AY = y, AZ = minZ,
            BX = minX, BY = y, BZ = minZ,
            CX = maxX, CY = y, CZ = maxZ,
        };


    private static CollisionTriangle PlatformTop(float minX, float maxX, float minZ, float maxZ)
        => new()
        {
            AX = minX, AY = 1f, AZ = minZ,
            BX = minX, BY = 1f, BZ = maxZ,
            CX = maxX, CY = 1f, CZ = minZ,
        };


    /// <summary>Closed box wound so that (B - A) x (C - A) points out of it.</summary>
    private static CollisionTriangle[] Box(float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        var p = new[]
        {
            (minX, minY, minZ), (maxX, minY, minZ), (maxX, minY, maxZ), (minX, minY, maxZ),
            (minX, maxY, minZ), (maxX, maxY, minZ), (maxX, maxY, maxZ), (minX, maxY, maxZ),
        };
        var quads = new[] { (4, 7, 6, 5), (0, 1, 2, 3), (0, 4, 5, 1), (3, 2, 6, 7), (0, 3, 7, 4), (1, 5, 6, 2) };
        var triangles = new List<CollisionTriangle>();
        foreach (var (a, b, c, d) in quads)
        {
            triangles.Add(Triangle(p[a], p[b], p[c]));
            triangles.Add(Triangle(p[a], p[c], p[d]));
        }
        return triangles.ToArray();
    }

    private static CollisionTriangle Triangle((float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c)
        => new()
        {
            AX = a.X, AY = a.Y, AZ = a.Z,
            BX = b.X, BY = b.Y, BZ = b.Z,
            CX = c.X, CY = c.Y, CZ = c.Z,
        };

    private static CollisionTriangle WallX(float x, float minY, float maxY)
        => WallX(x, minY, maxY, -10f, 10f);

    private static CollisionTriangle WallX(float x, float minY, float maxY, float minZ, float maxZ)
        => new()
        {
            AX = x, AY = minY, AZ = minZ,
            BX = x, BY = maxY, BZ = minZ,
            CX = x, CY = minY, CZ = maxZ,
        };

    private static CollisionTriangle WallXOther(float x, float minY, float maxY, float minZ, float maxZ)
        => new()
        {
            AX = x, AY = minY, AZ = maxZ,
            BX = x, BY = minY, BZ = minZ,
            CX = x, CY = maxY, CZ = maxZ,
        };

    private static CollisionTriangle WallZ(float z, float minY, float maxY)
        => WallZ(z, minY, maxY, -10f, 10f);

    private static CollisionTriangle WallZ(float z, float minY, float maxY, float minX, float maxX)
        => new()
        {
            AX = minX, AY = minY, AZ = z,
            BX = minX, BY = maxY, BZ = z,
            CX = maxX, CY = minY, CZ = z,
        };

    private static CollisionTriangle WallZOther(float z, float minY, float maxY, float minX, float maxX)
        => new()
        {
            AX = maxX, AY = minY, AZ = z,
            BX = minX, BY = minY, BZ = z,
            CX = maxX, CY = maxY, CZ = z,
        };
}
