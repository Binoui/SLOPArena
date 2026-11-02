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
        private readonly OnlineCharacterCourse? _course;
        private readonly MankiMayhemPresentation? _mayhem;
        private readonly float? _courtNetZ;
        private readonly List<SlopballTickEvent> _slopballEvents = new();

        private ReplayModes(OnlineSlopball? slopball, OnlineCharacterCourse? course, MankiMayhemPresentation? mayhem, float? courtNetZ)
        {
            _slopball = slopball; _course = course; _mayhem = mayhem; _courtNetZ = courtNetZ;
        }

        /// <summary>The Mayhem host rig, which the timeline dispatcher draws host hazards from.</summary>
        public MankiHostPresenter? MayhemHost => _mayhem?.Host;

        /// <summary>Null for a stock Replay or a format this build does not know.</summary>
        public static ReplayModes? Create(ReplayWorld replay, in ReplayModeScene scene)
        {
            if (!MatchFormats.TryParseWire(replay.Header.Rules.FormatId, out var format) || format == MatchFormat.Stock) return null;
            var arena = replay.Arena;
            if (format.IsSlopball() && arena.Arcade?.Slopball != null)
                return new ReplayModes(new OnlineSlopball(scene.Parent, scene.Stage, arena, scene.Hud, scene.RenderCamera, format, scene.Perspective),
                    null, null, arena.Arcade.Slopball.NetZ);
            if (format.IsCourse() && arena.Arcade?.Course != null)
                return new ReplayModes(null, new OnlineCharacterCourse(scene.Parent, arena, scene.Hud, scene.RenderCamera,
                    Roster(replay), scene.Perspective), null, null);
            if (format == MatchFormat.MankiMayhem && arena.Arcade?.Mayhem != null && scene.Catalog.Resolve(CharacterClass.Manki) is { } manki)
            {
                var friendlyFire = replay.Header.Rules.FriendlyFireId switch
                {
                    "on" => FriendlyFireMode.On, "shoves" => FriendlyFireMode.Shoves, _ => FriendlyFireMode.Off,
                };
                return new ReplayModes(null, null, new MankiMayhemPresentation(scene.Parent, replay.World, arena.Arcade.Mayhem, manki,
                    scene.Fighters, scene.Players, scene.Hud, scene.Projectiles, scene.Camera, scene.RenderCamera, friendlyFire), null);
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
        public bool Present(ServerSimulation world, ulong focus, Transform focusTransform, CameraMount? camera)
        {
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

        /// <summary>Per rendered frame, as PvPMatch.Update: the ball, its sender and pops, and the course cues.</summary>
        public void Update(ServerSimulation world, UnityEngine.Camera camera)
        {
            _course?.Update();
            _slopball?.Update(Time.deltaTime, world, camera);
        }

        /// <summary>A seek, or a new followed fighter: every mode presenter shows nothing from before.</summary>
        public void Reset()
        {
            _slopball?.Reset();
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
