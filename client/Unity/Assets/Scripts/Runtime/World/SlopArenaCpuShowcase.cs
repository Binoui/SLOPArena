#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SlopArena.Client;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;
using SlopArena.Client.World;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using UnityEngine;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Disposable four-CPU filming setup. Existing bots supply inputs, Shared resolves
    /// the fight, and MatchBase/PlayerRenderer present it. Never included in a player build.
    /// </summary>
    public sealed class SlopArenaCpuShowcase : MatchBase
    {
        public static readonly string[] Packages = { "fightguy", "manki", "wibou", "bonk" };
        public string Stage = "after_hours";
        public int Seed = 7;
        public float SpawnScale = 1f;
        public CpuDifficulty Difficulty = CpuDifficulty.Hard;
        public UnityEngine.Camera FilmCamera;
        public bool FilmReveal, FilmCaptions;
        public Font FilmFont;
        private LocalSimulationBridge _bridge;
        protected override ISimulationBridge Bridge => _bridge;
        private ArenaDefinition _arena;
        private CombatFeedback _feedback;
        private ProjectileVFXManager _projectiles;
        private readonly List<Fighter> _fighters = new();
        private readonly Dictionary<ulong, InputState> _inputs = new();
        private Vector3 _cameraFocus;
        private float _cameraDistance = 22f;
        private uint _runUntil;
        private int _acceptedHits, _blocks;
        public uint Tick { get; private set; }
        public bool Ready { get; private set; }
        public bool Frozen { get; private set; } = true;
        public string Failure { get; private set; }
        public bool Recording { get; private set; }
        public int RecordedFrames { get; private set; }
        private Process _encoder;
        private Task<string> _encoderErrors;
        private Texture2D _pixels;
        private int _previousCaptureRate;
        private Bounds _billboard;
        private Vector3 _revealPosition;
        private Quaternion _revealRotation = Quaternion.identity;
        private float _revealFov;
        private bool _filmingStarted;
        private GUIStyle _captionStyle;

        private sealed class Fighter
        {
            public ulong Id;
            public MatchContentEntry Entry;
            public PlayerRenderer Renderer;
            // Policies cache character-specific reach/pose data: do not share one across kits.
            public readonly HeuristicBotPolicy Policy = new();
            public readonly BotMemory Memory = new();
            public System.Random Random;
            public byte LastDeaths;
            public int Hits;
            public readonly int[] Presses = new int[AbilitySlots.Count];
        }

        // Deliberately omit MatchBase's human input, pause menu and match music for filming.
        private void Start()
        {
            try
            {
                OnMatchStart();
                gameObject.AddComponent<MatchVisualStyle>().Apply();
            }
            catch (Exception error)
            {
                Failure = error.ToString();
                UnityEngine.Debug.LogException(error);
                enabled = false;
            }
        }

        private void FixedUpdate() => OnMatchFixedUpdate();

        protected override bool ShowFighterIndicators => false;


        protected override void OnMatchStart()
        {
            FilmCamera.transform.SetParent(null, true);
            string path = BakedContentPaths.ResolveArena(Stage);
            var loaded = path == null ? null : ArenaBinaryFormat.LoadFromFile(path);
            if (loaded is not ArenaDefinition arena || arena.Purpose != ArenaPurpose.PvP
                || arena.SpawnPoints.Length < Packages.Length)
                throw new InvalidOperationException("The showcase needs a baked PvP arena with four spawns.");
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
                throw new InvalidOperationException("Local content catalog: " + failure);
            _arena = arena;
            _bridge = new LocalSimulationBridge(arena, NoWinMatchRule.Instance);
            _feedback = gameObject.AddComponent<CombatFeedback>();
            _feedback.SetSimulation(_bridge);
            _projectiles = gameObject.AddComponent<ProjectileVFXManager>();
            _projectiles.SetSimulation(_bridge.InternalSim);
            SpawnStageVisual(arena, cameraObstruction: false);
            if (FilmReveal)
            {
                var poster = StageVisual.GetComponentsInChildren<MeshRenderer>()
                    .SingleOrDefault(x => x.name == "After Hours Fight Night Billboard")
                    ?? throw new InvalidOperationException("Apply the fight-night billboard before filming its reveal.");
                _billboard = poster.bounds;
            }
            for (int i = 0; i < Packages.Length; i++)
            {
                var entry = catalog.ResolvePackage(Packages[i])
                    ?? throw new InvalidOperationException("Missing roster package: " + Packages[i]);
                var fighter = new Fighter
                {
                    Id = (ulong)i + 1, Entry = entry,
                    Renderer = new GameObject("CPU " + entry.DisplayName).AddComponent<PlayerRenderer>(),
                    Random = new System.Random(unchecked(Seed + i * 7919))
                };
                fighter.Memory.Difficulty = Difficulty;
                fighter.Renderer.transform.SetParent(transform, false);
                if (!SetupRenderer(fighter.Renderer, entry, arena, fighter.Id, false))
                    throw new InvalidOperationException("Could not present package " + Packages[i]);
                _feedback.RegisterRenderer(fighter.Renderer);
                var spawn = arena.SpawnPoints[i];
                // Filming setup only: gather the cast on After Hours' flat dance floor.
                float spawnX = spawn.X * SpawnScale, spawnZ = spawn.Z * SpawnScale;
                var state = new CharacterState
                {
                    PX = spawnX, PY = spawn.Y, PZ = spawnZ, FacingYaw = spawn.Yaw,
                    AimYaw = spawn.Yaw, JumpsLeft = entry.Definition.Movement.MaxJumps,
                    AirDodgesLeft = 1, MatchState = MatchState.Playing
                };
                _bridge.RegisterEntity(fighter.Id, entry.Definition, state, entry.BakedAnimation);
                _bridge.SetRespawnPosition(fighter.Id, spawnX, spawn.Y, spawnZ, spawn.Yaw);
                fighter.Renderer.ApplyServerState(_bridge.GetState(fighter.Id), _bridge.GetActiveMove(fighter.Id)?.Slot);
                _fighters.Add(fighter);
            }
            FilmCamera.fieldOfView = 42f;
            Ready = true;
            UpdateCamera(true);
        }

        public void Run(uint ticks)
        {
            if (!Ready || Recording || ticks == 0 || ticks > 3600)
                throw new InvalidOperationException("Run requires a ready, non-recording showcase and 1–3600 ticks.");
            _runUntil = Tick + ticks;
            Frozen = false;
        }

        protected override void OnMatchFixedUpdate()
        {
            if (!Ready || Frozen) return;
            _inputs.Clear();
            // Every decision samples the same pre-tick world; no bot sees another's new input.
            foreach (var fighter in _fighters)
            {
                var self = _bridge.GetState(fighter.Id);
                foreach (var other in _fighters)
                    if (other.Id != fighter.Id)
                        fighter.Policy.ObserveOpponent(fighter.Memory, _bridge.GetState(other.Id),
                            _bridge.InternalSim.GetDefinition(other.Id), activeMove: _bridge.GetActiveMove(other.Id)?.Slot);
                fighter.Memory.ChooseOpponent(self);
                var input = fighter.Policy.Decide(self, fighter.Entry.Definition.ForForm(self.Form.Active),
                    fighter.Random, fighter.Memory, _arena, fighter.Entry.BakedAnimation,
                    activeMove: _bridge.GetActiveMove(fighter.Id)?.Slot,
                    canSwapCancel: _bridge.InternalSim.CanSwapCancel(fighter.Id));
                _inputs[fighter.Id] = input;
                if (input.ActiveSlot > 0 && input.ActiveSlot <= fighter.Presses.Length)
                    fighter.Presses[input.ActiveSlot - 1]++;
            }
            _bridge.Tick(_inputs);
            Tick++;
            foreach (var fighter in _fighters)
            {
                var state = _bridge.GetState(fighter.Id);
                if (state.Deaths != fighter.LastDeaths)
                {
                    fighter.LastDeaths = state.Deaths;
                    fighter.Memory.Reset();
                }
                foreach (var hit in _bridge.LastTickHits)
                {
                    if (hit.OwnerEntityId != fighter.Id) continue;
                    if (hit.Blocked) _blocks++;
                    else { fighter.Hits++; _acceptedHits++; }
                    // BotMemory keeps only contacts on the opponent it chose.
                    var opponent = _bridge.GetState(hit.TargetEntityId);
                    fighter.Memory.RecordOpponentContact(hit.AttackSlot, hit.Blocked ? CpuContactKind.Block : CpuContactKind.Hit, opponent, hit.HitstopTicks);
                }
                fighter.Renderer.ApplyServerState(state, _bridge.GetActiveMove(fighter.Id)?.Slot);
            }
            _projectiles.OnTick();
            _feedback.OnTick();
            UpdateSwordTrailsFromResolver(_bridge.Resolver);
            PresentTimelineEvents();
            if (!Recording && Tick >= _runUntil) Frozen = true;
        }

        private void LateUpdate()
        {
            if (Ready) UpdateCamera(false);
        }

        private void UpdateCamera(bool snap)
        {
            var points = _fighters.Select(f => _bridge.GetState(f.Id))
                .Where(s => s.PY >= -3f && Mathf.Abs(s.PX) < 22f && Mathf.Abs(s.PZ) < 20f)
                .Select(s => new Vector3(s.PX, s.PY + .9f, s.PZ)).ToArray();
            if (points.Length == 0) return;
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            Vector3 target = bounds.center;
            target.x = Mathf.Clamp(target.x, -7, 7); target.z = Mathf.Clamp(target.z, -5, 5);
            var rotation = Quaternion.Euler(27, 0, 0);
            const float fightFov = 42f;
            float tangent = Mathf.Tan(fightFov * .5f * Mathf.Deg2Rad);
            float distance = 14f;
            foreach (var point in points)
            {
                Vector3 local = Quaternion.Inverse(rotation) * (point - target);
                distance = Mathf.Max(distance, (Mathf.Abs(local.x) + 2f) / (tangent * FilmCamera.aspect) - local.z,
                    (Mathf.Abs(local.y) + 2f) / tangent - local.z);
            }
            float blend = snap ? 1 : 1 - Mathf.Exp(-2.5f * Time.deltaTime);
            _cameraFocus = Vector3.Lerp(_cameraFocus, target, blend);
            _cameraDistance = Mathf.Lerp(_cameraDistance, Mathf.Clamp(distance, 14, 35), blend);
            Vector3 position = _cameraFocus - rotation * Vector3.forward * _cameraDistance;
            float reveal = FilmReveal && _filmingStarted
                ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.4f, 4f, RecordedFrames / 30f)) : 1;
            FilmCamera.fieldOfView = Mathf.Lerp(_revealFov, fightFov, reveal);
            FilmCamera.transform.SetPositionAndRotation(Vector3.Lerp(_revealPosition, position, reveal),
                Quaternion.Slerp(_revealRotation, rotation, reveal));
        }

        protected override void OnGUI()
        {
            if (!FilmCaptions || !_filmingStarted) return;
            float seconds = RecordedFrames / 30f;
            string text = seconds < 7 ? null : seconds < 9 ? "F2P" : seconds < 11 ? "ONLINE"
                : seconds < 13 ? "OPENSOURCE" : seconds < 15 ? "JANKY AS HELL" : null;
            if (text == null) return;
            _captionStyle ??= new GUIStyle { font = FilmFont, alignment = TextAnchor.MiddleCenter };
            _captionStyle.fontSize = Mathf.RoundToInt(Screen.height * (text == "JANKY AS HELL" ? .115f : .135f));
            var rect = new Rect(Screen.width * .07f, Screen.height * .37f, Screen.width * .86f, Screen.height * .26f);
            _captionStyle.normal.textColor = new Color(0, 0, 0, .8f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 3, rect.width, rect.height), text, _captionStyle);
            _captionStyle.normal.textColor = Color.white;
            GUI.Label(rect, text, _captionStyle);
        }

        public object Inspect() => new
        {
            success = Failure == null, ready = Ready, failure = Failure, stage = Stage, seed = Seed,
            difficulty = Difficulty.ToString(), spawnScale = SpawnScale, tick = Tick, frozen = Frozen, recording = Recording,
            recordedFrames = RecordedFrames, reveal = FilmReveal, captions = FilmCaptions,
            firstCaptionSeconds = 7, acceptedHits = _acceptedHits, blocks = _blocks,
            fighters = _fighters.Select(f => new
            {
                entity = f.Id.ToString(), package = f.Entry.Identity.PackageId, version = f.Entry.Identity.Version,
                cookedHash = f.Entry.Identity.CookedContentHash, hits = f.Hits, target = f.Memory.OpponentId.ToString(),
                targetChanges = f.Memory.OpponentSwitches, presses = f.Presses, state = _bridge.GetState(f.Id)
            }).ToArray()
        };

        public void Record(string output, int seconds)
        {
            if (!Ready || Recording || seconds < 1 || seconds > 30 || Screen.width % 2 != 0 || Screen.height % 2 != 0)
                throw new InvalidOperationException("Recording requires a ready showcase, even viewport dimensions and 1–30 seconds.");
            if (File.Exists(output)) throw new IOException("Recording never overwrites an existing file.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            _encoder = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg", UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardError = true, CreateNoWindow = true,
                Arguments = $"-n -loglevel error -f rawvideo -pixel_format rgb24 -video_size {Screen.width}x{Screen.height} -framerate 30 -i - -vf vflip -an -c:v libx264 -preset fast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{output}\""
            }};
            _encoder.Start();
            _encoderErrors = _encoder.StandardError.ReadToEndAsync();
            _pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            _previousCaptureRate = Time.captureFramerate;
            Time.captureFramerate = 30;
            if (FilmReveal)
            {
                _revealPosition = FilmCamera.transform.position + Vector3.up * 9f;
                _revealRotation = Quaternion.LookRotation(_billboard.center - _revealPosition, Vector3.up);
                float radius = Mathf.Max(_billboard.extents.y, _billboard.extents.x / FilmCamera.aspect) * 1.12f;
                _revealFov = Mathf.Clamp(2 * Mathf.Atan(radius / Vector3.Distance(_revealPosition, _billboard.center)) * Mathf.Rad2Deg, 10, 35);
            }
            _filmingStarted = true;
            RecordedFrames = 0; Recording = true; Frozen = false;
            StartCoroutine(RecordFrames(seconds * 30));
        }

        private IEnumerator RecordFrames(int frames)
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (RecordedFrames < frames && Recording)
            {
                yield return endOfFrame;
                try
                {
                    if (Screen.width != _pixels.width || Screen.height != _pixels.height)
                        throw new InvalidOperationException("Game View dimensions changed during recording.");
                    _pixels.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
                    byte[] bytes = _pixels.GetRawTextureData<byte>().ToArray();
                    _encoder.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    RecordedFrames++;
                }
                catch (Exception error) { Failure = error.ToString(); break; }
            }
            FinishRecording();
        }

        public void FinishRecording()
        {
            if (_encoder == null) return;
            Frozen = true; Recording = false; Time.captureFramerate = _previousCaptureRate;
            try
            {
                _encoder.StandardInput.Close();
                if (!_encoder.WaitForExit(5000))
                {
                    _encoder.Kill(); Failure ??= "ffmpeg did not finish within five seconds.";
                }
                else if (_encoder.ExitCode != 0)
                    Failure ??= "ffmpeg failed: " + (_encoderErrors.IsCompleted ? _encoderErrors.Result : "see stderr");
            }
            finally
            {
                _encoder.Dispose(); _encoder = null;
                if (_pixels != null) Destroy(_pixels);
                _pixels = null;
            }
        }

        protected override void OnDestroy()
        {
            FinishRecording();
            base.OnDestroy();
        }
    }
}
#endif
