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
        if (sim.MankiMayhem is
            {
            }
       m)
        {
            a.Add(new ReplayMayhem
            {
                RoundIndex = m.RoundIndex,
                CardId = ReplaySemanticIds.Id(m.Card),
                PhaseId = ReplaySemanticIds.Id(m.Phase),
                PhaseTick = m.PhaseTick,
                HazardsLaunched = m.HazardsLaunched
            });
            var players = new List<MayhemPlayer>(sim.MayhemPlayers);
            players.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
            foreach (var p in players) a.Add(new ReplayMayhemPlayer
            {
                EntityId = p.EntityId,
                Score = p.Score,
                RoundsSurvived = p.RoundsSurvived,
                Bonuses = p.Bonuses,
                Benched = p.Benched,
                Withdrawn = p.Withdrawn
            });
            foreach (var r in sim.MayhemLandingRings) a.Add(new ReplayMayhemRing
            {
                Ordinal = r.Ordinal,
                Position = V(r.Position),
                LaunchTick = r.LaunchTick,
                LandingTick = r.LandingTick,
                FuseTicks = r.FuseTicks
            });
            foreach (var sl in sim.MayhemSlams) a.Add(new ReplayMayhemSlam
            {
                Ordinal = sl.Ordinal,
                Position = V(sl.Position),
                Tick = sl.Tick
            });
            foreach (var w in sim.MayhemWalls) a.Add(new ReplayMayhemWall
            {
                EntityId = w.EntityId,
                CageEntityId = w.CageEntityId,
                CageCenter = V(w.CageCenter),
                Center = V(w.Center),
                HalfExtents = V(w.HalfExtents),
                Yaw = w.Yaw,
                HitsLeft = w.HitsLeft,
                PhaseId = ReplaySemanticIds.Id(w.Phase)
            ,
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
