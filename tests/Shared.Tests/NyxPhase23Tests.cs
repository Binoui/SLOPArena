using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class NyxPhase23Tests
{
    private static ServerSimulation Make(byte form, bool target = false, bool saved = false)
    {
        if (!saved) return FormsFixture.Sim(TestHelpers.ResolveSourceDef("nyx"), form, dummy: target);
        var content = BuiltInContentResolver.Resolve(CharacterClass.Nyx);
        var sim = FormsFixture.Sim(content.Definition, form, dummy: target);
        sim.RegisterEntity(FormsFixture.Nyx, content.Definition, sim.GetState(FormsFixture.Nyx), content.BakedAnimation);
        return sim;
    }

    private static void Airborne(ServerSimulation sim)
    {
        var s = sim.GetState(FormsFixture.Nyx);
        s.PY = 5; s.IsGrounded = false; s.JumpsLeft = 0; s.AirTimeTicks = 100; s.VY = -3;
        sim.SetState(FormsFixture.Nyx, s);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatScatterCarriesUpAndTowardTheStickWithoutGrantingInvulnerability(bool saved)
    {
        var sim = Make(0, saved: saved);
        Airborne(sim);
        var start = sim.GetState(FormsFixture.Nyx);
        FormsFixture.Step(sim, new InputState { ActiveSlot = AbilitySlots.E, MoveY = -1 });
        for (int i = 0; i < 20; i++) FormsFixture.Step(sim, new InputState { MoveY = -1 });
        var travel = sim.GetState(FormsFixture.Nyx);
        Assert.True(travel.PY > start.PY + 1);
        Assert.True(travel.PZ < start.PZ - 1);
        Assert.False(sim.GetActiveAbility(FormsFixture.Nyx)!.HasArmor);
        Assert.Equal(0, travel.InvincibilityTicks);
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = FormsFixture.Dummy, Shape = HitboxShape.Sphere, Radius = 2,
            X = travel.PX, Y = travel.PY, Z = travel.PZ,
            Damage = 7, BaseKnockback = 5, KnockbackAngle = 30, StunTicks = 20, DurationTicks = 2,
        });
        FormsFixture.Step(sim);
        Assert.True(sim.GetState(FormsFixture.Nyx).DamagePercent > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RingLightRiseLaunchesWithoutJumpsAndKeepsSteeringAfterLaunch(bool saved)
    {
        var sim = Make(1, saved: saved);
        Airborne(sim);
        var start = sim.GetState(FormsFixture.Nyx);
        var recovery = sim.GetDefinition(FormsFixture.Nyx).ForForm(1)
            .GetCookedSlotAbility(AbilitySlots.E, true)!.Timeline.Stages[0].Operations
            .OfType<CookedStartCapabilityOperation>().Single();
        var startup = ((CookedAimedRecoveryCapabilityParameters)recovery.Parameters).StartupTicks;
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.E));
        for (int i = 1; i < startup; i++) FormsFixture.Step(sim);
        var launch = sim.GetState(FormsFixture.Nyx);
        Assert.True(launch.VY > 0);
        Assert.Equal(0, launch.JumpsLeft);
        for (int i = 0; i < 10; i++) FormsFixture.Step(sim, new InputState { MoveX = 1 });
        var right = sim.GetState(FormsFixture.Nyx);
        Assert.True(right.PY > start.PY);
        Assert.True(right.VX > launch.VX);
        for (int i = 0; i < 20; i++) FormsFixture.Step(sim, new InputState { MoveX = -1 });
        Assert.True(sim.GetState(FormsFixture.Nyx).VX < 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RingLightTurnsInPlaceAndDazzlesWithoutLaunching(bool behind, bool saved)
    {
        var sim = Make(1, target: true, saved: saved);
        var target = sim.GetState(FormsFixture.Dummy);
        target.PZ = FormsFixture.Z + (behind ? -2.8f : 2.8f);
        target.FacingYaw = behind ? 0f : MathF.PI;
        sim.SetState(FormsFixture.Dummy, target);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.A));
        var hits = new List<SpellResolver.HitResult>();
        float maxTravel = 0;
        ushort dazzle = sim.GetDefinition(FormsFixture.Nyx)!.ForForm(1).GetCookedSlotAbility(AbilitySlots.A, false)!
            .Timeline.Stages[0].Operations.OfType<CookedSpawnHitboxOperation>().Max(op => op.Hitbox.FixedHitstunTicks);
        for (int i = 0; i < 60; i++)
        {
            FormsFixture.Step(sim);
            maxTravel = MathF.Max(maxTravel, MathF.Abs(sim.GetState(FormsFixture.Nyx).PZ - FormsFixture.Z));
            hits.AddRange(sim.LastTickHits.Where(h => h.OwnerEntityId == FormsFixture.Nyx));
            var victim = sim.GetState(FormsFixture.Dummy);
            if (victim.HitstunTicks > 0)
            {
                Assert.Equal((0f, 0f, 0f), (victim.KVX, victim.KVY, victim.KVZ));
                Assert.InRange(victim.HitstunTicks, 1, dazzle);
                Assert.True(maxTravel < .01f, $"Ring Light moved her {maxTravel} m: it has no lunge");
                Assert.Single(hits);
                return;
            }
        }
        Assert.Fail("Ring Light never put the target into fixed Hitstun.");
    }
}
