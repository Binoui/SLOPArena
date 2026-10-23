using System;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.EditorTools
{
    internal static class PresentationSelfTestContent
    {
        internal static CookedStage Stage(ushort duration, string[] animations, params HitboxEvent[] hits)
            => new CookedStage(duration, 0, 0, 0, 0, animations,
                hits.Select(hit =>
                {
                    var kb = hit.Knockback.Resolve();
                    return (CookedTimelineOperation)new CookedSpawnHitboxOperation(hit.TriggerTick, AuthoringUnit.Meters,
                        new CookedHitbox((AuthoringHitboxShape)hit.Shape, hit.Radius, hit.OffX, hit.OffY, hit.OffZ,
                            hit.EndOffX, hit.EndOffY, hit.EndOffZ, hit.BoneName, hit.EndBoneName, hit.Damage,
                            kb.angle, kb.baseKB, kb.growthKB, hit.StunTicks, hit.DurationTicks, hit.Interruptible,
                            hit.HitGroup, hit.KnockbackDirection, hit.FixedHitstunTicks));
                }).ToArray());

        internal static CookedSlotDefinition Move(int ordinal, params CookedStage[] stages)
        {
            var address = CanonicalSlotProjection.All[ordinal];
            return new CookedSlotDefinition(ordinal, address.Id, address.IsAirborne, "Presentation fixture", "", "",
                AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false, new CookedTimeline(stages));
        }

        internal static CookedSlotDefinition Replace(CookedSlotDefinition slot, CookedTimeline timeline, string aimAnimationId)
            => new CookedSlotDefinition(slot.Ordinal, slot.Id, slot.IsAir, slot.Name, slot.Description, slot.IconId,
                slot.Behavior, slot.AimMode, slot.CooldownTicks, slot.IsRecoveryMove, slot.PreserveMomentumOnStart,
                timeline, slot.ChargePool, slot.AimMovement, aimAnimationId, slot.AllowSlideCarry,
                slot.HitPresentationId, slot.AimMovementSpeedScale);
    }
}
