using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Hit and block hooks belong to the activation whose hitbox made the contact, not to whatever
/// move its owner is in when a slower projectile arrives.
/// </summary>
public sealed class ContactHookOwnershipTests
{
    private static readonly CharacterDefinition Definition = CreateDefinition();
    private static readonly CharacterDefinition TargetDef = TestHelpers.EngineDef;

    [Fact]
    public void ProjectileBlockedDuringALaterDashDoesNotStopTheDash()
    {
        var sim = new ServerSimulation(TestHelpers.TestArena());
        sim.RegisterEntity(1, Definition, TestHelpers.PlayerState() with { PY = TestHelpers.GroundPY(Definition) });
        sim.RegisterEntity(2, TargetDef, TestHelpers.PlayerState(4f, 0f) with
        {
            EntityId = 2,
            PY = TestHelpers.GroundPY(TargetDef),
            State = ActionState.Shielding,
        });

        // A: a slow projectile to the right, at the shielding target.
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.A, AimYaw = 0 });
        while (sim.GetActiveAbility(1) != null)
            Tick(sim, default);
        Assert.Single(sim.Resolver.GetActiveHitboxes());

        // R: charge, then dash forward (+Z), away from the projectile's path.
        Tick(sim, new InputState { ActiveSlot = AbilitySlots.R, IsAiming = true, AimYaw = 0 });
        for (int i = 1; i < 20; i++)
            Tick(sim, new InputState { IsAiming = true, AimYaw = 0 });
        Tick(sim, default);

        bool blockedDuringDash = false;
        for (int i = 0; i < 180 && sim.GetActiveAbility(1) != null; i++)
        {
            bool dashing = sim.GetActiveAbility(1)!.IgnoresFighterPushboxes;
            Tick(sim, default);
            blockedDuringDash |= dashing && sim.LastTickHits.Any(hit => hit.TargetEntityId == 2 && hit.Blocked);
        }

        Assert.True(blockedDuringDash, "the projectile must reach the shield while the dash travels");
        Assert.InRange(sim.GetState(1).PZ, 2.93f, 2.937f);
    }

    private static void Tick(ServerSimulation sim, InputState attacker)
        => sim.Tick(new Dictionary<ulong, InputState>
        {
            [1] = attacker,
            [2] = new InputState { ShieldHeld = true },
        });

    private static CharacterDefinition CreateDefinition()
    {
        var definition = TestHelpers.EngineDef;
        var slots = definition.CookedSlots!.ToArray();
        var projectile = new CookedProjectile(0f, 0f, 0f, 4f, 0f, .3f, 3f, 45f, 4f, 10f, 10, 240,
            YawOffsetDegrees: 90f);
        slots[3] = new CookedSlotDefinition(3, "ground.A", false, "Slow shot fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(10, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
                {
                    new CookedSpawnProjectileOperation(0, AuthoringUnit.Ticks, projectile),
                }),
            }));

        var traversal = new CookedHitbox(AuthoringHitboxShape.Sphere, .45f,
            0f, 0f, 0f, 0f, 0f, 0f, null, null, 1f, 0f, 0f, 0f, 0, 1, true, 0);
        var finisher = new CookedHitbox(AuthoringHitboxShape.Capsule, .5f,
            0f, 0f, 0f, 0f, 0f, 0f, "_weapon_hilt", "_weapon_tip", 6f, 45f, 5f, 80f, 8, 9, true, 0);
        var dash = new CookedChargedDirectionalDashCapabilityParameters(
            60, 20, 45, 1.2f, 6.4f, 10.5f, 4, 31, 24, 9f, 12f, traversal, finisher);
        slots[5] = new CookedSlotDefinition(5, "ground.R", false, "Charged dash fixture", "", "",
            AuthoringAbilityBehavior.DirectionalDash, AuthoringAimMode.GroundVector, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(80, 0, 0, 0, 0, Array.Empty<string>(), new CookedTimelineOperation[]
                {
                    new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
                        CharacterPackageCompiler.ChargedDirectionalDashCapabilityId, "1", dash),
                }),
            }), aimMovement: AuthoringAimMovementMode.Mobile);
        definition = definition with { CookedSlots = slots };

        return definition;
    }
}
