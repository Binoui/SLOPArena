using System;
using System.Linq;

namespace SlopArena.Shared;

internal static class BonkSpecialContract
{
    internal static string? Charge(CookedSteeredChargeCapabilityParameters p)
    {
        if (p.Assist is { } a && (!Finite(a.AssistRange, a.AssistHalfAngleDegrees, a.AssistDegreesPerSecond)
            || a.AssistRange <= 0 || a.AssistRange > 10 || a.AssistHalfAngleDegrees <= 0 || a.AssistHalfAngleDegrees > 90
            || a.AssistDegreesPerSecond <= 0 || a.AssistDegreesPerSecond > 1440))
            return "Charge assist range, cone or turn rate is invalid.";
        var c = p.Carry; var f = c.Fling;
        if (!Finite(p.TrackDegreesPerSecond, p.ChallengedTrackDegreesPerSecond, c.AnchorDistance, c.AnchorHeight,
                f.Damage, f.Angle, f.BaseKnockback, f.KnockbackGrowth)
            || p.TrackDegreesPerSecond < 0 || p.TrackDegreesPerSecond > 1440
            || p.ChallengedTrackDegreesPerSecond < 0 || p.ChallengedTrackDegreesPerSecond > 1440
            || p.ChallengedRearTicks == 0 || p.ChallengedRearTicks > 120
            || c.AnchorDistance < 0 || c.AnchorDistance > 3 || c.AnchorHeight < 0 || c.AnchorHeight > 3
            || c.MaxCarryTicks == 0 || c.MaxCarryTicks > 120 || c.FlingTicks == 0 || c.FlingTicks > 120
            || f.Damage <= 0 || f.Damage > 100 || f.Angle < -90 || f.Angle > 90
            || f.BaseKnockback < 0 || f.KnockbackGrowth < 0 || f.StunTicks > 240
            || p.ChestHitbox.StartBoneId != null || p.ChestHitbox.EndBoneId != null || p.ChestHitbox.HitGroup != 0
            || p.LanceHitbox.Damage <= 0 || p.ChestHitbox.Damage != p.LanceHitbox.Damage)
            return "Charge tracking, chest contact, anchor, carry or fling values are invalid.";
        return null;
    }

    internal static string? Leap(CookedTargetedLeapCapabilityParameters p)
    {
        if (!Finite(p.RiseHeight, p.MaxRange, p.NoTargetDistance) || p.RiseHeight <= 0 || p.RiseHeight > 30
            || p.MaxRange <= 0 || p.MaxRange > 60 || p.NoTargetDistance < 0 || p.NoTargetDistance > p.MaxRange
            || p.RiseTicks == 0 || p.RiseTicks > 240 || p.HangTicks == 0 || p.HangTicks > 240
            || p.DropTicks == 0 || p.DropTicks > 240 || p.MaxFallTicks == 0 || p.MaxFallTicks > 600
            || p.HitRecoveryTicks == 0 || p.MissRecoveryTicks < p.HitRecoveryTicks || p.MissRecoveryTicks > 600
            || p.Hitbox.DurationTicks == 0 || p.Hitbox.DurationTicks > p.HitRecoveryTicks)
            return "Leap heights, ranges, beats or recoveries are invalid.";
        return null;
    }

    internal static string? LegHit(ProjectileLegHit h)
        => !Finite(h.Damage, h.Angle, h.BaseKnockback, h.KnockbackGrowth) || h.Damage < 0 || h.Damage > 100
           || h.Angle < -90 || h.Angle > 90 || h.BaseKnockback < 0 || h.KnockbackGrowth < 0
           || h.StunTicks > 240 || h.FixedHitstunTicks > 240 || h.FixedHitstunTicks > 0 && h.StunTicks == 0
            ? "Return hit values are invalid; fixed hitstun needs a nonzero stun gate." : null;

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
