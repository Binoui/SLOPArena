using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared.Replays
{
    /// <summary>
    /// The inverse of ReplayCapture.ProjectView on everything the View records, with today's content. Pure: it builds
    /// the simulation's own restore types and never writes simulation state. ReplayWorldTests pins the round trip
    /// and lists what does not survive it.
    /// </summary>
    internal static class ReplayViewDecoder
    {
        internal static ServerSimulation.PreparedSnapshot Prepare(ServerSimulation shell, ReplayViewTick view, ReplayCastIndex cast)
        {
            var fighters = new ServerSimulation.FighterSnapshot[view.Fighters.Count];
            ulong maxActivation = 0, maxInteraction = 0;
            for (int i = 0; i < fighters.Length; i++)
            {
                var f = view.Fighters[i];
                var member = cast.Member(f.EntityId) ?? throw new KeyNotFoundException("View fighter " + f.EntityId);
                var move = cast.Move(f.EntityId, f.PackageId, f.MoveFormId, f.MoveName, f.SlotId, f.ActivationAirborne);
                var state = Fighter(f, cast.Form(member, f.FormId));
                CookedTimelineAbility? ability = null;
                AttackPose? pose = null;
                if (move.Slot is { } slot)
                {
                    var (stage, stageTick, clip) = Cursor(slot, f.PoseAnimationId, f.MoveElapsedTicks);
                    state.AttackSlot = move.WireSlot;
                    state.ComboStage = (byte)stage;
                    state.AnimIndex = clip;
                    ability = shell.ObserveAbility(f.EntityId, slot, move.WireSlot, f.ActivationId, stage, stageTick, clip, f.ActivationAirborne);
                    pose = new AttackPose(f.PoseAnimationId, f.PoseElapsedTicks, f.PoseDurationTicks, f.PhaseClip);
                }
                else if (move.Match == ReplayMoveMatch.Missing)
                {
                    // Neutral pose: keep where she is and which way she faces, drop the move.
                    if (state.State is ActionState.Attacking or ActionState.Aiming) state.State = ActionState.Idle;
                    state.ClearAttackFields();
                    state.ChargeTicks = 0;
                }
                foreach (var c in f.Cooldowns)
                    if (cast.Move(f.EntityId, c.PackageId, c.FormId, c.MoveName, c.SlotId, false) is { Slot: not null } cooled)
                        state.SetCooldown(cooled.WireSlot, c.TicksLeft);
                fighters[i] = new ServerSimulation.FighterSnapshot
                {
                    Id = f.EntityId,
                    State = state,
                    Kos = f.KOs > 0 ? f.KOs : null,
                    LastActivationId = f.ActivationId != 0 ? f.ActivationId : null,
                    Ability = ability,
                    ObservedPose = pose,
                };
                maxActivation = Math.Max(maxActivation, f.ActivationId);
                maxInteraction = Math.Max(maxInteraction, f.InteractionId);
            }
            var resolver = new SpellResolver.SnapshotData();
            foreach (var s in view.Shapes)
            {
                resolver.Hitboxes.Add(Shape(s, cast));
                resolver.LastEmissionId = Math.Max(resolver.LastEmissionId, s.EmissionId);
                maxActivation = Math.Max(maxActivation, s.ActivationId);
            }
            // Never used: a display world is never ticked. Kept past every observed identity for consistency.
            return new ServerSimulation.PreparedSnapshot(view.Tick, maxActivation + 1, maxInteraction + 1, fighters, resolver,
                default, default, default, false, null, null, null);
        }

        /// <summary>
        /// The tick's recorded hits, deaths and events. A mode event of a kind this build lacks, or one naming a course
        /// target or checkpoint today's arena lacks, is left out rather than guessed.
        /// </summary>
        internal static ServerSimulation.TickOutputs Outputs(ReplayViewTick view, ReplayCastIndex cast, ArenaDefinition arena)
        {
            var hits = new SpellResolver.HitResult[view.Hits.Count];
            for (int i = 0; i < hits.Length; i++) hits[i] = Hit(view.Hits[i], view, cast);
            var deaths = new ServerSimulation.DeathEvent[view.Deaths.Count];
            for (int i = 0; i < deaths.Length; i++) deaths[i] = Death(view.Deaths[i], view.Tick, cast);
            var events = new List<TimelinePresentationEvent>(view.Events.Count);
            var slopball = new List<SlopballEvent>();
            var course = new List<CourseTickEvent>();
            var mayhem = new List<MankiMayhemEvent>();
            foreach (var e in view.Events)
                switch (e)
                {
                    case ReplayPresentationEvent p:
                        events.Add(Presentation(p, view.Tick));
                        break;
                    case ReplaySlopballEvent s when SemanticParse.SlopballEvent(s.EventId) is { } kind:
                        slopball.Add(new SlopballEvent(kind, s.BallId, s.Returns, s.ContactTime, SemanticParse.Camp(s.CampId) ?? SlopballCamp.None));
                        break;
                    case ReplayCourseEvent c when SemanticParse.CourseEvent(c.EventId) is { } kind
                        && CourseObjectIndex(arena, kind, c.ObjectId) is { } index:
                        course.Add(new CourseTickEvent(view.Tick, c.EntityId, new CharacterCourseEvent(kind, index, c.ElapsedTicks), c.Sequence));
                        break;
                    case ReplayMayhemEvent m when SemanticParse.MayhemEvent(m.EventId) is { } kind:
                        mayhem.Add(new MankiMayhemEvent(view.Tick, m.RoundIndex, kind, m.EntityId, m.OperationIndex, m.Points));
                        break;
                }
            return new ServerSimulation.TickOutputs(hits, deaths, events, slopball, course, mayhem);
        }

        /// <summary>Today's index of a recorded course target or checkpoint ID; -1 for none; null when today's arena lacks it.</summary>
        internal static int? CourseObjectIndex(ArenaDefinition arena, CharacterCourseEventKind kind, string objectId)
        {
            if (objectId.Length == 0) return -1;
            var course = arena.Arcade?.Course;
            if (course == null) return null;
            if (kind is CharacterCourseEventKind.CheckpointSaved or CharacterCourseEventKind.Respawned)
            {
                for (int i = 0; i < course.Checkpoints.Count; i++) if (course.Checkpoints[i].Id == objectId) return i;
            }
            else if (kind is CharacterCourseEventKind.TargetBroken or CharacterCourseEventKind.TargetHit)
                for (int i = 0; i < course.Targets.Count; i++) if (course.Targets[i].Id == objectId) return i;
            return null;
        }

        static CharacterState Fighter(ReplayFighter f, byte form)
        {
            var state = new CharacterState
            {
                EntityId = f.EntityId,
                State = SemanticParse.Action(f.ActionId) ?? ActionState.Idle,
                MatchState = MatchState.Playing,
                PX = f.Position.X, PY = f.Position.Y, PZ = f.Position.Z,
                VX = f.Velocity.X, VY = f.Velocity.Y, VZ = f.Velocity.Z,
                KVX = f.KnockbackVelocity.X, KVY = f.KnockbackVelocity.Y, KVZ = f.KnockbackVelocity.Z,
                FacingYaw = f.FacingYaw,
                IsGrounded = f.Grounded,
                StateTicks = f.ActionTicks,
                DamagePercent = f.DamagePercent,
                Deaths = f.Deaths,
                JumpsLeft = f.JumpsLeft,
                WallJumpUsed = f.WallJumpUsed,
                InvincibilityTicks = f.InvincibilityTicks,
                LandingLagTicks = f.LandingLagTicks,
                HitstunTicks = f.HitstunTicks,
                HitstopTicks = f.HitstopTicks,
                HitstunLevel = f.HitstunId switch { "medium" => 1, "hard" => 2, _ => 0 },
                HitReactionSequence = f.HitReactionSequence,
                WasAirborneDuringKnockback = f.WasAirborneDuringKnockback,
                InPostHitstunFlight = f.PostHitstunFlight,
                AttackSequence = f.AttackSequence,
                AttackElapsedTicks = f.MoveElapsedTicks,
                AttackPosePitch = f.PosePitch,
                ChargeTicks = f.ChargeTicks,
                IsAiming = SemanticParse.Action(f.ActionId) == ActionState.Aiming,
                Flight = new FlyingState
                {
                    Active = f.FlightActive,
                    FuelTicksLeft = f.FlightFuelLeft,
                    BurstTicksLeft = f.FlightBurstLeft,
                    LiftSpeed = f.FlightLiftSpeed,
                    HeadingYaw = f.FlightHeadingYaw,
                },
                InteractionPhase = (byte)(SemanticParse.Interaction(f.InteractionPhaseId) ?? DefenseInteractionPhase.None),
                InteractionId = f.InteractionId,
                InteractionPartnerId = f.InteractionPartnerId,
                CapturedYaw = (short)Math.Clamp(Math.Round(f.CapturedYaw * 180.0 / Math.PI * 100.0), short.MinValue, short.MaxValue),
                IsFastFalling = f.FastFalling,
                DashDirX = f.DashDirection.X, DashDirY = f.DashDirection.Y, DashDirZ = f.DashDirection.Z,
                AimYaw = f.AimYaw,
                AimPitch = f.AimPitch,
                LockOn = f.LockOn,
                TargetEntityId = f.LockTargetId,
                HomingDashTargetId = f.HomingTargetId,
                HomingDashCooldownTicks = f.HomingCooldownLeft,
                HomingDashAirUsed = f.HomingAirUsed,
            };
            state.Form.Active = form;
            return state;
        }

        /// <summary>The recorded clip when today's move has it, else the stage covering the recorded elapsed ticks.</summary>
        static (int Stage, ushort StageTick, byte Clip) Cursor(CookedSlotDefinition slot, string poseAnimationId, ushort elapsed)
        {
            var stages = slot.Timeline.Stages;
            int clip = poseAnimationId.Length > 0 ? slot.FindClip(poseAnimationId) : -1;
            int stage;
            if (clip >= 0) stage = slot.Clip(clip).StageIndex;
            else
            {
                stage = 0;
                int end = 0;
                for (; stage < stages.Count - 1; stage++)
                {
                    end += stages[stage].DurationTicks;
                    if (elapsed < end) break;
                }
                clip = slot.FirstClipIndex(stage);
            }
            int start = 0;
            for (int i = 0; i < stage; i++) start += stages[i].DurationTicks;
            return (stage, (ushort)Math.Max(0, elapsed - start), (byte)clip);
        }

        static Hitbox Shape(ReplayShape s, ReplayCastIndex cast)
        {
            var member = cast.Member(s.OwnerId);
            byte form = member != null ? cast.Form(member, s.FormId) : (byte)0;
            var move = cast.Move(s.OwnerId, s.PackageId, s.FormId, s.MoveName, s.SlotId, s.ActivationAirborne);
            string? bone = s.TracksOwner && s.AttachmentBoneId.Length > 0 ? s.AttachmentBoneId : null;
            var hitbox = new Hitbox
            {
                X = s.Start.X, Y = s.Start.Y, Z = s.Start.Z,
                EndX = s.End.X, EndY = s.End.Y, EndZ = s.End.Z,
                VX = s.Velocity.X, VY = s.Velocity.Y, VZ = s.Velocity.Z,
                Radius = s.Radius,
                PreviousRadius = s.PreviousRadius,
                RingHalfWidth = s.RingHalfWidth,
                RingHalfHeight = s.RingHalfHeight,
                Shape = SemanticParse.Shape(s.Kind) ?? HitboxShape.Sphere,
                OwnerId = s.OwnerId,
                ActivationId = s.ActivationId,
                ActivationAirborne = s.ActivationAirborne,
                AttackSequence = s.AttackSequence,
                AttackSlot = move.WireSlot,
                Slot = move.WireSlot > 0 ? (byte)(move.WireSlot - 1) : (byte)0,
                Airborne = s.ActivationAirborne,
                Form = form,
                VisualOperationIndex = s.VisualOperationIndex,
                Active = true,
                AgeTicks = s.AgeTicks,
                AttachedEntityId = s.AttachedEntityId,
                FuseTicksLeft = s.FuseTicksLeft,
                StickyFuseTicks = FuseLength(s.FuseTicksLeft, s.FuseProgress),
                MinePhase = SemanticParse.Mine(s.MinePhaseId) ?? MinePhase.None,
                MineTicksLeft = s.MineTicksLeft,
                Return = s.ReturnPhaseId is "out" or "back" ? new ProjectileReturn() : null,
                ReturnLeg = s.ReturnPhaseId == "back",
                TracksBone = s.TracksOwner,
                OwnerWindow = s.SourceKind == "owner-window",
                ContactExplosion = s.VisualKindId == "contact-bomb",
                // The View has no perch phase or spot: a perching shape rests where it was recorded (View v2 adds them).
                Perch = s.VisualKindId == "perching" ? new ProjectilePerch
                {
                    Phase = s.AttachedEntityId != 0 ? PerchPhase.Perched : PerchPhase.Marking,
                    SpotX = s.Start.X, SpotY = s.Start.Y, SpotZ = s.Start.Z,
                } : null,
                SourceEvent = new HitboxEvent
                {
                    BoneName = bone,
                    EndBoneName = bone != null && move.Slot != null ? EndBone(move.Slot, bone) : null,
                    Shape = SemanticParse.Shape(s.Kind) ?? HitboxShape.Sphere,
                    Radius = s.Radius,
                },
                Def = member?.Content.Definition.ForForm(form),
                Baked = member?.Content.BakedAnimation,
            };
            hitbox.EmissionId = s.EmissionId;
            return hitbox;
        }

        /// <summary>The fuse length that gives the recorded progress: progress = 1 - left / length.</summary>
        static ushort FuseLength(ushort left, float progress)
        {
            if (progress <= 0f) return left;
            if (progress >= 1f || left == 0) return 1;
            return (ushort)Math.Clamp(Math.Round(left / (1.0 - progress)), left, ushort.MaxValue);
        }

        /// <summary>The View records the start bone only; today's move supplies the end bone of the same start bone.</summary>
        static string? EndBone(CookedSlotDefinition slot, string bone)
        {
            foreach (var stage in slot.Timeline.Stages)
                foreach (var e in stage.ReadHitboxes())
                    if (e.BoneName == bone && e.EndBoneName != null) return e.EndBoneName;
            return null;
        }

        static SpellResolver.HitResult Hit(ReplayHit h, ReplayViewTick view, ReplayCastIndex cast)
        {
            var move = cast.Move(h.OwnerId, h.PackageId, h.FormId, h.MoveName, h.SlotId, h.ActivationAirborne);
            var member = cast.Member(h.OwnerId);
            var d = h.LaunchDirection;
            double angle = Math.Asin(Math.Clamp(d.Y, -1f, 1f));
            double cos = Math.Cos(angle);
            float dirX, dirZ;
            if (cos > 1e-4) { dirX = (float)(d.X / cos); dirZ = (float)(d.Z / cos); }
            else
            {
                double length = Math.Sqrt(d.X * d.X + d.Z * d.Z);
                dirX = length > 0 ? (float)(d.X / length) : 0f;
                dirZ = length > 0 ? (float)(d.Z / length) : 0f;
            }
            return new SpellResolver.HitResult
            {
                TargetEntityId = h.TargetId,
                OwnerEntityId = h.OwnerId,
                AttackSlot = move.WireSlot,
                ActivationId = h.ActivationId,
                AttackSequence = h.AttackSequence,
                Airborne = h.ActivationAirborne,
                Form = member != null ? cast.Form(member, h.FormId) : (byte)0,
                ReturnPhase = h.ReturnPhaseId switch { "out" => 1, "back" => 2, _ => 0 },
                Damage = h.AppliedDamage,
                DirX = dirX,
                DirZ = dirZ,
                KnockbackAngle = (sbyte)Math.Clamp(Math.Round(angle * 180.0 / Math.PI), sbyte.MinValue, sbyte.MaxValue),
                StunTicks = Stuns(h, view) ? (ushort)1 : (ushort)0,
                ArmorProtected = h.ArmorProtected,
                FreezesOwner = h.FreezesOwner,
                OwnerWindow = h.SourceKind == "owner-window",
                HitX = h.Position.X, HitY = h.Position.Y, HitZ = h.Position.Z,
                HitPresentationId = h.PresentationId.Length > 0 ? h.PresentationId : null,
                ImpactForce = h.ImpactForce,
                HitstopTicks = h.HitstopTicks,
                Blocked = h.Blocked,
                Unblockable = h.Unblockable,
                MatchTick = view.Tick,
                ContactTime = h.ContactTime,
            };
        }

        /// <summary>
        /// The View has no applied stun (View v2 adds it). Pushes carry force and launch too, so a hit stuns when it
        /// did damage, froze anyone, or left its target reacting this tick.
        /// </summary>
        static bool Stuns(ReplayHit h, ReplayViewTick view)
        {
            if (h.AppliedDamage > 0f || h.HitstopTicks > 0) return true;
            foreach (var f in view.Fighters)
                if (f.EntityId == h.TargetId) return f.HitstunTicks > 0 || f.HitstopTicks > 0;
            return false;
        }

        static ServerSimulation.DeathEvent Death(ReplayDeath d, uint tick, ReplayCastIndex cast)
        {
            var move = cast.Move(d.LastHitOwnerId, d.LastHitPackageId, d.LastHitFormId, d.LastHitMoveName, d.LastHitSlotId, false);
            return new ServerSimulation.DeathEvent
            {
                Tick = tick,
                EntityId = d.EntityId,
                State = new CharacterState
                {
                    EntityId = d.EntityId,
                    PX = d.Position.X, PY = d.Position.Y, PZ = d.Position.Z,
                    VX = d.Velocity.X, VY = d.Velocity.Y, VZ = d.Velocity.Z,
                    KVX = d.KnockbackVelocity.X, KVY = d.KnockbackVelocity.Y, KVZ = d.KnockbackVelocity.Z,
                    DamagePercent = d.DamagePercent,
                },
                KillerEntityId = d.KillerId,
                LastHitEntityId = d.LastHitOwnerId,
                LastHitTick = d.LastHitTick,
                LastHitSlot = move.WireSlot,
                Boundary = d.BoundaryId,
            };
        }

        static TimelinePresentationEvent Presentation(ReplayPresentationEvent e, uint tick)
        {
            var p = e.Placement;
            return new TimelinePresentationEvent(tick, e.EntityId, e.OperationIndex, e.PresentationId, e.AttackSequence,
                SemanticParse.Source(e.SourceId) ?? PresentationEventSource.Timeline,
                e.WorldPosition.X, e.WorldPosition.Y, e.WorldPosition.Z, e.WorldYaw)
            {
                Placement = new PresentationPlacement(
                    SemanticParse.Attachment(p.AttachmentId) ?? AuthoringPresentationAttachmentMode.World,
                    p.BoneId.Length > 0 ? p.BoneId : null,
                    p.LocalPosition.X, p.LocalPosition.Y, p.LocalPosition.Z,
                    p.LocalEulerDegrees.X, p.LocalEulerDegrees.Y, p.LocalEulerDegrees.Z,
                    p.LocalScale.X, p.LocalScale.Y, p.LocalScale.Z,
                    p.DurationTicks),
            };
        }
    }

    /// <summary>
    /// Inverse maps of ReplaySemanticIds, derived once by enumerating each enum through the forward switch, so there
    /// is no second hand-written table. Enum.GetValues is AOT-safe.
    /// </summary>
    internal static class SemanticParse
    {
        static Dictionary<string, T> Inverse<T>(Func<T, string> id) where T : struct, Enum
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                string key = id(value);
                if (key != "unknown") map[key] = value;
            }
            return map;
        }

        static readonly Dictionary<string, ActionState> Actions = Inverse<ActionState>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, PresentationEventSource> Sources = Inverse<PresentationEventSource>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, DefenseInteractionPhase> Interactions = Inverse<DefenseInteractionPhase>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, HitboxShape> Shapes = Inverse<HitboxShape>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, MinePhase> Mines = Inverse<MinePhase>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, AuthoringPresentationAttachmentMode> Attachments = Inverse<AuthoringPresentationAttachmentMode>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, SlopballEventKind> SlopballEvents = Inverse<SlopballEventKind>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, SlopballCamp> Camps = Inverse<SlopballCamp>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, CharacterCourseEventKind> CourseEvents = Inverse<CharacterCourseEventKind>(ReplaySemanticIds.Id);
        static readonly Dictionary<string, MankiMayhemEventKind> MayhemEvents = Inverse<MankiMayhemEventKind>(ReplaySemanticIds.Id);

        internal static ActionState? Action(string id) => Actions.TryGetValue(id, out var v) ? v : null;
        internal static PresentationEventSource? Source(string id) => Sources.TryGetValue(id, out var v) ? v : null;
        internal static DefenseInteractionPhase? Interaction(string id) => Interactions.TryGetValue(id, out var v) ? v : null;
        internal static HitboxShape? Shape(string id) => Shapes.TryGetValue(id, out var v) ? v : null;
        internal static MinePhase? Mine(string id) => Mines.TryGetValue(id, out var v) ? v : null;
        internal static AuthoringPresentationAttachmentMode? Attachment(string id) => Attachments.TryGetValue(id, out var v) ? v : null;
        internal static SlopballEventKind? SlopballEvent(string id) => SlopballEvents.TryGetValue(id, out var v) ? v : null;
        internal static SlopballCamp? Camp(string id) => Camps.TryGetValue(id, out var v) ? v : null;
        internal static CharacterCourseEventKind? CourseEvent(string id) => CourseEvents.TryGetValue(id, out var v) ? v : null;
        internal static MankiMayhemEventKind? MayhemEvent(string id) => MayhemEvents.TryGetValue(id, out var v) ? v : null;
    }
}
