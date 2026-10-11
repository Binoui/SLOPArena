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
        def.CookedSlots = null;
        def.Slot1 = def.AirSlot1 = new AbilitySpec { AimMovement = AimMovementMode.Mobile };
        var s = TestHelpers.PlayerState(20f, 20f);
        s.PY = TestHelpers.GroundPY(def) + (airborne ? 5f : 0f);
        s.IsGrounded = !airborne;
        s.State = ActionState.Aiming;
        s.AttackSlot = AbilitySlots.Slot1;

        Simulation.SimulateTick(ref s, def, new InputState { IsAiming = true, MoveX = 1f },
            TestHelpers.TestArena(), out _, out _, DownActionTuning.Default, verticalMotionOwned: false);

        Assert.True(s.PX > 20f);
        Assert.True(s.VX > 0f);
        Assert.Equal(ActionState.Aiming, s.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedHold_StopsHorizontalMomentumAndSteering(bool airborne)
    {
        var def = TestHelpers.EngineDef;
        var s = TestHelpers.PlayerState(20f, 20f);
        s.PY = TestHelpers.GroundPY(def) + (airborne ? 5f : 0f);
        s.IsGrounded = !airborne;
        s.State = ActionState.Aiming;
        s.AttackSlot = AbilitySlots.Slot1;
        s.VX = 6f;
        s.VZ = -4f;
        var input = new InputState { IsAiming = true, MoveX = 1f, MoveY = 1f, Jump = true };

        Simulation.SimulateTick(ref s, def, input, TestHelpers.TestArena(),
            out _, out _, DownActionTuning.Default, verticalMotionOwned: false);

        Assert.Equal(20f, s.PX);
        Assert.Equal(20f, s.PZ);
        Assert.Equal(0f, s.VX);
        Assert.Equal(0f, s.VZ);
        Assert.Equal(ActionState.Aiming, s.State);
        Assert.Equal((byte)2, s.JumpsLeft);
        if (airborne)
            Assert.False(s.IsGrounded); // A stationary aim does not pin vertical physics.
    }

    [Theory]
    [InlineData(CharacterClass.Manki, AbilitySlots.A)]
    [InlineData(CharacterClass.Manki, AbilitySlots.R)]
    [InlineData(CharacterClass.Manki, AbilitySlots.F)]
    [InlineData(CharacterClass.Bonk, AbilitySlots.E)]
    [InlineData(CharacterClass.Wibou, AbilitySlots.R)]
    public void AuthoredAimedHolds_UseFixedMovementOnGroundAndInAir(CharacterClass character, byte slot)
    {
        var def = TestHelpers.ResolveSourceDef(character.ToString().ToLowerInvariant());
        Assert.Equal(AimMovementMode.Fixed, def.GetAimMovementMode(slot, airborne: false));
        Assert.Equal(AimMovementMode.Fixed, def.GetAimMovementMode(slot, airborne: true));
    }

    [Fact]
    public void WibouDash_HoldStopsMovementAndReleaseStillTravels()
    {
        var def = TestHelpers.ResolveSourceDef("wibou");
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState(20f, 20f);
        s.PY = TestHelpers.GroundPY(def);
        sim.RegisterEntity(1, def, s);
        var input = new InputState
        {
            ActiveSlot = AbilitySlots.R, IsAiming = true, AimYaw = 9000,
            MoveX = 1f, MoveY = 1f,
        };
        TestHelpers.TickHold(sim, input, 30);
        var held = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, held.State);
        Assert.Equal(20f, held.PX);
        Assert.Equal(20f, held.PZ);
        Assert.Equal(0f, held.VX);
        Assert.Equal(0f, held.VZ);

        TestHelpers.TickN(sim, new InputState { AimYaw = -9000 }, 1);
        var released = sim.GetState(1);
        Assert.Equal(ActionState.Attacking, released.State);
        Assert.True(released.VX > 0f);
        var traveling = TestHelpers.TickDefault(sim, 1);
        Assert.True(traveling.PX > held.PX);
    }
}
