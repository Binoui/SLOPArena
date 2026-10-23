using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Real baked-pose normal contacts and attack completion, independent of tuning snapshots.
/// </summary>
public class FightGuyNormalTuningTests : KitScenarioTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static float GroundPy => TestHelpers.GroundPY(Def);

    private static CharacterState GroundedPlayer()
    {
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        return state;
    }

    private static CharacterState AirbornePlayer()
    {
        var state = TestHelpers.PlayerState();
        state.PY = 2f; // Calibrated low jump: still airborne during the late a4 strike.
        state.IsGrounded = false;
        return state;
    }

    private static CharacterState AirborneNpc(float z)
    {
        var state = TestHelpers.NpcState(0f, z);
        state.PY = 3f; // Air dummy sits above the attacker at the contact window.
        state.IsGrounded = false;
        return state;
    }


    [Fact]
    public void G2_ForwardPunch_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "FightGuy G2 Forward Punch Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, 7),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => TestHelpers.NpcState(0f, 1f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot2, false), (float)npc.DamagePercent),
            TotalTicks = 80,
        });
    }

    [Fact]
    public void G4_DoubleKick_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "FightGuy G4 Double Kick Hit Confirm",
            Def = Def,
            Setup = GroundedPlayer,
            Inputs = new InputSequence().Press(0, 9),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => TestHelpers.NpcState(0f, 0.8f) with { PY = GroundPy },
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot4, false), (float)npc.DamagePercent),
            TotalTicks = 100,
        });
    }

    [Fact]
    public void A2_SustainedOverlap_HitsOnceAcrossSweetAndSourWindows()
    {
        var windows = Def.GetCookedSlotAbility(AbilitySlots.Slot2, airborne: true)!.Timeline.Stages
            .SelectMany(stage => stage.Operations).OfType<CookedSpawnHitboxOperation>()
            .OrderBy(operation => operation.Tick).ToArray();
        var sweet = windows.First();
        var sour = windows.Last();
        Assert.True(sweet.Tick < sour.Tick && sweet.Hitbox.Damage > sour.Hitbox.Damage);

        var sim = TestHelpers.MakeSim();
        sim.NoGravityEntityId = 1;
        sim.RegisterEntity(1, Def, AirbornePlayer() with { PX = 20f, PY = 20f, PZ = 20f },
            TestHelpers.LoadBakedData(Def));
        var targetDef = TestHelpers.EngineDef;
        targetDef.HurtboxCapsules = new[] { new HurtboxCapsule(0, 0, 0, 0, 0, 0, 4f) };
        var target = TestHelpers.NpcState(20f, 21f) with { PY = 20f, IsGrounded = false };
        sim.RegisterEntity(100, targetDef, target);
        bool lateTargetAdded = false;
        int sweetOverlapTicks = 0, sourOverlapTicks = 0;
        var contacts = new List<SpellResolver.HitResult>();
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SetState(100, target with { DamagePercent = sim.GetState(100).DamagePercent });
            if (lateTargetAdded)
                sim.SetState(101, target with { EntityId = 101, DamagePercent = sim.GetState(101).DamagePercent });
            sim.Tick(new Dictionary<ulong, InputState>
            {
                [1] = tick == 0 ? new InputState { ActiveSlot = AbilitySlots.Slot2 } : default,
            });
            contacts.AddRange(sim.LastTickHits.Where(h => h.OwnerEntityId == 1));
            foreach (var hitbox in sim.Resolver.LastTickCheckedHitboxes.Where(h => h.OwnerId == 1))
            {
                var receiver = Assert.Single(sim.LastTickAttackEntities.Where(e => e.Id == 100));
                Assert.True(SpellResolver.SurfaceGap(hitbox, receiver, out _, out _, out _) <= 0f,
                    "The receiver must stay inside the live baked hitbox throughout both windows.");
                if (hitbox.Damage == sweet.Hitbox.Damage) sweetOverlapTicks++;
                if (hitbox.Damage != sour.Hitbox.Damage) continue;
                sourOverlapTicks++;
                if (lateTargetAdded) continue;
                sim.RegisterEntity(101, targetDef, target with { EntityId = 101 });
                lateTargetAdded = true;
            }
            if (sim.GetActiveAbility(1) == null) break;
        }

        Assert.True(sweetOverlapTicks > 1 && sourOverlapTicks > 1);
        var firstContact = Assert.Single(contacts.Where(h => h.TargetEntityId == 100));
        Assert.False(firstContact.Blocked);
        Assert.Equal(sweet.Hitbox.Damage, firstContact.Damage);
        Assert.Equal(sweet.Hitbox.Damage, (float)sim.GetState(100).DamagePercent);
        Assert.True(lateTargetAdded);
        var lateContact = Assert.Single(contacts.Where(h => h.TargetEntityId == 101));
        Assert.False(lateContact.Blocked);
        Assert.Equal(sour.Hitbox.Damage, lateContact.Damage);
        Assert.Equal(sour.Hitbox.Damage, (float)sim.GetState(101).DamagePercent);
        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal((byte)0, sim.GetState(1).AttackSlot);
    }

    [Fact]
    public void A3_HighKick_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "FightGuy A3 High Kick Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, 8),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => AirborneNpc(0.8f),
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot3, true), (float)npc.DamagePercent),
            TotalTicks = 90,
        });
    }

    [Fact]
    public void A4_AirSmash_DealsAuthoredDamage()
    {
        AssertScenario(new KitScenario
        {
            Name = "FightGuy A4 Air Smash Hit Confirm",
            Def = Def,
            Setup = AirbornePlayer,
            Inputs = new InputSequence().Press(0, 9),
            Assert = player => Assert.Equal((byte)0, player.AttackSlot),
            NpcSetup = () => AirborneNpc(0.8f),
            NpcDef = Def,
            NpcAssert = npc => Assert.Equal(NormalDamage(AbilitySlots.Slot4, true), (float)npc.DamagePercent),
            TotalTicks = 100,
        });
    }
    private static float NormalDamage(byte slot, bool airborne)
    {
        var damage = Def.GetCookedSlotAbility(slot, airborne)!.Timeline.Stages
            .SelectMany(stage => stage.Operations).OfType<CookedSpawnHitboxOperation>()
            .Sum(operation => operation.Hitbox.Damage);
        Assert.True(damage > 0f, "Hit-confirm scenarios require a damaging authored normal.");
        return damage;
    }
}
