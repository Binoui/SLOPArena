using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>Gameplay comparison policy, separate from exact cooked provenance.</summary>
public static class ArcadeRecordCompatibility
{
    // Bump for Shared primitive/capability/rule changes that alter Arcade comparability.
    // Data changes below are detected automatically; UI, art, audio and VFX are excluded.
    // 4: explosions hit everything in their radius, not only the first entity (2026-10-18).
    // 5: melee hitboxes keep their active ticks through the owner's hitstop (2026-10-18).
    // 6: the air dodge follows the stick, front-loads its burst and glides out (2026-10-18).
    // 7: Slopball hits always pop over the net, and rally heat mixes zingers, moonballs and curveballs in (2026-10-18).
    // 8: Slopball passes through passive fighter bodies; only attacks redirect it (2026-10-19).
    // 9: Slopball keeps each hit's launch direction and angle without automatic return assistance (2026-10-19).
    // 10: Wibou's Rising Slash no longer refunds its charge on a hit (2026-10-20).
    // 11: universal Progressive ground movement (2026-10-27).
    public const uint RulesRevision = 11;
    public const ushort SlopballCountdownTicks = 180;
    public const ushort CourseCountdownTicks = 180;
    // 2: melee hitboxes keep their active ticks through the owner's hitstop (2026-10-18).
    // 3: the air dodge follows the stick, front-loads its burst and glides out (2026-10-18).
    // 4: Wibou's Rising Slash no longer refunds its charge on a hit (2026-10-20).
    // 5: universal Progressive ground movement (2026-10-27).
    public const uint CourseRulesRevision = 5;

    public static string Slopball(in ArenaDefinition arena, MatchContentEntry fighter,
        uint rulesRevision = RulesRevision, CombatTuning? tuning = null)
        => Fingerprint(arena, fighter, rulesRevision, tuning, course: false);

    public static string Course(in ArenaDefinition arena, MatchContentEntry fighter,
        uint rulesRevision = CourseRulesRevision, CombatTuning? tuning = null)
        => Fingerprint(arena, fighter, rulesRevision, tuning, course: true);

    private static string Fingerprint(in ArenaDefinition arena, MatchContentEntry fighter,
        uint rulesRevision, CombatTuning? tuning, bool course)
    {
        var stage = arena.Arcade ?? throw new ArgumentException("An Arcade stage is required.");
        if ((course ? stage.Course == null : stage.Slopball == null) || !stage.AllowsPackage(fighter.Identity.PackageId))
            throw new ArgumentException("This Arcade profile does not admit the fighter.");
        var c = fighter.CookedCharacterPackage?.Definition
            ?? throw new ArgumentException("Records require a cooked fighter.");
        var d = fighter.Definition;
        var baked = fighter.BakedAnimation;
        // The admitted SKEL payload is Shared's deterministic collision pose data,
        // already separate from Unity's rig, clip and presentation bindings. Use its
        // actual channel names rather than treating semantic attachment IDs as names.
        var bones = baked?.BoneNames.OrderBy(x => x, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        object? Pose(string? name)
        {
            if (baked == null) return null;
            int index = string.IsNullOrEmpty(name) ? -1 : baked.FindAnimIndex(name!);
            if (index < 0) index = baked.FindAnimIndex("idle");
            if (index < 0) return null;
            var animation = baked.Animations[index];
            var frames = animation.Frames.Select(frame => bones.SelectMany(bone =>
            {
                int boneIndex = baked.FindBoneIndex(bone);
                return new[] { frame[boneIndex * 3], frame[boneIndex * 3 + 1], frame[boneIndex * 3 + 2] };
            }).ToArray()).ToArray();
            return new { animation.FrameCount, Frames = frames };
        }
        tuning ??= CombatTuning.Default;
        // Retain the original Slopball projection bytes: adding Course must not archive ball records.
        var gameplay = new
        {
            ProjectionVersion = 1, RulesRevision = rulesRevision, Simulation.TickDt,
            Combat = new { tuning.KnockbackScale, tuning.HitstunCoefficient, tuning.HitstunMagnitudeBonus },
            Stage = course ? (object)new
            {
                stage.ProfileId, stage.GameplayRevision, stage.Course, CountdownTicks = CourseCountdownTicks,
                arena.KillHeight, arena.KillTop, arena.KillMinX, arena.KillMaxX, arena.KillMinZ, arena.KillMaxZ,
                arena.MinX, arena.MaxX, arena.MinZ, arena.MaxZ,
                Starts = arena.SpawnPoints.Select(x => new { x.X, x.Y, x.Z, x.Yaw }).ToArray(),
                Collision = arena.CollisionTriangles.Select(x => new
                    { x.AX, x.AY, x.AZ, x.BX, x.BY, x.BZ, x.CX, x.CY, x.CZ }).ToArray(),
                Ground = new { arena.Heightmap.Width, arena.Heightmap.Height, arena.Heightmap.CellSize,
                    arena.Heightmap.OriginX, arena.Heightmap.OriginZ, arena.Heightmap.Data },
            } : new
            {
                stage.ProfileId, stage.GameplayRevision, Ball = stage.Ball, ShotProfile = stage.Slopball,
                arena.KillHeight, arena.KillTop, arena.KillMinX, arena.KillMaxX, arena.KillMinZ, arena.KillMaxZ,
                arena.MinX, arena.MaxX, arena.MinZ, arena.MaxZ,
                Starts = arena.SpawnPoints.Select(x => new { x.X, x.Y, x.Z, x.Yaw }).ToArray(),
                Collision = arena.CollisionTriangles.Select(x => new
                    { x.AX, x.AY, x.AZ, x.BX, x.BY, x.BZ, x.CX, x.CY, x.CZ }).ToArray(),
                Ground = new { arena.Heightmap.Width, arena.Heightmap.Height, arena.Heightmap.CellSize,
                    arena.Heightmap.OriginX, arena.Heightmap.OriginZ, arena.Heightmap.Data },
            },
            Fighter = new
            {
                fighter.Identity.PackageId, c.Weight, c.Movement, c.CapsuleRadius, c.CapsuleHeight,
                c.HipHeight, c.HurtboxRadius, c.ShieldRadius, c.CaptureGeometry,
                c.HurtboxCapsules, c.HurtboxBoneDefs, d.HurtboxBoneScale,
                Capabilities = c.CapabilityRequirements.OrderBy(x => x.CapabilityId, StringComparer.Ordinal).ToArray(),
                Bones = bones,
                LowPosture = new { Crouch = baked != null && !string.IsNullOrEmpty(d.CrouchAnim) && baked.FindAnimIndex(d.CrouchAnim) >= 0,
                    Slide = baked != null && !string.IsNullOrEmpty(d.SlideAnim) && baked.FindAnimIndex(d.SlideAnim) >= 0 },
                Locomotion = new[] { Pose(d.IdleAnim), Pose(d.RunAnim), Pose(d.DashAnim), Pose(d.JumpAnim),
                    Pose(d.FallAnim), Pose(d.HitSmallAnim), Pose(d.HitMediumAnim), Pose(d.HitHardAnim),
                    Pose(d.CrouchAnim), Pose(d.SlideAnim), Pose(d.AirDodgeAnim) },
                Slots = c.Slots.Select(slot => new
                {
                    slot.Ordinal, slot.IsAir, slot.Behavior, slot.AimMode, slot.CooldownTicks,
                    slot.IsRecoveryMove, slot.PreserveMomentumOnStart, slot.AllowSlideCarry,
                    slot.ChargePool, slot.AimMovement, slot.AimMovementSpeedScale,
                    Stages = slot.Timeline.Stages.Select(s => new
                    {
                        s.DurationTicks, s.IasaTicks, s.LandingLagTicks, s.AutoCancelBeforeTicks,
                        s.AutoCancelAfterTicks, s.AttackRange, s.UseTargetLock, s.RotateTowardTarget, s.TrackingStrength,
                        Poses = s.AnimationIds.Select(Pose).ToArray(),
                        Operations = s.Operations.Where(op => op.Kind != CookedOperationKind.EmitPresentation)
                            .Select(Operation).ToArray(),
                    }).ToArray(),
                }).ToArray(),
            },
        };
        using var sha = SHA256.Create();
        // ArcadePoint uses readonly fields. Other projection records expose properties.
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(gameplay, new JsonSerializerOptions { IncludeFields = true });
        // Bonus targets' hit counts join the projection only when a course has them, so courses of
        // single-hit targets keep the bytes (and the records) they had before hit counts existed.
        if (course && stage.Course!.HasMultiHitTargets)
            bytes = bytes.Concat(JsonSerializer.SerializeToUtf8Bytes(
                new { TargetHits = stage.Course.Targets.Select(t => (int)t.Hits).ToArray() })).ToArray();
        // The shell stance (ADR-0045) joins only for fighters that have one, so other fighters keep their records.
        if (c.Shell != null || c.Slots.Any(slot => slot.ShellPolicy != ShellPolicy.PopOut))
            bytes = bytes.Concat(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Shell = c.Shell == null ? null : c.Shell with { HitPresentationId = null },
                ShellPose = Pose(d.ShellAnim),
                Tucks = c.Slots.Where(slot => slot.ShellPolicy != ShellPolicy.PopOut).Select(slot => slot.Ordinal).ToArray(),
            }, new JsonSerializerOptions { IncludeFields = true })).ToArray();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static object Operation(CookedTimelineOperation op)
    {
        object? data = op switch
        {
            CookedSetVelocityOperation v => new { v.VelocityMode, v.X, v.Y, v.Z },
            CookedForwardLungeOperation l => new { l.Speed, l.DurationTicks, l.StopInAttackRange },
            CookedGravityWindowOperation g => new { g.GravityScale, g.DurationTicks },
            CookedArmorWindowOperation a => new { a.DurationTicks },
            CookedStartupAimCorrectionOperation a => new { a.EndTick, a.AcquisitionRange,
                a.AcquisitionHalfAngleDegrees, a.MaxYawDegrees, a.MaxPitchDegrees,
                a.YawDegreesPerSecond, a.PitchDegreesPerSecond },
            CookedSpawnHitboxOperation h => h.Hitbox,
            CookedSpawnProjectileOperation p => Projectile(p.Projectile),
            CookedSetAimStateOperation a => new { a.AimState },
            CookedStartCapabilityOperation c => new { c.CapabilityId, c.CapabilityVersion,
                Parameters = Parameters(c.Parameters) },
            CookedCompleteTimelineOperation => null,
            _ => throw new ArgumentException("Update the Arcade comparison policy for this operation."),
        };
        return new { op.Tick, op.Unit, op.Kind, Data = data };
    }

    /// <summary>
    /// A projectile as compared before returning projectiles existed: the same properties in the same
    /// order, so content without a return keeps its fingerprint. A return block joins it, minus its
    /// presentation IDs.
    /// </summary>
    private static object Projectile(CookedProjectile x) => x.Return == null
        ? new
        {
            x.LaunchOffsetX, x.LaunchOffsetY, x.LaunchOffsetZ, x.Speed, x.Gravity, x.Radius, x.Damage, x.Angle,
            x.BaseKnockback, x.KnockbackGrowth, x.StunTicks, x.MaxFlightTicks, x.YawOffsetDegrees,
        }
        : new
        {
            x.LaunchOffsetX, x.LaunchOffsetY, x.LaunchOffsetZ, x.Speed, x.Gravity, x.Radius, x.Damage, x.Angle,
            x.BaseKnockback, x.KnockbackGrowth, x.StunTicks, x.MaxFlightTicks, x.YawOffsetDegrees,
            Return = x.Return with { CatchPresentationId = "", SelfHitPresentationId = "" },
        };

    private static object Parameters(CookedCapabilityParameters p) => p switch
    {
        CookedTargetedLeapCapabilityParameters x => x with { LaunchPresentationId = "", LandingPresentationId = "" },
        CookedSteeredChargeCapabilityParameters x => x with
        {
            GallopPresentationId = "", HitEndingPresentationId = "",
            MissEndingPresentationId = "", AirEndingPresentationId = "",
        },
        CookedMankiRoundBombCapabilityParameters x => x with { ExplosionPresentationId = "" },
        CookedMankiJetpackBoostCapabilityParameters x => x with { ExplosionPresentationId = "" },
        CookedMankiBazookaCapabilityParameters x => x with { ExplosionPresentationId = "" },
        CookedChargedProjectileCapabilityParameters x => new
        {
            x.MaxChargeTicks, x.Tier2Ticks, x.Tier3Ticks, x.FireTriggerTick, x.Tier2Radius, x.Tier3Radius,
            Projectile = Projectile(x.Projectile),
        },
        CookedFingerGunsCapabilityParameters x => new
        {
            x.FireTriggerTick, x.RecoveryTicks, x.ShotsPerReload, x.ReloadTicks,
            x.AirMaxFallSpeed, x.AirRecoilSpeed, Projectile = Projectile(x.Projectile),
        },
        CookedAimedRecoveryCapabilityParameters or CookedThrustCapabilityParameters
            or CookedRisingDragonCapabilityParameters or CookedChargedStreamCapabilityParameters
            or CookedCycloneKickCapabilityParameters or CookedChargedDirectionalDashCapabilityParameters
            or CookedWibouRisingSlashCapabilityParameters or CookedWibouBladeFlurryCapabilityParameters
            or CookedMankiAerosolInfernoCapabilityParameters => p,
        _ => throw new ArgumentException("Update the Arcade comparison policy for this capability."),
    };
}
