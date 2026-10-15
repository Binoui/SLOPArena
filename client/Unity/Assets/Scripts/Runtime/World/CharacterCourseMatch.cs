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
using UnityEngine.InputSystem;

namespace SlopArena.Client.World
{
    /// <summary>Local course input and presentation. Shared owns hits, progress, respawns and the clock.</summary>
    public sealed class CharacterCourseMatch : MatchBase
    {
        public const string ArenaKey = "fightguy_sanctuary";
        public const string SceneName = "Arcade_FightGuyCourse";
        public static bool MovementTipsEnabled { get; set; }
#if UNITY_EDITOR
        public static bool FreezeOnStartForProbe { get; set; }
#endif
        private LocalSimulationBridge _bridge;
        protected override ISimulationBridge Bridge => _bridge;
        [SerializeField] private CombatFeedback _combatFeedback;
        [SerializeField] private ProjectileVFXManager _projectileVFX;
        private CharacterCourseDefinition _course;
        private Transform[] _targetViews;
        private Material _targetMaterial, _targetCoreMaterial;
        private CharacterCourseView _view;
        private ArcadeRecordKey _recordKey;
        private ArcadeRecordProvenance _provenance;
        private InputState? _probeInput;
        private bool _restarting, _wentLive, _banked;
        private int _countdownBeat = -1;
        public ServerSimulation Simulation => _bridge?.InternalSim;
        public bool Ready => _bridge != null && _targetViews != null && _view != null;
        public uint SimulationTick => Simulation?.SimulationTick ?? 0;
        public ulong? SavedBest { get; private set; }
        public ArcadeRecordUpdate LastRecordUpdate { get; private set; }
        public ArcadeRecordProvenance Provenance => _provenance;
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
            _bridge = new LocalSimulationBridge(arena, NoWinMatchRule.Instance);
            if (!SetupPlayerRenderer(entry, arena)) { enabled = false; return; }
            SpawnStageVisual(arena);
            var spawn = arena.SpawnPoints[0];
            _bridge.RegisterEntity(PlayerEntityId, _playerDef, new CharacterState
            {
                PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, AimYaw = spawn.Yaw,
                JumpsLeft = _playerDef.Movement.MaxJumps, AirDodgesLeft = 1,
            }, entry.BakedAnimation);
            _bridge.SetRespawnPosition(PlayerEntityId, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
            _playerRenderer.transform.SetPositionAndRotation(new(spawn.X, spawn.Y, spawn.Z), Quaternion.Euler(0, spawn.Yaw * Mathf.Rad2Deg, 0));
            if (_combatFeedback == null) _combatFeedback = gameObject.AddComponent<CombatFeedback>();
            _combatFeedback.SetSimulation(_bridge);
            if (_projectileVFX == null) _projectileVFX = gameObject.AddComponent<ProjectileVFXManager>();
            _projectileVFX.SetSimulation(Simulation);
            SetupHUD(_playerDef); SetupCamera(); SetupAimHandler(_playerDef);
            _inputController.RequireReleaseBeforeHumanInput();
            CreateTargets();
            _recordKey = new("character-course", arena.Arcade.ProfileId, entry.Identity.PackageId,
                ArcadeRecordCompatibility.Course(arena, entry));
            using var hash = SHA256.Create();
            string stageHash = BitConverter.ToString(hash.ComputeHash(ArenaBinaryFormat.Serialize(arena))).Replace("-", "").ToLowerInvariant();
            _provenance = new(entry.Identity.Version, entry.Identity.CookedContentHash, entry.Identity.PackageHash, stageHash);
            SavedBest = ArcadeRecordsStore.Instance.Book.Best(_recordKey);
            _view = new CharacterCourseView(_hudManager.Document.rootVisualElement, RestartAttempt, ReturnToArcade);
            Simulation.BeginCourseAttempt(ArcadeRecordCompatibility.CourseCountdownTicks);
#if UNITY_EDITOR
            ProbeFrozen = FreezeOnStartForProbe;
#endif
            PresentChallenge();
        }

        private void Update()
        {
            if (!Ready || _restarting) return;
            if (!IsPaused && !ChatInputGate.SuppressShortcuts && Keyboard.current?.backspaceKey.wasPressedThisFrame == true)
            { RestartAttempt(); return; }
            var challenge = Simulation.Course.Value;
            if (IsPaused || ProbeFrozen || challenge.Phase == CharacterCoursePhase.Countdown || challenge.Ended)
                _inputController.RequireReleaseBeforeHumanInput();
            else _inputController.Poll();
            if (challenge.Phase == CharacterCoursePhase.Completed && !IsPaused) _cameraMount?.SetMode(CameraMode.FreeCursor);
            _view.Present(challenge, IsPaused, SavedBest, LastRecordUpdate?.Improved ?? false, ArcadeRecordsStore.Instance.Feedback);
        }

        protected override void OnMatchFixedUpdate()
        {
            if (!Ready || IsPaused || ProbeFrozen || _restarting) return;
            if (Simulation.Course.Value.Ended) { ProbeTickCompleted?.Invoke(SimulationTick); return; }
            byte slot = _inputController.ConsumePendingSlotPress();
            var owner = _bridge.GetState(PlayerEntityId);
            var aim = _aimHandler != null ? _aimHandler.Evaluate(owner, slot, _playerDef, _inputController) : AimContext.None;
            _showCrosshair = _aimHandler?.ShowCrosshair ?? false;
            var camera = _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main;
            byte targetAlias = 0; float bestScreenDistance = float.MaxValue;
            for (int i = 0; i < _targetViews.Length; i++)
            {
                if (Simulation.IsCourseTargetBroken(i) || camera == null) continue;
                var p = _targetViews[i].position;
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
            _playerRenderer.ApplyServerState(_bridge.GetState(PlayerEntityId));
            _projectileVFX?.OnTick(); _combatFeedback?.OnTick(); _hudManager?.Refresh();
            UpdateSwordTrailsFromResolver(Simulation.Resolver); PresentTimelineEvents();
            PresentChallenge(); ProbeTickCompleted?.Invoke(SimulationTick);
        }

        private void PresentChallenge()
        {
            var challenge = Simulation.Course.Value;
            if (challenge.Phase == CharacterCoursePhase.Countdown)
            {
                int beat = (challenge.CountdownTicks + 59) / 60;
                if (beat != _countdownBeat) { _countdownBeat = beat; _hudManager.ShowSoloCountdown(beat.ToString(), 1f); }
            }
            else if (!_wentLive && challenge.Phase == CharacterCoursePhase.Running)
            {
                _wentLive = true; _inputController.RequireReleaseBeforeHumanInput(); _aimHandler?.ResetPresentation();
                _hudManager.ShowSoloCountdown("GO", .8f);
            }
            var owner = Simulation.GetState(PlayerEntityId); Transform selected = null; float radius = 0f;
            for (int i = 0; i < _targetViews.Length; i++)
            {
                bool broken = Simulation.IsCourseTargetBroken(i);
                _targetViews[i].gameObject.SetActive(!broken);
                if (!broken && owner.TargetEntityId == Simulation.CourseTargetEntityId(i))
                { selected = _targetViews[i]; radius = _course.Targets[i].Radius; }
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

        private void CreateTargets()
        {
            _targetMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _targetMaterial.SetColor("_BaseColor", new Color(1, .75f, .15f));
            _targetCoreMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _targetCoreMaterial.SetColor("_BaseColor", new Color(.82f, .18f, .18f));
            _targetViews = new Transform[_course.Targets.Count];
            for (int i = 0; i < _targetViews.Length; i++)
            {
                var target = _course.Targets[i];
                var view = GameObject.CreatePrimitive(PrimitiveType.Sphere); view.name = "Course Target " + target.Id;
                view.transform.SetParent(transform, false); view.transform.position = new(target.Position.X, target.Position.Y, target.Position.Z);
                view.transform.localScale = Vector3.one * target.Radius * 2f;
                Destroy(view.GetComponent<Collider>()); view.GetComponent<Renderer>().sharedMaterial = _targetMaterial;
                var core = GameObject.CreatePrimitive(PrimitiveType.Sphere); core.name = "Target Core";
                core.transform.SetParent(view.transform, false); core.transform.localScale = new(.72f, .72f, 1.02f);
                Destroy(core.GetComponent<Collider>()); core.GetComponent<Renderer>().sharedMaterial = _targetCoreMaterial;
                _targetViews[i] = view.transform;
            }
        }

        public void RestartAttempt()
        {
            if (!Ready || _restarting) return;
            _restarting = true; Simulation.StopCourse(); _inputController.RequireReleaseBeforeHumanInput();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(gameObject.scene.path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.path);
#endif
        }
        protected override void LeaveMatch() => ReturnToArcade();
        private void ReturnToArcade()
        { Simulation?.StopCourse(); MatchConfig.ClearActiveMatch(); MatchConfig.Mode = GameMode.Arcade; FrontendController.Show(FrontendPage.ArcadeSelect); }
        protected override void OnDestroy()
        {
            Simulation?.StopCourse(); _view?.Dispose();
            if (_targetMaterial != null) Destroy(_targetMaterial);
            if (_targetCoreMaterial != null) Destroy(_targetCoreMaterial);
            base.OnDestroy();
        }
    }
}
