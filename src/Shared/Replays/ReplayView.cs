using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Replays
{
    // View Track v1. Tags and defaults are permanent; see docs/systems/replay-file.md.
    // Each class is a durable domain record. Field IDs are internal
    // serialization metadata. Internal setters are used only while constructing/decoding;
    // publication freezes records and copies collections into read-only wrappers.
    // ALL below fields exist in v1. Default initializers are normative, including nested defaults.
    // Numeric IDs are observations (entity/activation/emission), never enum/slot identities.
    public readonly record struct ReplayVector(float X, float Y, float Z);
    // v1: 3 f32; default=(0,0,0)
    /// <summary>A complete visible world after Tick. All records are immutable after publication.</summary>
    public sealed class ReplayViewTick
    {
        // Tick is the record's fixed u32 prefix, not a tagged field. It must equal the chunk index tick.
        public uint Tick { get; internal set; }
        public string MatchPhase { get; internal set; } = "unknown";
        // frame.MatchState, explicit stable switch
        public IReadOnlyList<ReplayFighter> Fighters { get; internal set; } = Array.Empty<ReplayFighter>();
        public IReadOnlyList<ReplayShape> Shapes { get; internal set; } = Array.Empty<ReplayShape>();
        public IReadOnlyList<ReplayModeObject> ModeObjects { get; internal set; } = Array.Empty<ReplayModeObject>();
        public IReadOnlyList<ReplayEvent> Events { get; internal set; } = Array.Empty<ReplayEvent>();
        public IReadOnlyList<ReplayHit> Hits { get; internal set; } = Array.Empty<ReplayHit>();
        public IReadOnlyList<ReplayDeath> Deaths { get; internal set; } = Array.Empty<ReplayDeath>();
    }

    /// <summary>
    /// Current renderer inputs, expressed without AttackSlot, AnimIndex, ComboStage or ActionState ordinals.
    /// Entity order matches the header. Pose and timing are recorded; today's tuning never retimes an old move.
    /// Clip, form and move lookups failing in Unity give a neutral pose and the recorded move name.
    /// </summary>
    public sealed class ReplayFighter
    {
        public ulong EntityId { get; internal set; } = 0;
        // s.EntityId
        public string ActionId { get; internal set; } = "unknown";
        // explicit ActionState->stable ID
        public ReplayVector Position { get; internal set; } = default;
        // s.PX/PY/PZ
        public ReplayVector Velocity { get; internal set; } = default;
        // s.VX/VY/VZ
        public ReplayVector KnockbackVelocity { get; internal set; } = default;
        // s.KVX/KVY/KVZ
        public float FacingYaw { get; internal set; } = 0;
        // s.FacingYaw, radians
        public bool Grounded { get; internal set; } = false;
        // s.IsGrounded
        public ushort ActionTicks { get; internal set; } = 0;
        // s.StateTicks (remaining)
        public ushort DamagePercent { get; internal set; } = 0;
        // s.DamagePercent, observed value
        public byte Deaths { get; internal set; } = 0;
        // s.Deaths
        public byte KOs { get; internal set; } = 0;
        // sim.GetKOs(id)
        public byte JumpsLeft { get; internal set; } = 0;
        // s.JumpsLeft
        public bool WallJumpUsed { get; internal set; } = false;
        // s.WallJumpUsed, transition cues
        public ushort InvincibilityTicks { get; internal set; } = 0;
        // s.InvincibilityTicks
        public ushort LandingLagTicks { get; internal set; } = 0;
        // s.LandingLagTicks
        public ushort HitstunTicks { get; internal set; } = 0;
        // s.HitstunTicks
        public ushort HitstopTicks { get; internal set; } = 0;
        // s.HitstopTicks
        public string HitstunId { get; internal set; } = "none";
        // no reaction -> none; reacting level0=small,1=medium,2=hard
        public ushort HitReactionSequence { get; internal set; } = 0;
        // s.HitReactionSequence
        public bool WasAirborneDuringKnockback { get; internal set; } = false;
        public bool PostHitstunFlight { get; internal set; } = false;
        // s.InPostHitstunFlight
        public string FormId { get; internal set; } = "base";
        // s.Form.Active -> base/alternate; no cooldown banks
        public string SlotId { get; internal set; } = "";
        // GetActiveMove(id)?.Slot.Id; slot address, paired with package/form/name
        public ulong ActivationId { get; internal set; } = 0;
        // GetLastActivationId, only when active move exists
        public byte AttackSequence { get; internal set; } = 0;
        // event identity, not an activation counter
        public bool ActivationAirborne { get; internal set; } = false;
        // ActiveMoveView.AirborneAtStart
        public string MoveFormId { get; internal set; } = "base";
        // ActiveMoveView.Slot.Form, may differ from live form
        public ushort MoveElapsedTicks { get; internal set; } = 0;
        // s.AttackElapsedTicks
        public string PoseAnimationId { get; internal set; } = "";
        // ActiveMoveView.Pose.AnimationId
        public float PoseElapsedTicks { get; internal set; } = 0;
        // Pose.ElapsedTicks, f32 may be fractional
        public ushort PoseDurationTicks { get; internal set; } = 0;
        // Pose.DurationTicks, recorded presentation clock
        public bool PhaseClip { get; internal set; } = false;
        // Pose.IsPhaseClip
        public float PosePitch { get; internal set; } = 0;
        // s.AttackPosePitch
        public ushort ChargeTicks { get; internal set; } = 0;
        // s.ChargeTicks
        public string AimAnimationId { get; internal set; } = "";
        // active Slot.AimAnimationId; aiming/charge pose
        public string AimMovementId { get; internal set; } = "fixed";
        // active Slot.AimMovement explicit switch
        public string ReturningSlotId { get; internal set; } = "";
        // owner's live returning h, h.Form, h.ActivationAirborne
        public bool FlightActive { get; internal set; } = false;
        // s.Flight.Active
        public ushort FlightFuelLeft { get; internal set; } = 0;
        // s.Flight.FuelTicksLeft
        public ushort FlightBurstLeft { get; internal set; } = 0;
        // s.Flight.BurstTicksLeft
        public float FlightLiftSpeed { get; internal set; } = 0;
        // s.Flight.LiftSpeed, current state
        public float FlightHeadingYaw { get; internal set; } = 0;
        // s.Flight.HeadingYaw, radians
        public float FlightElapsedTicks { get; internal set; } = 0;
        // current parameter FuelTicks - s.Flight.FuelTicksLeft, not parameter itself
        public string InteractionPhaseId { get; internal set; } = "none";
        // s.InteractionPhase explicit switch
        public ulong InteractionId { get; internal set; } = 0;
        // s.InteractionId
        public ulong InteractionPartnerId { get; internal set; } = 0;
        // s.InteractionPartnerId
        public float CapturedYaw { get; internal set; } = 0;
        // s.CapturedYaw * 0.01 degrees -> radians
        public bool Inactive { get; internal set; } = false;
        // sim.IsEntityInactive(id), not Deaths >= today's stock tuning
        public bool FastFalling { get; internal set; } = false;
        // s.IsFastFalling; avoid today's FastFallSpeed comparison
        public string PackageId { get; internal set; } = "";
        public string MoveName { get; internal set; } = "";
        public string ReturningPackageId { get; internal set; } = "";
        public string ReturningFormId { get; internal set; } = "base";
        public string ReturningMoveName { get; internal set; } = "";
        public ReplayVector DashDirection { get; internal set; } = default;
        public float AimYaw { get; internal set; } = 0;
        public float AimPitch { get; internal set; } = 0;
        public bool LockOn { get; internal set; } = false;
        public ulong HomingTargetId { get; internal set; } = 0;
        public float FlightFuelFraction { get; internal set; } = 0;
        public ulong LockTargetId { get; internal set; } = 0;
        public int ChargeTier { get; internal set; } = -1;
        public float ChargeProgress { get; internal set; } = 0;
        public int ChargeStepsReached { get; internal set; } = 0;
        public ushort HomingCooldownLeft { get; internal set; } = 0;
        public bool HomingAirUsed { get; internal set; } = false;
        public IReadOnlyList<ReplayCooldown> Cooldowns { get; internal set; } = Array.Empty<ReplayCooldown>();

    }

    /// <summary>Actual active shape dimensions and placement. No damage/knockback/collision tuning.</summary>
    public sealed class ReplayShape
    {
        public ulong EmissionId { get; internal set; } = 0;
        // h.EmissionId
        public ulong OwnerId { get; internal set; } = 0;
        // h.OwnerId; may be Mayhem Host Entity
        public ulong ActivationId { get; internal set; } = 0;
        // h.ActivationId
        public string Kind { get; internal set; } = "unknown";
        // sphere/capsule/ring, explicit h.Shape switch
        public string SourceKind { get; internal set; } = "unknown";
        // move/host-hazard/owner-window/defense
        public ReplayVector Start { get; internal set; } = default;
        // h.X/Y/Z
        public ReplayVector End { get; internal set; } = default;
        // h.EndX/Y/Z
        public ReplayVector Velocity { get; internal set; } = default;
        // h.VX/VY/VZ
        public float Radius { get; internal set; } = 0;
        public float PreviousRadius { get; internal set; } = 0;
        public float RingHalfWidth { get; internal set; } = 0;
        public float RingHalfHeight { get; internal set; } = 0;
        public string SlotId { get; internal set; } = "";
        // roster base.ForForm(h.Form) + h.AttackSlot + h.ActivationAirborne
        public string FormId { get; internal set; } = "base";
        // h.Form
        public bool ActivationAirborne { get; internal set; } = false;
        public int VisualOperationIndex { get; internal set; } = -1;
        // Identity within this activation only; never a content lookup key.
        public byte AttackSequence { get; internal set; } = 0;
        public ushort AgeTicks { get; internal set; } = 0;
        // h.AgeTicks
        public string AttachmentBoneId { get; internal set; } = "";
        // h.SourceEvent.BoneName, empty for world projectile
        public ulong AttachedEntityId { get; internal set; } = 0;
        // h.AttachedEntityId
        public ushort FuseTicksLeft { get; internal set; } = 0;
        public string MinePhaseId { get; internal set; } = "none";
        // explicit switch h.MinePhase
        public ushort MineTicksLeft { get; internal set; } = 0;
        public string ReturnPhaseId { get; internal set; } = "none";
        // h.Return null ? none : h.ReturnLeg ? back : out
        public bool TracksOwner { get; internal set; } = false;
        // h.TracksBone
        public string VisualKindId { get; internal set; } = "unknown";
        // derived observed h flags: melee/projectile/contact-bomb/perching/host-hazard
        public float FuseProgress { get; internal set; } = 0;
        // observed fraction elapsed, 1-FuseLeft/max(1,StickyFuseTicks)
        // Perching attachment uses recorded carrier + bone.head, not SourceEvent's launch bone.
        // Current active hitboxes already include projectiles; do not separately emit the same projectile.
        public string PackageId { get; internal set; } = "";
        public string MoveName { get; internal set; } = "";
        public string PresentationId { get; internal set; } = "";
    }

    /// <summary>Placement defaults match PresentationPlacement today, permanently pinned to v1.</summary>
    public sealed class ReplayPlacement
    {
        public string AttachmentId { get; internal set; } = "world";
        // explicit placement.AttachmentMode switch
        public string BoneId { get; internal set; } = "";
        public ReplayVector LocalPosition { get; internal set; } = default;
        public ReplayVector LocalEulerDegrees { get; internal set; } = default;
        public ReplayVector LocalScale { get; internal set; } = new(1, 1, 1);
        public ushort DurationTicks { get; internal set; } = 28;
    }

    // Heterogeneous list element: tag 1 is always Kind. Codec uses only a closed switch,
    // never arbitrary CLR type names. Unknown Kind in a supported version is InvalidView.
    public abstract class ReplayEvent
    {
        public string Kind { get; internal set; } = "unknown";
        internal ReplayEvent()
        {
        }
    }
    public sealed class ReplayPresentationEvent : ReplayEvent // Kind = "presentation"
    {
        internal ReplayPresentationEvent()
        {
            Kind = "presentation";
        }
        // Project only frame.Events with MatchTick == frame.Tick; tick is inherited from parent.
        public ulong EntityId { get; internal set; } = 0;
        public byte AttackSequence { get; internal set; } = 0;
        public string SourceId { get; internal set; } = "timeline";
        // explicit PresentationEventSource switch
        public int OperationIndex { get; internal set; } = 0;
        public string PresentationId { get; internal set; } = "";
        public ReplayVector WorldPosition { get; internal set; } = default;
        public float WorldYaw { get; internal set; } = 0;
        public ReplayPlacement Placement { get; internal set; } = new();
        // Stable key = (parent tick, entity, sequence, SourceId, OperationIndex).
    }
    public sealed class ReplaySlopballEvent : ReplayEvent // Kind = "slopball"
    {
        internal ReplaySlopballEvent()
        {
            Kind = "slopball";
        }
        public string EventId { get; internal set; } = "unknown";
        // frame.SlopballEvents[].Event.Kind explicit switch
        public ulong BallId { get; internal set; } = 0;
        public ulong Returns { get; internal set; } = 0;
        public float ContactTime { get; internal set; } = 0;
        public string CampId { get; internal set; } = "";
    }
    public sealed class ReplayCourseEvent : ReplayEvent // Kind = "course"
    {
        internal ReplayCourseEvent()
        {
            Kind = "course";
        }
        public string EventId { get; internal set; } = "unknown";
        // frame.CourseEvents[].Event.Kind explicit switch
        public ulong EntityId { get; internal set; } = 0;
        public string ObjectId { get; internal set; } = "";
        // Index->arena course target/checkpoint.Id; -1 -> empty
        public ulong ElapsedTicks { get; internal set; } = 0;
        public ushort Sequence { get; internal set; } = 0;
    }
    public sealed class ReplayMayhemEvent : ReplayEvent // Kind = "mayhem"
    {
        internal ReplayMayhemEvent()
        {
            Kind = "mayhem";
        }
        public string EventId { get; internal set; } = "unknown";
        // frame.MayhemEvents[].Kind explicit switch
        public ulong EntityId { get; internal set; } = 0;
        public ushort RoundIndex { get; internal set; } = 0;
        // round number is a counter, not Card identity
        public int OperationIndex { get; internal set; } = 0;
        public ushort Points { get; internal set; } = 0;
        // awarded result
    }

    /// <summary>Observed accepted contact; no base knockback, growth, stun formula or slot ordinals.</summary>
    public sealed class ReplayHit
    {
        public ushort Occurrence { get; internal set; } = 0;
        // ordinal among this tick's accepted contacts
        public ulong OwnerId { get; internal set; } = 0;
        // frame.Hits owner
        public ulong TargetId { get; internal set; } = 0;
        public ulong ActivationId { get; internal set; } = 0;
        public byte AttackSequence { get; internal set; } = 0;
        public string SlotId { get; internal set; } = "";
        // base def + hit.Form, hit.AttackSlot, hit.Airborne
        public string FormId { get; internal set; } = "base";
        public bool ActivationAirborne { get; internal set; } = false;
        public ReplayVector Position { get; internal set; } = default;
        // HitX/Y/Z
        public float ContactTime { get; internal set; } = 0;
        // [0,1]
        public float AppliedDamage { get; internal set; } = 0;
        // hit.Damage, observed accepted outcome
        public float ImpactForce { get; internal set; } = 0;
        // resolved result, not tuning
        public ReplayVector LaunchDirection { get; internal set; } = default;
        // recorded DirX/DirZ + resolved angle -> unit vector
        public ushort HitstopTicks { get; internal set; } = 0;
        public bool Blocked { get; internal set; } = false;
        public bool ArmorProtected { get; internal set; } = false;
        public bool FreezesOwner { get; internal set; } = false;
        public string PresentationId { get; internal set; } = "";
        // optional decoration from matching sim.LastTickHits
        public string SourceKind { get; internal set; } = "unknown";
        // move/host-hazard/owner-window/defense
        public bool Unblockable { get; internal set; } = false;
        // Presentation event owns effect emission. Do not emit this ID a second time.
        public string PackageId { get; internal set; } = "";
        public string MoveName { get; internal set; } = "";
        public string ReturnPhaseId { get; internal set; } = "none";
    }
    public sealed class ReplayDeath
    {
        // From LastTickDeaths; never from post-respawn frame position or deaths delta alone.
        public ulong EntityId { get; internal set; } = 0;
        public ReplayVector Position { get; internal set; } = default;
        // death.State.PX/PY/PZ
        public ReplayVector Velocity { get; internal set; } = default;
        // death.State.VX/VY/VZ
        public ReplayVector KnockbackVelocity { get; internal set; } = default;
        // death.State.KV*
        public string BoundaryId { get; internal set; } = "";
        // bottom/top/minX/maxX/minZ/maxZ pinned v1 strings
        public ulong KillerId { get; internal set; } = 0;
        public ulong LastHitOwnerId { get; internal set; } = 0;
        public uint LastHitTick { get; internal set; } = 0;
        public string LastHitSlotId { get; internal set; } = "";
        // unavailable old form -> empty, join earlier ViewHit
        public ushort DamagePercent { get; internal set; } = 0;
        // death.State.DamagePercent
        public string LastHitPackageId { get; internal set; } = "";
        public string LastHitFormId { get; internal set; } = "";
        public string LastHitMoveName { get; internal set; } = "";
    }

    public abstract class ReplayModeObject
    {
        public string Kind { get; internal set; } = "unknown";
        internal ReplayModeObject()
        {
        }
    }
    public sealed class ReplayBall : ReplayModeObject // Kind="ball", sim.ArcadeBall
    {
        internal ReplayBall()
        {
            Kind = "ball";
        }
        public ulong EntityId { get; internal set; } = 0;
        public ReplayVector Position { get; internal set; } = default;
        public ReplayVector Velocity { get; internal set; } = default;
        public float Radius { get; internal set; } = 0;
        public float DamagePercent { get; internal set; } = 0;
        public ushort HitstopTicks { get; internal set; } = 0;
        public bool Active { get; internal set; } = false;
        public float Curve { get; internal set; } = 0;
    }
    public sealed class ReplaySlopballRun : ReplayModeObject // Kind="slopball-run", sim.Slopball
    {
        internal ReplaySlopballRun()
        {
            Kind = "slopball-run";
        }
        public string PhaseId { get; internal set; } = "unknown";
        public ulong Returns { get; internal set; } = 0;
        public ulong BestStreak { get; internal set; } = 0;
        public ulong TotalReturns { get; internal set; } = 0;
        public ushort ServeInTicks { get; internal set; } = 0;
        public ushort CountdownTicks { get; internal set; } = 0;
        public bool Received { get; internal set; } = false;
        public bool ReturnArmed { get; internal set; } = false;
        public bool ClearedNet { get; internal set; } = false;
        public string ShotId { get; internal set; } = "unknown";
        public byte Misses { get; internal set; } = 0;
        // No seed, difficulty formula, miss budget or shot tuning is necessary for display.
    }
    public sealed class ReplaySlopballVersus : ReplayModeObject // Kind="slopball-versus", sim.SlopballVersus
    {
        internal ReplaySlopballVersus()
        {
            Kind = "slopball-versus";
        }
        public string PhaseId { get; internal set; } = "unknown";
        public ushort TealScore { get; internal set; } = 0;
        public ushort ClayScore { get; internal set; } = 0;
        public string ServingCampId { get; internal set; } = "";
        public ulong ServerEntityId { get; internal set; } = 0;
        public ushort ServeInTicks { get; internal set; } = 0;
        public string WinningCampId { get; internal set; } = "";
    }
    public sealed class ReplayCourse : ReplayModeObject // Kind="course", sim.OnlineCourse + sim.Scramble?
    {
        internal ReplayCourse()
        {
            Kind = "course";
        }
        public string FormatId { get; internal set; } = "unknown";
        // race/coop/scramble
        public ulong ElapsedTicks { get; internal set; } = 0;
        public ushort FinishWindowTicksLeft { get; internal set; } = 0;
        public bool Ended { get; internal set; } = false;
        public ulong WinnerId { get; internal set; } = 0;
        public ulong LeaderId { get; internal set; } = 0;
        // Scramble.LeaderEntityId
        public ReplayVector SafePosition { get; internal set; } = default;
        // Scramble.SafeGround.Position
        public float SafeYaw { get; internal set; } = 0;
        public float SafeProgress { get; internal set; } = 0;
    }
    public sealed class ReplayCoursePlayer : ReplayModeObject // Kind="course-player", each sim.CoursePlayers
    {
        internal ReplayCoursePlayer()
        {
            Kind = "course-player";
        }
        public ulong EntityId { get; internal set; } = 0;
        public string PhaseId { get; internal set; } = "unknown";
        // p.Course.Phase
        public ulong ElapsedTicks { get; internal set; } = 0;
        public int BrokenTargets { get; internal set; } = 0;
        public int TotalTargets { get; internal set; } = 0;
        public string SavedCheckpointId { get; internal set; } = "";
        // arena checkpoint ID; index -1 -> empty
        public ushort CountdownTicks { get; internal set; } = 0;
        public byte Place { get; internal set; } = 0;
        public bool Withdrawn { get; internal set; } = false;
        public ushort Points { get; internal set; } = 0;
        public ushort LeashTicks { get; internal set; } = 0;
        public float Progress { get; internal set; } = 0;
    }
    public sealed class ReplayCourseTarget : ReplayModeObject // Kind="course-target", per fighter x arena.Course.Targets
    {
        internal ReplayCourseTarget()
        {
            Kind = "course-target";
        }
        public ulong EntityId { get; internal set; } = 0;
        // sim.CourseTargetEntityId(owner,index)
        public ulong PerspectiveEntityId { get; internal set; } = 0;
        // owner; private race sets differ
        public string TargetId { get; internal set; } = "";
        // arena.Course.Targets[index].Id
        public ReplayVector Position { get; internal set; } = default;
        // target.Position
        public float Radius { get; internal set; } = 0;
        // current displayed geometry, not a tuning formula
        public bool Broken { get; internal set; } = false;
        // sim.IsCourseTargetBroken(owner,index)
        public int HitsLeft { get; internal set; } = 0;
        // sim.CourseTargetHitsLeft(owner,index)
        public ulong BreakerId { get; internal set; } = 0;
        // sim.CourseTargetBreaker(index), scramble only
    }
    public sealed class ReplayMayhem : ReplayModeObject // Kind="mayhem", sim.MankiMayhem
    {
        internal ReplayMayhem()
        {
            Kind = "mayhem";
        }
        public ushort RoundIndex { get; internal set; } = 0;
        public string CardId { get; internal set; } = "unknown";
        // scatter/jump/break-out, explicit switch
        public string PhaseId { get; internal set; } = "unknown";
        public ushort PhaseTick { get; internal set; } = 0;
        public int HazardsLaunched { get; internal set; } = 0;
    }
    public sealed class ReplayMayhemPlayer : ReplayModeObject // Kind="mayhem-player", sim.MayhemPlayers
    {
        internal ReplayMayhemPlayer()
        {
            Kind = "mayhem-player";
        }
        public ulong EntityId { get; internal set; } = 0;
        public ushort Score { get; internal set; } = 0;
        public ushort RoundsSurvived { get; internal set; } = 0;
        public ushort Bonuses { get; internal set; } = 0;
        public bool Benched { get; internal set; } = false;
        public bool Withdrawn { get; internal set; } = false;
    }
    public sealed class ReplayMayhemRing : ReplayModeObject // Kind="mayhem-ring", sim.MayhemLandingRings
    {
        internal ReplayMayhemRing()
        {
            Kind = "mayhem-ring";
        }
        public int Ordinal { get; internal set; } = 0;
        public ReplayVector Position { get; internal set; } = default;
        public uint LaunchTick { get; internal set; } = 0;
        public uint LandingTick { get; internal set; } = 0;
        public ushort FuseTicks { get; internal set; } = 0;
        // observed telegraph lifetime, not recomputed tuning
    }
    public sealed class ReplayMayhemSlam : ReplayModeObject // Kind="mayhem-slam", sim.MayhemSlams
    {
        internal ReplayMayhemSlam()
        {
            Kind = "mayhem-slam";
        }
        public int Ordinal { get; internal set; } = 0;
        public ReplayVector Position { get; internal set; } = default;
        public uint Tick { get; internal set; } = 0;
    }
    public sealed class ReplayMayhemWall : ReplayModeObject // Kind="mayhem-wall", sim.MayhemWalls
    {
        internal ReplayMayhemWall()
        {
            Kind = "mayhem-wall";
        }
        public ulong EntityId { get; internal set; } = 0;
        public ulong CageEntityId { get; internal set; } = 0;
        public ReplayVector CageCenter { get; internal set; } = default;
        public ReplayVector Center { get; internal set; } = default;
        public ReplayVector HalfExtents { get; internal set; } = default;
        public float Yaw { get; internal set; } = 0;
        public byte HitsLeft { get; internal set; } = 0;
        public string PhaseId { get; internal set; } = "unknown";
        public ushort PhaseTick { get; internal set; } = 0;
    }
}

namespace SlopArena.Shared.Replays
{
    public sealed class ReplayCooldown
    {
        public string PackageId { get; internal set; } = "";
        public string FormId { get; internal set; } = "base";
        public string SlotId { get; internal set; } = "";
        public string MoveName { get; internal set; } = "";
        public ushort TicksLeft { get; internal set; } = 0;
        public ushort DurationTicks { get; internal set; } = 0;
    }
}
