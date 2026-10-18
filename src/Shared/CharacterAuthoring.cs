using System.Collections.Generic;

namespace SlopArena.Shared;

public sealed record CharacterPackageSource(
    PackageManifestSource Manifest,
    CharacterAuthoringDocument Character);

public sealed record PackageManifestSource(
    ushort ManifestSchemaVersion,
    string PackageId,
    string Version,
    string Creator,
    string License,
    string Attribution,
    IReadOnlyList<PackageDependencySource> Dependencies);

public sealed record PackageDependencySource(
    string PackageId,
    string Version,
    string CookedHash);

public sealed record CharacterAuthoringDocument(
    ushort AuthoringSchemaVersion,
    string DisplayName,
    float Weight,
    CharacterMovementSource Movement,
    CharacterPresentationSource Presentation,
    float CapsuleRadius,
    float CapsuleHeight,
    float HipHeight,
    float HurtboxRadius,
    float ShieldRadius,
    CharacterCaptureGeometrySource CaptureGeometry,
    IReadOnlyList<HurtboxCapsuleSource> HurtboxCapsules,
    IReadOnlyList<HurtboxBoneSource> HurtboxBoneDefs,
    IReadOnlyList<string> AttachmentBoneIds,
    IReadOnlyList<string> PresentationIds,
    IReadOnlyList<CapabilityRequirementSource> CapabilityRequirements,
    IReadOnlyList<CharacterSlotSource> Slots,
    IReadOnlyList<CharacterAliasSource> Aliases);

public sealed record CharacterMovementSource(
    float RunSpeed,
    float RunAccelerationA,
    float RunAccelerationB,
    float DashSpeed,
    float AirDodgeSpeed,
    float AirSpeedMax,
    float AirAccelStick,
    float AirAccelBase,
    float JumpForce,
    float ShortHopForce,
    float AirJumpVMultiplier,
    float AirJumpHMultiplier,
    float Gravity,
    float AirFloatGravity,
    ushort DashDurationTicks,
    ushort DashCooldownTicks,
    float GroundFriction,
    float AirFriction,
    float MaxFallSpeed,
    float FastFallSpeed,
    byte MaxJumps,
    ushort JumpSquatTicks,
    ushort FloatWindowTicks,
    ushort RushTicks);

public sealed record CharacterPresentationSource(
    string Idle,
    string Run,
    string Dash,
    string Jump,
    string Fall,
    string HitSmall,
    string HitMedium,
    string HitHard,
    float LandStartOffsetSeconds,
    string ModelResourcePath = "",
    float VisualScale = 1f,
    float HurtboxBoneScale = 1f,
    float ModelYOffset = 0f,
    float ModelSoleOffset = 0f,
    bool AutoModelYOffset = false,
    string Tumble = "",
    string Crouch = "",
    string Slide = "",
    string Shield = "",
    string Grab = "",
    string Grabbed = "",
    string ThrowForward = "",
    string AirDodge = "");

public sealed record HurtboxCapsuleSource(
    float StartX,
    float StartY,
    float StartZ,
    float EndX,
    float EndY,
    float EndZ,
    float Radius);

public sealed record HurtboxBoneSource(
    string BoneId,
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float Radius);

public sealed record CharacterCaptureGeometrySource(
    float Reach,
    float Width,
    float Height,
    float OffsetY,
    CaptureAnchorSource AttackerAnchor,
    CaptureAnchorSource VictimAnchor);

public sealed record CaptureAnchorSource(float X, float Y, float Z);

public sealed record CapabilityRequirementSource(string CapabilityId, string CapabilityVersion);
public sealed record CharacterSlotSource(
    string Id,
    string Name,
    string Description,
    string IconId,
    AuthoringAbilityBehavior Behavior,
    AuthoringAimMode AimMode,
    ushort CooldownTicks,
    bool IsRecoveryMove,
    bool PreserveMomentumOnStart,
    CharacterTimelineSource Timeline,
    ChargePoolSource? ChargePool = null,
    AuthoringAimMovementMode AimMovement = AuthoringAimMovementMode.Fixed,
    string? AimAnimationId = null,
    bool AllowSlideCarry = false,
    string? HitPresentationId = null,
    float AimMovementSpeedScale = 1f);

public sealed record ChargePoolSource(int MaxCharges, ushort RegenTicks);
public sealed record CharacterAliasSource(string From, string To);

public sealed record CharacterTimelineSource(IReadOnlyList<CharacterStageSource> Stages);

public sealed record CharacterStageSource(
    ushort DurationTicks,
    ushort IasaTicks,
    ushort LandingLagTicks,
    ushort AutoCancelBeforeTicks,
    ushort AutoCancelAfterTicks,
    IReadOnlyList<string> AnimationIds,
    IReadOnlyList<CharacterTimelineOperationSource> Operations,
    float AttackRange = 0f,
    float WarpRange = 0f,
    bool UseTargetLock = false,
    bool RotateTowardTarget = false,
    float TrackingStrength = 0f);

public sealed record StartupAimCorrectionOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    ushort EndTick,
    float AcquisitionRange,
    float AcquisitionHalfAngleDegrees,
    float MaxYawDegrees,
    float MaxPitchDegrees,
    float YawDegreesPerSecond,
    float PitchDegreesPerSecond) : CharacterTimelineOperationSource(Tick, Unit);
public abstract record CharacterTimelineOperationSource(ushort Tick, AuthoringUnit Unit);

public sealed record SetVelocityOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    AuthoringVelocityMode VelocityMode,
    float X,
    float Y,
    float Z) : CharacterTimelineOperationSource(Tick, Unit);
public sealed record ForwardLungeOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    float Speed,
    ushort DurationTicks,
    bool StopInAttackRange = false) : CharacterTimelineOperationSource(Tick, Unit);
public sealed record GravityWindowOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    float GravityScale,
    ushort DurationTicks) : CharacterTimelineOperationSource(Tick, Unit);

public sealed record ArmorWindowOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    ushort DurationTicks) : CharacterTimelineOperationSource(Tick, Unit);


public sealed record SpawnHitboxOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    HitboxSource Hitbox) : CharacterTimelineOperationSource(Tick, Unit);

public sealed record SpawnProjectileOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    ProjectileSource Projectile) : CharacterTimelineOperationSource(Tick, Unit);

public sealed record SetAimStateOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    AuthoringAimMode AimState) : CharacterTimelineOperationSource(Tick, Unit);

public sealed record StartCapabilityOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    string CapabilityId,
    string CapabilityVersion,
    TypedCapabilityParameters Parameters) : CharacterTimelineOperationSource(Tick, Unit);
public enum AuthoringPresentationAttachmentMode : byte
{
    World = 0,
    Bone = 1,
}

public sealed record PresentationPlacement(
    AuthoringPresentationAttachmentMode AttachmentMode = AuthoringPresentationAttachmentMode.World,
    string? BoneId = null,
    float LocalPositionX = 0f,
    float LocalPositionY = 0f,
    float LocalPositionZ = 0f,
    float LocalRotationX = 0f,
    float LocalRotationY = 0f,
    float LocalRotationZ = 0f,
    float LocalScaleX = 1f,
    float LocalScaleY = 1f,
    float LocalScaleZ = 1f,
    ushort DurationTicks = 28);

public sealed record EmitPresentationOperationSource(
    ushort Tick,
    AuthoringUnit Unit,
    string PresentationId,
    PresentationPlacement Placement) : CharacterTimelineOperationSource(Tick, Unit)
{
    public EmitPresentationOperationSource(ushort tick, AuthoringUnit unit, string presentationId)
        : this(tick, unit, presentationId, new PresentationPlacement()) { }
}

public sealed record CompleteTimelineOperationSource(
    ushort Tick,
    AuthoringUnit Unit) : CharacterTimelineOperationSource(Tick, Unit);

public sealed record HitboxSource(
    AuthoringHitboxShape Shape,
    float Radius,
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float EndOffsetX,
    float EndOffsetY,
    float EndOffsetZ,
    string? StartBoneId,
    string? EndBoneId,
    float Damage,
    float Angle,
    float BaseKnockback,
    float KnockbackGrowth,
    ushort StunTicks,
    ushort DurationTicks,
    bool Interruptible,
    byte HitGroup,
    AuthoringKnockbackDirection KnockbackDirection = AuthoringKnockbackDirection.AwayFromOwner,
    ushort FixedHitstunTicks = 0);
public sealed record ProjectileSource(
    float LaunchOffsetX,
    float LaunchOffsetY,
    float LaunchOffsetZ,
    float Speed,
    float Gravity,
    float Radius,
    float Damage,
    float Angle,
    float BaseKnockback,
    float KnockbackGrowth,
    ushort StunTicks,
    ushort MaxFlightTicks,
    float YawOffsetDegrees = 0f);

public abstract record TypedCapabilityParameters;

public sealed record ChargedProjectileCapabilityParameters(
    ushort MaxChargeTicks,
    ushort Tier2Ticks,
    ushort Tier3Ticks,
    ushort FireTriggerTick,
    float Tier2Radius,
    float Tier3Radius,
    ProjectileSource Projectile) : TypedCapabilityParameters;


public sealed record RisingDragonCapabilityParameters(
    float RiseSpeed,
    ushort RiseTicks,
    ushort RiseDelay) : TypedCapabilityParameters;

public sealed record CycloneKickCapabilityParameters(
    float ForwardSpeed,
    ushort WindupTicks,
    ushort HitboxEndTick,
    ushort DurationTicks,
    float BodyRadius,
    float SideRadius,
    float SideOffset,
    float Damage,
    float KnockbackAngle,
    float KnockbackBase,
    float KnockbackGrowth,
    ushort StunTicks,
    float BodyY,
    float SideY) : TypedCapabilityParameters;


public sealed record ChargedDirectionalDashCapabilityParameters(
    ushort MaxChargeTicks,
    ushort Tier2Ticks,
    ushort Tier3Ticks,
    float MinDistance,
    float MaxDistance,
    float DashSpeed,
    ushort FinisherLeadTicks,
    ushort FinisherSeekTick,
    ushort RecoveryTicks,
    float Tier2Damage,
    float Tier3Damage,
    HitboxSource TraversalHitbox,
    HitboxSource FinisherHitbox) : TypedCapabilityParameters;

public sealed record WibouRisingSlashCapabilityParameters(
    float RiseSpeed,
    ushort RiseTicks,
    float HomingRange,
    float HomingSpeed) : TypedCapabilityParameters;

public sealed record WibouBladeFlurryCapabilityParameters(
    float ForwardSpeed,
    ushort MoveTicks) : TypedCapabilityParameters;
public sealed record TargetedLeapCapabilityParameters(
    ushort MaxAimTicks,
    ushort MaxFlightTicks,
    float MinRange,
    float MaxRange,
    float LaunchVerticalSpeed,
    ushort LandingSeekTick,
    ushort RecoveryTicks,
    HitboxSource Hitbox,
    string LaunchPresentationId = "",
    string LandingPresentationId = "") : TypedCapabilityParameters;
public sealed record MankiRoundBombCapabilityParameters(
    ushort ThrowTriggerTick,
    float MaxRange,
    float LaunchAngle,
    float Gravity,
    float HitboxRadius,
    float Damage,
    ushort StunTicks,
    ushort MaxFlightTicks,
    float KbAngle,
    float ExplosionDamage,
    float ExplosionRadius,
    float ExplosionKbBase,
    float ExplosionKbGrowth,
    ushort ExplosionStunTicks,
    ushort ExplosionDurationTicks,
    float ExplosionKbAngle,
    string ExplosionPresentationId = "") : TypedCapabilityParameters;
public sealed record MankiJetpackBoostCapabilityParameters(
    ushort StartupTicks,
    float VerticalSpeed,
    float HorizontalSpeed,
    float ExplosionRadius,
    float ExplosionDamage,
    float ExplosionKbAngle,
    float ExplosionKbBase,
    float ExplosionKbGrowth,
    ushort ExplosionStunTicks,
    ushort ExplosionDurationTicks,
    string ExplosionPresentationId = "") : TypedCapabilityParameters;
public sealed record MankiBazookaCapabilityParameters(
    ushort FireTriggerTick,
    float ProjectileSpeed,
    float HitboxRadius,
    float Damage,
    float Gravity,
    ushort MaxFlightTicks,
    ushort StunTicks,
    float ExplosionRadius,
    float KbAngle,
    float ExplosionKbBase,
    float ExplosionKbGrowth,
    ushort ExplosionStunTicks,
    ushort ExplosionDurationTicks,
    float ExplosionKbAngle,
    ushort CastDuration,
    ushort RecoveryDuration,
    string ExplosionPresentationId = "") : TypedCapabilityParameters;

public sealed record MankiAerosolChargeTier(ushort ChargeTicks, float Damage, float ReachScale);

public sealed record MankiAerosolInfernoCapabilityParameters(
    ushort FireTriggerTick,
    ushort FireDurationTicks,
    ushort HitboxDurationTicks,
    float HitboxRadius,
    float OffsetY,
    float OffsetZ,
    float EndOffsetZ,
    float Damage,
    float KnockbackAngle,
    float KnockbackBase,
    float KnockbackGrowth,
    ushort StunTicks,
    byte HitGroup,
    ushort MinimumChargeTicks = 0,
    IReadOnlyList<MankiAerosolChargeTier>? ChargeTiers = null) : TypedCapabilityParameters;

public enum AuthoringAbilityBehavior : byte
{
    MeleeCombo = 0,
    ChargeAttack = 1,
    AimedProjectile = 2,
    Projectile = 3,
    AirGroundProjectile = 4,
    SelfBuff = 5,
    AreaDenial = 6,
    DirectionalDash = 7,
}

public enum AuthoringAimMode : byte
{
    None = 0,
    GroundCursor = 1,
    CameraForward3D = 2,
    GroundVector = 3,
}
public enum AuthoringAimMovementMode : byte
{
    Fixed = 0,
    Mobile = 1,
}


public enum AuthoringHitboxShape : byte
{
    Sphere = 0,
    Capsule = 1,
}
public enum AuthoringKnockbackDirection : byte
{
    AwayFromOwner = 0,
    TowardOwner = 1,
}

public enum AuthoringVelocityMode : byte
{
    Absolute = 0,
    Additive = 1,
}

public enum AuthoringUnit : byte
{
    Meters = 0,
    MetersPerSecond = 1,
    MetersPerSecondSquared = 2,
    Degrees = 3,
    Normalized = 4,
    Damage = 5,
    Knockback = 6,
    Ticks = 7,
}
public enum SourceSlotId : byte
{
    Ground1 = 0,
    Ground2 = 1,
    Ground3 = 2,
    Ground4 = 3,
    GroundA = 4,
    GroundE = 5,
    GroundR = 6,
    GroundF = 7,
    Air1 = 8,
    Air2 = 9,
    Air3 = 10,
    Air4 = 11,
    AirA = 12,
    AirE = 13,
    AirR = 14,
    AirF = 15,
}
