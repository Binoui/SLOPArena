using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;

namespace SlopArena.Shared.Replays
{
    /// <summary>
    /// The mode half of the View decoder: each mode record becomes that mode's own restore snapshot. What the View does
    /// not record takes a stated default, never a guess (replay-file.md, mode coverage): a display world is never ticked,
    /// so seeds, shot indices, serve rotation, finish history and scramble ground history are not needed to draw it.
    /// </summary>
    internal static partial class ReplayViewDecoder
    {
        internal readonly record struct ModeSnapshots(
            ArcadeBallSimulation.Snapshot Ball, SlopballSimulation.Snapshot Rules, SlopballVersusSimulation.Snapshot Volley,
            OnlineCourseSimulation.Snapshot? Course, MayhemShow.Snapshot? Mayhem);

        // A fresh versus rally: what the simulation starts from, used when a recording has no versus record.
        static readonly SlopballVersusSimulation.Snapshot FreshVolley =
            new(new SlopballVersusState(SlopballVersusPhase.WaitingServe, 0, 0, SlopballCamp.Teal, 0, 1, SlopballCamp.None), 0, 0);

        /// <summary>Only the modes the display world runs are decoded; ApplySnapshot writes exactly those.</summary>
        internal static ModeSnapshots Modes(ServerSimulation shell, ReplayViewTick view, ArenaDefinition arena, ReplayCastIndex cast)
        {
            ReplayBall? ball = null; ReplaySlopballRun? run = null; ReplaySlopballVersus? versus = null;
            ReplayCourse? course = null; ReplayMayhemShow? show = null;
            var coursePlayers = new List<ReplayCoursePlayer>();
            var targets = new List<ReplayCourseTarget>();
            var contestants = new List<ReplayMayhemContestant>();
            var carries = new List<CarryCue>();
            var progress = new List<HitProgressCue>();
            var bombs = new List<BombCue>();
            var rings = new List<RingCue>();
            var zones = new List<ZoneCue>();
            var watches = new List<WatchCue>();
            var mayhemTargets = new List<ReplayMayhemTarget>();
            var walls = new List<RuntimeSolid>();
            foreach (var o in view.ModeObjects)
                switch (o)
                {
                    case ReplayBall x: ball = x; break;
                    case ReplaySlopballRun x: run = x; break;
                    case ReplaySlopballVersus x: versus = x; break;
                    case ReplayCourse x: course = x; break;
                    case ReplayCoursePlayer x: coursePlayers.Add(x); break;
                    case ReplayCourseTarget x: targets.Add(x); break;
                    case ReplayMayhemShow x: show = x; break;
                    case ReplayMayhemContestant x: contestants.Add(x); break;
                    case ReplayMayhemBomb x:
                        bombs.Add(new BombCue(x.Lane, x.Ordinal, x.Launched, P(x.Landing), x.LaunchTick, x.ImpactTick, x.BlastRadius, x.Exploded, SemanticParse.BombKind(x.BombKindId) ?? throw SnapshotReader.Malformed("Replay bomb kind")));
                        break;
                    case ReplayMayhemShockwave x:
                        rings.Add(new RingCue(x.Lane, x.Ordinal, P(x.Center), x.SlamTick, x.Speed, x.HalfWidth, x.HalfHeight, x.LifetimeTicks));
                        break;
                    case ReplayMayhemZone x when SemanticParse.ZoneKind(x.ZoneId) is { } zone:
                        zones.Add(new ZoneCue(x.Lane, x.Ordinal, zone, P(x.Center), x.Radius, x.ActiveFrom, x.ActiveUntil, P(x.EndCenter), x.EndRadius, x.ArmedFrom, x.HalfDepth, x.EndHalfDepth));
                        break;
                    case ReplayMayhemWatch x: watches.Add(new WatchCue(x.Lane, x.Ordinal, x.WarningTick, x.StartTick, x.EndTick, SemanticParse.WatchKind(x.WatchKindId) ?? throw SnapshotReader.Malformed("Replay watch kind"), x.CopyKey)); break;
                    case ReplayMayhemCarry x: carries.Add(new(x.Lane, x.Ordinal, x.HolderSeat, x.FuseEnd, x.LastGiver == 255 ? null : x.LastGiver)); break;
                    case ReplayMayhemHitProgress x: progress.Add(new(x.Seat, x.Hits, x.Quota)); break;
                    case ReplayMayhemTarget x: mayhemTargets.Add(x); break;
                    // The View has no hit budget: 0 marks it unrecorded, and the presenter draws such a wall neutrally.
                    case ReplayMayhemWall x:
                        walls.Add(new(x.EntityId, x.CageEntityId, P(x.CageCenter), P(x.Center), P(x.HalfExtents), x.Yaw, x.HitsLeft, 0,
                            SemanticParse.WallPhase(x.PhaseId) ?? RuntimeSolidPhase.Active, x.PhaseTick));
                        break;
                }
            return new ModeSnapshots(
                ball != null ? Ball(ball) : default,
                run != null ? Run(run) : default,
                versus != null ? Volley(versus) : FreshVolley,
                shell.OnlineCourse is { } running && arena.Arcade?.Course is { } definition
                    ? Course(course ?? new ReplayCourse(), coursePlayers, targets, definition, cast, running.Format == CourseFormat.Scramble) : null,
                shell.IsMankiMayhem && show is { Seed: not 0 } && contestants.Count is >= 2 and <= 4
                    ? Show(show, view.Tick, contestants, new ChannelScene(bombs, rings, zones, watches, walls, Array.Empty<TargetCue>()) { Carries = carries, HitProgress = progress },
                        mayhemTargets)
                    : null);
        }

        /// <summary>
        /// The show from its recorded seed, verdict rows and cues. The display world gets the recorded scene instead
        /// of a channel: it is never ticked, and a channel is never rebuilt by running its setup. Safe poses are unrecorded.
        /// </summary>
        static MayhemShow.Snapshot Show(ReplayMayhemShow show, uint tick, List<ReplayMayhemContestant> recorded,
            ChannelScene scene, List<ReplayMayhemTarget> targets)
        {
            var rows = recorded.OrderBy(c => c.EntityId).ToArray();
            var roster = rows.Select(c => c.EntityId).ToArray();
            byte onAir = 0;
            var failures = new MayhemFailure?[rows.Length];
            for (int seat = 0; seat < rows.Length; seat++)
            {
                if (rows[seat].OnAir) onAir |= (byte)(1 << seat);
                if (SemanticParse.FailCause(rows[seat].FailCauseId) is { } cause && SemanticParse.SplatStyle(rows[seat].SplatStyleId) is { } style)
                    failures[seat] = new MayhemFailure(cause, style, rows[seat].FailTick,
                        new MayhemPose(rows[seat].Anchor.X, rows[seat].Anchor.Y, rows[seat].Anchor.Z, rows[seat].AnchorYaw));
            }
            var cues = new List<TargetCue>();
            foreach (var t in targets)
                if (SemanticParse.TargetKind(t.TargetId) is { } kind && (t.OwnerId == 0 || Array.IndexOf(roster, t.OwnerId) >= 0))
                    cues.Add(new TargetCue(t.Lane, t.Ordinal, kind, t.EntityId, t.OwnerId == 0 ? null : (byte)Array.IndexOf(roster, t.OwnerId), P(t.Center), t.Radius, t.HalfHeight, t.HitsLeft, t.Hits, t.ActiveFrom, t.ActiveUntil));
            var plan = MayhemShowPlan.For(show.Seed);
            var cursor = plan.At(tick);
            if (cursor.HasEpisode && show.ChannelId.Length > 0
                && (show.ChannelId != MayhemChannels.Find(plan.ChannelAt(cursor.Episode)).CopyKey
                    || plan.AiringAt(cursor.Episode) switch
                    {
                        SoloAiring solo => show.TwistCopyKey != solo.TwistCopyKey || show.PairCopyKey.Length > 0
                            || show.GoalTwistCopyKey.Length > 0 || show.HazardTwistCopyKey.Length > 0,
                        RemixAiring remix => show.TwistCopyKey.Length > 0 || show.PairCopyKey != remix.PairCopyKey
                            || show.GoalTwistCopyKey != remix.GoalTwistCopyKey || show.HazardTwistCopyKey != remix.HazardTwistCopyKey,
                        _ => true,
                    }))
                throw SnapshotReader.Malformed("Replay Mayhem airing");
            int episode = cursor.Phase is MayhemPhase.Call or MayhemPhase.Play or MayhemPhase.Verdict ? cursor.Episode : -1;
            return new MayhemShow.Snapshot(show.Seed, roster, rows.Select(c => c.Withdrawn).ToArray(), onAir,
                rows.Select(c => c.PassMask & MayhemShowPlan.AllEpisodesMask).ToArray(), failures, new MayhemPose[rows.Length],
                episode, null)
            { Display = episode >= 0 ? scene with { Targets = cues } : null };
        }

        static ulong CourseTarget(ServerSimulation shell, ReplayViewTick view, ArenaDefinition arena, ulong perspective, ulong recordedId)
        {
            if (recordedId == 0 || view.Fighters.Any(f => f.EntityId == recordedId)) return recordedId;
            var target = view.ModeObjects.OfType<ReplayCourseTarget>()
                .FirstOrDefault(t => t.PerspectiveEntityId == perspective && t.EntityId == recordedId);
            if (target == null) return 0;
            var targets = arena.Arcade!.Course!.Targets;
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].Id == target.TargetId) return shell.CourseTargetEntityId(perspective, i);
            return 0;

        }

        static ArcadePoint P(ReplayVector v) => new(v.X, v.Y, v.Z);

        static ArcadeBallSimulation.Snapshot Ball(ReplayBall b)
            => new(b.EntityId, b.Position.X, b.Position.Y, b.Position.Z, b.Velocity.X, b.Velocity.Y, b.Velocity.Z,
                b.DamagePercent, b.Curve, b.HitstopTicks, b.Active);

        /// <summary>
        /// The shot's spawn, velocity and flight are not recorded: the shot keeps its recorded serve kind with a zero spawn,
        /// and the viewer places the sender from the recorded ball instead. Seed, shot index and miss budget are 0.
        /// </summary>
        static SlopballSimulation.Snapshot Run(ReplaySlopballRun r)
            => new(SemanticParse.SlopballPhase(r.PhaseId) ?? SlopballPhase.WaitingServe, r.Returns, r.BestStreak, 0, r.TotalReturns,
                0, r.ServeInTicks, r.CountdownTicks, r.Misses, 0, r.Received, r.ReturnArmed, r.ClearedNet,
                new SlopballShot(0, default, default, 0, SemanticParse.Serve(r.ShotId) ?? SlopballServe.Lob));

        /// <summary>The serve rotation counters are not recorded; they only pick the next server when ticking.</summary>
        static SlopballVersusSimulation.Snapshot Volley(ReplaySlopballVersus v)
            => new(new SlopballVersusState(SemanticParse.VersusPhase(v.PhaseId) ?? SlopballVersusPhase.WaitingServe, v.TealScore, v.ClayScore,
                SemanticParse.Camp(v.ServingCampId) ?? SlopballCamp.Teal, v.ServerEntityId, v.ServeInTicks,
                SemanticParse.Camp(v.WinningCampId) ?? SlopballCamp.None), 0, 0);

        /// <summary>
        /// Targets and checkpoints are today's arena layout, matched by recorded ID. A target this arena has but the
        /// recording lacks keeps all its hits; a recorded ID the arena lacks is not placed (the viewer labels it).
        /// The first-finish tick and every racer's ground history are not recorded; only the leader's safe ground is.
        /// </summary>
        static OnlineCourseSimulation.Snapshot Course(ReplayCourse c, List<ReplayCoursePlayer> recorded, List<ReplayCourseTarget> targets,
            CharacterCourseDefinition definition, ReplayCastIndex cast, bool scramble)
        {
            var byId = recorded.ToDictionary(p => p.EntityId);
            var players = new List<(ulong Id, byte Place, bool Withdrawn, CharacterCourseSimulation.Snapshot Course)>();
            var racers = new List<CourseScramble.RacerSnapshot>();
            var safe = new CourseSafeGround(P(c.SafePosition), c.SafeYaw, c.SafeProgress);
            var breakers = new ulong[definition.Targets.Count];
            foreach (var member in cast.Members.OrderBy(m => m.EntityId))
            {
                var p = byId.TryGetValue(member.EntityId, out var found) ? found : new ReplayCoursePlayer { EntityId = member.EntityId };
                var hits = new byte[definition.Targets.Count];
                for (int i = 0; i < hits.Length; i++)
                {
                    var target = targets.FirstOrDefault(t => t.PerspectiveEntityId == p.EntityId && t.TargetId == definition.Targets[i].Id);
                    hits[i] = target != null ? (byte)Math.Clamp(target.HitsLeft, 0, byte.MaxValue) : definition.Targets[i].Hits;
                    if (target != null && target.BreakerId != 0) breakers[i] = target.BreakerId;
                }
                int checkpoint = -1;
                for (int i = 0; i < definition.Checkpoints.Count; i++) if (definition.Checkpoints[i].Id == p.SavedCheckpointId) checkpoint = i;
                players.Add((p.EntityId, p.Place, p.Withdrawn, new CharacterCourseSimulation.Snapshot(
                    SemanticParse.CoursePhase(p.PhaseId) ?? CharacterCoursePhase.Running, p.ElapsedTicks, checkpoint, p.CountdownTicks, hits)));
                racers.Add(new CourseScramble.RacerSnapshot(p.Points, p.LeashTicks, p.Progress, default,
                    p.EntityId == c.LeaderId ? safe : default, new CourseSafeGround[CourseScrambleTuning.SafeGroundLagTicks]));
            }
            bool begun = c.ElapsedTicks > 0 || players.Any(p => p.Course.Phase != CharacterCoursePhase.Ready);
            var snapshot = new OnlineCourseSimulation.Snapshot(begun, c.Ended, c.ElapsedTicks, 0, c.FinishWindowTicksLeft, players.ToArray());
            return scramble
                ? snapshot with { Scramble = new CourseScramble.Snapshot(c.LeaderId, 0, racers.ToArray(), breakers) }
                : snapshot;
        }
    }
}
