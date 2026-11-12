using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using SlopArena.Shared.Replays;
using UnityEngine;

namespace SlopArena.Client.Replays
{
    /// <summary>What a Replay's mode presenters need from the viewer; the perspective is the followed fighter.</summary>
    internal readonly struct ReplayModeScene
    {
        public ReplayModeScene(Transform parent, Transform stage, HUDManager hud, CameraMount camera, Func<UnityEngine.Camera> renderCamera,
            ProjectileVFXManager projectiles, IReadOnlyDictionary<ulong, PlayerRenderer> fighters, IReadOnlyList<HUDManager.HudPlayer> players,
            MatchContentCatalog catalog, Func<ulong> perspective)
        {
            Parent = parent; Stage = stage; Hud = hud; Camera = camera; RenderCamera = renderCamera; Projectiles = projectiles;
            Fighters = fighters; Players = players; Catalog = catalog; Perspective = perspective;
        }

        public Transform Parent { get; }
        public Transform Stage { get; }
        public HUDManager Hud { get; }
        public CameraMount Camera { get; }
        public Func<UnityEngine.Camera> RenderCamera { get; }
        public ProjectileVFXManager Projectiles { get; }
        public IReadOnlyDictionary<ulong, PlayerRenderer> Fighters { get; }
        public IReadOnlyList<HUDManager.HudPlayer> Players { get; }
        public MatchContentCatalog Catalog { get; }
        public Func<ulong> Perspective { get; }
    }

    /// <summary>
    /// The online mode presenters of one Replay (Slopball, Course or Mayhem), chosen by the file's format and fed the
    /// display world in PvPMatch's order. Each is the live presenter with the followed fighter as its perspective.
    /// A stock Replay has none.
    /// </summary>
    internal sealed class ReplayModes : IDisposable
    {
        private readonly OnlineSlopball? _slopball;
        private ReplayWorld? _replay;
        private readonly List<(uint Tick, ReplayVector Position)> _serves = new();
        private readonly List<(uint Tick, ulong Combo, bool Reaction)> _courtTicks = new();

        private ReplayModes WithHistory(ReplayWorld replay)
        {
            _replay = replay;
            foreach (var tick in replay.ReadViews(replay.Range.Last))
            {
                _courtTicks.Add((tick.Tick, tick.ModeObjects.OfType<ReplaySlopballRun>().FirstOrDefault()?.Returns ?? 0,
                    tick.Events.OfType<ReplaySlopballEvent>().Any(e => e.EventId is "returned" or "point-scored")));
                if (tick.Events.OfType<ReplaySlopballEvent>().Any(e => e.EventId is "served" or "serve-ready")
                    && tick.ModeObjects.OfType<ReplayBall>().FirstOrDefault() is { } ball)
                    _serves.Add((tick.Tick, ball.Position));
            }
            return this;
        }
        private readonly OnlineCharacterCourse? _course;
        private readonly MankiMayhemPresentation? _mayhem;
        private readonly float? _courtNetZ;
        private readonly CharacterCourseDefinition? _courseDefinition;
        private readonly List<SlopballTickEvent> _slopballEvents = new();
        private List<(ulong Key, string Text, Vector3 Position)>? _courseLabels;

        private ReplayModes(OnlineSlopball? slopball, OnlineCharacterCourse? course, MankiMayhemPresentation? mayhem, float? courtNetZ,
            CharacterCourseDefinition? courseDefinition = null)
        {
            _slopball = slopball; _course = course; _mayhem = mayhem; _courtNetZ = courtNetZ; _courseDefinition = courseDefinition;
        }

        /// <summary>
        /// Course targets that do not line up with today's arena, labelled where they stand: a recorded target this arena
        /// no longer has, and a target this arena has that the Replay never recorded. Empty for other modes.
        /// </summary>
        public IReadOnlyList<(ulong Key, string Text, Vector3 Position)> Labels
            => (IReadOnlyList<(ulong, string, Vector3)>?)_courseLabels ?? Array.Empty<(ulong, string, Vector3)>();

        /// <summary>The Mayhem host rig, which the timeline dispatcher draws host hazards from.</summary>
        public MankiHostPresenter? MayhemHost => _mayhem?.Host;

        /// <summary>Null for a stock Replay or a format this build does not know.</summary>
        public static ReplayModes? Create(ReplayWorld replay, in ReplayModeScene scene)
        {
            if (!MatchFormats.TryParseWire(replay.Header.Rules.FormatId, out var format) || format == MatchFormat.Stock) return null;
            var arena = replay.Arena;
            // The View track records no shot spawn, blast radius or call time: those presenters use their stated fallbacks.
            bool view = replay.Track == ReplayTrack.View;
            if (format.IsSlopball() && arena.Arcade?.Slopball != null)
                return new ReplayModes(new OnlineSlopball(scene.Parent, scene.Stage, arena, scene.Hud, scene.RenderCamera, format, scene.Perspective,
                    senderFollowsBall: view, replayCourt: true), null, null, arena.Arcade.Slopball.NetZ).WithHistory(replay);
            if (format.IsCourse() && arena.Arcade?.Course != null)
                return new ReplayModes(null, new OnlineCharacterCourse(scene.Parent, arena, scene.Hud, scene.RenderCamera,
                    Roster(replay), scene.Perspective), null, null, arena.Arcade.Course);
            if (format == MatchFormat.MankiMayhem && arena.Arcade?.Mayhem != null && scene.Catalog.ResolvePackage("manki") is { } manki)
            {
                var friendlyFire = replay.Header.Rules.FriendlyFireId switch
                {
                    "on" => FriendlyFireMode.On, "shoves" => FriendlyFireMode.Shoves, _ => FriendlyFireMode.Off,
                };
                return new ReplayModes(null, null, new MankiMayhemPresentation(scene.Parent, replay.World, arena.Arcade.Mayhem, manki, scene.Catalog,
                    scene.Fighters, scene.Players, scene.Hud, scene.Projectiles, scene.Camera, scene.RenderCamera, friendlyFire)
                    { FixedTelegraphs = view }, null);
            }
            return null;
        }

        /// <summary>The course board's rows: the fighter and short participant ID, in the recorded seat colour.</summary>
        private static IReadOnlyList<LobbyPlayerInfo> Roster(ReplayWorld replay)
            => replay.Cast.Select(m => new LobbyPlayerInfo(0, ReplayText.Fighter(m), null, true, false, (int)m.EntityId,
                SlopballCamps.TryParse(m.Recorded.CampId, out var camp) ? camp : SlopballCamp.None,
                byte.TryParse(m.Recorded.ColourId, NumberStyles.None, CultureInfo.InvariantCulture, out byte colour) ? colour : (byte)0)).ToList();

        /// <summary>
        /// One presented tick, after the fighters. Returns true when the followed fighter is locked on a mode object
        /// (the ball, a course target), which then owns the lock camera.
        /// </summary>
        public bool Present(in ReplayFrame frame, ulong focus, Transform focusTransform, CameraMount? camera)
        {
            var world = frame.World;
            if (_courseDefinition != null) _courseLabels = CourseLabels(frame.View, _courseDefinition, focus);
            if (_mayhem != null)
            {
                _mayhem.Host.Present(world, world.LastTickMayhemEvents);
                _mayhem.AfterTick(world.LastTickMayhemEvents);
            }
            _course?.AfterUpdate(world, world.LastTickOnlineCourseEvents, Array.Empty<CourseTickEvent>(), false);
            var state = world.GetState(focus);
            if (_slopball != null && world.ArcadeBall is { } ball)
            {
                _slopballEvents.Clear();
                foreach (var e in world.LastTickSlopballEvents) _slopballEvents.Add(new SlopballTickEvent(world.CurrentTick, e));
                _slopball.AfterUpdate(world, world.LastTickHits, _slopballEvents);
                if (_replay?.Track == ReplayTrack.Exact && world.Slopball is { } run) _slopball.RestoreSender(run.Shot.Spawn);
                _slopball.PresentReplayCourt(world.Slopball?.Returns ?? 0,
                    _slopballEvents.Any(e => e.Event.Kind is SlopballEventKind.Returned or SlopballEventKind.PointScored));
                camera?.SetCourt(_courtNetZ, ball.Active ? _slopball.BallAnchor.position : null);
                if (state.LockOn && ball.Active && state.TargetEntityId == ball.EntityId)
                {
                    camera?.SetLockFocus(focusTransform, _slopball.BallAnchor.position);
                    return true;
                }
            }
            else if (_course != null && state.LockOn && _course.TryTarget(world, state, out var target, out _))
            {
                camera?.SetLockFocus(focusTransform, target.position);
                return true;
            }
            return false;
        }

        /// <summary>The target set is the arena's, so the first presented tick decides the labels for the whole Replay.</summary>
        private static List<(ulong Key, string Text, Vector3 Position)> CourseLabels(ReplayViewTick view, CharacterCourseDefinition course, ulong focus)
        {
            var recorded = new Dictionary<string, ReplayCourseTarget>();
            foreach (var target in view.ModeObjects.OfType<ReplayCourseTarget>().Where(t => t.PerspectiveEntityId == focus)) recorded.TryAdd(target.TargetId, target);
            var today = new HashSet<string>(course.Targets.Select(t => t.Id));
            var labels = new List<(ulong, string, Vector3)>();
            foreach (var target in recorded.Values.Where(t => !today.Contains(t.TargetId)))
                labels.Add((target.EntityId, $"Target {target.TargetId}: not on today's course",
                    new Vector3(target.Position.X, target.Position.Y + target.Radius + .4f, target.Position.Z)));
            for (int i = 0; i < course.Targets.Count; i++)
            {
                var target = course.Targets[i];
                // Keys from the top of the range never meet fighter or recorded target entity IDs; the label sits higher
                // so it never covers a renamed target's label on the same spot.
                if (recorded.TryGetValue(target.Id, out var observation) && observation.HitsLeft > target.Hits)
                    labels.Add((observation.EntityId, $"Target {target.Id}: {observation.HitsLeft} recorded hits left exceeds today's budget {target.Hits}; neutral damage",
                        new Vector3(target.Position.X, target.Position.Y + target.Radius + .9f, target.Position.Z)));
                if (!recorded.ContainsKey(target.Id))
                    labels.Add((ulong.MaxValue - (ulong)i, $"Target {target.Id}: not in this Replay",
                        new Vector3(target.Position.X, target.Position.Y + target.Radius + .9f, target.Position.Z)));
            }
            var fighter = view.Fighters.FirstOrDefault(f => f.EntityId == focus);
            if (fighter is { LockOn: true } && recorded.Values.FirstOrDefault(t => t.EntityId == fighter.LockTargetId) is { } locked
                && !today.Contains(locked.TargetId))
                labels.Add((ulong.MaxValue - (ulong)course.Targets.Count, $"Lock cleared: target {locked.TargetId} is absent",
                    new Vector3(locked.Position.X, locked.Position.Y + locked.Radius + 1.4f, locked.Position.Z)));
            return labels;
        }

        /// <summary>Per rendered frame, as PvPMatch.Update: the ball, its sender and pops, and the course cues.</summary>
        public void Update(ServerSimulation world, UnityEngine.Camera camera)
        {
            _course?.Update();
            _slopball?.Update(Time.deltaTime, world, camera);
        }

        /// <summary>A seek, or a new followed fighter: every mode presenter shows nothing from before.</summary>
        public void Reset(uint targetTick)
        {
            _slopball?.Reset();
            if (_slopball != null && _replay != null)
            {
                uint first = Math.Max(_replay.Range.First, targetTick >= 30 ? targetTick - 30 : 0);
                foreach (var tick in _courtTicks)
                {
                    if (tick.Tick >= first) break;
                    _slopball.PresentReplayCourt(tick.Combo, tick.Reaction);
                }
            }
            if (_slopball != null && _replay?.Track == ReplayTrack.View)
            {
                uint first = Math.Max(_replay.Range.First, targetTick >= 30 ? targetTick - 30 : 0);
                ReplayVector? serve = null;
                foreach (var item in _serves)
                    if (item.Tick <= first) serve = item.Position;
                if (serve is { } position) _slopball.RestoreSender(new ArcadePoint(position.X, position.Y, position.Z));
            }
            _course?.Reset();
            _mayhem?.Reset();
        }

        public void Dispose()
        {
            _slopball?.Dispose();
            _course?.Dispose();
            _mayhem?.Dispose();
        }
    }
}
