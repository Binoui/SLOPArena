using System;

namespace SlopArena.Shared;

/// <summary>
/// slop.ability.command-grab.v1 (runtime API 1.18.0, ADR-0051, Nyx's Selfie Grab): a grab owned by a slot. At the press
/// she turns to her current target and lunges at <see cref="LungeSpeed"/> for <see cref="LungeTicks"/>, stopping once
/// that target is within <see cref="StopDistance"/>. At <see cref="CaptureTick"/> the authored <see cref="Capture"/>
/// volume (root-relative, no damage, knockback or stun; its durationTicks is the window) opens. The first fighter it
/// meets is held at her grab's victim anchor, she Attacking and the victim Grabbed in the command-hold phase: shields
/// do not stop it, armour does not resist it and a grab on the same tick clashes. At <see cref="ReleaseTick"/> the pair
/// ends and <see cref="Launch"/> lands on the victim as her hit. The stage's own length ends the move, hit or whiff.
/// Ticks are stage ticks at 60 Hz, distances metres, speeds m/s.
/// </summary>
public sealed record CommandGrabCapabilityParameters(
    ushort LungeTicks,
    float LungeSpeed,
    float StopDistance,
    ushort CaptureTick,
    HitboxSource Capture,
    ushort ReleaseTick,
    GrabLaunchSource Launch,
    string CatchPresentationId = "",
    string ReleasePresentationId = "") : TypedCapabilityParameters;

/// <summary>The command grab's release: an ordinary launch from the grabber, through shields.</summary>
public sealed record GrabLaunchSource(float Damage, float Angle, float BaseKnockback, float KnockbackGrowth, ushort StunTicks);

public sealed record CookedCommandGrabCapabilityParameters(
    ushort LungeTicks,
    float LungeSpeed,
    float StopDistance,
    ushort CaptureTick,
    CookedHitbox Capture,
    ushort ReleaseTick,
    CookedGrabLaunch Launch,
    string CatchPresentationId = "",
    string ReleasePresentationId = "") : CookedCapabilityParameters;

public sealed record CookedGrabLaunch(float Damage, float Angle, float BaseKnockback, float KnockbackGrowth, ushort StunTicks);

/// <summary>The command grab rules shared by the compiler and the cooked loader; each returns null when admissible.</summary>
internal static class CommandGrabContract
{
    internal static string? BoundsError(CookedCommandGrabCapabilityParameters x)
    {
        var c = x.Capture;
        var l = x.Launch;
        if (!Finite(x.LungeSpeed, x.StopDistance, c.Radius, c.OffsetX, c.OffsetY, c.OffsetZ, c.EndOffsetX, c.EndOffsetY,
                c.EndOffsetZ, c.Damage, l.Damage, l.Angle, l.BaseKnockback, l.KnockbackGrowth))
            return "Command grab values must be finite.";
        if (x.LungeTicks > 60 || !(x.LungeSpeed >= 0f && x.LungeSpeed <= 60f) || !(x.StopDistance >= 0f && x.StopDistance <= 5f))
            return "The lunge needs lungeTicks 0 to 60, a speed of 0 to 60 and a stopDistance of 0 to 5.";
        if (c.RequiresFacingOwner || c.StartBoneId != null || c.EndBoneId != null || c.HitGroup != 0
            || c.Shape is not (AuthoringHitboxShape.Sphere or AuthoringHitboxShape.Capsule))
            return "The capture is a root-relative sphere or capsule with authored hitGroup 0.";
        if (!(c.Radius > 0f && c.Radius <= 3f) || c.Damage != 0f || c.BaseKnockback != 0f || c.KnockbackGrowth != 0f
            || c.StunTicks != 0 || c.FixedHitstunTicks != 0)
            return "The capture holds instead of hitting: a radius above 0 and at most 3, no damage, knockback or stun.";
        if (x.CaptureTick == 0 || x.CaptureTick > 120 || c.DurationTicks == 0 || c.DurationTicks > 30)
            return "The capture opens at tick 1 to 120 for 1 to 30 ticks.";
        if (x.ReleaseTick < x.CaptureTick + c.DurationTicks || x.ReleaseTick > 240)
            return "releaseTick must follow the capture window and be at most 240.";
        if (!(l.Damage > 0f && l.Damage <= 100f) || l.Angle < -90f || l.Angle > 90f || l.BaseKnockback < 0f
            || l.KnockbackGrowth < 0f || l.StunTicks > 240)
            return "The launch needs damage above 0 and at most 100, an angle of -90 to 90, no negative knockback and stun to 240.";
        return null;
    }

    /// <summary>
    /// One command grab at tick zero of a single meleeCombo stage with no aim, carry or charge pool, beside presentation
    /// only and never tucked. The release falls inside the stage, and IASA, if any, comes after it.
    /// </summary>
    internal static string? SlotError(int count, int stages, ushort tick, AuthoringAbilityBehavior behavior,
        AuthoringAimMode aim, ShellPolicy shellPolicy, bool carry, bool charge, bool otherOperations, ushort duration,
        ushort iasa, CookedCommandGrabCapabilityParameters p)
    {
        if (count != 1 || stages != 1 || tick != 0 || behavior != AuthoringAbilityBehavior.MeleeCombo
            || aim != AuthoringAimMode.None || carry || charge || otherOperations)
            return "A command grab needs one tick-zero lifecycle in one meleeCombo stage with no aim, carry or charge pool, "
                + "and only presentation beside it.";
        if (shellPolicy == ShellPolicy.Tuck) return "A command grab cannot tuck.";
        if (p.ReleaseTick >= duration) return "releaseTick must fall inside the stage.";
        if (iasa != 0 && iasa <= p.ReleaseTick) return "The stage cannot admit IASA before the release.";
        return null;
    }

    private static bool Finite(params float[] values)
    {
        foreach (float value in values)
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
        return true;
    }
}
