using System;
using System.Collections.Generic;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Co-op Slopball in an online Match (ADR-0040): the court presentation PvPMatch hosts for a
    /// <see cref="MatchFormat.SlopballCoop"/> run. It reads the predicted world, and takes hits and
    /// Slopball events from the rollback ledgers, so every serve, return and miss presents once even
    /// when a World Frame re-simulates the tick. Authority stays in the Shared simulation.
    /// </summary>
    public sealed class OnlineSlopball : IDisposable
    {
        private readonly Transform _anchor;
        private readonly SlopballView _view;
        private readonly SlopballCourtShow _court;
        private readonly SlopballPresentation _presentation;
        private readonly Transform _launcher;
        private readonly float _launcherHeight;
        private readonly Vector3 _launcherHome;
        private readonly GameObject _plainBall;
        private readonly Material _plainMaterial;
        private readonly List<SlopballEvent> _events = new();
        private readonly bool _versus;
        private readonly Func<ulong> _perspective;
        private readonly bool _senderFollowsBall;
        private readonly bool _replayCourt;

        /// <param name="format">The Match format; live play reads MatchConfig.</param>
        /// <param name="perspective">Whose camp the versus panel speaks for; live play is the local fighter, a Replay the followed one.</param>
        /// <param name="senderFollowsBall">A View-track Replay without shot spawns: the sender goes where the ball is served.</param>
        public OnlineSlopball(Transform parent, Transform stage, in ArenaDefinition arena, HUDManager hud,
            Func<UnityEngine.Camera> camera, MatchFormat? format = null, Func<ulong> perspective = null, bool senderFollowsBall = false, bool replayCourt = false)
        {
            _senderFollowsBall = senderFollowsBall;
            _replayCourt = replayCourt;
            _versus = (format ?? MatchConfig.Format) == MatchFormat.SlopballVersus;
            _perspective = perspective ?? (() => MatchConfig.LocalEntityId);
            var launcher = stage != null ? stage.Find("Ball Sender") : null;
            if (_versus && launcher != null) launcher.gameObject.SetActive(false);
            _launcher = _versus ? null : launcher;
            if (_launcher != null) { _launcherHeight = _launcher.position.y; _launcherHome = _launcher.position; }
            _court = SlopballCourtShow.Find(stage);
            if (_versus) _court?.ClearCombo();
            _view = new SlopballView(hud.Document.rootVisualElement, cooperative: true);
            var anchor = new GameObject("Online Slopball Anchor") { layer = LayerMask.NameToLayer("Ignore Raycast") };
            anchor.transform.SetParent(parent, false);
            _anchor = anchor.transform;
            var set = Resources.Load<SlopballPresentationSet>(SlopballPresentationSet.ResourcesPath);
            if (set != null && set.Ball != null && set.GroundMarker != null)
                _presentation = new SlopballPresentation(set, parent, _anchor, arena, _launcher, _view, hud, camera)
                    { SenderFollowsBall = senderFollowsBall };
            else
            {
                // Without the generated set the court still shows a plain ball.
                _plainBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _plainBall.name = "Online Slopball Ball";
                _plainBall.layer = LayerMask.NameToLayer("Ignore Raycast");
                UnityEngine.Object.Destroy(_plainBall.GetComponent<Collider>());
                _plainBall.transform.SetParent(_anchor, false);
                _plainMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _plainMaterial.SetColor("_BaseColor", new Color(1f, .76f, .12f));
                _plainBall.GetComponent<MeshRenderer>().sharedMaterial = _plainMaterial;
            }
        }

        /// <summary>The ball's position, for aim selection, lock-on and the target indicator.</summary>
        public Transform BallAnchor => _anchor;

        /// <summary>Lock on the ball when it is in front of the camera and within reach.</summary>
        public static byte PickTarget(ServerSimulation simulation, UnityEngine.Camera camera, in CharacterState owner)
        {
            if (simulation.ArcadeBall is not ArcadeBallState ball || !ball.Active || camera == null) return 0;
            var point = new Vector3(ball.Position.X, ball.Position.Y, ball.Position.Z);
            float dx = point.x - owner.PX, dz = point.z - owner.PZ;
            return camera.WorldToScreenPoint(point).z > 0f && dx * dx + dz * dz <= 400f
                ? ServerSimulation.ArcadeBallTargetInputId : (byte)0;
        }

        /// <summary>Present one update: the predicted ball and rally, plus the hits and events the ledgers handed out.</summary>
        public void AfterUpdate(ServerSimulation simulation, IReadOnlyList<SpellResolver.HitResult> hits,
            IReadOnlyList<SlopballTickEvent> events)
        {
            if (simulation.ArcadeBall is not ArcadeBallState ball) return;
            var run = simulation.Slopball ?? default;
            _anchor.position = new Vector3(ball.Position.X, ball.Position.Y, ball.Position.Z);
            if (_plainBall != null)
            {
                _plainBall.SetActive(ball.Active);
                _plainBall.transform.localScale = Vector3.one * (ball.Radius * 2f);
                if (_launcher != null && !_senderFollowsBall)
                    _launcher.position = new Vector3(run.Shot.Spawn.X, _launcherHeight, run.Shot.Spawn.Z);
                else if (_launcher != null)
                    foreach (var item in events)
                        if (item.Event.Kind is SlopballEventKind.Served or SlopballEventKind.ServeReady)
                            _launcher.position = new Vector3(ball.Position.X, _launcherHeight, ball.Position.Z);
            }
            _events.Clear();
            foreach (var item in events)
            {
                _events.Add(item.Event);
                if (!_replayCourt && item.Event.Kind == SlopballEventKind.Returned) _court?.Returned();
                if (item.Event.Kind == SlopballEventKind.PointScored)
                { if (!_replayCourt) _court?.Returned(); _view.ShowPoint(item.Event.Camp); }
            }
            _presentation?.Present(simulation, hits, simulation.LastTickArcadeBallContacts, _events);
            if (simulation.SlopballVersus is { } versus)
            {
                ulong self = _perspective();
                _view.PresentVersus(versus, ball, self, simulation.CampOf(self));
            }
            else
            {
                _view.Present(run, ball, 0, null);
                if (!_replayCourt) { _court?.Present(run.Returns); _court?.SetHeat(run.Heat); }
            }
        }

        /// <summary>Restore the last recorded serve origin before the seek warmup.</summary>
        public void RestoreSender(ArcadePoint position)
        {
            _presentation?.RestoreSender(position);
            if (_launcher != null) _launcher.position = new Vector3(position.X, _launcherHeight, position.Z);
        }

        /// <summary>Replay court reactions advance on recorded 60 Hz ticks, including during seek reconstruction.</summary>
        public void PresentReplayCourt(ulong combo, bool reaction)
        {
            if (!_replayCourt) return;
            if (reaction) _court?.Returned();
            if (!_versus) { _court?.Present(combo); _court?.SetHeat(SlopballDefinition.HeatOf(combo)); }
            _court?.Tick(1f / 60f);
        }

        public void Update(float deltaTime, ServerSimulation simulation, UnityEngine.Camera camera)
        {
            _view.Tick(Time.unscaledDeltaTime, camera);
            if (!_replayCourt) _court?.Tick(Time.unscaledDeltaTime);
            _presentation?.Update(deltaTime, simulation);
        }

        /// <summary>A Replay seek: the court shows nothing from before it.</summary>
        public void Reset()
        {
            _events.Clear();
            _court?.Reset();
            if (_versus) _court?.ClearCombo();
            if (_launcher != null) _launcher.position = _launcherHome;
            _presentation?.Reset();
            _view.Reset();
        }

        public void Dispose()
        {
            _view.Dispose();
            _presentation?.Dispose();
            if (_plainMaterial != null) UnityEngine.Object.Destroy(_plainMaterial);
        }
    }
}
