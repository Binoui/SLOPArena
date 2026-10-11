using System;
using System.Linq;
using Xunit;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Behaviour tests for Wibou's kit. Normal tier (keys 1-4, ground + air) activation and
/// damage, plus the specials: E rising recovery, R directional dash, and F skyfall.
/// </summary>
public class WibouAbilityTests
{
    private static readonly float GroundPY = TestHelpers.GroundPY(TestHelpers.WibouDef);
    private static CharacterDefinition Def => TestHelpers.WibouDef;

    private static ServerSimulation SimWithPlayer(out CharacterState player)
    {
        var sim = TestHelpers.MakeSim();
        player = TestHelpers.PlayerState();
        player.PY = GroundPY;
        // Baked data required: Wibou's normals are blade-anchored (RightHand→_weapon_tip),
        // which resolves against the bake like the real game (MatchInstance.LoadBakedData).
        sim.RegisterEntity(1, Def, player, TestHelpers.LoadBakedData(Def));
        return sim;
    }

    // ── Activation of every slot ──

    [Theory]
    [InlineData((byte)3)] // key "1" — Quick Slash (normal)
    [InlineData((byte)7)] // key "2" — Double Slash (normal)
    [InlineData((byte)8)] // key "3" — Up Slash (normal)
    [InlineData((byte)9)] // key "4" — Heavy Down Slash (normal)
[InlineData((byte)4)] // E — Rising Slash
[InlineData((byte)5)] // R — Dash Slash
[InlineData((byte)6)] // F — Skyfall
    public void GroundSlot_Activates(byte slot)
    {
        var sim = SimWithPlayer(out _);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot, aiming: true), 1);
        // R (slot 5) is a hold-to-aim ability: it enters the Aiming state instead of Attacking.
        ActionState expected = slot == 5 ? ActionState.Aiming : ActionState.Attacking;
        Assert.Equal(expected, t0.State);
        Assert.Equal(slot, t0.AttackSlot);
    }

    [Theory]
    [InlineData((byte)3)] // key "1" air — Air Slash
    [InlineData((byte)7)] // key "2" air — Reverse Slash
    [InlineData((byte)8)] // key "3" air — Air Up Slash
    [InlineData((byte)9)] // key "4" air — Air Heavy Down Slash
    public void AirSlot_Activates(byte slot)
    {
        var sim = TestHelpers.MakeSim();
        var s = TestHelpers.PlayerState();
        s.PY = GroundPY + 5f;
        s.IsGrounded = false;
        TestHelpers.RegisterPlayer(sim, Def, s);
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal(slot, t0.AttackSlot);
    }

    // ── Normal tier: each ground normal damages a nearby enemy ──

    [Theory]
    [InlineData((byte)3)] // key "1"
    [InlineData((byte)7)] // key "2"
    [InlineData((byte)8)] // key "3"
    [InlineData((byte)9)] // key "4"
    public void GroundNormal_DamagesNearbyEnemy(byte slot)
    {
        var sim = SimWithPlayer(out _);
        var baked = TestHelpers.LoadBakedData(Def);

        var npc = TestHelpers.NpcState(0f, 1f);
        npc.PY = GroundPY;
        sim.RegisterEntity(100, Def, npc, baked);

        var duration = Def.GetCookedSlotAbility(slot, airborne: false)!.Timeline.Stages
            .Sum(stage => stage.DurationTicks);
        sim.Tick(new() { { 1, new InputState { ActiveSlot = slot } }, { 100, default } });
        for (int i = 0; i < duration; i++) sim.Tick(new() { { 1, default }, { 100, default } });

        Assert.True(sim.GetState(100).DamagePercent > 0, $"slot {slot} should hit the enemy in reach");
    }


    // ── E: rising slash lifts Wibou off ground as recovery ──

    [Fact]
    public void E_RisesOffGround()
    {
        var sim = SimWithPlayer(out _);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        for (int i = 0; i < 10; i++) sim.Tick(new() { { 1, default } });
        var s = sim.GetState(1);
        Assert.False(s.IsGrounded);
        Assert.True(s.PY > GroundPY + 1f, $"expected Wibou to rise above {GroundPY + 1f:F2}, got {s.PY:F2}");
    }

    [Fact]
    public void RisingSlashConnectsWithAirborneVictimAndRefundsItsRecoveryCharge()
    {
        var sim = SimWithPlayer(out _);
        var victim = TestHelpers.NpcState(0f, 4f);
        victim.PY = GroundPY + 4f;
        victim.IsGrounded = false;
        victim.DamagePercent = 120;
        sim.RegisterEntity(100, Def, victim, TestHelpers.LoadBakedData(Def));
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.E) }, { 100, default } });
        Assert.Equal(1, sim.GetState(1).ChargeStockSpent);

        bool connected = false;
        bool launched = false;
        for (int tick = 0; tick < 100; tick++)
        {
            sim.Tick(new() { { 1, default }, { 100, default } });
            connected |= sim.LastTickHits.Any(hit => hit.TargetEntityId == 100 && !hit.Blocked);
            var state = sim.GetState(100);
            launched |= state.HitstunTicks > 0 && state.KVY > 0f;
            if (connected) Assert.Equal(0, sim.GetState(1).ChargeStockSpent);
        }
        Assert.True(connected, "The recovery slash should connect with an airborne victim in its path.");
        Assert.True(launched, "The connected recovery slash should retain an upward launch.");
    }

    [Fact]
    public void E_RecoveryUsesCameraDirection_NotTargetDirection()
    {
        var sim = SimWithPlayer(out _);
        var target = TestHelpers.NpcState(0f, 4f);
        target.PY = GroundPY;
        TestHelpers.RegisterNpc(sim, Def, target);

        // Camera yaw +90° points along +X; the target is straight ahead on +Z.
        sim.Tick(new() { { 1, new InputState { ActiveSlot = 4, AimYaw = 9000 } }, { 100, default } });
        for (int i = 0; i < 10; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var s = sim.GetState(1);
        Assert.True(s.PX > 1f, $"expected recovery movement along camera +X, got PX={s.PX:F2}");
        Assert.True(MathF.Abs(s.PZ) < 0.1f, $"recovery must not home toward target +Z, got PZ={s.PZ:F2}");
    }

    [Theory]
    [InlineData(AbilitySlots.E, 19, false)]
    [InlineData(AbilitySlots.F, 14, false)]
    [InlineData(AbilitySlots.F, 60, false)]
    [InlineData(AbilitySlots.E, 19, true)]
    [InlineData(AbilitySlots.F, 14, true)]
    [InlineData(AbilitySlots.F, 60, true)]
    public void SpecialSpawnsEachAuthoredHitboxOnce(byte slot, int triggerTick, bool airborne)
    {
        var def = Def;
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState() with
        {
            PY = GroundPY + (airborne ? 10f : 0f),
            IsGrounded = !airborne,
        };
        sim.RegisterEntity(1, def, player, TestHelpers.LoadBakedData(def));
        var authored = Assert.Single(def.GetCookedSlotAbility(slot, airborne)!.Timeline.Stages[0]
            .Operations.OfType<CookedSpawnHitboxOperation>().Where(op => op.Tick == triggerTick));

        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: slot), triggerTick);

        var hitbox = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal(slot, hitbox.AttackSlot);
        Assert.Equal(authored.Hitbox.Damage, hitbox.Damage);
        Assert.Equal(authored.Hitbox.HitGroup, hitbox.SourceEvent.HitGroup);
    }


    // ── F: blade flurry deals damage ──

    [Fact]
    public void F_FlurryDamagesEnemy()
    {
        var targetDef = Def;
        var targetBaked = TestHelpers.LoadBakedData(targetDef);
        var sim = SimWithPlayer(out _);
        var npc = TestHelpers.NpcState(0f, 1.3f); npc.PY = GroundPY;
        sim.RegisterEntity(100, targetDef, npc, targetBaked);
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 6) }, { 100, default } });
        for (int i = 0; i < 64; i++) sim.Tick(new() { { 1, default }, { 100, default } });
        Assert.True(sim.GetState(100).DamagePercent > 0);
    }
}
