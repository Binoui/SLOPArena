using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.Input;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// One Character Course offered by Arcade: its stage key, the menu and HUD title, how cues name
    /// the finish and optional movement tips (before any save, then after checkpoint 1, 2, …).
    /// The course's baked profile decides which fighters it admits.
    /// </summary>
    public sealed class CharacterCourseInfo
    {
        public string Key { get; }
        public string Title { get; }
        public string Finish { get; }
        public string[] Tips { get; }
        public CharacterCourseInfo(string key, string title, string finish, string[] tips)
        { Key = key; Title = title; Finish = finish; Tips = tips; }
        public string Tip(int savedCheckpoint)
            => Tips.Length == 0 ? "" : Tips[Math.Clamp(savedCheckpoint + 1, 0, Tips.Length - 1)];
    }

    /// <summary>Local course input and presentation. Shared owns hits, progress, respawns and the clock.</summary>
    public sealed class CharacterCourseMatch : MatchBase
    {
        public const string ArenaKey = "fightguy_sanctuary";
        /// <summary>Every course plays in this scene; the stage, fighter and look come from the course.</summary>
        public const string SceneName = "Arcade_FightGuyCourse";
        public static readonly CharacterCourseInfo[] Courses =
        {
            // A fighter plays the first listed course whose arena is baked and admits it
            // (ArcadeSelectController), so FightGuy gets the pier when it is baked and Sanctuary otherwise.
            new("fightguy_pier", "FIGHT NIGHT PIER", "the ring", new[]
            {
                "F wrecks the car in one go. R sweeps a row, E pops a stack, A reaches far balloons.",
                "Jump then R to the old pier through the balloon line, or hop the pilings and punch them.",
                "Jump then E up the wheel tower through the stack, or climb the gondolas. Then max out the punch machine.",
                "R sweeps the coaster straight. Ki Shot the prize buoy, F the heavy bag, then into the ring.",
            }),
            new(ArenaKey, "SANCTUARY COURSE", "the shrine", new[]
            {
                "Jump then R to cross the broken bridge, or use the pole detour. Any route counts.",
                "Jump then E to reach the upper terrace, or climb the staggered ledges.",
                "The summit shortcut combines Jump, E, then R. Islands offer another route.",
                "Press Down again while descending to FastFall after committed move motion ends.",
            }),
            new("manki_quarry", "KABOOM QUARRY", "the detonator", new[]
            {
                "Lob A bombs onto the islands, or jump over and punch. One blast breaks every crate it reaches.",
                "Bomb low rafts from above; rocket (R) the high stacks. Aim R a little high at long range.",
                "Jump then E up the blast face, or climb the benches on the left.",
                "Dive-bomb the crates below on your way down to the detonator.",
            }),
            new("wibou_mall", "NEO MALL", "the game store", new[]
            {
                "R cuts a row in one dash, A fans three kunai. E carries you over the closed skybridge, or hop the planters.",
                "Jump the missing escalator steps. Bosses take five hits: Skyfall lands two, close kunai three.",
                "Jump, jump, E up the elevator tower, or climb the hanging signs. Kunai the agents peeking over level 4.",
                "R the top bridge row, cut the agents at the railing, then get past Officer Doug to the game store.",
            }),
        };
        public static CharacterCourseInfo Course(string key)
            => Array.Find(Courses, c => c.Key == key) ?? new CharacterCourseInfo(key, "CHARACTER COURSE", "the finish", Array.Empty<string>());
        /// <summary>
        /// A retry starts with a short READY…GO instead of 3-2-1. The countdown never advances the
        /// simulation (the clock starts at Go), so it cannot change times or record compatibility.
        /// </summary>
        public const ushort RetryCountdownTicks = 60;
        public static bool MovementTipsEnabled { get; set; }
#if UNITY_EDITOR
        public static bool FreezeOnStartForProbe { get; set; }
#endif
        private LocalSimulationBridge _bridge;
        protected override ISimulationBridge Bridge => _bridge;
        [SerializeField] private CombatFeedback _combatFeedback;
        [SerializeField] private ProjectileVFXManager _projectileVFX;
        private CharacterCourseDefinition _course;
        private ArenaDefinition _arena;
        private BakedAnimationData _baked;
        private ArcadeRetryInput _retryInput;
        private CharacterCoursePresentation _presentation;
        private CharacterCourseView _view;
        private ArcadeRecordKey _recordKey;
        private ArcadeRecordProvenance _provenance;
        private InputState? _probeInput;
        private bool _wentLive, _banked, _quickStart;
        private int _countdownBeat = -1;
        public ServerSimulation Simulation => _bridge?.InternalSim;
        public bool Ready => _bridge != null && _presentation != null && _view != null;
        public uint SimulationTick => Simulation?.SimulationTick ?? 0;
        public ulong? SavedBest { get; private set; }
        public ArcadeRecordUpdate LastRecordUpdate { get; private set; }
        public ArcadeRecordProvenance Provenance => _provenance;
        /// <summary>Presentation-only readout for verification commands (look, clips, sounds played, cues).</summary>
        public object PresentationDiagnostics => _presentation?.Diagnostics;
        public event Action<uint> ProbeTickCompleted;
        public bool ProbeInputActive => _probeInput.HasValue;
        /// <summary>Explicit native probe boundary, like Training capture freeze; never set by gameplay.</summary>
        public bool ProbeFrozen { get; set; }
        public void SetProbeInput(InputState? input)
        { if (_probeInput.HasValue && !input.HasValue) _inputController?.RequireReleaseBeforeHumanInput(); _probeInput = input; }

        protected override void OnMatchStart()
        {
            string stage = MatchConfig.Mode == GameMode.Arcade ? MatchConfig.ArenaName : ArenaKey;
            string fighter = MatchConfig.Mode == GameMode.Arcade ? MatchConfig.PlayerPackageId : "fightguy";
            MatchConfig.ClearActiveMatch(); MatchConfig.Mode = GameMode.Arcade; MatchConfig.ArenaName = stage;
            string path = BakedContentPaths.ResolveArena(stage);
            var loaded = path == null ? null : ArenaBinaryFormat.LoadFromFile(path);
            if (loaded is not ArenaDefinition arena || arena.Arcade?.Course == null || arena.SpawnPoints.Length != 1)
            { Debug.LogError("[Course] A baked Character Course with one start is required: " + stage); enabled = false; return; }
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
            { Debug.LogError("[Course] Local content catalog failed: " + failure); enabled = false; return; }
            var entry = catalog.ResolvePackage(fighter);
            if (entry == null || !arena.Arcade.AllowsPackage(entry.Identity.PackageId))
            { Debug.LogError("[Course] This course does not admit package " + fighter); enabled = false; return; }
            ClientSession.InstallLocalMatchCatalog(catalog);
            if (entry.LegacySelector.HasValue) MatchConfig.PlayerClass = entry.LegacySelector.Value;
            MatchConfig.PlayerPackageId = entry.Identity.PackageId;
            _playerDef = entry.Definition; _course = arena.Arcade.Course;
            _arena = arena; _baked = entry.BakedAnimation;
            _bridge = new LocalSimulationBridge(arena, NoWinMatchRule.Instance);
            if (!SetupPlayerRenderer(entry, arena)) { enabled = false; return; }
            SpawnStageVisual(arena);
            RegisterFighter();
            if (_combatFeedback == null) _combatFeedback = gameObject.AddComponent<CombatFeedback>();
            _combatFeedback.SetSimulation(_bridge);
            if (_projectileVFX == null) _projectileVFX = gameObject.AddComponent<ProjectileVFXManager>();
            _projectileVFX.SetSimulation(Simulation);
            SetupHUD(_playerDef); SetupCamera(); SetupAimHandler(_playerDef);
            _inputController.RequireReleaseBeforeHumanInput();
            _recordKey = new("character-course", arena.Arcade.ProfileId, entry.Identity.PackageId,
                ArcadeRecordCompatibility.Course(arena, entry));
            using var hash = SHA256.Create();
            string stageHash = BitConverter.ToString(hash.ComputeHash(ArenaBinaryFormat.Serialize(arena))).Replace("-", "").ToLowerInvariant();
            _provenance = new(entry.Identity.Version, entry.Identity.CookedContentHash, entry.Identity.PackageHash, stageHash);
            SavedBest = ArcadeRecordsStore.Instance.Book.Best(_recordKey);
            _view = new CharacterCourseView(_hudManager.Document.rootVisualElement, Course(stage), RestartAttempt, ReturnToArcade);
            _presentation = new CharacterCoursePresentation(
                Resources.Load<CoursePresentationSet>($"{CoursePresentationSet.ResourcesFolder}/{stage}"),
                _course, transform, _view, _hudManager, () => ViewCamera);
            _retryInput = new ArcadeRetryInput();
            BeginAttempt(retry: false);
        }

        private void RegisterFighter()
        {
            var spawn = _arena.SpawnPoints[0];
            _bridge.RegisterEntity(PlayerEntityId, _playerDef, new CharacterState
            {
                PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, AimYaw = spawn.Yaw,
                JumpsLeft = _playerDef.Movement.MaxJumps, AirDodgesLeft = 1,
            }, _baked);
            _bridge.SetRespawnPosition(PlayerEntityId, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
            _playerRenderer.transform.SetPositionAndRotation(new(spawn.X, spawn.Y, spawn.Z), Quaternion.Euler(0, spawn.Yaw * Mathf.Rad2Deg, 0));
        }

        private void BeginAttempt(bool retry)
        {
            _wentLive = _banked = false; _countdownBeat = -1; LastRecordUpdate = null;
            _quickStart = retry;
            Simulation.BeginCourseAttempt(retry ? RetryCountdownTicks : ArcadeRecordCompatibility.CourseCountdownTicks);
#if UNITY_EDITOR
            ProbeFrozen = FreezeOnStartForProbe;
#endif
            PresentChallenge();
        }

        protected override Action PauseRetry => RestartAttempt;

        private void Update()
        {
            if (!Ready) return;
            bool retry = _retryInput.Poll(!IsPaused && !ChatInputGate.SuppressShortcuts);
            _view.PresentRetryHold(_retryInput.HoldProgress);
            if (retry) { RestartAttempt(); return; }
            var challenge = Simulation.Course.Value;
            if (IsPaused || ProbeFrozen || challenge.Phase == CharacterCoursePhase.Countdown || challenge.Ended)
                _inputController.RequireReleaseBeforeHumanInput();
            else _inputController.Poll();
            if (challenge.Phase == CharacterCoursePhase.Completed && !IsPaused) _cameraMount?.SetMode(CameraMode.FreeCursor);
            _presentation.Update();
            _view.Tick(Time.deltaTime, ViewCamera, challenge.Phase == CharacterCoursePhase.Completed);
            _view.Present(challenge, IsPaused, SavedBest, LastRecordUpdate?.Improved ?? false, ArcadeRecordsStore.Instance.Feedback);
        }

        private UnityEngine.Camera ViewCamera => _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main;

        protected override void OnMatchFixedUpdate()
        {
            if (!Ready || IsPaused || ProbeFrozen) return;
            if (Simulation.Course.Value.Ended) { ProbeTickCompleted?.Invoke(SimulationTick); return; }
            byte slot = _inputController.ConsumePendingSlotPress();
            var owner = _bridge.GetState(PlayerEntityId);
            var aim = _aimHandler != null ? _aimHandler.Evaluate(owner, slot, _playerDef, _inputController) : AimContext.None;
            _showCrosshair = _aimHandler?.ShowCrosshair ?? false;
            var camera = ViewCamera;
            var targets = _presentation.Targets;
            byte targetAlias = 0; float bestScreenDistance = float.MaxValue;
            for (int i = 0; i < targets.Count; i++)
            {
                if (Simulation.IsCourseTargetBroken(i) || camera == null) continue;
                var p = targets[i].position;
                var screen = camera.WorldToViewportPoint(p);
                float dx = p.x - owner.PX, dz = p.z - owner.PZ;
                float score = (screen.x - .5f) * (screen.x - .5f) + (screen.y - .5f) * (screen.y - .5f);
                if (screen.z <= 0 || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1
                    || dx * dx + dz * dz > 400f || score >= bestScreenDistance) continue;
                bestScreenDistance = score; targetAlias = Simulation.CourseTargetInputAlias(i);
            }
            var (input, _, _) = _inputController.BuildInputState(_cameraMount, _playerRenderer.transform.eulerAngles.y,
                isNPC: false, pendingSlotPress: slot, aimCtx: aim, canMove: null, targetEntityId: targetAlias);
            _bridge.Tick(new Dictionary<ulong, InputState> { [PlayerEntityId] = _probeInput ?? input });
            var fighter = _bridge.GetState(PlayerEntityId);
            _playerRenderer.ApplyServerState(fighter);
            _presentation.AfterTick(Simulation, fighter);
            _projectileVFX?.OnTick(); _combatFeedback?.OnTick(); _hudManager?.Refresh();
            UpdateSwordTrailsFromResolver(Simulation.Resolver); PresentTimelineEvents();
            PresentChallenge(); ProbeTickCompleted?.Invoke(SimulationTick);
        }

        private void PresentChallenge()
        {
            var challenge = Simulation.Course.Value;
            if (challenge.Phase == CharacterCoursePhase.Countdown)
            {
                int beat = _quickStart ? 0 : (challenge.CountdownTicks + 59) / 60;
                if (beat != _countdownBeat)
                {
                    _countdownBeat = beat;
                    if (_quickStart) _hudManager.ShowSoloCountdown("READY", RetryCountdownTicks / 60f);
                    else _hudManager.ShowSoloCountdown(beat.ToString(), 1f);
                }
            }
            else if (!_wentLive && challenge.Phase == CharacterCoursePhase.Running)
            {
                _wentLive = true; _inputController.RequireReleaseBeforeHumanInput(); _aimHandler?.ResetPresentation();
                _hudManager.ShowSoloCountdown("GO", .8f);
            }
            var owner = Simulation.GetState(PlayerEntityId); Transform selected = null; float radius = 0f;
            var targets = _presentation.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                bool broken = Simulation.IsCourseTargetBroken(i);
                targets[i].gameObject.SetActive(!broken);
                if (!broken && owner.TargetEntityId == Simulation.CourseTargetEntityId(i))
                { selected = targets[i]; radius = _course.Targets[i].Radius; }
            }
            _aimHandler?.UpdateObjectTargetPresentation(owner, selected, radius, null);
            if (owner.LockOn && selected != null) _cameraMount?.SetLockFocus(_playerRenderer.transform, selected.position);
            else _cameraMount?.ClearLockFocus(_playerRenderer.transform);
            if (challenge.Phase == CharacterCoursePhase.Completed && !_banked)
            {
                _banked = true;
                LastRecordUpdate = ArcadeRecordsStore.Instance.BankCourse(_recordKey, challenge, _provenance);
                SavedBest = LastRecordUpdate.PersonalBest;
                _inputController.RequireReleaseBeforeHumanInput(); _aimHandler?.ResetPresentation();
                _cameraMount?.SetMode(CameraMode.FreeCursor);
            }
            _view.Present(challenge, IsPaused, SavedBest, LastRecordUpdate?.Improved ?? false, ArcadeRecordsStore.Instance.Feedback);
        }

        /// <summary>
        /// A full fresh attempt without reloading the scene: a clean simulation (fighter, projectiles,
        /// queued effects, targets, checkpoint and clock), cleared presentation and a short READY…GO.
        /// The stage, fighter model, music and records stay loaded.
        /// </summary>
        public void RestartAttempt()
        {
            if (!Ready) return;
            Simulation.StopCourse();
            _bridge.Restart();
            RegisterFighter();
            _projectileVFX.SetSimulation(Simulation);
            ResetSimulationPresentation();
            _playerRenderer.ResetForFreshSimulation();
            _playerRenderer.ApplyServerState(_bridge.GetState(PlayerEntityId));
            _hudManager?.Refresh();
            _inputController.RequireReleaseBeforeHumanInput();
            _cameraMount?.ClearLockFocus(_playerRenderer.transform);
            _cameraMount?.SetMode(CameraMode.Normal);
            _cameraMount?.ResetView(_playerRenderer.transform);
            _cameraMount?.Cut();
            _view.ResetForRetry();
            _presentation.Reset();
            BeginAttempt(retry: true);
        }
        protected override void LeaveMatch() => ReturnToArcade();
        private void ReturnToArcade()
        { Simulation?.StopCourse(); MatchConfig.ClearActiveMatch(); MatchConfig.Mode = GameMode.Arcade; FrontendController.Show(FrontendPage.ArcadeSelect); }
        protected override void OnDestroy()
        {
            Simulation?.StopCourse(); _view?.Dispose(); _presentation?.Dispose();
            base.OnDestroy();
        }
    }
}
