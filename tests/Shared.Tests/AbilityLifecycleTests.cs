using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

using SlopArena.Shared.Abilities;

/// <summary>
/// Lifecycle contracts for authored abilities.
/// </summary>
public class AbilityLifecycleTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef;

    // ── Q: ServerAbility (MankiRoundBomb) — basic activation ──

    [Fact]
    public void MankiQ_BasicActivation()
    {
        var def = TestHelpers.ResolveSourceDef("manki");
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, def, state);

        Assert.Equal(AuthoringAimMovementMode.Fixed, (def.GetCookedSlotAbility(AbilitySlots.A, airborne: false)?.AimMovement ?? AuthoringAimMovementMode.Fixed));
        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: AbilitySlots.A), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)AbilitySlots.A, t0.AttackSlot);
    }










    // ══════════════════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════════════════



    // ══════════════════════════════════════════════════════════════════
    // ── R: Bazooka — FPS fire-and-forget rocket ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiR_BasicActivation_EntersCharging()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        TestHelpers.RegisterPlayer(sim, Def, state);

        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 1);
        Assert.Equal(ActionState.Attacking, after.State);
        Assert.True(after.IsAiming);
        Assert.Equal((byte)1, after.ComboStage);
        Assert.Equal((byte)5, after.AttackSlot);
    }

    [Fact]
    public void MankiR_RecoveryEnds_ReturnsToIdle()
    {
        var arena = TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);
        var state = TestHelpers.PlayerState();
        TestHelpers.RegisterPlayer(sim, Def, state);

        // Charge, firing and recovery total 75 ticks.
        var after = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 5), 80);

        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }

    [Fact]
    public void MankiR_WithoutTargetUsesFacingAndIgnoresAimPitch()
    {
        var sim = TestHelpers.MakeSim();
        TestHelpers.RegisterPlayer(sim, Def,
            TestHelpers.PlayerState(20f, 20f) with { PY = TestHelpers.GroundPY(Def), FacingYaw = MathF.PI / 3f });
        var aim = new InputState
        {
            ActiveSlot = AbilitySlots.R, FacingYaw = 6000, AimYaw = 6000, AimPitch = 3000,
        };
        sim.Tick(new() { [1] = aim });
        aim.ActiveSlot = 0;
        for (int tick = 0; tick < 4; tick++)
            sim.Tick(new() { [1] = aim });
        Assert.Empty(sim.Resolver.GetActiveHitboxes());

        var rockets = new List<Hitbox>();
        aim.IsAiming = false;
        for (int tick = 0; tick < 45; tick++)
        {
            sim.Tick(new() { [1] = aim });
            rockets.AddRange(sim.Resolver.LastTickCheckedHitboxes.Where(h =>
                h.OwnerId == 1 && h.Explosion.HasValue && h.AgeTicks == 0));
        }

        var rocket = Assert.Single(rockets);
        float horizontalSpeed = MathF.Sqrt(rocket.VX * rocket.VX + rocket.VZ * rocket.VZ);
        Assert.True(horizontalSpeed > 0f);
        Assert.Equal(MathF.PI / 3f, MathF.Atan2(rocket.VX, rocket.VZ), 3);
        Assert.Equal(0f,
            MathF.Atan2(rocket.VY + rocket.Gravity * Simulation.TickDt, horizontalSpeed), 3);
        Assert.Equal(0UL, rocket.HomingTargetEntityId);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── E (slot 3): Jetpack Boost — basic activation ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MankiE_BasicActivation_EntersAttackingCompression()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        var t0 = TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 1);
        Assert.Equal(ActionState.Attacking, t0.State);
        Assert.Equal((byte)4, t0.AttackSlot);
        Assert.Equal(0f, t0.VY);
    }

    [Fact]
    public void MankiE_IgnitionPresentationEvent_IsAuthoritativeAndPositional()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        float expectedFeetY = state.PY - Def.CapsuleHeight * 0.5f;
        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 4);

        var presentation = Assert.Single(sim.GetPresentationEvents(),
            e => e.PresentationId == "presentation.manki.jetpack-boost.ignition");
        Assert.Equal((ulong)1, presentation.EntityId);
        Assert.Equal("presentation.manki.jetpack-boost.ignition", presentation.PresentationId);
        Assert.Equal(PresentationEventSource.CapabilityExplosion, presentation.Source);
        Assert.Equal(expectedFeetY, presentation.WorldY, 5);
        Assert.Equal(0f, presentation.WorldYaw);
    }

    [Fact]
    public void MankiE_IgnitionHitbox_UsesSmallQExplosionAtFeet()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        float expectedFeetY = state.PY - Def.CapsuleHeight * 0.5f;
        TestHelpers.RegisterPlayer(sim, Def, state);

        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 4);

        var hitbox = Assert.Single(sim.Resolver.GetActiveHitboxes());
        Assert.Equal((byte)3, hitbox.Slot);
        Assert.Equal(1.5f, hitbox.Radius);
        Assert.Equal(expectedFeetY, hitbox.Y, 5);
        Assert.Equal(10f, hitbox.Damage);
        Assert.Equal(2.4f, hitbox.BaseKnockback);
        Assert.Equal(24f, hitbox.KnockbackGrowth);
        Assert.Equal((sbyte)30, hitbox.KnockbackAngle);
    }

    [Fact]
    public void MankiE_FliesThenReturnsToIdleAfterFuel()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        bool launched = false;
        var after = state;
        for (int tick = 0; tick < 220; tick++)
        {
            after = TestHelpers.TickN(sim,
                tick == 0 ? TestHelpers.Input(activeSlot: AbilitySlots.E) : default, 1);
            launched |= after.VY > 0f;
            if (launched && !sim.GetState(1).Flight.Active)
                break;
        }

        Assert.True(launched, "Jetpack must ignite before powered flight.");
        Assert.True(after.VY <= 0f, "Jetpack must release vertical ownership when fuel ends.");
        Assert.Equal(ActionState.Idle, after.State);
        Assert.Equal((byte)0, after.AttackSlot);
    }
    public static IEnumerable<object[]> GroundAbilityCases()
    {
        foreach (var character in new[] { CharacterClass.Manki, CharacterClass.FightGuy, CharacterClass.Wibou, CharacterClass.Bonk })
        {
            var def = BuiltInContentResolver.Resolve(character).Definition;
            for (byte wireSlot = 1; wireSlot <= AbilitySlots.Count; wireSlot++)
            {
                if (def.GetCookedSlotAbility(wireSlot, airborne: false) != null)
                    yield return new object[] { character, wireSlot };
            }
        }
    }

        public static IEnumerable<object[]> GroundAndAirAimingAbilityCases()
    {
        foreach (var character in new[] { CharacterClass.Manki, CharacterClass.FightGuy, CharacterClass.Wibou, CharacterClass.Bonk })
        {
            var def = BuiltInContentResolver.Resolve(character).Definition;
            for (byte wireSlot = 1; wireSlot <= AbilitySlots.Count; wireSlot++)
            {
                for (int air = 0; air < 2; air++)
                {
                    bool airborne = air != 0;
                    var cooked = def.GetCookedSlotAbility(wireSlot, airborne);
                    bool aiming = cooked != null && cooked.AimMode != AuthoringAimMode.None;
                    if (aiming)
                        yield return new object[] { character, wireSlot, airborne };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(GroundAndAirAimingAbilityCases))]
    public void EveryAuthoredAimingAbility_FacesActivationAim(
        CharacterClass character, byte wireSlot, bool airborne)
    {
        var def = BuiltInContentResolver.Resolve(character).Definition;
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def) + (airborne ? 2f : 0f);
        state.IsGrounded = !airborne;
        TestHelpers.RegisterPlayer(sim, def, state);

        const short aimYaw = 9000; // +90° world yaw
        sim.Tick(new()
        {
            { 1, new InputState { ActiveSlot = wireSlot, AimYaw = aimYaw, IsAiming = true } },
        });

        TestHelpers.AssertNear(MathF.PI / 2f, sim.GetState(1).FacingYaw);
    }

[Theory]
    [MemberData(nameof(GroundAbilityCases))]
    public void EveryAuthoredGroundAbility_ReleasesAttackState(CharacterClass character, byte wireSlot)
    {
        var def = BuiltInContentResolver.Resolve(character).Definition;
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.GroundPY(def);
        TestHelpers.RegisterPlayer(sim, def, state);

        sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: wireSlot, aiming: true) } });
        var started = sim.GetState(1);
        Assert.Equal(wireSlot, started.AttackSlot);
        Assert.NotEqual(ActionState.Idle, started.State);
        for (int tick = 0; tick < 600; tick++)
        {
            var current = sim.GetState(1);
            if (current.State == ActionState.Idle && current.AttackSlot == 0)
                return;
            sim.Tick(new() { { 1, default } });
        }

        var final = sim.GetState(1);
        Assert.Equal(ActionState.Idle, final.State);
        Assert.Equal((byte)0, final.AttackSlot);
    }

    [Fact]
    public void HeldAbilityInput_DoesNotRestartCompletedAbility()
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState();
        state.PY = TestHelpers.MankiGroundPY;
        TestHelpers.RegisterPlayer(sim, Def, state);

        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick(new() { { 1, TestHelpers.Input(activeSlot: AbilitySlots.E) } });
            if (sim.GetState(1).State == ActionState.Idle)
                return;
        }

        Assert.Fail("Held ability input restarted the ability instead of releasing it.");
    }

}
