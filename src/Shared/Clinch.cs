using System;

namespace SlopArena.Shared;

/// <summary>
/// slop.ability.clinch.v1 (runtime API 1.17.0, ADR-0053): a command grab owning the first two stages of its move.
/// Stage 0 is the lunge: the fighter dashes <see cref="LungeDistance"/> at <see cref="LungeSpeed"/> toward the current
/// target, level and with gravity off, carrying <see cref="CatchHitbox"/> (a root-relative sphere; its knockback,
/// stun, lifetime and hit group are unused). The first fighter it meets is clinched and held
/// <see cref="AnchorDistance"/> in front of the holder. Stage 1 is the hold: its authored hits deal damage and hitstop
/// only to the held fighter, and on its tick <see cref="ReleaseTick"/> the pair ends with the victim stuck for
/// <see cref="ReleaseLockTicks"/>. A lunge that ends empty holds stage 0 for <see cref="WhiffRecoveryTicks"/>, then
/// the move ends. Ticks are 60 Hz, distances metres, speeds m/s.
/// </summary>
public sealed record ClinchCapabilityParameters(
    float LungeDistance, float LungeSpeed, HitboxSource CatchHitbox, float AnchorDistance,
    ushort WhiffRecoveryTicks, ushort ReleaseTick, ushort ReleaseLockTicks) : TypedCapabilityParameters;

public sealed record CookedClinchCapabilityParameters(
    float LungeDistance, float LungeSpeed, CookedHitbox CatchHitbox, float AnchorDistance,
    ushort WhiffRecoveryTicks, ushort ReleaseTick, ushort ReleaseLockTicks) : CookedCapabilityParameters
{
    public int TravelTicks => Math.Max(1, (int)Math.Ceiling(LungeDistance / (LungeSpeed * Simulation.TickDt) - 1e-4));
    /// <summary>The lunge stage's exact length: every travel tick carries a catch, then one tick decides the whiff.</summary>
    public int LungeStageTicks => TravelTicks + 1;
}

/// <summary>The clinch rules shared by the compiler and the cooked loader; each returns null when admissible.</summary>
internal static class ClinchContract
{
    internal static string? BoundsError(CookedClinchCapabilityParameters x)
    {
        var c = x.CatchHitbox;
        if (!Finite(x.LungeDistance) || !Finite(x.LungeSpeed) || !Finite(x.AnchorDistance) || !Finite(c.Radius)
            || !Finite(c.OffsetX) || !Finite(c.OffsetY) || !Finite(c.OffsetZ) || !Finite(c.Damage))
            return "Clinch values must be finite.";
        if (!(x.LungeDistance >= 0f && x.LungeDistance <= 8f) || !(x.LungeSpeed > 0f && x.LungeSpeed <= 60f))
            return "The lunge needs a distance of 0 to 8 and a speed above 0 and at most 60.";
        if (x.TravelTicks > 60) return "The lunge must take at most 60 ticks.";
        if (c.RequiresFacingOwner || c.Shape != AuthoringHitboxShape.Sphere || c.StartBoneId != null || c.EndBoneId != null || c.HitGroup != 0)
            return "The catch is a root-relative sphere with authored hitGroup 0.";
        if (!(c.Radius > 0f && c.Radius <= 3f) || c.Damage < 0f)
            return "The catch needs a radius above 0 and at most 3, and damage >= 0.";
        if (c.BaseKnockback != 0f || c.KnockbackGrowth != 0f || c.StunTicks != 0 || c.FixedHitstunTicks != 0)
            return "The catch holds instead of launching: no knockback and no stun.";
        if (x.LungeSpeed * Simulation.TickDt > 2f * c.Radius)
            return "The lunge's travel per tick exceeds twice the catch radius, leaving gaps in its reach.";
        if (!(x.AnchorDistance >= 0f && x.AnchorDistance <= 3f)) return "anchorDistance must be 0 to 3.";
        if (x.WhiffRecoveryTicks == 0 || x.WhiffRecoveryTicks > 120) return "whiffRecoveryTicks must be 1 to 120.";
        if (x.ReleaseTick == 0) return "releaseTick must be at least 1.";
        if (x.ReleaseLockTicks == 0 || x.ReleaseLockTicks > 60) return "releaseLockTicks must be 1 to 60.";
        return null;
    }

    /// <summary>
    /// One clinch at tick zero of a two-stage meleeCombo with no aim, carry or charge pool and never tucked. Stage 0
    /// is the lunge (exactly <see cref="CookedClinchCapabilityParameters.LungeStageTicks"/> long, IASA disabled,
    /// presentation only beside the clinch); stage 1 is the hold, which releases inside it and admits no IASA
    /// before the release.
    /// </summary>
    internal static string? SlotError(int count, int stages, ushort tick, AuthoringAbilityBehavior behavior,
        AuthoringAimMode aim, ShellPolicy shellPolicy, bool carry, bool charge, bool otherLungeOperations,
        ushort lungeDuration, ushort lungeIasa, ushort holdDuration, ushort holdIasa, CookedClinchCapabilityParameters p)
    {
        if (count != 1 || stages != 2 || tick != 0 || behavior != AuthoringAbilityBehavior.MeleeCombo
            || aim != AuthoringAimMode.None || carry || charge || otherLungeOperations || lungeIasa != 0)
            return "A clinch opens a two-stage meleeCombo with no aim, carry or charge pool: a lunge stage with only "
                + "presentation beside the tick-zero clinch and IASA disabled, then the hold stage.";
        if (shellPolicy == ShellPolicy.Tuck) return "A clinch cannot tuck.";
        if (lungeDuration != p.LungeStageTicks)
            return $"The lunge stage lasts the lunge's travel plus one catch tick: {p.LungeStageTicks} ticks.";
        if (p.ReleaseTick >= holdDuration) return "releaseTick must fall inside the hold stage.";
        if (holdIasa != 0 && holdIasa <= p.ReleaseTick) return "The hold stage cannot admit IASA before the release.";
        return null;
    }

    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
}
