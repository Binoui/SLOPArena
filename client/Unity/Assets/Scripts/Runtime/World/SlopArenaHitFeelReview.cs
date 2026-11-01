#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SlopArena.Client;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Disposable hit-feel review duel: an attacker presses one canonical move on an idle victim,
    /// Shared resolves it and the normal renderers present it. One simulation tick runs per
    /// rendered frame, so each capture is a deterministic tick. Never included in a player build.
    /// </summary>
    public sealed class SlopArenaHitFeelReview : MatchBase
    {
        public const ulong AttackerId = 1, VictimId = 2;

        public ArenaDefinition Arena;
        public string AttackerPackage = "fightguy", VictimPackage = "manki";
        public byte SlotWire = AbilitySlots.Normal3;
        public float Distance = 1.2f;
        public ushort VictimDamage;
        // Spawns sit above the floor: let both fighters land and settle before the press.
        public int PressTick = 40, CaptureTicks = 90;
        public HitFeelStrength Strength = HitFeelStrength.Default;
        public bool TrackWide;
        /// <summary>Ticks to keep aiming after the press, which charges aim-hold specials.</summary>
        public int HoldTicks;
        /// <summary>
        /// Degrees both cameras orbit around the fighters: 0 is the side view, 90 looks from behind
        /// the attacker along the attack, like the gameplay camera.
        /// </summary>
        public float CameraOrbitDegrees;
        /// <summary>The victim holds its shield the whole duel, so blocked moves show their blocked path (Sir Bonk's
        /// Joust skids to a stop on a shield).</summary>
        public bool VictimShield;
        public bool Airborne, VictimAirborne;
        public UnityEngine.Camera CloseCamera = null!, WideCamera = null!;
        public RenderTexture CloseTarget = null!, WideTarget = null!;
        public string OutputDirectory = "";

        private LocalSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;
        private CombatFeedback _feedback = null!;
        private ProjectileVFXManager _projectiles = null!;
        private PlayerRenderer _attacker = null!, _victim = null!;
        private readonly Dictionary<ulong, InputState> _inputs = new();
        private readonly List<object> _hits = new();
        private short _attackerYaw, _victimYaw;
        private Vector3 _forward = Vector3.forward;
        private Vector3 _wideFocus;
        private float _wideDistance;
        private Texture2D? _pixels;
        private uint _capturedTick;

        public uint Tick { get; private set; }
        public bool Ready { get; private set; }
        public bool Done { get; private set; }
        public string? Failure { get; private set; }
        public int FramesWritten { get; private set; }

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
            if (!Ready || Done || Failure != null || Tick >= PressTick + CaptureTicks) return;
            try { OnMatchFixedUpdate(); }
            catch (Exception error)
            {
                Failure = error.ToString();
                Debug.LogException(error);
            }
        }

        protected override bool ShowFighterIndicators => false;


        protected override void OnMatchStart()
        {
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
                throw new InvalidOperationException("Local content catalog: " + failure);
            var attacker = catalog.ResolvePackage(AttackerPackage)
                ?? throw new InvalidOperationException("Missing roster package: " + AttackerPackage);
            var victim = catalog.ResolvePackage(VictimPackage)
                ?? throw new InvalidOperationException("Missing roster package: " + VictimPackage);
            if (Arena.SpawnPoints == null || Arena.SpawnPoints.Length == 0)
                throw new InvalidOperationException("The review arena has no spawn points.");

            // Each preset spawns its own stage so the stage atmosphere re-applies after
            // MatchVisualStyle, exactly as in Training; OnDestroy removes it again.
            SpawnStageVisual(Arena, cameraObstruction: false);
            _bridge = new LocalSimulationBridge(Arena, NoWinMatchRule.Instance);
            _feedback = gameObject.AddComponent<CombatFeedback>();
            _feedback.SetSimulation(_bridge);
            _projectiles = gameObject.AddComponent<ProjectileVFXManager>();
            _projectiles.SetSimulation(_bridge.InternalSim);

            // The duel runs along the first spawn's facing, centred on that spawn.
            var spawn = Arena.SpawnPoints[0];
            var forward = new Vector3(Mathf.Sin(spawn.Yaw), 0f, Mathf.Cos(spawn.Yaw));
            var center = new Vector3(spawn.X, spawn.Y, spawn.Z);
            _attackerYaw = WireYaw(spawn.Yaw);
            _victimYaw = WireYaw(spawn.Yaw + Mathf.PI);
            _attacker = Spawn(attacker, AttackerId, center - forward * (Distance * 0.5f), spawn.Yaw, 0);
            _victim = Spawn(victim, VictimId, center + forward * (Distance * 0.5f), spawn.Yaw + Mathf.PI, VictimDamage);
            _forward = forward;
            Frame(center, forward);
            Ready = true;
        }

        private PlayerRenderer Spawn(MatchContentEntry entry, ulong id, Vector3 position, float yaw, ushort damage)
        {
            var renderer = new GameObject($"Review {entry.DisplayName} {id}").AddComponent<PlayerRenderer>();
            renderer.transform.SetParent(transform, false);
            if (!SetupRenderer(renderer, entry, Arena, id, false))
                throw new InvalidOperationException("Could not present package " + entry.Identity.PackageId);
            // SetupRenderer registers with the first CombatFeedback in the scene; this duel owns its own.
            _feedback.RegisterRenderer(renderer);
            if (renderer.HitFeel != null)
            {
                renderer.HitFeel.Strength = Strength;
                renderer.HitFeel.IgnoreReducedFlashing = true;
            }
            var state = new CharacterState
            {
                PX = position.x, PY = position.y, PZ = position.z, FacingYaw = yaw, AimYaw = yaw,
                JumpsLeft = entry.Definition.Movement.MaxJumps, AirDodgesLeft = 1,
                DamagePercent = damage, MatchState = MatchState.Playing
            };
            _bridge.RegisterEntity(id, entry.Definition, state, entry.BakedAnimation);
            _bridge.SetRespawnPosition(id, position.x, position.y, position.z, yaw);
            renderer.ApplyServerState(_bridge.GetState(id), _bridge.GetActiveMove(id)?.Slot);
            return renderer;
        }

        /// <summary>Side views: attacker on the left, victim on the right, launches travel right.</summary>
        private void Frame(Vector3 center, Vector3 forward)
        {
            Vector3 side = Side(forward);
            Place(CloseCamera, center + forward * 0.35f + Vector3.up * 0.9f, side, 5.2f, 0.45f, 30f);
            Place(WideCamera, center + forward * 2.2f + Vector3.up * 1.4f, side, 8f, 0.9f, 40f);
        }

        /// <summary>Fit the close camera to both fighters' gameplay capsules so tall rigs stay in frame.</summary>
        private void FrameFighters(CharacterState attacker, CharacterState victim)
        {
            float height = Mathf.Max(_attacker.CapsuleHeight, _victim.CapsuleHeight) + 0.3f;
            var a = new Vector3(attacker.PX, attacker.PY, attacker.PZ);
            var v = new Vector3(victim.PX, victim.PY, victim.PZ);
            // PY is the capsule centre (CharacterDefinition.BoneYToWorldY), not the feet.
            float floor = Mathf.Min(a.y - _attacker.CapsuleHeight * 0.5f, v.y - _victim.CapsuleHeight * 0.5f);
            var middle = new Vector3((a.x + v.x) * 0.5f, floor + height * 0.5f, (a.z + v.z) * 0.5f);
            Vector3 side = Side(_forward);
            const float fov = 30f;
            float tangent = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float gap = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(v.x, 0f, v.z));
            // Fighters fill about 80% of the height, with room on the right for recoil.
            float distance = Mathf.Max(height / (0.8f * 2f * tangent), (gap + 1.8f) / (2f * tangent * CloseCamera.aspect), 3.2f);
            Vector3 focus = middle + _forward * 0.3f;
            Place(CloseCamera, focus, side, distance, distance * 0.08f, fov);
            _wideFocus = focus + _forward * 2f + Vector3.up * 0.6f;
            _wideDistance = distance * 1.8f;
            Place(WideCamera, _wideFocus, side, _wideDistance, _wideDistance * 0.09f, 40f);
        }

        /// <summary>Eases the wide camera toward a frame holding both fighters' capsules.</summary>
        private void TrackWideCamera(CharacterState attacker, CharacterState victim)
        {
            float attackerHalf = _attacker.CapsuleHeight * 0.5f, victimHalf = _victim.CapsuleHeight * 0.5f;
            float bottom = Mathf.Min(attacker.PY - attackerHalf, victim.PY - victimHalf);
            float top = Mathf.Max(attacker.PY + attackerHalf, victim.PY + victimHalf);
            var middle = new Vector3((attacker.PX + victim.PX) * 0.5f, (bottom + top) * 0.5f, (attacker.PZ + victim.PZ) * 0.5f);
            float gap = Vector2.Distance(new Vector2(attacker.PX, attacker.PZ), new Vector2(victim.PX, victim.PZ));
            float tangent = Mathf.Tan(20f * Mathf.Deg2Rad);
            float distance = Mathf.Max((top - bottom + 2f) / (2f * tangent), (gap + 3f) / (2f * tangent * WideCamera.aspect), 6f);
            _wideFocus = Vector3.Lerp(_wideFocus, middle, 0.2f);
            _wideDistance = Mathf.Lerp(_wideDistance, distance, 0.2f);
            Place(WideCamera, _wideFocus, Side(_forward), _wideDistance, _wideDistance * 0.09f, 40f);
        }

        /// <summary>The cameras' direction from the fighters: the side, orbited toward the attacker's back.</summary>
        private Vector3 Side(Vector3 forward)
            => Quaternion.AngleAxis(CameraOrbitDegrees, Vector3.up) * Vector3.Cross(Vector3.up, forward);

        private static void Place(UnityEngine.Camera camera, Vector3 focus, Vector3 side, float distance,
            float rise, float fieldOfView)
        {
            Vector3 offset = side * distance + Vector3.up * rise;
            camera.fieldOfView = fieldOfView;
            camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset, Vector3.up));
        }

        protected override void OnMatchFixedUpdate()
        {
            _inputs.Clear();
            _inputs[AttackerId] = new InputState
            {
                FacingYaw = _attackerYaw, AimYaw = _attackerYaw, TargetEntityId = (byte)VictimId,
                // Ground-cursor specials (Manki's Round Bomb) land at the victim, as a player aims.
                AimDistance = (ushort)Mathf.RoundToInt(Distance * 100f),
                ActiveSlot = Tick == PressTick ? SlotWire : AbilitySlots.None,
                Jump = Airborne && Tick == PressTick - 12,
                JumpHeld = Airborne && Tick >= PressTick - 12 && Tick < PressTick,
                IsAiming = HoldTicks > 0 && Tick >= PressTick && Tick < PressTick + HoldTicks
            };
            _inputs[VictimId] = new InputState
            {
                FacingYaw = _victimYaw, AimYaw = _victimYaw, ShieldHeld = VictimShield,
                Jump = VictimAirborne && Tick == PressTick - 12,
                JumpHeld = VictimAirborne && Tick >= PressTick - 12 && Tick < PressTick
            };
            _bridge.Tick(_inputs);
            Tick++;
            foreach (var hit in _bridge.LastTickHits)
                _hits.Add(new
                {
                    tick = Tick, owner = hit.OwnerEntityId.ToString(), target = hit.TargetEntityId.ToString(),
                    slot = hit.AttackSlot, damage = hit.Damage, hitstop = hit.HitstopTicks, force = hit.ImpactForce,
                    blocked = hit.Blocked, tier = CombatFeedback.Classify(in hit).ToString()
                });
            var attackerState = _bridge.GetState(AttackerId);
            var victimState = _bridge.GetState(VictimId);
            _attacker.ApplyServerState(attackerState, _bridge.GetActiveMove(AttackerId)?.Slot);
            _victim.ApplyServerState(victimState, _bridge.GetActiveMove(VictimId)?.Slot);
            // Frame the landed fighters once, just before the first captured tick.
            if (Tick == PressTick)
                FrameFighters(attackerState, victimState);
            else if (TrackWide && Tick > PressTick)
                TrackWideCamera(attackerState, victimState);
            _projectiles.OnTick();
            _feedback.OnTick();
            UpdateSwordTrailsFromResolver(_bridge.Resolver);
            PresentTimelineEvents();
        }

        private IEnumerator CaptureFrames()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (!Done && Failure == null)
            {
                yield return endOfFrame;
                if (!Ready || Tick < PressTick || Tick == _capturedTick) continue;
                try
                {
                    Write(CloseTarget, "close");
                    Write(WideTarget, "wide");
                    _capturedTick = Tick;
                    FramesWritten++;
                    if (Tick >= PressTick + CaptureTicks) Done = true;
                }
                catch (Exception error)
                {
                    Failure = error.ToString();
                    Debug.LogException(error);
                }
            }
        }

        private void Write(RenderTexture target, string view)
        {
            _pixels ??= new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                _pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            }
            finally { RenderTexture.active = previous; }
            string directory = Path.Combine(OutputDirectory, view);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"{Tick - PressTick:000}.jpg"), _pixels.EncodeToJPG(92));
        }

        private static short WireYaw(float radians)
            => (short)Mathf.Clamp(Mathf.RoundToInt(Mathf.DeltaAngle(0f, radians * Mathf.Rad2Deg) * 100f), -18000, 18000);

        public object Inspect() => new
        {
            success = Failure == null, failure = Failure, ready = Ready, done = Done, tick = Tick, pressTick = PressTick,
            frames = FramesWritten, attacker = AttackerPackage, victim = VictimPackage, hits = _hits,
            localReducedFlashing = SlopArena.Client.UI.ClientSettingsService.Instance.ReducedFlashing,
            attackerFeel = _attacker != null ? _attacker.HitFeel?.Diagnostics() : null,
            victimFeel = _victim != null ? _victim.HitFeel?.Diagnostics() : null,
            strength = new { Strength.Shake, Strength.Flash, Strength.Squash, Strength.Lean, Strength.Trail },
            attackerState = _bridge?.GetState(AttackerId), victimState = _bridge?.GetState(VictimId)
        };

        protected override void OnDestroy()
        {
            if (_pixels != null) Destroy(_pixels);
            if (StageVisual != null && StageVisual.parent != null) Destroy(StageVisual.parent.gameObject);
            base.OnDestroy();
        }
    }
}
#endif
