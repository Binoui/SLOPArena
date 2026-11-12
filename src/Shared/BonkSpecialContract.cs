using System;
using System.Linq;

namespace SlopArena.Shared;

internal static class BonkSpecialContract
{
    internal static string? Charge(CookedSteeredChargeCapabilityParameters p)
    {
        var rulesLaunch = CharacterRuntimeLimits.Object("launch");
        var rulesChargeCarry = CharacterRuntimeLimits.Object("chargeCarry");
        var rulesCapSteeredChargeV2 = CharacterRuntimeLimits.Object("cap.steered-charge.v2");
        var rulesChargeAssist = CharacterRuntimeLimits.Object("chargeAssist");
        if (p.Assist is { } a && (!Finite(a.AssistRange, a.AssistHalfAngleDegrees, a.AssistDegreesPerSecond)
            || rulesChargeAssist.Field("assistRange").IsOutsideGameplay(a.AssistRange) || rulesChargeAssist.Field("assistHalfAngleDegrees").IsOutsideGameplay(a.AssistHalfAngleDegrees)
            || rulesChargeAssist.Field("assistDegreesPerSecond").IsOutsideGameplay(a.AssistDegreesPerSecond)))
            return "Charge assist range, cone or turn rate is invalid.";
        var c = p.Carry; var f = c.Fling;
        if (!Finite(p.TrackDegreesPerSecond, p.ChallengedTrackDegreesPerSecond, c.AnchorDistance, c.AnchorHeight,
                f.Damage, f.Angle, f.BaseKnockback, f.KnockbackGrowth)
            || rulesCapSteeredChargeV2.Field("trackDegreesPerSecond").IsOutsideGameplay(p.TrackDegreesPerSecond)
            || rulesCapSteeredChargeV2.Field("challengedTrackDegreesPerSecond").IsOutsideGameplay(p.ChallengedTrackDegreesPerSecond)
            || rulesCapSteeredChargeV2.Field("challengedRearTicks").IsOutsideGameplay(p.ChallengedRearTicks)
            || rulesChargeCarry.Field("anchorDistance").IsOutsideGameplay(c.AnchorDistance) || rulesChargeCarry.Field("anchorHeight").IsOutsideGameplay(c.AnchorHeight)
            || rulesChargeCarry.Field("maxCarryTicks").IsOutsideGameplay(c.MaxCarryTicks) || rulesChargeCarry.Field("flingTicks").IsOutsideGameplay(c.FlingTicks)
            || rulesLaunch.Field("damage").IsOutsideGameplay(f.Damage) || rulesLaunch.Field("angle").IsOutsideGameplay(f.Angle)
            || f.BaseKnockback < rulesLaunch.Field("baseKnockback").GameplayMinimum!.Value || f.KnockbackGrowth < rulesLaunch.Field("knockbackGrowth").GameplayMinimum!.Value || f.StunTicks > rulesLaunch.Field("stunTicks").GameplayMaximum!.Value
            || p.ChestHitbox.StartBoneId != null || p.ChestHitbox.EndBoneId != null || p.ChestHitbox.HitGroup != 0
            || p.LanceHitbox.Damage <= 0 || p.ChestHitbox.Damage != p.LanceHitbox.Damage)
            return "Charge tracking, chest contact, anchor, carry or fling values are invalid.";
        return null;
    }

    internal static string? Leap(CookedTargetedLeapCapabilityParameters p)
    {
        var rulesCapTargetedLeapV2 = CharacterRuntimeLimits.Object("cap.targeted-leap.v2");
        if (!Finite(p.RiseHeight, p.MaxRange, p.NoTargetDistance) || rulesCapTargetedLeapV2.Field("riseHeight").IsOutsideGameplay(p.RiseHeight)
            || rulesCapTargetedLeapV2.Field("maxRange").IsOutsideGameplay(p.MaxRange) || p.NoTargetDistance < 0 || p.NoTargetDistance > p.MaxRange
            || rulesCapTargetedLeapV2.Field("riseTicks").IsOutsideGameplay(p.RiseTicks) || rulesCapTargetedLeapV2.Field("hangTicks").IsOutsideGameplay(p.HangTicks)
            || rulesCapTargetedLeapV2.Field("dropTicks").IsOutsideGameplay(p.DropTicks) || rulesCapTargetedLeapV2.Field("maxFallTicks").IsOutsideGameplay(p.MaxFallTicks)
            || p.HitRecoveryTicks == 0 || p.MissRecoveryTicks < p.HitRecoveryTicks || p.MissRecoveryTicks > rulesCapTargetedLeapV2.Field("missRecoveryTicks").GameplayMaximum!.Value
            || p.Hitbox.DurationTicks == 0 || p.Hitbox.DurationTicks > p.HitRecoveryTicks)
            return "Leap heights, ranges, beats or recoveries are invalid.";
        return null;
    }

    internal static string? LegHit(ProjectileLegHit h)
    {
        var rules = CharacterRuntimeLimits.Object("legHit");
        return !Finite(h.Damage, h.Angle, h.BaseKnockback, h.KnockbackGrowth)
            || rules.Field("damage").IsOutsideGameplay(h.Damage) || rules.Field("angle").IsOutsideGameplay(h.Angle)
            || rules.Field("baseKnockback").IsOutsideGameplay(h.BaseKnockback) || rules.Field("knockbackGrowth").IsOutsideGameplay(h.KnockbackGrowth)
            || rules.Field("stunTicks").IsOutsideGameplay(h.StunTicks) || rules.Field("fixedHitstunTicks").IsOutsideGameplay(h.FixedHitstunTicks)
            || h.FixedHitstunTicks > 0 && h.StunTicks == 0
            ? "Return hit values are invalid; fixed hitstun needs a nonzero stun gate." : null;
    }

    internal static bool NeedsApi(CookedSlotDefinition slot)
        => slot.Timeline.Stages.SelectMany(s => s.Operations).Any(op => op is CookedStartCapabilityOperation
            { Parameters: CookedSteeredChargeCapabilityParameters or CookedTargetedLeapCapabilityParameters }
            || op is CookedSpawnProjectileOperation { Projectile: var p } && (p.ChallengeTicks > 0 || p.Return?.Hit != null)
            || op is CookedStartCapabilityOperation { Parameters: CookedChargedProjectileCapabilityParameters { Projectile.ChallengeTicks: > 0 }
                or CookedChargedStreamCapabilityParameters { Projectile.ChallengeTicks: > 0 }
                or CookedFingerGunsCapabilityParameters { Projectile.ChallengeTicks: > 0 } });

    internal static string? Slot(CharacterSlotSource slot, StartCapabilityOperationSource op)
    {
        var stages = slot.Timeline.Stages;
        if (stages.Count != 1 || op.Tick != 0 || slot.Behavior != AuthoringAbilityBehavior.MeleeCombo
            || slot.AimMode != AuthoringAimMode.None || slot.AllowSlideCarry || slot.ChargePool != null
            || stages[0].IasaTicks != 0 || stages[0].Operations.Any(o => o != op && o is not EmitPresentationOperationSource))
            return "A v2 special needs one tick-zero lifecycle, one meleeCombo stage, no aim, carry, charge pool or IASA, with only presentation alongside it.";
        var clips = op.Parameters switch
        {
            TargetedLeapCapabilityParameters p => new[] { p.RiseAnimationId, p.HangAnimationId, p.DropAnimationId, p.LandingAnimationId },
            SteeredChargeCapabilityParameters p => new[] { p.Carry.FlingAnimationId },
            _ => Array.Empty<string>(),
        };
        if (clips.Any(id => id.Length > 0 && !stages[0].AnimationIds.Contains(id, StringComparer.Ordinal)))
            return "A phase clip must be listed in the stage.";
        return null;
    }

    internal static string? Slot(CookedSlotDefinition slot, CookedStartCapabilityOperation op)
    {
        var stages = slot.Timeline.Stages;
        if (stages.Count != 1 || op.Tick != 0 || slot.Behavior != AuthoringAbilityBehavior.MeleeCombo
            || slot.AimMode != AuthoringAimMode.None || slot.AllowSlideCarry || slot.ChargePool != null
            || stages[0].IasaTicks != 0 || stages[0].Operations.Any(o => o != op && o is not CookedEmitPresentationOperation))
            return "A v2 special owns one tick-zero lifecycle in one meleeCombo stage, no aim, carry, charge pool or IASA, with only presentation alongside it.";
        return null;
    }
    private static bool Finite(params float[] values) => values.All(v => !float.IsNaN(v) && !float.IsInfinity(v));
}
