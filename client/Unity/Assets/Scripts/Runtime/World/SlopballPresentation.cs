using System;
using System.Collections.Generic;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Slopball's feel: the mascot ball and its ground cue (ring, contact shadow and the fighters'
    /// dotted drop line), the sender's 3-2-1 lights and POP!, hit bursts and kicks, cage and net
    /// bounces, the SLOP! splat with a climbing note per return, the X_X pancake on a miss and the
    /// NEW BEST! stamp, rally heat (HEAT UP!, the sender's serve colours, a curveball's green streak)
    /// and trick-shot callouts (bank shot, sky ball, net cord, juggle, body bank). It reads Shared
    /// ball state, contacts, hits and Slopball events after each tick and never feeds back.
    /// </summary>
    internal sealed class SlopballPresentation : IDisposable
    {
        private const int Voices = 10;
        private const float HeavyHit = 14f;
        private const float BounceCooldown = .07f;
        private const float DropLineWidth = .06f, DropLineMinPixels = 4f, DropLineSpacing = 2f, DropLineCore = .3f;
        // The SLOP! word (SlopballPresentationBuilder.Splat) is 2.3 m wide, rises from 1.05 m and draws toward the camera.
        private const float SlopWordMiddle = 1.25f, SlopWordTop = 2.4f, SlopWordClearance = 1.6f;
        private static readonly Color DropLineOrange = new(1f, 176f / 255f, 59f / 255f);
        private static readonly Color LampOff = new(.22f, .16f, .08f), LampGo = new(2.4f, 2.3f, 1.9f);
        // The sender's countdown lights tell the next serve: amber lob, cyan zinger, violet moonball, green curveball.
        private static readonly Color[] LampServe =
        {
            new(2.2f, 1.55f, .35f), new(.35f, 1.9f, 2.4f), new(1.7f, .55f, 2.4f), new(.45f, 2.3f, .7f),
        };
        private static readonly Color CurveTrail = new(.45f, 1f, .55f);
        // The streak heats up every five returns: coral, orange, red-orange, hot pink, white-hot.
        private static readonly Color[] Heat =
        {
            new(1f, .42f, .29f), new(1f, .6f, .12f), new(1f, .3f, .1f), new(1f, .28f, .55f), new(1f, .93f, .75f),
        };
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int AirborneProperty = Shader.PropertyToID("_Airborne");
        private static readonly int LengthInWidthsProperty = Shader.PropertyToID("_LengthInWidths");
        private static readonly int SpacingProperty = Shader.PropertyToID("_Spacing");
        private static readonly int CoreProperty = Shader.PropertyToID("_Core");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private enum Surface { Cage, Net, Ceiling, Floor, Body }

        private readonly SlopballPresentationSet _set;
        private readonly Transform _root, _anchor, _sender, _senderDressing;
        private readonly SlopballView _view;
        private readonly HUDManager _hud;
        private readonly Func<UnityEngine.Camera> _camera;
        private readonly SlopballBall _ball;
        private readonly Transform _marker, _ring, _shadow;
        private readonly Renderer _shadowRenderer;
        private readonly LineRenderer _dropLine;
        private readonly Material _dropLineMaterial, _lampMaterial;
        private readonly MaterialPropertyBlock _block = new();
        private readonly Renderer[] _lamps = new Renderer[3];
        private readonly AudioSource[] _voices = new AudioSource[Voices];
        private readonly AudioClip _beep, _pop, _kickLight, _kickHeavy, _cage, _net, _floor, _splat, _note, _flip, _chord, _best, _miss,
            _heat, _trick;
        private readonly List<(float at, AudioClip clip, float volume, float pitch)> _queued = new();
        private readonly List<GameObject> _effects = new();
        private readonly Dictionary<string, int> _played = new();
        private readonly float[] _surfaceReady = new float[5];
        private readonly float _radius, _floorY, _netZ, _netHalf, _netTop;
        private readonly float _senderHeight;
        private readonly Vector3 _senderScale;
        private bool _senderPlaced;
        private SlopballServe _nextServe;
        private Vector3 _ballVelocity;
        // Trick bookkeeping for the live ball: attack touches, and what it hit since the last touch.
        private int _hits;
        private bool _bodyTouch, _bankSince, _skySince, _netSince;
        private int _beat;
        private float _lampFlash, _recoil, _recoilSpeed;
        private ulong _combo;
        private bool _newBestShown;
        private Vector3 _senderTarget;

        /// <summary>The saved best before this attempt; SlopballMatch refreshes it after banking.</summary>
        public ulong PersonalBest { get; set; }

        /// <summary>For verification commands: loaded clips, sounds played this attempt, the ball's face and live effects.</summary>
        public object Diagnostics
        {
            get
            {
                int clips = 0;
                foreach (var clip in new[] { _beep, _pop, _kickLight, _kickHeavy, _cage, _net, _floor, _splat, _note, _flip, _chord, _best, _miss, _heat, _trick })
                    if (clip != null) clips++;
                _effects.RemoveAll(x => x == null);
                return new
                {
                    clips, played = new Dictionary<string, int>(_played), mood = _ball != null ? _ball.CurrentMood.ToString() : null,
                    ballVisible = _ball != null && _ball.gameObject.activeSelf, effects = _effects.Count, lamps = _beat,
                    dropLine = _dropLine != null && _dropLine.enabled,
                };
            }
        }

        public SlopballPresentation(SlopballPresentationSet set, Transform parent, Transform anchor, in ArenaDefinition arena,
            Transform sender, SlopballView view, HUDManager hud, Func<UnityEngine.Camera> camera)
        {
            _set = set; _anchor = anchor; _sender = sender; _view = view; _hud = hud; _camera = camera;
            var ball = arena.Arcade!.Ball!;
            var rules = arena.Arcade.Slopball!;
            _radius = ball.Radius;
            _floorY = rules.FloorY; _netZ = rules.NetZ; _netHalf = rules.NetHalfThickness; _netTop = rules.NetTop;
            _root = new GameObject("Slopball Presentation").transform;
            _root.SetParent(parent, false);

            _ball = Object.Instantiate(set.Ball, _root).GetComponent<SlopballBall>();
            _ball.name = "Slopball Ball";
            _ball.Init(Viewer);
            _ball.gameObject.SetActive(false);

            _marker = Object.Instantiate(set.GroundMarker, _root).transform;
            _marker.name = "Slopball Ground Marker";
            _ring = _marker.Find("Ring");
            _shadow = _marker.Find("Shadow");
            _shadowRenderer = _shadow != null ? _shadow.GetComponent<Renderer>() : null;
            _marker.localScale = Vector3.one * (_radius / Mathf.Max(.01f, set.BallRadius));
            _marker.gameObject.SetActive(false);

            var lineShader = Resources.Load<Shader>("Shaders/FighterDropLine");
            if (lineShader != null)
            {
                _dropLineMaterial = new Material(lineShader);
                _dropLine = new GameObject("Slopball Drop Line").AddComponent<LineRenderer>();
                _dropLine.transform.SetParent(_root, false);
                _dropLine.useWorldSpace = true; _dropLine.positionCount = 2;
                _dropLine.alignment = LineAlignment.View; _dropLine.textureMode = LineTextureMode.Stretch;
                _dropLine.numCapVertices = 0; _dropLine.sharedMaterial = _dropLineMaterial;
                _dropLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _dropLine.receiveShadows = false; _dropLine.enabled = false;
            }

            if (_sender != null)
            {
                _senderHeight = _sender.position.y;
                _senderDressing = _sender.Find("Club Sender");
                _senderScale = _senderDressing != null ? _senderDressing.localScale : Vector3.one;
                _lampMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                // Three countdown lights in a row on the sender's chest, above its stripe, facing the
                // player (-Z); the head's front tilts toward the floor and hides anything on it.
                var host = _senderDressing != null ? _senderDressing : _sender;
                for (int i = 0; i < _lamps.Length; i++)
                {
                    var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    lamp.name = "Serve Light " + (i + 1);
                    Object.Destroy(lamp.GetComponent<Collider>());
                    lamp.transform.SetParent(host, false);
                    lamp.transform.localPosition = new Vector3((i - 1) * .2f, .34f, -.4f);
                    lamp.transform.localScale = Vector3.one * .15f;
                    var renderer = lamp.GetComponent<Renderer>();
                    renderer.sharedMaterial = _lampMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _lamps[i] = renderer;
                }
                ShowLamps(0, 0f);
            }

            string folder = set.SoundFolder;
            AudioClip Load(string name) => string.IsNullOrEmpty(folder) ? null : Resources.Load<AudioClip>($"{folder}/{name}");
            _beep = Load("slopball_beep"); _pop = Load("slopball_pop");
            _kickLight = Load("slopball_kick_light"); _kickHeavy = Load("slopball_kick_heavy");
            _cage = Load("slopball_cage"); _net = Load("slopball_net"); _floor = Load("slopball_floor");
            _splat = Load("slopball_splat"); _note = Load("slopball_note"); _flip = Load("slopball_flip");
            _chord = Load("slopball_chord"); _best = Load("slopball_best"); _miss = Load("slopball_miss");
            _heat = Load("slopball_heat"); _trick = Load("slopball_trick");
            var audio = new GameObject("Slopball Audio");
            audio.transform.SetParent(_root, false);
            var bus = ClientSettingsService.Instance.FindBus("SFX");
            for (int i = 0; i < Voices; i++)
            {
                var voice = audio.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.spatialBlend = 0f; voice.outputAudioMixerGroup = bus;
                _voices[i] = voice;
            }
        }

        private Transform Viewer => _camera()?.transform;

        // ── Per tick ────────────────────────────────────────────────────────────

        /// <summary>Consumes one completed Shared tick (countdown ticks included): hits on the ball, its bounces and the Slopball events.</summary>
        public void AfterTick(ServerSimulation simulation)
            => Present(simulation, simulation.LastTickHits, simulation.LastTickArcadeBallContacts, simulation.LastTickSlopballEvents);

        /// <summary>
        /// Consumes what one update brings: hits on the ball, its bounces and the Slopball events. Online, the
        /// rollback ledgers hand hits and events out once each, so a re-simulated tick never sounds twice (ADR-0040).
        /// </summary>
        public void Present(ServerSimulation simulation, IReadOnlyList<SpellResolver.HitResult> hits,
            IReadOnlyList<ArcadeBallContact> contacts, IReadOnlyList<SlopballEvent> events)
        {
            if (simulation.Slopball is not SlopballState state || simulation.ArcadeBall is not ArcadeBallState ball) return;
            _senderTarget = new Vector3(state.Shot.Spawn.X, _senderHeight, state.Shot.Spawn.Z);
            if (!_senderPlaced && _sender != null) { _sender.position = _senderTarget; _senderPlaced = true; }

            // A hit ends the ball's path at its contact time: later contacts in that tick never happened.
            _ballVelocity = Position(ball.Velocity);
            float hitTime = float.MaxValue;
            foreach (var hit in hits)
                if (hit.TargetEntityId == ball.EntityId && !hit.Blocked) hitTime = Mathf.Min(hitTime, hit.ContactTime);
            OnContacts(contacts, hitTime);
            foreach (var hit in hits)
                if (hit.TargetEntityId == ball.EntityId && !hit.Blocked) OnHit(hit, ball);

            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case SlopballEventKind.Served: OnServe(ball); break;
                    case SlopballEventKind.Returned: OnScore(e, ball); break;
                    case SlopballEventKind.Missed: OnMiss(e, ball, state); break;
                    case SlopballEventKind.ReServe: OnFizzle(ball); break;
                }
            }
            Telegraph(state);
            _combo = state.Returns;
        }

        private void OnServe(in ArcadeBallState ball)
        {
            if (_sender != null) _sender.position = _senderTarget;
            var centre = Position(ball.Position);
            _ball.Respawn(centre, _radius, Position(ball.Velocity));
            _lampFlash = 1f; _beat = 0;
            _recoil = .16f; _recoilSpeed = 0f;
            _hits = 0; _bodyTouch = _bankSince = _skySince = _netSince = false;
            Spawn(_set.ServePuff, centre + Vector3.down * _radius * .3f, 1f, 2f);
            Play(_pop, .75f, UnityEngine.Random.Range(.97f, 1.03f));
        }

        private void OnHit(in SpellResolver.HitResult hit, in ArcadeBallState ball)
        {
            float speed = hit.ImpactForce;
            float strength = Mathf.Clamp01((speed - 4f) / 22f);
            bool heavy = speed >= HeavyHit;
            var centre = Position(ball.Position);
            var launch = Position(ball.Velocity);
            var contact = new Vector3(hit.HitX, hit.HitY, hit.HitZ);
            var toContact = contact - centre;
            if (toContact.sqrMagnitude < 1e-4f || toContact.sqrMagnitude > 16f) toContact = -launch;
            var viewer = Viewer;
            var toViewer = viewer != null ? (viewer.position - centre).normalized : Vector3.back;
            // In front of the ball's face, toward the hit: the body would hide a burst at its centre.
            var burst = centre + toContact.normalized * _radius * .55f + toViewer * _radius * 1.05f;
            _ball.Hit(launch.sqrMagnitude > 1e-4f ? launch.normalized : -toContact.normalized, strength, hit.HitstopTicks / 60f);
            _hits++; _bodyTouch = _bankSince = _skySince = _netSince = false;
            Spawn(heavy ? _set.HitHeavy : _set.HitLight, burst, _radius / Mathf.Max(.01f, _set.BallRadius), 1.5f);
            Play(heavy ? _kickHeavy : _kickLight, Mathf.Lerp(.5f, .85f, strength), Mathf.Lerp(1.08f, .94f, strength) * UnityEngine.Random.Range(.97f, 1.03f));
        }

        /// <summary>
        /// Shared's bounces this tick, up to <paramref name="before"/> (a hit) or the floor (a landing, which
        /// arrives as a Slopball event): sounds, squash, dust, and what the ball touched for trick callouts.
        /// </summary>
        private void OnContacts(IReadOnlyList<ArcadeBallContact> contacts, float before)
        {
            foreach (var contact in contacts)
            {
                if (contact.Time > before) break;
                var p = Position(contact.Position);
                if (!contact.Body && contact.NormalY > .5f && p.y <= _floorY + _radius + .02f) break;
                var normal = new Vector3(contact.NormalX, contact.NormalY, contact.NormalZ);
                Surface surface = contact.Body ? Surface.Body
                    : contact.NormalY < -.5f ? Surface.Ceiling
                    : Mathf.Abs(p.z - _netZ) <= _netHalf + _radius + .05f && p.y <= _netTop + _radius + .05f ? Surface.Net
                    : Surface.Cage;
                switch (surface)
                {
                    case Surface.Body: _bodyTouch = true; _bankSince = _skySince = _netSince = false; break;
                    case Surface.Ceiling: _skySince = true; break;
                    case Surface.Net: _netSince = true; break;
                    default: _bankSince = true; break;
                }
                if (Time.time < _surfaceReady[(int)surface]) continue;
                _surfaceReady[(int)surface] = Time.time + BounceCooldown;
                float strength = Mathf.Clamp01(_ballVelocity.magnitude / 18f);
                _ball.Bounce(normal, strength);
                switch (surface)
                {
                    case Surface.Cage:
                        Play(_cage, Mathf.Lerp(.3f, .75f, strength), UnityEngine.Random.Range(.94f, 1.06f));
                        Spawn(_set.Bounce, p - normal * _radius * .9f, 1f, 1.2f);
                        break;
                    case Surface.Net: Play(_net, Mathf.Lerp(.35f, .7f, strength), 1f); break;
                    case Surface.Ceiling: Play(_net, Mathf.Lerp(.3f, .6f, strength), .72f); break;
                    default: Play(_kickLight, Mathf.Lerp(.25f, .5f, strength), .78f); break;
                }
            }
        }

        private void OnScore(in SlopballEvent e, in ArcadeBallState ball)
        {
            ulong combo = e.Returns;
            var floor = Floor(ball.Position);
            Spawn(_set.Score, floor, 1f, 2.4f);
            _ball.PlayOutro(scored: true, fizzle: false, floor);
            Play(_splat, .7f, UnityEngine.Random.Range(.96f, 1.04f));
            int[] notes = _set.NoteSemitones;
            if (notes != null && notes.Length > 0)
                Queue(_note, .04f, .6f, Mathf.Pow(2f, notes[(int)Math.Min(combo - 1, (ulong)notes.Length - 1)] / 12f));
            Queue(_flip, .08f, .35f);
            bool milestone = combo % 5 == 0;
            var (beside, side) = BesideSlop(floor);
            _view?.ShowCountPop(beside, $"{combo}!", milestone, side);
            int line = 1;
            // Rally heat steps up every fifth return until it maxes out; after that, a chord.
            if (SlopballDefinition.HeatOf(combo) > SlopballDefinition.HeatOf(combo - 1))
            {
                Queue(_heat, .1f, .6f);
                _view?.ShowCallout(beside, SlopballDefinition.HeatOf(combo) == SlopballDefinition.MaxHeat ? "MAX HEAT!" : "HEAT UP!", true, line++, side);
            }
            else if (milestone) Queue(_chord, .14f, .55f);
            var tricks = Tricks();
            foreach (string trick in tricks) _view?.ShowCallout(beside, trick, false, line++, side);
            if (tricks.Count > 0) Queue(_trick, .22f, .55f, 1f + .06f * (tricks.Count - 1));
            _ball.SetTrailColor(Heat[SlopballDefinition.HeatOf(combo)]);
            if (!_newBestShown && PersonalBest > 0 && combo > PersonalBest)
            {
                _newBestShown = true;
                _hud?.ShowSoloStamp($"NEW BEST! {combo}", 1.4f);
                Queue(_best, .3f, .6f);
            }
        }

        /// <summary>
        /// Where the combo column goes: level with the SLOP! word and just past its edge, on the side toward the
        /// middle of the screen so the panel on the right never covers it (+1 starts the column there, -1 ends it).
        /// </summary>
        private (Vector3 world, int side) BesideSlop(Vector3 floor)
        {
            var camera = _camera();
            if (camera == null) return (floor + Vector3.up * SlopWordTop, 0);
            int side = camera.WorldToViewportPoint(floor).x > .58f ? -1 : 1;
            var right = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized;
            return (floor + Vector3.up * SlopWordMiddle + right * (side * SlopWordClearance), side);
        }

        /// <summary>The scoring ball's tricks: a juggle of three or more hits, a body bank, the ceiling, a wall and the net since its last touch.</summary>
        private List<string> Tricks()
        {
            var tricks = new List<string>(3);
            if (_hits >= 3) tricks.Add($"JUGGLE ×{_hits}");
            if (_bodyTouch) tricks.Add("BODY BANK");
            if (_skySince) tricks.Add("SKY BALL");
            if (_bankSince) tricks.Add("BANK SHOT");
            if (_netSince) tricks.Add("NET CORD");
            if (tricks.Count > 3) tricks.RemoveRange(3, tricks.Count - 3);
            return tricks;
        }

        private void OnMiss(in SlopballEvent e, in ArcadeBallState ball, in SlopballState state)
        {
            var floor = Floor(ball.Position);
            Spawn(_set.Miss, floor, 1f, 2.4f);
            _ball.PlayOutro(scored: false, fizzle: false, floor);
            Play(_miss, .7f);
            if (e.Returns > 0 || _combo > 0) Queue(_flip, .1f, .3f);
            if (e.Returns > 0) _view?.ShowDropToast(e.Returns, Math.Max(PersonalBest, state.BestStreak));
            _ball.SetTrailColor(Heat[0]);
            _newBestShown = false;
        }

        private void OnFizzle(in ArcadeBallState ball)
        {
            _ball.PlayOutro(scored: false, fizzle: true, Floor(ball.Position));
            Play(_floor, .45f, 1.05f);
        }

        /// <summary>The sender's lights count the last 0.6 s of the gap: beep, beep, then a higher beep.</summary>
        private void Telegraph(in SlopballState state)
        {
            if (state.Phase != SlopballPhase.WaitingServe) return;
            _nextServe = state.Shot.Kind;
            int beat = state.ServeInTicks <= 12 ? 3 : state.ServeInTicks <= 24 ? 2 : state.ServeInTicks <= 36 ? 1 : 0;
            if (beat <= _beat) return;
            _beat = beat;
            Play(_beep, .4f, beat == 3 ? 1.5f : 1f);
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        public void Update(float deltaTime, ServerSimulation simulation)
        {
            for (int i = _queued.Count - 1; i >= 0; i--)
            {
                if (Time.time < _queued[i].at) continue;
                var (_, clip, volume, pitch) = _queued[i];
                _queued.RemoveAt(i);
                Play(clip, volume, pitch);
            }
            UpdateSender(deltaTime);
            if (simulation?.ArcadeBall is not ArcadeBallState ball) return;
            bool live = ball.Active && !_ball.InOutro;
            if (live)
            {
                var centre = _anchor != null ? _anchor.position : Position(ball.Position);
                var velocity = Position(ball.Velocity);
                _ballVelocity = velocity;
                if (!_ball.gameObject.activeSelf) _ball.Respawn(centre, _radius, velocity);
                _ball.Place(centre, _radius, velocity);
                _ball.SetCurving(ball.Curve != 0f, CurveTrail);
                // "Uh-oh": dropping toward your own floor.
                bool fallingAtYou = centre.z < _netZ && velocity.y < -1f && centre.y - _radius - _floorY < 3f;
                _ball.SetMood(fallingAtYou ? SlopballBall.Mood.Yikes : SlopballBall.Mood.Grin);
                PresentGroundCue(centre);
                _view?.PresentBallChip(_camera(), centre, centre.y - _radius - _floorY, true);
            }
            else
            {
                if (!ball.Active && !_ball.InOutro && _ball.gameObject.activeSelf) _ball.Hide();
                _marker.gameObject.SetActive(false);
                if (_dropLine != null) _dropLine.enabled = false;
                _view?.PresentBallChip(_camera(), Vector3.zero, 0f, false);
            }
        }

        /// <summary>The ring and contact shadow straight under the ball, and the fighters' dotted drop line up to it.</summary>
        private void PresentGroundCue(Vector3 centre)
        {
            float height = Mathf.Max(0f, centre.y - _radius - _floorY);
            var floor = new Vector3(centre.x, _floorY + .03f, centre.z);
            _marker.gameObject.SetActive(true);
            _marker.position = floor;
            if (_shadow != null)
            {
                float near = 1f - Mathf.Clamp01(height / 6f);
                _shadow.localScale = Vector3.one * Mathf.Lerp(1.1f, 1.7f, near);
                if (_shadowRenderer != null)
                {
                    _shadowRenderer.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, new Color(1f, 1f, 1f, Mathf.Lerp(.25f, .7f, near * near)));
                    _shadowRenderer.SetPropertyBlock(_block);
                    _block.Clear();
                }
            }
            if (_dropLine == null) return;
            float length = height;
            if (length <= .08f) { _dropLine.enabled = false; return; }
            var top = new Vector3(floor.x, centre.y - _radius, floor.z);
            float width = DropLineWidth;
            var camera = _camera();
            if (camera != null && !camera.orthographic)
            {
                float distance = Vector3.Distance(camera.transform.position, (floor + top) * .5f);
                float metresPerPixel = 2f * distance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight);
                width = Mathf.Max(width, DropLineMinPixels * metresPerPixel);
            }
            _dropLine.SetPosition(0, floor);
            _dropLine.SetPosition(1, top);
            _dropLine.startWidth = _dropLine.endWidth = width;
            _dropLine.GetPropertyBlock(_block);
            _block.SetFloat(AirborneProperty, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.05f, .5f, height)));
            _block.SetFloat(LengthInWidthsProperty, (top.y - floor.y) / width);
            _block.SetFloat(SpacingProperty, DropLineSpacing);
            _block.SetFloat(CoreProperty, DropLineCore);
            _block.SetColor(ColorProperty, DropLineOrange);
            _dropLine.SetPropertyBlock(_block);
            _block.Clear();
            _dropLine.enabled = true;
        }

        /// <summary>The sender glides to its next origin during the gap, recoils on POP! and lights its countdown.</summary>
        private void UpdateSender(float deltaTime)
        {
            if (_sender == null || deltaTime <= 0f) return;
            if (_senderPlaced)
                _sender.position = Vector3.Lerp(_sender.position, _senderTarget, 1f - Mathf.Exp(-deltaTime * 14f));
            if (_senderDressing != null)
            {
                _recoilSpeed += (-260f * _recoil - 12f * _recoilSpeed) * deltaTime;
                _recoil += _recoilSpeed * deltaTime;
                _senderDressing.localScale = Vector3.Scale(_senderScale, new Vector3(1f + _recoil * .55f, 1f - _recoil, 1f + _recoil * .55f));
            }
            _lampFlash = Mathf.Max(0f, _lampFlash - deltaTime / .18f);
            ShowLamps(_beat, _lampFlash);
        }

        private void ShowLamps(int lit, float flash)
        {
            for (int i = 0; i < _lamps.Length; i++)
            {
                if (_lamps[i] == null) continue;
                var on = LampServe[Mathf.Clamp((int)_nextServe, 0, LampServe.Length - 1)];
                var color = flash > 0f ? Color.Lerp(LampOff, LampGo, flash) : i < lit ? (lit == 3 ? Color.Lerp(on, LampGo, .35f) : on) : LampOff;
                _lamps[i].GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _lamps[i].SetPropertyBlock(_block);
                _block.Clear();
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private Vector3 Floor(ArcadePoint point) => new(point.X, _floorY, point.Z);
        private static Vector3 Position(ArcadePoint point) => new(point.X, point.Y, point.Z);

        private void Spawn(GameObject prefab, Vector3 position, float scale, float seconds)
        {
            if (prefab == null) return;
            var instance = Object.Instantiate(prefab, position, Quaternion.identity, _root);
            instance.transform.localScale = Vector3.one * scale;
            Object.Destroy(instance, seconds);
            _effects.RemoveAll(x => x == null);
            _effects.Add(instance);
        }

        private void Queue(AudioClip clip, float delay, float volume, float pitch = 1f)
        {
            if (clip != null) _queued.Add((Time.time + delay, clip, volume, pitch));
        }

        private void Play(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null) return;
            AudioSource voice = null;
            foreach (var candidate in _voices) if (!candidate.isPlaying) { voice = candidate; break; }
            if (voice == null)
            {
                voice = _voices[0];
                foreach (var candidate in _voices) if (candidate.time > voice.time) voice = candidate;
            }
            voice.clip = clip; voice.volume = volume; voice.pitch = pitch;
            voice.Play();
            _played.TryGetValue(clip.name, out int count);
            _played[clip.name] = count + 1;
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            foreach (var lamp in _lamps) if (lamp != null) Object.Destroy(lamp.gameObject);
            if (_senderDressing != null) _senderDressing.localScale = _senderScale;
            if (_dropLineMaterial != null) Object.Destroy(_dropLineMaterial);
            if (_lampMaterial != null) Object.Destroy(_lampMaterial);
        }
    }
}
