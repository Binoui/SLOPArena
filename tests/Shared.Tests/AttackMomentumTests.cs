using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public class AttackMomentumTests
{
    [Theory]
    [InlineData(ActionState.Run, 1f, 20)]
    [InlineData(ActionState.Sliding, 1f, 10)]
    [InlineData(ActionState.Idle, 0f, 0)]
    public void EligibleNormal_BrakesBeforeIntegrationWithoutSteeringOrAcceleration(
        ActionState origin, float entryRatio, int stoppingTick)
    {
        var def = MakeDefinition();
        var sim = NewSimulation(def, origin, def.Movement.RunSpeed);
        float startX = sim.GetState(1).PX;
        float startZ = sim.GetState(1).PZ;
        float distance = 0f;

        for (int tick = 1; tick <= 22; tick++)
        {
            var state = Tick(sim, TestHelpers.Input(
                activeSlot: tick == 1 ? AbilitySlots.Slot1 : AbilitySlots.None,
                moveX: tick % 2 == 0 ? -1f : 1f, moveY: 1f,
                down: origin == ActionState.Sliding));
            float speed = MathF.Max(0f, entryRatio - Brake(origin) * tick) * def.Movement.RunSpeed;
            distance += speed * Simulation.TickDt;

            Assert.Equal(ActionState.Attacking, state.State);
            Assert.Equal(origin != ActionState.Idle, state.SlideAttackCarryActive);
            TestHelpers.AssertNear(speed, state.VX, .001f);
            TestHelpers.AssertNear(0f, state.VZ, .001f);
            TestHelpers.AssertNear(startX + distance, state.PX, .001f);
            TestHelpers.AssertNear(startZ, state.PZ, .001f);
            if (tick >= stoppingTick)
                Assert.Equal(0f, state.VX);
        }
    }

    [Theory]
    [InlineData(ActionState.Run, 1.2f, .9f, 1f)]
    [InlineData(ActionState.Run, .24f, .18f, .3f)]
    [InlineData(ActionState.Run, .03f, .04f, .05f)]
    [InlineData(ActionState.Sliding, 1.2f, .9f, 1f)]
    [InlineData(ActionState.Sliding, .24f, .18f, .3f)]
    public void Carry_CapsVectorMagnitudeWithoutRefillingLowerSpeed(
        ActionState origin, float xRatio, float zRatio, float entryRatio)
    {
        var def = MakeDefinition();
        float runSpeed = def.Movement.RunSpeed;
        var sim = NewSimulation(def, origin, xRatio * runSpeed, zRatio * runSpeed);
        var before = sim.GetState(1);
        float magnitude = MathF.Sqrt(xRatio * xRatio + zRatio * zRatio);
        float directionX = xRatio / magnitude;
        float directionZ = zRatio / magnitude;
        float distance = 0f;

        for (int tick = 1; tick <= 22; tick++)
        {
            var after = Tick(sim, TestHelpers.Input(
                activeSlot: tick == 1 ? AbilitySlots.Slot1 : AbilitySlots.None,
                moveX: -1f, moveY: -1f, down: origin == ActionState.Sliding));
            float speed = MathF.Max(0f, entryRatio - Brake(origin) * tick) * runSpeed;
            distance += speed * Simulation.TickDt;

            Assert.Equal(ActionState.Attacking, after.State);
            Assert.True(after.SlideAttackCarryActive);
            TestHelpers.AssertNear(directionX * speed, after.VX, .001f);
            TestHelpers.AssertNear(directionZ * speed, after.VZ, .001f);
            TestHelpers.AssertNear(before.PX + directionX * distance, after.PX, .001f);
            TestHelpers.AssertNear(before.PZ + directionZ * distance, after.PZ, .001f);
        }
        Assert.Equal(0f, sim.GetState(1).VX);
        Assert.Equal(0f, sim.GetState(1).VZ);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeldRunInput_ThenOptionalDown_AdmitsCarryWithoutRequiringDownOnRun(bool slide)
    {
        var def = MakeDefinition();
        var sim = NewSimulation(def, ActionState.Idle, 0f);
        var running = TestHelpers.TickHold(sim, TestHelpers.Input(moveY: 1f), 30);
        Assert.Equal(ActionState.Run, running.State);
        TestHelpers.AssertNear(def.Movement.RunSpeed, running.VZ);
        if (slide)
            Assert.Equal(ActionState.Sliding,
                Tick(sim, new InputState { MoveY = 1f, Down = true, DownPressed = true }).State);
        var before = sim.GetState(1);
        float entrySpeed = slide ? before.VZ : def.Movement.RunSpeed;
        float expectedSpeed = entrySpeed
            - Brake(slide ? ActionState.Sliding : ActionState.Run) * def.Movement.RunSpeed;

        var after = Tick(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1, moveY: 1f, down: slide));

        Assert.Equal(ActionState.Attacking, after.State);
        Assert.True(after.SlideAttackCarryActive);
        TestHelpers.AssertNear(expectedSpeed, after.VZ, .001f);
        TestHelpers.AssertNear(before.PZ + expectedSpeed * Simulation.TickDt, after.PZ, .001f);
    }

    [Theory]
    [InlineData(ActionState.Run)]
    [InlineData(ActionState.Sliding)]
    [InlineData(ActionState.Idle)]
    public void NonOptedGroundedNormal_StopsIncomingMomentum(ActionState origin)
    {
        var def = MakeDefinition(allowCarry: false);
        var sim = NewSimulation(def, origin, 10f, 5f);
        var before = sim.GetState(1);

        var after = Tick(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1,
            down: origin == ActionState.Sliding));

        Assert.Equal(ActionState.Attacking, after.State);
        Assert.False(after.SlideAttackCarryActive);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
        Assert.Equal(before.PX, after.PX);
        Assert.Equal(before.PZ, after.PZ);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AerialNormal_PreservesMomentumWithoutGroundCarry(bool allowCarry)
    {
        var def = MakeDefinition(allowCarry: allowCarry);
        var sim = NewSimulation(def, ActionState.Idle, 10f, 5f, grounded: false);
        var before = sim.GetState(1);

        var after = Tick(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1));

        Assert.Equal(ActionState.Attacking, after.State);
        Assert.False(after.SlideAttackCarryActive);
        TestHelpers.AssertNear(10f, after.VX, .001f);
        TestHelpers.AssertNear(5f, after.VZ, .001f);
        TestHelpers.AssertNear(before.PX + after.VX * Simulation.TickDt, after.PX, .001f);
        TestHelpers.AssertNear(before.PZ + after.VZ * Simulation.TickDt, after.PZ, .001f);
    }

    [Theory]
    [InlineData(ActionState.Run, true, false)]
    [InlineData(ActionState.Run, false, false)]
    [InlineData(ActionState.Idle, true, false)]
    [InlineData(ActionState.Sliding, true, true)]
    [InlineData(ActionState.Sliding, false, false)]
    public void PreserveOverride_KeepsVelocityExceptForEligibleSlide(
        ActionState origin, bool allowCarry, bool brakes)
    {
        var def = MakeDefinition(allowCarry: allowCarry, preserveMomentum: true);
        float runSpeed = def.Movement.RunSpeed;
        var sim = NewSimulation(def, origin, 1.2f * runSpeed, .9f * runSpeed);
        var before = sim.GetState(1);

        for (int tick = 0; tick < 2; tick++)
        {
            var after = Tick(sim, TestHelpers.Input(
                activeSlot: tick == 0 ? AbilitySlots.Slot1 : AbilitySlots.None,
                down: origin == ActionState.Sliding));
            float xRatio = brakes ? .8f * (.9f - .1f * tick) : 1.2f;
            float zRatio = brakes ? .6f * (.9f - .1f * tick) : .9f;
            Assert.Equal(ActionState.Attacking, after.State);
            Assert.Equal(brakes, after.SlideAttackCarryActive);
            TestHelpers.AssertNear(xRatio * runSpeed, after.VX, .001f);
            TestHelpers.AssertNear(zRatio * runSpeed, after.VZ, .001f);
            TestHelpers.AssertNear(before.PX + after.VX * Simulation.TickDt, after.PX, .001f);
            TestHelpers.AssertNear(before.PZ + after.VZ * Simulation.TickDt, after.PZ, .001f);
            before = after;
        }
    }

    [Theory]
    [InlineData(ActionState.Run)]
    [InlineData(ActionState.Sliding)]
    public void Carry_HitstopFreezesMotionAndResumesTheRemainingBrake(ActionState origin)
    {
        var def = MakeDefinition();
        var sim = NewSimulation(def, origin, def.Movement.RunSpeed);
        var before = Tick(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1));
        sim.SetState(1, before with { HitstopTicks = 3 });

        for (int frozenTick = 0; frozenTick < 3; frozenTick++)
        {
            var frozen = Tick(sim);
            Assert.Equal(before.PX, frozen.PX);
            Assert.Equal(before.PZ, frozen.PZ);
            Assert.Equal(before.VX, frozen.VX);
            Assert.Equal(before.AttackElapsedTicks, frozen.AttackElapsedTicks);
            Assert.True(frozen.SlideAttackCarryActive);
        }

        float distance = 0f;
        int remainingTicks = origin == ActionState.Run ? 19 : 9;
        for (int tick = 1; tick <= remainingTicks + 1; tick++)
        {
            var after = Tick(sim, TestHelpers.Input(moveY: 1f));
            float speed = MathF.Max(0f, before.VX - tick * Brake(origin) * def.Movement.RunSpeed);
            distance += speed * Simulation.TickDt;
            TestHelpers.AssertNear(speed, after.VX, .001f);
            TestHelpers.AssertNear(before.PX + distance, after.PX, .001f);
            Assert.Equal(before.PZ, after.PZ);
            if (tick >= remainingTicks)
                Assert.Equal(0f, after.VX);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Iasa_PreservesAdmissionBoundaryAndDoesNotTreatChainedNormalAsRun(int elapsed, bool accepted)
    {
        var def = MakeDefinition(iasaTicks: 2);
        var sim = NewSimulation(def, ActionState.Run, def.Movement.RunSpeed);
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1), elapsed);
        var before = sim.GetState(1);
        Assert.Equal(elapsed, before.AttackElapsedTicks);
        var originalAbility = sim.GetActiveAbility(1);

        var after = Tick(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot3, moveX: 1f));

        Assert.Equal(ActionState.Attacking, after.State);
        Assert.Equal(accepted ? AbilitySlots.Slot3 : AbilitySlots.Slot1, after.AttackSlot);
        Assert.Equal(!accepted, after.SlideAttackCarryActive);
        if (accepted)
        {
            Assert.NotSame(originalAbility, sim.GetActiveAbility(1));
            Assert.Equal(0f, after.VX);
            Assert.Equal(before.PX, after.PX);
        }
        else
        {
            Assert.Same(originalAbility, sim.GetActiveAbility(1));
            TestHelpers.AssertNear(.9f * def.Movement.RunSpeed, after.VX, .001f);
            TestHelpers.AssertNear(before.PX + after.VX * Simulation.TickDt, after.PX, .001f);
        }
        Assert.Equal(0f, after.VZ);
    }

    [Theory]
    [InlineData(ActionState.Run, 0, false)]
    [InlineData(ActionState.Run, 2, false)]
    [InlineData(ActionState.Run, 2, true)]
    [InlineData(ActionState.Sliding, 0, false)]
    [InlineData(ActionState.Sliding, 2, true)]
    public void AuthoredMotion_TakesOwnershipAtStartOrDuringCarry(
        ActionState origin, ushort operationTick, bool lunge)
    {
        CookedTimelineOperation operation = lunge
            ? new CookedForwardLungeOperation(operationTick, AuthoringUnit.MetersPerSecond, 3f, 8)
            : new CookedSetVelocityOperation(operationTick, AuthoringUnit.MetersPerSecond,
                AuthoringVelocityMode.Absolute, 0f, 0f, 3f);
        var def = MakeDefinition(operations: new[] { operation });
        var sim = NewSimulation(def, origin, def.Movement.RunSpeed);
        var before = sim.GetState(1);
        var after = Tick(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1));
        if (operationTick > 0)
        {
            Assert.Equal(lunge, after.SlideAttackCarryActive);
            float speed = (1f - (lunge ? Brake(origin) : 0f)) * def.Movement.RunSpeed;
            TestHelpers.AssertNear(speed, after.VX, .001f);
            TestHelpers.AssertNear(before.PX + speed * Simulation.TickDt, after.PX, .001f);
            for (int tick = 1; tick < operationTick; tick++)
                after = Tick(sim);
        }

        Assert.False(after.SlideAttackCarryActive);
        Assert.Equal(0f, after.VX);
        TestHelpers.AssertNear(3f, after.VZ, .001f);
        before = after;
        for (int tick = 0; tick < 3; tick++)
        {
            after = Tick(sim, TestHelpers.Input(moveX: -1f));
            Assert.False(after.SlideAttackCarryActive);
            Assert.Equal(before.PX, after.PX);
            TestHelpers.AssertNear(3f, after.VZ, .001f);
            TestHelpers.AssertNear(before.PZ + 3f * Simulation.TickDt, after.PZ, .001f);
            before = after;
        }
    }

    [Theory]
    [InlineData(ActionState.Run, 0)]
    [InlineData(ActionState.Run, 2)]
    [InlineData(ActionState.Sliding, 0)]
    [InlineData(ActionState.Sliding, 2)]
    public void SnapshotRestorationMidCarry_ReproducesMotionThroughStoppingAndCompletion(
        ActionState origin, ushort hitstop)
    {
        var def = MakeDefinition();
        var continuous = NewSimulation(def, origin, def.Movement.RunSpeed);
        var before = TestHelpers.TickN(continuous, TestHelpers.Input(activeSlot: AbilitySlots.Slot1), 2);
        continuous.SetState(1, before with { HitstopTicks = hitstop });
        var restored = NewSimulation(def, ActionState.Idle, 0f);
        restored.RestoreSnapshot(new WorldSnapshot(continuous.CaptureSnapshot().Data));
        Assert.True(restored.GetState(1).SlideAttackCarryActive);
        Assert.Equal(before.VX, restored.GetState(1).VX);
        float distance = 0f;

        for (int tick = 1; tick <= 32; tick++)
        {
            var expected = Tick(continuous);
            var actual = Tick(restored);
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
            float speed = MathF.Max(0f, before.VX - Math.Max(0, tick - hitstop) * Brake(origin) * def.Movement.RunSpeed);
            if (tick > hitstop)
                distance += speed * Simulation.TickDt;
            TestHelpers.AssertNear(speed, actual.VX, .001f);
            TestHelpers.AssertNear(before.PX + distance, actual.PX, .001f);
            Assert.Equal(expected.PZ, actual.PZ);
        }
        Assert.Equal(0f, restored.GetState(1).VX);
        Assert.False(restored.GetState(1).SlideAttackCarryActive);
        Assert.Null(restored.GetActiveAbility(1));
    }

    [Theory]
    [InlineData(ActionState.Run)]
    [InlineData(ActionState.Sliding)]
    public void NaturalAbilityCompletion_ClearsCarry(ActionState origin)
    {
        var def = MakeDefinition();
        var sim = NewSimulation(def, origin, def.Movement.RunSpeed);
        Assert.True(Tick(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1)).SlideAttackCarryActive);

        var after = TestHelpers.TickDefault(sim, 30);

        Assert.False(after.SlideAttackCarryActive);
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Null(sim.GetActiveAbility(1));
    }

    [Theory]
    [InlineData(ActionState.Run)]
    [InlineData(ActionState.Sliding)]
    public void ShieldAdmission_ClearsCarryAndBrakes(ActionState origin)
    {
        var def = MakeDefinition(iasaTicks: 1);
        var sim = NewSimulation(def, origin, def.Movement.RunSpeed);
        Assert.True(Tick(sim, TestHelpers.Input(activeSlot: AbilitySlots.Slot1)).SlideAttackCarryActive);
        var before = sim.GetState(1);

        var after = Tick(sim, new InputState { ShieldHeld = true, ShieldPressed = true });

        Assert.Equal(ActionState.Shielding, after.State);
        Assert.False(after.SlideAttackCarryActive);
        Assert.Equal(0f, after.VX);
        Assert.Equal(0f, after.VZ);
        Assert.Equal(before.PX, after.PX);
        Assert.Equal(before.PZ, after.PZ);
        Assert.Null(sim.GetActiveAbility(1));
    }

    private static float Brake(ActionState origin) => origin == ActionState.Run ? .05f : .1f;

    private static CharacterState Tick(ServerSimulation sim, InputState input = default)
    {
        sim.Tick(new Dictionary<ulong, InputState> { [1] = input });
        return sim.GetState(1);
    }

    private static ServerSimulation NewSimulation(CharacterDefinition def, ActionState origin,
        float vx, float vz = 0f, bool grounded = true)
    {
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(10f, 10f) with
        {
            PY = grounded ? TestHelpers.GroundPY(def) : 2f,
            IsGrounded = grounded,
            State = origin,
            VX = vx,
            VZ = vz,
        });
        return sim;
    }

    [Fact]
    public void AttackFromWalkCarriesOnlyWalkingSpeedAndBrakesToRest()
    {
        var def = MakeDefinition();
        var sim = NewSimulation(def, ActionState.Idle, 0f);
        Tick(sim, new InputState { MoveX = 1f, Walk = true });
        Assert.Equal(def.Movement.RunSpeed * .4f, sim.GetState(1).VX, 4);
        for (int tick = 1; tick <= 8; tick++)
        {
            var state = Tick(sim, new InputState { MoveX = 1f, Walk = true,
                ActiveSlot = tick == 1 ? AbilitySlots.Slot1 : AbilitySlots.None });
            Assert.Equal(ActionState.Attacking, state.State);
            Assert.True(state.SlideAttackCarryActive);
            Assert.Equal(MathF.Max(0f, .4f - tick / 20f) * def.Movement.RunSpeed, state.VX, 4);
        }
    }

    private static CharacterDefinition MakeDefinition(bool allowCarry = true, bool preserveMomentum = false,
        ushort iasaTicks = 0, CookedTimelineOperation[]? operations = null)
    {
        var def = TestHelpers.EngineDef;
        def = def with { CookedSlots = def.CookedSlots!.Select(slot => new CookedSlotDefinition(
            slot.Ordinal, slot.Id, slot.IsAir, slot.Name, slot.Description, slot.IconId,
            slot.Behavior, slot.AimMode, slot.CooldownTicks, slot.IsRecoveryMove,
            preserveMomentum, new CookedTimeline(new[]
            {
                new CookedStage(30, iasaTicks, 0, 0, 0, Array.Empty<string>(),
                    operations ?? Array.Empty<CookedTimelineOperation>()),
            }), slot.ChargePool, slot.AimMovement, slot.AimAnimationId, allowCarry)).ToArray() };
        return def;
    }
}
