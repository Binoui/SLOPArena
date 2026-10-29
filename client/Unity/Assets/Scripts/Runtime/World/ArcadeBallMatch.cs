using System;
using System.Collections.Generic;
using SlopArena.Client.Combat;
using SlopArena.Client.Camera;
using SlopArena.Client.Simulation;
using SlopArena.Client.UI;
using SlopArena.Client.Input;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.World
{
    /// <summary>Common local ball rendering/input; challenge authority stays in the Shared simulation.</summary>
    public abstract class ArcadeBallMatch : MatchBase
    {
        protected abstract string StageKey { get; }
        public const string FighterSessionKey = "SlopArena.ArcadeBallLab.Fighter";
        [SerializeField] private CharacterClass _characterOverride = CharacterClass.None;
        [SerializeField] private CombatFeedback _combatFeedback;
        [SerializeField] private ProjectileVFXManager _projectileVFX;
        private LocalSimulationBridge _bridge;
        protected override ISimulationBridge Bridge => _bridge;
        private Transform _ballView;
        private Material _ballMaterial;
        private LineRenderer _groundRing;
        private Material _groundMaterial;
        private float _ballFloorY;
        private float? _courtNetZ;
        private uint _tick, _attackContacts;
        private InputState? _probeInput;
        private ArcadeRetryInput _retryInput;
        private bool _restarting;
        private Renderer[] _closeCameraRenderers;
        private bool[] _originalForceRenderingOff;
        public ServerSimulation Simulation => _bridge?.InternalSim;
        public uint SimulationTick => _tick;
        public bool Ready => _bridge != null && _ballView != null && _playerDef != null;
        public bool ProbeInputActive => _probeInput.HasValue;
        public event Action<uint> ProbeTickCompleted;
        public void SetProbeInput(InputState? input)
        {
            if (_probeInput.HasValue && !input.HasValue) _inputController?.RequireReleaseBeforeHumanInput();
            _probeInput = input;
        }

        protected override void OnMatchStart()
        {
            MatchConfig.ClearActiveMatch();
            MatchConfig.Mode = GameMode.Arcade;
            MatchConfig.ArenaName = StageKey;
            string path = BakedContentPaths.ResolveArena(StageKey);
            var loaded = path != null ? ArenaBinaryFormat.LoadFromFile(path) : null;
            if (loaded is not ArenaDefinition arena || arena.Arcade == null || arena.SpawnPoints.Length != 1)
            {
                Debug.LogError("[Arcade] Prepare and bake this Arcade court before playing.");
                enabled = false;
                return;
            }
            arena = PrepareArena(arena);
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
            {
                Debug.LogError("[Arcade] Local content catalog failed: " + failure);
                enabled = false;
                return;
            }
            var selectedClass = _characterOverride != CharacterClass.None ? _characterOverride : MatchConfig.PlayerClass;
#if UNITY_EDITOR
            if (_characterOverride == CharacterClass.None && Enum.TryParse<CharacterClass>(
                    UnityEditor.SessionState.GetString(FighterSessionKey, selectedClass.ToString()), out var editorChoice))
                selectedClass = editorChoice;
#endif
            var entry = catalog.Resolve(selectedClass);
            if (entry == null || !arena.Arcade.AllowsPackage(entry.Identity.PackageId))
            {
                Debug.LogError("[Arcade] Fighter is not admitted by this court profile.");
                enabled = false;
                return;
            }
            ClientSession.InstallLocalMatchCatalog(catalog);
            MatchConfig.PlayerClass = selectedClass;
            MatchConfig.PlayerPackageId = entry.Identity.PackageId;
            _playerDef = entry.Definition;
            _bridge = new LocalSimulationBridge(arena, NoWinMatchRule.Instance);
            if (_combatFeedback == null) _combatFeedback = GetComponent<CombatFeedback>() ?? gameObject.AddComponent<CombatFeedback>();
            _combatFeedback.SetSimulation(_bridge);
            if (_projectileVFX == null) _projectileVFX = GetComponent<ProjectileVFXManager>() ?? gameObject.AddComponent<ProjectileVFXManager>();
            _projectileVFX.SetSimulation(_bridge.InternalSim);
            if (!SetupPlayerRenderer(entry, arena)) { enabled = false; return; }
            // Enclosed courts also need the baked stage's camera-only obstruction.
            SpawnStageVisual(arena);
            var spawn = arena.SpawnPoints[0];
            _bridge.RegisterEntity(PlayerEntityId, _playerDef, new CharacterState
            {
                PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, AimYaw = spawn.Yaw,
                JumpsLeft = _playerDef.ForForm(MatchConfig.PlayerForm).Movement.MaxJumps, AirDodgesLeft = 1,
                Form = new FormState { Active = MatchConfig.PlayerForm },
            }, entry.BakedAnimation);
            _bridge.SetRespawnPosition(PlayerEntityId, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
            _playerRenderer.transform.SetPositionAndRotation(new(spawn.X, spawn.Y, spawn.Z),
                Quaternion.Euler(0f, spawn.Yaw * Mathf.Rad2Deg, 0f));
            SetupHUD(_playerDef);
            SetupCamera(); SetupAimHandler(_playerDef);
            if (arena.Arcade.Slopball != null)
            {
                _closeCameraRenderers = _playerRenderer.GetComponentsInChildren<Renderer>(true);
                _originalForceRenderingOff = new bool[_closeCameraRenderers.Length];
                for (int i = 0; i < _closeCameraRenderers.Length; i++)
                    _originalForceRenderingOff[i] = _closeCameraRenderers[i].forceRenderingOff;
                RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
                RenderPipelineManager.endCameraRendering += EndCameraRendering;
            }
            _inputController.RequireReleaseBeforeHumanInput();
            _retryInput = new ArcadeRetryInput();
            _ballFloorY = arena.Arcade.Slopball?.FloorY ?? arena.Heightmap.Sample(spawn.X, spawn.Z);
            _courtNetZ = arena.Arcade.Slopball?.NetZ;
            CreateBallView(); PresentBall();
            OnBallMatchReady(entry, arena);
        }

        protected virtual void OnBallMatchReady(MatchContentEntry entry, ArenaDefinition arena) { }
        protected virtual void OnChallengeTick() { }
        protected virtual void OnAttemptStopped() { }
        /// <summary>The court a fresh attempt simulates, from the baked one (a local profile trial, say).</summary>
        protected virtual ArenaDefinition PrepareArena(ArenaDefinition arena) => arena;
        /// <summary>True when the subclass draws the ball and its ground cue; the base then keeps only a position anchor.</summary>
        protected virtual bool CustomBallPresentation => false;
        /// <summary>Follows the Shared ball each tick; aim selection and lock-on read it.</summary>
        protected Transform BallAnchor => _ballView;
        /// <summary>
        /// True while OnChallengeTick follows a simulation tick, so its LastTick* results are new. An
        /// ended attempt is presented again without ticking (and countdown ticks keep SimulationTick).
        /// </summary>
        protected bool SimulationAdvanced { get; private set; }

        private void BeginCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (camera != _cameraMount?.RenderCamera || _closeCameraRenderers == null) return;
            bool insideModel = false;
            for (int i = 0; i < _closeCameraRenderers.Length; i++)
            {
                var renderer = _closeCameraRenderers[i];
                if (renderer == null) continue;
                _originalForceRenderingOff[i] = renderer.forceRenderingOff;
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                var bounds = renderer.bounds;
                bounds.Expand((camera.nearClipPlane + .15f) * 2f);
                insideModel |= bounds.Contains(camera.transform.position);
            }
            // A wall can retract the camera into the local fighter. Hide its renderers
            // for this camera only, keeping the court readable without changing state.
            if (insideModel)
                foreach (var renderer in _closeCameraRenderers)
                    if (renderer != null) renderer.forceRenderingOff = true;
        }

        private void EndCameraRendering(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (camera != _cameraMount?.RenderCamera) return;
            RestoreCameraRenderers();
        }

        private void RestoreCameraRenderers()
        {
            if (_closeCameraRenderers == null) return;
            for (int i = 0; i < _closeCameraRenderers.Length; i++)
                if (_closeCameraRenderers[i] != null)
                    _closeCameraRenderers[i].forceRenderingOff = _originalForceRenderingOff[i];
        }

        protected virtual void Update()
        {
            if (!Ready || _restarting) return;
            bool retry = _retryInput.Poll(!IsPaused && !ChatInputGate.SuppressShortcuts);
            if (IsPaused) return;
            if (retry)
            {
                if (_bridge.InternalSim.Slopball.HasValue) { RestartAttempt(); return; }
                _bridge.InternalSim.ResetArcadeBall(); PresentBall();
            }
            if (_bridge.InternalSim.Slopball is SlopballState challenge && (challenge.Ended || challenge.Phase == SlopballPhase.Countdown))
            { _inputController.RequireReleaseBeforeHumanInput(); return; }
            _inputController.Poll();
        }

        protected override void OnMatchFixedUpdate()
        {
            if (!Ready || IsPaused || _restarting) return;
            if (_bridge.InternalSim.Slopball is SlopballState ended && ended.Ended)
            { SimulationAdvanced = false; OnChallengeTick(); ProbeTickCompleted?.Invoke(_tick); return; }
            byte slot = _inputController.ConsumePendingSlotPress();
            var owner = _bridge.GetState(PlayerEntityId);
            var aim = _aimHandler != null ? _aimHandler.Evaluate(owner, slot, _playerDef, _inputController, _bridge.GetActiveMove(PlayerEntityId)?.Slot) : AimContext.None;
            _showCrosshair = _aimHandler?.ShowCrosshair ?? false;
            var camera = _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main;
            byte target = 0;
            if (_bridge.InternalSim.ArcadeBall is ArcadeBallState ball && ball.Active && camera != null)
            {
                Vector3 point = new(ball.Position.X, ball.Position.Y, ball.Position.Z);
                Vector3 screen = camera.WorldToScreenPoint(point);
                float dx = point.x - owner.PX, dz = point.z - owner.PZ;
                if (screen.z > 0f && dx * dx + dz * dz <= 400f)
                    target = ServerSimulation.ArcadeBallTargetInputId;
            }
            var (input, _, _) = _inputController.BuildInputState(_cameraMount,
                _playerRenderer.transform.eulerAngles.y, pendingSlotPress: slot,
                aimCtx: aim, canMove: null, targetEntityId: target);
            _bridge.Tick(new Dictionary<ulong, InputState> { [PlayerEntityId] = _probeInput ?? input });
            _tick = _bridge.InternalSim.SimulationTick;
            _playerRenderer.ApplyServerState(_bridge.GetState(PlayerEntityId), _bridge.GetActiveMove(PlayerEntityId)?.Slot);
            PresentBall();
            foreach (var hit in _bridge.LastTickHits)
                if (hit.TargetEntityId == _bridge.InternalSim.ArcadeBall!.Value.EntityId) _attackContacts++;
            _projectileVFX?.OnTick();
            _combatFeedback?.OnTick();
            _hudManager?.Refresh();
            UpdateSwordTrailsFromResolver(_bridge.InternalSim.Resolver);
            PresentTimelineEvents();
            owner = _bridge.GetState(PlayerEntityId);
            var presented = _bridge.InternalSim.ArcadeBall!.Value;
            bool selected = presented.Active && owner.TargetEntityId == presented.EntityId;
            _aimHandler?.UpdateObjectTargetPresentation(owner, selected ? _ballView : null,
                presented.Radius, (ushort)presented.DamagePercent);
            _cameraMount?.SetCourt(_courtNetZ, presented.Active ? _ballView.position : null);
            if (owner.LockOn && selected) _cameraMount?.SetLockFocus(_playerRenderer.transform, _ballView.position);
            else _cameraMount?.ClearLockFocus(_playerRenderer.transform);
            SimulationAdvanced = true;
            OnChallengeTick();
            SimulationAdvanced = false;
            ProbeTickCompleted?.Invoke(_tick);
        }

        private void CreateBallView()
        {
            if (CustomBallPresentation)
            {
                var anchor = new GameObject("Arcade Ball Anchor");
                anchor.layer = LayerMask.NameToLayer("Ignore Raycast");
                anchor.transform.SetParent(transform, false);
                _ballView = anchor.transform;
                return;
            }
            var view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            view.name = "Arcade Ball Presentation";
            view.layer = LayerMask.NameToLayer("Ignore Raycast");
            view.transform.SetParent(transform, false);
            Destroy(view.GetComponent<Collider>());
            _ballView = view.transform;
            _ballMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _ballMaterial.SetColor("_BaseColor", new Color(1f, .76f, .12f));
            _ballMaterial.SetColor("_EmissionColor", new Color(.6f, .25f, .02f));
            _ballMaterial.EnableKeyword("_EMISSION");
            view.GetComponent<MeshRenderer>().sharedMaterial = _ballMaterial;
            if (_bridge.InternalSim.Slopball.HasValue)
            {
                var marker = new GameObject("Ball Ground Indicator");
                marker.transform.SetParent(transform, false);
                _groundRing = marker.AddComponent<LineRenderer>();
                _groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _groundMaterial.SetColor("_BaseColor", new Color(1f, .85f, .25f));
                _groundRing.sharedMaterial = _groundMaterial;
                _groundRing.useWorldSpace = false; _groundRing.loop = true;
                _groundRing.positionCount = 48;
                _groundRing.startWidth = _groundRing.endWidth = .06f;
                _groundRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _groundRing.receiveShadows = false;
                float radius = _bridge.InternalSim.ArcadeBall.Value.Radius;
                for (int i = 0; i < _groundRing.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2f / _groundRing.positionCount;
                    _groundRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
                }
            }
        }
        private void PresentBall()
        {
            var ball = _bridge.InternalSim.ArcadeBall!.Value;
            _ballView.gameObject.SetActive(ball.Active);
            _ballView.position = new(ball.Position.X, ball.Position.Y, ball.Position.Z);
            _ballView.localScale = Vector3.one * (ball.Radius * 2f);
            if (_groundRing != null)
            {
                _groundRing.gameObject.SetActive(ball.Active);
                // This is the ball's footprint on the baked flat court, not a physics
                // query or a promised future landing position after another rebound.
                _groundRing.transform.position = new(ball.Position.X, _ballFloorY + .035f, ball.Position.Z);
            }
        }

        protected override void OnGUI()
        {
            base.OnGUI();
            if (Ready && !IsPaused && !_restarting) DrawChallengeHUD(_bridge.InternalSim.ArcadeBall!.Value);
        }

        protected abstract void DrawChallengeHUD(ArcadeBallState ball);
        protected uint AttackContacts => _attackContacts;
        protected string FighterName => _playerDef.DisplayName;

        public void RestartAttempt()
        {
            if (_restarting || !Ready || !_bridge.InternalSim.Slopball.HasValue) return;
            _restarting = true;
            _bridge.InternalSim.StopSlopball();
            OnAttemptStopped();
            _inputController.RequireReleaseBeforeHumanInput();
            // Reloading the dedicated scene destroys its fighter, resolver, queued effects and
            // presentation together. The challenge captures its earned result before unload.
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(gameObject.scene.path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.path);
#endif
        }

        protected override void LeaveMatch()
        {
            _bridge?.InternalSim.StopSlopball();
            MatchConfig.ClearActiveMatch();
            FrontendController.Show(FrontendPage.Home);
        }
        protected override void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RestoreCameraRenderers();
            base.OnDestroy();
            if (_ballMaterial != null) Destroy(_ballMaterial);
            if (_groundMaterial != null) Destroy(_groundMaterial);
        }
    }
}
