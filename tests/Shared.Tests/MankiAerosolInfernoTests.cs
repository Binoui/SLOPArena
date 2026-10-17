using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class MankiAerosolInfernoTests
{
    // The charge cue (rattling can, pilot flame) starts with the charge; the flame stream starts
    // with the hitbox on fire frame eight.
    private const string Charge = "presentation.manki.aerosol-inferno.charge";
    private const string Stream = "presentation.manki.aerosol-inferno.start";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TapWaitsForMinimumChargeThenStartsHitboxAndVfxOnFireFrameEight(bool airborne)
    {
        var def = TestHelpers.ResolveSourceDef("manki");
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with
        {
            PY = airborne ? 8f : TestHelpers.GroundPY(def),
            IsGrounded = !airborne,
        };
        sim.RegisterEntity(1, def, state);

        var charging = new List<TimelinePresentationEvent>();
        for (int frame = 0; frame < 18; frame++)
        {
            sim.Tick(new() { [1] = frame == 0 ? new InputState { ActiveSlot = AbilitySlots.F } : default });
            Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
            Assert.Equal((ushort)(frame + 1), sim.GetState(1).ChargeTicks);
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            charging.AddRange(sim.GetPresentationEvents(clear: true));
        }
        Assert.Equal(Charge, Assert.Single(charging).PresentationId);

        sim.Tick(new());
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal((ushort)0, sim.GetState(1).AttackElapsedTicks);
        for (int fireFrame = 1; fireFrame < 8; fireFrame++)
        {
            sim.Tick(new());
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            Assert.Empty(sim.GetPresentationEvents(clear: true));
        }

        sim.Tick(new());
        Assert.Equal((ushort)8, sim.GetState(1).AttackElapsedTicks);
        var hitbox = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(HitboxShape.Capsule, hitbox.Shape);
        Assert.Equal(0.42f, hitbox.Radius, 4);
        var flame = Assert.Single(sim.GetPresentationEvents(clear: true));
        Assert.Equal(Stream, flame.PresentationId);
        Assert.Equal((ushort)44, flame.Placement!.DurationTicks);

        Assert.Equal((ushort)44, hitbox.DurationTicks);
        for (int fireFrame = 9; fireFrame < 51; fireFrame++)
        {
            sim.Tick(new());
            Assert.Single(sim.Resolver.GetActiveHitboxes());
            Assert.Empty(sim.GetPresentationEvents(clear: true));
        }
        // The resolver consumes the final active collision tick before removing it.
        sim.Tick(new());
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        sim.Tick(new());
        Assert.Equal(ActionState.Idle, sim.GetState(1).State);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Theory]
    [InlineData(18, 10f, 0.6f)]
    [InlineData(29, 10f, 0.6f)]
    [InlineData(30, 14f, 0.8f)]
    [InlineData(41, 14f, 0.8f)]
    [InlineData(42, 18f, 1f)]
    [InlineData(53, 18f, 1f)]
    [InlineData(54, 22f, 1.25f)]
    [InlineData(65, 22f, 1.25f)]
    [InlineData(66, 26f, 1.5f)]
    [InlineData(120, 26f, 1.5f)]
    public void ReleaseSelectsDamageAndMatchingFlameSizeAtEachChargeBoundary(int heldTicks, float damage, float scale)
    {
        foreach (bool airborne in new[] { false, true })
        {
            var def = TestHelpers.ResolveSourceDef("manki");
            var sim = TestHelpers.MakeSim();
            sim.RegisterEntity(1, def, TestHelpers.PlayerState() with
            {
                PY = airborne ? 8f : TestHelpers.GroundPY(def), IsGrounded = !airborne,
            });
            for (int frame = 0; frame < heldTicks; frame++)
                sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.F, IsAiming = true } });
            sim.Tick(new());
            Assert.Equal((ushort)System.Math.Min(heldTicks, 66), sim.GetState(1).ChargeTicks);
            TestHelpers.TickDefault(sim, 7);
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            Assert.DoesNotContain(sim.GetPresentationEvents(clear: true), e => e.PresentationId == Stream);
            sim.Tick(new());
            var hitbox = Assert.Single(sim.Resolver.GetActiveHitboxes());
            Assert.Equal(damage, hitbox.Damage);
            Assert.Equal(0.7f * scale, hitbox.Radius, 4);
            Assert.Equal(3f * scale, hitbox.EndZ - hitbox.Z, 4);
            Assert.Equal(0.55f + 0.7f * scale, hitbox.Z - sim.GetState(1).PZ, 4);
            var flame = Assert.Single(sim.GetPresentationEvents(clear: true));
            Assert.Equal(scale, flame.Placement!.LocalScaleX);
            Assert.Equal(scale, flame.Placement.LocalScaleY);
            Assert.Equal(scale, flame.Placement.LocalScaleZ);
            Assert.Equal(0.55f, flame.Placement.LocalPositionZ);
            Assert.Equal((ushort)44, flame.Placement.DurationTicks);
        }
    }

    [Theory]
    [InlineData(18, 0f)]
    [InlineData(66, 26f)]
    public void LongerChargeReachesDistantTargetWithItsSelectedDamage(int heldTicks, float expectedDamage)
    {
        var def = TestHelpers.ResolveSourceDef("manki");
        var targetDef = TestHelpers.EngineDef;
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        sim.RegisterEntity(2, targetDef, TestHelpers.NpcState(0f, 4.5f) with { PY = TestHelpers.GroundPY(targetDef) });
        for (int frame = 0; frame < heldTicks; frame++)
            sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.F, IsAiming = true } });
        sim.Tick(new());
        TestHelpers.TickDefault(sim, 8);
        Assert.Equal(expectedDamage, sim.GetState(2).DamagePercent);
        if (expectedDamage > 0f)
        {
            Assert.True(sim.GetState(2).HitstopTicks > 0);
            TestHelpers.TickDefault(sim, 70);
            Assert.Equal(expectedDamage, sim.GetState(2).DamagePercent);
        }
    }

    [Fact]
    public void HoldingBeyondMinimumStillWaitsForRelease()
    {
        var def = TestHelpers.ResolveSourceDef("manki");
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        for (int frame = 0; frame < 90; frame++)
        {
            sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.F, IsAiming = true } });
            Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            Assert.DoesNotContain(sim.GetPresentationEvents(clear: true), e => e.PresentationId == Stream);
        }
        sim.Tick(new());
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);
        Assert.Equal((ushort)0, sim.GetState(1).AttackElapsedTicks);
    }

    [Fact]
    public void InterruptingMinimumChargeCancelsQueuedFire()
    {
        var def = TestHelpers.ResolveSourceDef("manki");
        var sim = TestHelpers.MakeSim();
        sim.RegisterEntity(1, def, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(def) });
        sim.Tick(new() { [1] = new InputState { ActiveSlot = AbilitySlots.F } });
        TestHelpers.TickDefault(sim, 8);
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);
        sim.SetState(1, sim.GetState(1) with { State = ActionState.Hitstun, HitstunTicks = 4 });
        for (int frame = 0; frame < 80; frame++)
        {
            sim.Tick(new());
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
            Assert.DoesNotContain(sim.GetPresentationEvents(clear: true), e => e.PresentationId == Stream);
        }
        Assert.Equal((ushort)0, sim.GetState(1).ChargeTicks);
    }
}
