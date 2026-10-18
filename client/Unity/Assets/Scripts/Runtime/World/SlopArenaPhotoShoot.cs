#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SlopArena.Client;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools
{
    /// <summary>One scripted input held over an inclusive tick range; press edges fire on its first tick.</summary>
    public sealed class PhotoInput
    {
        public int From, To;
        public byte Slot;
        public bool Aim, Jump, Down, Shield, Grab;
        /// <summary>World direction to run toward, in degrees (0 = +Z, 90 = +X).</summary>
        public float? MoveYaw;
        /// <summary>World facing in degrees, applied with a face edge on the first tick.</summary>
        public float? FaceYaw;
    }

    public sealed class PhotoFighter
    {
        public ulong Id;
        public string Package = "";
        /// <summary>Feet position; with Grounded the height comes from the arena floor below.</summary>
        public Vector3 Feet;
        public bool Grounded = true;
        public float YawDegrees;
        public ushort Damage;
        public byte Deaths;
        /// <summary>Fighter to aim at every tick (0 keeps YawDegrees).</summary>
        public ulong Target;
        /// <summary>Metres for ground-cursor specials; negative tracks the target's distance.</summary>
        public float AimDistance = -1f;
        public float AimPitchDegrees;
        public List<PhotoInput> Inputs = new();
    }

    public sealed class PhotoCamera
    {
        public string Name = "a";
        public Vector3 Position, LookAt;
        /// <summary>Look at this fighter's capsule centre plus LookOffset instead of LookAt.</summary>
        public ulong Follow;
        public Vector3 LookOffset;
        /// <summary>With Follow, keep the Position − LookAt offset from the followed point.</summary>
        public bool Move;
        /// <summary>Per-tick blend toward the followed framing; 1 snaps.</summary>
        public float Ease = 1f;
        public float Fov = 40f, Ortho, Roll, Far;
        public Vector3 Up = Vector3.up;
    }

    /// <summary>
    /// Disposable store-screenshot shoot: up to four fighters follow scripted inputs, Shared
    /// resolves the fight, the normal renderers present it, and free cameras write each requested
    /// tick. One simulation tick runs per rendered frame, so frame N is the state after N ticks.
    /// An optional HUD layer renders the real match HUD into a transparent texture.
    /// Never included in a player build.
    /// </summary>
    public sealed class SlopArenaPhotoShoot : MatchBase
    {
        public ArenaDefinition Arena;
        public List<PhotoFighter> Fighters = new();
        public List<PhotoCamera> Cameras = new();
        /// <summary>Gameplay camera whose look (post-processing, antialiasing) the photo cameras copy.</summary>
        public UnityEngine.Camera Template = null!;
        public int From, To = 120, Step = 1, Width = 960, Height = 540, Quality = 90;
        public bool Hud;
        public ulong HudLocal = 1;
        public int HudStocks = 3;
        public string[] Landmarks = Array.Empty<string>();
        public string OutputDirectory = "";

        private sealed class View
        {
            public PhotoCamera Spec = null!;
            public UnityEngine.Camera Camera = null!;
            public RenderTexture Target = null!;
            public Vector3 Look, Position;
        }

        private LocalSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;
        private CombatFeedback _feedback = null!;
        private ProjectileVFXManager _projectiles = null!;
        private readonly Dictionary<ulong, PlayerRenderer> _renderers = new();
        private readonly Dictionary<ulong, MatchContentEntry> _entries = new();
        private readonly Dictionary<ulong, InputState> _inputs = new();
        private readonly List<View> _views = new();
        private readonly List<object> _hits = new();
        private readonly StringBuilder _states = new();
        private UIDocument? _hudDocument;
        private PanelSettings? _hudOriginalPanel, _hudPanel;
        private bool _hudWasEnabled;
        private RenderTexture? _hudTarget;
        private Texture2D? _pixels, _hudPixels;
        private bool _stepping;
        private uint _capturedTick = uint.MaxValue;

        public uint Tick { get; private set; }
        public bool Ready { get; private set; }
        public bool Done { get; private set; }
        public string? Failure { get; private set; }
        public int FramesWritten { get; private set; }
        public object? Survey { get; private set; }

        // Deliberately omit MatchBase's human input, pause menu and match music.
        private void Start()
        {
            try
            {
                OnMatchStart();
                gameObject.AddComponent<MatchVisualStyle>().Apply();
                StartCoroutine(CaptureFrames());
            }
            catch (Exception error)
            {
                Failure = error.ToString();
                Debug.LogException(error);
                enabled = false;
            }
        }

        // Stepping happens in Update so each rendered frame is exactly one tick.
        private void FixedUpdate() { }

        private void Update()
        {
            if (!Ready || Done || Failure != null || Tick >= To) return;
            // The first frame shows the placed fighters (tick 0); later frames each add one tick.
            if (!_stepping)
            {
                _stepping = true;
                return;
            }
            try { OnMatchFixedUpdate(); }
            catch (Exception error)
            {
                Failure = error.ToString();
                Debug.LogException(error);
            }
        }

        protected override void OnMatchStart()
        {
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
                throw new InvalidOperationException("Local content catalog: " + failure);
            SpawnStageVisual(Arena, cameraObstruction: false);
            _bridge = new LocalSimulationBridge(Arena, NoWinMatchRule.Instance);
            _feedback = gameObject.AddComponent<CombatFeedback>();
            _feedback.SetSimulation(_bridge);
            _projectiles = gameObject.AddComponent<ProjectileVFXManager>();
            _projectiles.SetSimulation(_bridge.InternalSim);
            foreach (var fighter in Fighters)
            {
                var entry = catalog.ResolvePackage(fighter.Package)
                    ?? throw new InvalidOperationException("Missing roster package: " + fighter.Package);
                _entries[fighter.Id] = entry;
                _renderers[fighter.Id] = Spawn(entry, fighter);
            }
            for (int i = 0; i < Cameras.Count; i++)
                _views.Add(CreateView(Cameras[i], i == 0));
            if (Hud) SetupHudLayer();
            Survey = BuildSurvey();
            Ready = true;
        }

        private PlayerRenderer Spawn(MatchContentEntry entry, PhotoFighter fighter)
        {
            var renderer = new GameObject($"Photo {entry.DisplayName} {fighter.Id}").AddComponent<PlayerRenderer>();
            renderer.transform.SetParent(transform, false);
            if (!SetupRenderer(renderer, entry, Arena, fighter.Id, false))
                throw new InvalidOperationException("Could not present package " + entry.Identity.PackageId);
            // SetupRenderer registers with the first CombatFeedback in the scene; this shoot owns its own.
            _feedback.RegisterRenderer(renderer);
            float feet = fighter.Feet.y;
            if (fighter.Grounded)
            {
                feet = Arena.Heightmap.Sample(fighter.Feet.x, fighter.Feet.z);
                if (feet == float.MinValue)
                    throw new InvalidOperationException($"Fighter {fighter.Id} has no floor below ({fighter.Feet.x}, {fighter.Feet.z}); set grounded false to place it in the air.");
            }
            float yaw = fighter.YawDegrees * Mathf.Deg2Rad;
            // PY is the capsule centre. Airborne placements start past the FloatWindow, whose
            // zero float gravity would otherwise hold a freshly placed fighter in mid-air.
            var state = new CharacterState
            {
                PX = fighter.Feet.x, PY = feet + entry.Definition.CapsuleHeight * 0.5f,
                PZ = fighter.Feet.z, FacingYaw = yaw, AimYaw = yaw,
                IsGrounded = fighter.Grounded,
                AirTimeTicks = fighter.Grounded ? (ushort)0 : entry.Definition.Movement.FloatWindowTicks,
                JumpsLeft = entry.Definition.Movement.MaxJumps, AirDodgesLeft = 1,
                DamagePercent = fighter.Damage, Deaths = fighter.Deaths, MatchState = MatchState.Playing
            };
            _bridge.RegisterEntity(fighter.Id, entry.Definition, state, entry.BakedAnimation);
            _bridge.SetRespawnPosition(fighter.Id, state.PX, state.PY, state.PZ, yaw);
            renderer.ApplyServerState(_bridge.GetState(fighter.Id));
            return renderer;
        }

        private View CreateView(PhotoCamera spec, bool main)
        {
            var camera = new GameObject($"Photo Camera {spec.Name} (temporary)").AddComponent<UnityEngine.Camera>();
            camera.CopyFrom(Template);
            var data = camera.GetUniversalAdditionalCameraData();
            var source = Template.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = source.renderPostProcessing;
            data.antialiasing = source.antialiasing;
            data.antialiasingQuality = source.antialiasingQuality;
            camera.orthographic = spec.Ortho > 0f;
            if (camera.orthographic) camera.orthographicSize = spec.Ortho;
            camera.fieldOfView = spec.Fov;
            if (spec.Far > 0f) camera.farClipPlane = spec.Far;
            // Camera-facing effects (hit sparks, lettering) orient to Camera.main: the first view.
            if (main) camera.tag = "MainCamera";
            var target = new RenderTexture(Width, Height, 24) { antiAliasing = 4, name = "Photo " + spec.Name };
            camera.targetTexture = target;
            camera.enabled = true;
            var view = new View { Spec = spec, Camera = camera, Target = target };
            Aim(view, snap: true);
            return view;
        }

        private void Aim(View view, bool snap)
        {
            var spec = view.Spec;
            Vector3 look = spec.LookAt, position = spec.Position;
            if (spec.Follow != 0 && _entries.ContainsKey(spec.Follow))
            {
                var state = _bridge.GetState(spec.Follow);
                look = new Vector3(state.PX, state.PY, state.PZ) + spec.LookOffset;
                if (spec.Move) position = look + (spec.Position - spec.LookAt);
            }
            float blend = snap ? 1f : Mathf.Clamp01(spec.Ease);
            view.Look = Vector3.Lerp(view.Look, look, blend);
            view.Position = Vector3.Lerp(view.Position, position, blend);
            Vector3 forward = view.Look - view.Position;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            view.Camera.transform.SetPositionAndRotation(view.Position,
                Quaternion.LookRotation(forward, spec.Up) * Quaternion.Euler(0f, 0f, spec.Roll));
        }

        private void SetupHudLayer()
        {
            _hudManager = FindFirstObjectByType<HUDManager>(FindObjectsInactive.Include)
                ?? throw new InvalidOperationException("The capture scene has no HUDManager for the HUD layer.");
            _hudDocument = _hudManager.Document
                ?? throw new InvalidOperationException("The HUDManager has no UIDocument.");
            if (!_entries.ContainsKey(HudLocal))
                throw new InvalidOperationException($"The HUD's local fighter {HudLocal} is not in the shoot.");
            _hudOriginalPanel = _hudDocument.panelSettings;
            _hudWasEnabled = _hudDocument.enabled;
            // In memory only: a clone renders the same layout into a transparent texture.
            _hudTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Photo HUD" };
            _hudPanel = Instantiate(_hudOriginalPanel);
            _hudPanel.targetTexture = _hudTarget;
            _hudPanel.clearColor = true;
            _hudPanel.colorClearValue = Color.clear;
            _hudDocument.panelSettings = _hudPanel;
            _hudDocument.enabled = true;
            var players = Fighters
                .Select(f => new HUDManager.HudPlayer(f.Id, $"P{f.Id}", _entries[f.Id].Definition.Class, f.Id == HudLocal))
                .OrderBy(p => p.EntityId).ToList();
            _hudManager.Initialize(_bridge.GetState, players, HudStocks);
            _hudManager.SetCharacterDefinition(_entries[HudLocal].Definition);
            _hudManager.SetCamera(_views.Count > 0 ? _views[0].Camera : Template);
            _hudManager.Refresh();
            HideChat();
        }

        // Chat attaches itself to the HUD document; store shots show the match HUD only.
        private void HideChat()
        {
            var root = _hudDocument != null ? _hudDocument.rootVisualElement : null;
            if (root == null) return;
            foreach (string elementName in new[] { "chat-root", "chat-region-hint" })
                root.Query<VisualElement>(elementName).ForEach(element => element.style.display = DisplayStyle.None);
        }

        protected override void OnMatchFixedUpdate()
        {
            _inputs.Clear();
            foreach (var fighter in Fighters)
                _inputs[fighter.Id] = BuildInput(fighter, (int)Tick);
            _bridge.Tick(_inputs);
            Tick++;
            foreach (var hit in _bridge.LastTickHits)
                _hits.Add(new
                {
                    tick = Tick, owner = hit.OwnerEntityId.ToString(), target = hit.TargetEntityId.ToString(),
                    slot = hit.AttackSlot, damage = hit.Damage, hitstop = hit.HitstopTicks, force = hit.ImpactForce,
                    blocked = hit.Blocked, tier = CombatFeedback.Classify(in hit).ToString()
                });
            foreach (var fighter in Fighters)
                _renderers[fighter.Id].ApplyServerState(_bridge.GetState(fighter.Id));
            _projectiles.OnTick();
            _feedback.OnTick();
            UpdateSwordTrailsFromResolver(_bridge.Resolver);
            PresentTimelineEvents();
            if (_hudDocument != null)
            {
                _hudManager.Refresh();
                HideChat();
            }
            foreach (var view in _views) Aim(view, snap: false);
            LogStates();
        }

        private InputState BuildInput(PhotoFighter fighter, int tick)
        {
            var self = _bridge.GetState(fighter.Id);
            float aimYaw = fighter.YawDegrees, distance = fighter.AimDistance;
            if (fighter.Target != 0 && _entries.ContainsKey(fighter.Target))
            {
                var target = _bridge.GetState(fighter.Target);
                float dx = target.PX - self.PX, dz = target.PZ - self.PZ;
                if (dx * dx + dz * dz > 1e-4f) aimYaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                if (distance < 0f) distance = Mathf.Sqrt(dx * dx + dz * dz);
            }
            var input = new InputState
            {
                TargetEntityId = (byte)fighter.Target,
                AimDistance = (ushort)Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(distance, 0f) * 100f), 0, 6500),
                AimPitch = (short)Mathf.Clamp(Mathf.RoundToInt(fighter.AimPitchDegrees * 100f), -9000, 9000)
            };
            foreach (var step in fighter.Inputs)
            {
                if (tick < step.From || tick > step.To) continue;
                bool edge = tick == step.From;
                if (step.FaceYaw.HasValue)
                {
                    aimYaw = step.FaceYaw.Value;
                    input.FaceToCamera |= edge;
                }
                if (edge && step.Slot != AbilitySlots.None) input.ActiveSlot = step.Slot;
                if (step.Aim) input.IsAiming = true;
                if (step.Jump)
                {
                    input.JumpHeld = true;
                    input.Jump |= edge;
                }
                if (step.Down)
                {
                    input.Down = true;
                    input.DownPressed |= edge;
                }
                if (step.Shield)
                {
                    input.ShieldHeld = true;
                    input.ShieldPressed |= edge;
                }
                if (step.Grab) input.GrabPressed |= edge;
                if (step.MoveYaw.HasValue)
                {
                    float radians = step.MoveYaw.Value * Mathf.Deg2Rad;
                    input.MoveX = Mathf.Sin(radians);
                    input.MoveY = Mathf.Cos(radians);
                }
            }
            short wire = (short)Mathf.Clamp(Mathf.RoundToInt(Mathf.DeltaAngle(0f, aimYaw) * 100f), -18000, 18000);
            input.AimYaw = wire;
            input.FacingYaw = wire;
            return input;
        }

        private void LogStates()
        {
            _states.Append("{\"tick\":").Append(Tick).Append(",\"fighters\":[");
            for (int i = 0; i < Fighters.Count; i++)
            {
                var fighter = Fighters[i];
                var s = _bridge.GetState(fighter.Id);
                float feet = s.PY - _entries[fighter.Id].Definition.CapsuleHeight * 0.5f;
                if (i > 0) _states.Append(',');
                _states.Append("{\"id\":").Append(fighter.Id)
                    .Append(",\"x\":").Append(Num(s.PX)).Append(",\"y\":").Append(Num(feet)).Append(",\"z\":").Append(Num(s.PZ))
                    .Append(",\"vy\":").Append(Num(s.VY))
                    .Append(",\"state\":\"").Append(s.State).Append("\",\"slot\":").Append(s.AttackSlot)
                    .Append(",\"grounded\":").Append(s.IsGrounded ? "true" : "false")
                    .Append(",\"damage\":").Append(s.DamagePercent)
                    .Append(",\"hitstun\":").Append(s.HitstunTicks)
                    .Append(",\"facing\":").Append(Num(s.FacingYaw * Mathf.Rad2Deg)).Append('}');
            }
            _states.Append("]}\n");
        }

        private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private IEnumerator CaptureFrames()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (!Done && Failure == null)
            {
                yield return endOfFrame;
                if (!Ready || Tick == _capturedTick) continue;
                try
                {
                    if (Tick >= From && Tick <= To && (Tick - From) % Step == 0)
                    {
                        foreach (var view in _views) Write(view.Target, view.Spec.Name, alpha: false);
                        if (_hudTarget != null) Write(_hudTarget, "hud", alpha: true);
                        FramesWritten++;
                    }
                    _capturedTick = Tick;
                    if (Tick >= To)
                    {
                        Directory.CreateDirectory(OutputDirectory);
                        File.WriteAllText(Path.Combine(OutputDirectory, "states.jsonl"), _states.ToString());
                        Done = true;
                    }
                }
                catch (Exception error)
                {
                    Failure = error.ToString();
                    Debug.LogException(error);
                }
            }
        }

        private void Write(RenderTexture target, string folder, bool alpha)
        {
            Texture2D pixels;
            if (alpha)
                pixels = _hudPixels ??= new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            else
                pixels = _pixels ??= new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            }
            finally { RenderTexture.active = previous; }
            string directory = Path.Combine(OutputDirectory, folder);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"{Tick:000}.{(alpha ? "png" : "jpg")}"),
                alpha ? pixels.EncodeToPNG() : pixels.EncodeToJPG(Quality));
        }

        private object BuildSurvey()
        {
            var triangles = Arena.CollisionTriangles ?? Array.Empty<CollisionTriangle>();
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var t in triangles)
            {
                min = Vector3.Min(min, Vector3.Min(new Vector3(t.AX, t.AY, t.AZ), Vector3.Min(new Vector3(t.BX, t.BY, t.BZ), new Vector3(t.CX, t.CY, t.CZ))));
                max = Vector3.Max(max, Vector3.Max(new Vector3(t.AX, t.AY, t.AZ), Vector3.Max(new Vector3(t.BX, t.BY, t.BZ), new Vector3(t.CX, t.CY, t.CZ))));
            }
            var landmarks = new List<object>();
            if (StageVisual != null && Landmarks.Length > 0)
                foreach (var renderer in StageVisual.GetComponentsInChildren<Renderer>(true))
                {
                    string path = PathOf(renderer.transform);
                    if (!Landmarks.Any(filter => path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    var bounds = renderer.bounds;
                    landmarks.Add(new { path, center = V(bounds.center), size = V(bounds.size) });
                    if (landmarks.Count >= 120) break;
                }
            var map = Arena.Heightmap;
            return new
            {
                arena = Arena.Name,
                collision = triangles.Length == 0 ? null : new { min = V(min), max = V(max) },
                bounds = new { Arena.MinX, Arena.MaxX, Arena.MinZ, Arena.MaxZ },
                kill = new { height = Arena.KillHeight, top = Arena.KillTop, Arena.KillMinX, Arena.KillMaxX, Arena.KillMinZ, Arena.KillMaxZ },
                heightmap = new { map.Width, map.Height, map.CellSize, map.OriginX, map.OriginZ },
                spawns = (Arena.SpawnPoints ?? Array.Empty<SpawnPoint>())
                    .Select(s => new { x = s.X, y = s.Y, z = s.Z, yaw = s.Yaw * Mathf.Rad2Deg }).ToArray(),
                cameras = _views.Select(v => new
                {
                    name = v.Spec.Name, position = V(v.Position), look = V(v.Look), fov = v.Spec.Fov, ortho = v.Spec.Ortho,
                    aspect = (float)Width / Height
                }).ToArray(),
                landmarks
            };
        }

        private string PathOf(Transform transform)
        {
            var names = new List<string>();
            for (var current = transform; current != null && current != StageVisual; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static float[] V(Vector3 value) => new[] { Round(value.x), Round(value.y), Round(value.z) };
        private static float Round(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Round(value * 100f) / 100f;

        public object Inspect() => new
        {
            success = Failure == null, failure = Failure, ready = Ready, done = Done, tick = Tick, frames = FramesWritten,
            hud = _hudTarget != null, hits = _hits,
            fighters = Fighters.Select(f => new
            {
                id = f.Id.ToString(), package = f.Package,
                state = _bridge != null && _entries.ContainsKey(f.Id) ? (object)_bridge.GetState(f.Id) : null
            }).ToArray()
        };

        protected override void OnDestroy()
        {
            if (_hudDocument != null)
            {
                _hudDocument.panelSettings = _hudOriginalPanel;
                _hudDocument.enabled = _hudWasEnabled;
            }
            if (_hudPanel != null) Destroy(_hudPanel);
            if (_hudTarget != null)
            {
                _hudTarget.Release();
                Destroy(_hudTarget);
            }
            foreach (var view in _views)
            {
                if (view.Camera != null) Destroy(view.Camera.gameObject);
                if (view.Target != null)
                {
                    view.Target.Release();
                    Destroy(view.Target);
                }
            }
            if (_pixels != null) Destroy(_pixels);
            if (_hudPixels != null) Destroy(_hudPixels);
            if (StageVisual != null && StageVisual.parent != null) Destroy(StageVisual.parent.gameObject);
            base.OnDestroy();
        }
    }
}
#endif
