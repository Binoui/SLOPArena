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
        private readonly GameObject _plainBall;
        private readonly Material _plainMaterial;
        private readonly List<SlopballEvent> _events = new();

        public OnlineSlopball(Transform parent, Transform stage, in ArenaDefinition arena, HUDManager hud,
            Func<UnityEngine.Camera> camera)
        {
            _launcher = stage != null ? stage.Find("Ball Sender") : null;
            if (_launcher != null) _launcherHeight = _launcher.position.y;
            _court = SlopballCourtShow.Find(stage);
            _view = new SlopballView(hud.Document.rootVisualElement);
            var anchor = new GameObject("Online Slopball Anchor") { layer = LayerMask.NameToLayer("Ignore Raycast") };
            anchor.transform.SetParent(parent, false);
            _anchor = anchor.transform;
            var set = Resources.Load<SlopballPresentationSet>(SlopballPresentationSet.ResourcesPath);
            if (set != null && set.Ball != null && set.GroundMarker != null)
                _presentation = new SlopballPresentation(set, parent, _anchor, arena, _launcher, _view, hud, camera);
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
            if (simulation.Slopball is not SlopballState run || simulation.ArcadeBall is not ArcadeBallState ball) return;
            _anchor.position = new Vector3(ball.Position.X, ball.Position.Y, ball.Position.Z);
            if (_plainBall != null)
            {
                _plainBall.SetActive(ball.Active);
                _plainBall.transform.localScale = Vector3.one * (ball.Radius * 2f);
                if (_launcher != null)
                    _launcher.position = new Vector3(run.Shot.Spawn.X, _launcherHeight, run.Shot.Spawn.Z);
            }
            _events.Clear();
            foreach (var item in events)
            {
                _events.Add(item.Event);
                if (item.Event.Kind == SlopballEventKind.Returned) _court?.Returned();
            }
            _presentation?.Present(simulation, hits, simulation.LastTickArcadeBallContacts, _events);
            _view.Present(run, ball, 0, null);
            _court?.Present(run.Returns);
            _court?.SetHeat(run.Heat);
        }

        public void Update(float deltaTime, ServerSimulation simulation, UnityEngine.Camera camera)
        {
            _view.Tick(Time.unscaledDeltaTime, camera);
            _court?.Tick(Time.unscaledDeltaTime);
            _presentation?.Update(deltaTime, simulation);
        }

        public void Dispose()
        {
            _view.Dispose();
            _presentation?.Dispose();
            if (_plainMaterial != null) UnityEngine.Object.Destroy(_plainMaterial);
        }
    }
}
