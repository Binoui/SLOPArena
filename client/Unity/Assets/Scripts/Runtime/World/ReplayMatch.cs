using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Replays;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.Replays;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopArena.Client.World
{
    /// <summary>
    /// The in-game Replay viewer. A MatchBase, so renderer setup, timeline events, tether, Bonk, sword trails, HUD
    /// and camera framing are the live code; it reads no MatchConfig. It presents a Replay's display world in the
    /// order PvPMatch presents its predicted world, one tick per FixedUpdate, with Time.timeScale as the speed.
    /// </summary>
    public sealed class ReplayMatch : MatchBase, IReplayPresenter, IRecordedHudSource
    {
        [SerializeField] private PlayerRenderer _fighterTemplate = null!;
        [SerializeField] private CombatFeedback _combatFeedback = null!;

        private ReplayWorld? _replay;
        private ReplayContent? _content;
        private ReplayModes? _modes;
        private ReplayBridge? _bridge;
        private ReplayRequest? _request;
        private ReplayTransport _transport = new(ReplayTransportState.Paused, ReplaySpeed.One);
        private ReplayOverlay? _overlay;
        private ProjectileVFXManager _projectiles = null!;
        private ReplayFrame _frame;
        private ulong _focus;
        private uint? _pendingSeek;
        private bool _pausedForMenu;
        private CinemachineBrain? _brain;
        private string? _banner;
        private readonly ReplayEffects _effects = new();
        private readonly List<ulong> _cast = new();
        private readonly Dictionary<ulong, byte> _deaths = new();
        private readonly Dictionary<ulong, (ActionState State, int Ticks)> _poseTicks = new();

        /// <summary>The open Replay; null before it opens or when it failed to.</summary>
        public ReplayWorld? Replay => _replay;
        public ReplayTransport Transport => _transport;
        public ulong Focus => _focus;
        /// <summary>What a viewer sees now, for the history-independent seek check.</summary>
        public IReadOnlyList<string> Census()
        {
            var lines = _effects.Census(Fighters.Values).ToList();
            if (StageVisual != null && StageVisual.Find("Ball Sender") is { } sender)
                lines.Add($"sender {sender.position.x:0.000},{sender.position.y:0.000},{sender.position.z:0.000}");
            if (StageVisual != null)
            {
                var block = new MaterialPropertyBlock();
                foreach (var t in StageVisual.GetComponentsInChildren<Transform>(true))
                    if (SlopballCourtShow.CardNames.Contains(t.name) || t.name is "Flood Panels" or "Flood Beams")
                    {
                        var scale = t.localScale;
                        var rotation = t.localRotation;
                        var position = t.localPosition;
                        var renderer = t.GetComponent<Renderer>();
                        block.Clear();
                        if (renderer != null) renderer.GetPropertyBlock(block);
                        var uv = block.GetVector("_BaseMap_ST");
                        var colour = block.GetColor("_BaseColor");
                        lines.Add($"court {t.name} scale={scale.ToString("F5")} rotation={rotation.ToString("F5")} position={position.ToString("F5")} uv={uv.ToString("F5")} colour={colour.ToString("F5")} glow={block.GetFloat("_Glow"):F5}");
                    }
                foreach (var light in StageVisual.GetComponentsInChildren<Light>(true))
                    if (light.name == SlopballCourtShow.KeyLightName)
                        lines.Add($"court light {light.intensity:F5} {light.color.ToString("F5")}");
            }
            return lines;
        }

        public PlayerRenderer? RendererOf(ulong entityId) => Fighters.TryGetValue(entityId, out var renderer) ? renderer : null;
        /// <summary>Ticks presented since the scene opened, for checks.</summary>
        public int PresentedTicks { get; private set; }

        protected override ISimulationBridge Bridge => _bridge!;
        protected override ulong FocusEntityId => _focus;
        protected override int? FramingMaxStocks => StockLimit > 0 ? StockLimit : null;
        protected override SlopballCamp CampOf(ulong id)
            => _replay?.Cast.FirstOrDefault(m => m.EntityId == id) is { } member && SlopballCamps.TryParse(member.Recorded.CampId, out var camp)
                ? camp : SlopballCamp.None;
        protected override string MusicArenaName => _replay?.Header.ArenaId ?? "";
        protected override bool FreezesOnPause => false;
        public override bool DrivesControllerFeedback => false;
        public override bool CameraUsesUnscaledTime => _transport.State != ReplayTransportState.Playing;

        private int StockLimit => _replay != null && _replay.Header.Rules.FormatId == "stock" ? _replay.Header.Rules.MaxStocks : 0;

        protected override void Awake()
        {
            base.Awake();
            enabled = false;
            if (!ReplayLaunch.TryTake(out _request) || _request == null)
            {
                ReplayLaunch.Return(FrontendPage.Home, "No Replay was requested.");
                return;
            }
            if (!ReplayContentSource.TryToday(out var content, out var contentFailure) || content == null)
            {
                ReplayLaunch.Return(_request.ReturnTo, $"Today's content is unavailable: {contentFailure}");
                return;
            }
            Stream file;
            try { file = File.OpenRead(_request.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                ReplayLaunch.Return(_request.ReturnTo, $"The Replay file could not be read: {e.Message}");
                return;
            }
            var opened = ReplayWorld.Open(file, content, _request.Track);
            if (opened.World == null)
            {
                ReplayLaunch.Return(_request.ReturnTo, ReplayText.Describe(opened.Failure!));
                return;
            }
            _replay = opened.World;
            _content = content;
            _bridge = new ReplayBridge(_replay.World);
            enabled = true;
        }

        protected override void OnMatchStart()
        {
            var replay = _replay!;
            var arena = replay.Arena;
            SpawnStageVisual(arena);
            _projectiles = GetComponent<ProjectileVFXManager>() ?? gameObject.AddComponent<ProjectileVFXManager>();
            _projectiles.SetPredictedSimulation(replay.World);
            if (_combatFeedback == null)
                _combatFeedback = GetComponent<CombatFeedback>() ?? gameObject.AddComponent<CombatFeedback>();
            _combatFeedback.SetSimulation(_bridge!);
            _combatFeedback.RecordedOwnPresentation = ShowedOwnPresentation;

            foreach (var member in replay.Cast)
            {
                var renderer = _cast.Count == 0 ? _playerRenderer
                    : _cast.Count == 1 && _fighterTemplate != null ? _fighterTemplate
                    : new GameObject($"Fighter{member.EntityId}").AddComponent<PlayerRenderer>();
                if (!SetupRenderer(renderer, member.Content, arena, member.EntityId, false))
                {
                    Fail($"Fighter {member.Content.Identity.PackageId} could not be drawn.");
                    return;
                }
                renderer.PlayerNumber = _cast.Count + 1;
                _cast.Add(member.EntityId);
            }

            _focus = _request!.Focus is { } focus && _cast.Contains(focus) ? focus : _cast[0];
            _playerRenderer = Fighters[_focus];
            SetupFocusHud();
            _hudManager!.Recorded = this;
            SetupCamera();
            _brain = FindFirstObjectByType<CinemachineBrain>();
            _modes = ReplayModes.Create(replay, new ReplayModeScene(transform, StageVisual, _hudManager, _cameraMount,
                () => _cameraMount?.RenderCamera ?? UnityEngine.Camera.main, _projectiles, Fighters, HudPlayers(), _content!.Catalog, () => _focus));
            if (_modes?.MayhemHost is { } mayhemHost) RegisterHostPresentation(ServerSimulation.MayhemHostEntityId, mayhemHost.Root, mayhemHost.Catalog);

            var host = _hudManager.Document?.rootVisualElement;
            if (host != null)
                _overlay = new ReplayOverlay(host, new ReplayOverlay.Actions
                {
                    TogglePlay = TogglePlay, StepBack = StepBack, StepForward = StepForward,
                    Slower = () => SetTransport(_transport.Slower()), Faster = () => SetTransport(_transport.Faster()),
                    PreviousFocus = () => CycleFocus(-1), NextFocus = () => CycleFocus(1),
                    ToggleNames = () => { if (_overlay != null) _overlay.NamesShown = !_overlay.NamesShown; },
                    Leave = LeaveMatch, Seek = RequestSeek,
                }, ReplayText.Track(replay.Track, replay.ExactRefusal), replay.Range.First, replay.Range.Last);

            _effects.Begin(gameObject.scene);
            var failure = replay.Seek(_request.StartTick ?? replay.Range.First, this);
            if (failure != null)
            {
                Fail(ReplayText.Describe(failure));
                return;
            }
            SetTransport(new ReplayTransport(ReplayTransportState.Playing, ReplaySpeed.One));
        }

        protected override void OnMatchFixedUpdate()
        {
            if (_replay == null || _transport.State != ReplayTransportState.Playing) return;
            var step = _replay.Step(this);
            if (step.Status == ReplayStep.End)
            {
                SetTransport(_transport.With(ReplayTransportState.Ended));
                _banner = ReplayText.Conclusion(_replay.Conclusion, _replay.PrefixReason);
                _overlay?.Banner(_banner);
            }
            else if (step.Status == ReplayStep.Failed)
                Fail(ReplayText.Describe(step.Failure!));
        }

        private void Update()
        {
            if (_replay == null) return;
            _modes?.Update(_replay.World, _cameraMount?.RenderCamera ?? UnityEngine.Camera.main);
            // The Esc menu opens over the Replay; the Replay pauses itself under it and resumes after.
            if (IsPaused != _pausedForMenu)
            {
                _pausedForMenu = IsPaused;
                if (IsPaused && _transport.State == ReplayTransportState.Playing) SetTransport(_transport.With(ReplayTransportState.Paused));
            }
            if (IsPaused) return;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            if (keyboard?.spaceKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true) TogglePlay();
            if (keyboard?.rightArrowKey.wasPressedThisFrame == true || pad?.dpad.right.wasPressedThisFrame == true) StepForward();
            if (keyboard?.leftArrowKey.wasPressedThisFrame == true || pad?.dpad.left.wasPressedThisFrame == true) StepBack();
            if (keyboard?.upArrowKey.wasPressedThisFrame == true || pad?.dpad.up.wasPressedThisFrame == true) SetTransport(_transport.Faster());
            if (keyboard?.downArrowKey.wasPressedThisFrame == true || pad?.dpad.down.wasPressedThisFrame == true) SetTransport(_transport.Slower());
            if (keyboard?.tabKey.wasPressedThisFrame == true)
                CycleFocus(keyboard.shiftKey.isPressed ? -1 : 1);
            if (pad?.rightShoulder.wasPressedThisFrame == true) CycleFocus(1);
            if (pad?.leftShoulder.wasPressedThisFrame == true) CycleFocus(-1);
            if (keyboard?.mKey.wasPressedThisFrame == true && _overlay != null) _overlay.NamesShown = !_overlay.NamesShown;
            if (keyboard?.hKey.wasPressedThisFrame == true) _overlay?.ToggleHidden();
        }

        /// <summary>The viewer owns cursor admission even when focus, seek or the menu resets the camera.</summary>
        public CameraMode ViewerCameraMode => !IsPaused && Mouse.current?.rightButton.isPressed == true
            ? CameraMode.Normal : CameraMode.FreeCursor;

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (_replay == null) return;
            // A timeline drag asks for many ticks a frame; seek once, to the latest.
            if (_pendingSeek is { } tick)
            {
                _pendingSeek = null;
                Seek(tick);
            }
            UpdateLabels();
        }

        // ── IReplayPresenter ───────────────────────────────────────────────────────────

        void IReplayPresenter.Reset(uint targetTick)
        {
            foreach (var renderer in Fighters.Values)
            {
                if (renderer == null) continue;
                renderer.ResetForFreshSimulation();
                if (renderer.TryGetComponent<WeaponAttach>(out var weapon)) weapon.ResetForSeek();
            }
            ResetSimulationPresentation();
            _projectiles.SetPredictedSimulation(_replay!.World);
            _modes?.Reset(targetTick);
            _effects.Sweep();
            _deaths.Clear();
            _poseTicks.Clear();
            _banner = null;
            _overlay?.Banner(null);
        }

        void IReplayPresenter.Present(in ReplayFrame frame)
        {
            if (frame.Delivery == ReplayDelivery.Warmup)
            {
                _effects.Mark();
                using (SilenceOutput()) PresentWorld(frame);
                _effects.AgeSinceMark(frame.TicksBeforeTarget / (float)ReplayHeader.TickRate);
            }
            else PresentWorld(frame);
        }

        /// <summary>The present half of PvPMatch's fixed update, without input, prediction or retraction.</summary>
        private void PresentWorld(in ReplayFrame frame)
        {
            _frame = frame;
            PresentedTicks++;
            var world = frame.World;
            _projectiles.OnTick();
            _combatFeedback.OnTick();
            _hudManager?.Refresh();
            foreach (var fighter in frame.View.Fighters)
            {
                if (!Fighters.TryGetValue(fighter.EntityId, out var renderer) || renderer == null) continue;
                var state = world.GetState(fighter.EntityId);
                renderer.ApplyServerState(state, world.GetActiveMove(fighter.EntityId)?.Slot, Clocks(fighter));
                _poseTicks[fighter.EntityId] = _poseTicks.TryGetValue(fighter.EntityId, out var pose) && pose.State == state.State
                    ? (state.State, pose.Ticks + 1) : (state.State, 0);
                if (state.State is ActionState.Attacking or ActionState.Aiming)
                    renderer.PlayScrubbedState(state, fighter.ActivationAirborne, _poseTicks[fighter.EntityId].Ticks);
            }
            UpdateSwordTrailsFromResolver(world.Resolver);
            PresentTimelineEvents();
            if (_modes?.Present(frame, _focus, _playerRenderer.transform, _cameraMount) != true) UpdateLockCamera(world);
            PresentStockLosses(frame);
            _overlay?.Show(frame.View.Tick, _replay!.Range.First, _transport.State == ReplayTransportState.Playing,
                ReplayTransport.Label(_transport.Speed), FocusName());
        }

        private static RecordedFighterClocks Clocks(ReplayFighter fighter)
            => new(fighter.SlotId.Length > 0 || fighter.MoveName.Length > 0
                    ? new AttackPose(fighter.PoseAnimationId, fighter.PoseElapsedTicks, fighter.PoseDurationTicks, fighter.PhaseClip)
                    : null,
                fighter.FlightElapsedTicks, fighter.FlightFuelFraction, fighter.ChargeTier, fighter.ChargeProgress);

        private void UpdateLockCamera(ServerSimulation world)
        {
            if (_cameraMount == null) return;
            var focus = world.GetState(_focus);
            if (focus.LockOn && focus.TargetEntityId != 0 && Fighters.TryGetValue(focus.TargetEntityId, out var target) && target != null)
                _cameraMount.SetLockFocus(_playerRenderer.transform, target.transform.position);
            else
                _cameraMount.ClearLockFocus(_playerRenderer.transform);
        }

        private void PresentStockLosses(in ReplayFrame frame)
        {
            foreach (var fighter in frame.View.Fighters)
            {
                bool known = _deaths.TryGetValue(fighter.EntityId, out var previous);
                _deaths[fighter.EntityId] = fighter.Deaths;
                if (known && fighter.Deaths > previous && frame.Delivery == ReplayDelivery.Live)
                    _hudManager?.ShowStockToast(fighter.EntityId, _replay!.Header.Rules.FormatId switch
                    {
                        MatchFormats.MankiMayhemWire => $"P{fighter.EntityId} · OUT · BACK NEXT ROUND",
                        MatchFormats.CourseRaceWire or MatchFormats.CourseCoopWire or MatchFormats.CourseScrambleWire => $"P{fighter.EntityId} · FALL · CHECKPOINT RESET",
                        _ => $"P{fighter.EntityId} · −1 LIFE",
                    });
            }
        }

        // ── Transport ───────────────────────────────────────────────────────────────

        public void TogglePlay()
        {
            if (_replay == null) return;
            if (_transport.State == ReplayTransportState.Ended)
            {
                if (Seek(_replay.Range.First)) SetTransport(_transport.With(ReplayTransportState.Playing));
                return;
            }
            SetTransport(_transport.With(_transport.State == ReplayTransportState.Playing ? ReplayTransportState.Paused : ReplayTransportState.Playing));
        }

        /// <summary>One tick forward as Live while paused; poses snap.</summary>
        public void StepForward()
        {
            if (_replay == null) return;
            if (_transport.State == ReplayTransportState.Playing) SetTransport(_transport.With(ReplayTransportState.Paused));
            var step = _replay.Step(this);
            if (step.Status == ReplayStep.End) SetTransport(_transport.With(ReplayTransportState.Ended));
            else if (step.Status == ReplayStep.Failed) Fail(ReplayText.Describe(step.Failure!));
            SnapPausedPoses();
        }

        /// <summary>Seek(current - 1), with the full warmup.</summary>
        public void StepBack()
        {
            if (_replay?.CurrentTick is not { } tick || tick <= _replay.Range.First) return;
            if (_transport.State != ReplayTransportState.Paused) SetTransport(_transport.With(ReplayTransportState.Paused));
            Seek(tick - 1);
        }

        /// <summary>Timeline drags call this; LateUpdate applies the latest request once per rendered frame.</summary>
        public void RequestSeek(uint tick) => _pendingSeek = tick;

        /// <summary>Seeks now. Returns false when the Replay refused the tick.</summary>
        public bool Seek(uint tick)
        {
            if (_replay == null) return false;
            var failure = _replay.Seek(tick, this);
            if (failure != null)
            {
                _overlay?.Banner(ReplayText.Describe(failure));
                return false;
            }
            if (_transport.State == ReplayTransportState.Ended) SetTransport(_transport.With(ReplayTransportState.Paused));
            SnapPausedPoses();
            return true;
        }

        public void CycleFocus(int direction)
        {
            if (_cast.Count == 0) return;
            int index = (_cast.IndexOf(_focus) + direction + _cast.Count) % _cast.Count;
            _focus = _cast[index];
            _playerRenderer = Fighters[_focus];
            SetupFocusHud();
            SetupCamera();
            _cameraMount?.Cut();
            RebuildFighterIndicators();
            // Course targets, events and the versus panel follow the followed fighter: show this tick from their side.
            if (_modes != null && _replay?.CurrentTick is { } now) Seek(now);
            _overlay?.Show(_frame.View?.Tick ?? _replay!.Range.First, _replay!.Range.First,
                _transport.State == ReplayTransportState.Playing, ReplayTransport.Label(_transport.Speed), FocusName());
        }

        public void SetSpeed(ReplaySpeed speed) => SetTransport(new ReplayTransport(_transport.State, speed));

        /// <summary>The one place Time.timeScale is written while a Replay runs.</summary>
        private void SetTransport(ReplayTransport transport)
        {
            _transport = transport;
            bool playing = transport.State == ReplayTransportState.Playing;
            Time.timeScale = transport.TimeScale;
            if (_brain != null) _brain.IgnoreTimeScale = !playing;
            foreach (var renderer in Fighters.Values)
                if (renderer != null) renderer.RenderInterpolation = playing;
            if (_frame.View != null)
                _overlay?.Show(_frame.View.Tick, _replay!.Range.First, playing, ReplayTransport.Label(transport.Speed), FocusName());
        }

        /// <summary>Paused presents do not advance Animancer, so the shown tick is sampled as a still pose.</summary>
        private void SnapPausedPoses()
        {
            if (_transport.State == ReplayTransportState.Playing || _replay == null) return;
            foreach (var pair in Fighters)
            {
                if (pair.Value == null) continue;
                var state = _replay.World.GetState(pair.Key);
                bool airborne = _replay.World.GetActiveMove(pair.Key)?.AirborneAtStart ?? !state.IsGrounded;
                int ticks = _poseTicks.TryGetValue(pair.Key, out var pose) ? pose.Ticks : 0;
                if (pair.Value.CanPlayScrubbedState(state, airborne)) pair.Value.PlayScrubbedState(state, airborne, ticks);
            }
        }

        private List<HUDManager.HudPlayer> HudPlayers()
            => _replay!.Cast.Select((m, i) => new HUDManager.HudPlayer(m.EntityId, $"P{i + 1}",
                m.Content.LegacySelector ?? CharacterClass.None, m.EntityId == _focus, ReplayText.Fighter(m), playerNumber: i + 1)).ToList();

        private void SetupFocusHud()
        {
            var focus = _replay!.Cast.First(m => m.EntityId == _focus);
            SetupHUD(HudPlayers(), StockLimit, focus.Content.Definition);
        }

        private string FocusName()
            => _replay?.Cast.FirstOrDefault(m => m.EntityId == _focus) is { } member ? ReplayText.Fighter(member) : "";

        private void UpdateLabels()
        {
            if (_overlay == null || _replay == null || _frame.View == null) return;
            var camera = _cameraMount?.RenderCamera ?? UnityEngine.Camera.main;
            for (int i = 0; i < _frame.Moves.Count; i++)
            {
                var move = _frame.Moves[i];
                if (!Fighters.TryGetValue(move.EntityId, out var renderer) || renderer == null) continue;
                var member = _replay.Cast.First(m => m.EntityId == move.EntityId);
                var parts = new List<string>(3) { ReplayText.Fighter(member) };
                if (member.Kind == ReplayCastKind.StandIn) parts.Add($"Stand-in for {member.Recorded.Package.PackageId}");
                if (move.RecordedName.Length > 0 && (move.Match == ReplayMoveMatch.Missing || _overlay.NamesShown))
                    parts.Add(move.Match == ReplayMoveMatch.Missing ? $"{move.RecordedName} (missing)" : move.RecordedName);
                float height = renderer.CharacterDef?.CapsuleHeight ?? 1.8f;
                _overlay.Label(move.EntityId, string.Join(" · ", parts), renderer.transform.position + Vector3.up * (height + 0.4f), camera);
            }
            if (_modes != null)
                foreach (var (key, text, position) in _modes.Labels) _overlay.Label(key, text, position, camera);
        }

        // ── Recorded observations ───────────────────────────────────────────────────

        public ushort? CooldownDuration(ulong entityId, byte wireSlot)
        {
            if (_frame.View == null || _replay == null) return null;
            foreach (var fighter in _frame.View.Fighters)
                if (fighter.EntityId == entityId) return _replay.CooldownDuration(fighter, wireSlot);
            return null;
        }

        public int? ChargeStepsReached(ulong entityId)
        {
            if (_frame.View == null) return null;
            foreach (var fighter in _frame.View.Fighters)
                if (fighter.EntityId == entityId) return fighter.ChargeStepsReached;
            return null;
        }

        /// <summary>
        /// A hit without its own presentation ID showed its move's presentation when the recording has a hit-contact
        /// event from its owner this tick. A hit with its own ID asks today's move, as live play does.
        /// </summary>
        private bool ShowedOwnPresentation(SpellResolver.HitResult hit)
        {
            if (!string.IsNullOrEmpty(hit.HitPresentationId) || _frame.View == null || _replay == null)
                return !string.IsNullOrEmpty(_replay?.World.GetDefinition(hit.OwnerEntityId)?.ForForm(hit.Form)
                    .GetCookedSlotAbility(hit.AttackSlot, hit.Airborne)?.HitPresentationId);
            foreach (var e in _frame.View.Events)
                if (e is ReplayPresentationEvent { SourceId: "hit-contact" } contact && contact.EntityId == hit.OwnerEntityId)
                    return true;
            return false;
        }

        // ── Leaving ─────────────────────────────────────────────────────────────────

        protected override void LeaveMatch() => ReplayLaunch.Return(_request?.ReturnTo ?? FrontendPage.Home);

        private void Fail(string reason)
        {
            SetTransport(_transport.With(ReplayTransportState.Paused));
            _overlay?.Banner(reason);
            Debug.LogWarning($"[Replay] {reason}");
        }

        protected override void OnDestroy()
        {
            Time.timeScale = 1f;
            if (_brain != null) _brain.IgnoreTimeScale = false;
            _overlay?.Dispose();
            _modes?.Dispose();
            if (_hudManager != null) _hudManager.Recorded = null;
            _replay?.Dispose();
            base.OnDestroy();
        }
    }
}
