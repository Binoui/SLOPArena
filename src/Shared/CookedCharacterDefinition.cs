using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SlopArena.Shared;
public enum CharacterDiagnosticSeverity : byte
{
    Warning = 0,
    Error = 1,
}

public sealed record CharacterDiagnostic(
    CharacterDiagnosticSeverity Severity,
    string Code,
    string Path,
    string Message);


public sealed class CookedCharacterPackage
{
    public CookedPackageMetadata Metadata { get; }
    public CookedCharacterDefinition Definition { get; }
    public CookedBudget Budget { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    private readonly byte[] _canonicalBytes;
    public byte[] CanonicalBytes => (byte[])_canonicalBytes.Clone();

    public CookedCharacterPackage(
        CookedPackageMetadata metadata,
        CookedCharacterDefinition definition,
        CookedBudget budget,
        IReadOnlyList<CharacterDiagnostic> diagnostics,
        byte[] canonicalBytes)
    {
        Metadata = metadata;
        Definition = definition;
        Budget = budget;
        Diagnostics = Copy(diagnostics);
        _canonicalBytes = (byte[])canonicalBytes.Clone();
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        => new ReadOnlyCollection<T>(new List<T>(values));
}

public sealed record CookedPackageMetadata(
    string PackageId,
    string Version,
    ushort CookedSchemaVersion,
    string RuntimeApiMin,
    string RuntimeApiMax);

public sealed class CookedCharacterDefinition
{
    public string DisplayName { get; }
    public float Weight { get; }
    public CookedMovement Movement { get; }
    public CookedPresentation Presentation { get; }
    public float CapsuleRadius { get; }
    public float CapsuleHeight { get; }
    public float HipHeight { get; }
    public float HurtboxRadius { get; }
    public float ShieldRadius { get; }
    public CookedCaptureGeometry CaptureGeometry { get; }
    public IReadOnlyList<CookedHurtboxCapsule> HurtboxCapsules { get; }
    public IReadOnlyList<CookedHurtboxBone> HurtboxBoneDefs { get; }
    public IReadOnlyList<string> AttachmentBoneIds { get; }
    public IReadOnlyList<string> PresentationIds { get; }
    public IReadOnlyList<CookedCapabilityRequirement> CapabilityRequirements { get; }
    public IReadOnlyList<CookedSlotDefinition> Slots { get; }

    public CookedCharacterDefinition(
        string displayName,
        float weight,
        CookedMovement movement,
        CookedPresentation presentation,
        float capsuleRadius,
        float capsuleHeight,
        float hipHeight,
        float hurtboxRadius,
        float shieldRadius,
        CookedCaptureGeometry captureGeometry,
        IReadOnlyList<CookedHurtboxCapsule> hurtboxCapsules,
        IReadOnlyList<CookedHurtboxBone> hurtboxBoneDefs,
        IReadOnlyList<string> attachmentBoneIds,
        IReadOnlyList<string> presentationIds,
        IReadOnlyList<CookedCapabilityRequirement> capabilityRequirements,
        IReadOnlyList<CookedSlotDefinition> slots)
    {
        DisplayName = displayName;
        Weight = weight;
        Movement = movement;
        Presentation = presentation;
        CapsuleRadius = capsuleRadius;
        CapsuleHeight = capsuleHeight;
        HipHeight = hipHeight;
        HurtboxRadius = hurtboxRadius;
        ShieldRadius = shieldRadius;
        CaptureGeometry = captureGeometry;
        HurtboxCapsules = Copy(hurtboxCapsules);
        HurtboxBoneDefs = Copy(hurtboxBoneDefs);
        AttachmentBoneIds = Copy(attachmentBoneIds);
        PresentationIds = Copy(presentationIds);
        CapabilityRequirements = Copy(capabilityRequirements);
        Slots = Copy(slots);
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        => new ReadOnlyCollection<T>(new List<T>(values));
}

public sealed record CookedMovement(
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

public sealed record CookedPresentation(
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

public sealed record CookedHurtboxCapsule(
    float StartX,
    float StartY,
    float StartZ,
    float EndX,
    float EndY,
    float EndZ,
    float Radius);

public sealed record CookedHurtboxBone(
    string BoneId,
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float Radius);

public sealed record CookedCaptureGeometry(
    float Reach,
    float Width,
    float Height,
    float OffsetY,
    CaptureAnchor AttackerAnchor,
    CaptureAnchor VictimAnchor);

public sealed record CaptureAnchor(float X, float Y, float Z);
public sealed record CookedCapabilityRequirement(string CapabilityId, string CapabilityVersion);
public sealed class CookedSlotDefinition
{
    public int Ordinal { get; }
    public string Id { get; }
    public bool IsAir { get; }
    public string Name { get; }
    public string Description { get; }
    public string IconId { get; }
    public AuthoringAbilityBehavior Behavior { get; }
    public AuthoringAimMode AimMode { get; }
    public ushort CooldownTicks { get; }
    public bool IsRecoveryMove { get; }
    public bool PreserveMomentumOnStart { get; }
    public bool AllowSlideCarry { get; }
    public string? HitPresentationId { get; }
    public CookedChargePool? ChargePool { get; }
    public AuthoringAimMovementMode AimMovement { get; }
    public string? AimAnimationId { get; }
    public CookedTimeline Timeline { get; }

    public CookedSlotDefinition(
        int ordinal,
        string id,
        bool isAir,
        string name,
        string description,
        string iconId,
        AuthoringAbilityBehavior behavior,
        AuthoringAimMode aimMode,
        ushort cooldownTicks,
        bool isRecoveryMove,
        bool preserveMomentumOnStart,
        CookedTimeline timeline,
        CookedChargePool? chargePool = null,
        AuthoringAimMovementMode aimMovement = AuthoringAimMovementMode.Fixed,
        string? aimAnimationId = null,
        bool allowSlideCarry = false,
        string? hitPresentationId = null)
    {
        Ordinal = ordinal;
        Id = id;
        IsAir = isAir;
        Name = name;
        Description = description;
        IconId = iconId;
        Behavior = behavior;
        AimMode = aimMode;
        CooldownTicks = cooldownTicks;
        IsRecoveryMove = isRecoveryMove;
        PreserveMomentumOnStart = preserveMomentumOnStart;
        AllowSlideCarry = allowSlideCarry;
        Timeline = timeline;
        ChargePool = chargePool;
        AimMovement = aimMovement;
        AimAnimationId = aimAnimationId;
        HitPresentationId = hitPresentationId;
    }
}

public sealed record CookedChargePool(int MaxCharges, ushort RegenTicks);

public sealed class CookedTimeline
{
    public IReadOnlyList<CookedStage> Stages { get; }

    public CookedTimeline(IReadOnlyList<CookedStage> stages)
        => Stages = new ReadOnlyCollection<CookedStage>(new List<CookedStage>(stages));
}

public sealed class CookedStage
{
    public ushort DurationTicks { get; }
    public ushort IasaTicks { get; }
    public ushort LandingLagTicks { get; }
    public ushort AutoCancelBeforeTicks { get; }
    public ushort AutoCancelAfterTicks { get; }
    public float AttackRange { get; }
    public float WarpRange { get; }
    public bool UseTargetLock { get; }
    public bool RotateTowardTarget { get; }
    public float TrackingStrength { get; }
    public IReadOnlyList<string> AnimationIds { get; }
    public IReadOnlyList<CookedTimelineOperation> Operations { get; }

    public CookedStage(
        ushort durationTicks,
        ushort iasaTicks,
        ushort landingLagTicks,
        ushort autoCancelBeforeTicks,
        ushort autoCancelAfterTicks,
        IReadOnlyList<string> animationIds,
        IReadOnlyList<CookedTimelineOperation> operations,
        float attackRange = 0f,
        float warpRange = 0f,
        bool useTargetLock = false,
        bool rotateTowardTarget = false,
        float trackingStrength = 0f)
    {
        DurationTicks = durationTicks;
        IasaTicks = iasaTicks;
        LandingLagTicks = landingLagTicks;
        AutoCancelBeforeTicks = autoCancelBeforeTicks;
        AutoCancelAfterTicks = autoCancelAfterTicks;
        AttackRange = attackRange;
        WarpRange = warpRange;
        UseTargetLock = useTargetLock;
        RotateTowardTarget = rotateTowardTarget;
        TrackingStrength = trackingStrength;
        AnimationIds = new ReadOnlyCollection<string>(new List<string>(animationIds));
        Operations = new ReadOnlyCollection<CookedTimelineOperation>(new List<CookedTimelineOperation>(operations));
    }
}

public enum CookedOperationKind : byte
{
    SetVelocity = 0,
    SpawnHitbox = 1,
    SpawnProjectile = 2,
    SetAimState = 3,
    StartCapability = 4,
    EmitPresentation = 5,
    CompleteTimeline = 6,
    ForwardLunge = 7,
    GravityWindow = 8,
    ArmorWindow = 9,
    StartupAimCorrection = 10,
}
public abstract class CookedTimelineOperation
{
    public ushort Tick { get; }
    public AuthoringUnit Unit { get; }
    public CookedOperationKind Kind { get; }

    protected CookedTimelineOperation(ushort tick, AuthoringUnit unit, CookedOperationKind kind)
    {
        Tick = tick;
        Unit = unit;
        Kind = kind;
    }
}

public sealed class CookedSetVelocityOperation : CookedTimelineOperation
{
    public AuthoringVelocityMode VelocityMode { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public CookedSetVelocityOperation(ushort tick, AuthoringUnit unit, AuthoringVelocityMode velocityMode, float x, float y, float z)
        : base(tick, unit, CookedOperationKind.SetVelocity)
    {
        VelocityMode = velocityMode;
        X = x;
        Y = y;
        Z = z;
    }
}
public sealed class CookedForwardLungeOperation : CookedTimelineOperation
{
    public float Speed { get; }
    public ushort DurationTicks { get; }
    public bool StopInAttackRange { get; }

    public CookedForwardLungeOperation(ushort tick, AuthoringUnit unit, float speed, ushort durationTicks, bool stopInAttackRange = false)
        : base(tick, unit, CookedOperationKind.ForwardLunge)
    {
        Speed = speed;
        DurationTicks = durationTicks;
        StopInAttackRange = stopInAttackRange;
    }
}
public sealed class CookedGravityWindowOperation : CookedTimelineOperation
{
    public float GravityScale { get; }
    public ushort DurationTicks { get; }

    public CookedGravityWindowOperation(ushort tick, AuthoringUnit unit, float gravityScale, ushort durationTicks)
        : base(tick, unit, CookedOperationKind.GravityWindow)
    {
        GravityScale = gravityScale;
        DurationTicks = durationTicks;
    }
}

public sealed class CookedArmorWindowOperation : CookedTimelineOperation
{
    public ushort DurationTicks { get; }

    public CookedArmorWindowOperation(ushort tick, AuthoringUnit unit, ushort durationTicks)
        : base(tick, unit, CookedOperationKind.ArmorWindow)
        => DurationTicks = durationTicks;
}

public sealed class CookedStartupAimCorrectionOperation : CookedTimelineOperation
{
    public ushort EndTick { get; }
    public float AcquisitionRange { get; }
    public float AcquisitionHalfAngleDegrees { get; }
    public float MaxYawDegrees { get; }
    public float MaxPitchDegrees { get; }
    public float YawDegreesPerSecond { get; }
    public float PitchDegreesPerSecond { get; }

    public CookedStartupAimCorrectionOperation(
        ushort tick,
        AuthoringUnit unit,
        ushort endTick,
        float acquisitionRange,
        float acquisitionHalfAngleDegrees,
        float maxYawDegrees,
        float maxPitchDegrees,
        float yawDegreesPerSecond,
        float pitchDegreesPerSecond)
        : base(tick, unit, CookedOperationKind.StartupAimCorrection)
    {
        EndTick = endTick;
        AcquisitionRange = acquisitionRange;
        AcquisitionHalfAngleDegrees = acquisitionHalfAngleDegrees;
        MaxYawDegrees = maxYawDegrees;
        MaxPitchDegrees = maxPitchDegrees;
        YawDegreesPerSecond = yawDegreesPerSecond;
        PitchDegreesPerSecond = pitchDegreesPerSecond;
    }
}


public sealed class CookedSpawnHitboxOperation : CookedTimelineOperation
{
    public CookedHitbox Hitbox { get; }
    public CookedSpawnHitboxOperation(ushort tick, AuthoringUnit unit, CookedHitbox hitbox)
        : base(tick, unit, CookedOperationKind.SpawnHitbox) => Hitbox = hitbox;
}

public sealed record CookedHitbox(
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

public sealed class CookedSpawnProjectileOperation : CookedTimelineOperation
{
    public CookedProjectile Projectile { get; }
    public CookedSpawnProjectileOperation(ushort tick, AuthoringUnit unit, CookedProjectile projectile)
        : base(tick, unit, CookedOperationKind.SpawnProjectile) => Projectile = projectile;
}

public sealed record CookedProjectile(
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

public sealed record CookedChargedProjectileCapabilityParameters(
    ushort MaxChargeTicks,
    ushort Tier2Ticks,
    ushort Tier3Ticks,
    ushort FireTriggerTick,
    float Tier2Radius,
    float Tier3Radius,
    CookedProjectile Projectile) : CookedCapabilityParameters
{
    public byte GetChargeTier(ushort chargeTicks)
        => chargeTicks >= Tier3Ticks ? (byte)2 : chargeTicks >= Tier2Ticks ? (byte)1 : (byte)0;

    public float GetRadius(ushort chargeTicks)
        => GetChargeTier(chargeTicks) == 2 ? Tier3Radius
            : GetChargeTier(chargeTicks) == 1 ? Tier2Radius : Projectile.Radius;
}

public sealed class CookedSetAimStateOperation : CookedTimelineOperation
{
    public AuthoringAimMode AimState { get; }
    public CookedSetAimStateOperation(ushort tick, AuthoringUnit unit, AuthoringAimMode aimState)
        : base(tick, unit, CookedOperationKind.SetAimState) => AimState = aimState;
}

public sealed class CookedStartCapabilityOperation : CookedTimelineOperation
{
    public string CapabilityId { get; }
    public string CapabilityVersion { get; }
    public CookedCapabilityParameters Parameters { get; }

    public CookedStartCapabilityOperation(ushort tick, AuthoringUnit unit, string capabilityId, string capabilityVersion, CookedCapabilityParameters parameters)
        : base(tick, unit, CookedOperationKind.StartCapability)
    {
        CapabilityId = capabilityId;
        CapabilityVersion = capabilityVersion;
        Parameters = parameters;
    }
}

public sealed class CookedEmitPresentationOperation : CookedTimelineOperation
{
    public string PresentationId { get; }
    public int OperationIndex { get; }
    public PresentationPlacement Placement { get; }
    public CookedEmitPresentationOperation(
        ushort tick,
        AuthoringUnit unit,
        string presentationId,
        int operationIndex,
        PresentationPlacement placement)
        : base(tick, unit, CookedOperationKind.EmitPresentation)
    {
        PresentationId = presentationId;
        OperationIndex = operationIndex;
        Placement = placement ?? new PresentationPlacement();
    }
    public CookedEmitPresentationOperation(
        ushort tick,
        AuthoringUnit unit,
        string presentationId,
        int operationIndex)
        : this(tick, unit, presentationId, operationIndex, new PresentationPlacement()) { }
}

public sealed class CookedCompleteTimelineOperation : CookedTimelineOperation
{
    public CookedCompleteTimelineOperation(ushort tick, AuthoringUnit unit)
        : base(tick, unit, CookedOperationKind.CompleteTimeline) { }
}
public abstract record CookedCapabilityParameters;
public sealed record CookedRisingDragonCapabilityParameters(float RiseSpeed, ushort RiseTicks, ushort RiseDelay) : CookedCapabilityParameters;
public sealed record CookedCycloneKickCapabilityParameters(
    float ForwardSpeed, ushort WindupTicks, ushort HitboxEndTick, ushort DurationTicks, float BodyRadius,
    float SideRadius, float SideOffset, float Damage, float KnockbackAngle, float KnockbackBase,
    float KnockbackGrowth, ushort StunTicks, float BodyY, float SideY) : CookedCapabilityParameters;
public sealed record CookedChargedDirectionalDashCapabilityParameters(
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
    CookedHitbox TraversalHitbox,
    CookedHitbox FinisherHitbox) : CookedCapabilityParameters
{
    public byte GetChargeTier(ushort chargeTicks)
        => chargeTicks >= Tier3Ticks ? (byte)2 : chargeTicks >= Tier2Ticks ? (byte)1 : (byte)0;

    public float GetDashDistance(ushort chargeTicks)
        => chargeTicks >= MaxChargeTicks ? MaxDistance
            : MinDistance + (MaxDistance - MinDistance) * chargeTicks / MaxChargeTicks;

    public float GetFinisherDamage(ushort chargeTicks)
        => GetChargeTier(chargeTicks) == 2 ? Tier3Damage
            : GetChargeTier(chargeTicks) == 1 ? Tier2Damage
            : FinisherHitbox.Damage;
}

public sealed record CookedWibouRisingSlashCapabilityParameters(
    float RiseSpeed,
    ushort RiseTicks,
    float HomingRange,
    float HomingSpeed) : CookedCapabilityParameters;

public sealed record CookedWibouBladeFlurryCapabilityParameters(
    float ForwardSpeed,
    ushort MoveTicks) : CookedCapabilityParameters;
public sealed record CookedTargetedLeapCapabilityParameters(
    ushort MaxAimTicks,
    ushort MaxFlightTicks,
    float MinRange,
    float MaxRange,
    float LaunchVerticalSpeed,
    ushort LandingSeekTick,
    ushort RecoveryTicks,
    CookedHitbox Hitbox) : CookedCapabilityParameters;
public sealed record CookedMankiRoundBombCapabilityParameters(
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
    string ExplosionPresentationId = "") : CookedCapabilityParameters;
public sealed record CookedMankiJetpackBoostCapabilityParameters(
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
    string ExplosionPresentationId = "") : CookedCapabilityParameters;
public sealed record CookedMankiBazookaCapabilityParameters(
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
    string ExplosionPresentationId = "") : CookedCapabilityParameters;

public sealed record CookedMankiAerosolInfernoCapabilityParameters(
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
    byte HitGroup) : CookedCapabilityParameters;

public sealed record CookedBudget(
    int SlotCount,
    int StageCount,
    int OperationCount,
    int HitboxCount,
    int ProjectileCount,
    int CapabilityCount,
    int MaxTimelineDurationTicks)
{
    public const int MaxSlots = 16;
    public const int MaxStagesPerTimeline = 8;
    public const int MaxOperationsPerStage = 64;
    public const int MaxOperationsPerTimeline = 256;
    public const int MaxCapabilityRequirements = 32;
}
