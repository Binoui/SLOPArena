using System;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ChargedProjectileTests
{
    [Theory]
    [InlineData(1, .5f)]
    [InlineData(19, .5f)]
    [InlineData(20, .75f)]
    [InlineData(44, .75f)]
    [InlineData(45, 1f)]
    [InlineData(60, 1f)]
    [InlineData(180, 1f)]
    public void ChargeBoundariesSelectRadiusAndCapWithoutAutoRelease(int ticks, float radius)
    {
        var def = Definition();
        var sim = TestHelpers.MakeSim();
        var start = TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(def) };
        sim.RegisterEntity(1, def, start);
        TestHelpers.TickHold(sim, new InputState
        {
            ActiveSlot = AbilitySlots.A, IsAiming = true, MoveX = 1f, MoveY = 1f,
        }, ticks);
        var held = sim.GetState(1);
        Assert.Equal(ActionState.Aiming, held.State);
        Assert.Equal((ushort)Math.Min(60, ticks), held.ChargeTicks);
        Assert.Equal(start.PX, held.PX);
        Assert.Equal(start.PZ, held.PZ);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        // Holding plays only the charge cue; nothing from the release has fired.
        Assert.All(sim.GetPresentationEvents(),
            e => Assert.Equal("presentation.fightguy.ki-shot.charge", e.PresentationId));

        TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        TestHelpers.TickDefault(sim, 7);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        TestHelpers.TickDefault(sim, 1);
        var shot = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(radius, shot.Radius);
        Assert.Equal(20f, shot.VZ, 3);
        Assert.Equal(6f, shot.Damage);
        Assert.Equal(0, shot.VisualOperationIndex);
        TestHelpers.TickDefault(sim, 5);
        Assert.Single(sim.Resolver.GetActiveHitboxes());
        TestHelpers.TickDefault(sim, 40);
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
    }

    [Fact]
    public void TapReleasesTheSmallestShotAndRetainsTargetAssistance()
    {
        var def = Definition();
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, def, TestHelpers.PlayerState(25f, 20f) with { EntityId = 2, PY = TestHelpers.GroundPY(def) });
        TestHelpers.TickN(sim, new InputState { ActiveSlot = AbilitySlots.A }, 9);
        var shot = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(.5f, shot.Radius);
        Assert.True(shot.VX > 0f);
        Assert.InRange(MathF.Abs(shot.VZ), 0f, .001f);
    }

    [Fact]
    public void GroundAndAirUseTheSameChargePolicyAndLoop()
    {
        var def = Definition();
        foreach (bool air in new[] { false, true })
        {
            var slot = def.GetCookedSlotAbility(AbilitySlots.A, air)!;
            Assert.Equal("anim.ki-shot-loop", slot.AimAnimationId);
            Assert.Equal(AuthoringAimMovementMode.Fixed, slot.AimMovement);
            var charge = Assert.IsType<CookedChargedProjectileCapabilityParameters>(
                slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                    .OfType<CookedStartCapabilityOperation>().Single().Parameters);
            Assert.Equal((ushort)20, charge.Tier2Ticks);
            Assert.Equal((ushort)45, charge.Tier3Ticks);
        }
    }

    [Fact]
    public void AirChargeDoesNotDriftHorizontallyOrEndOnLanding()
    {
        var def = Definition();
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState(20f, 20f) with
        {
            PY = TestHelpers.GroundPY(def) + .01f, IsGrounded = false, VY = -1f, VX = 6f,
        };
        sim.RegisterEntity(1, def, state);
        TestHelpers.TickHold(sim, new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true, MoveX = 1f }, 90);
        var held = sim.GetState(1);
        Assert.True(held.IsGrounded);
        Assert.Equal(ActionState.Aiming, held.State);
        Assert.Equal(state.PX, held.PX);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    private static CharacterDefinition Definition()
    {
        var def = TestHelpers.ResolveSourceDef("fightguy");
        def.Class = CharacterClass.FightGuy;
        return def;
    }
}
