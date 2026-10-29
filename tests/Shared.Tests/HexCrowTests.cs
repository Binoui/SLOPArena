using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// A two-form engine fixture for perching projectiles (ADR-0051, Nyx's Hex Crow): form 0's A throws a homing crow
/// that perches, each form has a jab, and F swaps. Values are fixture values, not her tuning.
/// </summary>
internal static class CrowFixture
{
    public const ulong Nyx = 1, Dummy = 2, Other = 3;
    public const ushort SpawnTick = 2, PerchTicks = 240, MaxFlight = 150, SwapTick = 2;
    public const float Speed = 9f, TurnDegrees = 4f, PopDamage = 6f, JabDamage = 5f;
    public const float X = 100f, Z = 100f;

    public static CharacterDefinition Def()
    {
        var body = TestHelpers.EngineDef;
        return CharacterDefinition.WithForms(body with { CookedSlots = Grid(0) }, body with { CookedSlots = Grid(1) });
    }

    public static CookedProjectile Crow => new(0f, 0.3f, 0.6f, Speed, 0f, 0.3f, 0f, 0f, 0f, 0f, 0, MaxFlight, 0f, null,
        ProjectilePerchContract.Cook(new ProjectileHomingSource(TurnDegrees)),
        new CookedProjectilePerch(PerchTicks, new CookedProjectilePop(PopDamage, 60f, 6f, 20f, 15)));

    private static CookedSlotDefinition[] Grid(byte form)
    {
        var slots = CanonicalSlotProjection.All.Select(a => Slot(a, form, AuthoringAbilityBehavior.MeleeCombo, 30)).ToArray();
        var jab = new CookedHitbox(AuthoringHitboxShape.Sphere, 1.2f, 0f, 0f, 1.0f, 0f, 0f, 0f, null, null,
            JabDamage, 30f, 5f, 10f, 10, 4, true, 0);
        Set(slots, AbilitySlots.Slot1, form, AuthoringAbilityBehavior.MeleeCombo, 20,
            new CookedSpawnHitboxOperation(3, AuthoringUnit.Meters, jab));
        foreach (bool air in new[] { false, true })
            Set(slots, AbilitySlots.F, form, AuthoringAbilityBehavior.MeleeCombo, 10, air,
                new CookedSwapFormOperation(SwapTick, AuthoringUnit.Ticks));
        if (form == 0)
            Set(slots, AbilitySlots.A, form, AuthoringAbilityBehavior.AimedProjectile, 20,
                new CookedSpawnProjectileOperation(SpawnTick, AuthoringUnit.Meters, Crow));
        return slots;
    }

    private static void Set(CookedSlotDefinition[] slots, byte wire, byte form, AuthoringAbilityBehavior behavior,
        ushort duration, params CookedTimelineOperation[] operations)
        => Set(slots, wire, form, behavior, duration, false, operations);

    private static void Set(CookedSlotDefinition[] slots, byte wire, byte form, AuthoringAbilityBehavior behavior,
        ushort duration, bool air, params CookedTimelineOperation[] operations)
    {
        Assert.True(CanonicalSlotProjection.TryGet(wire, air, out var address));
        slots[address.Ordinal] = Slot(address, form, behavior, duration, operations);
    }

    private static CookedSlotDefinition Slot(SlotAddress address, byte form, AuthoringAbilityBehavior behavior,
        ushort duration, params CookedTimelineOperation[] operations)
        => new(address.Ordinal, address.Id, address.IsAirborne, address.Id, "", "", behavior, AuthoringAimMode.None, 0,
            false, false, new CookedTimeline(new[] { new CookedStage(duration, 0, 0, 0, 0, Array.Empty<string>(), operations) }),
            form: form);

    /// <summary>Nyx at (X, Z) facing +Z, a dummy <paramref name="dummyDistance"/> ahead facing her.</summary>
    public static ServerSimulation Sim(float dummyDistance = 6f, byte form = 0, CharacterDefinition? def = null)
    {
        def ??= Def();
        var arena = TestHelpers.TestArena();
        arena.KillTop = 40f;
        var sim = TestHelpers.MakeSim(arena);
        var me = TestHelpers.PlayerState(X, Z);
        me.PY = TestHelpers.GroundPY(def);
        me.Form.Active = form;
        sim.RegisterEntity(Nyx, def, me);
        var target = TestHelpers.NpcState(X, Z + dummyDistance);
        target.EntityId = Dummy;
        target.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        target.FacingYaw = MathF.PI;
        sim.RegisterEntity(Dummy, TestHelpers.EngineDef, target);
        return sim;
    }

    public static void Step(ServerSimulation sim, InputState nyx = default, InputState dummy = default, List<SpellResolver.HitResult>? hits = null)
    {
        sim.Tick(new Dictionary<ulong, InputState> { [Nyx] = nyx, [Dummy] = dummy });
        hits?.AddRange(sim.LastTickHits);
    }

    public static InputState Press(byte slot) => new() { ActiveSlot = slot, TargetEntityId = (byte)Dummy };

    public static IEnumerable<Hitbox> Crows(ServerSimulation sim)
        => sim.Resolver.GetActiveHitboxes().Where(h => h.OwnerId == Nyx && h.Perch.HasValue);

    public static void Throw(ServerSimulation sim)
    {
        int before = Crows(sim).Count();
        Step(sim, Press(AbilitySlots.A));
        for (int i = 0; i < SpawnTick && Crows(sim).Count() == before; i++) Step(sim);
        Assert.Equal(before + 1, Crows(sim).Count());
    }

    /// <summary>Off the arena's floor and below its kill height: the next tick knocks them out.</summary>
    public static void KnockOut(ServerSimulation sim, ulong id) => Teleport(sim, id, -50f, Z, y: -100f);

    /// <summary>Throws a crow and steps until it perches on the dummy.</summary>
    public static Hitbox Perched(ServerSimulation sim, List<SpellResolver.HitResult>? hits = null)
    {
        Throw(sim);
        for (int i = 0; i < MaxFlight && !Crows(sim).Any(h => h.AttachedEntityId == Dummy); i++) Step(sim, hits: hits);
        return Assert.Single(Crows(sim), h => h.AttachedEntityId == Dummy);
    }

    public static void Teleport(ServerSimulation sim, ulong id, float x, float z, float? y = null)
    {
        var s = sim.GetState(id);
        s.PX = x; s.PZ = z;
        if (y is { } py) s.PY = py;
        s.VX = s.VZ = 0f;
        sim.SetState(id, s);
    }

    public static void Swap(ServerSimulation sim)
    {
        Step(sim, Press(AbilitySlots.F));
        for (int i = 0; i < 20 && sim.GetActiveAbility(Nyx) != null; i++) Step(sim);
        Assert.Equal(1, sim.GetState(Nyx).Form.Active);
    }
}

public sealed class HexCrowTests
{
    private const ulong Nyx = CrowFixture.Nyx, Dummy = CrowFixture.Dummy;

    private static float Degrees(float ax, float ay, float az, float bx, float by, float bz)
    {
        float dot = (ax * bx + ay * by + az * bz)
            / (MathF.Sqrt(ax * ax + ay * ay + az * az) * MathF.Sqrt(bx * bx + by * by + bz * bz));
        return MathF.Acos(Math.Clamp(dot, -1f, 1f)) * 180f / MathF.PI;
    }

    [Fact]
    public void TheCrowTurnsTowardItsTargetByAtMostTheCapEachTickAndKeepsItsSpeed()
    {
        var sim = CrowFixture.Sim(dummyDistance: 12f);
        CrowFixture.Throw(sim);
        var crow = CrowFixture.Crows(sim).Single();
        Assert.Equal(Dummy, crow.HomingTargetEntityId);
        CrowFixture.Teleport(sim, Dummy, CrowFixture.X + 6f, CrowFixture.Z + 12f);
        var previous = crow;
        bool turned = false;
        for (int tick = 0; tick < 20; tick++)
        {
            CrowFixture.Step(sim);
            var now = CrowFixture.Crows(sim).Single();
            float turn = Degrees(previous.VX, previous.VY, previous.VZ, now.VX, now.VY, now.VZ);
            Assert.True(turn <= CrowFixture.TurnDegrees + .01f, $"turned {turn} degrees in one tick");
            turned |= turn > .5f;
            Assert.Equal(CrowFixture.Speed, MathF.Sqrt(now.VX * now.VX + now.VY * now.VY + now.VZ * now.VZ), 3);
            previous = now;
        }
        Assert.True(turned, "the crow never turned toward its moved target");
        Assert.True(previous.VX > 0f, "the crow turns toward the side its target moved to");
    }

    [Fact]
    public void TheCrowGivesUpForGoodOnceItsTargetIsBehindIt()
    {
        var sim = CrowFixture.Sim(dummyDistance: 12f);
        CrowFixture.Throw(sim);
        CrowFixture.Teleport(sim, Dummy, CrowFixture.X, CrowFixture.Z - 4f);
        CrowFixture.Step(sim);
        var crow = CrowFixture.Crows(sim).Single();
        Assert.Equal(0ul, crow.HomingTargetEntityId);
        var velocity = (crow.VX, crow.VY, crow.VZ);
        CrowFixture.Teleport(sim, Dummy, CrowFixture.X + 3f, crow.Z + 5f);
        for (int tick = 0; tick < 10; tick++)
        {
            CrowFixture.Step(sim);
            var now = CrowFixture.Crows(sim).Single();
            Assert.Equal(velocity, (now.VX, now.VY, now.VZ));
            Assert.Equal(0ul, now.HomingTargetEntityId);
        }
    }

    [Fact]
    public void TheFirstBodyContactPerchesWithoutDamageFollowsTheCarrierAndExpiresSilently()
    {
        var sim = CrowFixture.Sim();
        var hits = new List<SpellResolver.HitResult>();
        CrowFixture.Perched(sim, hits);
        Assert.Empty(hits);
        var dummy = sim.GetState(Dummy);
        Assert.Equal((ushort)0, dummy.DamagePercent);
        Assert.Equal((ushort)0, dummy.HitstopTicks);
        Assert.Equal(ActionState.Idle, dummy.State);

        CrowFixture.Teleport(sim, Dummy, CrowFixture.X - 5f, CrowFixture.Z + 3f);
        CrowFixture.Step(sim, hits: hits);
        var riding = CrowFixture.Crows(sim).Single();
        Assert.Equal(CrowFixture.X - 5f, riding.X, 3);
        Assert.Equal(CrowFixture.Z + 3f, riding.Z, 3);

        for (int tick = 0; tick < CrowFixture.PerchTicks && CrowFixture.Crows(sim).Any(); tick++) CrowFixture.Step(sim, hits: hits);
        Assert.Empty(CrowFixture.Crows(sim));
        Assert.Empty(hits);
        Assert.Equal((ushort)0, sim.GetState(Dummy).DamagePercent);
    }

    [Fact]
    public void AShieldSpendsTheCrow()
    {
        var sim = CrowFixture.Sim();
        var shield = new InputState { ShieldHeld = true };
        CrowFixture.Step(sim, CrowFixture.Press(AbilitySlots.A), shield);
        for (int tick = 0; tick < CrowFixture.MaxFlight && (tick < 3 || CrowFixture.Crows(sim).Any()); tick++)
            CrowFixture.Step(sim, dummy: shield);
        Assert.Equal(ActionState.Shielding, sim.GetState(Dummy).State);
        Assert.Empty(CrowFixture.Crows(sim));
        Assert.Equal((ushort)0, sim.GetState(Dummy).DamagePercent);
    }

    [Fact]
    public void AnOpposingActiveHitboxBreaksTheCrowInFlightInsteadOfReflectingIt()
    {
        var sim = CrowFixture.Sim(dummyDistance: 8f);
        CrowFixture.Throw(sim);
        var crow = CrowFixture.Crows(sim).Single();
        sim.Resolver.Spawn(new Hitbox
        {
            X = crow.X, Y = crow.Y, Z = crow.Z + 1f, Radius = 1f, Shape = HitboxShape.Sphere, Damage = 4f,
            BaseKnockback = 4f, KnockbackAngle = 30, StunTicks = 8, DurationTicks = 4, OwnerId = Dummy,
            CancelWithAbility = true, HitsMultipleOpponents = true, HitEntities = new HashSet<ulong>(),
        });
        for (int tick = 0; tick < 4; tick++) CrowFixture.Step(sim);
        Assert.Empty(CrowFixture.Crows(sim));
        Assert.DoesNotContain(sim.Resolver.GetActiveHitboxes(), h => h.OwnerId == Nyx);
    }

    [Fact]
    public void ANewPerchReplacesHerOlderOneOnTheSameCarrier()
    {
        var sim = CrowFixture.Sim();
        var first = CrowFixture.Perched(sim);
        for (int tick = 0; tick < 25; tick++) CrowFixture.Step(sim);
        CrowFixture.Throw(sim);
        for (int tick = 0; tick < CrowFixture.MaxFlight && CrowFixture.Crows(sim).Count() > 1; tick++) CrowFixture.Step(sim);
        var kept = Assert.Single(CrowFixture.Crows(sim));
        Assert.Equal(Dummy, kept.AttachedEntityId);
        Assert.True(kept.EmissionId > first.EmissionId, "the newer crow is the one that rides");
    }

    [Fact]
    public void HerKnockoutRemovesHerCrowsAndTheCarriersKnockoutRemovesItWithoutAPop()
    {
        var sim = CrowFixture.Sim();
        CrowFixture.Perched(sim);
        CrowFixture.KnockOut(sim, Dummy);
        var hits = new List<SpellResolver.HitResult>();
        for (int tick = 0; tick < 5; tick++) CrowFixture.Step(sim, hits: hits);
        Assert.Empty(CrowFixture.Crows(sim));
        Assert.Empty(hits);

        sim = CrowFixture.Sim();
        CrowFixture.Perched(sim);
        for (int tick = 0; tick < 25; tick++) CrowFixture.Step(sim);
        CrowFixture.Throw(sim);
        Assert.Equal(2, CrowFixture.Crows(sim).Count());
        CrowFixture.KnockOut(sim, Nyx);
        CrowFixture.Step(sim);
        Assert.Empty(CrowFixture.Crows(sim));
    }

    [Fact]
    public void AHitFromHerOtherFormPopsTheCrowAfterThatHitsHitstop()
    {
        var sim = CrowFixture.Sim();
        CrowFixture.Perched(sim);
        CrowFixture.Swap(sim);
        CrowFixture.Teleport(sim, Dummy, CrowFixture.X, CrowFixture.Z + 1.5f);
        var hits = new List<SpellResolver.HitResult>();
        CrowFixture.Step(sim, CrowFixture.Press(AbilitySlots.Slot1), hits: hits);
        for (int tick = 0; tick < 10 && hits.Count == 0; tick++) CrowFixture.Step(sim, hits: hits);
        var jab = Assert.Single(hits);
        Assert.Equal(1, jab.Form);
        Assert.True(jab.HitstopTicks > 0);
        Assert.Empty(CrowFixture.Crows(sim));

        for (int tick = 1; tick < jab.HitstopTicks; tick++)
        {
            CrowFixture.Step(sim, hits: hits);
            Assert.Single(hits);
        }
        CrowFixture.Step(sim, hits: hits);
        Assert.Equal(2, hits.Count);
        var pop = hits[1];
        Assert.Equal(jab.MatchTick + jab.HitstopTicks, pop.MatchTick);
        Assert.Equal(Nyx, pop.OwnerEntityId);
        Assert.Equal(Dummy, pop.TargetEntityId);
        Assert.Equal(CrowFixture.PopDamage, pop.Damage);
        Assert.Equal(0, pop.Form);
        Assert.Equal((ushort)(CrowFixture.JabDamage + CrowFixture.PopDamage), sim.GetState(Dummy).DamagePercent);
    }

    [Fact]
    public void AHitFromTheFormThatThrewItDoesNotPop()
    {
        var sim = CrowFixture.Sim();
        CrowFixture.Perched(sim);
        CrowFixture.Teleport(sim, Dummy, CrowFixture.X, CrowFixture.Z + 1.5f);
        for (int tick = 0; tick < 25; tick++) CrowFixture.Step(sim);
        var hits = new List<SpellResolver.HitResult>();
        CrowFixture.Step(sim, CrowFixture.Press(AbilitySlots.Slot1), hits: hits);
        for (int tick = 0; tick < 40; tick++) CrowFixture.Step(sim, hits: hits);
        var jab = Assert.Single(hits);
        Assert.Equal(0, jab.Form);
        Assert.Equal(Dummy, CrowFixture.Crows(sim).Single().AttachedEntityId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARestoreWithAPerchedCrowOrAQueuedPopMatchesTheContinuousRun(bool queuedPop)
    {
        var continuous = CrowFixture.Sim();
        CrowFixture.Perched(continuous);
        if (queuedPop)
        {
            CrowFixture.Swap(continuous);
            CrowFixture.Teleport(continuous, Dummy, CrowFixture.X, CrowFixture.Z + 1.5f);
            var hits = new List<SpellResolver.HitResult>();
            CrowFixture.Step(continuous, CrowFixture.Press(AbilitySlots.Slot1), hits: hits);
            for (int tick = 0; tick < 10 && hits.Count == 0; tick++) CrowFixture.Step(continuous, hits: hits);
            Assert.Single(hits);
            Assert.Empty(CrowFixture.Crows(continuous));
        }
        else Assert.Single(CrowFixture.Crows(continuous));

        var restored = CrowFixture.Sim();
        var snapshot = continuous.CaptureSnapshot();
        restored.RestoreSnapshot(snapshot);
        Assert.True(snapshot.ContentEquals(restored.CaptureSnapshot()));
        var continuousHits = new List<SpellResolver.HitResult>();
        var restoredHits = new List<SpellResolver.HitResult>();
        for (int tick = 0; tick < 40; tick++)
        {
            CrowFixture.Step(continuous, hits: continuousHits);
            CrowFixture.Step(restored, hits: restoredHits);
            Assert.True(continuous.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()),
                $"diverged {tick + 1} ticks after the restore");
        }
        Assert.Equal(queuedPop ? 1 : 0, restoredHits.Count);
        Assert.Equal(continuousHits.Select(h => (h.MatchTick, h.Damage)), restoredHits.Select(h => (h.MatchTick, h.Damage)));
    }
}
