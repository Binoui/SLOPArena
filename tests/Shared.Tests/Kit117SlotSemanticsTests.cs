using Xunit;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Issue #117 — the 3-state ground/air slot semantics and the FightGuy kit rework:
///   - all four FightGuy specials have air specs (Fist of Fury; slots E/R/F/A share ground specs)
///   - normals 1-4 have DISTINCT air specs (AirSlot1-4 — the air normal pass)
///   - Air* = ground spec reference = shared (Rising Dragon, Cyclone, Fist of Fury, Ki Shot)
///   - the E-slot rising punch: anti-air on the ground, recovery burst in the air
///   - cooldowns on slots 6-11 now tick down (TickTimers loop fix)
/// </summary>
public class Kit117SlotSemanticsTests
{
    private static readonly CharacterDefinition Def = TestHelpers.FightGuyDef;
    private static readonly float GroundPy = TestHelpers.GroundPY(Def);

    private static CharacterState AirborneState()
    {
        var s = TestHelpers.PlayerState();
        s.PY = GroundPy + 5f;
        s.IsGrounded = false;
        s.AirTimeTicks = 100;
        return s;
    }


    // ── Grounded-only gating (behavior) ──

    [Fact]
    public void AirNormal_ActivatesWhileAirborne()
    {
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, AirborneState());

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3), 1); // key 1 air — Double Punch
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
    }

    [Fact]
    public void NormalSlot1_ActivatesOnGround()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 3), 1); // Low Kick
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)3, t0.AttackSlot);
    }


    [Fact]
    public void EmptySlot5_Noop_NoCrash()
    {
        // Key "5" has no data — the old code NRE'd on spec.Timeline.Stages; must be a silent no-op.
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, TestHelpers.PlayerState());

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 10), 1);
        Assert.Equal(ActionState.Idle, t0.State);
        Assert.Equal((byte)0, t0.AttackSlot);
    }

    // ── Shared abilities in the air ──

    [Fact]
    public void KiShot_Shared_ChargesThenFiresWhileAirborne()
    {
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def, AirborneState());

        var held = TestHelpers.TickHold(sim, TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: true), 3);
        Assert.Equal(ActionState.Aiming, held.State);
        Assert.Equal(AbilitySlots.A, held.AttackSlot);
        Assert.True(held.IsAiming);
        Assert.False(held.IsGrounded);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());

        var released = TestHelpers.TickDefault(sim, 1);
        Assert.Equal(ActionState.Attacking, released.State);
        Assert.False(released.IsAiming);
        var charge = Assert.IsType<CookedChargedProjectileCapabilityParameters>(
            Def.GetCookedSlotAbility(AbilitySlots.A, true)!.Timeline.Stages
                .SelectMany(stage => stage.Operations).OfType<CookedStartCapabilityOperation>()
                .Single(operation => operation.Parameters is CookedChargedProjectileCapabilityParameters).Parameters);
        TestHelpers.TickDefault(sim, charge.FireTriggerTick);
        Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.False(sim.GetState(1).IsGrounded);
    }

    // ── E-slot Rising Dragon ──

    [Fact]
    public void RisingDragon_Ground_WindupThenLaunches()
    {
        // Grounded cast = anti-air launcher: an 8-tick windup (rise_delay) keeps the body low
        // so the tick-6/10 hitboxes connect on grounded opponents, THEN the burst launches.
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        state.VY = 0f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t1 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t1.State);
        Assert.True(t1.AnimLockTicks > 0);
        Assert.Equal(0f, t1.VY); // windup — still planted

        // Past the 8-tick windup the sustained rise holds VY at rise_speed (11).
        var t12 = TestHelpers.TickN(sim, TestHelpers.Input(), 11);
        Assert.True(t12.VY > 10f, $"expected upward burst after the windup, got VY={t12.VY:F3}");
    }

    [Fact]
    public void RisingDragon_Ground_SpawnsHitbox()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) } });
        // Hitbox triggers at tick 6 of the 24-tick duration.
        for (int i = 0; i < 10 && sim.Resolver.GetActiveHitboxes().Count == 0; i++)
            sim.Tick(new() { { 1, default } });
        Assert.NotEmpty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void RisingDragon_Air_ResetsFloatWindow()
    {
        // The ADR-0015 recovery contract: airborne use resets AirTimeTicks (FloatWindow).
        var sim = TestHelpers.MakeSim();
        var state = AirborneState();
        Assert.True(state.AirTimeTicks > 0);
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        // Reset at activation (100 → 0), then the air-time counter +1s during the same tick.
        Assert.True(t0.AirTimeTicks < 2, $"expected FloatWindow reset, got AirTimeTicks={t0.AirTimeTicks}");
        Assert.True(t0.VY > 0f, $"expected upward burst in the air, got VY={t0.VY:F3}");
    }
    [Fact]
    public void RisingDragon_CommitsToStickDirection_NotCameraOrTargetLock()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        state.FacingYaw = 0f;       // target is in front of the old facing
        TestHelpers.RegisterPlayer(sim, Def, state);

        var target = TestHelpers.NpcState(0f, 1f);
        target.PY = GroundPy;
        sim.RegisterEntity(100, Def, target);

        var input = new InputState
        {
            ActiveSlot = 4,
            MoveY = -1f,             // stick points behind the fighter: π radians
            AimYaw = 9000,           // camera points along +X
            TargetEntityId = 100,    // prove target lock cannot override E
        };
        var afterStart = TestHelpers.TickN(sim, input, 1);

        Assert.InRange(MathF.Abs(afterStart.FacingYaw - MathF.PI), 0f, 0.001f);
        Assert.Equal(ActionState.Attacking, afterStart.State);
    }


    // ── Rising Dragon connect envelope (baked bones — the in-game path) ──
    // User report (2026-08-19): hard to hit in-game despite an OffZ push. E's hitboxes are
    // bone-anchored (RightHand/Head) and the attacker rises at rise_speed from tick 1, so the
    // connect range depends on where the bake puts the hand/head at the spell_e frames AND how
    // high the attacker already is at trigger tick 6. These sweep the grounded-victim distance
    // to find the real envelope instead of trusting the authored OffZ.

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.7f)]
    [InlineData(1.0f)]
    public void RisingDragon_Ground_ConnectsAtRange(float distance)
    {
        var baked = TestHelpers.LoadBakedData(Def);
        Assert.NotNull(baked); // loaded from the admitted cooked FightGuy pose package

        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPy;
        player.FacingYaw = 0f; // facing +Z
        sim.RegisterEntity(1, Def, player, baked);

        var npc = TestHelpers.NpcState(0f, distance);
        npc.PY = GroundPy;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, Def, npc, baked);

        // E press on tick 0; hitboxes trigger at tick 6, active through tick 30.
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 30; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"Rising Dragon should connect on a grounded victim at {distance}m, got {npcAfter.DamagePercent}");
    }

    [Fact]
    public void RisingDragon_Ground_MissesADistantVictim()
    {
        var baked = TestHelpers.LoadBakedData(Def);
        Assert.NotNull(baked);

        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState(20f, 20f);
        player.PY = GroundPy;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, Def, player, baked);

        var npc = TestHelpers.NpcState(20f, 32f);
        npc.PY = GroundPy;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, Def, npc, baked);

        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick(new()
            {
                [1] = tick == 0 ? TestHelpers.Input(activeSlot: AbilitySlots.E) : default,
                [100] = default,
            });
            if (tick == 0)
                Assert.Equal(AbilitySlots.E, sim.GetState(1).AttackSlot);
            Assert.DoesNotContain(sim.LastTickHits, hit => hit.OwnerEntityId == 1 && hit.TargetEntityId == 100);
            Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        }

        Assert.Null(sim.GetActiveAbility(1));
        Assert.Equal((byte)0, sim.GetState(1).AttackSlot);
    }

    [Theory]
    [InlineData(0.5f, 1.0f)]
    [InlineData(0.7f, 1.5f)]
    [InlineData(1.0f, 2.0f)]
    public void RisingDragon_AirborneVictim_Connects(float distance, float height)
    {
        // The designed anti-air case: victim airborne at close range — the hitbox rides the
        // attacker's rise, so it should catch a mid-air opponent the grounded one misses.
        var baked = TestHelpers.LoadBakedData(Def);
        Assert.NotNull(baked);

        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = GroundPy;
        player.FacingYaw = 0f;
        sim.RegisterEntity(1, Def, player, baked);

        var npc = TestHelpers.NpcState(0f, distance);
        npc.PY = GroundPy + height;
        npc.IsGrounded = false;
        npc.AirTimeTicks = 100;
        npc.DamagePercent = 0;
        sim.RegisterEntity(100, Def, npc, baked);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 4) }, { 100, default } });
        for (int i = 0; i < 30; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);
        Assert.True(npcAfter.DamagePercent > 0,
            $"Rising Dragon should connect on an airborne victim at {distance}m/{height}m, got {npcAfter.DamagePercent}");
    }

    // ── Cooldowns on slots 6-11 (TickTimers fix) ──

    [Fact]
    public void KiShot_Cooldown_AppliesAndExpires()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = GroundPy;
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Ki Shot starts immediately; its natural completion applies cooldown.
        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: 11) } });
        for (int i = 0; i < 70; i++)
            sim.Tick(new() { { 1, default } });

        ushort cd = sim.GetState(1).GetCooldown(AbilitySlots.A);
        Assert.True(cd > 0, $"expected cooldown on the A slot after firing, got {cd}");

        // The 120-tick cooldown must expire (slots 6-11 decrement since issue #117).
        for (int i = 0; i < 130; i++)
            sim.Tick(new() { { 1, default } });
        Assert.Equal((ushort)0, sim.GetState(1).GetCooldown(AbilitySlots.A));
    }
}
