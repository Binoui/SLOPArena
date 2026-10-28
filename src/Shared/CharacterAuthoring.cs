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
    IReadOnlyList<CharacterAliasSource> Aliases,
    ShellSource? Shell = null);

/// <summary>
/// A character's shell stance (slop.ability.shell-stance.v1, ADR-0045): the state between her tuck moves.
/// She skates on the ground for <see cref="SkateTicks"/> and coasts in the air, bowling fighters over with
/// <see cref="Hitbox"/>, until the skate runs out, a wall or a shield stops her, or she acts.
/// </summary>
public sealed record ShellSource(
    ushort SkateTicks,
    float MinSkateSpeed,
    float MaxSkateSpeed,
    float LandingSpeedRatio,
    float PopOutDeceleration,
    float BlockedRecoilSpeed,
    HitboxSource Hitbox,
    string? HitPresentationId = null);

/// <summary>How a move treats the shell (ADR-0045). Omitted means PopOut.</summary>
public enum ShellPolicy : byte
{
    /// <summary>Starting the move pops her out; from a grounded shell it starts at skate speed.</summary>
    PopOut = 0,
    /// <summary>The move runs in the shell, tucking her if she is not; when it ends she is back in the shell.</summary>
    Tuck = 1,
}

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
    string AirDodge = "",
    string Shell = "");

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
    float AimMovementSpeedScale = 1f,
    ShellPolicy ShellPolicy = ShellPolicy.PopOut);

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
    float YawOffsetDegrees = 0f,
    ProjectileReturnSource? Return = null);

/// <summary>
/// A projectile's optional "return" block: it flies out for outboundTicks (or until it meets the stage
/// or a shield), then steers back to its thrower at up to returnSpeed, its velocity changing by at most
/// returnAcceleration. It hits each fighter at most once per leg, pulling them toward the thrower on the
/// way back. The thrower catches it when free; busy, it hits him for selfHitDamage and a fixed stun of
/// selfHitStunTicks with no launch. Its slot cannot be used again while it is out.
/// </summary>
public sealed record ProjectileReturnSource(
    ushort OutboundTicks,
    float ReturnSpeed,
    float ReturnAcceleration,
    float SelfHitDamage,
    ushort SelfHitStunTicks,
    string CatchPresentationId = "",
    string SelfHitPresentationId = "");

public abstract record TypedCapabilityParameters;

/// <summary>Automatic setup followed by a movement-input-aimed ballistic recovery (runtime API 1.9.0).
/// Optional RiseSteerAcceleration (API 1.11.0, m/s²) lets the stick steer horizontally after launch, up to HorizontalSpeed.</summary>
public sealed record AimedRecoveryCapabilityParameters(
    ushort StartupTicks,
    float VerticalSpeed,
    float DirectionalVerticalSpeed,
    float HorizontalSpeed,
    float PreparationRiseSpeed,
    RecoveryHopParameters? Hop = null,
    float RiseSteerAcceleration = 0f) : TypedCapabilityParameters;

/// <summary>Optional automatic setup hop (API 1.10.0), in ticks and world metres. ArcHeight is above the entry-to-landing chord.</summary>
public sealed record RecoveryHopParameters(
    ushort StartTicks, ushort ContactTicks, float ForwardDistance, float ArcHeight, float LandingHeight);

public sealed record ChargedProjectileCapabilityParameters(
    ushort MaxChargeTicks,
    ushort Tier2Ticks,
    ushort Tier3Ticks,
    ushort FireTriggerTick,
    float Tier2Radius,
    float Tier3Radius,
    ProjectileSource Projectile) : TypedCapabilityParameters;

/// <summary>
/// Hold to pump, release to spray a stream of projectiles (runtime API 1.8.0; Bashie's Hose Down, like FLUDD). The longer
/// the pump, up to <see cref="MaxChargeTicks"/>, the longer the stream and the harder each projectile pushes: from the
/// projectile's own <c>BaseKnockback</c> and <see cref="MinStreamTicks"/> after a tap to <see cref="MaxBaseKnockback"/>
/// and <see cref="MaxStreamTicks"/> after a full pump. Fired in the air, the stream pushes its owner back at
/// <see cref="MinRecoilSpeed"/> to <see cref="MaxRecoilSpeed"/> (m/s). The capability owns the end of its move.
/// <see cref="SprayAnimationId"/> plays from the release, looped every <see cref="SprayLoopTicks"/> while the stream runs,
/// and <see cref="EndAnimationId"/> for the recovery: phase clips listed in the stage after its first clip, played in
/// real time.
/// </summary>
public sealed record ChargedStreamCapabilityParameters(
    ushort MaxChargeTicks,
    ushort FireTriggerTick,
    ushort EmitIntervalTicks,
    ushort MinStreamTicks,
    ushort MaxStreamTicks,
    ushort RecoveryTicks,
    float MaxBaseKnockback,
    float MinRecoilSpeed,
    float MaxRecoilSpeed,
    ProjectileSource Projectile,
    string SprayAnimationId = "",
    ushort SprayLoopTicks = 0,
    string EndAnimationId = "") : TypedCapabilityParameters;


/// <summary>One aimed shot, then recovery; the last shot starts a committed reload, with no passive refill.</summary>
public sealed record FingerGunsCapabilityParameters(
    ushort FireTriggerTick,
    ushort RecoveryTicks,
    byte ShotsPerReload,
    ushort ReloadTicks,
    string ReloadAnimationId,
    float AirMaxFallSpeed,
    float AirRecoilSpeed,
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
    float SideY,
    ushort HitIntervalTicks = 0,
    ushort FixedHitstunTicks = 0,
    ushort FinisherTick = 0,
    float FinisherDamage = 0f,
    float FinisherKnockbackAngle = 0f,
    float FinisherKnockbackBase = 0f,
    float FinisherKnockbackGrowth = 0f) : TypedCapabilityParameters;


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
    ushort AscentStartTick,
    ushort AscentEndTick,
    ushort DescentStartTick,
    float RiseSpeed,
    float DescentSpeed) : TypedCapabilityParameters;
/// <summary>
/// A steerable charge (slop.ability.steered-charge.v1): a gallop at a fixed speed whose heading the
/// stick turns within a capped rate, with a lance hitbox and optional armour for as long as it lasts.
/// The first fighter the lance hits ends it with the hit ending; a shield, a wall or running out of
/// gallop ends it with the miss ending (a skid, optionally turning round). With no ground under it the
/// charge strides level on thin air, hangs for the look-down and ends, so the fighter falls and can act.
/// Each phase can show its own clip, named here and listed in the capability's stage.
/// </summary>
public sealed record SteeredChargeCapabilityParameters(
    float GallopSpeed,
    float TurnDegreesPerSecond,
    ushort MaxGallopTicks,
    bool Armored,
    HitboxSource LanceHitbox,
    ushort HitEndingTicks,
    ushort MissEndingTicks,
    ushort SkidTicks,
    bool TurnAroundOnMiss,
    ushort AirStrideTicks,
    ushort LookDownTicks,
    string GallopAnimationId = "",
    ushort GallopLoopTicks = 0,
    string HitEndingAnimationId = "",
    string MissEndingAnimationId = "",
    string AirEndingAnimationId = "",
    string GallopPresentationId = "",
    string HitEndingPresentationId = "",
    string MissEndingPresentationId = "",
    string AirEndingPresentationId = "") : TypedCapabilityParameters;
/// <summary>Where a thrust points (ADR-0045).</summary>
public enum ThrustAim : byte
{
    /// <summary>Yaw from the movement stick, facing when it is neutral.</summary>
    Stick = 0,
    /// <summary>Yaw toward the current target when it is inside the cone ahead of her facing, else as Stick.</summary>
    TargetAhead = 1,
}

/// <summary>
/// slop.ability.thrust.v1 (runtime API 1.12.0): travel along one direction chosen at start, gravity off,
/// for <see cref="ThrustTicks"/>, then leave the velocity alone. Pitch runs from the neutral pitch to the
/// stick pitch with stick magnitude (+90 straight up). An optional trail hitbox is authored in the travel
/// frame (+Z along the travel, behind her is -Z) and lasts as long as the thrust.
/// </summary>
public sealed record ThrustCapabilityParameters(
    ThrustAim Aim,
    float TargetConeDegrees,
    float NeutralPitchDegrees,
    float StickPitchDegrees,
    float Speed,
    float Acceleration,
    float MaxSpeed,
    bool KeepIncomingSpeed,
    ushort ThrustTicks,
    HitboxSource? TrailHitbox = null) : TypedCapabilityParameters;

/// <summary>
/// slop.ability.tether.v1 (runtime API 1.13.0, ADR-0048), Bashie's Rescue Ring: at <see cref="ThrowTick"/> the
/// projectile flies straight at her target (her facing without one). The first fighter it meets is reeled to
/// <see cref="AnchorDistance"/> in front of her feet at <see cref="ReelSpeed"/> per axis and arrives stuck for
/// <see cref="ArrivalStunTicks"/>. A miss reels the empty ring back at <see cref="RetractSpeed"/>, then
/// <see cref="MissRecoveryTicks"/>. Airborne her gravity is scaled by <see cref="AirGravityScale"/>. The projectile
/// has no gravity, no yaw offset and no return. Ticks are 60 Hz, distances metres, speeds m/s.
/// </summary>
public sealed record TetherCapabilityParameters(
    ushort ThrowTick,
    ProjectileSource Projectile,
    float ReelSpeed,
    float AnchorDistance,
    ushort ArrivalStunTicks,
    ushort MaxReelTicks,
    float RetractSpeed,
    ushort MissRecoveryTicks,
    float AirGravityScale,
    string ReelAnimationId = "",
    string RetractAnimationId = "",
    string ThrowPresentationId = "",
    string RetractPresentationId = "") : TypedCapabilityParameters;
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
    string ExplosionPresentationId = "",
    ushort FuseTicks = 240,
    ushort WarningTicks = 8,
    float EarlyDamage = 16f,
    float EarlyRadius = 4f,
    float ImpactRadius = 1.1f) : TypedCapabilityParameters;
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
    string ExplosionPresentationId = "",
    ushort FuelTicks = 120,
    float SinkSpeed = 1.2f,
    float JumpBurstSpeed = 10f,
    ushort JumpBurstTicks = 12,
    float ExhaustRadius = 1.4f) : TypedCapabilityParameters;
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
    float HomingTurnDegPerTick,
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
    /// <summary>Writes only the vertical speed and keeps horizontal carry (runtime API 1.12.0).</summary>
    Vertical = 2,
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
