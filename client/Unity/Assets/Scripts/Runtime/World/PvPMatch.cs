using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using SlopArena.Client.Network;
using SlopArena.Client.Simulation;

namespace SlopArena.Client.World
{
    /// <summary>
    /// PvP match backed by a remote server. Uses RollbackSimulationBridge (ADR-0011):
    /// the local player predicts continuously; opponents predict while in a movement
    /// state and render raw from the server otherwise.
    /// </summary>
    public class PvPMatch : MatchBase
    {
        [Header("Entities (Opponent)")]
        [SerializeField] private PlayerRenderer _opponentRenderer;

        [Header("Network")]
        [SerializeField] private NetworkClient _networkClient;
        [Header("Combat")]
        [SerializeField] private CombatFeedback _combatFeedback;
        private ProjectileVFXManager _projectileVFX;


        private readonly Dictionary<ulong, PlayerRenderer> _opponentRenderers = new();
        private PlayerRenderer[] _opponentArray = System.Array.Empty<PlayerRenderer>();
        private readonly Dictionary<ulong, InputState> _localInputs = new(1);


        private uint _tick;
        private MatchState _lastMatchState = MatchState.Waiting;
        private RollbackSimulationBridge _bridge = null!;
        private readonly Dictionary<ulong, ushort> _lastPresentedDeaths = new();
        private int _observedConnectionGeneration = -1;
        private int _clockAcknowledgedGeneration = -1;
        private bool _resultsScheduled;
        private Coroutine? _countdownPresentation;
        protected override ISimulationBridge Bridge => _bridge;

        protected override void LeaveMatch()
        {
            bool roomMode = ClientSession.SelectedOnlineMode == ClientSession.OnlineSelection.Room;
            if (MatchConfig.Transport == MatchTransport.DevelopmentUdp && MatchConfig.IsHost)
                ServerHost.Instance?.Stop();

            if (roomMode)
            {
                _ = LeaveRoomAndReturnAsync();
                return;
            }

            _ = ClientSession.ActiveLobby?.LeaveLobbyAsync();
            ClientSession.ClearMatchForExit();
            FrontendController.Show(FrontendPage.ServerBrowser);
        }

        private async System.Threading.Tasks.Task LeaveRoomAndReturnAsync()
        {
            bool leftRoom = false;
            try
            {
                var chat = ChatSession.Instance;
                if (chat != null && await chat.EnsureConnectedAsync() &&
                    chat.ActiveLobby is { } lobby)
                {
                    await lobby.LeaveRoomAsync();
                    leftRoom = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PvPMatch] Could not leave Room after exiting match: {ex.Message}");
            }

            ClientSession.ClearMatchForExit();
            if (leftRoom)
            {
                ClientSession.SelectedRoomId = Guid.Empty;
                ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
                ClientSession.SelectedServerName = string.Empty;
                ServerBrowserUI.PendingReturnNotice = "You left the Room and the match.";
                FrontendController.Show(FrontendPage.ServerBrowser);
                return;
            }

            LobbyRoomUI.PendingRoomNotice = "Couldn’t leave the Room. Reconnecting so you can retry.";
            FrontendController.Show(FrontendPage.LobbyRoom);
        }


        protected override void OnMatchStart()
        {
            Debug.Log($"[{GetType().Name}] Starting match: mode={MatchConfig.Mode} char={MatchConfig.PlayerClass} arena={MatchConfig.ArenaName}");
            // Baked arena is required (issue #77): hardcoded ArenaRegistry arenas carry
            // no collision data, so a missing .arena file used to make players fall
            // through the floor. The client only renders; the server is authoritative.
            string? arenaPath = BakedContentPaths.ResolveArena(MatchConfig.ArenaName);
            if (arenaPath == null)
            {
                ClientSession.RejectMatchStart($"Baked arena '{MatchConfig.ArenaName}' is missing.");
                return;
            }
            var arenaOpt = ArenaBinaryFormat.LoadFromFile(arenaPath);
            if (arenaOpt is not ArenaDefinition arena)
            {
                ClientSession.RejectMatchStart($"Baked arena could not be loaded: {arenaPath}");
                return;
            }
            if (arena.Purpose != ArenaPurpose.PvP)
            {
                ClientSession.RejectMatchStart("Solo Arcade stages cannot run as PvP matches.");
                return;
            }
            Debug.Log($"[PvPMatch] Loaded arena: {arenaPath}");

            SlopArena.Shared.Simulation.OnDebugLog = msg => Debug.Log(msg);

            if (_networkClient == null)
            {
                ClientSession.RejectMatchStart("PvP scene is missing its NetworkClient.");
                return;
            }
            _networkClient.EntityId = PlayerEntityId;
            _bridge = new RollbackSimulationBridge(arena, _networkClient, PlayerEntityId);
            SpawnStageVisual(arena);
            if (_combatFeedback == null)
                _combatFeedback = FindFirstObjectByType<CombatFeedback>();
            if (_combatFeedback == null)
                _combatFeedback = gameObject.AddComponent<CombatFeedback>();
            _combatFeedback.SetSimulation(_bridge);
            _projectileVFX = gameObject.AddComponent<ProjectileVFXManager>();
            _projectileVFX.SetRemoteSnapshots();

            // Character definitions come only from the admitted match catalog.
            var contentCatalog = SlopArena.Client.ClientSession.MatchContentCatalog;
            if (contentCatalog == null)
            {
                ClientSession.RejectMatchStart("Admitted match content catalog is unavailable.");
                return;
            }
            var playerEntry = contentCatalog.Resolve(MatchConfig.PlayerClass);
            if (playerEntry == null)
            {
                ClientSession.RejectMatchStart($"Player selector {MatchConfig.PlayerClass} is absent from the admitted content catalog.");
                return;
            }
            var playerDef = playerEntry.Definition;
            _playerDef = playerDef;

            // Shared player renderer + HUD setup
            if (!SetupPlayerRenderer(playerEntry, arena))
            {
                ClientSession.RejectMatchStart("Player render content could not be loaded.");
                return;
            }
            SetupHUD(playerDef);

            // Opponent renderers — one per MatchConfig.Opponents entry. The scene's
            // _opponentRenderer is the first opponent + the clone template for the rest.
            _opponentRenderers.Clear();
            for (int i = 0; i < MatchConfig.Opponents.Count; i++)
            {
                var opp = MatchConfig.Opponents[i];

                PlayerRenderer renderer;
                if (i == 0 && _opponentRenderer != null)
                {
                    renderer = _opponentRenderer;
                }
                else if (_opponentRenderer != null)
                {
                    var clone = Instantiate(_opponentRenderer.gameObject);
                    clone.name = $"Opponent_{opp.EntityId}";
                    renderer = clone.GetComponent<PlayerRenderer>();
                }
                else
                {
                    Debug.LogWarning($"[PvPMatch] No opponent template in scene — skipping opponent {opp.EntityId}.");
                    continue;
                }

                var opponentEntry = contentCatalog.Resolve(opp.Class);
                if (opponentEntry == null)
                {
                    ClientSession.RejectMatchStart($"Opponent selector {opp.Class} is absent from the admitted content catalog.");
                    return;
                }
                var def = opponentEntry.Definition;
                if (!SetupRenderer(renderer, opponentEntry, arena, opp.EntityId, false))
                {
                    ClientSession.RejectMatchStart($"Opponent render content could not be loaded for entity {opp.EntityId}.");
                    return;
                }
                renderer.transform.position = SpawnPosition(arena, opp.EntityId);
                _opponentRenderers[opp.EntityId] = renderer;

                // Register with the rollback bridge: populates _defs (prediction needs
                // the CharacterDefinition) and seeds the RawTrack initial state so the
                // opponent renders at its spawn until the first server packet. Without
                // this, PvP crashed on the first predictable opponent packet
                var oppSpawn = SpawnPointFor(arena, opp.EntityId);
                var initialState = new CharacterState
                {
                    PX = oppSpawn.X, PY = oppSpawn.Y, PZ = oppSpawn.Z,
                    FacingYaw = oppSpawn.Yaw,
                    State = ActionState.Idle,
                    IsGrounded = true,
                    JumpsLeft = def.Movement.MaxJumps,
                    AirDodgesLeft = 1,
                    DamagePercent = 0,
                };
                _bridge.RegisterEntity(opp.EntityId, def, initialState, opponentEntry.BakedAnimation);
                renderer.ApplyServerState(initialState);
            }
            _opponentArray = new List<PlayerRenderer>(_opponentRenderers.Values).ToArray();

            // Ground-shadow rings under every player: local + all opponents.
            var lockRenderers = new PlayerRenderer[_opponentArray.Length + 1];
            lockRenderers[0] = _playerRenderer;
            Array.Copy(_opponentArray, 0, lockRenderers, 1, _opponentArray.Length);
            SetupLockIndicator(lockRenderers, arena);

            // Player spawns at its own roster spawn point (entityId 1..N ↔ spawnPoints[0..N-1]).
            _playerRenderer.transform.position = SpawnPosition(arena, PlayerEntityId);

            var selfSpawn = SpawnPointFor(arena, PlayerEntityId);
            var initialPlayerState = new CharacterState
            {
                PX = selfSpawn.X, PY = selfSpawn.Y, PZ = selfSpawn.Z,
                FacingYaw = selfSpawn.Yaw,
                State = ActionState.Idle,
                IsGrounded = true,
                JumpsLeft = playerDef.Movement.MaxJumps,
                AirDodgesLeft = 1,
                DamagePercent = 0,
            };
            _bridge.RegisterEntity(PlayerEntityId, playerDef, initialPlayerState, playerEntry.BakedAnimation);
            _playerRenderer.ApplyServerState(initialPlayerState);
            _lastPresentedDeaths.Clear();
            _lastPresentedDeaths[PlayerEntityId] = 0;
            foreach (var id in _opponentRenderers.Keys)
                _lastPresentedDeaths[id] = 0;
            if (MatchConfig.Transport == MatchTransport.SteamP2P &&
                MatchConfig.SteamDescriptor is { } descriptor)
            {
                _networkClient.ConnectionFailed += OnSteamConnectionFailed;
                _networkClient.ConnectSteam(descriptor);
            }
            else if (MatchConfig.Transport == MatchTransport.DevelopmentUdp)
            {
                _networkClient.Connect(MatchConfig.ServerIP, MatchConfig.ServerPort);
            }
            else
            {
                ClientSession.RejectMatchStart("PvP match has no approved transport.");
                return;
            }
            _observedConnectionGeneration = _networkClient.ConnectionGeneration;


            // Shared camera + aim setup
            SetupCamera();
            SetupAimHandler(playerDef);
        }

        private void Update()
        {
            if (IsPaused) return; // pause menu owns Esc + skips polling (issue #77)
            _inputController.Poll();
        }

        /// <summary>Roster spawn point for an entity (entityId 1..N ↔ spawnPoints[0..N-1]),
        /// matching the server's PickSpawn fallback (issue #35).</summary>
        private static SpawnPoint SpawnPointFor(ArenaDefinition arena, ulong entityId)
        {
            int idx = (int)entityId - 1;
            if (idx < 0 || arena.SpawnPoints == null || idx >= arena.SpawnPoints.Length)
                return new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = 0f };
            return arena.SpawnPoints[idx];
        }

        private static Vector3 SpawnPosition(ArenaDefinition arena, ulong entityId)
        {
            var s = SpawnPointFor(arena, entityId);
            return new Vector3(s.X, s.Y, s.Z);
        }
        private bool HasCompleteRosterBaseline()
        {
            if (!_bridge.HasInitialState(PlayerEntityId))
                return false;
            foreach (var opponent in MatchConfig.Opponents)
                if (!_bridge.HasInitialState(opponent.EntityId))
                    return false;
            return true;
        }

        protected override void OnMatchFixedUpdate()
        {
            if (_bridge == null || _playerRenderer == null) return;
            int connectionGeneration = _networkClient.ConnectionGeneration;
            if (connectionGeneration != _observedConnectionGeneration)
            {
                _observedConnectionGeneration = connectionGeneration;
                _clockAcknowledgedGeneration = -1;
                _bridge.ResetInitialStates();
            }
            _bridge.PumpNetwork();
            foreach (var frame in _bridge.LastControlFrames)
                if (frame.Packet.EntityId == PlayerEntityId &&
                    frame.Packet.Kind == NetplayControlKind.Clock)
                    _clockAcknowledgedGeneration = connectionGeneration;

            bool rosterBaselineReady = HasCompleteRosterBaseline();
            if (rosterBaselineReady && _networkClient.IsServerConnected &&
                _networkClient.LastPingMilliseconds.HasValue &&
                _clockAcknowledgedGeneration != connectionGeneration)
                _networkClient.SendReady();

            byte slot = _inputController.ConsumePendingSlotPress();

            var playerState = _bridge.GetState(PlayerEntityId);
            var aimCtx = _aimHandler != null
                ? _aimHandler.Evaluate(playerState, slot, _playerDef, _inputController)
                : AimContext.None;
            _showCrosshair = _aimHandler?.ShowCrosshair ?? false;

            byte targetEntityId = PickScreenTarget(
                _opponentArray,
                _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main);

            var (input, _, _) = _inputController.BuildInputState(
                _cameraMount,
                _playerRenderer.transform.eulerAngles.y,
                isNPC: false,
                pendingSlotPress: slot,
                aimCtx: aimCtx,
                canMove: null,
                targetEntityId: targetEntityId);

            if (rosterBaselineReady && _networkClient.IsServerConnected &&
                _clockAcknowledgedGeneration == connectionGeneration)
            {
                _localInputs[PlayerEntityId] = input;
                _bridge.Tick(_localInputs);
            }
            var projectileSnapshot = _networkClient.ReceiveProjectileVisuals();
            var swordTrailSnapshot = _networkClient.ReceiveSwordTrailSnapshot();
            _combatFeedback?.OnTick();

            if (_bridge.LatestMatchResult != null && ClientSession.CurrentMatchResults == null)
                ClientSession.ApplyAuthoritativeMatchResult(_bridge.LatestMatchResult);

            _hudManager?.Refresh();

            // Apply server states to renderers
            _playerRenderer.ApplyServerState(_bridge.GetState(PlayerEntityId));
            foreach (var kv in _opponentRenderers)
                kv.Value.ApplyServerState(_bridge.GetState(kv.Key));
            if (projectileSnapshot.HasValue)
                _projectileVFX.OnSnapshot(projectileSnapshot.Value);
            if (swordTrailSnapshot.HasValue)
                ApplySwordTrailSnapshot(swordTrailSnapshot.Value);
            PresentTimelineEvents();

            var presentationState = _bridge.GetState(PlayerEntityId);
            _opponentRenderers.TryGetValue(presentationState.TargetEntityId, out var presentationTarget);
            ushort targetDamagePercent = presentationTarget != null
                ? _bridge.GetState(presentationState.TargetEntityId).DamagePercent
                : (ushort)0;
            _aimHandler?.UpdateTargetPresentation(presentationState, presentationTarget, targetDamagePercent);

            UpdateLockCamera();

            PresentStockLosses();

            var matchState = _bridge.AuthoritativeMatchState;
            if (matchState != _lastMatchState)
            {
                Debug.Log($"[PvP] MatchState transition: {_lastMatchState} → {matchState}");
                _lastMatchState = matchState;

                if (matchState == MatchState.Countdown)
                {
                    if (_countdownPresentation != null)
                        StopCoroutine(_countdownPresentation);
                    _countdownPresentation = StartCoroutine(ShowCountdownPresentation());
                }
                else if (matchState == MatchState.Playing)
                {
                    _hudManager?.ShowMatchCallout("SLOP IT OUT", 1f);
                }
                else if (matchState == MatchState.Ended)
                {
                    _hudManager?.ShowMatchCallout("MATCH COMPLETE", 1.4f);
                    _aimHandler?.ResetPresentation();
                }
            }

            if (matchState == MatchState.Ended)
                TryScheduleResults();

            _tick++;
            if (_tick % 120 == 1)
            {
                var ps = _bridge.GetState(PlayerEntityId);
                Debug.Log($"[PvP] tick={_tick} connected={_networkClient.IsServerConnected} " +
                          $"pos=({ps.PX:F1},{ps.PY:F2},{ps.PZ:F1}) serverTick={_networkClient.LastServerTick}");
            }
        }

        /// <summary>
        /// Follow the authoritative locked enemy while keeping the camera anchored
        /// on the player. Missing targets and unlock restore manual orbit behavior.
        /// </summary>
        private void UpdateLockCamera()
        {
            if (_cameraMount == null) return;
            var local = _bridge.GetState(PlayerEntityId);
            if (local.LockOn && local.TargetEntityId != 0
                && _opponentRenderers.TryGetValue(local.TargetEntityId, out var target))
            {
                _cameraMount.SetLockFocus(_playerRenderer.transform, target.transform.position);
                return;
            }
            _cameraMount.ClearLockFocus(_playerRenderer.transform);
        }


        protected override void OnGUI()
        {
            base.OnGUI();
            if (ChatInputGate.SuppressShortcuts || !UnityEngine.Input.GetKey(KeyCode.F3)) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            GUI.Label(new Rect(10, 10, 400, 20), $"Corrections: {_bridge.CorrectionCount}", style);
            GUI.Label(new Rect(10, 30, 400, 20), $"Frontier window: {_bridge.LastFrontierTicks} ticks", style);
        }

        private void PresentStockLosses()
        {
            PresentStockLoss(PlayerEntityId, _bridge.GetState(PlayerEntityId));
            foreach (var id in _opponentRenderers.Keys)
                PresentStockLoss(id, _bridge.GetState(id));
        }

        private void PresentStockLoss(ulong entityId, CharacterState state)
        {
            if (!_lastPresentedDeaths.TryGetValue(entityId, out var previous))
            {
                _lastPresentedDeaths[entityId] = state.Deaths;
                return;
            }

            if (state.Deaths <= previous)
                return;

            _lastPresentedDeaths[entityId] = state.Deaths;
            _hudManager?.ShowStockToast(entityId, $"{PlayerLabel(entityId)} · −1 LIFE");
        }

        private string PlayerLabel(ulong entityId)
        {
            if (entityId == PlayerEntityId)
                return "YOU";
            foreach (var opponent in MatchConfig.Opponents)
                if (opponent.EntityId == entityId)
                    return $"P{entityId}";
            return $"P{entityId}";
        }

        private System.Collections.IEnumerator ShowCountdownPresentation()
        {
            _hudManager?.ShowMatchCallout("READY", 2f);
            yield return new WaitForSecondsRealtime(2f);
            _hudManager?.ShowMatchCallout("3", 1f);
            yield return new WaitForSecondsRealtime(1f);
            _hudManager?.ShowMatchCallout("2", 1f);
            yield return new WaitForSecondsRealtime(1f);
            _hudManager?.ShowMatchCallout("1", 1f);
        }

        private void TryScheduleResults()
        {
            if (_resultsScheduled || ClientSession.CurrentMatchResults == null)
                return;

            _resultsScheduled = true;
            StartCoroutine(ReturnToResultsAfterDelay());
        }

        private System.Collections.IEnumerator ReturnToResultsAfterDelay()
        {
            yield return new WaitForSecondsRealtime(2f);
            if (ClientSession.CurrentMatchResults == null)
            {
                Debug.LogError("[PvP] Match ended without an authoritative result snapshot.");
                yield break;
            }

            FrontendController.Show(FrontendPage.Results);
        }
        private void OnSteamConnectionFailed(string reason)
        {
            if (MatchConfig.SteamDescriptor is { } descriptor)
                ClientSession.ApplySteamTransportFailure(descriptor.MatchId, reason);
        }

        protected override void OnDestroy()
        {
            if (_networkClient != null)
                _networkClient.ConnectionFailed -= OnSteamConnectionFailed;
            base.OnDestroy();
        }
    }
}
