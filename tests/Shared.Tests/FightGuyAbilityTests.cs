using System;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public class FightGuyAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);

    // ── A (Ki Shot — charged projectile) ──

    [Fact]
    public void FightGuyKiShot_PressStartsStationaryCharge()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, ChargedQDefinition(), state);
        var result = TestHelpers.TickN(sim, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 9000 }, 1);
        Assert.Equal(ActionState.Aiming, result.State);
        Assert.True(result.IsAiming);
    }

    [Fact]
    public void FightGuyKiShot_HoldsThenFiresAfterReleaseStartup()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, ChargedQDefinition(), state);
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 9000, AimPitch = 5000 } } });
        for (int i = 0; i < 60; i++)
        {
            sim.Tick(new() { { 1, new InputState { IsAiming = true, AimYaw = 9000, AimPitch = 5000 } } });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }
        sim.Tick(new() { { 1, default } });
        TestHelpers.TickDefault(sim, 8);
        var projectile = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(HitboxShape.Sphere, projectile.Shape);
        Assert.Equal(1f, projectile.Radius);
        Assert.Equal(20f, projectile.VZ, 3);
        Assert.True(projectile.VZ > 0f && MathF.Abs(projectile.VX) < projectile.VZ);
        Assert.InRange(MathF.Abs(projectile.VY), 0f, 0.1f);
    }

    [Fact]
    public void FightGuyKiShot_DamagesTargetOnce()
    {
        var sim = TestHelpers.MakeSim();
        var def = ChargedQDefinition();
        var baked = TestHelpers.LoadBakedData(def);
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        sim.RegisterEntity(1, def, player);
        var npc = TestHelpers.NpcState(0f, 2f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, def, npc, baked);
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 9000, AimPitch = 5000 } }, { 100, default } });
        for (int i = 0; i < 8; i++)
            sim.Tick(new() { { 1, new InputState { IsAiming = true, AimYaw = 9000, AimPitch = 5000 } }, { 100, default } });
        for (int i = 0; i < 40; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });
        var target = sim.GetState(100);
        Assert.Equal((ushort)6, target.DamagePercent);
    }

    private static CharacterDefinition ChargedQDefinition()
    {
        var def = TestHelpers.ResolveSourceDef("fightguy");
        def = def with { Class = CharacterClass.FightGuy };
        return def;
    }

    // ── R (FightGuyCycloneKick) ──

    [Fact]
    public void FightGuyCycloneKick_Activates()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)5, t0.AttackSlot);
    }

    [Fact]
    public void FightGuyCycloneKick_InterruptionClearsDynamicHitboxes()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.R) } });
        for (int tick = 0; tick < 30 && sim.Resolver.GetActiveHitboxes().Count == 0; tick++)
            sim.Tick(new() { { 1, default } });
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());

        state = sim.GetState(1);
        state.State = ActionState.Hitstun;
        state.HitstunTicks = 20;
        sim.SetState(1, state);
        sim.TickAbilities(new());

        Assert.Empty(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(ActionState.Hitstun, sim.GetState(1).State);
    }


    [Fact]
    public void FightGuyCycloneKick_CarriesVictimAndRecoversFirst()
    {
        var (sim, _) = CycloneVersusDummy(3f);
        int hits = 0, lastPercent = 0, fighterFree = -1, victimFree = -1;
        for (int i = 0; i < 140; i++)
        {
            sim.Tick(new()
            {
                { 1, i == 0 ? TestHelpers.Input(activeSlot: 5) : default },
                { 100, default },
            });
            var fighter = sim.GetState(1);
            var victim = sim.GetState(100);
            if (victim.DamagePercent != lastPercent)
            {
                hits++;
                lastPercent = victim.DamagePercent;
                victimFree = -1;
            }
            if (hits > 0 && victimFree < 0 && victim.State != ActionState.Hitstun
                && victim.HitstunTicks == 0 && victim.HitstopTicks == 0)
                victimFree = i;
            if (i > 2 && fighterFree < 0 && fighter.State != ActionState.Attacking && fighter.HitstopTicks == 0)
                fighterFree = i;
        }

        Assert.True(hits >= 4, $"expected the cyclone to keep hitting, got {hits} hits");
        Assert.True(victimFree > fighterFree,
            $"Cyclone must be safe on hit: victim free at {victimFree}, FightGuy at {fighterFree}");
    }

    [Theory]
    [InlineData(3f, false)]
    [InlineData(30f, true)]
    public void FightGuyCycloneKick_DizzyOnlyOnWhiff(float distance, bool expectDizzy)
    {
        var (sim, _) = CycloneVersusDummy(distance);
        bool dizzy = false;
        for (int i = 0; i < 80; i++)
        {
            sim.Tick(new()
            {
                { 1, i == 0 ? TestHelpers.Input(activeSlot: 5) : default },
                { 100, default },
            });
            dizzy |= sim.GetPresentationEvents(clear: true)
                .Any(e => e.PresentationId == "presentation.fightguy.cyclone-kick.dizzy");
        }

        Assert.Equal(expectDizzy, dizzy);
    }

    private static (ServerSimulation sim, CharacterDefinition def) CycloneVersusDummy(float distance)
    {
        var def = TestHelpers.FightGuyDef;
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, def, player);
        var npc = TestHelpers.NpcState(0f, distance);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, def, npc, TestHelpers.LoadBakedData(def));
        return (sim, def);
    }

    [Fact]
    public void FightGuyCycloneKick_HitsMultipleEnemiesAlongPath()
    {
        var def = TestHelpers.FightGuyDef;
        var baked = TestHelpers.LoadBakedData(def);
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, def, player);

        var npc1 = TestHelpers.NpcState(0f, 2f);
        npc1.PY = GroundPY;
        sim.RegisterEntity(100, def, npc1, baked);

        var npc2 = TestHelpers.NpcState(0f, 3.5f);
        npc2.PY = GroundPY;
        sim.RegisterEntity(101, def, npc2, baked);

        for (int i = 0; i < 150; i++)
        {
            sim.Tick(new()
            {
                { 1, i == 0 ? TestHelpers.Input(activeSlot: 5) : default },
                { 100, default },
                { 101, default },
            });
        }

        Assert.True(sim.GetState(100).DamagePercent > 0);
        Assert.True(sim.GetState(101).DamagePercent > 0);
    }

    // ── A move that pulls toward its owner (Fist of Fury's retired punches) ──

    [Theory]
    [InlineData(1UL, 100UL)]
    [InlineData(100UL, 1UL)]
    public void ATowardOwnerMove_BodyContactHoldsCasterOnlyDuringMove(ulong casterId, ulong targetId)
    {
        var pull = new HitboxEvent
        {
            TriggerTick = 10, DurationTicks = 5, Shape = HitboxShape.Sphere, Radius = .7f, OffZ = .5f, Damage = 2f,
            Knockback = new KnockbackData { Profile = KnockbackProfile.Custom, BaseKnockback = 20f },
            StunTicks = 12, KnockbackDirection = AuthoringKnockbackDirection.TowardOwner,
        };
        var def = TestHelpers.WithMove(TestHelpers.FightGuyDef, AbilitySlots.F, false, "Pull", 60, hits: new[] { pull });
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState(50f, 50f) with { PY = GroundPY };
        var target = TestHelpers.NpcState(50f, 50.2f) with { PY = GroundPY };
        sim.RegisterEntity(casterId, def, player);
        sim.RegisterEntity(targetId, def, target);

        sim.Tick(new() { { casterId, TestHelpers.Input(activeSlot: AbilitySlots.F) }, { targetId, default } });

        Assert.Equal(player.PZ, sim.GetState(casterId).PZ, 4);
        Assert.Equal(player.PZ + def.CapsuleRadius * 2f, sim.GetState(targetId).PZ, 4);

        for (int i = 0; i < 300 && sim.GetActiveAbility(casterId) != null; i++)
            sim.Tick(new() { { casterId, default }, { targetId, default } });
        Assert.Null(sim.GetActiveAbility(casterId));

        sim.SetState(targetId, target);
        sim.Tick(new() { { casterId, default }, { targetId, default } });

        Assert.True(sim.GetState(casterId).PZ < player.PZ,
            "ordinary body contact must push the caster again after the move ends");
    }

    // Title Match (F, ADR-0053) is covered through his package in ClinchTests.

    // ── Bone-attached hitbox ──

    [Fact]
    public void CookedBoneHitbox_IsSkippedWithoutBakedData()
    {
        var def = WithGroundSlot(new CookedTimeline(new[]
        {
            new CookedStage(20, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
            {
                new CookedSpawnHitboxOperation(5, AuthoringUnit.Meters,
                    new CookedHitbox(AuthoringHitboxShape.Sphere, 0.8f, 0f, 0.1f, 0f,
                        0f, 0f, 0f, "mixamorig:RightFoot", null, 10f, 45f, 20f, 2f, 10, 5, true, 0)),
            }),
        }));

        var sim = TestHelpers.MakeSim();
        var targetDef = TestHelpers.EngineDef;
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(def);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, def, player);

        // NPC is a synthetic target; no baked poses are needed for the synthetic hitbox.
        var npc = TestHelpers.NpcState(0.5f, 1.5f);
        npc.PY = TestHelpers.GroundPY(targetDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, targetDef, npc);

        // Tick through hitbox trigger (tick 5)
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.Slot1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        // No baked data → bone hitbox should have been skipped
        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent == 0,
            $"NPC should take NO damage (bone hitbox skipped without baked data), got {npcAfter.DamagePercent}");
    }

    [Fact]
    public void CookedEntityOffsetHitbox_StillWorks()
    {
        // Entity-relative offset (no BoneName) still hits via the standard path now that
        // bone-attached hitboxes exist — Off* is anchor-relative (bone or entity origin).
        var def = WithGroundSlot(new CookedTimeline(new[]
        {
            new CookedStage(20, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
            {
                new CookedSpawnHitboxOperation(5, AuthoringUnit.Meters,
                    new CookedHitbox(AuthoringHitboxShape.Sphere, 0.8f, 0f, 0.8f, 1.2f,
                        0f, 0f, 0f, null, null, 10f, 45f, 20f, 2f, 10, 5, true, 0)),
            }),
        }));

        var sim = TestHelpers.MakeSim();
        var targetDef = TestHelpers.EngineDef;
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(def);
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, def, player);

        // NPC directly in front at the entity-offset hitbox position (OffZ = 1.2).
        var npc = TestHelpers.NpcState(0f, 1.2f);
        npc.PY = TestHelpers.GroundPY(targetDef);
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, targetDef, npc);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.Slot1) }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should take damage from entity-offset hitbox, got {npcAfter.DamagePercent}");
    }

    // Reserved wire selector 1 must not start a move.

    [Fact]
    public void ReservedFirstWireSelector_DoesNotStartAttack()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.EngineDef, state);
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 1 } } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default } });

        var s = sim.GetState(1);
        Assert.Equal(ActionState.Idle, s.State); // never entered an attack
        Assert.Equal((byte)0, s.AttackSlot);
        Assert.Equal((ushort)0, s.DamagePercent);
    }
    private static CharacterDefinition WithGroundSlot(CookedTimeline timeline)
    {
        var def = TestHelpers.EngineDef;
        var slots = def.CookedSlots!.ToArray();
        slots[0] = new CookedSlotDefinition(
            0, "ground.1", false, "Test hitbox", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false, timeline);
        def = def with { CookedSlots = slots };
        return def;
    }
}
