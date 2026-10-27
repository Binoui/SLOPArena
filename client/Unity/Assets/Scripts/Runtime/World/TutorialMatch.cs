using System;
using System.Collections.Generic;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Guided lessons on a local simulation. Shared owns every hit, launch and respawn; the
    /// checklist only observes results, and the dummy is an ordinary fighter fed scripted input.
    /// </summary>
    public sealed class TutorialMatch : MatchBase
    {
        public const string SceneName = "Tutorial";
        public const ulong DummyEntityId = 100;
        /// <summary>Pause between a passed lesson and the next one, so the stamp reads.</summary>
        private const int LessonGapTicks = 90;
        public static TutorialStageLayout Layout => TutorialCurriculum.LicenseTest;
        /// <summary>True once any tutorial run on this machine has reached the end card.</summary>
        public static bool CompletedOnce => TutorialView.HasCompleted;

        [SerializeField] private CombatFeedback _combatFeedback;
        [SerializeField] private ProjectileVFXManager _projectileVFX;
        private LocalSimulationBridge _bridge;
        protected override ISimulationBridge Bridge => _bridge;
        private ArenaDefinition _arena;
        private MatchContentEntry _playerEntry, _dummyEntry;
        private PlayerRenderer _dummyRenderer;
        private TutorialView _view;
        private InputState? _probeInput;
        private uint _tick;
        private int _nextLessonTicks = -1;
        private float _cameraYaw;
        private bool _hasCameraYaw, _finished;

        public TutorialProgress Progress { get; private set; }
        public ServerSimulation Simulation => _bridge?.InternalSim;
        public bool Ready => Progress != null && _view != null;
        public bool Finished => _finished;
        public uint SimulationTick => _tick;
        public event Action<uint> ProbeTickCompleted;
        public bool ProbeInputActive => _probeInput.HasValue;
        public void SetProbeInput(InputState? input)
        { if (_probeInput.HasValue && !input.HasValue) _inputController?.RequireReleaseBeforeHumanInput(); _probeInput = input; }

        protected override void OnMatchStart()
        {
            MatchConfig.ClearActiveMatch();
            MatchConfig.Mode = GameMode.Tutorial;
            MatchConfig.ArenaName = Layout.ArenaKey;
            string path = BakedContentPaths.ResolveArena(Layout.ArenaKey);
            var loaded = path == null ? null : ArenaBinaryFormat.LoadFromFile(path);
            if (loaded is not ArenaDefinition arena || arena.SpawnPoints == null || arena.SpawnPoints.Length < 2)
            { Debug.LogError("[Tutorial] A baked tutorial stage with two spawns is required: " + Layout.ArenaKey); enabled = false; return; }
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
            { Debug.LogError("[Tutorial] Local content catalog failed: " + failure); enabled = false; return; }
            // Fighter Select records the class; the package id is only a fallback.
            var entry = catalog.Resolve(MatchConfig.PlayerClass) ?? catalog.ResolvePackage(MatchConfig.PlayerPackageId);
            if (entry == null) { Debug.LogError("[Tutorial] No fighter package for " + MatchConfig.PlayerClass); enabled = false; return; }
            // A different, roomy fighter stands in as the dummy so the two never look alike.
            _dummyEntry = catalog.Resolve(entry.Definition.Class == CharacterClass.Bonk ? CharacterClass.FightGuy : CharacterClass.Bonk) ?? entry;
            ClientSession.InstallLocalMatchCatalog(catalog);
            if (entry.LegacySelector.HasValue) MatchConfig.PlayerClass = entry.LegacySelector.Value;
            MatchConfig.PlayerPackageId = entry.Identity.PackageId;
            _arena = arena; _playerEntry = entry; _playerDef = entry.Definition;
            _bridge = new LocalSimulationBridge(arena, NoWinMatchRule.Instance);
            if (_combatFeedback == null) _combatFeedback = GetComponent<CombatFeedback>() ?? gameObject.AddComponent<CombatFeedback>();
            _combatFeedback.SetSimulation(_bridge);
            if (_projectileVFX == null) _projectileVFX = GetComponent<ProjectileVFXManager>() ?? gameObject.AddComponent<ProjectileVFXManager>();
            _projectileVFX.SetSimulation(Simulation);
            _dummyRenderer = new GameObject("Tutorial Dummy").AddComponent<PlayerRenderer>();
            if (!SetupPlayerRenderer(entry, arena) || !SetupRenderer(_dummyRenderer, _dummyEntry, arena, DummyEntityId, false))
            { enabled = false; return; }
            SpawnStageVisual(arena);
            Place(PlayerEntityId, entry, _playerRenderer, arena.SpawnPoints[0], 0, 0);
            Place(DummyEntityId, _dummyEntry, _dummyRenderer, arena.SpawnPoints[1], 0, 0);
            SetupHUD(_playerDef, new List<HUDManager.HudPlayer>
                { new(DummyEntityId, "DUMMY", _dummyEntry.Definition.Class, isLocal: false) });
            SetupCamera(); SetupAimHandler(_playerDef);
            _inputController.RequireReleaseBeforeHumanInput();
            Progress = new TutorialProgress(Layout, _playerDef.CapsuleHeight);
            _view = new TutorialView(_hudManager.Document.rootVisualElement, _playerDef, Layout, SkipLesson, RestartLesson,
                OpenTraining, LeaveMatch);
            BeginLesson();
        }

        private void Update()
        {
            if (!Ready) return;
            if (IsPaused || _finished) _inputController.RequireReleaseBeforeHumanInput();
            else _inputController.Poll();
            _view.Present(Progress, IsPaused, _finished, _nextLessonTicks >= 0);
        }

        protected override void OnMatchFixedUpdate()
        {
            if (!Ready || IsPaused) return;
            byte slot = _inputController.ConsumePendingSlotPress();
            var playerBefore = _bridge.GetState(PlayerEntityId);
            var dummyBefore = _bridge.GetState(DummyEntityId);
            var aim = _aimHandler != null ? _aimHandler.Evaluate(playerBefore, slot, _playerDef, _inputController, _bridge.GetActiveMove(PlayerEntityId)?.Slot) : AimContext.None;
            _showCrosshair = _aimHandler?.ShowCrosshair ?? false;
            var camera = _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main;
            byte target = PickScreenTarget(new[] { _dummyRenderer }, camera);
            var (input, _, _) = _inputController.BuildInputState(_cameraMount, _playerRenderer.transform.eulerAngles.y,
                pendingSlotPress: slot, aimCtx: aim, canMove: null, targetEntityId: target);
            if (_finished) input = default;
            var dummyInput = TutorialDummy.Build(Progress.DummyBehavior, dummyBefore, playerBefore, _tick);
            _bridge.Tick(new Dictionary<ulong, InputState> { [PlayerEntityId] = _probeInput ?? input, [DummyEntityId] = dummyInput });
            _tick++;

            float yaw = _cameraMount != null ? _cameraMount.GetCameraYawDeg() : 0f;
            float turned = _hasCameraYaw ? Mathf.Abs(Mathf.DeltaAngle(_cameraYaw, yaw)) : 0f;
            _cameraYaw = yaw; _hasCameraYaw = true;
            int completed = Progress.Observe(new TutorialFrame(playerBefore, _bridge.GetState(PlayerEntityId), dummyBefore,
                _bridge.GetState(DummyEntityId), _bridge.LastTickHits, turned));
            if (completed != 0) _view.ObjectivesCompleted(completed);
            if (Progress.LessonComplete && _nextLessonTicks < 0 && !_finished)
            {
                _nextLessonTicks = LessonGapTicks;
                _view.ShowStamp(Progress);
            }
            else if (_nextLessonTicks > 0 && --_nextLessonTicks == 0)
            {
                _nextLessonTicks = -1;
                if (Progress.StartNextLesson()) BeginLesson(); else Finish();
            }
            Present();
            ProbeTickCompleted?.Invoke(_tick);
        }

        private void Present()
        {
            var player = _bridge.GetState(PlayerEntityId);
            var dummy = _bridge.GetState(DummyEntityId);
            _playerRenderer.ApplyServerState(player, _bridge.GetActiveMove(PlayerEntityId)?.Slot);
            _dummyRenderer.ApplyServerState(dummy, _bridge.GetActiveMove(DummyEntityId)?.Slot);
            _projectileVFX?.OnTick(); _combatFeedback?.OnTick(); _hudManager?.Refresh();
            UpdateSwordTrailsFromResolver(Simulation.Resolver); PresentTimelineEvents();
            bool targeted = player.TargetEntityId == DummyEntityId;
            _aimHandler?.UpdateTargetPresentation(player, targeted ? _dummyRenderer : null, targeted ? dummy.DamagePercent : (ushort)0);
            if (player.LockOn && targeted) _cameraMount?.SetLockFocus(_playerRenderer.transform, _dummyRenderer.transform.position);
            else _cameraMount?.ClearLockFocus(_playerRenderer.transform);
        }

        /// <summary>Lesson setup: fresh cooldowns (or none, for practice), and the dummy placed as the lesson needs.</summary>
        private void BeginLesson()
        {
            var lesson = Progress.Lesson;
            if (lesson.DummySetup == TutorialDummySetup.Reset)
                Place(DummyEntityId, _dummyEntry, _dummyRenderer, _arena.SpawnPoints[1], lesson.DummyPercent,
                    _bridge.GetState(DummyEntityId).Deaths);
            else if (lesson.DummySetup == TutorialDummySetup.KnockoutReady)
                Place(DummyEntityId, _dummyEntry, _dummyRenderer, new SpawnPoint
                    { X = Layout.KnockoutX, Y = Layout.FloorHeight, Z = Layout.KnockoutZ, Yaw = Layout.KnockoutYaw },
                    lesson.DummyPercent, _bridge.GetState(DummyEntityId).Deaths);
            _bridge.InternalSim.NoCooldownsEntityId = lesson.NoCooldowns ? PlayerEntityId : (ulong?)null;
            var player = _bridge.GetState(PlayerEntityId);
            for (byte slot = 1; slot <= AbilitySlots.Count; slot++) player.SetCooldown(slot, 0);
            _bridge.InternalSim.SetState(PlayerEntityId, player);
            _view.ShowLesson(Progress);
            _hudManager?.Refresh();
        }

        /// <summary>Register a fighter fresh at a spot (floor-snapped), keeping its life count.</summary>
        private void Place(ulong id, MatchContentEntry entry, PlayerRenderer renderer, SpawnPoint spot, ushort percent, byte deaths)
        {
            var def = entry.Definition;
            float y = spot.Y <= Layout.FloorHeight + 0.01f ? Layout.FloorHeight + def.CapsuleHeight * 0.5f : spot.Y;
            _bridge.InternalSim.RemoveEntity(id);
            _bridge.RegisterEntity(id, def, new CharacterState
            {
                PX = spot.X, PY = y, PZ = spot.Z, FacingYaw = spot.Yaw, AimYaw = spot.Yaw, IsGrounded = true,
                JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1, DamagePercent = percent, Deaths = deaths,
            }, entry.BakedAnimation);
            if (id == DummyEntityId)
            {
                var home = _arena.SpawnPoints[1];
                _bridge.SetRespawnPosition(id, home.X, home.Y, home.Z, home.Yaw);
            }
            else _bridge.SetRespawnPosition(id, spot.X, y, spot.Z, spot.Yaw);
            renderer.ResetAnimationState();
            renderer.transform.SetPositionAndRotation(new(spot.X, y, spot.Z), Quaternion.Euler(0, spot.Yaw * Mathf.Rad2Deg, 0));
            renderer.ApplyServerState(_bridge.GetState(id), _bridge.GetActiveMove(id)?.Slot);
            _projectileVFX?.ClearHeldVisual(id);
        }

#if UNITY_EDITOR
        /// <summary>Native probe only: register the player fresh on a surface, keeping its damage and lives.</summary>
        public void PlacePlayerForProbe(float x, float surfaceY, float z, float yaw)
        {
            if (!Ready) throw new InvalidOperationException("The tutorial is not ready.");
            var before = _bridge.GetState(PlayerEntityId);
            Place(PlayerEntityId, _playerEntry, _playerRenderer, new SpawnPoint
                { X = x, Y = surfaceY + _playerDef.CapsuleHeight * 0.5f, Z = z, Yaw = yaw }, before.DamagePercent, before.Deaths);
            var home = _arena.SpawnPoints[0];
            _bridge.SetRespawnPosition(PlayerEntityId, home.X, home.Y, home.Z, home.Yaw);
        }

        /// <summary>Native probe only: restart the checklist at a lesson, optionally returning the player to spawn.</summary>
        public void JumpToLessonForProbe(int index, bool resetPlayer)
        {
            if (!Ready || _finished || index < 0 || index >= Progress.Lessons.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Choose a lesson of an unfinished tutorial.");
            if (resetPlayer)
            {
                Place(PlayerEntityId, _playerEntry, _playerRenderer, _arena.SpawnPoints[0], 0, _bridge.GetState(PlayerEntityId).Deaths);
            }
            Progress = new TutorialProgress(Layout, _playerDef.CapsuleHeight);
            while (Progress.LessonIndex < index) Progress.StartNextLesson();
            _nextLessonTicks = -1;
            BeginLesson();
        }
#endif

        private void SkipLesson()
        {
            if (!Ready || _finished || Progress.LessonComplete) return;
            Progress.SkipLesson();
            _nextLessonTicks = 1;
        }

        private void RestartLesson()
        {
            if (!Ready || _finished) return;
            _nextLessonTicks = -1;
            Progress.RestartLesson();
            BeginLesson();
        }

        private void Finish()
        {
            _finished = true;
            TutorialView.MarkCompleted();
            _inputController.RequireReleaseBeforeHumanInput();
            _aimHandler?.ResetPresentation();
            _cameraMount?.SetMode(CameraMode.FreeCursor);
            _view.ShowFinished();
        }

        private void OpenTraining()
        {
            MatchConfig.Mode = GameMode.Training;
            FrontendController.Show(FrontendPage.FighterSelect);
        }

        protected override void LeaveMatch() => FrontendController.Show(FrontendPage.Home);

        protected override void OnDestroy()
        {
            _view?.Dispose();
            if (_cameraMount != null && _finished) _cameraMount.SetMode(CameraMode.Normal);
            base.OnDestroy();
        }
    }
}
