using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SlopArena.Shared;

internal sealed record RuntimeFieldLimit(string SchemaId, string Name, double? GameplayMinimum, double? GameplayMaximum, bool ExclusiveGameplayMinimum = false)
{
    internal string Describe() => Name + ": "
        + (GameplayMinimum.HasValue ? (ExclusiveGameplayMinimum ? "greater than " : "at least ") + GameplayMinimum.Value : "no lower limit")
        + (GameplayMaximum.HasValue ? ", at most " + GameplayMaximum.Value : "");
    internal bool IsOutsideGameplay(double value) => GameplayMinimum.HasValue && (ExclusiveGameplayMinimum ? value <= GameplayMinimum.Value : value < GameplayMinimum.Value)
        || GameplayMaximum.HasValue && value > GameplayMaximum.Value;
    internal bool IsWithinGameplay(double value) => (!GameplayMinimum.HasValue || (ExclusiveGameplayMinimum ? value > GameplayMinimum.Value : value >= GameplayMinimum.Value))
        && (!GameplayMaximum.HasValue || value <= GameplayMaximum.Value);
}

internal sealed class RuntimeLimitSet
{
    private readonly IReadOnlyDictionary<string, RuntimeFieldLimit> _fields;
    internal RuntimeLimitSet(IEnumerable<RuntimeFieldLimit> fields) => _fields = new ReadOnlyDictionary<string, RuntimeFieldLimit>(fields.ToDictionary(x => x.Name, StringComparer.Ordinal));
    internal RuntimeFieldLimit Field(string name) => _fields[name];
    internal string Describe(params string[] names) => string.Join("; ", names.Select(name => Field(name).Describe()));
}

// These bounds also admit cooked packages. Authoring metadata is checked against them in tests.
internal static class CharacterRuntimeLimits
{
    internal const float TetherMaxProjectileSpeed = 60f;
    internal const float TetherMaxProjectileRadius = 3f;
    internal const ushort TetherMaxProjectileFlightTicks = 240;
    internal static readonly IReadOnlyList<RuntimeFieldLimit> Fields = Array.AsReadOnly(new[]
    {
        new RuntimeFieldLimit("cap.aimed-recovery.v1", "directionalVerticalSpeed", 0, null, true),
        new RuntimeFieldLimit("cap.aimed-recovery.v1", "horizontalSpeed", 0, 60, false),
        new RuntimeFieldLimit("cap.aimed-recovery.v1", "preparationRiseSpeed", 0, 4, false),
        new RuntimeFieldLimit("cap.aimed-recovery.v1", "riseSteerAcceleration", 0, 120, false),
        new RuntimeFieldLimit("cap.aimed-recovery.v1", "startupTicks", 1, 120, false),
        new RuntimeFieldLimit("cap.aimed-recovery.v1", "verticalSpeed", 0, 60, true),
        new RuntimeFieldLimit("cap.cannonball.v1", "maxFlightTicks", null, 240, false),
        new RuntimeFieldLimit("cap.cannonball.v1", "maxLaunchSpeed", 0, 60, true),
        new RuntimeFieldLimit("cap.cannonball.v1", "maxRange", null, 30, false),
        new RuntimeFieldLimit("cap.cannonball.v1", "minFlightTicks", 2, null, false),
        new RuntimeFieldLimit("cap.cannonball.v1", "noTargetDistance", 0, null, true),
        new RuntimeFieldLimit("cap.cannonball.v1", "takeoffTick", 0, 60, false),
        new RuntimeFieldLimit("cap.command-grab.v1", "anchorDistance", 0, 3, false),
        new RuntimeFieldLimit("cap.command-grab.v1", "captureTick", 1, 120, false),
        new RuntimeFieldLimit("cap.command-grab.v1", "lungeSpeed", 0, 60, false),
        new RuntimeFieldLimit("cap.command-grab.v1", "lungeTicks", 0, 60, false),
        new RuntimeFieldLimit("cap.command-grab.v1", "releaseLockTicks", 0, 60, false),
        new RuntimeFieldLimit("cap.command-grab.v1", "releaseTick", 0, 240, false),
        new RuntimeFieldLimit("cap.command-grab.v1", "stopDistance", 0, 5, false),
        new RuntimeFieldLimit("cap.command-grab.v1", "whiffRecoveryTicks", 0, 120, false),
        new RuntimeFieldLimit("cap.dash-strike.v1", "distance", 0, 12, true),
        new RuntimeFieldLimit("cap.dash-strike.v1", "recoveryTicks", 1, 120, false),
        new RuntimeFieldLimit("cap.dash-strike.v1", "speed", 0, 60, true),
        new RuntimeFieldLimit("cap.finger-guns.v1", "airMaxFallSpeed", 0, 30, false),
        new RuntimeFieldLimit("cap.finger-guns.v1", "airRecoilSpeed", 0, 30, false),
        new RuntimeFieldLimit("cap.finger-guns.v1", "fireTriggerTick", 1, 120, false),
        new RuntimeFieldLimit("cap.finger-guns.v1", "recoveryTicks", 1, 240, false),
        new RuntimeFieldLimit("cap.finger-guns.v1", "reloadTicks", 1, 600, false),
        new RuntimeFieldLimit("cap.finger-guns.v1", "shotsPerReload", 1, 24, false),
        new RuntimeFieldLimit("cap.flash-cut.v1", "legTicks", 1, 30, false),
        new RuntimeFieldLimit("cap.flash-cut.v1", "lockRange", 0, 20, true),
        new RuntimeFieldLimit("cap.flash-cut.v1", "maxTargets", 1, FlashCutContract.MaxTargetsLimit, false),
        new RuntimeFieldLimit("cap.flash-cut.v1", "passThroughDistance", 0, 3, false),
        new RuntimeFieldLimit("cap.flash-cut.v1", "recoveryTicks", 1, 120, false),
        new RuntimeFieldLimit("cap.flash-cut.v1", "sheatheTicks", 1, 120, false),
        new RuntimeFieldLimit("cap.flash-cut.v1", "stanceTicks", 1, 120, false),
        new RuntimeFieldLimit("cap.flash-cut.v1", "whiffTicks", 1, 120, false),
        new RuntimeFieldLimit("cap.steered-charge.v2", "challengedRearTicks", 1, 120, false),
        new RuntimeFieldLimit("cap.steered-charge.v2", "challengedTrackDegreesPerSecond", 0, 1440, false),
        new RuntimeFieldLimit("cap.steered-charge.v2", "trackDegreesPerSecond", 0, 1440, false),
        new RuntimeFieldLimit("cap.targeted-leap.v2", "dropTicks", 1, 240, false),
        new RuntimeFieldLimit("cap.targeted-leap.v2", "hangTicks", 1, 240, false),
        new RuntimeFieldLimit("cap.targeted-leap.v2", "maxFallTicks", 1, 600, false),
        new RuntimeFieldLimit("cap.targeted-leap.v2", "maxRange", 0, 60, true),
        new RuntimeFieldLimit("cap.targeted-leap.v2", "missRecoveryTicks", 0, 600, false),
        new RuntimeFieldLimit("cap.targeted-leap.v2", "riseHeight", 0, 30, true),
        new RuntimeFieldLimit("cap.targeted-leap.v2", "riseTicks", 1, 240, false),
        new RuntimeFieldLimit("cap.tether.v1", "airGravityScale", 0, 1, false),
        new RuntimeFieldLimit("cap.tether.v1", "anchorDistance", 0, 3, false),
        new RuntimeFieldLimit("cap.tether.v1", "arrivalStunTicks", 1, 240, false),
        new RuntimeFieldLimit("cap.tether.v1", "maxReelTicks", 1, 600, false),
        new RuntimeFieldLimit("cap.tether.v1", "missRecoveryTicks", 0, 240, false),
        new RuntimeFieldLimit("cap.tether.v1", "reelSpeed", 0, 60, true),
        new RuntimeFieldLimit("cap.tether.v1", "retractSpeed", 0, 60, true),
        new RuntimeFieldLimit("cap.tether.v1", "throwTick", 1, 120, false),
        new RuntimeFieldLimit("cap.thrust.v1", "acceleration", 0, 240, false),
        new RuntimeFieldLimit("cap.thrust.v1", "maxSpeed", null, 60, false),
        new RuntimeFieldLimit("cap.thrust.v1", "neutralPitchDegrees", -90, 90, false),
        new RuntimeFieldLimit("cap.thrust.v1", "speed", 0, null, true),
        new RuntimeFieldLimit("cap.thrust.v1", "stickPitchDegrees", -90, 90, false),
        new RuntimeFieldLimit("cap.thrust.v1", "targetConeDegrees", 0, 180, false),
        new RuntimeFieldLimit("cap.thrust.v1", "thrustTicks", 1, 600, false),
        new RuntimeFieldLimit("chargeAssist", "assistDegreesPerSecond", 0, 1440, true),
        new RuntimeFieldLimit("chargeAssist", "assistHalfAngleDegrees", 0, 90, true),
        new RuntimeFieldLimit("chargeAssist", "assistRange", 0, 10, true),
        new RuntimeFieldLimit("chargeCarry", "anchorDistance", 0, 3, false),
        new RuntimeFieldLimit("chargeCarry", "anchorHeight", 0, 3, false),
        new RuntimeFieldLimit("chargeCarry", "flingTicks", 1, 120, false),
        new RuntimeFieldLimit("chargeCarry", "maxCarryTicks", 1, 120, false),
        new RuntimeFieldLimit("launch", "angle", -90, 90, false),
        new RuntimeFieldLimit("launch", "baseKnockback", 0, null, false),
        new RuntimeFieldLimit("launch", "damage", 0, 100, true),
        new RuntimeFieldLimit("launch", "knockbackGrowth", 0, null, false),
        new RuntimeFieldLimit("launch", "stunTicks", 0, 240, false),
        new RuntimeFieldLimit("recoveryHop", "arcHeight", 0, 3, true),
        new RuntimeFieldLimit("recoveryHop", "forwardDistance", 0, 3, true),
        new RuntimeFieldLimit("recoveryHop", "landingHeight", 0, 1, false),
        new RuntimeFieldLimit("recoveryHop", "startTicks", 1, null, false),
        new RuntimeFieldLimit("ride", "speed", 0, 60, true),
        new RuntimeFieldLimit("ride", "ticks", 1, 600, false),
        new RuntimeFieldLimit("ride", "turnDegreesPerSecond", 0, 720, false),
        new RuntimeFieldLimit("shell", "blockedRecoilSpeed", 0, 30, false),
        new RuntimeFieldLimit("shell", "popOutDeceleration", 0, 600, false),
        new RuntimeFieldLimit("splash", "popUpTicks", 1, 240, false),
        new RuntimeFieldLimit("tetherAtTarget", "catchTicks", 1, 60, false),
        new RuntimeFieldLimit("tetherAtTarget", "damage", 0, 100, false),
        new RuntimeFieldLimit("tetherAtTarget", "height", null, 6, false),
        new RuntimeFieldLimit("tetherAtTarget", "maxRange", 0, 32, true),
        new RuntimeFieldLimit("tetherAtTarget", "noTargetDistance", 0, null, false),
        new RuntimeFieldLimit("tetherAtTarget", "radius", 0, 3, true),
        new RuntimeFieldLimit("tetherAtTarget", "warningTicks", 1, 120, false),
        new RuntimeFieldLimit("homing", "turnDegreesPerTick", 0, ProjectilePerchContract.MaxTurnDegreesPerTick, true),
        new RuntimeFieldLimit("perch", "perchTicks", 1, ProjectilePerchContract.MaxPerchTicks, false),
        new RuntimeFieldLimit("pop", "damage", 0, 100, true),
        new RuntimeFieldLimit("pop", "angle", -90, 90, false),
        new RuntimeFieldLimit("pop", "baseKnockback", 0, null, false),
        new RuntimeFieldLimit("pop", "knockbackGrowth", 0, null, false),
        new RuntimeFieldLimit("pop", "stunTicks", 0, ProjectilePerchContract.MaxPopStunTicks, false),
        new RuntimeFieldLimit("peck", "intervalTicks", 1, null, false),
        new RuntimeFieldLimit("peck", "damage", 1, ProjectilePerchContract.MaxPeckDamage, false),
        new RuntimeFieldLimit("dive", "hoverHeight", 0, ProjectilePerchContract.MaxHoverHeight, true),
        new RuntimeFieldLimit("dive", "maxRiseTicks", 1, ProjectilePerchContract.MaxDivePhaseTicks, false),
        new RuntimeFieldLimit("dive", "markTicks", 1, ProjectilePerchContract.MaxDivePhaseTicks, false),
        new RuntimeFieldLimit("dive", "diveSpeed", 0, ProjectilePerchContract.MaxDiveSpeed, true),
        new RuntimeFieldLimit("dive", "noTargetDistance", 0, ProjectilePerchContract.MaxNoTargetDistance, false),
        new RuntimeFieldLimit("legHit", "damage", 0, 100, false),
        new RuntimeFieldLimit("legHit", "angle", -90, 90, false),
        new RuntimeFieldLimit("legHit", "baseKnockback", 0, null, false),
        new RuntimeFieldLimit("legHit", "knockbackGrowth", 0, null, false),
        new RuntimeFieldLimit("legHit", "stunTicks", 0, 240, false),
        new RuntimeFieldLimit("legHit", "fixedHitstunTicks", 0, 240, false),
        new RuntimeFieldLimit("command-grab capture", "durationTicks", 1, CommandGrabContract.MaxCaptureDurationTicks),
        new RuntimeFieldLimit("command-grab capture", "radius", 0, CommandGrabContract.MaxCaptureRadius, true),
        new RuntimeFieldLimit("command-grab capture", "baseKnockback", 0, 0),
        new RuntimeFieldLimit("command-grab capture", "knockbackGrowth", 0, 0),
        new RuntimeFieldLimit("command-grab capture", "stunTicks", 0, 0),
        new RuntimeFieldLimit("command-grab capture", "fixedHitstunTicks", 0, 0),
        new RuntimeFieldLimit("thrown tether", "speed", 0, TetherMaxProjectileSpeed, true),
        new RuntimeFieldLimit("thrown tether", "radius", 0, TetherMaxProjectileRadius, true),
        new RuntimeFieldLimit("thrown tether", "maxFlightTicks", 1, TetherMaxProjectileFlightTicks),
        new RuntimeFieldLimit("diving perch", "speed", 0, ProjectilePerchContract.MaxDiveSpeed, true),
        new RuntimeFieldLimit("tetherUnmarked", "riseTick", 1, 120, false),
        new RuntimeFieldLimit("tetherUnmarked", "activeTicks", 1, 60, false),
        new RuntimeFieldLimit("tetherUnmarked", "distance", 0, 6, false),
        new RuntimeFieldLimit("tetherUnmarked", "radius", 0, 3, true),
        new RuntimeFieldLimit("tetherUnmarked", "height", null, 6, false),
        new RuntimeFieldLimit("tetherUnmarked", "damage", 0, 100, true),
        new RuntimeFieldLimit("tetherUnmarked", "angle", -90, 90, false),
        new RuntimeFieldLimit("tetherUnmarked", "baseKnockback", 0, null, false),
        new RuntimeFieldLimit("tetherUnmarked", "knockbackGrowth", 0, null, false),
        new RuntimeFieldLimit("tetherUnmarked", "stunTicks", 0, 240, false),
        new RuntimeFieldLimit("tetherUnmarked", "recoveryTicks", 0, 240, false),
    });
    private static readonly IReadOnlyDictionary<string, RuntimeLimitSet> Sets = new ReadOnlyDictionary<string, RuntimeLimitSet>(
        Fields.GroupBy(x => x.SchemaId).ToDictionary(x => x.Key, x => new RuntimeLimitSet(x), StringComparer.Ordinal));
    internal static RuntimeLimitSet Object(string id) => Sets[id];
}
