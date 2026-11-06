using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using static SlopArena.Shared.Replays.ReplayFields;
namespace SlopArena.Shared.Replays;

internal static class ReplayViewCodec
{
    internal const int MaxRecord = 512 * 1024;
    internal static byte[] Encode(ReplayViewTick tick)
    {
        var bytes = ReplayBinary.Bytes(w => { w.Write(tick.Tick); ReplayViewTickTable.WriteTo(w, tick); });
        if (bytes.Length > MaxRecord) throw new InvalidDataException("View bound");
        return bytes;
    }
    internal static ReplayViewTick Decode(byte[] bytes, uint tick)
    {
        using var r = ReplayBinary.Reader(bytes);
        if (r.ReadUInt32() != tick) throw new InvalidDataException("View tick");
        var v = ReadReplayViewTick(r.ReadBytes(bytes.Length - 4));
        v.Tick = tick;
        return v;
    }
    internal static readonly Table<ReplayViewTick> ReplayViewTickTable = new(() => new(),
        Field<ReplayViewTick, string>(1, Text, x => x.MatchPhase, (x, v) => { x.MatchPhase = v; return x; }, nameof(ReplayViewTick.MatchPhase)),
        Field<ReplayViewTick, IReadOnlyList<ReplayFighter>>(2, List(Obj((w, x) => ReplayFighterTable!.WriteTo(w, x), ReadReplayFighter), 4), x => x.Fighters, (x, v) => { x.Fighters = v; return x; }, nameof(ReplayViewTick.Fighters)),
        Field<ReplayViewTick, IReadOnlyList<ReplayShape>>(3, List(Obj((w, x) => ReplayShapeTable!.WriteTo(w, x), ReadReplayShape), 4096), x => x.Shapes, (x, v) => { x.Shapes = v; return x; }, nameof(ReplayViewTick.Shapes)),
        Field<ReplayViewTick, IReadOnlyList<ReplayModeObject>>(4, List(Obj((w, x) => ReplayModeObjectKinds!.WriteTo(w, x), ReadReplayModeObject), 2048), x => x.ModeObjects, (x, v) => { x.ModeObjects = v; return x; }, nameof(ReplayViewTick.ModeObjects)),
        Field<ReplayViewTick, IReadOnlyList<ReplayEvent>>(5, List(Obj((w, x) => ReplayEventKinds!.WriteTo(w, x), ReadReplayEvent), 2048), x => x.Events, (x, v) => { x.Events = v; return x; }, nameof(ReplayViewTick.Events)),
        Field<ReplayViewTick, IReadOnlyList<ReplayHit>>(6, List(Obj((w, x) => ReplayHitTable!.WriteTo(w, x), ReadReplayHit), 128), x => x.Hits, (x, v) => { x.Hits = v; return x; }, nameof(ReplayViewTick.Hits)),
        Field<ReplayViewTick, IReadOnlyList<ReplayDeath>>(7, List(Obj((w, x) => ReplayDeathTable!.WriteTo(w, x), ReadReplayDeath), 4), x => x.Deaths, (x, v) => { x.Deaths = v; return x; }, nameof(ReplayViewTick.Deaths)));
    internal static byte[] WriteReplayViewTick(ReplayViewTick x) => ReplayViewTickTable.Write(x);
    internal static ReplayViewTick ReadReplayViewTick(byte[] b) => ReplayViewTickTable.Read(b);
    internal static readonly Table<ReplayFighter> ReplayFighterTable = new(() => new(),
        Field<ReplayFighter, ulong>(1, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayFighter.EntityId)),
        Field<ReplayFighter, string>(2, Text, x => x.ActionId, (x, v) => { x.ActionId = v; return x; }, nameof(ReplayFighter.ActionId)),
        Field<ReplayFighter, ReplayVector>(3, Vec, x => x.Position, (x, v) => { x.Position = v; return x; }, nameof(ReplayFighter.Position)),
        Field<ReplayFighter, ReplayVector>(4, Vec, x => x.Velocity, (x, v) => { x.Velocity = v; return x; }, nameof(ReplayFighter.Velocity)),
        Field<ReplayFighter, ReplayVector>(5, Vec, x => x.KnockbackVelocity, (x, v) => { x.KnockbackVelocity = v; return x; }, nameof(ReplayFighter.KnockbackVelocity)),
        Field<ReplayFighter, float>(6, F32, x => x.FacingYaw, (x, v) => { x.FacingYaw = v; return x; }, nameof(ReplayFighter.FacingYaw)),
        Field<ReplayFighter, bool>(7, Bool, x => x.Grounded, (x, v) => { x.Grounded = v; return x; }, nameof(ReplayFighter.Grounded)),
        Field<ReplayFighter, ushort>(8, U16, x => x.ActionTicks, (x, v) => { x.ActionTicks = v; return x; }, nameof(ReplayFighter.ActionTicks)),
        Field<ReplayFighter, ushort>(9, U16, x => x.DamagePercent, (x, v) => { x.DamagePercent = v; return x; }, nameof(ReplayFighter.DamagePercent)),
        Field<ReplayFighter, byte>(10, U8, x => x.Deaths, (x, v) => { x.Deaths = v; return x; }, nameof(ReplayFighter.Deaths)),
        Field<ReplayFighter, byte>(11, U8, x => x.KOs, (x, v) => { x.KOs = v; return x; }, nameof(ReplayFighter.KOs)),
        Field<ReplayFighter, byte>(12, U8, x => x.JumpsLeft, (x, v) => { x.JumpsLeft = v; return x; }, nameof(ReplayFighter.JumpsLeft)),
        Field<ReplayFighter, bool>(13, Bool, x => x.WallJumpUsed, (x, v) => { x.WallJumpUsed = v; return x; }, nameof(ReplayFighter.WallJumpUsed)),
        Field<ReplayFighter, ushort>(14, U16, x => x.InvincibilityTicks, (x, v) => { x.InvincibilityTicks = v; return x; }, nameof(ReplayFighter.InvincibilityTicks)),
        Field<ReplayFighter, ushort>(15, U16, x => x.LandingLagTicks, (x, v) => { x.LandingLagTicks = v; return x; }, nameof(ReplayFighter.LandingLagTicks)),
        Field<ReplayFighter, ushort>(16, U16, x => x.HitstunTicks, (x, v) => { x.HitstunTicks = v; return x; }, nameof(ReplayFighter.HitstunTicks)),
        Field<ReplayFighter, ushort>(17, U16, x => x.HitstopTicks, (x, v) => { x.HitstopTicks = v; return x; }, nameof(ReplayFighter.HitstopTicks)),
        Field<ReplayFighter, string>(18, Text, x => x.HitstunId, (x, v) => { x.HitstunId = v; return x; }, nameof(ReplayFighter.HitstunId)),
        Field<ReplayFighter, ushort>(19, U16, x => x.HitReactionSequence, (x, v) => { x.HitReactionSequence = v; return x; }, nameof(ReplayFighter.HitReactionSequence)),
        Field<ReplayFighter, bool>(20, Bool, x => x.WasAirborneDuringKnockback, (x, v) => { x.WasAirborneDuringKnockback = v; return x; }, nameof(ReplayFighter.WasAirborneDuringKnockback)),
        Field<ReplayFighter, bool>(21, Bool, x => x.PostHitstunFlight, (x, v) => { x.PostHitstunFlight = v; return x; }, nameof(ReplayFighter.PostHitstunFlight)),
        Field<ReplayFighter, string>(22, Text, x => x.FormId, (x, v) => { x.FormId = v; return x; }, nameof(ReplayFighter.FormId)),
        Field<ReplayFighter, string>(23, Text, x => x.SlotId, (x, v) => { x.SlotId = v; return x; }, nameof(ReplayFighter.SlotId)),
        Field<ReplayFighter, ulong>(24, U64, x => x.ActivationId, (x, v) => { x.ActivationId = v; return x; }, nameof(ReplayFighter.ActivationId)),
        Field<ReplayFighter, byte>(25, U8, x => x.AttackSequence, (x, v) => { x.AttackSequence = v; return x; }, nameof(ReplayFighter.AttackSequence)),
        Field<ReplayFighter, bool>(26, Bool, x => x.ActivationAirborne, (x, v) => { x.ActivationAirborne = v; return x; }, nameof(ReplayFighter.ActivationAirborne)),
        Field<ReplayFighter, string>(27, Text, x => x.MoveFormId, (x, v) => { x.MoveFormId = v; return x; }, nameof(ReplayFighter.MoveFormId)),
        Field<ReplayFighter, ushort>(28, U16, x => x.MoveElapsedTicks, (x, v) => { x.MoveElapsedTicks = v; return x; }, nameof(ReplayFighter.MoveElapsedTicks)),
        Field<ReplayFighter, string>(29, Text, x => x.PoseAnimationId, (x, v) => { x.PoseAnimationId = v; return x; }, nameof(ReplayFighter.PoseAnimationId)),
        Field<ReplayFighter, float>(30, F32, x => x.PoseElapsedTicks, (x, v) => { x.PoseElapsedTicks = v; return x; }, nameof(ReplayFighter.PoseElapsedTicks)),
        Field<ReplayFighter, ushort>(31, U16, x => x.PoseDurationTicks, (x, v) => { x.PoseDurationTicks = v; return x; }, nameof(ReplayFighter.PoseDurationTicks)),
        Field<ReplayFighter, bool>(32, Bool, x => x.PhaseClip, (x, v) => { x.PhaseClip = v; return x; }, nameof(ReplayFighter.PhaseClip)),
        Field<ReplayFighter, float>(33, F32, x => x.PosePitch, (x, v) => { x.PosePitch = v; return x; }, nameof(ReplayFighter.PosePitch)),
        Field<ReplayFighter, ushort>(34, U16, x => x.ChargeTicks, (x, v) => { x.ChargeTicks = v; return x; }, nameof(ReplayFighter.ChargeTicks)),
        Field<ReplayFighter, string>(35, Text, x => x.AimAnimationId, (x, v) => { x.AimAnimationId = v; return x; }, nameof(ReplayFighter.AimAnimationId)),
        Field<ReplayFighter, string>(36, Text, x => x.AimMovementId, (x, v) => { x.AimMovementId = v; return x; }, nameof(ReplayFighter.AimMovementId)),
        Field<ReplayFighter, string>(37, Text, x => x.ReturningSlotId, (x, v) => { x.ReturningSlotId = v; return x; }, nameof(ReplayFighter.ReturningSlotId)),
        Field<ReplayFighter, bool>(38, Bool, x => x.FlightActive, (x, v) => { x.FlightActive = v; return x; }, nameof(ReplayFighter.FlightActive)),
        Field<ReplayFighter, ushort>(39, U16, x => x.FlightFuelLeft, (x, v) => { x.FlightFuelLeft = v; return x; }, nameof(ReplayFighter.FlightFuelLeft)),
        Field<ReplayFighter, ushort>(40, U16, x => x.FlightBurstLeft, (x, v) => { x.FlightBurstLeft = v; return x; }, nameof(ReplayFighter.FlightBurstLeft)),
        Field<ReplayFighter, float>(41, F32, x => x.FlightLiftSpeed, (x, v) => { x.FlightLiftSpeed = v; return x; }, nameof(ReplayFighter.FlightLiftSpeed)),
        Field<ReplayFighter, float>(42, F32, x => x.FlightHeadingYaw, (x, v) => { x.FlightHeadingYaw = v; return x; }, nameof(ReplayFighter.FlightHeadingYaw)),
        Field<ReplayFighter, float>(43, F32, x => x.FlightElapsedTicks, (x, v) => { x.FlightElapsedTicks = v; return x; }, nameof(ReplayFighter.FlightElapsedTicks)),
        Field<ReplayFighter, string>(44, Text, x => x.InteractionPhaseId, (x, v) => { x.InteractionPhaseId = v; return x; }, nameof(ReplayFighter.InteractionPhaseId)),
        Field<ReplayFighter, ulong>(45, U64, x => x.InteractionId, (x, v) => { x.InteractionId = v; return x; }, nameof(ReplayFighter.InteractionId)),
        Field<ReplayFighter, ulong>(46, U64, x => x.InteractionPartnerId, (x, v) => { x.InteractionPartnerId = v; return x; }, nameof(ReplayFighter.InteractionPartnerId)),
        Field<ReplayFighter, float>(47, F32, x => x.CapturedYaw, (x, v) => { x.CapturedYaw = v; return x; }, nameof(ReplayFighter.CapturedYaw)),
        Field<ReplayFighter, bool>(48, Bool, x => x.Inactive, (x, v) => { x.Inactive = v; return x; }, nameof(ReplayFighter.Inactive)),
        Field<ReplayFighter, bool>(49, Bool, x => x.FastFalling, (x, v) => { x.FastFalling = v; return x; }, nameof(ReplayFighter.FastFalling)),
        Field<ReplayFighter, string>(50, Text, x => x.PackageId, (x, v) => { x.PackageId = v; return x; }, nameof(ReplayFighter.PackageId)),
        Field<ReplayFighter, string>(51, Text, x => x.MoveName, (x, v) => { x.MoveName = v; return x; }, nameof(ReplayFighter.MoveName)),
        Field<ReplayFighter, string>(52, Text, x => x.ReturningPackageId, (x, v) => { x.ReturningPackageId = v; return x; }, nameof(ReplayFighter.ReturningPackageId)),
        Field<ReplayFighter, string>(53, Text, x => x.ReturningFormId, (x, v) => { x.ReturningFormId = v; return x; }, nameof(ReplayFighter.ReturningFormId)),
        Field<ReplayFighter, string>(54, Text, x => x.ReturningMoveName, (x, v) => { x.ReturningMoveName = v; return x; }, nameof(ReplayFighter.ReturningMoveName)),
        Field<ReplayFighter, ReplayVector>(55, Vec, x => x.DashDirection, (x, v) => { x.DashDirection = v; return x; }, nameof(ReplayFighter.DashDirection)),
        Field<ReplayFighter, float>(56, F32, x => x.AimYaw, (x, v) => { x.AimYaw = v; return x; }, nameof(ReplayFighter.AimYaw)),
        Field<ReplayFighter, float>(57, F32, x => x.AimPitch, (x, v) => { x.AimPitch = v; return x; }, nameof(ReplayFighter.AimPitch)),
        Field<ReplayFighter, bool>(58, Bool, x => x.LockOn, (x, v) => { x.LockOn = v; return x; }, nameof(ReplayFighter.LockOn)),
        Field<ReplayFighter, ulong>(59, U64, x => x.HomingTargetId, (x, v) => { x.HomingTargetId = v; return x; }, nameof(ReplayFighter.HomingTargetId)),
        Field<ReplayFighter, float>(60, F32, x => x.FlightFuelFraction, (x, v) => { x.FlightFuelFraction = v; return x; }, nameof(ReplayFighter.FlightFuelFraction)),
        Field<ReplayFighter, ulong>(61, U64, x => x.LockTargetId, (x, v) => { x.LockTargetId = v; return x; }, nameof(ReplayFighter.LockTargetId)),
        Field<ReplayFighter, int>(62, I32, x => x.ChargeTier, (x, v) => { x.ChargeTier = v; return x; }, nameof(ReplayFighter.ChargeTier)),
        Field<ReplayFighter, float>(63, F32, x => x.ChargeProgress, (x, v) => { x.ChargeProgress = v; return x; }, nameof(ReplayFighter.ChargeProgress)),
        Field<ReplayFighter, int>(64, I32, x => x.ChargeStepsReached, (x, v) => { x.ChargeStepsReached = v; return x; }, nameof(ReplayFighter.ChargeStepsReached)),
        Field<ReplayFighter, ushort>(65, U16, x => x.HomingCooldownLeft, (x, v) => { x.HomingCooldownLeft = v; return x; }, nameof(ReplayFighter.HomingCooldownLeft)),
        Field<ReplayFighter, bool>(66, Bool, x => x.HomingAirUsed, (x, v) => { x.HomingAirUsed = v; return x; }, nameof(ReplayFighter.HomingAirUsed)),
        Field<ReplayFighter, IReadOnlyList<ReplayCooldown>>(67, List(Obj((w, x) => ReplayCooldownTable!.WriteTo(w, x), ReadReplayCooldown), 14), x => x.Cooldowns, (x, v) => { x.Cooldowns = v; return x; }, nameof(ReplayFighter.Cooldowns)));
    internal static byte[] WriteReplayFighter(ReplayFighter x) => ReplayFighterTable.Write(x);
    internal static ReplayFighter ReadReplayFighter(byte[] b) => ReplayFighterTable.Read(b);
    internal static readonly Table<ReplayShape> ReplayShapeTable = new(() => new(),
        Field<ReplayShape, ulong>(1, U64, x => x.EmissionId, (x, v) => { x.EmissionId = v; return x; }, nameof(ReplayShape.EmissionId)),
        Field<ReplayShape, ulong>(2, U64, x => x.OwnerId, (x, v) => { x.OwnerId = v; return x; }, nameof(ReplayShape.OwnerId)),
        Field<ReplayShape, ulong>(3, U64, x => x.ActivationId, (x, v) => { x.ActivationId = v; return x; }, nameof(ReplayShape.ActivationId)),
        Field<ReplayShape, string>(4, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayShape.Kind)),
        Field<ReplayShape, string>(5, Text, x => x.SourceKind, (x, v) => { x.SourceKind = v; return x; }, nameof(ReplayShape.SourceKind)),
        Field<ReplayShape, ReplayVector>(6, Vec, x => x.Start, (x, v) => { x.Start = v; return x; }, nameof(ReplayShape.Start)),
        Field<ReplayShape, ReplayVector>(7, Vec, x => x.End, (x, v) => { x.End = v; return x; }, nameof(ReplayShape.End)),
        Field<ReplayShape, ReplayVector>(8, Vec, x => x.Velocity, (x, v) => { x.Velocity = v; return x; }, nameof(ReplayShape.Velocity)),
        Field<ReplayShape, float>(9, F32, x => x.Radius, (x, v) => { x.Radius = v; return x; }, nameof(ReplayShape.Radius)),
        Field<ReplayShape, float>(10, F32, x => x.PreviousRadius, (x, v) => { x.PreviousRadius = v; return x; }, nameof(ReplayShape.PreviousRadius)),
        Field<ReplayShape, float>(11, F32, x => x.RingHalfWidth, (x, v) => { x.RingHalfWidth = v; return x; }, nameof(ReplayShape.RingHalfWidth)),
        Field<ReplayShape, float>(12, F32, x => x.RingHalfHeight, (x, v) => { x.RingHalfHeight = v; return x; }, nameof(ReplayShape.RingHalfHeight)),
        Field<ReplayShape, string>(13, Text, x => x.SlotId, (x, v) => { x.SlotId = v; return x; }, nameof(ReplayShape.SlotId)),
        Field<ReplayShape, string>(14, Text, x => x.FormId, (x, v) => { x.FormId = v; return x; }, nameof(ReplayShape.FormId)),
        Field<ReplayShape, bool>(15, Bool, x => x.ActivationAirborne, (x, v) => { x.ActivationAirborne = v; return x; }, nameof(ReplayShape.ActivationAirborne)),
        Field<ReplayShape, int>(16, I32, x => x.VisualOperationIndex, (x, v) => { x.VisualOperationIndex = v; return x; }, nameof(ReplayShape.VisualOperationIndex)),
        Field<ReplayShape, byte>(17, U8, x => x.AttackSequence, (x, v) => { x.AttackSequence = v; return x; }, nameof(ReplayShape.AttackSequence)),
        Field<ReplayShape, ushort>(18, U16, x => x.AgeTicks, (x, v) => { x.AgeTicks = v; return x; }, nameof(ReplayShape.AgeTicks)),
        Field<ReplayShape, string>(19, Text, x => x.AttachmentBoneId, (x, v) => { x.AttachmentBoneId = v; return x; }, nameof(ReplayShape.AttachmentBoneId)),
        Field<ReplayShape, ulong>(20, U64, x => x.AttachedEntityId, (x, v) => { x.AttachedEntityId = v; return x; }, nameof(ReplayShape.AttachedEntityId)),
        Field<ReplayShape, ushort>(21, U16, x => x.FuseTicksLeft, (x, v) => { x.FuseTicksLeft = v; return x; }, nameof(ReplayShape.FuseTicksLeft)),
        Field<ReplayShape, string>(22, Text, x => x.MinePhaseId, (x, v) => { x.MinePhaseId = v; return x; }, nameof(ReplayShape.MinePhaseId)),
        Field<ReplayShape, ushort>(23, U16, x => x.MineTicksLeft, (x, v) => { x.MineTicksLeft = v; return x; }, nameof(ReplayShape.MineTicksLeft)),
        Field<ReplayShape, string>(24, Text, x => x.ReturnPhaseId, (x, v) => { x.ReturnPhaseId = v; return x; }, nameof(ReplayShape.ReturnPhaseId)),
        Field<ReplayShape, bool>(25, Bool, x => x.TracksOwner, (x, v) => { x.TracksOwner = v; return x; }, nameof(ReplayShape.TracksOwner)),
        Field<ReplayShape, string>(26, Text, x => x.VisualKindId, (x, v) => { x.VisualKindId = v; return x; }, nameof(ReplayShape.VisualKindId)),
        Field<ReplayShape, float>(27, F32, x => x.FuseProgress, (x, v) => { x.FuseProgress = v; return x; }, nameof(ReplayShape.FuseProgress)),
        Field<ReplayShape, string>(28, Text, x => x.PackageId, (x, v) => { x.PackageId = v; return x; }, nameof(ReplayShape.PackageId)),
        Field<ReplayShape, string>(29, Text, x => x.MoveName, (x, v) => { x.MoveName = v; return x; }, nameof(ReplayShape.MoveName)),
        Field<ReplayShape, string>(30, Text, x => x.PresentationId, (x, v) => { x.PresentationId = v; return x; }, nameof(ReplayShape.PresentationId)));
    internal static byte[] WriteReplayShape(ReplayShape x) => ReplayShapeTable.Write(x);
    internal static ReplayShape ReadReplayShape(byte[] b) => ReplayShapeTable.Read(b);
    internal static readonly Table<ReplayPlacement> ReplayPlacementTable = new(() => new(),
        Field<ReplayPlacement, string>(1, Text, x => x.AttachmentId, (x, v) => { x.AttachmentId = v; return x; }, nameof(ReplayPlacement.AttachmentId)),
        Field<ReplayPlacement, string>(2, Text, x => x.BoneId, (x, v) => { x.BoneId = v; return x; }, nameof(ReplayPlacement.BoneId)),
        Field<ReplayPlacement, ReplayVector>(3, Vec, x => x.LocalPosition, (x, v) => { x.LocalPosition = v; return x; }, nameof(ReplayPlacement.LocalPosition)),
        Field<ReplayPlacement, ReplayVector>(4, Vec, x => x.LocalEulerDegrees, (x, v) => { x.LocalEulerDegrees = v; return x; }, nameof(ReplayPlacement.LocalEulerDegrees)),
        Field<ReplayPlacement, ReplayVector>(5, Vec, x => x.LocalScale, (x, v) => { x.LocalScale = v; return x; }, nameof(ReplayPlacement.LocalScale)),
        Field<ReplayPlacement, ushort>(6, U16, x => x.DurationTicks, (x, v) => { x.DurationTicks = v; return x; }, nameof(ReplayPlacement.DurationTicks)));
    internal static byte[] WriteReplayPlacement(ReplayPlacement x) => ReplayPlacementTable.Write(x);
    internal static ReplayPlacement ReadReplayPlacement(byte[] b) => ReplayPlacementTable.Read(b);
    internal static readonly Table<ReplayPresentationEvent> ReplayPresentationEventTable = new(() => new(),
        Field<ReplayPresentationEvent, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayPresentationEvent.Kind)),
        Field<ReplayPresentationEvent, ulong>(2, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayPresentationEvent.EntityId)),
        Field<ReplayPresentationEvent, byte>(3, U8, x => x.AttackSequence, (x, v) => { x.AttackSequence = v; return x; }, nameof(ReplayPresentationEvent.AttackSequence)),
        Field<ReplayPresentationEvent, string>(4, Text, x => x.SourceId, (x, v) => { x.SourceId = v; return x; }, nameof(ReplayPresentationEvent.SourceId)),
        Field<ReplayPresentationEvent, int>(5, I32, x => x.OperationIndex, (x, v) => { x.OperationIndex = v; return x; }, nameof(ReplayPresentationEvent.OperationIndex)),
        Field<ReplayPresentationEvent, string>(6, Text, x => x.PresentationId, (x, v) => { x.PresentationId = v; return x; }, nameof(ReplayPresentationEvent.PresentationId)),
        Field<ReplayPresentationEvent, ReplayVector>(7, Vec, x => x.WorldPosition, (x, v) => { x.WorldPosition = v; return x; }, nameof(ReplayPresentationEvent.WorldPosition)),
        Field<ReplayPresentationEvent, float>(8, F32, x => x.WorldYaw, (x, v) => { x.WorldYaw = v; return x; }, nameof(ReplayPresentationEvent.WorldYaw)),
        Field<ReplayPresentationEvent, ReplayPlacement>(9, Obj((w, x) => ReplayPlacementTable!.WriteTo(w, x), ReadReplayPlacement), x => x.Placement, (x, v) => { x.Placement = v; return x; }, nameof(ReplayPresentationEvent.Placement)));
    internal static byte[] WriteReplayPresentationEvent(ReplayPresentationEvent x) => ReplayPresentationEventTable.Write(x);
    internal static ReplayPresentationEvent ReadReplayPresentationEvent(byte[] b) => ReplayPresentationEventTable.Read(b);
    internal static readonly Table<ReplaySlopballEvent> ReplaySlopballEventTable = new(() => new(),
        Field<ReplaySlopballEvent, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplaySlopballEvent.Kind)),
        Field<ReplaySlopballEvent, string>(2, Text, x => x.EventId, (x, v) => { x.EventId = v; return x; }, nameof(ReplaySlopballEvent.EventId)),
        Field<ReplaySlopballEvent, ulong>(3, U64, x => x.BallId, (x, v) => { x.BallId = v; return x; }, nameof(ReplaySlopballEvent.BallId)),
        Field<ReplaySlopballEvent, ulong>(4, U64, x => x.Returns, (x, v) => { x.Returns = v; return x; }, nameof(ReplaySlopballEvent.Returns)),
        Field<ReplaySlopballEvent, float>(5, F32, x => x.ContactTime, (x, v) => { x.ContactTime = v; return x; }, nameof(ReplaySlopballEvent.ContactTime)),
        Field<ReplaySlopballEvent, string>(6, Text, x => x.CampId, (x, v) => { x.CampId = v; return x; }, nameof(ReplaySlopballEvent.CampId)));
    internal static byte[] WriteReplaySlopballEvent(ReplaySlopballEvent x) => ReplaySlopballEventTable.Write(x);
    internal static ReplaySlopballEvent ReadReplaySlopballEvent(byte[] b) => ReplaySlopballEventTable.Read(b);
    internal static readonly Table<ReplayCourseEvent> ReplayCourseEventTable = new(() => new(),
        Field<ReplayCourseEvent, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayCourseEvent.Kind)),
        Field<ReplayCourseEvent, string>(2, Text, x => x.EventId, (x, v) => { x.EventId = v; return x; }, nameof(ReplayCourseEvent.EventId)),
        Field<ReplayCourseEvent, ulong>(3, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayCourseEvent.EntityId)),
        Field<ReplayCourseEvent, string>(4, Text, x => x.ObjectId, (x, v) => { x.ObjectId = v; return x; }, nameof(ReplayCourseEvent.ObjectId)),
        Field<ReplayCourseEvent, ulong>(5, U64, x => x.ElapsedTicks, (x, v) => { x.ElapsedTicks = v; return x; }, nameof(ReplayCourseEvent.ElapsedTicks)),
        Field<ReplayCourseEvent, ushort>(6, U16, x => x.Sequence, (x, v) => { x.Sequence = v; return x; }, nameof(ReplayCourseEvent.Sequence)));
    internal static byte[] WriteReplayCourseEvent(ReplayCourseEvent x) => ReplayCourseEventTable.Write(x);
    internal static ReplayCourseEvent ReadReplayCourseEvent(byte[] b) => ReplayCourseEventTable.Read(b);
    internal static readonly Table<ReplayMayhemEvent> ReplayMayhemEventTable = new(() => new(),
        Field<ReplayMayhemEvent, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemEvent.Kind)),
        Field<ReplayMayhemEvent, string>(2, Text, x => x.EventId, (x, v) => { x.EventId = v; return x; }, nameof(ReplayMayhemEvent.EventId)),
        Field<ReplayMayhemEvent, ulong>(3, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayMayhemEvent.EntityId)),
        Field<ReplayMayhemEvent, ushort>(4, U16, x => x.RoundIndex, (x, v) => { x.RoundIndex = v; return x; }, nameof(ReplayMayhemEvent.RoundIndex)),
        Field<ReplayMayhemEvent, int>(5, I32, x => x.OperationIndex, (x, v) => { x.OperationIndex = v; return x; }, nameof(ReplayMayhemEvent.OperationIndex)),
        Field<ReplayMayhemEvent, ushort>(6, U16, x => x.Points, (x, v) => { x.Points = v; return x; }, nameof(ReplayMayhemEvent.Points)));
    internal static byte[] WriteReplayMayhemEvent(ReplayMayhemEvent x) => ReplayMayhemEventTable.Write(x);
    internal static ReplayMayhemEvent ReadReplayMayhemEvent(byte[] b) => ReplayMayhemEventTable.Read(b);
    internal static readonly Table<ReplayHit> ReplayHitTable = new(() => new(),
        Field<ReplayHit, ushort>(1, U16, x => x.Occurrence, (x, v) => { x.Occurrence = v; return x; }, nameof(ReplayHit.Occurrence)),
        Field<ReplayHit, ulong>(2, U64, x => x.OwnerId, (x, v) => { x.OwnerId = v; return x; }, nameof(ReplayHit.OwnerId)),
        Field<ReplayHit, ulong>(3, U64, x => x.TargetId, (x, v) => { x.TargetId = v; return x; }, nameof(ReplayHit.TargetId)),
        Field<ReplayHit, ulong>(4, U64, x => x.ActivationId, (x, v) => { x.ActivationId = v; return x; }, nameof(ReplayHit.ActivationId)),
        Field<ReplayHit, byte>(5, U8, x => x.AttackSequence, (x, v) => { x.AttackSequence = v; return x; }, nameof(ReplayHit.AttackSequence)),
        Field<ReplayHit, string>(6, Text, x => x.SlotId, (x, v) => { x.SlotId = v; return x; }, nameof(ReplayHit.SlotId)),
        Field<ReplayHit, string>(7, Text, x => x.FormId, (x, v) => { x.FormId = v; return x; }, nameof(ReplayHit.FormId)),
        Field<ReplayHit, bool>(8, Bool, x => x.ActivationAirborne, (x, v) => { x.ActivationAirborne = v; return x; }, nameof(ReplayHit.ActivationAirborne)),
        Field<ReplayHit, ReplayVector>(9, Vec, x => x.Position, (x, v) => { x.Position = v; return x; }, nameof(ReplayHit.Position)),
        Field<ReplayHit, float>(10, F32, x => x.ContactTime, (x, v) => { x.ContactTime = v; return x; }, nameof(ReplayHit.ContactTime)),
        Field<ReplayHit, float>(11, F32, x => x.AppliedDamage, (x, v) => { x.AppliedDamage = v; return x; }, nameof(ReplayHit.AppliedDamage)),
        Field<ReplayHit, float>(12, F32, x => x.ImpactForce, (x, v) => { x.ImpactForce = v; return x; }, nameof(ReplayHit.ImpactForce)),
        Field<ReplayHit, ReplayVector>(13, Vec, x => x.LaunchDirection, (x, v) => { x.LaunchDirection = v; return x; }, nameof(ReplayHit.LaunchDirection)),
        Field<ReplayHit, ushort>(14, U16, x => x.HitstopTicks, (x, v) => { x.HitstopTicks = v; return x; }, nameof(ReplayHit.HitstopTicks)),
        Field<ReplayHit, bool>(15, Bool, x => x.Blocked, (x, v) => { x.Blocked = v; return x; }, nameof(ReplayHit.Blocked)),
        Field<ReplayHit, bool>(16, Bool, x => x.ArmorProtected, (x, v) => { x.ArmorProtected = v; return x; }, nameof(ReplayHit.ArmorProtected)),
        Field<ReplayHit, bool>(17, Bool, x => x.FreezesOwner, (x, v) => { x.FreezesOwner = v; return x; }, nameof(ReplayHit.FreezesOwner)),
        Field<ReplayHit, string>(18, Text, x => x.PresentationId, (x, v) => { x.PresentationId = v; return x; }, nameof(ReplayHit.PresentationId)),
        Field<ReplayHit, string>(19, Text, x => x.SourceKind, (x, v) => { x.SourceKind = v; return x; }, nameof(ReplayHit.SourceKind)),
        Field<ReplayHit, bool>(20, Bool, x => x.Unblockable, (x, v) => { x.Unblockable = v; return x; }, nameof(ReplayHit.Unblockable)),
        Field<ReplayHit, string>(21, Text, x => x.PackageId, (x, v) => { x.PackageId = v; return x; }, nameof(ReplayHit.PackageId)),
        Field<ReplayHit, string>(22, Text, x => x.MoveName, (x, v) => { x.MoveName = v; return x; }, nameof(ReplayHit.MoveName)),
        Field<ReplayHit, string>(23, Text, x => x.ReturnPhaseId, (x, v) => { x.ReturnPhaseId = v; return x; }, nameof(ReplayHit.ReturnPhaseId)));
    internal static byte[] WriteReplayHit(ReplayHit x) => ReplayHitTable.Write(x);
    internal static ReplayHit ReadReplayHit(byte[] b) => ReplayHitTable.Read(b);
    internal static readonly Table<ReplayDeath> ReplayDeathTable = new(() => new(),
        Field<ReplayDeath, ulong>(1, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayDeath.EntityId)),
        Field<ReplayDeath, ReplayVector>(2, Vec, x => x.Position, (x, v) => { x.Position = v; return x; }, nameof(ReplayDeath.Position)),
        Field<ReplayDeath, ReplayVector>(3, Vec, x => x.Velocity, (x, v) => { x.Velocity = v; return x; }, nameof(ReplayDeath.Velocity)),
        Field<ReplayDeath, ReplayVector>(4, Vec, x => x.KnockbackVelocity, (x, v) => { x.KnockbackVelocity = v; return x; }, nameof(ReplayDeath.KnockbackVelocity)),
        Field<ReplayDeath, string>(5, Text, x => x.BoundaryId, (x, v) => { x.BoundaryId = v; return x; }, nameof(ReplayDeath.BoundaryId)),
        Field<ReplayDeath, ulong>(6, U64, x => x.KillerId, (x, v) => { x.KillerId = v; return x; }, nameof(ReplayDeath.KillerId)),
        Field<ReplayDeath, ulong>(7, U64, x => x.LastHitOwnerId, (x, v) => { x.LastHitOwnerId = v; return x; }, nameof(ReplayDeath.LastHitOwnerId)),
        Field<ReplayDeath, uint>(8, U32, x => x.LastHitTick, (x, v) => { x.LastHitTick = v; return x; }, nameof(ReplayDeath.LastHitTick)),
        Field<ReplayDeath, string>(9, Text, x => x.LastHitSlotId, (x, v) => { x.LastHitSlotId = v; return x; }, nameof(ReplayDeath.LastHitSlotId)),
        Field<ReplayDeath, ushort>(10, U16, x => x.DamagePercent, (x, v) => { x.DamagePercent = v; return x; }, nameof(ReplayDeath.DamagePercent)),
        Field<ReplayDeath, string>(11, Text, x => x.LastHitPackageId, (x, v) => { x.LastHitPackageId = v; return x; }, nameof(ReplayDeath.LastHitPackageId)),
        Field<ReplayDeath, string>(12, Text, x => x.LastHitFormId, (x, v) => { x.LastHitFormId = v; return x; }, nameof(ReplayDeath.LastHitFormId)),
        Field<ReplayDeath, string>(13, Text, x => x.LastHitMoveName, (x, v) => { x.LastHitMoveName = v; return x; }, nameof(ReplayDeath.LastHitMoveName)));
    internal static byte[] WriteReplayDeath(ReplayDeath x) => ReplayDeathTable.Write(x);
    internal static ReplayDeath ReadReplayDeath(byte[] b) => ReplayDeathTable.Read(b);
    internal static readonly Table<ReplayBall> ReplayBallTable = new(() => new(),
        Field<ReplayBall, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayBall.Kind)),
        Field<ReplayBall, ulong>(2, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayBall.EntityId)),
        Field<ReplayBall, ReplayVector>(3, Vec, x => x.Position, (x, v) => { x.Position = v; return x; }, nameof(ReplayBall.Position)),
        Field<ReplayBall, ReplayVector>(4, Vec, x => x.Velocity, (x, v) => { x.Velocity = v; return x; }, nameof(ReplayBall.Velocity)),
        Field<ReplayBall, float>(5, F32, x => x.Radius, (x, v) => { x.Radius = v; return x; }, nameof(ReplayBall.Radius)),
        Field<ReplayBall, float>(6, F32, x => x.DamagePercent, (x, v) => { x.DamagePercent = v; return x; }, nameof(ReplayBall.DamagePercent)),
        Field<ReplayBall, ushort>(7, U16, x => x.HitstopTicks, (x, v) => { x.HitstopTicks = v; return x; }, nameof(ReplayBall.HitstopTicks)),
        Field<ReplayBall, bool>(8, Bool, x => x.Active, (x, v) => { x.Active = v; return x; }, nameof(ReplayBall.Active)),
        Field<ReplayBall, float>(9, F32, x => x.Curve, (x, v) => { x.Curve = v; return x; }, nameof(ReplayBall.Curve)));
    internal static byte[] WriteReplayBall(ReplayBall x) => ReplayBallTable.Write(x);
    internal static ReplayBall ReadReplayBall(byte[] b) => ReplayBallTable.Read(b);
    internal static readonly Table<ReplaySlopballRun> ReplaySlopballRunTable = new(() => new(),
        Field<ReplaySlopballRun, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplaySlopballRun.Kind)),
        Field<ReplaySlopballRun, string>(2, Text, x => x.PhaseId, (x, v) => { x.PhaseId = v; return x; }, nameof(ReplaySlopballRun.PhaseId)),
        Field<ReplaySlopballRun, ulong>(3, U64, x => x.Returns, (x, v) => { x.Returns = v; return x; }, nameof(ReplaySlopballRun.Returns)),
        Field<ReplaySlopballRun, ulong>(4, U64, x => x.BestStreak, (x, v) => { x.BestStreak = v; return x; }, nameof(ReplaySlopballRun.BestStreak)),
        Field<ReplaySlopballRun, ulong>(5, U64, x => x.TotalReturns, (x, v) => { x.TotalReturns = v; return x; }, nameof(ReplaySlopballRun.TotalReturns)),
        Field<ReplaySlopballRun, ushort>(6, U16, x => x.ServeInTicks, (x, v) => { x.ServeInTicks = v; return x; }, nameof(ReplaySlopballRun.ServeInTicks)),
        Field<ReplaySlopballRun, ushort>(7, U16, x => x.CountdownTicks, (x, v) => { x.CountdownTicks = v; return x; }, nameof(ReplaySlopballRun.CountdownTicks)),
        Field<ReplaySlopballRun, bool>(8, Bool, x => x.Received, (x, v) => { x.Received = v; return x; }, nameof(ReplaySlopballRun.Received)),
        Field<ReplaySlopballRun, bool>(9, Bool, x => x.ReturnArmed, (x, v) => { x.ReturnArmed = v; return x; }, nameof(ReplaySlopballRun.ReturnArmed)),
        Field<ReplaySlopballRun, bool>(10, Bool, x => x.ClearedNet, (x, v) => { x.ClearedNet = v; return x; }, nameof(ReplaySlopballRun.ClearedNet)),
        Field<ReplaySlopballRun, string>(11, Text, x => x.ShotId, (x, v) => { x.ShotId = v; return x; }, nameof(ReplaySlopballRun.ShotId)),
        Field<ReplaySlopballRun, byte>(12, U8, x => x.Misses, (x, v) => { x.Misses = v; return x; }, nameof(ReplaySlopballRun.Misses)));
    internal static byte[] WriteReplaySlopballRun(ReplaySlopballRun x) => ReplaySlopballRunTable.Write(x);
    internal static ReplaySlopballRun ReadReplaySlopballRun(byte[] b) => ReplaySlopballRunTable.Read(b);
    internal static readonly Table<ReplaySlopballVersus> ReplaySlopballVersusTable = new(() => new(),
        Field<ReplaySlopballVersus, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplaySlopballVersus.Kind)),
        Field<ReplaySlopballVersus, string>(2, Text, x => x.PhaseId, (x, v) => { x.PhaseId = v; return x; }, nameof(ReplaySlopballVersus.PhaseId)),
        Field<ReplaySlopballVersus, ushort>(3, U16, x => x.TealScore, (x, v) => { x.TealScore = v; return x; }, nameof(ReplaySlopballVersus.TealScore)),
        Field<ReplaySlopballVersus, ushort>(4, U16, x => x.ClayScore, (x, v) => { x.ClayScore = v; return x; }, nameof(ReplaySlopballVersus.ClayScore)),
        Field<ReplaySlopballVersus, string>(5, Text, x => x.ServingCampId, (x, v) => { x.ServingCampId = v; return x; }, nameof(ReplaySlopballVersus.ServingCampId)),
        Field<ReplaySlopballVersus, ulong>(6, U64, x => x.ServerEntityId, (x, v) => { x.ServerEntityId = v; return x; }, nameof(ReplaySlopballVersus.ServerEntityId)),
        Field<ReplaySlopballVersus, ushort>(7, U16, x => x.ServeInTicks, (x, v) => { x.ServeInTicks = v; return x; }, nameof(ReplaySlopballVersus.ServeInTicks)),
        Field<ReplaySlopballVersus, string>(8, Text, x => x.WinningCampId, (x, v) => { x.WinningCampId = v; return x; }, nameof(ReplaySlopballVersus.WinningCampId)));
    internal static byte[] WriteReplaySlopballVersus(ReplaySlopballVersus x) => ReplaySlopballVersusTable.Write(x);
    internal static ReplaySlopballVersus ReadReplaySlopballVersus(byte[] b) => ReplaySlopballVersusTable.Read(b);
    internal static readonly Table<ReplayCourse> ReplayCourseTable = new(() => new(),
        Field<ReplayCourse, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayCourse.Kind)),
        Field<ReplayCourse, string>(2, Text, x => x.FormatId, (x, v) => { x.FormatId = v; return x; }, nameof(ReplayCourse.FormatId)),
        Field<ReplayCourse, ulong>(3, U64, x => x.ElapsedTicks, (x, v) => { x.ElapsedTicks = v; return x; }, nameof(ReplayCourse.ElapsedTicks)),
        Field<ReplayCourse, ushort>(4, U16, x => x.FinishWindowTicksLeft, (x, v) => { x.FinishWindowTicksLeft = v; return x; }, nameof(ReplayCourse.FinishWindowTicksLeft)),
        Field<ReplayCourse, bool>(5, Bool, x => x.Ended, (x, v) => { x.Ended = v; return x; }, nameof(ReplayCourse.Ended)),
        Field<ReplayCourse, ulong>(6, U64, x => x.WinnerId, (x, v) => { x.WinnerId = v; return x; }, nameof(ReplayCourse.WinnerId)),
        Field<ReplayCourse, ulong>(7, U64, x => x.LeaderId, (x, v) => { x.LeaderId = v; return x; }, nameof(ReplayCourse.LeaderId)),
        Field<ReplayCourse, ReplayVector>(8, Vec, x => x.SafePosition, (x, v) => { x.SafePosition = v; return x; }, nameof(ReplayCourse.SafePosition)),
        Field<ReplayCourse, float>(9, F32, x => x.SafeYaw, (x, v) => { x.SafeYaw = v; return x; }, nameof(ReplayCourse.SafeYaw)),
        Field<ReplayCourse, float>(10, F32, x => x.SafeProgress, (x, v) => { x.SafeProgress = v; return x; }, nameof(ReplayCourse.SafeProgress)));
    internal static byte[] WriteReplayCourse(ReplayCourse x) => ReplayCourseTable.Write(x);
    internal static ReplayCourse ReadReplayCourse(byte[] b) => ReplayCourseTable.Read(b);
    internal static readonly Table<ReplayCoursePlayer> ReplayCoursePlayerTable = new(() => new(),
        Field<ReplayCoursePlayer, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayCoursePlayer.Kind)),
        Field<ReplayCoursePlayer, ulong>(2, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayCoursePlayer.EntityId)),
        Field<ReplayCoursePlayer, string>(3, Text, x => x.PhaseId, (x, v) => { x.PhaseId = v; return x; }, nameof(ReplayCoursePlayer.PhaseId)),
        Field<ReplayCoursePlayer, ulong>(4, U64, x => x.ElapsedTicks, (x, v) => { x.ElapsedTicks = v; return x; }, nameof(ReplayCoursePlayer.ElapsedTicks)),
        Field<ReplayCoursePlayer, int>(5, I32, x => x.BrokenTargets, (x, v) => { x.BrokenTargets = v; return x; }, nameof(ReplayCoursePlayer.BrokenTargets)),
        Field<ReplayCoursePlayer, int>(6, I32, x => x.TotalTargets, (x, v) => { x.TotalTargets = v; return x; }, nameof(ReplayCoursePlayer.TotalTargets)),
        Field<ReplayCoursePlayer, string>(7, Text, x => x.SavedCheckpointId, (x, v) => { x.SavedCheckpointId = v; return x; }, nameof(ReplayCoursePlayer.SavedCheckpointId)),
        Field<ReplayCoursePlayer, ushort>(8, U16, x => x.CountdownTicks, (x, v) => { x.CountdownTicks = v; return x; }, nameof(ReplayCoursePlayer.CountdownTicks)),
        Field<ReplayCoursePlayer, byte>(9, U8, x => x.Place, (x, v) => { x.Place = v; return x; }, nameof(ReplayCoursePlayer.Place)),
        Field<ReplayCoursePlayer, bool>(10, Bool, x => x.Withdrawn, (x, v) => { x.Withdrawn = v; return x; }, nameof(ReplayCoursePlayer.Withdrawn)),
        Field<ReplayCoursePlayer, ushort>(11, U16, x => x.Points, (x, v) => { x.Points = v; return x; }, nameof(ReplayCoursePlayer.Points)),
        Field<ReplayCoursePlayer, ushort>(12, U16, x => x.LeashTicks, (x, v) => { x.LeashTicks = v; return x; }, nameof(ReplayCoursePlayer.LeashTicks)),
        Field<ReplayCoursePlayer, float>(13, F32, x => x.Progress, (x, v) => { x.Progress = v; return x; }, nameof(ReplayCoursePlayer.Progress)));
    internal static byte[] WriteReplayCoursePlayer(ReplayCoursePlayer x) => ReplayCoursePlayerTable.Write(x);
    internal static ReplayCoursePlayer ReadReplayCoursePlayer(byte[] b) => ReplayCoursePlayerTable.Read(b);
    internal static readonly Table<ReplayCourseTarget> ReplayCourseTargetTable = new(() => new(),
        Field<ReplayCourseTarget, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayCourseTarget.Kind)),
        Field<ReplayCourseTarget, ulong>(2, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayCourseTarget.EntityId)),
        Field<ReplayCourseTarget, ulong>(3, U64, x => x.PerspectiveEntityId, (x, v) => { x.PerspectiveEntityId = v; return x; }, nameof(ReplayCourseTarget.PerspectiveEntityId)),
        Field<ReplayCourseTarget, string>(4, Text, x => x.TargetId, (x, v) => { x.TargetId = v; return x; }, nameof(ReplayCourseTarget.TargetId)),
        Field<ReplayCourseTarget, ReplayVector>(5, Vec, x => x.Position, (x, v) => { x.Position = v; return x; }, nameof(ReplayCourseTarget.Position)),
        Field<ReplayCourseTarget, float>(6, F32, x => x.Radius, (x, v) => { x.Radius = v; return x; }, nameof(ReplayCourseTarget.Radius)),
        Field<ReplayCourseTarget, bool>(7, Bool, x => x.Broken, (x, v) => { x.Broken = v; return x; }, nameof(ReplayCourseTarget.Broken)),
        Field<ReplayCourseTarget, int>(8, I32, x => x.HitsLeft, (x, v) => { x.HitsLeft = v; return x; }, nameof(ReplayCourseTarget.HitsLeft)),
        Field<ReplayCourseTarget, ulong>(9, U64, x => x.BreakerId, (x, v) => { x.BreakerId = v; return x; }, nameof(ReplayCourseTarget.BreakerId)));
    internal static byte[] WriteReplayCourseTarget(ReplayCourseTarget x) => ReplayCourseTargetTable.Write(x);
    internal static ReplayCourseTarget ReadReplayCourseTarget(byte[] b) => ReplayCourseTargetTable.Read(b);
    internal static readonly Table<ReplayMayhem> ReplayMayhemTable = new(() => new(),
        Field<ReplayMayhem, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhem.Kind)),
        Field<ReplayMayhem, ushort>(2, U16, x => x.RoundIndex, (x, v) => { x.RoundIndex = v; return x; }, nameof(ReplayMayhem.RoundIndex)),
        Field<ReplayMayhem, string>(3, Text, x => x.CardId, (x, v) => { x.CardId = v; return x; }, nameof(ReplayMayhem.CardId)),
        Field<ReplayMayhem, string>(4, Text, x => x.PhaseId, (x, v) => { x.PhaseId = v; return x; }, nameof(ReplayMayhem.PhaseId)),
        Field<ReplayMayhem, ushort>(5, U16, x => x.PhaseTick, (x, v) => { x.PhaseTick = v; return x; }, nameof(ReplayMayhem.PhaseTick)),
        Field<ReplayMayhem, int>(6, I32, x => x.HazardsLaunched, (x, v) => { x.HazardsLaunched = v; return x; }, nameof(ReplayMayhem.HazardsLaunched)));
    internal static byte[] WriteReplayMayhem(ReplayMayhem x) => ReplayMayhemTable.Write(x);
    internal static ReplayMayhem ReadReplayMayhem(byte[] b) => ReplayMayhemTable.Read(b);
    internal static readonly Table<ReplayMayhemPlayer> ReplayMayhemPlayerTable = new(() => new(),
        Field<ReplayMayhemPlayer, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemPlayer.Kind)),
        Field<ReplayMayhemPlayer, ulong>(2, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayMayhemPlayer.EntityId)),
        Field<ReplayMayhemPlayer, ushort>(3, U16, x => x.Score, (x, v) => { x.Score = v; return x; }, nameof(ReplayMayhemPlayer.Score)),
        Field<ReplayMayhemPlayer, ushort>(4, U16, x => x.RoundsSurvived, (x, v) => { x.RoundsSurvived = v; return x; }, nameof(ReplayMayhemPlayer.RoundsSurvived)),
        Field<ReplayMayhemPlayer, ushort>(5, U16, x => x.Bonuses, (x, v) => { x.Bonuses = v; return x; }, nameof(ReplayMayhemPlayer.Bonuses)),
        Field<ReplayMayhemPlayer, bool>(6, Bool, x => x.Benched, (x, v) => { x.Benched = v; return x; }, nameof(ReplayMayhemPlayer.Benched)),
        Field<ReplayMayhemPlayer, bool>(7, Bool, x => x.Withdrawn, (x, v) => { x.Withdrawn = v; return x; }, nameof(ReplayMayhemPlayer.Withdrawn)));
    internal static byte[] WriteReplayMayhemPlayer(ReplayMayhemPlayer x) => ReplayMayhemPlayerTable.Write(x);
    internal static ReplayMayhemPlayer ReadReplayMayhemPlayer(byte[] b) => ReplayMayhemPlayerTable.Read(b);
    internal static readonly Table<ReplayMayhemRing> ReplayMayhemRingTable = new(() => new(),
        Field<ReplayMayhemRing, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemRing.Kind)),
        Field<ReplayMayhemRing, int>(2, I32, x => x.Ordinal, (x, v) => { x.Ordinal = v; return x; }, nameof(ReplayMayhemRing.Ordinal)),
        Field<ReplayMayhemRing, ReplayVector>(3, Vec, x => x.Position, (x, v) => { x.Position = v; return x; }, nameof(ReplayMayhemRing.Position)),
        Field<ReplayMayhemRing, uint>(4, U32, x => x.LaunchTick, (x, v) => { x.LaunchTick = v; return x; }, nameof(ReplayMayhemRing.LaunchTick)),
        Field<ReplayMayhemRing, uint>(5, U32, x => x.LandingTick, (x, v) => { x.LandingTick = v; return x; }, nameof(ReplayMayhemRing.LandingTick)),
        Field<ReplayMayhemRing, ushort>(6, U16, x => x.FuseTicks, (x, v) => { x.FuseTicks = v; return x; }, nameof(ReplayMayhemRing.FuseTicks)));
    internal static byte[] WriteReplayMayhemRing(ReplayMayhemRing x) => ReplayMayhemRingTable.Write(x);
    internal static ReplayMayhemRing ReadReplayMayhemRing(byte[] b) => ReplayMayhemRingTable.Read(b);
    internal static readonly Table<ReplayMayhemSlam> ReplayMayhemSlamTable = new(() => new(),
        Field<ReplayMayhemSlam, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemSlam.Kind)),
        Field<ReplayMayhemSlam, int>(2, I32, x => x.Ordinal, (x, v) => { x.Ordinal = v; return x; }, nameof(ReplayMayhemSlam.Ordinal)),
        Field<ReplayMayhemSlam, ReplayVector>(3, Vec, x => x.Position, (x, v) => { x.Position = v; return x; }, nameof(ReplayMayhemSlam.Position)),
        Field<ReplayMayhemSlam, uint>(4, U32, x => x.Tick, (x, v) => { x.Tick = v; return x; }, nameof(ReplayMayhemSlam.Tick)));
    internal static byte[] WriteReplayMayhemSlam(ReplayMayhemSlam x) => ReplayMayhemSlamTable.Write(x);
    internal static ReplayMayhemSlam ReadReplayMayhemSlam(byte[] b) => ReplayMayhemSlamTable.Read(b);
    internal static readonly Table<ReplayMayhemWall> ReplayMayhemWallTable = new(() => new(),
        Field<ReplayMayhemWall, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemWall.Kind)),
        Field<ReplayMayhemWall, ulong>(2, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayMayhemWall.EntityId)),
        Field<ReplayMayhemWall, ulong>(3, U64, x => x.CageEntityId, (x, v) => { x.CageEntityId = v; return x; }, nameof(ReplayMayhemWall.CageEntityId)),
        Field<ReplayMayhemWall, ReplayVector>(4, Vec, x => x.CageCenter, (x, v) => { x.CageCenter = v; return x; }, nameof(ReplayMayhemWall.CageCenter)),
        Field<ReplayMayhemWall, ReplayVector>(5, Vec, x => x.Center, (x, v) => { x.Center = v; return x; }, nameof(ReplayMayhemWall.Center)),
        Field<ReplayMayhemWall, ReplayVector>(6, Vec, x => x.HalfExtents, (x, v) => { x.HalfExtents = v; return x; }, nameof(ReplayMayhemWall.HalfExtents)),
        Field<ReplayMayhemWall, float>(7, F32, x => x.Yaw, (x, v) => { x.Yaw = v; return x; }, nameof(ReplayMayhemWall.Yaw)),
        Field<ReplayMayhemWall, byte>(8, U8, x => x.HitsLeft, (x, v) => { x.HitsLeft = v; return x; }, nameof(ReplayMayhemWall.HitsLeft)),
        Field<ReplayMayhemWall, string>(9, Text, x => x.PhaseId, (x, v) => { x.PhaseId = v; return x; }, nameof(ReplayMayhemWall.PhaseId)),
        Field<ReplayMayhemWall, ushort>(10, U16, x => x.PhaseTick, (x, v) => { x.PhaseTick = v; return x; }, nameof(ReplayMayhemWall.PhaseTick)));
    internal static byte[] WriteReplayMayhemWall(ReplayMayhemWall x) => ReplayMayhemWallTable.Write(x);
    internal static ReplayMayhemWall ReadReplayMayhemWall(byte[] b) => ReplayMayhemWallTable.Read(b);
    internal static readonly Table<ReplayMayhemChannelEvent> ReplayMayhemChannelEventTable = new(() => new(),
        Field<ReplayMayhemChannelEvent, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemChannelEvent.Kind)),
        Field<ReplayMayhemChannelEvent, string>(2, Text, x => x.EventId, (x, v) => { x.EventId = v; return x; }, nameof(ReplayMayhemChannelEvent.EventId)),
        Field<ReplayMayhemChannelEvent, ushort>(3, U16, x => x.Episode, (x, v) => { x.Episode = v; return x; }, nameof(ReplayMayhemChannelEvent.Episode)),
        Field<ReplayMayhemChannelEvent, byte>(4, U8, x => x.Lane, (x, v) => { x.Lane = v; return x; }, nameof(ReplayMayhemChannelEvent.Lane)),
        Field<ReplayMayhemChannelEvent, ulong>(5, U64, x => x.Operation, (x, v) => { x.Operation = v; return x; }, nameof(ReplayMayhemChannelEvent.Operation)),
        Field<ReplayMayhemChannelEvent, ulong>(6, U64, x => x.Subject, (x, v) => { x.Subject = v; return x; }, nameof(ReplayMayhemChannelEvent.Subject)),
        Field<ReplayMayhemChannelEvent, ushort>(7, U16, x => x.Value, (x, v) => { x.Value = v; return x; }, nameof(ReplayMayhemChannelEvent.Value)));
    internal static byte[] WriteReplayMayhemChannelEvent(ReplayMayhemChannelEvent x) => ReplayMayhemChannelEventTable.Write(x);
    internal static ReplayMayhemChannelEvent ReadReplayMayhemChannelEvent(byte[] b) => ReplayMayhemChannelEventTable.Read(b);
    internal static readonly Table<ReplayMayhemShow> ReplayMayhemShowTable = new(() => new(),
        Field<ReplayMayhemShow, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemShow.Kind)),
        Field<ReplayMayhemShow, uint>(2, U32, x => x.Seed, (x, v) => { x.Seed = v; return x; }, nameof(ReplayMayhemShow.Seed)),
        Field<ReplayMayhemShow, int>(3, I32, x => x.Episode, (x, v) => { x.Episode = v; return x; }, nameof(ReplayMayhemShow.Episode)),
        Field<ReplayMayhemShow, string>(4, Text, x => x.PhaseId, (x, v) => { x.PhaseId = v; return x; }, nameof(ReplayMayhemShow.PhaseId)),
        Field<ReplayMayhemShow, string>(5, Text, x => x.ChannelId, (x, v) => { x.ChannelId = v; return x; }, nameof(ReplayMayhemShow.ChannelId)),
        Field<ReplayMayhemShow, string>(6, Text, x => x.TwistCopyKey, (x, v) => { x.TwistCopyKey = v; return x; }, nameof(ReplayMayhemShow.TwistCopyKey)),
        Field<ReplayMayhemShow, string>(7, Text, x => x.PairCopyKey, (x, v) => { x.PairCopyKey = v; return x; }, nameof(ReplayMayhemShow.PairCopyKey)),
        Field<ReplayMayhemShow, string>(8, Text, x => x.GoalTwistCopyKey, (x, v) => { x.GoalTwistCopyKey = v; return x; }, nameof(ReplayMayhemShow.GoalTwistCopyKey)),
        Field<ReplayMayhemShow, string>(9, Text, x => x.HazardTwistCopyKey, (x, v) => { x.HazardTwistCopyKey = v; return x; }, nameof(ReplayMayhemShow.HazardTwistCopyKey)));
    internal static byte[] WriteReplayMayhemShow(ReplayMayhemShow x) => ReplayMayhemShowTable.Write(x);
    internal static ReplayMayhemShow ReadReplayMayhemShow(byte[] b) => ReplayMayhemShowTable.Read(b);
    internal static readonly Table<ReplayMayhemContestant> ReplayMayhemContestantTable = new(() => new(),
        Field<ReplayMayhemContestant, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemContestant.Kind)),
        Field<ReplayMayhemContestant, ulong>(2, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayMayhemContestant.EntityId)),
        Field<ReplayMayhemContestant, bool>(3, Bool, x => x.OnAir, (x, v) => { x.OnAir = v; return x; }, nameof(ReplayMayhemContestant.OnAir)),
        Field<ReplayMayhemContestant, bool>(4, Bool, x => x.Withdrawn, (x, v) => { x.Withdrawn = v; return x; }, nameof(ReplayMayhemContestant.Withdrawn)),
        Field<ReplayMayhemContestant, uint>(5, U32, x => x.PassMask, (x, v) => { x.PassMask = v; return x; }, nameof(ReplayMayhemContestant.PassMask)),
        Field<ReplayMayhemContestant, string>(6, Text, x => x.FailCauseId, (x, v) => { x.FailCauseId = v; return x; }, nameof(ReplayMayhemContestant.FailCauseId)),
        Field<ReplayMayhemContestant, string>(7, Text, x => x.SplatStyleId, (x, v) => { x.SplatStyleId = v; return x; }, nameof(ReplayMayhemContestant.SplatStyleId)),
        Field<ReplayMayhemContestant, uint>(8, U32, x => x.FailTick, (x, v) => { x.FailTick = v; return x; }, nameof(ReplayMayhemContestant.FailTick)),
        Field<ReplayMayhemContestant, ReplayVector>(9, Vec, x => x.Anchor, (x, v) => { x.Anchor = v; return x; }, nameof(ReplayMayhemContestant.Anchor)),
        Field<ReplayMayhemContestant, float>(10, F32, x => x.AnchorYaw, (x, v) => { x.AnchorYaw = v; return x; }, nameof(ReplayMayhemContestant.AnchorYaw)));
    internal static byte[] WriteReplayMayhemContestant(ReplayMayhemContestant x) => ReplayMayhemContestantTable.Write(x);
    internal static ReplayMayhemContestant ReadReplayMayhemContestant(byte[] b) => ReplayMayhemContestantTable.Read(b);
    internal static readonly Table<ReplayMayhemBomb> ReplayMayhemBombTable = new(() => new(),
        Field<ReplayMayhemBomb, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemBomb.Kind)),
        Field<ReplayMayhemBomb, byte>(2, U8, x => x.Lane, (x, v) => { x.Lane = v; return x; }, nameof(ReplayMayhemBomb.Lane)),
        Field<ReplayMayhemBomb, ushort>(3, U16, x => x.Ordinal, (x, v) => { x.Ordinal = v; return x; }, nameof(ReplayMayhemBomb.Ordinal)),
        Field<ReplayMayhemBomb, bool>(4, Bool, x => x.Launched, (x, v) => { x.Launched = v; return x; }, nameof(ReplayMayhemBomb.Launched)),
        Field<ReplayMayhemBomb, ReplayVector>(5, Vec, x => x.Landing, (x, v) => { x.Landing = v; return x; }, nameof(ReplayMayhemBomb.Landing)),
        Field<ReplayMayhemBomb, uint>(6, U32, x => x.LaunchTick, (x, v) => { x.LaunchTick = v; return x; }, nameof(ReplayMayhemBomb.LaunchTick)),
        Field<ReplayMayhemBomb, uint>(7, U32, x => x.ImpactTick, (x, v) => { x.ImpactTick = v; return x; }, nameof(ReplayMayhemBomb.ImpactTick)),
        Field<ReplayMayhemBomb, float>(8, F32, x => x.BlastRadius, (x, v) => { x.BlastRadius = v; return x; }, nameof(ReplayMayhemBomb.BlastRadius)),
        Field<ReplayMayhemBomb, bool>(9, Bool, x => x.Exploded, (x, v) => { x.Exploded = v; return x; }, nameof(ReplayMayhemBomb.Exploded)),
        Field<ReplayMayhemBomb, string>(10, Text, x => x.BombKindId, (x, v) => { x.BombKindId = v; return x; }, nameof(ReplayMayhemBomb.BombKindId)));
    internal static byte[] WriteReplayMayhemBomb(ReplayMayhemBomb x) => ReplayMayhemBombTable.Write(x);
    internal static ReplayMayhemBomb ReadReplayMayhemBomb(byte[] b) => ReplayMayhemBombTable.Read(b);
    internal static readonly Table<ReplayMayhemShockwave> ReplayMayhemShockwaveTable = new(() => new(),
        Field<ReplayMayhemShockwave, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemShockwave.Kind)),
        Field<ReplayMayhemShockwave, byte>(2, U8, x => x.Lane, (x, v) => { x.Lane = v; return x; }, nameof(ReplayMayhemShockwave.Lane)),
        Field<ReplayMayhemShockwave, ushort>(3, U16, x => x.Ordinal, (x, v) => { x.Ordinal = v; return x; }, nameof(ReplayMayhemShockwave.Ordinal)),
        Field<ReplayMayhemShockwave, ReplayVector>(4, Vec, x => x.Center, (x, v) => { x.Center = v; return x; }, nameof(ReplayMayhemShockwave.Center)),
        Field<ReplayMayhemShockwave, uint>(5, U32, x => x.SlamTick, (x, v) => { x.SlamTick = v; return x; }, nameof(ReplayMayhemShockwave.SlamTick)),
        Field<ReplayMayhemShockwave, float>(6, F32, x => x.Speed, (x, v) => { x.Speed = v; return x; }, nameof(ReplayMayhemShockwave.Speed)),
        Field<ReplayMayhemShockwave, float>(7, F32, x => x.HalfWidth, (x, v) => { x.HalfWidth = v; return x; }, nameof(ReplayMayhemShockwave.HalfWidth)),
        Field<ReplayMayhemShockwave, float>(8, F32, x => x.HalfHeight, (x, v) => { x.HalfHeight = v; return x; }, nameof(ReplayMayhemShockwave.HalfHeight)),
        Field<ReplayMayhemShockwave, ushort>(9, U16, x => x.LifetimeTicks, (x, v) => { x.LifetimeTicks = v; return x; }, nameof(ReplayMayhemShockwave.LifetimeTicks)));
    internal static byte[] WriteReplayMayhemShockwave(ReplayMayhemShockwave x) => ReplayMayhemShockwaveTable.Write(x);
    internal static ReplayMayhemShockwave ReadReplayMayhemShockwave(byte[] b) => ReplayMayhemShockwaveTable.Read(b);
    internal static readonly Table<ReplayMayhemZone> ReplayMayhemZoneTable = new(() => new(),
        Field<ReplayMayhemZone, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemZone.Kind)),
        Field<ReplayMayhemZone, byte>(2, U8, x => x.Lane, (x, v) => { x.Lane = v; return x; }, nameof(ReplayMayhemZone.Lane)),
        Field<ReplayMayhemZone, ushort>(3, U16, x => x.Ordinal, (x, v) => { x.Ordinal = v; return x; }, nameof(ReplayMayhemZone.Ordinal)),
        Field<ReplayMayhemZone, string>(4, Text, x => x.ZoneId, (x, v) => { x.ZoneId = v; return x; }, nameof(ReplayMayhemZone.ZoneId)),
        Field<ReplayMayhemZone, ReplayVector>(5, Vec, x => x.Center, (x, v) => { x.Center = v; return x; }, nameof(ReplayMayhemZone.Center)),
        Field<ReplayMayhemZone, float>(6, F32, x => x.Radius, (x, v) => { x.Radius = v; return x; }, nameof(ReplayMayhemZone.Radius)),
        Field<ReplayMayhemZone, uint>(7, U32, x => x.ActiveFrom, (x, v) => { x.ActiveFrom = v; return x; }, nameof(ReplayMayhemZone.ActiveFrom)),
        Field<ReplayMayhemZone, uint>(8, U32, x => x.ActiveUntil, (x, v) => { x.ActiveUntil = v; return x; }, nameof(ReplayMayhemZone.ActiveUntil)),
        Field<ReplayMayhemZone, ReplayVector>(9, Vec, x => x.EndCenter, (x, v) => { x.EndCenter = v; return x; }, nameof(ReplayMayhemZone.EndCenter)),
        Field<ReplayMayhemZone, float>(10, F32, x => x.EndRadius, (x, v) => { x.EndRadius = v; return x; }, nameof(ReplayMayhemZone.EndRadius)),
        Field<ReplayMayhemZone, uint>(11, U32, x => x.ArmedFrom, (x, v) => { x.ArmedFrom = v; return x; }, nameof(ReplayMayhemZone.ArmedFrom)),
        Field<ReplayMayhemZone, float>(12, F32, x => x.HalfDepth, (x, v) => { x.HalfDepth = v; return x; }, nameof(ReplayMayhemZone.HalfDepth)),
        Field<ReplayMayhemZone, float>(13, F32, x => x.EndHalfDepth, (x, v) => { x.EndHalfDepth = v; return x; }, nameof(ReplayMayhemZone.EndHalfDepth)));
    internal static byte[] WriteReplayMayhemZone(ReplayMayhemZone x) => ReplayMayhemZoneTable.Write(x);
    internal static ReplayMayhemZone ReadReplayMayhemZone(byte[] b) => ReplayMayhemZoneTable.Read(b);
    internal static readonly Table<ReplayMayhemWatch> ReplayMayhemWatchTable = new(() => new(),
        Field<ReplayMayhemWatch, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemWatch.Kind)),
        Field<ReplayMayhemWatch, byte>(2, U8, x => x.Lane, (x, v) => { x.Lane = v; return x; }, nameof(ReplayMayhemWatch.Lane)),
        Field<ReplayMayhemWatch, ushort>(3, U16, x => x.Ordinal, (x, v) => { x.Ordinal = v; return x; }, nameof(ReplayMayhemWatch.Ordinal)),
        Field<ReplayMayhemWatch, uint>(4, U32, x => x.WarningTick, (x, v) => { x.WarningTick = v; return x; }, nameof(ReplayMayhemWatch.WarningTick)),
        Field<ReplayMayhemWatch, uint>(5, U32, x => x.StartTick, (x, v) => { x.StartTick = v; return x; }, nameof(ReplayMayhemWatch.StartTick)),
        Field<ReplayMayhemWatch, uint>(6, U32, x => x.EndTick, (x, v) => { x.EndTick = v; return x; }, nameof(ReplayMayhemWatch.EndTick)),
        Field<ReplayMayhemWatch, string>(7, Text, x => x.WatchKindId, (x, v) => { x.WatchKindId = v; return x; }, nameof(ReplayMayhemWatch.WatchKindId)),
        Field<ReplayMayhemWatch, string>(8, Text, x => x.CopyKey, (x, v) => { x.CopyKey = v; return x; }, nameof(ReplayMayhemWatch.CopyKey)));
    internal static byte[] WriteReplayMayhemWatch(ReplayMayhemWatch x) => ReplayMayhemWatchTable.Write(x);
    internal static ReplayMayhemWatch ReadReplayMayhemWatch(byte[] b) => ReplayMayhemWatchTable.Read(b);
    internal static readonly Table<ReplayMayhemCarry> ReplayMayhemCarryTable = new(() => new(),
        Field<ReplayMayhemCarry, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemCarry.Kind)),
        Field<ReplayMayhemCarry, byte>(2, U8, x => x.Lane, (x, v) => { x.Lane = v; return x; }, nameof(ReplayMayhemCarry.Lane)),
        Field<ReplayMayhemCarry, ushort>(3, U16, x => x.Ordinal, (x, v) => { x.Ordinal = v; return x; }, nameof(ReplayMayhemCarry.Ordinal)),
        Field<ReplayMayhemCarry, byte>(4, U8, x => x.HolderSeat, (x, v) => { x.HolderSeat = v; return x; }, nameof(ReplayMayhemCarry.HolderSeat)),
        Field<ReplayMayhemCarry, uint>(5, U32, x => x.FuseEnd, (x, v) => { x.FuseEnd = v; return x; }, nameof(ReplayMayhemCarry.FuseEnd)),
        Field<ReplayMayhemCarry, byte>(6, U8, x => x.LastGiver, (x, v) => { x.LastGiver = v; return x; }, nameof(ReplayMayhemCarry.LastGiver)));
    internal static byte[] WriteReplayMayhemCarry(ReplayMayhemCarry x) => ReplayMayhemCarryTable.Write(x);
    internal static ReplayMayhemCarry ReadReplayMayhemCarry(byte[] b) => ReplayMayhemCarryTable.Read(b);
    internal static readonly Table<ReplayMayhemHitProgress> ReplayMayhemHitProgressTable = new(() => new(),
        Field<ReplayMayhemHitProgress, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemHitProgress.Kind)),
        Field<ReplayMayhemHitProgress, byte>(2, U8, x => x.Seat, (x, v) => { x.Seat = v; return x; }, nameof(ReplayMayhemHitProgress.Seat)),
        Field<ReplayMayhemHitProgress, byte>(3, U8, x => x.Hits, (x, v) => { x.Hits = v; return x; }, nameof(ReplayMayhemHitProgress.Hits)),
        Field<ReplayMayhemHitProgress, byte>(4, U8, x => x.Quota, (x, v) => { x.Quota = v; return x; }, nameof(ReplayMayhemHitProgress.Quota)));
    internal static byte[] WriteReplayMayhemHitProgress(ReplayMayhemHitProgress x) => ReplayMayhemHitProgressTable.Write(x);
    internal static ReplayMayhemHitProgress ReadReplayMayhemHitProgress(byte[] b) => ReplayMayhemHitProgressTable.Read(b);
    internal static readonly Table<ReplayMayhemTarget> ReplayMayhemTargetTable = new(() => new(),
        Field<ReplayMayhemTarget, string>(1, Text, x => x.Kind, (x, v) => { x.Kind = v; return x; }, nameof(ReplayMayhemTarget.Kind)),
        Field<ReplayMayhemTarget, byte>(2, U8, x => x.Lane, (x, v) => { x.Lane = v; return x; }, nameof(ReplayMayhemTarget.Lane)),
        Field<ReplayMayhemTarget, ushort>(3, U16, x => x.Ordinal, (x, v) => { x.Ordinal = v; return x; }, nameof(ReplayMayhemTarget.Ordinal)),
        Field<ReplayMayhemTarget, string>(4, Text, x => x.TargetId, (x, v) => { x.TargetId = v; return x; }, nameof(ReplayMayhemTarget.TargetId)),
        Field<ReplayMayhemTarget, ulong>(5, U64, x => x.EntityId, (x, v) => { x.EntityId = v; return x; }, nameof(ReplayMayhemTarget.EntityId)),
        Field<ReplayMayhemTarget, ulong>(6, U64, x => x.OwnerId, (x, v) => { x.OwnerId = v; return x; }, nameof(ReplayMayhemTarget.OwnerId)),
        Field<ReplayMayhemTarget, ReplayVector>(7, Vec, x => x.Center, (x, v) => { x.Center = v; return x; }, nameof(ReplayMayhemTarget.Center)),
        Field<ReplayMayhemTarget, float>(8, F32, x => x.Radius, (x, v) => { x.Radius = v; return x; }, nameof(ReplayMayhemTarget.Radius)),
        Field<ReplayMayhemTarget, float>(9, F32, x => x.HalfHeight, (x, v) => { x.HalfHeight = v; return x; }, nameof(ReplayMayhemTarget.HalfHeight)),
        Field<ReplayMayhemTarget, byte>(10, U8, x => x.HitsLeft, (x, v) => { x.HitsLeft = v; return x; }, nameof(ReplayMayhemTarget.HitsLeft)),
        Field<ReplayMayhemTarget, byte>(11, U8, x => x.Hits, (x, v) => { x.Hits = v; return x; }, nameof(ReplayMayhemTarget.Hits)),
        Field<ReplayMayhemTarget, uint>(12, U32, x => x.ActiveFrom, (x, v) => { x.ActiveFrom = v; return x; }, nameof(ReplayMayhemTarget.ActiveFrom)),
        Field<ReplayMayhemTarget, uint>(13, U32, x => x.ActiveUntil, (x, v) => { x.ActiveUntil = v; return x; }, nameof(ReplayMayhemTarget.ActiveUntil)));
    internal static byte[] WriteReplayMayhemTarget(ReplayMayhemTarget x) => ReplayMayhemTargetTable.Write(x);
    internal static ReplayMayhemTarget ReadReplayMayhemTarget(byte[] b) => ReplayMayhemTargetTable.Read(b);
    internal static readonly Table<ReplayCooldown> ReplayCooldownTable = new(() => new(),
        Field<ReplayCooldown, string>(1, Text, x => x.PackageId, (x, v) => { x.PackageId = v; return x; }, nameof(ReplayCooldown.PackageId)),
        Field<ReplayCooldown, string>(2, Text, x => x.FormId, (x, v) => { x.FormId = v; return x; }, nameof(ReplayCooldown.FormId)),
        Field<ReplayCooldown, string>(3, Text, x => x.SlotId, (x, v) => { x.SlotId = v; return x; }, nameof(ReplayCooldown.SlotId)),
        Field<ReplayCooldown, string>(4, Text, x => x.MoveName, (x, v) => { x.MoveName = v; return x; }, nameof(ReplayCooldown.MoveName)),
        Field<ReplayCooldown, ushort>(5, U16, x => x.TicksLeft, (x, v) => { x.TicksLeft = v; return x; }, nameof(ReplayCooldown.TicksLeft)),
        Field<ReplayCooldown, ushort>(6, U16, x => x.DurationTicks, (x, v) => { x.DurationTicks = v; return x; }, nameof(ReplayCooldown.DurationTicks)));
    internal static byte[] WriteReplayCooldown(ReplayCooldown x) => ReplayCooldownTable.Write(x);
    internal static ReplayCooldown ReadReplayCooldown(byte[] b) => ReplayCooldownTable.Read(b);
    internal static readonly KindRegistry<ReplayEvent> ReplayEventKinds = new(
        Kind<ReplayEvent, ReplayPresentationEvent>("presentation", ReplayPresentationEventTable),
        Kind<ReplayEvent, ReplaySlopballEvent>("slopball", ReplaySlopballEventTable),
        Kind<ReplayEvent, ReplayCourseEvent>("course", ReplayCourseEventTable),
        Kind<ReplayEvent, ReplayMayhemEvent>("mayhem", ReplayMayhemEventTable),
        Kind<ReplayEvent, ReplayMayhemChannelEvent>("mayhem-channel", ReplayMayhemChannelEventTable));
    internal static byte[] WriteReplayEvent(ReplayEvent x) => ReplayEventKinds.Write(x);
    internal static ReplayEvent ReadReplayEvent(byte[] b) => ReplayEventKinds.Read(b);
    internal static readonly KindRegistry<ReplayModeObject> ReplayModeObjectKinds = new(
        Kind<ReplayModeObject, ReplayBall>("ball", ReplayBallTable),
        Kind<ReplayModeObject, ReplaySlopballRun>("slopball-run", ReplaySlopballRunTable),
        Kind<ReplayModeObject, ReplaySlopballVersus>("slopball-versus", ReplaySlopballVersusTable),
        Kind<ReplayModeObject, ReplayCourse>("course", ReplayCourseTable),
        Kind<ReplayModeObject, ReplayCoursePlayer>("course-player", ReplayCoursePlayerTable),
        Kind<ReplayModeObject, ReplayCourseTarget>("course-target", ReplayCourseTargetTable),
        Kind<ReplayModeObject, ReplayMayhem>("mayhem", ReplayMayhemTable),
        Kind<ReplayModeObject, ReplayMayhemPlayer>("mayhem-player", ReplayMayhemPlayerTable),
        Kind<ReplayModeObject, ReplayMayhemRing>("mayhem-ring", ReplayMayhemRingTable),
        Kind<ReplayModeObject, ReplayMayhemSlam>("mayhem-slam", ReplayMayhemSlamTable),
        Kind<ReplayModeObject, ReplayMayhemWall>("mayhem-wall", ReplayMayhemWallTable),
        Kind<ReplayModeObject, ReplayMayhemShow>("mayhem-show", ReplayMayhemShowTable),
        Kind<ReplayModeObject, ReplayMayhemContestant>("mayhem-contestant", ReplayMayhemContestantTable),
        Kind<ReplayModeObject, ReplayMayhemBomb>("mayhem-bomb", ReplayMayhemBombTable),
        Kind<ReplayModeObject, ReplayMayhemShockwave>("mayhem-shockwave", ReplayMayhemShockwaveTable),
        Kind<ReplayModeObject, ReplayMayhemZone>("mayhem-zone", ReplayMayhemZoneTable),
        Kind<ReplayModeObject, ReplayMayhemWatch>("mayhem-watch", ReplayMayhemWatchTable),
        Kind<ReplayModeObject, ReplayMayhemCarry>("mayhem-carry", ReplayMayhemCarryTable),
        Kind<ReplayModeObject, ReplayMayhemHitProgress>("mayhem-hit-progress", ReplayMayhemHitProgressTable),
        Kind<ReplayModeObject, ReplayMayhemTarget>("mayhem-target", ReplayMayhemTargetTable));
    internal static byte[] WriteReplayModeObject(ReplayModeObject x) => ReplayModeObjectKinds.Write(x);
    internal static ReplayModeObject ReadReplayModeObject(byte[] b) => ReplayModeObjectKinds.Read(b);
}
