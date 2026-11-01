using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class FormsCancelTests
{
    private const ulong Fighter = FormsFixture.Nyx, Target = FormsFixture.Dummy;

    private static CharacterDefinition Definition(bool forms = true)
    {
        var body = FormsFixture.Def();
        var grid = body.CookedSlots!.ToArray();
        var hit = new CookedHitbox(AuthoringHitboxShape.Sphere, 3f, 0, 0, 0, 0, 0, 0, null, null,
            4, 20, 0, 0, 12, 3, true, 0);
        CookedTimelineOperation contact = new CookedSpawnHitboxOperation(3, AuthoringUnit.Meters, hit);
        grid[0] = new CookedSlotDefinition(0, "ground.1", false, "Contact", "", "", AuthoringAbilityBehavior.MeleeCombo,
            AuthoringAimMode.None, 90, false, false,
            new CookedTimeline(new[] { new CookedStage(60, 50, 0, 0, 0, Array.Empty<string>(), new[] { contact }) }));
        var real = body with { CookedSlots = grid };
        return forms ? CharacterDefinition.WithForms(real, body.ForForm(1)) : TestHelpers.EngineDef with { CookedSlots = grid };
    }

    private static ServerSimulation Start(bool hit = true, bool shield = false, bool forms = true)
    {
        var sim = FormsFixture.Sim(Definition(forms), dummy: hit);
        for (int i = 0; i < 4; i++)
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [Fighter] = i == 0 ? FormsFixture.Press(AbilitySlots.Normal1) : default,
                [Target] = new() { ShieldHeld = shield },
            });
        return sim;
    }

    private static void Thaw(ServerSimulation sim)
    {
        for (int i = 0; i < 30 && sim.GetState(Fighter).HitstopTicks > 0; i++) FormsFixture.Step(sim);
    }

    [Fact]
    public void FighterHitUnlocksOnlyFBeforeIasaAndRetiresTheOutgoingMove()
    {
        var sim = Start();
        Assert.True(sim.GetState(Target).DamagePercent > 0);
        Thaw(sim);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.E));
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(Fighter).AttackSlot);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        Assert.Equal(AbilitySlots.F, sim.GetState(Fighter).AttackSlot);
        Assert.True(sim.GetState(Fighter).GetCooldown(AbilitySlots.Normal1) > 0);
        FormsFixture.StepThroughSwap(sim);
        Assert.Equal(1, sim.GetState(Fighter).Form.Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhiffOrShieldDoesNotUnlockF(bool shield)
    {
        var sim = Start(hit: shield, shield: shield);
        Thaw(sim);
        if (shield) Assert.Equal(0, sim.GetState(Target).DamagePercent);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(Fighter).AttackSlot);
        Assert.Equal(0, sim.GetState(Fighter).Form.Active);
    }

    [Fact]
    public void FPressedDuringHitstopIsHeldUntilTheFirstUnfrozenAdmission()
    {
        var sim = Start();
        Assert.True(sim.GetState(Fighter).HitstopTicks > 0);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(Fighter).AttackSlot);
        Assert.Equal(AbilitySlots.F, sim.GetState(Fighter).BufferedSlot);
        Thaw(sim);
        FormsFixture.Step(sim);
        Assert.Equal(AbilitySlots.F, sim.GetState(Fighter).AttackSlot);
        Assert.Equal(0, sim.GetState(Fighter).BufferedSlot);
    }

    [Theory]
    [InlineData("Hitstun")]
    [InlineData("BlockStun")]
    [InlineData("LandingLag")]
    public void OtherLocksStillBlockTheConnectedCancel(string kind)
    {
        var sim = Start();
        Thaw(sim);
        var s = sim.GetState(Fighter);
        if (kind == "Hitstun") s.HitstunTicks = 10;
        if (kind == "BlockStun") s.BlockStunTicks = 10;
        if (kind == "LandingLag") s.LandingLagTicks = 10;
        sim.SetState(Fighter, s);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        Assert.NotEqual(AbilitySlots.F, sim.GetState(Fighter).AttackSlot);
    }

    [Fact]
    public void AHitDoesNotGrantSwapCancelsToFightersWithoutForms()
    {
        var sim = Start(forms: false);
        Thaw(sim);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(Fighter).AttackSlot);
    }

    [Fact]
    public void ContactFromAnOlderActivationDoesNotUnlockTheRunningMove()
    {
        var sim = FormsFixture.Sim(Definition());
        var target = sim.GetState(Target);
        target.PZ += 10;
        sim.SetState(Target, target);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.Normal1));
        sim.Resolver.Spawn(new Hitbox
        {
            OwnerId = Fighter, ActivationId = ulong.MaxValue, AttackSlot = AbilitySlots.Normal1,
            X = target.PX, Y = target.PY, Z = target.PZ, Shape = HitboxShape.Sphere, Radius = 1,
            Damage = 4, BaseKnockback = 3, StunTicks = 12, DurationTicks = 1,
        });
        FormsFixture.Step(sim);
        Assert.Contains(sim.LastTickHits, h => h.OwnerEntityId == Fighter);
        Thaw(sim);
        FormsFixture.Step(sim, FormsFixture.Press(AbilitySlots.F));
        Assert.Equal(AbilitySlots.Normal1, sim.GetState(Fighter).AttackSlot);
    }

    [Fact]
    public void RestorePreservesAConnectedActivationAndItsBufferedCancel()
    {
        var continuous = Start();
        FormsFixture.Step(continuous, FormsFixture.Press(AbilitySlots.F));
        var restored = FormsFixture.Sim(Definition());
        var snapshot = continuous.CaptureSnapshot();
        restored.RestoreSnapshot(snapshot);
        Assert.True(snapshot.ContentEquals(restored.CaptureSnapshot()));
        for (int i = 0; i < 50; i++)
        {
            FormsFixture.Step(continuous);
            FormsFixture.Step(restored);
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
        }
        Assert.Equal(1, restored.GetState(Fighter).Form.Active);
    }
}
