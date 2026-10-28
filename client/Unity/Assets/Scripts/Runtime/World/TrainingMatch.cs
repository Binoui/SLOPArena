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
        private TrainingSettingsPanel _settingsPanel;
        // Opt-in local measurement of this match (Settings → Gameplay); presentation-only.
        private PracticeSessionLog? _sessionLog;

        private sealed class NpcSlot
        {
            public ulong Id;
            public PlayerRenderer Renderer;
            public CharacterDefinition Def;
            public MatchContentEntry Entry = null!;
            public int PlayerNumber;
            public ulong TargetId;
            public ushort LastToastDeaths;
            public InputController? Input;
            [NonSerialized] public InputActionMap? Map;
            public BotMemory Memory = new();
            public System.Random Rng;
            public SpawnPoint Spawn;
            public byte LastDeaths;
        }
        private bool Couch => _npcs.Any(npc => npc.Input != null);
        private ushort _soloCountdownTicks;
        private ushort _lastSoloPlayerDeaths;
        private readonly Dictionary<ulong, uint> _eliminatedAt = new();
        private bool _soloResultsShown;
        private MankiMayhemPresentation _mayhem;
        private bool Mayhem => MatchConfig.IsLocalMayhem;

        // Presentation-only input seam used by VisualBaselineCaptureController and
        // bounded Editor sequences. The normal training path leaves this null.
        private InputState? _captureInputOverride;
        private object _captureInputSequenceOwner;

        public event Action<uint> CaptureSimulationTickCompleted;
        public uint CaptureTick => _tick;
#if UNITY_EDITOR
        public Func<uint, InputState>? CaptureTargetInput { get; set; }
        public InputState CaptureLastTargetInput { get; private set; }
#endif
        public ServerSimulation CaptureSimulation => _bridge?.InternalSim;
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



        public int NpcCount => _npcs.Count;
        public int SelectedNpcIndex => _selectedNpcIndex;
        public int SelectedNpcPlayerNumber => _npcs.Count > 0 ? _npcs[_selectedNpcIndex].PlayerNumber : 2;
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
                RestoreTrainingEntity(npc.Id, npc.Spawn, npc.Entry, states[npc.Id], npc.Renderer);
                npc.Memory.Reset();
                npc.LastDeaths = states[npc.Id].Deaths;
            }
            ResetTrainingPresentation();
            _mayhem?.Host.Present(_bridge.InternalSim);
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
            if (MatchConfig.Mode != GameMode.Training || _bridge == null) return;
            var entry = _npcs.FirstOrDefault()?.Entry;
            if (entry == null) return;
            int playerNumber = 2;
            while (_npcs.Any(npc => npc.PlayerNumber == playerNumber)) playerNumber++;
            var slot = new NpcSlot
            {
                Id = (ulong)_nextNpcId++,
                Rng = new System.Random(),
                Memory = new BotMemory(),
                Entry = entry,
                Def = entry.Definition,
                PlayerNumber = playerNumber,
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
            ForgetFighter(slot.Id);
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
                _npcAiMode = Mayhem ? NpcAiMode.Avoid : NpcAiMode.Attack;
            _npcDifficulty = BotDifficultyProfile.Normalize(MatchConfig.Seat(2).Difficulty);
            _soloCountdownTicks = solo && !Mayhem ? (ushort)300 : (ushort)0;
            _bridge = new LocalSimulationBridge(
                arena,
                Mayhem ? new MankiMayhemMatchRule(FriendlyFireMode.Shoves)
                    : solo ? new StockMatchRule((byte)MatchConfig.MaxStocks) : NoWinMatchRule.Instance);
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
            if (playerEntry == null)
            {
                Debug.LogError($"[TrainingMatch] Missing catalog entry for player={playerClass}.");
                return;
            }
            if (solo && MatchConfig.SoloOpponentCount == 0)
            {
                Debug.LogError("[TrainingMatch] Solo needs at least one opponent.");
                return;
            }
            var playerDef = playerEntry.Definition;
            _playerDef = playerDef;
            _playerEntry = playerEntry;

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

            for (int player = 2; player <= (solo ? 4 : 2); player++)
            {
                var seat = MatchConfig.Seat(player);
                if (solo && seat.Kind == SeatKind.Open) continue;
                var entry = contentCatalog.Resolve(seat.Fighter);
                if (entry == null)
                {
                    Debug.LogError($"[TrainingMatch] Missing catalog entry for P{player}={seat.Fighter}.");
                    return;
                }
                var npc = new NpcSlot
                {
                    Id = NpcEntityId + (ulong)(player - 2),
                    Renderer = _npcs.Count == 0 ? _npcRenderer : null!,
                    Entry = entry,
                    Def = entry.Definition,
                    PlayerNumber = player,
                    Spawn = NextNpcSpawn(),
                    Rng = new System.Random(),
                };
                npc.Memory.Difficulty = BotDifficultyProfile.Normalize(seat.Difficulty);
                if (!SpawnNpcSlot(npc)) return;
                _npcs.Add(npc);
            }
            if (solo) StartCouch();
            SetupHUD(playerDef, HudExtraPlayers());

            // Shared camera + aim setup
            SetupCamera();
            if (Couch && _cameraMount != null) _cameraMount.Couch = CouchCamera.SideOnHigh;
            if (solo && !Mayhem)
                _hudManager?.ShowSoloCountdown("READY", 2f);
            SetupAimHandler(playerDef);
            // Training controls remain visible on the HUD throughout practice.
            if (MatchConfig.Mode == GameMode.Training)
            {
                var panel = gameObject.AddComponent<TrainingSettingsPanel>();
                _settingsPanel = panel;
                panel.Init(this, _hudManager.Document, _cameraMount);
            }
            if (Mayhem)
            {
                _bridge.InternalSim.BeginMankiMayhem(MatchConfig.MayhemSeed);
                var renderers = _npcs.ToDictionary(n => n.Id, n => n.Renderer);
                renderers.Add(PlayerEntityId, _playerRenderer);
                var players = HudExtraPlayers().ToList();
                players.Insert(0, new HUDManager.HudPlayer(PlayerEntityId, "P1", playerClass, true, playerNumber: 1));
                _mayhem = new MankiMayhemPresentation(transform, _bridge.InternalSim, arena.Arcade.Mayhem,
                    contentCatalog.Resolve(CharacterClass.Manki), renderers, players, _hudManager, _projectileVFX,
                    _cameraMount, () => _cameraMount.RenderCamera);
                RegisterHostPresentation(ServerSimulation.MayhemHostEntityId, _mayhem.Host.Root, _mayhem.Host.Catalog);
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
                header.Fighters.Add(PracticeSessionLog.Fighter(npc.Id, npc.Input != null ? "player" : "cpu", npc.Entry,
                    npc.Input != null ? "" : BotDifficultyProfile.DisplayName(npc.Memory.Difficulty), _npcAiMode.ToString()));
            PracticeSessionLog.DescribePresentation(header, _cameraMount,
                _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main);
            _sessionLog = PracticeSessionLog.TryStart(header, _playerDef);
            foreach (var npc in _npcs)
                _sessionLog?.LogEvent(0, "seat", $"P{npc.PlayerNumber}:{npc.Id}:{npc.Def.Class}:{(npc.Input != null ? "local" : npc.Memory.Difficulty.ToString())}");
        }

        private void FinishSessionLog(string reason)
        {
            _sessionLog?.Finish(reason);
            _sessionLog = null;
        }

        protected override void OnDestroy()
        {
            _mayhem?.Dispose();
            FinishSessionLog("left");
            if (Couch) HumanInputActions.RestrictToDevices(null);
            foreach (var npc in _npcs) npc.Map?.Disable();
            base.OnDestroy();
        }

        private void StartCouch()
        {
            var usedPads = new HashSet<InputDevice>();
            foreach (var npc in _npcs)
            {
                if (MatchConfig.Seat(npc.PlayerNumber).Kind != SeatKind.LocalPlayer) continue;
                var pad = CouchPads.ForSeat(npc.PlayerNumber);
                if (pad == null)
                {
                    Debug.LogWarning($"[Couch] P{npc.PlayerNumber}'s pad is disconnected; CPU takes over.");
                    continue;
                }
                usedPads.Add(pad);
                npc.Map = HumanInputActions.CreatePlayerMap(pad);
                npc.Input = gameObject.AddComponent<InputController>();
                npc.Input.UsePlayerMap(npc.Map);
                Debug.Log($"[Couch] P{npc.PlayerNumber} = {pad.displayName}");
            }
            if (usedPads.Count == 0) return;
            var p1Devices = new List<InputDevice>();
            if (Keyboard.current != null) p1Devices.Add(Keyboard.current);
            if (Mouse.current != null) p1Devices.Add(Mouse.current);
            foreach (var pad in Gamepad.all)
                if (!usedPads.Contains(pad)) p1Devices.Add(pad);
            HumanInputActions.RestrictToDevices(p1Devices.ToArray());
        }

        private InputState BuildLocalInput(NpcSlot npc)
        {
            var input = npc.Input!;
            byte slot = input.ConsumePendingSlotPress();
            return input.BuildInputState(_cameraMount, npc.Renderer.transform.eulerAngles.y, slot,
                AimContext.None, canMove: null, targetEntityId: (byte)npc.TargetId,
                aimFromFacing: true).input;
        }

        /// <summary>Rebuild the HUD and lock indicator after the NPC roster changes.</summary>
        private void RebuildRosterVisuals()
        {
            // Roster rebuild may destroy the currently marked NPC renderer.
            _aimHandler?.UpdateTargetPresentation(_bridge.GetState(PlayerEntityId), null, 0);
            SetupHUD(_playerDef, HudExtraPlayers());
            RebuildFighterIndicators();
        }

        private List<HUDManager.HudPlayer> HudExtraPlayers()
        {
            var list = new List<HUDManager.HudPlayer>(_npcs.Count);
            for (int i = 0; i < _npcs.Count; i++)
                list.Add(new HUDManager.HudPlayer(_npcs[i].Id, $"P{_npcs[i].PlayerNumber}",
                    _npcs[i].Def.Class, isLocal: false, playerNumber: _npcs[i].PlayerNumber));
            return list;
        }

        private SpawnPoint NextNpcSpawn()
            => _arenaDef.SpawnPoints[1 + _npcs.Count % (_arenaDef.SpawnPoints.Length - 1)];

        private bool SpawnNpcSlot(NpcSlot slot)
        {
            if (slot.Renderer == null)
                slot.Renderer = new GameObject($"Npc{slot.Id}").AddComponent<PlayerRenderer>();
            if (!SetupRenderer(slot.Renderer, slot.Entry, _arenaDef, slot.Id, false))
                return false;
            slot.Renderer.PlayerNumber = slot.PlayerNumber;
            slot.Renderer.RenderInterpolation = _renderInterpolation;
            var spawn = slot.Spawn;
            slot.Renderer.transform.position = new Vector3(spawn.X, spawn.Y, spawn.Z);
            _bridge.RegisterEntity(slot.Id, slot.Def, new CharacterState
            {
                PX = spawn.X, PY = spawn.Y, PZ = spawn.Z,
                FacingYaw = spawn.Yaw,
                JumpsLeft = slot.Def.Movement.MaxJumps,
            }, slot.Entry.BakedAnimation);
            _bridge.SetRespawnPosition(slot.Id, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
            return true;
        }

        private void Update()
        {
            // P1's pause key is on the shared map; P2's Start opens and closes the same menu.
            if (enabled && _pauseMenu != null && _npcs.Any(npc => npc.Map != null && npc.Map.FindAction("Pause").WasPressedThisFrame()))
                _pauseMenu.SetPaused(!IsPaused);
            // While paused the MatchPauseMenu owns Esc and input polling is skipped so
            // no buffered presses leak into the first frame after resume (issue #77).
            if (!enabled || IsPaused) return;

            _inputController.Poll();
            foreach (var npc in _npcs) npc.Input?.Poll();

            if (_showHitboxes && _bridge != null)
            {
                DrawHitboxDebug();
                DrawHurtboxDebug();
            }
        }

        protected override void OnMatchFixedUpdate()
        {
            if (!enabled || _bridge == null || _playerRenderer == null || _soloResultsShown) return;

            if (MatchConfig.Mode == GameMode.Solo && _soloCountdownTicks > 0)
            {
                _inputController.ClearPendingFrameState();
                foreach (var npc in _npcs) npc.Input?.ClearPendingFrameState();
                _soloCountdownTicks--;
                if (_soloCountdownTicks == 180) _hudManager?.ShowSoloCountdown("3", 1f);
                else if (_soloCountdownTicks == 120) _hudManager?.ShowSoloCountdown("2", 1f);
                else if (_soloCountdownTicks == 60) _hudManager?.ShowSoloCountdown("1", 1f);
                else if (_soloCountdownTicks == 0) _hudManager?.ShowSoloCountdown("SLOP IT OUT", 0.72f);
                return;
            }

            // Poll done in Update() — keep FixedUpdate clean
            byte slot = _inputController.ConsumePendingSlotPress();

            var aimCtx = _aimHandler != null && !Couch && !Mayhem
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
                aimFromFacing: Couch || Mayhem);
            if (_captureInputOverride.HasValue)
                input = _captureInputOverride.Value;

            // NPC AI — one input per slot
            var tickInputs = new Dictionary<ulong, InputState>(_npcs.Count + 1)
            {
                { PlayerEntityId, input }
            };
            foreach (var npc in _npcs)
            {
                var state = _bridge.GetState(npc.Id);
                if (_bridge.InternalSim.IsEntityInactive(npc.Id))
                {
                    tickInputs[npc.Id] = default;
                    continue;
                }
                var opponent = SelectOpponent(npc, state);
                tickInputs[npc.Id] = npc.Input != null ? BuildLocalInput(npc) : BuildNpcInput(state, opponent, npc);
#if UNITY_EDITOR
                if (MatchConfig.Mode == GameMode.Training && npc.Id == SelectedNpcId && CaptureTargetInput != null)
                    tickInputs[npc.Id] = CaptureTargetInput(_tick);
                if (npc.Id == SelectedNpcId) CaptureLastTargetInput = tickInputs[npc.Id];
#endif
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
                        || hit.TargetEntityId != npc.TargetId)
                        continue;
                    var playerAfterHit = _bridge.GetState(npc.TargetId);
                    npc.Memory.RecordOpponentHit(hit.AttackSlot, !playerAfterHit.IsGrounded,
                        playerAfterHit, playerAfterHit.HitstunTicks, hit.HitstopTicks);
                }
            }

            _mayhem?.Host.Present(_bridge.InternalSim);
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
            _mayhem?.AfterTick();
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
            if (MatchConfig.Mode == GameMode.Solo && !Mayhem)
            {
                var soloPlayer = _bridge.GetState(PlayerEntityId);
                if (soloPlayer.Deaths > _lastSoloPlayerDeaths)
                {
                    _lastSoloPlayerDeaths = soloPlayer.Deaths;
                    _hudManager?.ShowStockToast(PlayerEntityId, "P1 · −1 LIFE");
                }
                foreach (var npc in _npcs)
                {
                    var state = _bridge.GetState(npc.Id);
                    if (state.Deaths <= npc.LastToastDeaths) continue;
                    npc.LastToastDeaths = state.Deaths;
                    _hudManager?.ShowStockToast(npc.Id, $"P{npc.PlayerNumber} · −1 LIFE");
                }
                foreach (var state in _bridge.GetAllStates())
                    if (state.Value.Deaths >= MatchConfig.MaxStocks && !_eliminatedAt.ContainsKey(state.Key))
                    {
                        _eliminatedAt.Add(state.Key, _tick);
                        Debug.Log($"[Solo] eliminated entity={state.Key} tick={_tick}");
                        _sessionLog?.LogEvent((int)_tick, "eliminated", state.Key.ToString());
                    }
            }

            UpdateLockCamera();
            if (MatchConfig.Mode == GameMode.Solo && !_soloResultsShown)
            {
                var outcome = _bridge.InternalSim.EvaluateMatch();
                if (outcome.IsEnded)
                {
                    _aimHandler?.ResetPresentation();
                    if (Mayhem)
                    {
                        _soloResultsShown = true;
                        _hudManager?.ShowMayhemEnding(_bridge.InternalSim.MayhemPlayers,
                            id => id == PlayerEntityId ? "P1" : $"P{_npcs.First(n => n.Id == id).PlayerNumber}");
                    }
                    else BuildSoloResults(outcome);
                }
            }
            CaptureSimulationTickCompleted?.Invoke(_tick);
        }

        private void LateUpdate() => _mayhem?.UpdateView();

        // Leaving keeps the Mayhem format, so Fighter Select reopens on the same show's seats.
        protected override Action PauseRetry => Mayhem ? RestartMayhem : null;

        private static void RestartMayhem()
        {
            MatchConfig.MayhemSeed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void BuildSoloResults(MatchOutcome outcome)
        {
            _soloResultsShown = true;
            var entries = new List<ClientSession.ResultEntry>();
            foreach (var pair in _bridge.GetAllStates())
            {
                var npc = _npcs.FirstOrDefault(slot => slot.Id == pair.Key);
                int playerNumber = npc?.PlayerNumber ?? 1;
                var fighter = npc?.Def.Class ?? _playerEntry.Definition.Class;
                var state = pair.Value;
                int placement = 1;
                if (_eliminatedAt.TryGetValue(pair.Key, out uint eliminated))
                    placement += _bridge.GetAllStates().Keys.Count(id => id != pair.Key &&
                        (!_eliminatedAt.TryGetValue(id, out uint other) || other > eliminated));
                entries.Add(new ClientSession.ResultEntry(pair.Key, placement, kos: 0, falls: state.Deaths,
                    name: $"P{playerNumber}", className: fighter.ToString(),
                    stocksRemaining: Math.Max(0, MatchConfig.MaxStocks - state.Deaths), damagePercent: state.DamagePercent));
            }
            entries.Sort((a, b) => a.Placement != b.Placement ? a.Placement.CompareTo(b.Placement) : a.EntityId.CompareTo(b.EntityId));
            var results = new ClientSession.MatchResultsData(outcome.IsSharedVictory, MatchConfig.ArenaName, 0, entries);
            ClientSession.SetLocalMatchResults(results);
            var winner = entries[0];
            Enum.TryParse(winner.ClassName, out CharacterClass winnerClass);
            _hudManager?.ShowSoloEnding(winner.Name, winnerClass, results.SharedVictory);
            foreach (var entry in entries)
            {
                string result = $"{entry.Name} {entry.ClassName} place={entry.Placement} falls={entry.Falls}";
                Debug.Log($"[Solo] result {result}");
                _sessionLog?.LogEvent((int)_tick, "result", result);
            }
            FinishSessionLog("solo-ended");
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

        private CharacterState SelectOpponent(NpcSlot npc, CharacterState self)
        {
            if (MatchConfig.Mode != GameMode.Solo)
            {
                npc.TargetId = PlayerEntityId;
                return _bridge.GetState(PlayerEntityId);
            }
            ulong nearest = 0;
            float distance = float.PositiveInfinity;
            float currentDistance = float.PositiveInfinity;
            foreach (var pair in _bridge.GetAllStates())
            {
                if (pair.Key == npc.Id || _bridge.InternalSim.IsEntityInactive(pair.Key)) continue;
                var other = pair.Value;
                float dx = self.PX - other.PX, dy = self.PY - other.PY, dz = self.PZ - other.PZ;
                float squared = dx * dx + dy * dy + dz * dz;
                if (pair.Key == npc.TargetId) currentDistance = squared;
                if (squared < distance) { nearest = pair.Key; distance = squared; }
            }
            // Keep the current opponent until another is at least 20 percent closer.
            if (nearest != npc.TargetId && (float.IsPositiveInfinity(currentDistance) || distance < currentDistance * 0.64f))
            {
                ulong previous = npc.TargetId;
                npc.TargetId = nearest;
                npc.Memory.Reset();
                if (npc.Input == null)
                {
                    Debug.Log($"[Solo] CPU P{npc.PlayerNumber} target={nearest} previous={previous} tick={_tick}");
                    _sessionLog?.LogEvent((int)_tick, "cpu-target", $"P{npc.PlayerNumber}:{previous}->{nearest}");
                }
            }
            return npc.TargetId != 0 ? _bridge.GetState(npc.TargetId) : self;
        }

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
            slot.Memory.ObserveOpponent(playerState);
            return _npcPolicy.Decide(npcState, slot.Def, slot.Rng, slot.Memory,
                _arenaDef, slot.Entry.BakedAnimation, behavior, _bridge.GetActiveMove(slot.Id)?.Slot,
                mayhemThreats: Mayhem ? new MayhemThreats(_bridge.InternalSim) : null);
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
