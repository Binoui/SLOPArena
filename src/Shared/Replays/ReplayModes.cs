using System;
using System.Collections.Generic;
namespace SlopArena.Shared.Replays;

public sealed partial class ReplayCapture
{
    IReadOnlyList<ReplayModeObject> ProjectModes(ServerSimulation sim)
    {
        var a = new List<ReplayModeObject>();
        if (sim.ArcadeBall is
            {
            }
       b) a.Add(new ReplayBall
       {
           EntityId = b.EntityId,
           Position = V(b.Position),
           Velocity = V(b.Velocity),
           Radius = b.Radius,
           DamagePercent = b.DamagePercent,
           HitstopTicks = b.HitstopTicks,
           Active = b.Active,
           Curve = b.Curve
       });
        if (sim.Slopball is
            {
            }
       s) a.Add(new ReplaySlopballRun
       {
           PhaseId = ReplaySemanticIds.Id(s.Phase),
           Returns = s.Returns,
           TotalReturns = s.TotalReturns,
           BestStreak = s.BestStreak,
           ServeInTicks = s.ServeInTicks,
           CountdownTicks = s.CountdownTicks,
           Received = s.Received,
           ReturnArmed = s.ReturnArmed,
           ClearedNet = s.ClearedNet,
           ShotId = ReplaySemanticIds.Id(s.Shot.Kind)
      ,
           Misses = s.Misses
       });
        if (sim.SlopballVersus is
            {
            }
       v) a.Add(new ReplaySlopballVersus
       {
           PhaseId = ReplaySemanticIds.Id(v.Phase),
           TealScore = v.TealScore,
           ClayScore = v.ClayScore,
           ServingCampId = ReplaySemanticIds.Id(v.ServingCamp),
           ServerEntityId = v.ServerEntityId,
           ServeInTicks = v.ServeInTicks,
           WinningCampId = ReplaySemanticIds.Id(v.Winner)
       });
        if (sim.OnlineCourse is
            {
            }
       c)
        {
            var sc = sim.Scramble;
            a.Add(new ReplayCourse
            {
                FormatId = ReplaySemanticIds.Id(c.Format),
                ElapsedTicks = c.ElapsedTicks,
                FinishWindowTicksLeft = c.FinishWindowTicksLeft,
                Ended = c.Ended,
                WinnerId = c.WinnerEntityId,
                LeaderId = sc?.LeaderEntityId ?? 0,
                SafePosition = sc.HasValue ? V(sc.Value.SafeGround.Position) : default,
                SafeYaw = sc?.SafeGround.Yaw ?? 0,
                SafeProgress = sc?.SafeGround.Progress ?? 0
            });
            var def = arena.Arcade!.Course!;
            var players = new List<CoursePlayerProgress>(sim.CoursePlayers);
            players.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
            foreach (var p in players)
            {
                var q = p.Course;
                a.Add(new ReplayCoursePlayer
                {
                    EntityId = p.EntityId,
                    PhaseId = ReplaySemanticIds.Id(q.Phase),
                    ElapsedTicks = q.ElapsedTicks,
                    BrokenTargets = q.BrokenTargets,
                    TotalTargets = q.TotalTargets,
                    SavedCheckpointId = q.SavedCheckpoint < 0 || q.SavedCheckpoint >= def.Checkpoints.Count ? "" : def.Checkpoints[q.SavedCheckpoint].Id,
                    CountdownTicks = q.CountdownTicks,
                    Place = p.Place,
                    Withdrawn = p.Withdrawn,
                    Points = p.Points,
                    LeashTicks = p.LeashTicks,
                    Progress = p.Progress
                });
                for (int i = 0; i < def.Targets.Count; i++)
                {
                    var t = def.Targets[i];
                    a.Add(new ReplayCourseTarget
                    {
                        EntityId = sim.CourseTargetEntityId(p.EntityId, i),
                        PerspectiveEntityId = p.EntityId,
                        TargetId = t.Id,
                        Position = V(t.Position),
                        Radius = t.Radius,
                        Broken = sim.IsCourseTargetBroken(p.EntityId, i),
                        HitsLeft = sim.CourseTargetHitsLeft(p.EntityId, i),
                        BreakerId = sim.CourseTargetBreaker(i)
                    });
                }
            }
        }
        if (sim.MayhemView is { } show)
        {
            a.Add(new ReplayMayhemShow
            {
                Seed = show.Seed,
                TwistCopyKey = (show.Airing as SlopArena.Shared.Mayhem.SoloAiring)?.TwistCopyKey ?? "",
                PairCopyKey = (show.Airing as SlopArena.Shared.Mayhem.RemixAiring)?.PairCopyKey ?? "",
                GoalTwistCopyKey = (show.Airing as SlopArena.Shared.Mayhem.RemixAiring)?.GoalTwistCopyKey ?? "",
                HazardTwistCopyKey = (show.Airing as SlopArena.Shared.Mayhem.RemixAiring)?.HazardTwistCopyKey ?? "",
                Episode = show.Cursor.Episode,
                PhaseId = ReplaySemanticIds.Id(show.Cursor.Phase),
                ChannelId = show.Channel?.CopyKey ?? ""
            });
            foreach (var row in show.Contestants) a.Add(new ReplayMayhemContestant
            {
                EntityId = row.EntityId,
                OnAir = row.OnAir,
                Withdrawn = row.Withdrawn,
                PassMask = row.PassMask,
                FailCauseId = row.Failure is { } f ? ReplaySemanticIds.Id(f.Cause) : "",
                SplatStyleId = row.Failure is { } splat ? ReplaySemanticIds.Id(splat.Style) : "",
                FailTick = row.Failure?.Tick ?? 0,
                Anchor = row.Failure is { } anchor ? new ReplayVector(anchor.Anchor.X, anchor.Anchor.Y, anchor.Anchor.Z) : default,
                AnchorYaw = row.Failure?.Anchor.Yaw ?? 0f
            });
            var scene = show.Scene;
            foreach (var bomb in scene.Bombs) a.Add(new ReplayMayhemBomb
            {
                Lane = bomb.Lane,
                Ordinal = bomb.Ordinal,
                Launched = bomb.Launched,
                Exploded = bomb.Exploded,
                Landing = V(bomb.Landing),
                LaunchTick = bomb.LaunchTick,
                ImpactTick = bomb.ImpactTick,
                BombKindId = ReplaySemanticIds.Id(bomb.Kind),
                BlastRadius = bomb.BlastRadius
            });
            foreach (var r in scene.Rings) a.Add(new ReplayMayhemShockwave
            {
                Lane = r.Lane,
                Ordinal = r.Ordinal,
                Center = V(r.Center),
                SlamTick = r.SlamTick,
                Speed = r.Speed,
                HalfWidth = r.HalfWidth,
                HalfHeight = r.HalfHeight,
                LifetimeTicks = r.LifetimeTicks
            });
            foreach (var z in scene.Zones) a.Add(new ReplayMayhemZone
            {
                Lane = z.Lane,
                Ordinal = z.Ordinal,
                ZoneId = ReplaySemanticIds.Id(z.Kind),
                Center = V(z.Center),
                Radius = z.Radius,
                ActiveFrom = z.ActiveFrom,
                ActiveUntil = z.ActiveUntil,
                EndCenter = V(z.EndCenter),
                EndRadius = z.EndRadius,
                ArmedFrom = z.ArmedFrom,
                HalfDepth = z.HalfDepth, EndHalfDepth = z.EndHalfDepth
            });
            foreach (var w in scene.Watches) a.Add(new ReplayMayhemWatch
            {
                Lane = w.Lane,
                Ordinal = w.Ordinal,
                WarningTick = w.WarningTick,
                WatchKindId = ReplaySemanticIds.Id(w.Kind), CopyKey = w.CopyKey,
                StartTick = w.StartTick,
                EndTick = w.EndTick
            });
            foreach (var carry in scene.Carries) a.Add(new ReplayMayhemCarry { Lane = carry.Lane, Ordinal = carry.Ordinal,
                HolderSeat = carry.HolderSeat, FuseEnd = carry.FuseEnd, LastGiver = carry.LastGiver ?? 255 });
            foreach (var h in scene.HitProgress) a.Add(new ReplayMayhemHitProgress { Seat = h.Seat, Hits = h.Hits, Quota = h.Quota });
            foreach (var t in scene.Targets) a.Add(new ReplayMayhemTarget
            {
                Lane = t.Lane,
                Ordinal = t.Ordinal,
                TargetId = ReplaySemanticIds.Id(t.Kind),
                EntityId = t.EntityId,
                OwnerId = t.OwnerSeat is { } owner && owner < show.Contestants.Count ? show.Contestants[owner].EntityId : 0,
                Center = V(t.Center),
                Radius = t.Radius,
                HalfHeight = t.HalfHeight,
                HitsLeft = t.HitsLeft,
                Hits = t.Hits,
                ActiveFrom = t.ActiveFrom, ActiveUntil = t.ActiveUntil
            });
            foreach (var w in scene.Solids) a.Add(new ReplayMayhemWall
            {
                EntityId = w.EntityId,
                CageEntityId = w.CageEntityId,
                CageCenter = V(w.CageCenter),
                Center = V(w.Center),
                HalfExtents = V(w.HalfExtents),
                Yaw = w.Yaw,
                HitsLeft = w.HitsLeft,
                PhaseId = ReplaySemanticIds.Id(w.Phase),
                PhaseTick = w.PhaseTick
            });
        }
        // Stable ordering preserves the order of objects of the same kind.
        for (int i = 1; i < a.Count; i++)
        {
            var item = a[i];
            int j = i;
            while (j > 0 && StringComparer.Ordinal.Compare(a[j - 1].Kind, item.Kind) > 0) { a[j] = a[j - 1]; j--; }
            a[j] = item;
        }
        return a.AsReadOnly();
    }
}
