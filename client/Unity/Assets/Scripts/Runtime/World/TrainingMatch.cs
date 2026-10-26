using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using SlopArena.Client.Simulation;
using SlopArena.Client.Diagnostics;
using SlopArena.Shared.Telemetry;
namespace SlopArena.Client.World
{
    public enum NpcAiMode
    {
        Attack = 1,
        Idle = 2,
        Shield = 3,
        Avoid = 4,
    }
    
    public class TrainingMatch : MatchBase
    {
        [Header("Entities (NPC)")]
        [SerializeField] private PlayerRenderer _npcRenderer;
        // ^ Optional scene-placed renderer for the FIRST NPC (id 100, offline-scene dummy).
        // Runtime-created GameObjects back every slot; this seed keeps the serialized
        // scene wiring (SlopArenaSceneSetup) and the editor capture harness intact.

        [Header("Characters (Player)")]
        [SerializeField] private CharacterClass _playerClassOverride;
 
        [Header("Arena")]
        [SerializeField] private string _arenaNameOverride = "";

        [Header("Combat")]
        [SerializeField] private CombatFeedback _combatFeedback;
        [SerializeField] private ProjectileVFXManager _projectileVFX;
        [SerializeField] private NpcAiMode _npcAiMode = NpcAiMode.Idle;
        private CpuDifficulty _npcDifficulty = CpuDifficulty.Normal;

        [Header("Hitboxes")]
        [SerializeField] private bool _showHitboxes;

        private LocalSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;

        private uint _tick;
        // Heuristic-bot policy consumes delayed observations; per-NPC state (memory, rng, def)
        // lives in NpcSlot.
        private readonly HeuristicBotPolicy _npcPolicy = new();
        private readonly List<NpcSlot> _npcs = new();
        private int _nextNpcId = 101; // first NPC keeps entity id 100 (capture harness + Solo)

        private MatchContentEntry _npcEntry;
        private MatchContentEntry _playerEntry;
        private int _selectedNpcIndex;
#if UNITY_EDITOR
        // Launch-contract sentinel: logs the applied launch once per hit so the game can
        // be checked against tools/MoveDataReport (fightguy --parity). A mismatch here
        // (e.g. the 2026-08-14 x87 float bug: every hit took the unscaled force path)
        // means the game behaves differently from the report.
        private float _lastContractDamage;
#endif
        private ArenaDefinition _arenaDef;
        private const ulong NpcEntityId = 100;
        private TargetIndicator _lockIndicator;
        private TrainingSettingsPanel _settingsPanel;
        // Opt-in local measurement of this match (Settings → Gameplay); presentation-only.
        private PracticeSessionLog? _sessionLog;

        private sealed class NpcSlot
        {
            public ulong Id;
            public PlayerRenderer Renderer;
            public CharacterDefinition Def;
            public BotMemory Memory = new();
            public System.Random Rng;
            public SpawnPoint Spawn;
            public byte LastDeaths;
        }
        // Couch play (ADR-0041): Solo's P2 is a second Local Player reading its own pad.
        private InputController _p2Input;
        [NonSerialized] private InputActionMap _p2Map;
        private bool Couch => _p2Input != null;
        // Matches P2's orange-red HUD badge.
        private static readonly Color P2Accent = new(234f / 255f, 88f / 255f, 12f / 255f);
        protected override Func<ulong, Color?> LocalAccentOf =>
            Couch ? id => id == NpcEntityId ? P2Accent : null : null;
        private ushort _soloCountdownTicks;
        private ushort _lastSoloPlayerDeaths;
        private ushort _lastSoloNpcDeaths;
        private bool _soloResultsShown;

        // Presentation-only input seam used by VisualBaselineCaptureController and
        // bounded Editor sequences. The normal training path leaves this null.
        private InputState? _captureInputOverride;
        private object _captureInputSequenceOwner;

        public event Action<uint> CaptureSimulationTickCompleted;
        public uint CaptureTick => _tick;
        public bool CaptureInputOverrideActive => _captureInputOverride.HasValue;
        public bool CaptureSequenceReady
        {
            get
            {
                if (!enabled || _bridge == null || _playerRenderer == null || _playerDef == null
                    || MatchConfig.Mode != GameMode.Training || IsPaused || Time.timeScale <= 0f
                    || _npcs.Count == 0 || _bridge.InternalSim.GetDefinition(PlayerEntityId) == null
                    || _bridge.GetState(PlayerEntityId).EntityId != PlayerEntityId)
                    return false;
                return _npcs.All(npc => npc != null && npc.Def != null
                    && _bridge.InternalSim.GetDefinition(npc.Id) != null
                    && _bridge.GetState(npc.Id).EntityId == npc.Id);
            }
        }
        public IReadOnlyList<SpellResolver.HitResult> CaptureLastTickHits =>
            _bridge != null ? _bridge.LastTickHits : Array.Empty<SpellResolver.HitResult>();
        public IReadOnlyList<TimelinePresentationEvent> CaptureLastTickPresentationEvents =>
            _bridge != null ? _bridge.LastTickPresentationEvents : Array.Empty<TimelinePresentationEvent>();

        public CharacterState GetCaptureState(ulong entityId) => _bridge.GetState(entityId);
        public void SetCaptureState(ulong entityId, CharacterState state) =>
            _bridge.InternalSim.SetState(entityId, state);
        public void SetCaptureInput(InputState input)
        {
            if (_captureInputSequenceOwner != null)
                throw new InvalidOperationException("Capture input is owned by a Training command sequence.");
            _captureInputOverride = input;
        }
        /// <summary>Interpolate fighters between fixed steps; capture tooling disables it for exact-step frames.</summary>
        public void SetRenderInterpolation(bool enabled)
        {
            _renderInterpolation = enabled;
            if (_playerRenderer != null) _playerRenderer.RenderInterpolation = enabled;
            foreach (var npc in _npcs)
                if (npc?.Renderer != null) npc.Renderer.RenderInterpolation = enabled;
        }
        private bool _renderInterpolation = true;

        public void ClearCaptureInput()
        {
            if (_captureInputSequenceOwner != null)
                throw new InvalidOperationException("Capture input is owned by a Training command sequence.");
            _captureInputOverride = null;
        }
        public bool TryAcquireCaptureInputSequence(object owner)
        {
            if (owner == null || _captureInputSequenceOwner != null || _captureInputOverride.HasValue)
                return false;
            _captureInputSequenceOwner = owner;
            return true;
        }
        public void SetCaptureSequenceInput(object owner, InputState input)
        {
            if (!ReferenceEquals(_captureInputSequenceOwner, owner))
                throw new InvalidOperationException("The caller does not own Training capture input.");
            _captureInputOverride = input;
        }
        public void ReleaseCaptureInputSequence(object owner)
        {
            if (!ReferenceEquals(_captureInputSequenceOwner, owner))
                return;
            _captureInputOverride = null;
            _captureInputSequenceOwner = null;
        }
        // ── Training controls ──

        public const string MovementPreferencesKey = "SlopArena.Training.MovementPrototype.v1";
        [Serializable]
        private sealed class MovementPreferences
        {
            public GroundSpeedModel Model = GroundSpeedModel.Current;
            public bool Snap = true;
            public int RampTicks = 20;
            public GroundSpeedCurve Curve = GroundSpeedCurve.Linear;
            public float TurnaroundKeep = 1f;
            public int StopTicks = 20;
            public float StartRatio = 0.4f;
        }

        private GroundSpeedTuning _progressiveTuning = GroundSpeedTuning.Progressive();
        public GroundSpeedTuning ProgressiveTuning => _progressiveTuning;
        public GroundSpeedTuning MovementTuning => MovementModel == GroundSpeedModel.Progressive
            ? _progressiveTuning : GroundSpeedTuning.For(MovementModel);
        private GroundSpeedModel _groundSpeedModel = GroundSpeedModel.Current;
        private bool _stickSnap = true;
        public GroundSpeedModel MovementModel => MatchConfig.Mode == GameMode.Training
            ? _groundSpeedModel : GroundSpeedModel.Current;
        public bool MovementStickSnap => MatchConfig.Mode != GameMode.Training || _stickSnap;
        public string MovementPrototypeLabel => MovementModel == GroundSpeedModel.Progressive
            ? $"MOVEMENT · PROGRESSIVE\nSTART {_progressiveTuning.StartRatio * 100f:F0}% · {_progressiveTuning.RampTicks}T {(_progressiveTuning.Curve == GroundSpeedCurve.SlowStart ? "SLOW START" : "LINEAR")}\nKEEP {_progressiveTuning.TurnaroundKeep * 100f:F0}% · STOP {_progressiveTuning.StopTicks}T · SNAP {(MovementStickSnap ? "ON" : "OFF")}"
            : $"MOVEMENT · {MovementModel.ToString().ToUpperInvariant()} · SNAP {(MovementStickSnap ? "ON" : "OFF")}";

        public void SetMovementPrototype(GroundSpeedModel model, bool stickSnap, GroundSpeedTuning progressive = null)
        {
            if (MatchConfig.Mode != GameMode.Training || _bridge == null)
                throw new InvalidOperationException("Movement prototypes require an initialized Training match.");
            if (_captureInputSequenceOwner != null)
                throw new InvalidOperationException("Cannot switch movement during a captured Training sequence.");
            var profile = GroundSpeedTuning.For(model);
            if (progressive != null && progressive.Model != GroundSpeedModel.Progressive)
                throw new ArgumentException("Custom tuning must be Progressive.", nameof(progressive));
            var selectedProgressive = progressive ?? _progressiveTuning;
            _bridge.InternalSim.SetGroundSpeedTuning(model == GroundSpeedModel.Progressive ? selectedProgressive : profile);
            _progressiveTuning = selectedProgressive;
            _groundSpeedModel = model;
            _stickSnap = stickSnap;
            bool active = model != GroundSpeedModel.Current;
            _playerRenderer.GroundSpeedPrototypeActive = active;
            foreach (var npc in _npcs) npc.Renderer.GroundSpeedPrototypeActive = active;
            PlayerPrefs.SetString(MovementPreferencesKey, JsonUtility.ToJson(new MovementPreferences
            {
                Model = model, Snap = stickSnap, RampTicks = _progressiveTuning.RampTicks,
                Curve = _progressiveTuning.Curve, TurnaroundKeep = _progressiveTuning.TurnaroundKeep,
                StopTicks = _progressiveTuning.StopTicks, StartRatio = _progressiveTuning.StartRatio,
            }));
            PlayerPrefs.Save();
            _settingsPanel?.RefreshMovement();
            Debug.Log($"[MovementPrototype] {MovementPrototypeLabel}");
        }

        private void LoadMovementPrototype()
        {
            if (MatchConfig.Mode != GameMode.Training) return;
            var saved = new MovementPreferences();
            try
            {
                if (PlayerPrefs.HasKey(MovementPreferencesKey))
                    JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(MovementPreferencesKey), saved);
                GroundSpeedTuning.For(saved.Model);
                var tuning = GroundSpeedTuning.Progressive(saved.RampTicks, saved.Curve, saved.TurnaroundKeep, saved.StopTicks, saved.StartRatio);
                SetMovementPrototype(saved.Model, saved.Snap, tuning);
            }
            catch (ArgumentException)
            {
                SetMovementPrototype(GroundSpeedModel.Current, true, GroundSpeedTuning.Progressive());
            }
        }

        public int NpcCount => _npcs.Count;
        public int SelectedNpcIndex => _selectedNpcIndex;
        public NpcAiMode CurrentNpcMode => _npcAiMode;
        public CpuDifficulty CurrentNpcDifficulty => BotDifficultyProfile.Normalize(_npcDifficulty);
        public bool NoCooldowns => _bridge != null && _bridge.InternalSim.NoCooldownsEntityId == PlayerEntityId;
        public ulong SelectedNpcId => _npcs.Count > 0 ? _npcs[_selectedNpcIndex].Id : 0ul;
        public ulong GetNpcIdAt(int index) => _npcs[index].Id;

        public void SelectNpc(int index)
        {
            if (_npcs.Count == 0) return;
            _selectedNpcIndex = Math.Clamp(index, 0, _npcs.Count - 1);
            _settingsPanel?.RefreshSelection();
        }

        public void SetNoCooldowns(bool on)
        {
            _bridge.InternalSim.NoCooldownsEntityId = on ? PlayerEntityId : null;
            _sessionLog?.LogEvent((int)_tick, "no-cooldowns", on ? "on" : "off");
        }

        public void SetNpcMode(NpcAiMode mode)
        {
            if (_npcs.Count == 0 || !Enum.IsDefined(typeof(NpcAiMode), mode)) return;
            _npcAiMode = mode;
            foreach (var npc in _npcs)
                npc.Memory.Reset();
            _settingsPanel?.RefreshSelection();
            _sessionLog?.LogEvent((int)_tick, "cpu-behavior", mode.ToString());
        }

        public void SetNpcDifficulty(CpuDifficulty difficulty)
        {
            _npcDifficulty = BotDifficultyProfile.Normalize(difficulty);
            foreach (var npc in _npcs)
            {
                npc.Memory.Difficulty = _npcDifficulty;
                npc.Memory.Reset();
            }
            _settingsPanel?.RefreshSelection();
            _sessionLog?.LogEvent((int)_tick, "cpu-difficulty", _npcDifficulty.ToString());
        }
        public float GetSelectedNpcDamage()
            => _npcs.Count > 0 ? _bridge.GetState(SelectedNpcId).DamagePercent : 0f;

        public void SetSelectedNpcDamage(float percent)
        {
            if (_npcs.Count == 0) return;
            // DamagePercent is 0-999; clamp+round so repeated +10 can't overflow the ushort.
            var state = _bridge.GetState(SelectedNpcId);
            state.DamagePercent = (ushort)Mathf.Clamp(Mathf.RoundToInt(percent), 0, 999);
            _bridge.InternalSim.SetState(SelectedNpcId, state);
            _hudManager?.Refresh();
            _sessionLog?.LogEvent((int)_tick, "set-damage",
                FormattableString.Invariant($"{SelectedNpcId}:{state.DamagePercent}"));
        }

        /// <summary>Zero damage for the player and every CPU without moving them.</summary>
        public void ResetTrainingDamage()
        {
            if (_bridge == null || MatchConfig.Mode != GameMode.Training) return;
            foreach (var entity in new Dictionary<ulong, CharacterState>(_bridge.GetAllStates()))
            {
                var state = entity.Value;
                state.DamagePercent = 0;
                _bridge.InternalSim.SetState(entity.Key, state);
            }
            _hudManager?.Refresh();
            _sessionLog?.LogEvent((int)_tick, "reset-damage");
        }

        /// <summary>Return every fighter to its spawn, preserving damage and CPU settings.</summary>
        public void ResetTrainingPositions()
        {
            if (_bridge == null || _playerEntry == null || MatchConfig.Mode != GameMode.Training) return;
            var states = new Dictionary<ulong, CharacterState>(_bridge.GetAllStates());
            // Remove first so paired grabs and active abilities are interrupted before any
            // fresh state is installed. Clear lingering projectiles from completed abilities.
            foreach (var entity in states)
                _bridge.InternalSim.RemoveEntity(entity.Key);
            _bridge.InternalSim.Resolver.Clear();
            RestoreTrainingEntity(PlayerEntityId, _arenaDef.SpawnPoints[0], _playerEntry,
                states[PlayerEntityId], _playerRenderer);
            foreach (var npc in _npcs)
            {
                RestoreTrainingEntity(npc.Id, npc.Spawn, _npcEntry, states[npc.Id], npc.Renderer);
                npc.Memory.Reset();
                npc.LastDeaths = states[npc.Id].Deaths;
            }
            ResetTrainingPresentation();
            _projectileVFX?.OnTick();
            _hudManager?.Refresh();
            _sessionLog?.LogEvent((int)_tick, "reset-positions");
        }

        private void RestoreTrainingEntity(ulong id, SpawnPoint spawn, MatchContentEntry entry,
            CharacterState previous, PlayerRenderer renderer)
        {
            var state = new CharacterState
            {
                PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw,
                JumpsLeft = entry.Definition.Movement.MaxJumps, AirDodgesLeft = 1,
                DamagePercent = previous.DamagePercent, Deaths = previous.Deaths,
            };
            _bridge.RegisterEntity(id, entry.Definition, state, entry.BakedAnimation);
            _bridge.SetRespawnPosition(id, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
            renderer?.ResetAnimationState();
            renderer?.ApplyServerState(_bridge.GetState(id), _bridge.GetActiveMove(id)?.Slot);
            _projectileVFX?.ClearHeldVisual(id);
        }

        public void ResetTrainingPresentation()
        {
            _inputController?.RequireReleaseBeforeHumanInput();
            _aimHandler?.ResetPresentation();
            _showCrosshair = false;
        }

        public void AddNpc()
        {
            if (_npcEntry == null || _bridge == null) return;
            var slot = new NpcSlot
            {
                Id = (ulong)_nextNpcId++,
                Rng = new System.Random(),
                Memory = new BotMemory(),
                Def = _npcEntry.Definition,
                Spawn = NextNpcSpawn(),
            };
            slot.Memory.Difficulty = CurrentNpcDifficulty;
            if (!SpawnNpcSlot(slot))
                return;
            _npcs.Add(slot);
            RebuildRosterVisuals();
            _settingsPanel?.RefreshRoster();
            _sessionLog?.LogEvent((int)_tick, "cpu-added",
                slot.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void DeleteSelectedNpc()
        {
            if (_npcs.Count <= 1) return;
            var slot = _npcs[_selectedNpcIndex];
            _bridge.InternalSim.RemoveEntity(slot.Id);
            if (slot.Renderer != null)
                Destroy(slot.Renderer.gameObject);
            _npcs.RemoveAt(_selectedNpcIndex);
            _selectedNpcIndex = Math.Clamp(_selectedNpcIndex, 0, _npcs.Count - 1);
            RebuildRosterVisuals();
            _settingsPanel?.RefreshRoster();
            _sessionLog?.LogEvent((int)_tick, "cpu-removed",
                slot.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        protected override void OnMatchStart()
        {
            // Only a fully initialized match may poll input or advance the simulation.
            enabled = false;
            string arenaName = string.IsNullOrEmpty(_arenaNameOverride) ? MatchConfig.ArenaName : _arenaNameOverride;
            Debug.Log($"[{GetType().Name}] Starting match: mode={MatchConfig.Mode} char={MatchConfig.PlayerClass} arena={arenaName}");
            // Baked arena is required (issue #77): hardcoded ArenaRegistry arenas carry
            // no collision data, so a missing .arena file used to make players fall
            // through the floor (Simulation grounded at KillHeight + 1).
            string? arenaPath = BakedContentPaths.ResolveArena(arenaName);
            if (arenaPath == null)
            {
                Debug.LogError($"[TrainingMatch] Baked arena '{arenaName}' not found (looked in StreamingAssets/arenas and repo data/arenas). " +
                               "Bake the arena or run scripts/build-release.sh. Aborting match start.");
                return;
            }
            var arenaOpt = ArenaBinaryFormat.LoadFromFile(arenaPath);
            if (arenaOpt is not ArenaDefinition arena)
            {
                Debug.LogError($"[TrainingMatch] Failed to parse baked arena: {arenaPath}");
                return;
            }
            Debug.Log($"[TrainingMatch] Loaded arena: {arenaPath} — {arena.CollisionTriangles?.Length ?? 0} tris, heightmap={arena.Heightmap.Width}x{arena.Heightmap.Height}");
            if (arena.SpawnPoints == null || arena.SpawnPoints.Length < 2)
            {
                Debug.LogError($"[TrainingMatch] Arena '{arenaName}' needs at least two authored spawn points for local play.");
                return;
            }
            SpawnStageVisual(arena);

            // Wire sim debug logging to Unity console
            SlopArena.Shared.Simulation.OnDebugLog = msg => Debug.Log(msg);
            _arenaDef = arena;
            bool solo = MatchConfig.Mode == GameMode.Solo;
            if (solo)
                _npcAiMode = NpcAiMode.Attack;
            _npcDifficulty = BotDifficultyProfile.Normalize(MatchConfig.SoloCpuDifficulty);
            _soloCountdownTicks = solo ? (ushort)300 : (ushort)0;
            _bridge = new LocalSimulationBridge(
                arena,
                solo ? new StockMatchRule((byte)MatchConfig.MaxStocks) : NoWinMatchRule.Instance,
                GroundSpeedTuning.Default);
            _combatFeedback.SetSimulation(_bridge);
            if (_projectileVFX == null)
                _projectileVFX = GetComponent<ProjectileVFXManager>() ?? gameObject.AddComponent<ProjectileVFXManager>();
            _projectileVFX.SetSimulation(_bridge.InternalSim);
            if (!SlopArena.Client.ClientSession.TryBuildLocalMatchCatalog(out var contentCatalog, out var contentFailure) || contentCatalog == null)
            {
                Debug.LogError($"[TrainingMatch] Content catalog build failed: {contentFailure}");
                return;
            }
            SlopArena.Client.ClientSession.InstallLocalMatchCatalog(contentCatalog);
            var playerClass = _playerClassOverride != CharacterClass.None ? _playerClassOverride : MatchConfig.PlayerClass;
            var playerEntry = contentCatalog.Resolve(playerClass);
            var npcClass = MatchConfig.SoloBotClass;
            var npcEntry = contentCatalog.Resolve(npcClass);
            if (playerEntry == null || npcEntry == null)
            {
                Debug.LogError($"[TrainingMatch] Missing catalog entry for player={playerClass} or NPC={npcClass}.");
                return;
            }
            var playerDef = playerEntry.Definition;
            _playerDef = playerDef;
            _playerEntry = playerEntry;
            var npcDef = npcEntry.Definition;
            _npcEntry = npcEntry;

            // Shared player renderer + HUD setup. The training NPC is not in
            // MatchConfig.Opponents (PvP-only roster), so hand it to the HUD
            // explicitly — otherwise its damage % has no overhead panel.
            if (!SetupPlayerRenderer(playerEntry, arena))
                return;
            _playerRenderer.RenderInterpolation = _renderInterpolation;

            // Player spawn
            var pSpawn = arena.SpawnPoints[0];
            _bridge.RegisterEntity(PlayerEntityId, playerDef, new CharacterState
            {
                PX = pSpawn.X, PY = pSpawn.Y, PZ = pSpawn.Z,
                FacingYaw = pSpawn.Yaw,
                JumpsLeft = playerDef.Movement.MaxJumps,
            }, playerEntry.BakedAnimation);
            _playerRenderer.transform.position = new Vector3(pSpawn.X, pSpawn.Y, pSpawn.Z);

            // Keep the scene-placed first NPC renderer (capture harness + Solo);
            // its position and facing come from the selected stage's second spawn.
            var first = new NpcSlot
            {
                Id = NpcEntityId,
                Renderer = _npcRenderer,
                Def = npcDef,
                Spawn = NextNpcSpawn(),
                Rng = new System.Random(),
            };
            first.Memory.Difficulty = CurrentNpcDifficulty;
            if (!SpawnNpcSlot(first))
                return;
            _npcs.Add(first);

            if (solo && MatchConfig.SoloP2Human)
                StartCouch();
            SetupHUD(playerDef, HudExtraPlayers());

            // Shared camera + aim setup
            SetupCamera();
            if (solo)
                _hudManager?.ShowSoloCountdown("READY", 2f);
            SetupAimHandler(playerDef);
            SetupLockIndicator(new[] { _playerRenderer }.Concat(_npcs.Select(n => n.Renderer)).ToArray(), arena);
            _lockIndicator = FindFirstObjectByType<TargetIndicator>();
            // Training controls remain visible on the HUD throughout practice.
            if (MatchConfig.Mode == GameMode.Training)
            {
                LoadMovementPrototype();
                var panel = gameObject.AddComponent<TrainingSettingsPanel>();
                _settingsPanel = panel;
                panel.Init(this, _hudManager.Document, _cameraMount);
            }
            StartSessionLog(arenaName);
            enabled = true;
        }

        private void StartSessionLog(string arenaName)
        {
            if (!ClientSettingsService.Instance.RecordSessionLog
                || MatchConfig.Mode is not (GameMode.Training or GameMode.Solo))
                return;
            var header = new SessionHeader
            {
                StartedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                Mode = MatchConfig.Mode.ToString(),
                Arena = arenaName,
            };
            header.Fighters.Add(PracticeSessionLog.Fighter(PlayerEntityId, "player", _playerEntry));
            foreach (var npc in _npcs)
                header.Fighters.Add(PracticeSessionLog.Fighter(npc.Id, "cpu", _npcEntry,
                    BotDifficultyProfile.DisplayName(CurrentNpcDifficulty), _npcAiMode.ToString()));
            PracticeSessionLog.DescribePresentation(header, _cameraMount,
                _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main);
            _sessionLog = PracticeSessionLog.TryStart(header, _playerDef);
        }

        private void FinishSessionLog(string reason)
        {
            _sessionLog?.Finish(reason);
            _sessionLog = null;
        }

        protected override void OnDestroy()
        {
            FinishSessionLog("left");
            if (_p2Map != null)
            {
                HumanInputActions.RestrictToDevices(null);
                // Disable only: P2's InputController may still clear its state on teardown.
                _p2Map.Disable();
            }
            base.OnDestroy();
        }

        /// <summary>
        /// P2 plays on the pad that joined on Fighter Select; P1 keeps the keyboard, mouse and every other pad.
        /// </summary>
        private void StartCouch()
        {
            var p2Pad = CouchPads.P2;
            if (p2Pad == null)
            {
                Debug.LogWarning("[Couch] P2's pad is not connected; the CPU plays P2.");
                return;
            }
            var p1Devices = new List<InputDevice>();
            if (Keyboard.current != null) p1Devices.Add(Keyboard.current);
            if (Mouse.current != null) p1Devices.Add(Mouse.current);
            foreach (var pad in Gamepad.all)
                if (pad != p2Pad) p1Devices.Add(pad);
            HumanInputActions.RestrictToDevices(p1Devices.ToArray());
            _p2Map = HumanInputActions.CreatePlayerMap(p2Pad);
            _p2Input = gameObject.AddComponent<InputController>();
            _p2Input.UsePlayerMap(_p2Map);
            if (_cameraMount != null)
                _cameraMount.Couch = CouchCamera.SideOnHigh;
            Debug.Log($"[Couch] P1 = {string.Join(", ", p1Devices.Select(d => d.displayName))}; P2 = {p2Pad.displayName}");
        }

        private InputState BuildP2Input(NpcSlot p2)
        {
            byte slot = _p2Input.ConsumePendingSlotPress();
            return _p2Input.BuildInputState(_cameraMount, p2.Renderer.transform.eulerAngles.y, slot,
                AimContext.None, canMove: null, targetEntityId: (byte)PlayerEntityId,
                groundSpeedModel: MovementModel, stickSnap: MovementStickSnap, aimFromFacing: true).input;
        }

        /// <summary>Rebuild the HUD and lock indicator after the NPC roster changes.</summary>
        private void RebuildRosterVisuals()
        {
            // Roster rebuild may destroy the currently marked NPC renderer.
            _aimHandler?.UpdateTargetPresentation(_bridge.GetState(PlayerEntityId), null, 0);
            SetupHUD(_playerDef, HudExtraPlayers());
            if (_lockIndicator != null)
                Destroy(_lockIndicator.gameObject);
            SetupLockIndicator(new[] { _playerRenderer }.Concat(_npcs.Select(n => n.Renderer)).ToArray(), _arenaDef);
            _lockIndicator = FindFirstObjectByType<TargetIndicator>();
        }

        private List<HUDManager.HudPlayer> HudExtraPlayers()
        {
            // CPU cards name the CPU, not its internal entity id (which starts at 100).
            var list = new List<HUDManager.HudPlayer>(_npcs.Count);
            for (int i = 0; i < _npcs.Count; i++)
                list.Add(new HUDManager.HudPlayer(_npcs[i].Id, i == 0 ? Couch ? "P2" : "CPU" : $"CPU {i + 1}",
                    _npcs[i].Def.Class, isLocal: false));
            return list;
        }

        private SpawnPoint NextNpcSpawn()
            => _arenaDef.SpawnPoints[1 + _npcs.Count % (_arenaDef.SpawnPoints.Length - 1)];

        private bool SpawnNpcSlot(NpcSlot slot)
        {
            if (slot.Renderer == null)
                slot.Renderer = new GameObject($"Npc{slot.Id}").AddComponent<PlayerRenderer>();
            if (!SetupRenderer(slot.Renderer, _npcEntry, _arenaDef, slot.Id, false))
                return false;
            slot.Renderer.RenderInterpolation = _renderInterpolation;
            slot.Renderer.GroundSpeedPrototypeActive = MovementModel != GroundSpeedModel.Current;
            var spawn = slot.Spawn;
            slot.Renderer.transform.position = new Vector3(spawn.X, spawn.Y, spawn.Z);
            _bridge.RegisterEntity(slot.Id, slot.Def, new CharacterState
            {
                PX = spawn.X, PY = spawn.Y, PZ = spawn.Z,
                FacingYaw = spawn.Yaw,
                JumpsLeft = slot.Def.Movement.MaxJumps,
            }, _npcEntry.BakedAnimation);
            _bridge.SetRespawnPosition(slot.Id, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
            return true;
        }

        private void Update()
        {
            // P1's pause key is on the shared map; P2's Start opens and closes the same menu.
            if (enabled && _p2Map != null && _pauseMenu != null && _p2Map.FindAction("Pause").WasPressedThisFrame())
                _pauseMenu.SetPaused(!IsPaused);
            // While paused the MatchPauseMenu owns Esc and input polling is skipped so
            // no buffered presses leak into the first frame after resume (issue #77).
            if (!enabled || IsPaused) return;

            _inputController.Poll();
            _p2Input?.Poll();

            if (_showHitboxes && _bridge != null)
            {
                DrawHitboxDebug();
                DrawHurtboxDebug();
            }
        }

        protected override void OnMatchFixedUpdate()
        {
            if (!enabled || _bridge == null || _playerRenderer == null) return;

            if (MatchConfig.Mode == GameMode.Solo && _soloCountdownTicks > 0)
            {
                _inputController.ClearPendingFrameState();
                _p2Input?.ClearPendingFrameState();
                _soloCountdownTicks--;
                if (_soloCountdownTicks == 180) _hudManager?.ShowSoloCountdown("3", 1f);
                else if (_soloCountdownTicks == 120) _hudManager?.ShowSoloCountdown("2", 1f);
                else if (_soloCountdownTicks == 60) _hudManager?.ShowSoloCountdown("1", 1f);
                else if (_soloCountdownTicks == 0) _hudManager?.ShowSoloCountdown("SLOP IT OUT", 0.72f);
                return;
            }

            // Poll done in Update() — keep FixedUpdate clean
            byte slot = _inputController.ConsumePendingSlotPress();

            var aimCtx = _aimHandler != null && !Couch
                ? _aimHandler.Evaluate(_bridge.GetState(PlayerEntityId), slot, _playerDef, _inputController, _bridge.GetActiveMove(PlayerEntityId)?.Slot)
                : AimContext.None;
            _showCrosshair = _aimHandler?.ShowCrosshair ?? false;

            // ── Build input ──
            byte targetEntityId = PickScreenTarget(
                _npcs.Select(n => n.Renderer).ToArray(),
                _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main);

            var (input, _, _) = _inputController.BuildInputState(
                _cameraMount,
                _playerRenderer.transform.eulerAngles.y,
                pendingSlotPress: slot,
                aimCtx: aimCtx,
                canMove: null,
                targetEntityId: targetEntityId,
                groundSpeedModel: MovementModel,
                stickSnap: MovementStickSnap,
                aimFromFacing: Couch);
            if (_captureInputOverride.HasValue)
                input = _captureInputOverride.Value;

            // NPC AI — one input per slot
            var playerState = _bridge.GetState(PlayerEntityId);
            var tickInputs = new Dictionary<ulong, InputState>(_npcs.Count + 1)
            {
                { PlayerEntityId, input }
            };
            foreach (var npc in _npcs)
            {
                tickInputs[npc.Id] = Couch && npc.Id == NpcEntityId
                    ? BuildP2Input(npc)
                    : BuildNpcInput(_bridge.GetState(npc.Id), playerState, npc);
            }

            // Tick
            _sessionLog?.BeforeTick(_bridge.InternalSim, (int)_tick, tickInputs);
            _bridge.Tick(tickInputs);
            _sessionLog?.AfterTick(_bridge.InternalSim, (int)_tick, tickInputs, _cameraMount, _mainCamera);

            // Feed only authoritative resolver outcomes back to runner-owned bot memory.
            // Combat events are delayed inside BotMemory; no one-tick hit flag is exposed
            // to policy decisions.
            foreach (var npc in _npcs)
            {
                var npcState = _bridge.GetState(npc.Id);
                if (npcState.Deaths > npc.LastDeaths)
                {
                    npc.LastDeaths = npcState.Deaths;
                    npc.Memory.Reset();
                    continue;
                }

                foreach (var hit in _bridge.LastTickHits)
                {
                    if (hit.Blocked || hit.OwnerEntityId != npc.Id
                        || hit.TargetEntityId != PlayerEntityId)
                        continue;
                    var playerAfterHit = _bridge.GetState(PlayerEntityId);
                    npc.Memory.RecordOpponentHit(hit.AttackSlot, !playerAfterHit.IsGrounded,
                        playerAfterHit, playerAfterHit.HitstunTicks, hit.HitstopTicks);
                }
            }

            _projectileVFX?.OnTick();
            _combatFeedback.OnTick();
            _hudManager?.Refresh();

            _tick++;
            if (_tick % 120 == 1)
            {
                var ps3 = _bridge.GetState(PlayerEntityId);
                Debug.Log($"[Training] tick={_tick} pos=({ps3.PX:F1},{ps3.PY:F2},{ps3.PZ:F1}) vy={ps3.VY:F2} grounded={ps3.IsGrounded}");
            }

#if UNITY_EDITOR
            var hitState = _bridge.GetState(_npcs.Count > 0 ? _npcs[0].Id : NpcEntityId);
            if (hitState.HitstunTicks > 0)
                Debug.Log($"[Combat] NPC hit! damage={hitState.DamagePercent:F1} hitstun={hitState.HitstunTicks}");

            // One line per hit: the launch the sim actually applied. Compare KV mag and
            // hitstun with the report's trajectory rows (same %) to verify game parity.
            if (hitState.HitstunTicks > 0 && _lastContractDamage != hitState.DamagePercent)
            {
                _lastContractDamage = hitState.DamagePercent;
                float kvMag = Mathf.Sqrt(hitState.KVX * hitState.KVX + hitState.KVY * hitState.KVY + hitState.KVZ * hitState.KVZ);
                Debug.Log($"[Launch] applied KV={kvMag:F2} hitstun={hitState.HitstunTicks} damage={hitState.DamagePercent:F1}");
            }
#endif

            // Apply states
            _playerRenderer.ApplyServerState(_bridge.GetState(PlayerEntityId), _bridge.GetActiveMove(PlayerEntityId)?.Slot);
            foreach (var npc in _npcs)
            {
                if (npc.Renderer != null)
                    npc.Renderer.ApplyServerState(_bridge.GetState(npc.Id), _bridge.GetActiveMove(npc.Id)?.Slot);
            }
            UpdateSwordTrailsFromResolver(_bridge.InternalSim.Resolver);
            PresentTimelineEvents();

            var targetState = _bridge.GetState(PlayerEntityId);
            PlayerRenderer targetRenderer = null;
            if (targetState.TargetEntityId != 0)
            {
                foreach (var npc in _npcs)
                    if (npc.Id == targetState.TargetEntityId)
                        targetRenderer = npc.Renderer;
            }
            ushort targetDamagePercent = targetRenderer != null
                ? _bridge.GetState(targetState.TargetEntityId).DamagePercent
                : (ushort)0;
            _aimHandler?.UpdateTargetPresentation(targetState, targetRenderer, targetDamagePercent);
            if (MatchConfig.Mode == GameMode.Solo)
            {
                var soloPlayer = _bridge.GetState(PlayerEntityId);
                var soloNpc = _bridge.GetState(_npcs.Count > 0 ? _npcs[0].Id : NpcEntityId);
                if (soloPlayer.Deaths > _lastSoloPlayerDeaths)
                {
                    _lastSoloPlayerDeaths = soloPlayer.Deaths;
                    _hudManager?.ShowStockToast(PlayerEntityId, Couch ? "P1 · −1 LIFE" : "YOU · −1 LIFE");
                }
                if (soloNpc.Deaths > _lastSoloNpcDeaths)
                {
                    _lastSoloNpcDeaths = soloNpc.Deaths;
                    _hudManager?.ShowStockToast(NpcEntityId, Couch ? "P2 · −1 LIFE" : "CPU · −1 LIFE");
                }
            }

            UpdateLockCamera();
            if (MatchConfig.Mode == GameMode.Solo && !_soloResultsShown)
            {
                var outcome = new StockMatchRule((byte)MatchConfig.MaxStocks)
                    .Evaluate(_bridge.GetAllStates());
                if (outcome.IsEnded)
                {
                    _aimHandler?.ResetPresentation();
                    BuildSoloResults(outcome);
                }
            }
            CaptureSimulationTickCompleted?.Invoke(_tick);
        }

        private void BuildSoloResults(MatchOutcome outcome)
        {
            _soloResultsShown = true;
            FinishSessionLog("solo-ended");
            var player = _bridge.GetState(PlayerEntityId);
            var npc = _bridge.GetState(NpcEntityId);
            bool playerFirst = !outcome.IsSharedVictory && outcome.WinnerEntityId == PlayerEntityId;
            var entries = new List<ClientSession.ResultEntry>
            {
                new(
                    PlayerEntityId,
                    playerFirst || outcome.IsSharedVictory ? 1 : 2,
                    kos: 0,
                    falls: player.Deaths,
                    name: Couch ? "P1" : "YOU",
                    className: MatchConfig.PlayerClass.ToString(),
                    stocksRemaining: MatchConfig.MaxStocks - player.Deaths,
                    damagePercent: player.DamagePercent),
                new(
                    NpcEntityId,
                    !playerFirst && !outcome.IsSharedVictory ? 1 : 2,
                    kos: 0,
                    falls: npc.Deaths,
                    name: Couch ? "P2" : "CPU",
                    className: MatchConfig.SoloBotClass.ToString(),
                    stocksRemaining: MatchConfig.MaxStocks - npc.Deaths,
                    damagePercent: npc.DamagePercent),
            };

            entries.Sort((a, b) => a.Placement.CompareTo(b.Placement));
            var results = new ClientSession.MatchResultsData(
                outcome.IsSharedVictory,
                MatchConfig.ArenaName,
                0,
                entries);
            ClientSession.SetLocalMatchResults(results);
            _hudManager?.ShowSoloEnding(playerFirst, results.SharedVictory, MatchConfig.PlayerClass, MatchConfig.SoloBotClass, Couch);
            StartCoroutine(LoadSoloResults());
        }

        private System.Collections.IEnumerator LoadSoloResults()
        {
            yield return new WaitForSecondsRealtime(2f);
            FrontendController.Show(FrontendPage.Results);
        }

        /// <summary>
        /// Follow the authoritative locked NPC while keeping the camera anchored
        /// on the player. No camera assistance runs merely because a target exists.
        /// </summary>
        private void UpdateLockCamera()
        {
            if (_cameraMount == null) return;
            var local = _bridge.GetState(PlayerEntityId);
            if (!local.LockOn)
            {
                _cameraMount.ClearLockFocus(_playerRenderer.transform);
                return;
            }
            foreach (var npc in _npcs)
            {
                if (npc.Renderer != null && npc.Id == local.TargetEntityId)
                {
                    _cameraMount.SetLockFocus(_playerRenderer.transform, npc.Renderer.transform.position);
                    return;
                }
            }
            _cameraMount.ClearLockFocus(_playerRenderer.transform);
        }

        private static bool IsThreatening(in CharacterState state)
        {
            return state.State is ActionState.Attacking or ActionState.Aiming or ActionState.Warping
                || state.AnimLockTicks > 0
                || state.LandingLagTicks > 0;
        }

        /// <summary>
        /// Builds a synthetic InputState for the NPC dummy.
        /// Computes world-space direction toward player.
        /// Server auto-sets FacingYaw from movement velocity.
        /// </summary>
        private InputState BuildNpcInput(CharacterState npcState, CharacterState playerState, NpcSlot slot)
        {
            return _npcAiMode switch
            {
                NpcAiMode.Idle => BuildIdleInput(),
                NpcAiMode.Attack => BuildPolicyInput(npcState, playerState, slot, CpuBehavior.Attack),
                NpcAiMode.Shield => BuildPolicyInput(npcState, playerState, slot, CpuBehavior.Shield),
                NpcAiMode.Avoid => BuildPolicyInput(npcState, playerState, slot, CpuBehavior.Avoid),
                _ => BuildIdleInput(),
            };
        }

        /// <summary>
        /// Drive the NPC with the same deterministic heuristic policy the self-play telemetry
        /// uses (issue #148). Returns an <see cref="InputState"/> like the legacy AI, so all the
        /// existing input-injection / rendering / respawn plumbing applies unchanged. Seeded
        /// randomly per match; idle until the match has initialized the NPC def.
        /// </summary>
        private InputState BuildPolicyInput(CharacterState npcState, CharacterState playerState,
            NpcSlot slot, CpuBehavior behavior)
        {
            if (slot.Def == null) return BuildIdleInput();
            slot.Rng ??= new System.Random();
            return _npcPolicy.Decide(npcState, playerState, slot.Def, slot.Rng, slot.Memory,
                _arenaDef, _npcEntry.BakedAnimation, behavior, _bridge.GetActiveMove(slot.Id)?.Slot);
        }

        private static InputState BuildIdleInput()
        {
            return new InputState
            {
                MoveX = 0f,
                MoveY = 0f,
                ActiveSlot = 0,
                Jump = false,
            };
        }


        private void OnDrawGizmos()
        {
            if (_bridge == null) return;
            DrawHitboxGizmos();
            DrawHurtboxGizmos();
        }

        private void DrawHurtboxGizmos()
        {
            var entities = _bridge.InternalSim.GetLastEntityData();
            foreach (var ed in entities)
            {
                // Color by entity: player=green, NPC=red, others=blue
                Gizmos.color = ed.Id switch
                {
                    var id when id == PlayerEntityId => new Color(0f, 1f, 0.3f, 0.5f),
                    NpcEntityId    => new Color(1f, 0.3f, 0.3f, 0.5f),
                    _              => new Color(0.3f, 0.3f, 1f, 0.5f),
                };
                var center = new Vector3(ed.PosX, ed.PosY, ed.PosZ);
                if (ed.Shape == HitboxShape.Sphere)
                {
                    Gizmos.DrawWireSphere(center, ed.Radius);
                }
                else
                {
                    DrawWireCapsule(center, new Vector3(ed.EndX, ed.EndY, ed.EndZ), ed.Radius, Gizmos.DrawLine);
                }
            }
        }

        // Hitboxes exactly as the latest tick tested them, including a window's last tick.
        private void DrawHitboxGizmos()
        {
            var hitboxes = _bridge.InternalSim.Resolver.LastTickCheckedHitboxes;
            foreach (var hb in hitboxes)
            {
                Gizmos.color = new Color(0.6f, 0.3f, 0.05f, 0.6f);
                DrawSweepLines(in hb, Gizmos.DrawLine);
                Gizmos.color = new Color(1f, 0.3f, 0f, 0.6f);
                var center = new Vector3(hb.X, hb.Y, hb.Z);
                if (hb.Shape == HitboxShape.Sphere || (hb.X == hb.EndX && hb.Y == hb.EndY && hb.Z == hb.EndZ))
                {
                    Gizmos.DrawWireSphere(center, hb.Radius);
                }
                else
                {
                    DrawWireCapsule(center, new Vector3(hb.EndX, hb.EndY, hb.EndZ), hb.Radius, Gizmos.DrawLine);
                }
            }
        }

        private void DrawHitboxDebug()
        {
            var hitboxes = _bridge.InternalSim.Resolver.LastTickCheckedHitboxes;
            Color color = new Color(1f, 0.3f, 0f, 0.6f);
            Color sweepColor = new Color(0.6f, 0.3f, 0.05f, 0.6f);
            foreach (var hb in hitboxes)
            {
                DrawSweepLines(in hb, (p0, p1) => Debug.DrawLine(p0, p1, sweepColor));
                var center = new Vector3(hb.X, hb.Y, hb.Z);
                if (hb.Shape == HitboxShape.Sphere || (hb.X == hb.EndX && hb.Y == hb.EndY && hb.Z == hb.EndZ))
                {
                    DebugDrawWireSphere(center, hb.Radius, color);
                }
                else
                {
                    DrawWireCapsule(center, new Vector3(hb.EndX, hb.EndY, hb.EndZ), hb.Radius, (p0, p1) => Debug.DrawLine(p0, p1, color));
                }
            }
        }

        /// <summary>
        /// The path a swept bone hitbox tested since the previous tick: each swept pose's
        /// axis joined by its start and end paths up to the current pose.
        /// </summary>
        private static void DrawSweepLines(in Hitbox hb, Action<Vector3, Vector3> drawLine)
        {
            if (hb.Sweep == null) return;
            for (int p = 0; p < hb.Sweep.Length; p++)
            {
                var pose = hb.Sweep[p];
                var start = new Vector3(pose.X, pose.Y, pose.Z);
                var end = new Vector3(pose.EndX, pose.EndY, pose.EndZ);
                var nextStart = p + 1 < hb.Sweep.Length
                    ? new Vector3(hb.Sweep[p + 1].X, hb.Sweep[p + 1].Y, hb.Sweep[p + 1].Z)
                    : new Vector3(hb.X, hb.Y, hb.Z);
                var nextEnd = p + 1 < hb.Sweep.Length
                    ? new Vector3(hb.Sweep[p + 1].EndX, hb.Sweep[p + 1].EndY, hb.Sweep[p + 1].EndZ)
                    : new Vector3(hb.EndX, hb.EndY, hb.EndZ);
                drawLine(start, end);
                drawLine(start, nextStart);
                drawLine(end, nextEnd);
            }
        }

        private void DrawHurtboxDebug()
        {
            var entities = _bridge.InternalSim.GetLastEntityData();
            foreach (var ed in entities)
            {
                Color color = ed.Id switch
                {
                    var id when id == PlayerEntityId => new Color(0f, 1f, 0.3f, 0.5f),
                    NpcEntityId    => new Color(1f, 0.3f, 0.3f, 0.5f),
                    _              => new Color(0.3f, 0.3f, 1f, 0.5f),
                };
                var center = new Vector3(ed.PosX, ed.PosY, ed.PosZ);
                if (ed.Shape == HitboxShape.Sphere)
                {
                    DebugDrawWireSphere(center, ed.Radius, color);
                }
                else
                {
                    DrawWireCapsule(center, new Vector3(ed.EndX, ed.EndY, ed.EndZ), ed.Radius, (p0, p1) => Debug.DrawLine(p0, p1, color));
                }
            }
        }

        /// <summary>
        /// Draws the true wireframe of a capsule — segment (a→b) swept with radius:
        /// cap circles at both ends, 4 longitudinal cylinder lines, and dome arcs for
        /// the rounded caps. Matches the swept-sphere volume the collision tests use
        /// (SpellResolver.CapsuleCollision), unlike a naive 2-spheres + line.
        /// </summary>
        private static void DrawWireCapsule(Vector3 a, Vector3 b, float radius, Action<Vector3, Vector3> drawLine)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-4f)
            {
                // Degenerate (start ≈ end): single cross-section circle. Defensive only —
                // callers route equal endpoints to the sphere branch.
                const int seg = 16;
                Vector3 prev = default;
                for (int i = 0; i <= seg; i++)
                {
                    float angle = i * (Mathf.PI * 2f / seg);
                    Vector3 p = a + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (i > 0) drawLine(prev, p);
                    prev = p;
                }
                return;
            }

            Vector3 dir = axis / len;
            Vector3 u = Vector3.Cross(dir, Vector3.up);
            if (u.sqrMagnitude < 1e-6f)
                u = Vector3.Cross(dir, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(dir, u);

            const int segments = 16;
            const int planes = 4;

            // ── Cap circles (cylinder cross-section) at both endpoints ──
            Vector3 prevA = default, prevB = default;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / segments);
                Vector3 radial = (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius;
                Vector3 pa = a + radial;
                Vector3 pb = b + radial;
                if (i > 0)
                {
                    drawLine(prevA, pa);
                    drawLine(prevB, pb);
                }
                prevA = pa;
                prevB = pb;
            }

            // ── Longitudinal lines: the cylinder silhouette ──
            for (int p = 0; p < planes; p++)
            {
                float angle = p * (Mathf.PI * 2f / planes);
                Vector3 radial = (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius;
                drawLine(a + radial, b + radial);
            }

            // ── Dome arcs: semicircle from one cap-circle point to the opposite one,
            // through the pole (a − dir·r / b + dir·r) — the rounded cap surface ──
            for (int p = 0; p < planes; p++)
            {
                float angle = p * (Mathf.PI * 2f / planes);
                Vector3 radial = (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius;

                Vector3 prev = a + radial;
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f * Mathf.PI;
                    Vector3 point = a + radial * Mathf.Cos(t) - dir * radius * Mathf.Sin(t);
                    drawLine(prev, point);
                    prev = point;
                }

                prev = b + radial;
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f * Mathf.PI;
                    Vector3 point = b + radial * Mathf.Cos(t) + dir * radius * Mathf.Sin(t);
                    drawLine(prev, point);
                    prev = point;
                }
            }
        }

        private static void DebugDrawWireSphere(Vector3 center, float radius, Color color)
        {
            const int segments = 16;
            for (int ring = 0; ring < 3; ring++)
            {
                Vector3 prev = default;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * (Mathf.PI * 2f / segments);
                    Vector3 p = center;
                    if (ring == 0)
                    {
                        p.x += Mathf.Cos(angle) * radius;
                        p.y += Mathf.Sin(angle) * radius;
                    }
                    else if (ring == 1)
                    {
                        p.x += Mathf.Cos(angle) * radius;
                        p.z += Mathf.Sin(angle) * radius;
                    }
                    else
                    {
                        p.y += Mathf.Cos(angle) * radius;
                        p.z += Mathf.Sin(angle) * radius;
                    }
                    if (i > 0)
                        Debug.DrawLine(prev, p, color);
                    prev = p;
                }
            }
        }
    }
}
