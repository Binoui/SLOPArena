using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class AimMovementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MobileHold_StillAllowsSteering(bool airborne)
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        var address = CanonicalSlotProjection.All[airborne ? CanonicalSlotProjection.VariantSlotCount : 0];
        slots[address.Ordinal] = new CookedSlotDefinition(address.Ordinal, address.Id, airborne, "Mobile hold", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.GroundVector, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(60, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<CookedTimelineOperation>()) }),
            aimMovement: AuthoringAimMovementMode.Mobile);
        def = def with { CookedSlots = slots };
        var s = TestHelpers.PlayerState(20f, 20f);
        s.PY = TestHelpers.GroundPY(def) + (airborne ? 5f : 0f);
        s.IsGrounded = !airborne;
        s.State = ActionState.Aiming;
        s.AttackSlot = AbilitySlots.Slot1;

        Simulation.SimulateTick(ref s, def, new InputState { IsAiming = true, MoveX = 1f },
            TestHelpers.TestArena(), out _, out _, DownActionTuning.Default, verticalMotionOwned: false, move: def.GetCookedSlotAbility(s.AttackSlot, !s.IsGrounded));

        Assert.True(s.PX > 20f);
        Assert.True(s.VX > 0f);
        Assert.Equal(ActionState.Aiming, s.State);
    }

    [Fact]
    public void FixedHold_Grounded_StopsHorizontalMomentumAndSteering()
    {
        var def = TestHelpers.EngineDef;
        var s = TestHelpers.PlayerState(20f, 20f);
        s.PY = TestHelpers.GroundPY(def);
        s.State = ActionState.Aiming;
        s.AttackSlot = AbilitySlots.Slot1;
        s.VX = 6f;
        s.VZ = -4f;
        var input = new InputState { IsAiming = true, MoveX = 1f, MoveY = 1f, Jump = true };

        Simulation.SimulateTick(ref s, def, input, TestHelpers.TestArena(),
            out _, out _, DownActionTuning.Default, verticalMotionOwned: false, move: def.GetCookedSlotAbility(s.AttackSlot, !s.IsGrounded));

        Assert.Equal(20f, s.PX);
        Assert.Equal(20f, s.PZ);
        Assert.Equal(0f, s.VX);
        Assert.Equal(0f, s.VZ);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.Equal((byte)2, s.JumpsLeft);
    }

    [Theory]
    [InlineData(6f, -4f)]
    [InlineData(-4f, 6f)]
    [InlineData(0f, 0f)]
    public void FixedHold_Airborne_CoastsWithoutSteeringOrJumping(float vx, float vz)
    {
        var def = TestHelpers.EngineDef;
        var s = TestHelpers.PlayerState(20f, 20f);
        s.PY = TestHelpers.GroundPY(def) + 5f;
        s.IsGrounded = false;
        s.State = ActionState.Aiming;
        s.AttackSlot = AbilitySlots.Slot1;
        s.VX = vx;
        s.VZ = vz;
        s.VY = 3f;
        s.FacingYaw = 0.75f;
        var coasting = s;
        coasting.State = ActionState.Idle;
        coasting.AttackSlot = 0;
        var arena = TestHelpers.TestArena();

        Simulation.SimulateTick(ref coasting, def, default, arena,
            out _, out _, DownActionTuning.Default, verticalMotionOwned: false, move: def.GetCookedSlotAbility(s.AttackSlot, !s.IsGrounded));
        Simulation.SimulateTick(ref s, def,
            new InputState { IsAiming = true, MoveX = 1f, MoveY = 1f, Jump = true }, arena,
            out _, out _, DownActionTuning.Default, verticalMotionOwned: false, move: def.GetCookedSlotAbility(s.AttackSlot, !s.IsGrounded));

        Assert.Equal(coasting.VX, s.VX);
        Assert.Equal(coasting.VZ, s.VZ);
        Assert.Equal(coasting.PX, s.PX);
        Assert.Equal(coasting.PZ, s.PZ);
        Assert.Equal(coasting.PY, s.PY);
        Assert.Equal(coasting.VY, s.VY);
        Assert.Equal(0.75f, s.FacingYaw);
        Assert.Equal((byte)2, s.JumpsLeft);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.False(s.IsGrounded);
    }

    [Theory]
    [InlineData(AbilitySlots.R)]
    public void MankiAirborneSpecial_PreservesMomentumThroughCompletion(byte slot)
    {
        var def = TestHelpers.ResolveSourceDef("manki");
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState(20f, 20f);
        s.PY = TestHelpers.GroundPY(def) + 20f;
        s.IsGrounded = false;
        s.VX = 6f;
        s.VZ = -4f;
        s.VY = 5f;
        sim.RegisterEntity(1, def, s);
        var hold = new InputState
        {
            ActiveSlot = slot, IsAiming = true, MoveX = -1f, MoveY = 1f,
            AimYaw = 9000, AimPitch = 3000, AimDistance = 400,
        };

        var held = TestHelpers.TickHold(sim, hold, 10);

        Assert.Equal(slot == AbilitySlots.R ? ActionState.Attacking : ActionState.Aiming, held.State);
        Assert.True(held.PX > s.PX);
        Assert.True(held.PZ < s.PZ);
        Assert.True(held.VX > 0f);
        Assert.True(held.VZ < 0f);
        Assert.False(held.IsGrounded);

        bool fired = false;
        var current = held;
        for (int tick = 0; tick < 80 && current.AttackSlot != 0; tick++)
        {
            var previous = current;
            current = TestHelpers.TickDefault(sim, 1);
            fired |= sim.Resolver.GetActiveHitboxes().Any(h => h.AttackSlot == slot);
            Assert.True(current.VX > 0f, $"lost X momentum on release tick {tick}");
            Assert.True(current.VZ < 0f, $"lost Z momentum on release tick {tick}");
            Assert.True(current.PX > previous.PX);
            Assert.True(current.PZ < previous.PZ);
            Assert.False(current.IsGrounded);
        }

        Assert.True(fired);
        Assert.Equal(ActionState.Idle, current.State);
        Assert.Equal((byte)0, current.AttackSlot);
    }

    [Theory]
    [InlineData(AbilitySlots.R)]
    public void MankiAirborneSpecial_LandingStopsDriftAndKeepsAction(byte slot)
    {
        var def = TestHelpers.ResolveSourceDef("manki");
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState(20f, 20f);
        s.PY = TestHelpers.GroundPY(def) + 0.05f;
        s.IsGrounded = false;
        s.VX = 6f;
        s.VZ = -4f;
        s.VY = -5f;
        sim.RegisterEntity(1, def, s);

        var landed = TestHelpers.TickHold(sim,
            new InputState { ActiveSlot = slot, IsAiming = true, MoveX = 1f, MoveY = 1f }, 3);

        Assert.True(landed.IsGrounded);
        Assert.Equal(slot == AbilitySlots.R ? ActionState.Attacking : ActionState.Aiming, landed.State);
        Assert.Equal(slot, landed.AttackSlot);
        Assert.Equal(0f, landed.VX);
        Assert.Equal(0f, landed.VZ);
        Assert.True(landed.PX > s.PX);
        Assert.True(landed.PZ < s.PZ);
    }

    [Theory]
    [InlineData(CharacterClass.Manki, AbilitySlots.R)]
    [InlineData(CharacterClass.Bonk, AbilitySlots.E)]
    public void AuthoredAimedHolds_UseFixedMovementOnGroundAndInAir(CharacterClass character, byte slot)
    {
        var def = TestHelpers.ResolveSourceDef(character.ToString().ToLowerInvariant());
        Assert.Equal(AuthoringAimMovementMode.Fixed, (def.GetCookedSlotAbility(slot, airborne: false)?.AimMovement ?? AuthoringAimMovementMode.Fixed));
        Assert.Equal(AuthoringAimMovementMode.Fixed, (def.GetCookedSlotAbility(slot, airborne: true)?.AimMovement ?? AuthoringAimMovementMode.Fixed));
    }

    [Fact]
    public void QuickDrawIgnoresTheFormerAimHold()
    {
        var def = TestHelpers.ResolveSourceDef("wibou");
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(20, 20); state.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, state);
        var input = new InputState { ActiveSlot = AbilitySlots.R, IsAiming = true, AimYaw = 9000, MoveX = 1 };
        TestHelpers.TickHold(sim, input, 8);
        var traveled = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, traveled.State);
        Assert.False(traveled.IsAiming);
        Assert.InRange(traveled.PZ, 23.499f, 23.501f);
        Assert.InRange(traveled.PX, 19.999f, 20.001f);
    }

}
