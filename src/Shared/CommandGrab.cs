using System;

namespace SlopArena.Shared;

/// <summary>
/// slop.ability.command-grab.v1 (ADR-0057; Nyx's Selfie Grab and FightGuy's Title Match): a grab owned by a slot. At the
/// press the fighter turns to its current target and lunges at <see cref="LungeSpeed"/> for <see cref="LungeTicks"/>,
/// stopping once that target is within <see cref="StopDistance"/> (0 never stops). A <see cref="Level"/> lunge keeps its
/// height with gravity off, and a wall that stops it ends the grab empty. From <see cref="CaptureTick"/> the authored
/// <see cref="Capture"/> volume (root-relative, no knockback or stun; its durationTicks is the window) is placed afresh
/// each tick. The first fighter it meets is held, the grabber Attacking and the victim CommandGrabbed: shields do not
/// stop it, armour does not resist it and a grab on the same tick clashes. A capture with damage credits it as a hit.
/// The victim is held <see cref="AnchorDistance"/> in front of the grabber, facing it, or at the grabber's grab anchor
/// when that is null. The grabber's own hits on its held victim deal damage and hitstop only. At
/// <see cref="ReleaseTick"/> the pair ends: <see cref="Launch"/> lands on the victim as the grabber's hit, or without one
/// the victim stays held for <see cref="ReleaseLockTicks"/> so the timeline's finisher meets it.
/// With <see cref="WhiffRecoveryTicks"/> the move's second stage is a hold stage that never plays empty: a whiff holds the
/// lunge stage that long and ends the move, and a lost pair ends it at once. Without, the stage's own length ends the
/// move, hit or whiff. Ticks count from the press at 60 Hz, distances are metres, speeds m/s.
/// </summary>
public sealed record CommandGrabCapabilityParameters(
    ushort LungeTicks,
    float LungeSpeed,
    float StopDistance,
    ushort CaptureTick,
    HitboxSource Capture,
    ushort ReleaseTick,
    GrabLaunchSource? Launch,
    string CatchPresentationId = "",
    string ReleasePresentationId = "",
    bool Level = false,
    float? AnchorDistance = null,
    ushort ReleaseLockTicks = 0,
    ushort WhiffRecoveryTicks = 0) : TypedCapabilityParameters;

/// <summary>The command grab's release: an ordinary launch from the grabber, through shields.</summary>
public sealed record GrabLaunchSource(float Damage, float Angle, float BaseKnockback, float KnockbackGrowth, ushort StunTicks);

public sealed record CookedCommandGrabCapabilityParameters(
    ushort LungeTicks,
    float LungeSpeed,
    float StopDistance,
    ushort CaptureTick,
    CookedHitbox Capture,
    ushort ReleaseTick,
    CookedGrabLaunch? Launch,
    string CatchPresentationId = "",
    string ReleasePresentationId = "",
    bool Level = false,
    float? AnchorDistance = null,
    ushort ReleaseLockTicks = 0,
    ushort WhiffRecoveryTicks = 0) : CookedCapabilityParameters
{
    /// <summary>The move's second stage is the hold stage: a whiff or a lost pair ends the move instead of playing it.</summary>
    public bool HasHoldStage => WhiffRecoveryTicks > 0;
    /// <summary>Uses a field runtime API 1.20.0 added (ADR-0057): a level lunge, a hold stage, a lock release or a credited catch.</summary>
    internal bool NeedsRuntimeApi120 => Level || AnchorDistance != null || Launch == null || HasHoldStage || Capture.Damage > 0f;
}

public sealed record CookedGrabLaunch(float Damage, float Angle, float BaseKnockback, float KnockbackGrowth, ushort StunTicks);

/// <summary>The command grab rules shared by the compiler and the cooked loader; each returns null when admissible.</summary>
internal static class CommandGrabContract
{
    internal static string? BoundsError(CookedCommandGrabCapabilityParameters x)
    {
        var c = x.Capture;
        if (!Finite(x.LungeSpeed, x.StopDistance, x.AnchorDistance ?? 0f, c.Radius, c.OffsetX, c.OffsetY, c.OffsetZ,
                c.EndOffsetX, c.EndOffsetY, c.EndOffsetZ, c.Damage)
            || x.Launch is { } launch && !Finite(launch.Damage, launch.Angle, launch.BaseKnockback, launch.KnockbackGrowth))
            return "Command grab values must be finite.";
        if (x.LungeTicks > 60 || !(x.LungeSpeed >= 0f && x.LungeSpeed <= 60f) || !(x.StopDistance >= 0f && x.StopDistance <= 5f))
            return "The lunge needs lungeTicks 0 to 60, a speed of 0 to 60 and a stopDistance of 0 to 5.";
        if (c.RequiresFacingOwner || c.StartBoneId != null || c.EndBoneId != null || c.HitGroup != 0
            || c.Shape is not (AuthoringHitboxShape.Sphere or AuthoringHitboxShape.Capsule))
            return "The capture is a root-relative sphere or capsule with authored hitGroup 0.";
        if (!(c.Radius > 0f && c.Radius <= 3f) || c.Damage < 0f || c.BaseKnockback != 0f || c.KnockbackGrowth != 0f
            || c.StunTicks != 0 || c.FixedHitstunTicks != 0)
            return "The capture holds instead of launching: a radius above 0 and at most 3, damage >= 0, no knockback or stun.";
        if (x.LungeSpeed * Simulation.TickDt > 2f * c.Radius)
            return "The lunge's travel per tick exceeds twice the capture radius, leaving gaps in its reach.";
        if (x.CaptureTick == 0 || x.CaptureTick > 120 || c.DurationTicks == 0 || c.DurationTicks > 30)
            return "The capture opens at tick 1 to 120 for 1 to 30 ticks.";
        if (x.ReleaseTick < x.CaptureTick + c.DurationTicks || x.ReleaseTick > 240)
            return "releaseTick must follow the capture window and be at most 240.";
        if (x.AnchorDistance is { } anchor && !(anchor >= 0f && anchor <= 3f)) return "anchorDistance must be 0 to 3.";
        if (x.Launch is { } l)
        {
            if (!(l.Damage > 0f && l.Damage <= 100f) || l.Angle < -90f || l.Angle > 90f || l.BaseKnockback < 0f
                || l.KnockbackGrowth < 0f || l.StunTicks > 240)
                return "The launch needs damage above 0 and at most 100, an angle of -90 to 90, no negative knockback and stun to 240.";
            if (x.ReleaseLockTicks != 0) return "A release with a launch has no releaseLockTicks.";
        }
        else if (x.ReleaseLockTicks == 0 || x.ReleaseLockTicks > 60)
            return "A release without a launch holds the victim for releaseLockTicks 1 to 60 before the timeline's finisher.";
        if (x.WhiffRecoveryTicks > 120) return "whiffRecoveryTicks must be at most 120.";
        return null;
    }

    /// <summary>
    /// One command grab at tick zero of a meleeCombo with no aim, carry or charge pool, beside presentation only in its
    /// stage and never tucked. Without a hold stage the slot has one stage holding the release, and IASA, if any, comes
    /// after it. With one, stage 0 is the lunge (it ends as the capture closes, IASA disabled) and stage 1 the hold stage,
    /// which holds the release and admits no IASA before it.
    /// </summary>
    internal static string? SlotError(int count, int stages, ushort tick, AuthoringAbilityBehavior behavior,
        AuthoringAimMode aim, ShellPolicy shellPolicy, bool carry, bool charge, bool otherOperations,
        ushort firstDuration, ushort firstIasa, ushort holdDuration, ushort holdIasa, CookedCommandGrabCapabilityParameters p)
    {
        if (count != 1 || stages != (p.HasHoldStage ? 2 : 1) || tick != 0 || behavior != AuthoringAbilityBehavior.MeleeCombo
            || aim != AuthoringAimMode.None || carry || charge || otherOperations)
            return "A command grab needs one tick-zero lifecycle in a meleeCombo with no aim, carry or charge pool and only "
                + "presentation beside it: one stage, or with whiffRecoveryTicks a lunge stage and then the hold stage.";
        if (shellPolicy == ShellPolicy.Tuck) return "A command grab cannot tuck.";
        if (!p.HasHoldStage)
        {
            if (p.ReleaseTick >= firstDuration) return "releaseTick must fall inside the stage.";
            if (firstIasa != 0 && firstIasa <= p.ReleaseTick) return "The stage cannot admit IASA before the release.";
            return null;
        }
        int lungeTicks = p.CaptureTick + p.Capture.DurationTicks;
        if (firstDuration != lungeTicks || firstIasa != 0)
            return $"The lunge stage ends as the capture closes, after {lungeTicks} ticks, with IASA disabled.";
        if (p.ReleaseTick <= firstDuration || p.ReleaseTick >= firstDuration + holdDuration)
            return "releaseTick must fall inside the hold stage.";
        if (holdIasa != 0 && holdIasa <= p.ReleaseTick - firstDuration) return "The hold stage cannot admit IASA before the release.";
        return null;
    }

    private static bool Finite(params float[] values)
    {
        foreach (float value in values)
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
        return true;
    }
}
