using System;
using System.IO;
using System.Collections.Generic;
namespace SlopArena.Shared.Replays;

public sealed partial class ReplayCapture
{
    readonly Dictionary<CookedSlotDefinition, CookedCapabilityParameters?> charges = new();
    CookedCapabilityParameters? Charge(CookedSlotDefinition? move)
    {
        if (move == null) return null;
        if (charges.TryGetValue(move, out var found)) return found;
        foreach (var stage in move.Timeline.Stages)
            foreach (var op in stage.Operations)
                if (op is CookedStartCapabilityOperation c && c.Parameters is CookedChargedProjectileCapabilityParameters or CookedChargedStreamCapabilityParameters)
                { charges.Add(move, c.Parameters); return c.Parameters; }
        charges.Add(move, null);
        return null;
    }
    int ChargeTier(CookedSlotDefinition? m, ushort t) => Charge(m) is CookedChargedProjectileCapabilityParameters p ? p.GetChargeTier(t) : -1;
    float ChargeProgress(CookedSlotDefinition? m, ushort t)
    {
        ushort full = Charge(m) switch
        {
            CookedChargedProjectileCapabilityParameters p => p.MaxChargeTicks,
            CookedChargedStreamCapabilityParameters p => p.MaxChargeTicks,
            _ => (ushort)0
        };
        return full == 0 ? 0 : Math.Min(1, t / (float)full);
    }
    int ChargeSteps(CookedSlotDefinition? m, ushort t) => t == 0 ? 0 : Charge(m) switch
    {
        CookedChargedProjectileCapabilityParameters p => p.GetChargeTier(t) + 1,
        CookedChargedStreamCapabilityParameters _ => (int)MathF.Ceiling(ChargeProgress(m, t) * 3),
        _ => 0
    };
    readonly Dictionary<(ulong Entity, byte Form), int> flightFuel = new();
    static readonly byte[] CooldownSlots = { AbilitySlots.Normal1, AbilitySlots.Normal2, AbilitySlots.Normal3, AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F };
    IReadOnlyList<ReplayCooldown> Cooldowns(ReplayParticipant p, CharacterState s)
    {
        var def = rosterContent[p.EntityId].Definition.ForForm(s.Form.Active);
        List<ReplayCooldown>? list = null;
        foreach (byte slot in CooldownSlots) for (int variant = 0; variant < 2; variant++)
            {
                bool air = variant != 0;
                var m = def.GetCookedSlotAbility(slot, air);
                if (m == null) continue;
                ushort left = s.GetCooldown(slot);
                var key = (p.EntityId, s.Form.Active, slot, air);
                if (left == 0) { cooldownRuns.Remove(key); continue; }
                if (!cooldownRuns.TryGetValue(key, out var run) || left > run.Left) run = (left, left);
                cooldownRuns[key] = (run.Total, left);
                (list ??= new List<ReplayCooldown>()).Add(new ReplayCooldown
                {
                    PackageId = p.Package.PackageId,
                    FormId = ReplaySemanticIds.Form(s.Form.Active),
                    SlotId = m.Id,
                    MoveName = m.Name,
                    TicksLeft = left,
                    DurationTicks = run.Total
                });
            }
        return list == null ? Array.Empty<ReplayCooldown>() : list.AsReadOnly();
    }
    ReplayViewTick ProjectView(ServerSimulation sim, WorldFramePacket frame)
    {
        // Resolver already returns an owned snapshot containing only active shapes.
        var shapes = sim.Resolver.GetActiveHitboxes();
        shapes.Sort((a, b) => a.EmissionId.CompareTo(b.EmissionId));
        var fighters = new ReplayFighter[header.Roster.Count];
        for (int fighterIndex = 0; fighterIndex < fighters.Length; fighterIndex++)
        {
            var p = header.Roster[fighterIndex];
            ulong id = p.EntityId;
            var s = sim.GetState(id);
            var m = sim.GetActiveMove(id);
            Hitbox ret = default;
            foreach (var h in shapes) if (h.OwnerId == id && h.Return != null) { ret = h; break; }
            var rm = Move(id, ret.Form, ret.AttackSlot, ret.ActivationAirborne);
            var fuelKey = (id, s.Form.Active);
            if (!flightFuel.TryGetValue(fuelKey, out var fuel))
            {
                fuel = Flying.Parameters(sim.GetDefinition(id))?.FuelTicks ?? 0;
                flightFuel.Add(fuelKey, fuel);
            }
            fighters[fighterIndex] = new ReplayFighter
            {
                EntityId = id,
                PackageId = p.Package.PackageId,
                ActionId = ReplaySemanticIds.Id(s.State),
                Position = V(s.PX, s.PY, s.PZ),
                Velocity = V(s.VX, s.VY, s.VZ),
                KnockbackVelocity = V(s.KVX, s.KVY, s.KVZ),
                FacingYaw = s.FacingYaw,
                Grounded = s.IsGrounded,
                ActionTicks = s.StateTicks,
                DamagePercent = s.DamagePercent,
                Deaths = s.Deaths,
                KOs = sim.GetKOs(id),
                JumpsLeft = s.JumpsLeft,
                WallJumpUsed = s.WallJumpUsed,
                InvincibilityTicks = s.InvincibilityTicks,
                LandingLagTicks = s.LandingLagTicks,
                HitstunTicks = s.HitstunTicks,
                HitstopTicks = s.HitstopTicks,
                HitstunId = s.HitstunTicks == 0 ? "none" : s.HitstunLevel switch
                {
                    0 => "small",
                    1 => "medium",
                    _ => "hard"
                }
         ,
                HitReactionSequence = s.HitReactionSequence,
                WasAirborneDuringKnockback = s.WasAirborneDuringKnockback,
                PostHitstunFlight = s.InPostHitstunFlight,
                FormId = ReplaySemanticIds.Form(s.Form.Active),
                SlotId = m?.Slot.Id ?? "",
                MoveName = m?.Slot.Name ?? "",
                ActivationId = m.HasValue ? sim.GetLastActivationId(id) : 0,
                AttackSequence = s.AttackSequence,
                ActivationAirborne = m?.AirborneAtStart ?? false,
                MoveFormId = m.HasValue ? ReplaySemanticIds.Form(m.Value.Slot.Form) : "base",
                MoveElapsedTicks = s.AttackElapsedTicks,
                PoseAnimationId = m?.Pose.AnimationId ?? "",
                PoseElapsedTicks = m?.Pose.ElapsedTicks ?? 0,
                PoseDurationTicks = m?.Pose.DurationTicks ?? 0,
                PhaseClip = m?.Pose.IsPhaseClip ?? false,
                PosePitch = s.AttackPosePitch,
                ChargeTicks = s.ChargeTicks,
                AimAnimationId = m?.Slot.AimAnimationId ?? "",
                AimMovementId = m.HasValue ? ReplaySemanticIds.Id(m.Value.Slot.AimMovement) : "fixed",
                ReturningSlotId = rm.Slot,
                ReturningFormId = rm.Form,
                ReturningPackageId = ret.Return == null ? "" : rm.Package,
                ReturningMoveName = rm.Name,
                FlightActive = s.Flight.Active,
                FlightFuelLeft = s.Flight.FuelTicksLeft,
                FlightBurstLeft = s.Flight.BurstTicksLeft,
                FlightLiftSpeed = s.Flight.LiftSpeed,
                FlightHeadingYaw = s.Flight.HeadingYaw,
                FlightElapsedTicks = Math.Max(0, fuel - s.Flight.FuelTicksLeft),
                FlightFuelFraction = fuel == 0 ? 0 : s.Flight.FuelTicksLeft / (float)fuel,
                InteractionPhaseId = ReplaySemanticIds.Id((DefenseInteractionPhase)s.InteractionPhase),
                InteractionId = s.InteractionId,
                InteractionPartnerId = s.InteractionPartnerId,
                CapturedYaw = s.CapturedYaw * .01f * MathF.PI / 180f,
                Inactive = sim.IsEntityInactive(id),
                FastFalling = s.IsFastFalling,
                DashDirection = V(s.DashDirX, s.DashDirY, s.DashDirZ),
                AimYaw = s.AimYaw,
                AimPitch = s.AimPitch,
                LockOn = s.LockOn,
                LockTargetId = s.TargetEntityId,
                ChargeTier = ChargeTier(m?.Slot, s.ChargeTicks),
                ChargeProgress = ChargeProgress(m?.Slot, s.ChargeTicks),
                ChargeStepsReached = ChargeSteps(m?.Slot, s.ChargeTicks),
                HomingCooldownLeft = s.HomingDashCooldownTicks,
                HomingAirUsed = s.HomingDashAirUsed,
                Cooldowns = Cooldowns(p, s),
                HomingTargetId = s.HomingDashTargetId
            };
        }
        var events = new List<ReplayEvent>();
        foreach (var e in frame.Events)
        {
            if (e.MatchTick != frame.Tick) continue;
            var p = e.Placement;
            events.Add(new ReplayPresentationEvent
            {
                EntityId = e.EntityId,
                AttackSequence = e.AttackSequence,
                SourceId = ReplaySemanticIds.Id(e.Source),
                OperationIndex = e.OperationIndex,
                PresentationId = e.PresentationId,
                WorldPosition = V(e.WorldX, e.WorldY, e.WorldZ),
                WorldYaw = e.WorldYaw,
                Placement = new()
                {
                    AttachmentId = ReplaySemanticIds.Id(p.AttachmentMode),
                    BoneId = p.BoneId ?? "",
                    LocalPosition = V(p.LocalPositionX, p.LocalPositionY, p.LocalPositionZ),
                    LocalEulerDegrees = V(p.LocalRotationX, p.LocalRotationY, p.LocalRotationZ),
                    LocalScale = V(p.LocalScaleX, p.LocalScaleY, p.LocalScaleZ),
                    DurationTicks = p.DurationTicks
                }
            });
        }
        foreach (var e in frame.SlopballEvents) if (e.MatchTick == frame.Tick) events.Add(new ReplaySlopballEvent
        {
            EventId = ReplaySemanticIds.Id(e.Event.Kind),
            BallId = e.Event.BallId,
            Returns = e.Event.Returns,
            ContactTime = e.Event.ContactTime,
            CampId = ReplaySemanticIds.Id(e.Event.Camp)
        });
        foreach (var e in frame.CourseEvents) if (e.MatchTick == frame.Tick) events.Add(new ReplayCourseEvent
        {
            EntityId = e.EntityId,
            EventId = ReplaySemanticIds.Id(e.Event.Kind),
            ObjectId = CourseObject(e.Event),
            ElapsedTicks = e.Event.ElapsedTicks,
            Sequence = e.Sequence
        });
        foreach (var e in frame.MayhemEvents) if (e.MatchTick == frame.Tick) events.Add(new ReplayMayhemEvent
        {
            EntityId = e.EntityId,
            EventId = ReplaySemanticIds.Id(e.Kind),
            RoundIndex = e.RoundIndex,
            OperationIndex = e.OperationIndex,
            Points = e.Points
        });
        foreach (var hit in sim.LastTickHits) lastHits[(hit.TargetEntityId, hit.OwnerEntityId, hit.MatchTick, hit.AttackSlot)] = hit;
        var vh = new ReplayHit[sim.LastTickHits.Count];
        for (int i = 0; i < vh.Length; i++)
        {
            var h = sim.LastTickHits[i];
            var live = h;
            var m = Move(h.OwnerEntityId, h.Form, h.AttackSlot, h.Airborne);
            float rad = h.KnockbackAngle * MathF.PI / 180f;
            vh[i] = new ReplayHit
            {
                Occurrence = checked((ushort)i),
                OwnerId = h.OwnerEntityId,
                TargetId = h.TargetEntityId,
                ActivationId = h.ActivationId,
                AttackSequence = h.AttackSequence,
                SlotId = m.Slot,
                FormId = m.Form,
                PackageId = m.Package,
                MoveName = m.Name,
                ActivationAirborne = h.Airborne,
                Position = V(h.HitX, h.HitY, h.HitZ),
                ContactTime = h.ContactTime,
                AppliedDamage = h.Damage,
                ImpactForce = h.ImpactForce,
                LaunchDirection = V(h.DirX * MathF.Cos(rad), MathF.Sin(rad), h.DirZ * MathF.Cos(rad)),
                HitstopTicks = h.HitstopTicks,
                Blocked = h.Blocked,
                ArmorProtected = h.ArmorProtected,
                FreezesOwner = h.FreezesOwner,
                PresentationId = live.HitPresentationId ?? "",
                ReturnPhaseId = live.ReturnPhase switch
                {
                    0 => "none",
                    1 => "out",
                    2 => "back",
                    _ => "unknown"
                }
            ,
                SourceKind = live.OwnerWindow ? "owner-window" : m.Slot != "" ? "move" : h.OwnerEntityId == ServerSimulation.MayhemHostEntityId ? "host-hazard" : "defense",
                Unblockable = h.Unblockable
            };
        }
        var projectedShapes = new ReplayShape[shapes.Count];
        for (int i = 0; i < shapes.Count; i++)
        {
            var h = shapes[i];
            projectedShapes[i] = ProjectShape(h);
        }
        var deaths = new List<ReplayDeath>();
        foreach (var d in sim.LastTickDeaths)
        {
            if (d.Tick != frame.Tick) continue;
            deaths.Add(ProjectDeath(d));
        }
        return new ReplayViewTick
        {
            Tick = frame.Tick,
            MatchPhase = "playing",
            Fighters = Array.AsReadOnly(fighters),
            Shapes = Array.AsReadOnly(projectedShapes),
            ModeObjects = ProjectModes(sim),
            Events = events.AsReadOnly(),
            Hits = Array.AsReadOnly(vh),
            Deaths = deaths.AsReadOnly()
        };
    }
    ReplayShape ProjectShape(Hitbox h)
    {
        var m = Move(h.OwnerId, h.Form, h.AttackSlot, h.ActivationAirborne);
        return new ReplayShape
        {
            EmissionId = h.EmissionId,
            OwnerId = h.OwnerId,
            ActivationId = h.ActivationId,
            Kind = ReplaySemanticIds.Id(h.Shape),
            SourceKind = h.OwnerWindow ? "owner-window" : m.Slot != "" ? "move" : h.OwnerId == ServerSimulation.MayhemHostEntityId ? "host-hazard" : "defense",
            Start = V(h.X, h.Y, h.Z),
            End = V(h.EndX, h.EndY, h.EndZ),
            Velocity = V(h.VX, h.VY, h.VZ),
            Radius = h.Radius,
            PreviousRadius = h.PreviousRadius,
            RingHalfWidth = h.RingHalfWidth,
            RingHalfHeight = h.RingHalfHeight,
            SlotId = m.Slot,
            FormId = m.Form,
            PackageId = m.Package,
            MoveName = m.Name,
            ActivationAirborne = h.ActivationAirborne,
            VisualOperationIndex = h.VisualOperationIndex,
            PresentationId = ShapePresentation(h),
            AttackSequence = h.AttackSequence,
            AgeTicks = h.AgeTicks,
            AttachmentBoneId = h.AttachedEntityId != 0 && h.Perch != null ? "bone.head" : h.TracksBone ? h.SourceEvent.BoneName ?? "" : "",
            AttachedEntityId = h.AttachedEntityId,
            FuseTicksLeft = h.FuseTicksLeft,
            MinePhaseId = ReplaySemanticIds.Id(h.MinePhase),
            MineTicksLeft = h.MineTicksLeft,
            ReturnPhaseId = h.Return == null ? "none" : h.ReturnLeg ? "back" : "out",
            TracksOwner = h.TracksBone,
            VisualKindId = h.Perch != null ? "perching" : h.ContactExplosion ? "contact-bomb" : h.TracksBone ? "melee" : "projectile",
            FuseProgress = h.StickyFuseTicks == 0 ? 0 : 1 - h.FuseTicksLeft / (float)h.StickyFuseTicks
        };
    }
    ReplayDeath ProjectDeath(ServerSimulation.DeathEvent d)
    {
        var found = lastHits.TryGetValue((d.EntityId, d.LastHitEntityId, d.LastHitTick, d.LastHitSlot), out var hit);
        var move = found ? Move(d.LastHitEntityId, hit.Form, d.LastHitSlot, hit.Airborne) : (Package: "", Form: "", Slot: "", Name: "");
        return new ReplayDeath
        {
            LastHitPackageId = move.Package,
            LastHitFormId = move.Form,
            LastHitSlotId = move.Slot,
            LastHitMoveName = move.Name,
            EntityId = d.EntityId,
            Position = V(d.State.PX, d.State.PY, d.State.PZ),
            Velocity = V(d.State.VX, d.State.VY, d.State.VZ),
            KnockbackVelocity = V(d.State.KVX, d.State.KVY, d.State.KVZ),
            BoundaryId = d.Boundary ?? "",
            KillerId = d.KillerEntityId,
            LastHitOwnerId = d.LastHitEntityId,
            LastHitTick = d.LastHitTick,
            DamagePercent = d.State.DamagePercent
        };
    }
    string CourseObject(CharacterCourseEvent e)
    {
        var c = arena.Arcade?.Course;
        if (c == null || e.Index < 0) return "";
        return e.Kind is CharacterCourseEventKind.CheckpointSaved or CharacterCourseEventKind.Respawned ? (e.Index < c.Checkpoints.Count ? c.Checkpoints[e.Index].Id : "") : e.Kind is CharacterCourseEventKind.TargetBroken or CharacterCourseEventKind.TargetHit ? (e.Index < c.Targets.Count ? c.Targets[e.Index].Id : "") : "";
    }
}
