using System;
using System.Linq;

namespace SlopArena.Shared;

public sealed record DashStrikeCapabilityParameters(
    float Distance, float Speed, ushort TipPoseTick, ushort RecoveryTicks,
    HitboxSource LightHitbox, HitboxSource TipHitbox, string TipPresentationId) : TypedCapabilityParameters;

public sealed record CookedDashStrikeCapabilityParameters(
    float Distance, float Speed, ushort TipPoseTick, ushort RecoveryTicks,
    CookedHitbox LightHitbox, CookedHitbox TipHitbox, string TipPresentationId) : CookedCapabilityParameters
{
    public int TravelTicks => Math.Max(1, (int)Math.Ceiling(Distance / (Speed * Simulation.TickDt)));
}

internal static class DashStrikeContract
{
    internal static string? BoundsError(CookedDashStrikeCapabilityParameters x)
    {
        if (!Finite(x.Distance) || x.Distance <= 0 || x.Distance > 12 ||
            !Finite(x.Speed) || x.Speed <= 0 || x.Speed > 60 || x.RecoveryTicks == 0 || x.RecoveryTicks > 120)
            return "Dash strike requires finite distance (0, 12], speed (0, 60], and recovery 1..120 ticks.";
        if (x.Distance / (double)(x.Speed * Simulation.TickDt) > ushort.MaxValue)
            return "Dash strike travel exceeds the supported tick budget.";
        var light = x.LightHitbox;
        var tip = x.TipHitbox;
        if (light.Shape != AuthoringHitboxShape.Sphere || light.StartBoneId != null || light.EndBoneId != null ||
            tip.StartBoneId != null || tip.EndBoneId != null || light.HitGroup != 0 || tip.HitGroup != 0)
            return "Dash strike requires a root-relative light sphere and root-relative tip, with authored hitGroup 0.";
        if (light.BaseKnockback != 0 || light.KnockbackGrowth != 0 || light.FixedHitstunTicks == 0 || tip.DurationTicks == 0)
            return "The light cut needs zero base/growth and positive fixed hitstun; the tip needs a positive lifetime.";
        if (x.Speed * Simulation.TickDt > 2 * light.Radius)
            return "Dash strike travel per tick exceeds twice the light radius, leaving gaps in cut coverage.";
        return null;
    }

    internal static string? SlotError(int count, int stages, ushort tick, AuthoringAbilityBehavior behavior,
        AuthoringAimMode aim, ushort iasa, bool extra, bool carry, bool charge, ushort duration,
        CookedDashStrikeCapabilityParameters p)
        => count != 1 || stages != 1 || tick != 0 || behavior != AuthoringAbilityBehavior.MeleeCombo ||
           aim != AuthoringAimMode.None || iasa != 0 || extra || carry || charge
            ? "Dash strike requires one tick-zero meleeCombo lifecycle, no aim, IASA, carry or charge pool, and only presentation alongside it."
            : p.TravelTicks + p.TipHitbox.DurationTicks + p.RecoveryTicks > duration ||
              (int)p.TipPoseTick + p.TipHitbox.DurationTicks + p.RecoveryTicks > duration
                ? "Dash strike travel, tip pose, tip lifetime and recovery must fit the stage watchdog."
                : null;
    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
}
