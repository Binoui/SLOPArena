using System;
using System.Collections.Generic;
using System.Linq;

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
            OnlineCourseSimulation.Snapshot? Course, MankiMayhemSimulation.Snapshot? Mayhem);

        // A fresh versus rally: what the simulation starts from, used when a recording has no versus record.
        static readonly SlopballVersusSimulation.Snapshot FreshVolley =
            new(new SlopballVersusState(SlopballVersusPhase.WaitingServe, 0, 0, SlopballCamp.Teal, 0, 1, SlopballCamp.None), 0, 0);

        /// <summary>Only the modes the display world runs are decoded; ApplySnapshot writes exactly those.</summary>
        internal static ModeSnapshots Modes(ServerSimulation shell, ReplayViewTick view, ArenaDefinition arena, ReplayCastIndex cast)
        {
            ReplayBall? ball = null; ReplaySlopballRun? run = null; ReplaySlopballVersus? versus = null;
            ReplayCourse? course = null; ReplayMayhem? mayhem = null;
            var coursePlayers = new List<ReplayCoursePlayer>();
            var targets = new List<ReplayCourseTarget>();
            var mayhemPlayers = new List<ReplayMayhemPlayer>();
            var rings = new List<MayhemLandingRing>();
            var slams = new List<MayhemSlam>();
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
                    case ReplayMayhem x: mayhem = x; break;
                    case ReplayMayhemPlayer x: mayhemPlayers.Add(x); break;
                    case ReplayMayhemRing x: rings.Add(new(x.Ordinal, P(x.Position), x.LaunchTick, x.LandingTick, x.FuseTicks)); break;
                    case ReplayMayhemSlam x: slams.Add(new(x.Ordinal, P(x.Position), x.Tick)); break;
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
                shell.MankiMayhem.HasValue && mayhem != null ? new MankiMayhemSimulation.Snapshot(
                    // Seed 0: the director's seed is not recorded, and only ticking reads it.
                    new MankiMayhemState(0, mayhem.RoundIndex, SemanticParse.Card(mayhem.CardId) ?? MankiMayhemCard.Scatter,
                        SemanticParse.MayhemPhase(mayhem.PhaseId) ?? MankiMayhemPhase.Waiting, mayhem.PhaseTick, mayhem.HazardsLaunched),
                    mayhemPlayers.OrderBy(p => p.EntityId)
                        .Select(p => new MayhemPlayer(p.EntityId, p.Score, p.RoundsSurvived, p.Bonuses, p.Benched, p.Withdrawn)).ToArray(),
                    rings.ToArray(), slams.ToArray(), walls.ToArray()) : null);
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
