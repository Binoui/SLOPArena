using System;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public class FightGuyAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.FightGuyDef);

    // ── A (Ki Shot — cooked projectile) ──

    [Fact]
    public void FightGuyKiShot_PressStartsAttackWithoutAimState()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        var result = TestHelpers.TickN(sim, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 9000 }, 1);
        Assert.Equal(ActionState.Attacking, result.State);
        Assert.False(result.IsAiming);
    }

    [Fact]
    public void FightGuyKiShot_FiresAfterStartupWhileAimInputIsIgnored()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPY;
        TestHelpers.RegisterPlayer(sim, TestHelpers.FightGuyDef, state);
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 11, IsAiming = true, AimYaw = 9000, AimPitch = 5000 } } });
        for (int i = 0; i < 6; i++)
        {
            sim.Tick(new() { { 1, new InputState { IsAiming = true, AimYaw = 9000, AimPitch = 5000 } } });
            Assert.Empty(sim.Resolver.GetActiveHitboxes());
        }
        sim.Tick(new() { { 1, new InputState { IsAiming = true, AimYaw = 9000, AimPitch = 5000 } } });
        var projectile = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(HitboxShape.Sphere, projectile.Shape);
        Assert.True(projectile.VZ > 0f && MathF.Abs(projectile.VX) < projectile.VZ);
        Assert.InRange(MathF.Abs(projectile.VY), 0f, 0.1f);
    }

    [Fact]
    public void FightGuyKiShot_DamagesTargetOnce()
    {
        var sim = TestHelpers.MakeSim();
        var def = TestHelpers.FightGuyDef;
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
    public void FightGuyCycloneKick_HitsEachTargetOnceWithModerateKnockback()
    {
        var def = TestHelpers.FightGuyDef;
        var baked = TestHelpers.LoadBakedData(def);
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 3f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, def, npc, baked);

        float maxHorizontalVelocity = 0f;
        ushort maxStun = 0;
        for (int i = 0; i < 80; i++)
        {
            sim.Tick(new()
            {
                { 1, i == 0 ? TestHelpers.Input(activeSlot: 5) : default },
                { 100, default },
            });
            var target = sim.GetState(100);
            maxHorizontalVelocity = MathF.Max(maxHorizontalVelocity,
                MathF.Sqrt(target.VX * target.VX + target.VZ * target.VZ));
            maxStun = Math.Max(maxStun, target.HitstunTicks);
        }

        Assert.Equal((ushort)7, sim.GetState(100).DamagePercent);
        Assert.True(maxHorizontalVelocity > 0f, "Cyclone must apply nonzero knockback");
        Assert.True(maxStun > 0 && maxStun <= 6, $"expected short stun, got {maxStun}");
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

        Assert.Equal((ushort)7, sim.GetState(100).DamagePercent);
        Assert.Equal((ushort)7, sim.GetState(101).DamagePercent);
    }

    // ── F (FightGuy Fist of Fury) ──

    [Theory]
    [InlineData(0.9f, 0, 0, CharacterClass.FightGuy)]
    [InlineData(1.6f, 150, 0, CharacterClass.FightGuy)]
    [InlineData(1.6f, 0, 20, CharacterClass.FightGuy)]
    [InlineData(1.6f, 0, 28, CharacterClass.FightGuy)]
    [InlineData(1.6f, 0, 36, CharacterClass.FightGuy)]
    [InlineData(1.6f, 0, 44, CharacterClass.FightGuy)]
    [InlineData(1.6f, 0, 52, CharacterClass.FightGuy)]
    [InlineData(1.6f, 150, 60, CharacterClass.FightGuy)]
    [InlineData(1.6f, 0, 70, CharacterClass.FightGuy)]
    [InlineData(0.9f, 150, 60, CharacterClass.Bonk)]
    [InlineData(1.6f, 150, 60, CharacterClass.Manki)]
    [InlineData(1.6f, 150, 60, CharacterClass.Wibou)]
    public void FightGuyFistOfFury_PunchCatchRemainsLockedUntilFootFinisher(
        float distance, ushort startingDamage, ushort entryTick, CharacterClass targetClass)
    {
        var def = TestHelpers.FightGuyDef;
        var baked = TestHelpers.LoadBakedData(def);
        var targetDef = TestHelpers.ResolveDef(targetClass);
        var targetBaked = TestHelpers.LoadBakedData(targetDef);
        var slot = def.GetCookedSlotAbility(AbilitySlots.F, false)!;
        var finisher = slot.Timeline.Stages.SelectMany(stage => stage.Operations)
            .OfType<CookedSpawnHitboxOperation>()
            .Single(operation => operation.Hitbox.StartBoneId == "bone.right-foot");
        var sim = TestHelpers.MakeSim();
        // Keep the combo away from the heightmap boundary while inward hits resolve pushboxes.
        var player = TestHelpers.PlayerState(50f, 50f);
        player.PY = GroundPY;
        sim.RegisterEntity(1, def, player, baked);

        bool registered = false, caught = false, kicked = false, launched = false;
        int punchContacts = 0, kickContacts = 0;
        for (int i = 0; i < 220; i++)
        {
            if (!registered && sim.GetState(1).AttackElapsedTicks >= Math.Max(0, entryTick - 1))
            {
                var npc = TestHelpers.NpcState(50f, 50f + distance);
                npc.PY = TestHelpers.GroundPY(targetDef);
                npc.DamagePercent = startingDamage;
                sim.RegisterEntity(100, targetDef, npc, targetBaked);
                registered = true;
            }

            sim.Tick(new()
            {
                { 1, i == 0 ? TestHelpers.Input(activeSlot: 6) : default },
                { 100, caught ? TestHelpers.Input(moveY: 1f, jump: true, jumpHeld: true) : default },
            });
            if (!registered) continue;

            foreach (var hit in sim.LastTickHits.Where(hit => hit.TargetEntityId == 100))
            {
                Assert.False(hit.Blocked);
                if (hit.Damage == finisher.Hitbox.Damage)
                {
                    Assert.True(caught, "the foot must finish an opponent caught by a punch");
                    kicked = true;
                    kickContacts++;
                    Assert.Equal(AuthoringKnockbackDirection.AwayFromOwner, hit.KnockbackDirection);
                }
                else
                {
                    caught = true;
                    punchContacts++;
                    Assert.Equal(AuthoringKnockbackDirection.TowardOwner, hit.KnockbackDirection);
                }
            }

            var target = sim.GetState(100);
            if (caught && !kicked)
                Assert.True(target.HitstopTicks > 0 || target.HitstunTicks > 0,
                    $"caught opponent escaped before the foot at simulation tick {i}");
            if (kicked && target.HitstopTicks == 0 && target.KVZ > 0f)
                launched = true;
        }

        Assert.True(punchContacts > 0, "the entry must connect with the punch flurry");
        Assert.Equal(1, kickContacts);
        Assert.True(launched, "only the foot finisher should launch the victim outward");
    }

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
        def.CookedSlots = slots;
        return def;
    }
}
