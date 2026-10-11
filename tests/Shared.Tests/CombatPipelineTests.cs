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
    // TEST 2: Q projectile hits NPC, explodes
    // ═══════════════════════════════════════════════════════════════════
    //
    // Manki Q (Round Bomb):
    //   Phase 1 (hold, ~8 ticks): player aims (auto-release without input)
    //   Phase 2 (throw, 60 ticks): projectile spawned at trigger_tick=10
    //
    // Projectile:
    //   Launch: 30° upward, gravity=30, default aim distance=5m
    //   Hitbox: sphere radius 0.6, Damage=6
    //   Explosion: radius 3.0, Damage=10
    //
    // Timing estimate:
    //   Hold phase: ticks 0-8 (AttackElapsedTicks: 1→9, transition at 9 > 8)
    //   Throw phase starts: tick 8 (AttackElapsedTicks reset to 0)
    //   Projectile spawn: tick 18 (AttackElapsedTicks=10 in throw phase)
    //   Flight to ~Z=3.5 (ground impact): ≈22 ticks → tick ~40
    //   Explosion at ground: ProcessProjectileExplosions on tick 40
    //   Explosion hitbox processed: ResolveHits on tick 41→42
    //
    // Q projectile test.
    // Timing:
    //   Hold phase (aim with aimDistance=500): ticks 0-9 (AttackElapsedTicks 1→10)
    //   Release pull (IsAiming=false, aimDistance still set): triggers transition
    //   Throw phase: projectile spawns at AttackElapsedTicks=10
    //   Flight to Z=5 (ground impact): ≈22 ticks from spawn
    //   Explosion: next tick after ground impact

    [Fact]
    public void QProjectile_HitsNpc_DealsDamage()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        var def = TestHelpers.MankiDef;
        var player = TestHelpers.PlayerState();
        player.PY = Gpy;
        sim.RegisterEntity(1, def, player);

        var npc = TestHelpers.NpcState(0f, 3.5f);
        npc.PY = TestHelpers.GroundPY(TestHelpers.EngineDef);
        sim.RegisterEntity(100, TestHelpers.EngineDef, npc);

        // Build the aim input once
        var aimInput = TestHelpers.Input(activeSlot: AbilitySlots.A, aiming: true, aimDistance: 500);
        var releaseInput = new InputState { ActiveSlot = AbilitySlots.A, AimDistance = 500, IsAiming = false };

        // Tick 0: press Q with aim
        sim.Tick(new() { { 1, aimInput }, { 100, default } });
        Assert.Equal(ActionState.Aiming, sim.GetState(1).State);

        // Hold for 8 more ticks (so AttackElapsedTicks reaches 9+, exceeding 8)
        for (int i = 0; i < 8; i++)
            sim.Tick(new() { { 1, aimInput }, { 100, default } });

        // Release: IsAiming=false triggers transition, AimDistance=500 gives cached dist=5m
        for (int i = 0; i < 3; i++)
            sim.Tick(new() { { 1, releaseInput }, { 100, default } });

        // Wait for projectile flight (~22 ticks) + explosion + margin
        for (int i = 0; i < 60; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });

        var npcAfter = sim.GetState(100);

        // NPC should have taken SOME damage from projectile or explosion
        Assert.True(npcAfter.DamagePercent > 0,
            $"NPC should have taken damage from Q projectile or explosion, got {npcAfter.DamagePercent}");

        // Direct projectile hit = 6 damage minimum (not stacking with explosion 10)
        Assert.True(npcAfter.DamagePercent >= 6,
            $"Direct projectile hit = 6 damage minimum, got {npcAfter.DamagePercent}");
        // Both direct hit (stun=28) and explosion (stun=20) are ≤30 → HitstunLevel = 0
        Assert.Equal(0, (int)npcAfter.HitstunLevel);
    }


    // ═══════════════════════════════════════════════════════════════════
    // EDGE CASE: Mutual combat — two entities attack each other
    // ═══════════════════════════════════════════════════════════════════
    //
    // Both Player (1) and NPC (100) press LMB simultaneously. Their hitboxes
    // overlap due to close proximity. This tests that:
    //   1. Both hitboxes resolve without crashing
    //   2. Both entities receive damage/knockback/hitstun
    //   3. The simulation doesn't corrupt state when both sides are hit
    //
    // NOTE on ability interruption: ServerAbility has no OnInterrupt. When an
    // entity is hitstunned, its ability.Tick() continues running in the
    // TickAbilities phase. If EndAbility() fires during hitstun, it overwrites
    // State=Idle, clearing the hitstun state. This is a known design gap.
    // This test verifies the sim doesn't crash under these conditions.




    // ═══════════════════════════════════════════════════════════════════
    // VERTICAL KNOCKBACK VERIFICATION
    // ═══════════════════════════════════════════════════════════════════

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
