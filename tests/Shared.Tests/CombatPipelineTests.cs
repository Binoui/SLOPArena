using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════
/// GROUND-TRUTH COMBAT TESTS
/// ═══════════════════════════════════════════════════════════════════════
///
/// These tests exercise the FULL combat pipeline end to end:
///   Input → ServerAbility.OnStart → Ability.Tick → Resolver.Spawn →
///   BuildHurtboxList → Resolver.Tick → ResolveHits → ApplyKnockback
///
/// Each test verifies that a hitbox actually collides with an entity's
/// hurtbox and produces the correct damage/knockback/hitstun.
///
/// They serve as:
///   - Regression protection for the combat pipeline
///   - Ground-truth documentation of how abilities behave at the tick level
///   - A reference for agents modeling the game runtime
///
/// Generic hit-response and raw-hit cases use TestHelpers.EngineDef so their
/// mechanics do not inherit roster tuning. The Q projectile scenario deliberately
/// executes Manki's authored kit against a synthetic target.
/// ═══════════════════════════════════════════════════════════════════════
public class CombatPipelineTests
{
    private static readonly float Gpy = TestHelpers.GroundPY(TestHelpers.MankiDef);

    // ═══════════════════════════════════════════════════════════════════
    // The authored Manki Q regression below verifies its real projectile path;
    // its receiver and hurtbox geometry are synthetic.

    // ═══════════════════════════════════════════════════════════════════
    // TEST 1b: Re-hit while in hitstun resets HitstunTicks
    // ═══════════════════════════════════════════════════════════════════
    //
    // Two players attack the same NPC on consecutive ticks while the
    // hitboxes overlap. Player1 hits on tick 12 (LMB stage 1 trigger).
    // Player2's same-tick hitbox is blocked by hitThisTick, but player2's
    // hitbox (still active) hits on tick 13 while NPC is still in hitstun.
    //
    // Expected: HitstunTicks resets upward (new hit value, not continued
    // countdown). The client uses this to detect re-hits and restart
    // animation from frame 0.
    //
    [Theory]
    [InlineData(false, 0, 1, 20, true, "Fresh hit")]
    [InlineData(true, 1, 1, 31, false, "Normal countdown")]
    [InlineData(true, 1, 1, 32, false, "Repeated snapshot")]
    [InlineData(true, 1, 1, 16, false, "Skipped snapshots")]
    [InlineData(true, 1, 2, 32, true, "Same-duration re-hit")]
    [InlineData(true, 1, 2, 16, true, "Shorter re-hit")]
    public void ReHit_ClientDetectionLogic_Correct(bool wasInHitstun, ushort presentedSequence, ushort sequence,
        ushort currentTicks, bool expectedNewHit, string desc)
    {
        var state = new CharacterState { State = ActionState.Hitstun,
            HitstunTicks = currentTicks, HitReactionSequence = sequence };
        bool result = HitstunPresentation.ShouldRestart(
            wasInHitstun ? ActionState.Hitstun : ActionState.Idle, presentedSequence, state);
        if (result != expectedNewHit)
            Assert.Fail($"{desc}: expected {expectedNewHit} but got {result}");
    }
    [Fact]
    public void QProjectile_HitsNpc_DealsDamage()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        var def = TestHelpers.MankiDef;
        var player = TestHelpers.PlayerState();
        player.PY = Gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 1.5f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        sim.RegisterEntity(100, TestHelpers.EngineDef, npc);

        var aimInput = TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: true, aimDistance: 500);
        var releaseInput = new InputState { ActiveSlot = AbilitySlots.A, AimDistance = 500, IsAiming = false };

        sim.Tick(new() { { 1, aimInput }, { 100, default } });
        Assert.Equal(ActionState.Attacking, sim.GetState(1).State);

        for (int i = 0; i < 8; i++)
            sim.Tick(new() { { 1, aimInput }, { 100, default } });

        for (int i = 0; i < 3; i++)
            sim.Tick(new() { { 1, releaseInput }, { 100, default } });

        for (int i = 0; i < 60; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);

        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should have taken damage from Q projectile or explosion, got {npcAfter.DamagePercent}");

        Assert.True(npcAfter.DamagePercent >= 3,
            $"Bomb blast deals damage, got {npcAfter.DamagePercent}");
        Assert.Equal(0, (int)npcAfter.HitstunLevel);
    }







    [Fact]
    public void LightProfile_ApplyKnockback_HasVerticalComponent()
    {
        var state = TestHelpers.PlayerState();
        state.DamagePercent = 50;

        Simulation.ApplyKnockback(ref state, dirX: 1f, dirZ: 0f,
            angleDeg: 5, baseKB: 2f, growthKB: 1.5f, damage: 0f, stunTicks: 20, weight: 100f);

        Assert.True(state.KVY > 0f,
            $"Light profile (5°) should produce vertical knockback, got KVY={state.KVY:F4}");
    }

    [Fact]
    public void MediumProfile_ApplyKnockback_HasVerticalComponent()
    {
        var state = TestHelpers.PlayerState();
        state.DamagePercent = 50;

        Simulation.ApplyKnockback(ref state, dirX: 1f, dirZ: 0f,
            angleDeg: 15, baseKB: 8f, growthKB: 5f, damage: 0f, stunTicks: 20, weight: 100f);

        // KVY = 15.5 · sin15° · KbScaleFactor(0.11) ≈ 0.44 — the 0.11 velocity scale
        // lowered the launch; assert the vertical component survives, not the old 0.5.
        Assert.True(state.KVY > 0.4f,
            $"Medium profile (15°) should produce noticeable vertical knockback, got KVY={state.KVY:F4}");
    }


    [Fact]
    public void RealHit_ZeroHitstop_AppliesTargetDIImmediately()
    {
        var sim = TestHelpers.MakeSim(TestHelpers.TestArena());
        var attacker = TestHelpers.PlayerState();
        attacker.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        sim.RegisterEntity(1, TestHelpers.EngineDef, attacker);
        var target = TestHelpers.NpcState(0f, 1.5f);
        target.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        sim.RegisterEntity(100, TestHelpers.EngineDef, target);

        sim.Resolver.Spawn(new Hitbox
        {
            X = target.PX, Y = target.PY, Z = target.PZ,
            EndX = target.PX, EndY = target.PY, EndZ = target.PZ,
            Radius = 1f, Shape = HitboxShape.Sphere,
            Damage = 0f, BaseKnockback = 10f, KnockbackGrowth = 0f,
            KnockbackAngle = 45, StunTicks = 20, DurationTicks = 1,
            OwnerId = 1,
        });

        sim.Tick(new()
        {
            { 1, default },
            { 100, new InputState { MoveX = 1f, MoveY = 0f } },
        });

        var frozen = sim.GetState(100);
        Assert.True(frozen.HitstopTicks > 0);
        Assert.Equal(ActionState.Run, frozen.State);

        // 0-damage hit freezes 6 ticks under ADR-0019 (min(12, 0/3 + 6)) — was 2 under ADR-0012.
        for (int i = 0; i < 12 && sim.GetState(100).HitstopTicks > 0; i++)
        {
            sim.Tick(new()
            {
                { 1, default },
                { 100, new InputState { MoveX = 1f, MoveY = 0f } },
            });
        }
        var state = sim.GetState(100);
        Assert.Equal(0, state.HitstopTicks);
        Assert.Equal(ActionState.Hitstun, state.State);
        Assert.True(state.KVX > 0f);
        Assert.True(state.KVY > 0f);
        Assert.Equal(1f, state.DIX);
        Assert.True(state.KVZ > 0f, $"DI should preserve the launch-side horizontal direction, got KVZ={state.KVZ}");
    }
}
