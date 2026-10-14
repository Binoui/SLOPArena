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
    public const uint RulesRevision = 1;
    public const ushort SlopballCountdownTicks = 180;

    public static string Slopball(in ArenaDefinition arena, MatchContentEntry fighter,
        uint rulesRevision = RulesRevision, CombatTuning? tuning = null)
    {
        var stage = arena.Arcade ?? throw new ArgumentException("An Arcade stage is required.");
        if (stage.Slopball == null || !stage.AllowsPackage(fighter.Identity.PackageId))
            throw new ArgumentException("This Slopball profile does not admit the fighter.");
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
                int boneIndex = Array.IndexOf(baked.BoneNames, bone);
                return new[] { frame[boneIndex * 3], frame[boneIndex * 3 + 1], frame[boneIndex * 3 + 2] };
            }).ToArray()).ToArray();
            return new { animation.FrameCount, Frames = frames };
        }
        tuning ??= CombatTuning.Default;
        var gameplay = new
        {
            ProjectionVersion = 1, RulesRevision = rulesRevision, Simulation.TickDt,
            Combat = new { tuning.KnockbackScale, tuning.HitstunCoefficient, tuning.HitstunMagnitudeBonus },
            Stage = new
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
                    slot.ChargePool, slot.AimMovement,
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
            CookedSpawnProjectileOperation p => p.Projectile,
            CookedSetAimStateOperation a => new { a.AimState },
            CookedStartCapabilityOperation c => new { c.CapabilityId, c.CapabilityVersion,
                Parameters = Parameters(c.Parameters) },
            CookedCompleteTimelineOperation => null,
            _ => throw new ArgumentException("Update the Arcade comparison policy for this operation."),
        };
        return new { op.Tick, op.Unit, op.Kind, Data = data };
    }

    private static object Parameters(CookedCapabilityParameters p) => p switch
    {
        CookedTargetedLeapCapabilityParameters x => x with { LaunchPresentationId = "", LandingPresentationId = "" },
        CookedMankiRoundBombCapabilityParameters x => x with { ExplosionPresentationId = "" },
        CookedMankiJetpackBoostCapabilityParameters x => x with { ExplosionPresentationId = "" },
        CookedMankiBazookaCapabilityParameters x => x with { ExplosionPresentationId = "" },
        CookedChargedProjectileCapabilityParameters or CookedRisingDragonCapabilityParameters
            or CookedCycloneKickCapabilityParameters or CookedChargedDirectionalDashCapabilityParameters
            or CookedWibouRisingSlashCapabilityParameters or CookedWibouBladeFlurryCapabilityParameters
            or CookedMankiAerosolInfernoCapabilityParameters => p,
        _ => throw new ArgumentException("Update the Arcade comparison policy for this capability."),
    };
}
