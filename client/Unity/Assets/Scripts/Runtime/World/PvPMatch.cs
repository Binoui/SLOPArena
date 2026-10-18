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
    /// PvP match backed by a remote GameHost. Uses RollbackSimulationBridge (ADR-0039): every
    /// fighter is predicted in one simulation from the newest World Frame, so the match
    /// presents like Training: projectiles and sword trails from the predicted resolver,
    /// effects and hits from the rollback ledgers (ruled-out effects stop), and position
    /// corrections eased.
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
            _bridge = new RollbackSimulationBridge(arena, _networkClient, PlayerEntityId,
                new StockMatchRule((byte)MatchConfig.MaxStocks));
            SpawnStageVisual(arena);
            if (_combatFeedback == null)
                _combatFeedback = FindFirstObjectByType<CombatFeedback>();
            if (_combatFeedback == null)
                _combatFeedback = gameObject.AddComponent<CombatFeedback>();
            _combatFeedback.SetSimulation(_bridge);
            _projectileVFX = gameObject.AddComponent<ProjectileVFXManager>();
            _projectileVFX.SetPredictedSimulation(_bridge.Simulation);

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
                var opponentEntry = contentCatalog.Resolve(opp.Class);
                if (opponentEntry == null)
                {
                    ClientSession.RejectMatchStart($"Opponent selector {opp.Class} is absent from the admitted content catalog.");
                    return;
                }
                var def = opponentEntry.Definition;

                // Every fighter is simulated locally, drawn or not: register its content before
                // the first World Frame, which then restores its authoritative state.
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
                    Debug.LogWarning($"[PvPMatch] No opponent template in scene — opponent {opp.EntityId} is simulated but not drawn.");
                    continue;
                }

                if (!SetupRenderer(renderer, opponentEntry, arena, opp.EntityId, false))
                {
                    ClientSession.RejectMatchStart($"Opponent render content could not be loaded for entity {opp.EntityId}.");
                    return;
                }
                renderer.transform.position = SpawnPosition(arena, opp.EntityId);
                _opponentRenderers[opp.EntityId] = renderer;
                renderer.RenderInterpolation = true;
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
            _playerRenderer.RenderInterpolation = true;
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
            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (frameMs > _windowMaxFrameMs) _windowMaxFrameMs = frameMs;
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

        protected override void OnMatchFixedUpdate()
        {
            if (_bridge == null || _playerRenderer == null) return;
            _networkClient.PollTransport();
            int connectionGeneration = _networkClient.ConnectionGeneration;
            if (connectionGeneration != _observedConnectionGeneration)
            {
                _observedConnectionGeneration = connectionGeneration;
                _clockAcknowledgedGeneration = -1;
                _bridge.ResetConnection();
            }
            // Authoritative corrections and track switches move fighters without time
            // passing; hand exactly that displacement to presentation to smooth out.
            CaptureDisplayedPositions();
            _bridge.PumpNetwork();
            ApplyCorrectionSmoothing();
            foreach (var frame in _bridge.LastControlFrames)
                if (frame.Packet.EntityId == PlayerEntityId &&
                    frame.Packet.Kind == NetplayControlKind.Clock)
                {
                    _clockAcknowledgedGeneration = connectionGeneration;
                    if (frame.Packet.LateInputs > 0)
                        LogLateInputs(frame.Packet);
                }

            // Every World Frame carries every fighter: one frame is the full roster baseline.
            bool rosterBaselineReady = _bridge.HasWorldFrame;
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
            else
                _bridge.Idle();
            _projectileVFX.OnTick();
            _combatFeedback?.OnTick();

            if (_bridge.LatestMatchResult != null && ClientSession.CurrentMatchResults == null)
                ClientSession.ApplyAuthoritativeMatchResult(_bridge.LatestMatchResult);

            _hudManager?.Refresh();

            // Apply server states to renderers
            _playerRenderer.ApplyServerState(_bridge.GetState(PlayerEntityId));
            foreach (var kv in _opponentRenderers)
                kv.Value.ApplyServerState(_bridge.GetState(kv.Key));
            UpdateSwordTrailsFromResolver(_bridge.Resolver);
            RetractTimelineEvents(_bridge.RetractedPresentationEvents);
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
            if (_tick % NetplayStatsIntervalTicks == 0 && matchState == MatchState.Playing)
            {
                var stats = _bridge.TakeStats(NetworkClient.MonotonicSeconds);
                var p = stats.Prediction;
                _lastWindowMispredictions = p.Mispredictions;
                Debug.Log(FormattableString.Invariant(
                    $"[Netplay] utc={DateTime.UtcNow:HH:mm:ss} updates={stats.Updates} skipped={stats.SkippedUpdates} extraSteps={stats.ExtraStepUpdates} ") +
                    FormattableString.Invariant(
                    $"lead={stats.LeadTicks}({stats.LeadBonus:+0;-0;0}) ahead={stats.AheadOfServerTicks} ") +
                    FormattableString.Invariant(
                    $"frames={p.Frames} stale={p.StaleFrames} rejected={p.RejectedFrames} anchors={p.Anchors} exact={p.ExactAnchors} ") +
                    FormattableString.Invariant(
                    $"corrections={p.Corrections} mispredicts={p.Mispredictions} selfCorr={p.MaxSelfCorrectionMeters:F2}m oppCorr={p.MaxOpponentCorrectionMeters:F2}m ") +
                    FormattableString.Invariant(
                    $"selfSnap={p.MaxSelfSnapMeters:F2}m oppSnap={p.MaxOpponentSnapMeters:F2}m window={p.PredictionTicks}(max {p.MaxAnchorAgeTicks}) ") +
                    FormattableString.Invariant(
                    $"resim={p.ResimulatedTicks} resimMs={p.TotalResimulationMs:F1}(max {p.MaxResimulationMs:F2}) jumps={p.Jumps} starved={p.StarvedTicks} ") +
                    FormattableString.Invariant(
                    $"effects=+{p.EffectsStarted}/-{p.EffectsRetracted} hits=+{p.HitsStarted}/-{p.HitsRetracted} selfInputMismatch={p.SelfInputMismatches} ") +
                    FormattableString.Invariant(
                    $"serverLate={stats.ServerLateInputs} minSlack={stats.MinInputSlack?.ToString() ?? "-"} ping={_networkClient.LastPingMilliseconds?.ToString("F0") ?? "?"}ms ") +
                    FormattableString.Invariant($"frameMs={Time.smoothDeltaTime * 1000f:F1} maxFrameMs={_windowMaxFrameMs:F1}"));
                _windowMaxFrameMs = 0f;
            }
        }

        /// <summary>The GameServer received some of our inputs after their tick ran. Pair the
        /// report with Steam's view of our connection so the next stall can be attributed.</summary>
        private void LogLateInputs(NetplayControlPacket clock)
        {
            float now = Time.unscaledTime;
            if (now - _lastLateInputLogAt < 1f) return;
            _lastLateInputLogAt = now;
            string slack = clock.InputSlack == NetplayControlPacket.NoInputSlack ? "-" : clock.InputSlack.ToString();
            Debug.Log(FormattableString.Invariant(
                $"[Netplay] serverLate utc={DateTime.UtcNow:HH:mm:ss.fff} late={clock.LateInputs} minSlack={slack} serverTick={clock.Tick} ") +
                FormattableString.Invariant($"maxFrameMs={_windowMaxFrameMs:F1} {_networkClient.DescribeTransport()}"));
        }

        private const int NetplayStatsIntervalTicks = 300;
        private float _windowMaxFrameMs;
        private int _lastWindowMispredictions;
        private float _lastLateInputLogAt = float.NegativeInfinity;
        private readonly Dictionary<ulong, Vector3> _displayedBeforePump = new();

        private void CaptureDisplayedPositions()
        {
            _displayedBeforePump.Clear();
            if (_bridge.AuthoritativeMatchState != MatchState.Playing) return;
            var self = _bridge.GetState(PlayerEntityId);
            _displayedBeforePump[PlayerEntityId] = new Vector3(self.PX, self.PY, self.PZ);
            foreach (var id in _opponentRenderers.Keys)
            {
                var state = _bridge.GetState(id);
                _displayedBeforePump[id] = new Vector3(state.PX, state.PY, state.PZ);
            }
        }

        private void ApplyCorrectionSmoothing()
        {
            foreach (var (id, before) in _displayedBeforePump)
            {
                var state = _bridge.GetState(id);
                var delta = new Vector3(state.PX, state.PY, state.PZ) - before;
                var renderer = id == PlayerEntityId
                    ? _playerRenderer
                    : _opponentRenderers.TryGetValue(id, out var opponent) ? opponent : null;
                if (renderer != null)
                    renderer.AddVisualCorrection(delta);
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
            GUI.Label(new Rect(10, 10, 400, 20), $"Prediction window: {_bridge.PredictionTicks} ticks", style);
            GUI.Label(new Rect(10, 30, 400, 20), $"Mispredictions (last 5 s): {_lastWindowMispredictions}", style);
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
