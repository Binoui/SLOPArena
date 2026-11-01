using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Nyx's phase 4-6 specials and the three combos on her page (ADR-0051), from her source and from her saved package.
/// They assert which moves connect and in what order, not her spacing or damage tuning.
/// </summary>
public sealed class NyxSpecialsTests
{
    private const ulong Nyx = FormsFixture.Nyx, Dummy = FormsFixture.Dummy;
    private readonly List<SpellResolver.HitResult> _hits = new();
    private readonly ITestOutputHelper _output;

    public NyxSpecialsTests(ITestOutputHelper output) => _output = output;

    private static ServerSimulation Make(byte form, float distance, bool saved)
    {
        ServerSimulation sim;
        if (!saved) sim = FormsFixture.Sim(TestHelpers.ResolveSourceDef("nyx"), form);
        else
        {
            var content = BuiltInContentResolver.Resolve(CharacterClass.Nyx);
            sim = FormsFixture.Sim(content.Definition, form);
            sim.RegisterEntity(Nyx, content.Definition, sim.GetState(Nyx), content.BakedAnimation);
        }
        var dummy = sim.GetState(Dummy);
        dummy.PZ = FormsFixture.Z + distance;
        sim.SetState(Dummy, dummy);
        return sim;
    }

    private void Step(ServerSimulation sim, byte slot = 0)
    {
        FormsFixture.Step(sim, new InputState { ActiveSlot = slot, TargetEntityId = (byte)Dummy });
        foreach (var hit in sim.LastTickHits.Where(h => h.OwnerEntityId == Nyx))
        {
            _hits.Add(hit);
            _output.WriteLine($"tick {hit.MatchTick}, form {hit.Form}, slot {hit.AttackSlot}, damage {hit.Damage}, freeze {hit.HitstopTicks}");
        }
    }

    private static bool Free(ServerSimulation sim)
        => sim.GetActiveAbility(Nyx) == null || Simulation.IsIasaUnlocked(sim.GetState(Nyx), sim.GetActiveMove(Nyx));

    private void Until(ServerSimulation sim, Func<bool> done, int maxTicks)
    {
        if (sim.GetActiveMove(Nyx) is { } move)
            maxTicks = Math.Max(maxTicks, move.Slot.Timeline.Stages.Sum(stage => stage.DurationTicks)
                + move.Slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                    .OfType<CookedSpawnHitboxOperation>().Sum(op => ServerSimulation.ComputeHitstopTicks(op.Hitbox.Damage))
                + move.Slot.Timeline.Stages.SelectMany(stage => stage.Operations)
                    .OfType<CookedStartCapabilityOperation>().Select(op => op.Parameters)
                    .OfType<CookedCommandGrabCapabilityParameters>().Sum(p => ServerSimulation.ComputeHitstopTicks(p.Launch!.Damage))
                + sim.GetState(Nyx).HitstopTicks + 1);
        for (int tick = 0; tick < maxTicks && !done(); tick++) Step(sim);
        Assert.True(done(), $"not reached within {maxTicks} ticks");
    }

    private static bool Perched(ServerSimulation sim)
        => sim.Resolver.GetActiveHitboxes().Any(h => h.OwnerId == Nyx && h.Perch.HasValue && h.AttachedEntityId == Dummy);

    private (byte Slot, byte Form) Hit(int index) => (_hits[index].AttackSlot, _hits[index].Form);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhoneSlapsKeepsTheVictimLockedBetweenAllThreeContacts(bool saved)
    {
        var sim = Make(1, 1.2f, saved);
        Step(sim, AbilitySlots.Normal1);
        int horizon = sim.GetActiveMove(Nyx)!.Value.Stage.DurationTicks * 2;
        for (int tick = 0; tick < horizon && _hits.Count < 3; tick++)
        {
            if (_hits.Count > 0)
            {
                var victim = sim.GetState(Dummy);
                Assert.True(victim.HitstopTicks > 0 || victim.HitstunTicks > 0,
                    $"Victim became actionable after contact {_hits.Count} at tick {sim.CurrentTick}.");
            }
            Step(sim);
        }
        Assert.Equal(3, _hits.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComboOneCrowGraspGoingLivePhoneSlapsPopTheCrow(bool saved)
    {
        var sim = Make(0, 6f, saved);
        Step(sim, AbilitySlots.A);
        Until(sim, () => Perched(sim), 160);
        Until(sim, () => sim.GetActiveAbility(Nyx) == null, 60);
        Step(sim, AbilitySlots.R);
        Until(sim, () => sim.GetActiveAbility(Nyx) == null, 120);
        Assert.Equal(ActionState.Reeled, sim.GetState(Dummy).State);
        Step(sim, AbilitySlots.F);
        Until(sim, () => Free(sim) && sim.GetState(Nyx).Form.Active == 1, 60);
        Step(sim, AbilitySlots.Normal1);
        Until(sim, () => _hits.Any(h => h.AttackSlot == AbilitySlots.A), 90);

        Assert.Equal((AbilitySlots.R, (byte)0), Hit(0));
        Assert.Equal((AbilitySlots.F, (byte)0), Hit(1));
        Assert.Equal((AbilitySlots.Normal1, (byte)1), Hit(2));
        var pop = _hits.Single(h => h.AttackSlot == AbilitySlots.A);
        Assert.Equal(0, pop.Form);
        Assert.False(Perched(sim));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComboTwoUppercutLoggingOffThenTheCrowDivesOnThem(bool saved)
    {
        var sim = Make(1, 1.2f, saved);
        Step(sim, AbilitySlots.Normal2);
        Until(sim, () => Free(sim), 60);
        Step(sim, AbilitySlots.F);
        Until(sim, () => Free(sim) && sim.GetState(Nyx).Form.Active == 0, 60);
        Step(sim, AbilitySlots.A);
        Until(sim, () => Perched(sim), 200);
        Assert.Equal((AbilitySlots.Normal2, (byte)1), Hit(0));
        Assert.Equal((AbilitySlots.F, (byte)1), Hit(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComboThreeTheCrowPerchesSheSwapsAndHerFakeHitPopsIt(bool saved)
    {
        var sim = Make(0, 3f, saved);
        Step(sim, AbilitySlots.A);
        Until(sim, () => Perched(sim), 120);
        Until(sim, () => Free(sim), 60);
        Step(sim, AbilitySlots.F);
        Until(sim, () => Free(sim) && sim.GetState(Nyx).Form.Active == 1, 60);
        Assert.True(Perched(sim), "Going Live is a real-form hit: the crow stays");
        Step(sim, AbilitySlots.Normal1);
        Until(sim, () => _hits.Any(h => h.AttackSlot == AbilitySlots.A), 60);
        var slap = _hits.First(h => h.AttackSlot == AbilitySlots.Normal1);
        var pop = _hits.Single(h => h.AttackSlot == AbilitySlots.A);
        Assert.Equal(1, slap.Form);
        Assert.Equal(0, pop.Form);
        Assert.True(pop.MatchTick >= slap.MatchTick + slap.HitstopTicks, "the pop waits out the slap's hitstop");
        Assert.False(Perched(sim));
    }

    [Fact]
    public void HerSpecialsCookWithTheirNewBlocks()
    {
        var def = TestHelpers.ResolveSourceDef("nyx");
        var real = def.ForForm(0);
        var fake = def.ForForm(1);
        foreach (bool air in new[] { false, true })
        {
            var grasp = real.GetCookedSlotAbility(AbilitySlots.R, air)!.Timeline.Stages[0].Operations
                .OfType<CookedStartCapabilityOperation>().Single();
            Assert.IsType<CookedTetherCapabilityParameters>(grasp.Parameters);
            Assert.NotNull(((CookedTetherCapabilityParameters)grasp.Parameters).AtTarget?.Unmarked);
            var crow = real.GetCookedSlotAbility(AbilitySlots.A, air)!.Timeline.Stages[0].Operations
                .OfType<CookedSpawnProjectileOperation>().Single().Projectile;
            Assert.Null(crow.Homing);
            Assert.NotNull(crow.Perch?.Dive);
            Assert.Equal(CharacterPackageCompiler.CommandGrabCapabilityId, fake.GetCookedSlotAbility(AbilitySlots.R, air)!
                .Timeline.Stages[0].Operations.OfType<CookedStartCapabilityOperation>().Single().CapabilityId);
        }
    }
}
