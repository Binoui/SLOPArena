using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared;

public sealed record FlashCutCapabilityParameters(
    ushort StanceTicks, float LockRange, ushort MaxTargets, ushort LegTicks, float PassThroughDistance,
    ushort SheatheTicks, ushort RevealLeadTicks, ushort RecoveryTicks, ushort WhiffTicks, HitboxSource CutHitbox,
    string StanceAnimationId, string LegAnimationId, string SheatheAnimationId,
    string LockPresentationId, string RevealPresentationId, string SheathePresentationId, string PopPresentationId)
    : TypedCapabilityParameters;

public sealed record CookedFlashCutCapabilityParameters(
    ushort StanceTicks, float LockRange, ushort MaxTargets, ushort LegTicks, float PassThroughDistance,
    ushort SheatheTicks, ushort RevealLeadTicks, ushort RecoveryTicks, ushort WhiffTicks, CookedHitbox CutHitbox,
    string StanceAnimationId, string LegAnimationId, string SheatheAnimationId,
    string LockPresentationId, string RevealPresentationId, string SheathePresentationId, string PopPresentationId)
    : CookedCapabilityParameters
{
    /// <summary>Ticks from the lock to the click with this many victims locked. Nothing moves it afterwards.</summary>
    public int PopAfterLock(int victims) => victims * LegTicks + SheatheTicks;

    /// <summary>The longest path from the press: the stage watchdog must cover it.</summary>
    public int LongestTicks => StanceTicks + Math.Max(PopAfterLock(MaxTargets) + RecoveryTicks, WhiffTicks);

    /// <summary>
    /// The furthest one leg can need to go: between two victims on opposite sides of the lock sphere, plus the
    /// pass-through. The runtime never steps further than twice the cut radius in one tick.
    /// </summary>
    public float LongestLeg => 2f * LockRange + PassThroughDistance;
}

internal static class FlashCutContract
{
    internal const int MaxTargetsLimit = 7;
    /// <summary>A held victim's freeze is its hitstop; keep it within the fixed-hitstun ceiling.</summary>
    internal const int MaxHoldTicks = 240;

    internal static string? BoundsError(CookedFlashCutCapabilityParameters x)
    {
        if (x.StanceTicks is 0 or > 120 || x.LegTicks is 0 or > 30 || x.SheatheTicks is 0 or > 120 ||
            x.RecoveryTicks is 0 or > 120 || x.WhiffTicks is 0 or > 120 ||
            x.RevealLeadTicks == 0 || x.RevealLeadTicks > x.SheatheTicks)
            return "Flash cut requires stance, sheathe, recovery and whiff of 1..120 ticks, legs of 1..30, and a reveal lead of 1..sheatheTicks.";
        if (!Finite(x.LockRange) || x.LockRange <= 0 || x.LockRange > 20 ||
            !Finite(x.PassThroughDistance) || x.PassThroughDistance < 0 || x.PassThroughDistance > 3 ||
            x.MaxTargets is 0 or > MaxTargetsLimit)
            return "Flash cut requires a lock range in (0, 20] m, a pass-through in [0, 3] m and 1..7 targets.";
        if (x.PopAfterLock(x.MaxTargets) > MaxHoldTicks)
            return "Flash cut legs and sheathe hold their victims too long: maxTargets × legTicks + sheatheTicks must be at most 240.";
        var cut = x.CutHitbox;
        if (cut.Shape != AuthoringHitboxShape.Sphere || cut.StartBoneId != null || cut.EndBoneId != null || cut.HitGroup != 0)
            return "The flash cut requires a root-relative sphere with authored hitGroup 0.";
        if (!(cut.Damage > 0) || cut.StunTicks == 0 || !(cut.BaseKnockback > 0 || cut.KnockbackGrowth > 0))
            return "The flash cut needs damage, stun and a launch: its knockback is the pop.";
        if (x.LongestLeg / x.LegTicks > 2 * cut.Radius)
            return "Flash cut legs can travel twice the lock range plus the pass-through; that per-tick travel must not exceed twice the cut radius, or cuts leave gaps.";
        return null;
    }

    internal static string? SlotError(int count, int stages, ushort tick, AuthoringAbilityBehavior behavior,
        AuthoringAimMode aim, ushort iasa, bool extra, bool carry, bool charge, ushort duration,
        IReadOnlyCollection<string> clips, CookedFlashCutCapabilityParameters p)
        => count != 1 || stages != 1 || tick != 0 || behavior != AuthoringAbilityBehavior.MeleeCombo ||
           aim != AuthoringAimMode.None || iasa != 0 || extra || carry || charge
            ? "Flash cut requires one tick-zero meleeCombo lifecycle, no aim, IASA, carry or charge pool, and only presentation alongside it."
            : p.LongestTicks > duration
                ? "Flash cut stance, legs, sheathe and recovery, and its whiff, must fit the stage watchdog."
                : !clips.Contains(p.StanceAnimationId) || !clips.Contains(p.LegAnimationId) ||
                  p.SheatheAnimationId.Length > 0 && !clips.Contains(p.SheatheAnimationId)
                    ? "Flash cut stance, leg and sheathe clips must be listed in its stage."
                    : null;

    internal static IEnumerable<(string Id, string Field)> PresentationIds(CookedFlashCutCapabilityParameters p)
    {
        yield return (p.LockPresentationId, "lockPresentationId");
        yield return (p.RevealPresentationId, "revealPresentationId");
        yield return (p.SheathePresentationId, "sheathePresentationId");
        yield return (p.PopPresentationId, "popPresentationId");
    }

    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
}
