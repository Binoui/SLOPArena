using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using SlopArena.Shared;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;

using SlopArena.Client.Combat;
namespace SlopArena.Client.Tools
{
    /// Ability Lab rig: frame-by-frame preview of hurtboxes + hitboxes for the selected
    /// legacy catalog entry or an in-memory cooked Character Package. Poses come from
    /// the baked skeleton through the same Shared resolvers used by the server.
    ///
    /// Package source ownership, typed DTO editing, hashes, persistence, and cooking live
    ///
        /// ExecuteAlways: the orbit camera and verified package preview work in edit mode.
    [ExecuteAlways]
    public class AbilityLab : MonoBehaviour
    {
        public const float TickRate = 60f; // sim ticks per second (matches bake sample rate)
        public enum AuthoringPhase { Charge, Fire }

        /// <summary>Fixed Ability Lab slot order: 1, 2, 3, 4, A, E, R, F.</summary>
        public static readonly int[] SlotIndices = { 2, 6, 7, 8, 10, 3, 4, 5 };
        public static readonly string[] SlotNames = { "1", "2", "3", "4", "A", "E", "R", "F" };

        public static AbilityLab Instance { get; private set; }
        public readonly struct TimelineCursor
        {
            internal readonly string SlotId;
            internal readonly int Stage;
            internal readonly ushort Tick;
            internal readonly int SelectedHitbox;
            internal readonly bool Playing;
            internal readonly float PlayAccumulator;
            internal readonly AbilityLabScenarioResult Scenario;
            internal readonly int ScenarioFrame;
            internal readonly bool ShowDummy, ShowHitboxes, ShowHurtboxes, ShowBakedBones, ShowTrajectory;
            internal readonly bool PriorShowDummy;
            internal readonly bool PhasePreviewActive;
            internal readonly AuthoringPhase Phase;
            internal readonly int PhaseTick;

            internal TimelineCursor(string slotId, int stage, ushort tick, int selectedHitbox, bool playing, float playAccumulator,
                AbilityLabScenarioResult scenario, int scenarioFrame, bool showDummy, bool showHitboxes,
                bool showHurtboxes, bool showBakedBones, bool showTrajectory, bool priorShowDummy,
                bool phasePreviewActive, AuthoringPhase phase, int phaseTick)
            {
                SlotId = slotId;
                Stage = stage;
                Tick = tick;
                SelectedHitbox = selectedHitbox;
                Playing = playing;
                PlayAccumulator = playAccumulator;
                Scenario = scenario;
                ScenarioFrame = scenarioFrame;
                ShowDummy = showDummy;
                ShowHitboxes = showHitboxes;
                ShowHurtboxes = showHurtboxes;
                ShowBakedBones = showBakedBones;
                ShowTrajectory = showTrajectory;
                PriorShowDummy = priorShowDummy;
                PhasePreviewActive = phasePreviewActive;
                Phase = phase;
                PhaseTick = phaseTick;
            }
        }

        public readonly struct CameraState
        {
            internal readonly UnityEngine.Camera BoundCamera;
            internal readonly UnityEngine.Camera ObservedCamera;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            internal readonly RenderTexture Target;
            internal readonly float Aspect;
            internal readonly Vector2 OrbitAngles;
            internal readonly float OrbitDistance;
            internal readonly Vector3 OrbitPivot;

            internal CameraState(
                UnityEngine.Camera boundCamera,
                UnityEngine.Camera observedCamera,
                Vector3 position,
                Quaternion rotation,
                RenderTexture target,
                float aspect,
                Vector2 orbitAngles,
                float orbitDistance,
                Vector3 orbitPivot)
            {
                BoundCamera = boundCamera;
                ObservedCamera = observedCamera;
                Position = position;
                Rotation = rotation;
                Target = target;
                Aspect = aspect;
                OrbitAngles = orbitAngles;
                OrbitDistance = orbitDistance;
                OrbitPivot = orbitPivot;
            }
        }

        public TimelineCursor CaptureTimelineCursor()
            => new(SelectedSlotId, StageIndex, Tick, SelectedHitboxEventIndex, Playing, _playAccum,
                Scenario, ScenarioFrame, ShowDummy, ShowHitboxes, ShowHurtboxes, ShowBakedBones, ShowTrajectory,
                _scenarioPriorShowDummy, PhasePreviewActive, Phase, PhaseTick);

        public void RestoreTimelineCursor(TimelineCursor cursor)
        {
            Playing = false;
            if (CanonicalSlotProjection.TryGet(cursor.SlotId, out var address))
            {
                int index = Array.IndexOf(SlotNames, address.InputLabel);
                SelectedSlotId = address.Id;
                Airborne = address.IsAirborne;
                SlotIndex = SlotIndices[index];
            }
            StageIndex = cursor.Stage;
            Tick = cursor.Tick;
            SelectedHitboxEventIndex = cursor.SelectedHitbox;
            Scenario = cursor.Scenario;
            ScenarioFrame = cursor.ScenarioFrame;
            ShowDummy = cursor.ShowDummy;
            ShowHitboxes = cursor.ShowHitboxes;
            ShowHurtboxes = cursor.ShowHurtboxes;
            ShowBakedBones = cursor.ShowBakedBones;
            ShowTrajectory = cursor.ShowTrajectory;
            PhasePreviewActive = cursor.PhasePreviewActive;
            Phase = cursor.Phase;
            _phaseTick = cursor.PhaseTick;
            _scenarioPriorShowDummy = cursor.PriorShowDummy;
            RebuildScenarioTrailHistory();
            RefreshPose();
            _playAccum = cursor.PlayAccumulator;
            Playing = cursor.Playing;
        }


        public CameraState CaptureCameraState()
        {
            UnityEngine.Camera observed = _camera;
            return new CameraState(
                _camera,
                observed,
                observed != null ? observed.transform.position : default,
                observed != null ? observed.transform.rotation : default,
                observed != null ? observed.targetTexture : null,
                observed != null ? observed.aspect : 0f,
                _orbitAngles,
                _orbitDistance,
                _orbitPivot);
        }

        public void RestoreCameraState(CameraState state)
        {
            UnityEngine.Camera current = _camera;
            if (state.BoundCamera == null && current != null && current != state.ObservedCamera &&
                current.transform.parent == transform)
            {
#if UNITY_EDITOR
                DestroyImmediate(current.gameObject);
#else
                Destroy(current.gameObject);
#endif
            }
            _camera = state.BoundCamera;
            _orbitAngles = state.OrbitAngles;
            _orbitDistance = state.OrbitDistance;
            _orbitPivot = state.OrbitPivot;
            if (state.ObservedCamera != null)
            {
                state.ObservedCamera.transform.SetPositionAndRotation(state.Position, state.Rotation);
                state.ObservedCamera.targetTexture = state.Target;
                if (state.Aspect > 0f) state.ObservedCamera.aspect = state.Aspect;
            }
        }

        public UnityEngine.Camera PreviewCamera => _camera;

        // ── Selection state ──
        public CharacterClass Character { get; private set; } = CharacterClass.None;
        public string SelectedPackageId { get; private set; } = "";
        public string SelectedPackageHash { get; private set; } = "";
        public string SelectedSlotId { get; private set; } = "";
        private bool _packagePreviewAvailable;
        public bool IsPackagePreview => _packagePreviewAvailable && !string.IsNullOrEmpty(SelectedPackageId);
        public int SlotIndex { get; private set; }
        public bool Airborne { get; private set; }
        public int StageIndex { get; private set; }
        public ushort Tick { get; private set; }
        public bool PhasePreviewActive { get; private set; }
        public AuthoringPhase Phase { get; private set; }
        private int _phaseTick;
        public int PhaseTick => _phaseTick;
        public bool CanPreviewCharge => TryResolvePhaseAnimation(AuthoringPhase.Charge, out _, out _, out _, out _);
        public string PhaseClipName => TryResolvePhaseAnimation(Phase, out _, out AnimationClip clip, out _, out _)
            ? clip.name : string.Empty;
        public int PhaseDurationTicks => TryResolvePhaseAnimation(Phase, out _, out _, out int duration, out _)
            ? duration : 0;
        public string PhasePreviewStatus
        {
            get
            {
                if (!IsPackagePreview) return "Phase preview requires a package preview.";
                if (Scenario != null) return "Phase preview is separate from recorded scenarios.";
                if (TryResolvePhaseAnimation(Phase, out _, out _, out _, out _)) return "Ready";
                return Phase == AuthoringPhase.Charge
                    ? "Charge preview unavailable: the selected move has no resolved aim clip."
                    : "Fire preview unavailable: the selected move has no resolved release clip.";
            }
        }
        /// <summary>Select isolated Charge/Fire pose authoring without changing recorded scenario state.</summary>
        public bool SetPhasePreview(bool active)
        {
            if (Scenario != null)
                return false;
            if (active && !TryResolvePhaseAnimation(Phase, out _, out _, out _, out _))
                return false;
            PhasePreviewActive = active;
            _phaseTick = Mathf.Clamp(_phaseTick, 0, Mathf.Max(0, PhaseDurationTicks - 1));
            Playing = false;
            RefreshPose();
            return true;
        }

        public void SetAuthoringPhase(AuthoringPhase phase)
        {
            if (Scenario != null || Phase == phase) return;
            Phase = phase;
            _phaseTick = 0;
            Playing = false;
            if (PhasePreviewActive && !TryResolvePhaseAnimation(phase, out _, out _, out _, out _))
                PhasePreviewActive = false;
            RefreshPose();
        }

        public void SetPhaseTick(int tick)
        {
            if (Scenario != null || !PhasePreviewActive) return;
            int duration = PhaseDurationTicks;
            if (duration <= 0) return;
            int bounded = Mathf.Clamp(tick, 0, duration - 1);
            if (_phaseTick == bounded) return;
            _phaseTick = bounded;
            RefreshPose();
        }

        /// <summary>Inject or clear the transient weapon config used by the Lab's preview actor.</summary>
        public void SetWeaponAttachConfigOverride(WeaponAttachConfig config)
        {
            if (_weaponAttachConfigOverride == config)
            {
                RefreshPose();
                return;
            }
            _weaponAttachConfigOverride = config;
            if (_previewRenderer != null && DisplayDef != null)
                _weaponAttach = AttachWeapon(_previewRenderer, DisplayDef);
            RefreshPose();
        }

        private bool TryResolvePhaseAnimation(
            AuthoringPhase phase,
            out string clipName,
            out AnimationClip clip,
            out int durationTicks,
            out float playbackSpeed)
        {
            clipName = null;
            clip = null;
            durationTicks = 0;
            playbackSpeed = 1f;
            return IsPackagePreview && Renderer != null
                && Renderer.TryGetAbilityPhaseAnimation(
                    (byte)(SlotIndex + 1), Airborne, StageIndex,
                    phase == AuthoringPhase.Charge,
                    out clipName, out clip, out durationTicks, out playbackSpeed);
        }
        public int SelectedHitboxEventIndex { get; private set; } = -1;
        public bool Playing
        {
            get => _playing;
            set { _playing = value; _previewPlaybackClock = PreviewClock(); }
        }
        private bool _playing;
        private double _previewPlaybackClock;
        public float PlaySpeed { get; set; } = 1f;
        public float FacingYaw { get; set; }
        public bool ShowHurtboxes { get; set; }
        public bool ShowHitboxes { get; set; } = true;
        public bool ShowBakedBones { get; set; }
        public bool ShowDummy { get; set; }
        public float DummyDistance { get; set; } = 2.5f;

        // ── Knockback trajectory preview ──
        /// <summary>Draw the knockback arc for the selected hitbox on the dummy (opt-in).</summary>
        public bool ShowTrajectory { get; set; }
        /// <summary>Victim damage % used for the trajectory preview (shape is %-dependent).</summary>
        public float TrajectoryPercent { get; set; } = 0f;
        /// <summary>Hitbox index (into CurrentWorkingEvents) whose knockback the preview draws.</summary>
        public int PreviewHitboxIndex { get; set; }
        /// <summary>Last computed arc: (world pos, phase 'H'=hitstun 'F'=flight 'A'=apex 'G'=landing).</summary>
        public IReadOnlyList<(Vector3 pos, char phase)> Trajectory => _trajectory;
        /// <summary>Cache guard: recompute only when a preview input changes.</summary>
        private string _trajDirty = "";

        // ── Loaded data ──
        public CharacterDefinition Def { get; private set; } = null!;
        public CharacterDefinition DisplayDef { get; private set; } = null!; // Def + hurtbox override
        public BakedAnimationData? Baked { get; private set; }
        public string[] BakedBoneNames => Baked?.BoneNames ?? Array.Empty<string>();
        public bool AuthoritativePreview { get; private set; }
        public string PreviewStatus { get; private set; } = "Preview unavailable";
        private CharacterAnimationCatalog _previewAnimationCatalog;
        private GameObject _previewRig;
        [SerializeField] private PlayerRenderer _previewRenderer;
        [SerializeField] private PlayerRenderer _dummyRenderer;
        private GroundDestinationIndicator? _scenarioChargeIndicator;
        public PlayerRenderer Renderer
        {
            get
            {
                EnsurePreviewRenderers();
                return _previewRenderer;
            }
        }
        public PlayerRenderer DummyRenderer
        {
            get
            {
                EnsurePreviewRenderers();
                return _dummyRenderer;
            }
        }
        public HurtboxBoneDef[] WorkingDefs { get; private set; } = Array.Empty<HurtboxBoneDef>();


        private readonly List<SpellResolver.EntityData> _hurtboxes = new();
        private readonly List<SpellResolver.EntityData> _dummyHurtboxes = new();
        private readonly List<(int index, HitboxEvent evt, Vector3 start, Vector3 end)> _hitboxes = new();
        private readonly List<(Vector3 pos, char phase)> _trajectory = new();
        private WeaponAttach _weaponAttach;
        private WeaponAttach _dummyWeaponAttach;
        private WeaponAttachConfig _weaponAttachConfigOverride;
        private float _playAccum;
        [SerializeField] private UnityEngine.Camera _camera = null!;
        private Vector2 _orbitAngles = new(25f, 0f);
        private float _orbitDistance = 4.5f;
        private Vector3 _orbitPivot;
        private CookedCharacterPackage? _liveDraftPackage;
        private CharacterPackageSource? _sourceDocument;
        private CharacterAssetCatalog.PresentationBinding[] _presentationBindings = Array.Empty<CharacterAssetCatalog.PresentationBinding>();
        private readonly AbilityLabPresentationPreviewer _presentationPreviewer = new();
        private readonly AbilityLabSimulationController _simulationPreviewer = new();
        public int PresentationPreviewInstanceCount => _presentationPreviewer.ActiveInstanceCount;
        public IReadOnlyList<string> PresentationPreviewDiagnostics => _presentationPreviewer.Diagnostics;
        public IReadOnlyCollection<GameObject> PresentationPreviewInstances => _presentationPreviewer.ActiveInstances;

        public AbilityLabScenarioResult Scenario { get; private set; }
        public int ScenarioFrame { get; private set; }
        public bool IsScenarioPreview => Scenario != null;
        public string SelectedAction => Scenario?.Options.Action ?? SelectedSlotId;
        public bool CanRunScenario => IsPackagePreview && Def != null && Baked != null;
        public bool CanPreviewGrab => CanRunScenario && Def.CaptureGeometry != null;
        private bool _scenarioPriorShowDummy;
        private readonly List<CharacterState> _scenarioActorHistory = new();
        private AbilityLabScenarioResult _scenarioTrajectorySource;

        public AbilityLabScenarioResult RunScenario(AbilityLabScenarioOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();
            if (!CanRunScenario)
                throw new InvalidOperationException("Scenario requires an available package runtime, baked poses and rig.");
            // Build first: malformed input or unavailable actions cannot alter selection.
            var result = _simulationPreviewer.RunScenario(Def, Baked, options, BasePosition(), FacingYaw);
            bool airborne = CanonicalSlotProjection.TryGet(options.Action, out var scenarioAddress) && scenarioAddress.IsAirborne;
            foreach (var frame in result.Frames)
                if (!CanPoseOrClipNotInstalled(_previewRenderer, frame.Actor, airborne)
                    || !CanPoseOrClipNotInstalled(_dummyRenderer, frame.Opponent, false))
                    throw new InvalidOperationException($"Scenario animation binding is unavailable at frame {frame.FrameIndex}.");
            var priorCursor = CaptureTimelineCursor();
            try
            {
                bool priorDummy = Scenario != null ? _scenarioPriorShowDummy : ShowDummy;
                if (CanonicalSlotProjection.TryGet(options.Action, out var address))
                {
                    Airborne = address.IsAirborne;
                    SlotIndex = SlotIndices[Array.IndexOf(SlotNames, address.InputLabel)];
                    SelectedSlotId = address.Id;
                    StageIndex = 0;
                    Tick = 0;
                }
                Scenario = result;
                ScenarioFrame = options.LastFrame;
                _scenarioPriorShowDummy = priorDummy;
                ShowDummy = true;
                Playing = false;
                RebuildScenarioTrailHistory();
                RefreshPose();
                return result;
            }
            catch
            {
                RestoreTimelineCursor(priorCursor);
                throw;
            }
        }

        public void SelectSharedAction(string action)
        {
            if (action != "grab") throw new ArgumentException("Unknown shared action.", nameof(action));
            var old = Scenario?.Options;
            RunScenario(new AbilityLabScenarioOptions(action, old?.LastFrame ?? 60, old?.Distance ?? DummyDistance,
                old?.OpponentBehavior ?? AbilityLabOpponentBehavior.Idle, old?.OpponentDamage ?? 0,
                old?.RelativeFacingDegrees ?? 180f));
            SeekScenario(0);
        }

        public void SeekScenario(int frame)
        {
            if (Scenario == null) throw new InvalidOperationException("Run a scenario before seeking its frames.");
            if (frame < 0 || frame >= Scenario.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frame));
            ScenarioFrame = frame;
            RefreshPose();
        }

        public void ExitScenario()
        {
            if (Scenario == null) return;
            ClearScenario();
            RefreshPose();
        }

        private void ClearScenario()
        {
            if (Scenario == null) return;
            ShowDummy = _scenarioPriorShowDummy;
            Scenario = null;
            ScenarioFrame = 0;
            _scenarioActorHistory.Clear();
            _scenarioTrajectorySource = null;
            _trajectory.Clear();
            _trajDirty = "";
            Playing = false;
            _scenarioChargeIndicator?.Clear();
        }

        private void RebuildScenarioTrailHistory()
        {
            _scenarioActorHistory.Clear();
            if (Scenario == null) return;
            foreach (var frame in Scenario.Frames) _scenarioActorHistory.Add(frame.Actor);
        }

        private void RefreshScenarioPose()
        {
            var frame = Scenario.Frames[ScenarioFrame];
            bool airborne = CanonicalSlotProjection.TryGet(Scenario.Options.Action, out var address) && address.IsAirborne;
            _previewRenderer.EnsureModel();
            _dummyRenderer.EnsureModel();
            _dummyRenderer.gameObject.SetActive(ShowDummy);
            bool actorPosed = _previewRenderer.PlayScrubbedState(frame.Actor, airborne, frame.ActorPoseTicks);
            bool actorClipNotInstalled = !actorPosed && _previewRenderer.LastAnimationClipNotInstalled;
            bool opponentPosed = _dummyRenderer.PlayScrubbedState(frame.Opponent, false, frame.OpponentPoseTicks);
            bool opponentClipNotInstalled = !opponentPosed && _dummyRenderer.LastAnimationClipNotInstalled;
            if ((!actorPosed && !actorClipNotInstalled) || (!opponentPosed && !opponentClipNotInstalled))
                throw new InvalidOperationException($"Scenario pose has a missing animation binding at frame {ScenarioFrame}.");
            if (actorClipNotInstalled || opponentClipNotInstalled)
                WarnClipNotInstalledOnce();
            _weaponAttach?.SetPreviewState(frame.Actor.AttackSlot, frame.Actor.AttackElapsedTicks,
                frame.Actor.State == ActionState.Attacking);
            _weaponAttach?.SetHitboxTrailActive(frame.ActiveSwordHitboxSeconds >= 0f);
            if (_scenarioActorHistory.Count > 0)
                _weaponAttach?.SetPreviewTrailHistory(_scenarioActorHistory, ScenarioFrame, airborne);
            // Trail history samples the same rig. Restore the selected state
            // after those historical samples so recovery/interruption is visible.
            _previewRenderer.PlayScrubbedState(frame.Actor, airborne, frame.ActorPoseTicks);
            _dummyWeaponAttach?.SetPreviewState(frame.Opponent.AttackSlot, frame.Opponent.AttackElapsedTicks,
                frame.Opponent.State == ActionState.Attacking);
            _dummyWeaponAttach?.SetHitboxTrailActive(false);
            _presentationPreviewer.SetSimulationFrame(Scenario.PresentationEvents, frame.MatchTick,
                _presentationBindings, _previewRenderer, _previewRenderer.transform, _scenarioActorHistory);
            UpdateScenarioChargeCue(frame.Actor, airborne);
            _weaponAttach?.RefreshPresentation();
            if (ShowDummy) _dummyWeaponAttach?.RefreshPresentation();
            QueueEditorRefresh();
        }
        private void UpdateScenarioChargeCue(in CharacterState actor, bool airborne)
        {
            if (Scenario == null || actor.State != ActionState.Aiming || !actor.IsAiming ||
                !CanonicalSlotProjection.TryGet(Scenario.Options.Action, out var address))
            {
                _scenarioChargeIndicator?.Clear();
                return;
            }
            int index = Array.IndexOf(SlotNames, address.InputLabel);
            if (index < 0)
            {
                _scenarioChargeIndicator?.Clear();
                return;
            }
            var cookedSlot = Def.GetCookedSlotAbility((byte)(SlotIndices[index] + 1), airborne);
            CookedChargedDirectionalDashCapabilityParameters? parameters = null;
            if (cookedSlot != null)
                foreach (var stage in cookedSlot.Timeline.Stages)
                foreach (var operation in stage.Operations)
                    if (operation is CookedStartCapabilityOperation
                        { Parameters: CookedChargedDirectionalDashCapabilityParameters charged })
                    {
                        parameters = charged;
                        break;
                    }
            if (parameters == null)
            {
                _scenarioChargeIndicator?.Clear();
                return;
            }
            if (_scenarioChargeIndicator == null)
            {
                var cue = new GameObject("AbilityLabChargedDashCue") { hideFlags = HideFlags.DontSave };
                cue.transform.SetParent(transform, false);
                _scenarioChargeIndicator = cue.AddComponent<GroundDestinationIndicator>();
            }
            float distance = parameters.GetDashDistance(actor.ChargeTicks);
            Vector3 direction = new(Mathf.Sin(actor.AimYaw), 0f, Mathf.Cos(actor.AimYaw));
            Vector3 position = new(actor.PX, actor.PY - Def.CapsuleHeight * 0.5f + 0.06f, actor.PZ);
            position += direction * distance;
            float scale = Mathf.Lerp(0.7f, 1.3f,
                Mathf.InverseLerp(parameters.MinDistance, parameters.MaxDistance, distance));
            _scenarioChargeIndicator.SetDestination(position, direction, 0.55f,
                parameters.GetChargeTier(actor.ChargeTicks), scale);
        }

        private static double PreviewClock()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return EditorApplication.timeSinceStartup;
#endif
            return Time.realtimeSinceStartupAsDouble;
        }

#if UNITY_EDITOR
        [NonSerialized] private bool _waitingForAssemblyReload;
        private void OnEnable()
        {
            _waitingForAssemblyReload = false;
            if (!EditorSceneManager.IsPreviewSceneObject(gameObject)) Instance = this;
            EditorApplication.update += UpdateEditorPlayback;
        }
        private void OnDisable()
        {
            EditorApplication.update -= UpdateEditorPlayback;
            ReleasePreviewForReload();
        }

        public void ReleasePreviewForReload()
        {
            _waitingForAssemblyReload = true;
            _presentationPreviewer.Clear();
            foreach (var animancer in GetComponentsInChildren<Animancer.AnimancerComponent>(true))
                if (animancer.IsGraphInitialized) animancer.Graph.Destroy();
        }

        private void UpdateEditorPlayback()
        {
            // A close dispatched earlier in this update can destroy us after the invocation list was captured.
            if (this == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || _waitingForAssemblyReload || !isActiveAndEnabled) return;
            AdvancePlayback();
        }
#endif

        private void Awake()
        {
#if UNITY_EDITOR
            if (!EditorSceneManager.IsPreviewSceneObject(gameObject))
#endif
            Instance = this;
            EnsurePreviewRenderers();
            EnsureCamera();
        }

        private void EnsurePreviewRenderers()
        {
            _previewRenderer = EnsurePreviewRenderer(_previewRenderer, "LabCharacter");
            _dummyRenderer = EnsurePreviewRenderer(_dummyRenderer, "LabDummy");
        }

        private PlayerRenderer EnsurePreviewRenderer(PlayerRenderer current, string slotName)
        {
            if (current != null && current.transform.parent == transform)
                return current;

            Transform named = transform.Find(slotName);
            if (named != null)
            {
                var namedRenderer = named.GetComponent<PlayerRenderer>();
                if (namedRenderer != null && namedRenderer != _previewRenderer && namedRenderer != _dummyRenderer)
                    return namedRenderer;
                if (namedRenderer == null)
                    return named.gameObject.AddComponent<PlayerRenderer>();
            }

            var existing = GetComponentsInChildren<PlayerRenderer>(true)
                .FirstOrDefault(candidate => candidate != null
                    && candidate.transform.parent == transform
                    && candidate != _previewRenderer
                    && candidate != _dummyRenderer);
            if (existing != null)
                return existing;

            var go = new GameObject(slotName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<PlayerRenderer>();
        }

        private void OnDestroy()
        {
            _presentationPreviewer.Clear();
            _simulationPreviewer.Clear();
            ReleaseRendererAttachments();
            DestroyPreviewCatalog();
            if (Instance == this) Instance = null;
        }

        private void ReleaseRendererAttachments()
        {
            _weaponAttach?.Init(null, null);
            _dummyWeaponAttach?.Init(null, null);
            _weaponAttach = null;
            _dummyWeaponAttach = null;
        }


        private void Update()
        {
            if (Application.isPlaying) AdvancePlayback();
        }

        private void AdvancePlayback()
        {
            if (!Playing) return;
            double now = PreviewClock();
            float elapsed = Application.isPlaying && Scenario == null
                ? Time.deltaTime
                : Mathf.Max(0f, (float)(now - _previewPlaybackClock));
            _previewPlaybackClock = now;
            _playAccum += elapsed * PlaySpeed;
            if (_playAccum < 1f / TickRate) return;
            if (Scenario != null)
            {
                while (_playAccum >= 1f / TickRate)
                {
                    _playAccum -= 1f / TickRate;
                    ScenarioFrame = (ScenarioFrame + 1) % Scenario.Frames.Count;
                }
            }
            else if (PhasePreviewActive)
            {
                int duration = PhaseDurationTicks;
                if (duration <= 0) { Playing = false; return; }
                while (_playAccum >= 1f / TickRate)
                {
                    _playAccum -= 1f / TickRate;
                    _phaseTick = (_phaseTick + 1) % duration;
                }
            }
            else
            {
                if (!TryGetStage(out var stage)) return;
                while (_playAccum >= 1f / TickRate)
                {
                    _playAccum -= 1f / TickRate;
                    Tick = (ushort)((Tick + 1) % Math.Max(1, (int)stage.DurationTicks));
                }
            }
            RefreshPose();
        }

        private void LateUpdate()
        {
            if (Application.isPlaying && _camera != null)
            {
                // Lab camera: right-drag orbits, middle-drag pans the pivot, scroll zooms.
                // Project uses the new Input System — no legacy Input.* calls.
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    AdjustCameraView(
                        mouse.rightButton.isPressed ? mouse.delta.ReadValue() : Vector2.zero,
                        mouse.middleButton.isPressed ? mouse.delta.ReadValue() : Vector2.zero,
                        -mouse.scroll.ReadValue().y * 0.05f);
                }
            }
        }

        public void AdjustCameraView(Vector2 orbitDelta, Vector2 panDelta, float zoomDistance)
        {
            if (_camera == null) return;
            _orbitAngles.x = Mathf.Clamp(_orbitAngles.x - orbitDelta.y * 0.1f, 5f, 85f);
            _orbitAngles.y += orbitDelta.x * 0.1f;
            _orbitPivot += (-_camera.transform.right * panDelta.x + _camera.transform.up * panDelta.y)
                * (_orbitDistance * 0.0015f);
            _orbitDistance = Mathf.Clamp(_orbitDistance + zoomDistance, 1f, 20f);
            Quaternion rot = Quaternion.Euler(_orbitAngles.x, _orbitAngles.y, 0f);
            _camera.transform.SetPositionAndRotation(_orbitPivot - rot * Vector3.forward * _orbitDistance, rot);
        }

        /// <summary>Reset the lab camera to its default orbit around the character.</summary>
        public void ResetCameraView()
        {
            _orbitAngles = new Vector2(25f, 0f);
            _orbitDistance = 4.5f;
            _orbitPivot = transform.position;
            if (_camera != null)
            {
                Quaternion rot = Quaternion.Euler(_orbitAngles.x, _orbitAngles.y, 0f);
                _camera.transform.position = _orbitPivot - rot * Vector3.forward * _orbitDistance;
                _camera.transform.rotation = rot;
            }
        }

        // ── Loading ──

        /// <summary>
        /// Create an owned orbit camera without borrowing the open scene's main camera.
        /// </summary>
        public void EnsureCamera()
        {
            if (_camera != null) return; // serialized ref survives edit→play remap
            var camGo = new GameObject("LabCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<UnityEngine.Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            _camera.fieldOfView = 60f;
            _camera.nearClipPlane = 0.05f;
#if UNITY_EDITOR
            if (EditorSceneManager.IsPreviewScene(gameObject.scene))
            {
                camGo.hideFlags = HideFlags.HideAndDontSave;
                _camera.scene = gameObject.scene;
                _camera.cameraType = CameraType.Preview;
                _camera.enabled = false;
            }
            else
#endif
                camGo.AddComponent<AudioListener>();
            ResetCameraView();
        }


        public void LoadCharacter(ContentHandle handle)
        {
            if (!handle.IsValid) return;
            if (!SlopArena.Client.ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
            {
                Debug.LogError($"[AbilityLab] Failed to build local content catalog: {failure}");
                return;
            }
            var entry = catalog.Resolve(handle);
            if (entry == null)
            {
                Debug.LogError($"[AbilityLab] Unknown content handle {handle.Value}.");
                return;
            }
            if (entry.CookedCharacterPackage != null)
            {
                CharacterAnimationCatalog animationCatalog = null;
                GameObject rig = null;
                string error = "";
                bool resolved = entry.BakedAnimation != null &&
                    CookedCharacterClientAssetResolver.TryResolve(entry, out animationCatalog, out rig, out error);
                if (!resolved)
                {
                    if (entry.BakedAnimation == null)
                        error = "Cooked pose payload is missing.";
                    Debug.LogError($"[AbilityLab] Cooked client assets failed for {entry.Identity.PackageId}: {error}");
                    return;
                }
                ApplyPackageData(entry.CookedCharacterPackage, entry.BakedAnimation, animationCatalog, rig,
                    entry.Identity.PackageId, "");
            }
        }

        public void ApplyPackagePreview(AbilityLabPackagePreviewResult result)
        {
            if (result == null || !result.IsAvailable || result.Package == null ||
                result.BakedPoses == null || result.AnimationCatalog == null ||
                result.Rig == null || result.Identity == null)
            {
                ApplyPreviewUnavailable(result?.Diagnostics ?? Array.Empty<CharacterDiagnostic>());
                return;
            }
            ApplyPackageData(
                result.Package,
                result.BakedPoses,
                result.AnimationCatalog,
                result.Rig,
                result.Identity.PackageId,
                result.Identity.PackageHash);
        }


        public void ApplyPackageDraftPreview(CookedCharacterPackage package, AbilityLabPackagePreviewResult persistedPreview)
        {
            if (package == null || persistedPreview == null || !persistedPreview.IsAvailable ||
                persistedPreview.BakedPoses == null || persistedPreview.AnimationCatalog == null ||
                persistedPreview.Rig == null || persistedPreview.Identity == null ||
                package.Metadata == null || package.Definition == null ||
                package.Metadata.PackageId != persistedPreview.Identity.PackageId)
            {
                MarkPackageDraftInvalid();
                return;
            }

            if (ReferenceEquals(_liveDraftPackage, package) && IsPackagePreview)
            {
                RefreshPose();
                return;
            }

            var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(package, CharacterClass.None);
            string priorSlotId = SelectedSlotId;
            ClearScenario();
            int priorStage = StageIndex;
            ushort priorTick = Tick;
            int priorHitbox = SelectedHitboxEventIndex;
            if (!CanonicalSlotProjection.TryGet(priorSlotId, out var priorAddress) ||
                definition.GetSlotAbility(SlotIndices[Array.IndexOf(SlotNames, priorAddress.InputLabel)], priorAddress.IsAirborne) == null)
                priorAddress = CanonicalSlotProjection.All[0];

            int labelIndex = Array.IndexOf(SlotNames, priorAddress.InputLabel);
            var spec = definition.GetSlotAbility(SlotIndices[labelIndex], priorAddress.IsAirborne);
            if (spec?.Stages == null || spec.Stages.Length == 0)
            {
                MarkPackageDraftInvalid();
                return;
            }

            if (!ReferenceEquals(_previewAnimationCatalog, persistedPreview.AnimationCatalog))
            {
                DestroyPreviewCatalog();
                _previewAnimationCatalog = persistedPreview.AnimationCatalog;
            }
            _previewRig = persistedPreview.Rig;
            Character = CharacterClass.None;
            SelectedPackageId = package.Metadata.PackageId;
            SelectedSlotId = priorAddress.Id;
            Airborne = priorAddress.IsAirborne;
            SlotIndex = SlotIndices[labelIndex];
            Def = definition;
            Baked = persistedPreview.BakedPoses;
            WorkingDefs = definition.HurtboxBoneDefs != null
                ? (HurtboxBoneDef[])definition.HurtboxBoneDefs.Clone()
                : Array.Empty<HurtboxBoneDef>();
            DisplayDef = definition;
            AuthoritativePreview = false;
            _packagePreviewAvailable = true;
            _liveDraftPackage = package;
            PreviewStatus = "Live draft";
            StageIndex = Mathf.Clamp(priorStage, 0, spec.Stages.Length - 1);
            var stage = spec.Stages[StageIndex];
            Tick = (ushort)Mathf.Clamp(priorTick, 0, Mathf.Max(0, stage.DurationTicks - 1));
            int hitboxCount = stage.HitboxEvents?.Length ?? 0;
            SelectedHitboxEventIndex = priorHitbox >= 0 && priorHitbox < hitboxCount ? priorHitbox : -1;
            SpawnRenderer();
            RefreshPose();
        }
        public void MarkPackageDraftInvalid()
        {
            _packagePreviewAvailable = false;
            ClearScenario();
            Playing = false;
            _liveDraftPackage = null;
            AuthoritativePreview = false;
            PreviewStatus = "Draft invalid";
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
#endif
        }
        private void ApplyPackageData(
            CookedCharacterPackage package,
            BakedAnimationData baked,
            CharacterAnimationCatalog animationCatalog,
            GameObject rig,
            string packageId,
            string packageHash)
        {
            ClearScenario();
            if (!ReferenceEquals(_previewAnimationCatalog, animationCatalog))
            {
                DestroyPreviewCatalog();
                _previewAnimationCatalog = animationCatalog;
            }
            _previewRig = rig;
            var definition = CookedCharacterRuntimeAdapter.ToCharacterDefinition(package, CharacterClass.None);
            SelectedPackageHash = packageHash;
            Character = CharacterClass.None;
            SelectedPackageId = packageId;
            Def = definition;
            Baked = baked;
            WorkingDefs = definition.HurtboxBoneDefs != null ? (HurtboxBoneDef[])definition.HurtboxBoneDefs.Clone() : Array.Empty<HurtboxBoneDef>();
            DisplayDef = definition;
            AuthoritativePreview = true;
            _packagePreviewAvailable = true;
            PreviewStatus = "Authoritative";
            ShowHurtboxes = false;
            ShowHitboxes = true;
            ShowBakedBones = false;
            ShowDummy = false;
            SpawnRenderer();
            SetSlot(CanonicalSlotProjection.All[0]);
        }

        public void ApplyPreviewUnavailable(IReadOnlyList<CharacterDiagnostic> diagnostics)
        {
            _presentationPreviewer.Clear();
            ClearScenario();
            _simulationPreviewer.Clear();
            _presentationBindings = Array.Empty<CharacterAssetCatalog.PresentationBinding>();
            ReleaseRendererAttachments();
            DestroyPreviewCatalog();
            _previewRig = null;
            Character = CharacterClass.None;
            SelectedPackageId = "";
            SelectedPackageHash = "";
            SelectedSlotId = "";
            Def = null;
            DisplayDef = null;
            Baked = null;
            WorkingDefs = Array.Empty<HurtboxBoneDef>();
            AuthoritativePreview = false;
            _packagePreviewAvailable = false;
            PreviewStatus = "Preview unavailable";
            StageIndex = 0;
            Tick = 0;
            Playing = false;
            RefreshPose();
        }

        private void DestroyPreviewCatalog()
        {
            if (_previewAnimationCatalog == null) return;
#if UNITY_EDITOR
            if (!EditorUtility.IsPersistent(_previewAnimationCatalog))
                DestroyImmediate(_previewAnimationCatalog);
#else
            Destroy(_previewAnimationCatalog);
#endif
            _previewAnimationCatalog = null;
        }


        public void MarkPreviewNonAuthoritative()
        {
            ClearScenario();
            AuthoritativePreview = false;
            PreviewStatus = "Non-authoritative draft";
        }

        private static BakedAnimationData? LoadBaked(CharacterDefinition def)
        {
            if (def.Class == CharacterClass.FightGuy || string.IsNullOrEmpty(def.BakedDataPath)) return null;
            string? path = BakedContentPaths.ResolveBaked(def.BakedDataPath);
            if (path == null) return null;
            try { return BakedAnimationData.LoadFromBin(File.ReadAllBytes(path)); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AbilityLab] Failed to load baked data from {path}: {ex.Message}");
                return null;
            }
        }

        private static HurtboxBoneDef[] LoadWorkingDefs(CharacterDefinition def, BakedAnimationData? baked)
        {
            // Override file wins; else the shipped C# defs (cloned so edits never
            // touch the registry); else empty (capsule-only character — no bone edits).
            var overridePath = HurtboxOverride.OverridePathFor(def);
            if (overridePath != null && baked != null)
            {
                string? sysPath = BakedContentPaths.ResolveBaked(overridePath);
                if (sysPath != null)
                {
                    try
                    {
                        if (HurtboxOverride.TryParse(File.ReadAllText(sysPath), out _, out var parsed)
                            && parsed != null && HurtboxOverride.ValidateOrder(parsed, baked))
                        {
                            Debug.Log($"[AbilityLab] Loaded hurtbox override: {sysPath}");
                            return parsed;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AbilityLab] Failed to read hurtbox override {sysPath}: {ex.Message}");
                    }
                }
            }
            return def.HurtboxBoneDefs != null ? (HurtboxBoneDef[])def.HurtboxBoneDefs.Clone() : Array.Empty<HurtboxBoneDef>();
        }


        private void ClearPreviewSelection()
        {
#if UNITY_EDITOR
            if (Selection.activeGameObject == gameObject ||
                (Selection.activeTransform != null && Selection.activeTransform.IsChildOf(transform)))
                Selection.activeObject = gameObject;
#endif
        }

        private void SpawnRenderer()
        {
            ClearPreviewSelection();
            EnsurePreviewRenderers();
            if (DisplayDef == null) return;

            ConfigureRenderer(_previewRenderer, DisplayDef, "LabCharacter");
            _previewRenderer.transform.position = BasePosition();
            _weaponAttach = AttachWeapon(_previewRenderer, DisplayDef);

            ConfigureRenderer(_dummyRenderer, DisplayDef, "LabDummy");
            PositionDummy();
            _dummyRenderer.gameObject.SetActive(ShowDummy);
            _dummyWeaponAttach = AttachWeapon(_dummyRenderer, DisplayDef);
        }

        /// <summary>
        /// Attach the selected package's configured weapon prop to the preview model.
        /// Package mode reads the generated catalog binding.
        /// </summary>
        private WeaponAttach AttachWeapon(PlayerRenderer renderer, CharacterDefinition def)
        {
            var attach = renderer.GetComponent<WeaponAttach>();
            if (attach == null) attach = renderer.gameObject.AddComponent<WeaponAttach>();

            WeaponAttachConfig config = renderer == _previewRenderer && _weaponAttachConfigOverride != null
                ? _weaponAttachConfigOverride
                : _previewAnimationCatalog != null
                    ? _previewAnimationCatalog.WeaponConfig
                    : def != null && def.Class != CharacterClass.None
                        ? Resources.Load<WeaponAttachConfig>($"WeaponConfigs/{def.Class}")
                        : null;
            attach.Init(renderer, config);
            return attach;
        }

        private void ConfigureRenderer(PlayerRenderer renderer, CharacterDefinition def, string name)
        {
            renderer.name = name;
            renderer.ModelYOffset = def.ModelYOffset;
            renderer.CapsuleRadius = def.CapsuleRadius;
            renderer.CapsuleHeight = def.CapsuleHeight;
            renderer.HurtboxBoneDefs = def.HurtboxBoneDefs;
            renderer.SetBakedData(Baked);
            renderer.SetAnimationCatalog(_previewAnimationCatalog);
            renderer.SetCharacterDefinition(def);
            renderer.LoadModel(def, _previewRig);
            foreach (var skin in renderer.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.forceMatrixRecalculationPerRender = true;
        }



        private Vector3 BasePosition() => transform.position + new Vector3(0f, Def.CapsuleHeight * 0.5f, 0f);
        private Vector3 DummyPosition()
            => transform.position + new Vector3(
                Mathf.Sin(FacingYaw) * DummyDistance,
                Def.CapsuleHeight * 0.5f,
                Mathf.Cos(FacingYaw) * DummyDistance);

        private void PositionDummy()
        {
            if (_dummyRenderer == null) return;
            _dummyRenderer.transform.position = DummyPosition();
        }

        // ── Ability accessors ──

        public AbilitySpec? CurrentSpec() => Def?.GetSlotAbility(SlotIndex, Airborne);


        /// <summary>
        /// Stage lookup without nullable-struct pitfalls (AttackStage? member access
        /// does not narrow after == null in Roslyn). Returns false when there is no
        /// selectable stage.
        /// </summary>
        public bool TryGetStage(out AttackStage stage)
        {
            stage = default;
            var spec = CurrentSpec();
            if (spec?.Stages == null || StageIndex < 0 || StageIndex >= spec.Stages.Length) return false;
            stage = spec.Stages[StageIndex];
            return true;
        }

        private static string AnimNameFor(AbilitySpec spec, int stageIndex)
            => spec.AnimationNames != null && stageIndex >= 0 && stageIndex < spec.AnimationNames.Length
                ? spec.AnimationNames[stageIndex] : "idle";

        // ── State setters (window/UI entry points — keep pose + selection coherent) ──

        public void SetAirborne(bool airborne)
        {
            if (Airborne == airborne) return;
            Airborne = airborne;
            UpdateSelectedSlotId();
            StageIndex = 0;
            Tick = 0;
            SelectedHitboxEventIndex = -1;
            _phaseTick = 0;
            Playing = false;
            EnsurePhaseSelectionSupported();
            RefreshPose();
        }

        public void SetSlot(SlotAddress address)
        {
            if (!CanonicalSlotProjection.TryGet(address.Id, out var canonical) || canonical != address)
                return;
            ExitScenario();

            int labelIndex = Array.IndexOf(SlotNames, canonical.InputLabel);
            if (labelIndex < 0) return;
            Airborne = canonical.IsAirborne;
            SlotIndex = SlotIndices[labelIndex];
            SelectedSlotId = canonical.Id;
            StageIndex = 0;
            Tick = 0;
            _phaseTick = 0;
            EnsurePhaseSelectionSupported();
            Playing = false;
            RefreshPose();
        }


        public void SetSlot(int slot)
        {
            if (SlotIndex == slot) return;
            SlotIndex = slot;
            UpdateSelectedSlotId();
            StageIndex = 0;
            Tick = 0;
            _phaseTick = 0;
            Playing = false;
            EnsurePhaseSelectionSupported();
            RefreshPose();
        }

        private void UpdateSelectedSlotId()
        {
            if (!IsPackagePreview) return;
            int labelIndex = Array.IndexOf(SlotIndices, SlotIndex);
            if (labelIndex >= 0 && CanonicalSlotProjection.TryGet(Airborne, SlotNames[labelIndex], out var address))
                SelectedSlotId = address.Id;
        }
        private void EnsurePhaseSelectionSupported()
        {
            if (Phase == AuthoringPhase.Charge
                && !TryResolvePhaseAnimation(AuthoringPhase.Charge, out _, out _, out _, out _))
            {
                Phase = AuthoringPhase.Fire;
                _phaseTick = 0;
                Playing = false;
            }
            if (PhasePreviewActive
                && !TryResolvePhaseAnimation(Phase, out _, out _, out _, out _))
                PhasePreviewActive = false;
        }

        public void SetStage(int stage)
        {
            StageIndex = stage;
            Tick = 0;
            _phaseTick = 0;
            EnsurePhaseSelectionSupported();
            RefreshPose();
        }

        public void SelectHitbox(int index)
        {
            SelectedHitboxEventIndex = index >= 0 && index < CurrentWorkingEvents().Length ? index : -1;
            QueueEditorRefresh();
        }

        public void SetTick(ushort tick)
        {
            bool leftPhasePreview = PhasePreviewActive;
            PhasePreviewActive = false;
            if (Tick == tick && !leftPhasePreview) return;
            Tick = tick;
            RefreshPose();
        }

        /// <summary>The currently authored hitbox events for the selected stage.</summary>
        public HitboxEvent[] CurrentWorkingEvents()
        {
            return TryGetStage(out var stage) && stage.HitboxEvents != null
                ? stage.HitboxEvents
                : Array.Empty<HitboxEvent>();
        }


        // ── Pose resolution (the Shared functions the server uses) ──

        /// <summary>
        /// Mirror of the server's tick→baked-frame projection (SpawnHitbox /
        /// ResolveBoneAnimFrame): bakedFrame = min(tick * fc / durationTicks, fc-1),
        /// animation falls back to "idle" when missing from the bake.
        /// </summary>
        public bool ResolvePose(string animName, ushort durationTicks, out string resolvedAnim, out int bakedFrame)
        {
            resolvedAnim = animName;
            bakedFrame = 0;
            if (Baked == null) return false;
            int fc = Baked.FrameCountFor(animName);
            if (fc < 0) { resolvedAnim = "idle"; fc = Baked.FrameCountFor("idle"); }
            if (fc < 0) return false;
            bakedFrame = durationTicks > 0 ? Mathf.Min(Tick * fc / durationTicks, fc - 1) : Mathf.Min(Tick, fc - 1);
            return true;
        }

        public IReadOnlyList<SpellResolver.EntityData> ResolveHurtboxes()
        {
            _hurtboxes.Clear();
            if (Scenario != null)
            {
                foreach (var shape in Scenario.Frames[ScenarioFrame].Hurtboxes)
                    if (shape.Id == 1 && !shape.ShieldSurface && shape.Active) _hurtboxes.Add(shape);
                return _hurtboxes;
            }
            if (Baked == null) return _hurtboxes; // no pose data at all

            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage)) return _hurtboxes;

            string animName = AnimNameFor(spec, StageIndex);
            if (!ResolvePose(animName, stage.DurationTicks, out string resolvedAnim, out int bakedFrame)) return _hurtboxes;

            Vector3 lungeDisplacement = CalculateLungeDisplacement();
            var state = new CharacterState
            {
                PX = transform.position.x + lungeDisplacement.x,
                PY = BasePosition().y,
                PZ = transform.position.z + lungeDisplacement.z,
                FacingYaw = FacingYaw,
            };
            _hurtboxes.AddRange(ServerSimulation.BuildEntitiesFromState(state, DisplayDef, Baked, resolvedAnim, bakedFrame, 0));
            return _hurtboxes;
        }

        public IReadOnlyList<(int index, HitboxEvent evt, Vector3 start, Vector3 end)> ResolveHitboxes()
        {
            _hitboxes.Clear();
            if (Scenario != null)
            {
                int index = 0;
                foreach (var hitbox in Scenario.Frames[ScenarioFrame].ActiveHitboxes)
                {
                    if (!hitbox.Active) continue;
                    var evt = hitbox.SourceEvent;
                    evt.Shape = hitbox.Shape;
                    evt.Radius = hitbox.Radius;
                    _hitboxes.Add((index++, evt, new Vector3(hitbox.X, hitbox.Y, hitbox.Z),
                        new Vector3(hitbox.EndX, hitbox.EndY, hitbox.EndZ)));
                }
                return _hitboxes;
            }
            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage)) return _hitboxes;

            Vector3 lungeDisplacement = CalculateLungeDisplacement();
            var state = new CharacterState
            {
                PX = transform.position.x + lungeDisplacement.x,
                PY = BasePosition().y,
                PZ = transform.position.z + lungeDisplacement.z,
                FacingYaw = FacingYaw,
                AttackElapsedTicks = Tick,
            };
            var events = CurrentWorkingEvents();
            for (int i = 0; i < events.Length; i++)
            {
                var evt = events[i];
                if (Tick < evt.TriggerTick || Tick >= evt.TriggerTick + evt.DurationTicks) continue;
                HitboxGeometry.ResolvePositions(state, evt, Baked, DisplayDef,
                    spec.AnimationNames, (byte)StageIndex, (byte)SlotIndex, Airborne,
                    out float wx, out float wy, out float wz,
                    out float wex, out float wey, out float wez);
                _hitboxes.Add((i, evt, new Vector3(wx, wy, wz), new Vector3(wex, wey, wez)));
            }
            return _hitboxes;
        }

        public List<SpellResolver.EntityData> ResolveDummyHurtboxes()
        {
            _dummyHurtboxes.Clear();
            if (!ShowDummy) return _dummyHurtboxes;
            if (Scenario != null)
            {
                foreach (var shape in Scenario.Frames[ScenarioFrame].Hurtboxes)
                    if (shape.Id == 2 && shape.Active) _dummyHurtboxes.Add(shape);
                return _dummyHurtboxes;
            }
            if (Baked == null) return _dummyHurtboxes;
            int fc = Baked.FrameCountFor("idle");
            if (fc < 0) return _dummyHurtboxes;
            var state = new CharacterState
            {
                PX = DummyPosition().x,
                PY = DummyPosition().y,
                PZ = DummyPosition().z,
                FacingYaw = FacingYaw + Mathf.PI,
            };
            _dummyHurtboxes.AddRange(ServerSimulation.BuildEntitiesFromState(state, DisplayDef, Baked, "idle", 0, 0));
            return _dummyHurtboxes;
        }

        /// <summary>
        /// Knockback arc for the previewed hitbox: launches the dummy victim (at the current
        /// TrajectoryPercent) with the hitbox's authored knockback through the REAL sim
        /// (Simulation.ApplyKnockback + ServerSimulation tick loop — the same flight law the
        /// move-data tool uses), and samples the path to landing. Recompute on input change;
        /// draw via <see cref="Trajectory"/> in OnRenderObject.
        /// </summary>
        public IReadOnlyList<(Vector3 pos, char phase)> ResolveTrajectory()
        {
            if (Scenario != null)
            {
                if (!ReferenceEquals(_scenarioTrajectorySource, Scenario))
                {
                    _scenarioTrajectorySource = Scenario;
                    _trajectory.Clear();
                    foreach (var sample in Scenario.Frames)
                    {
                        var opponent = sample.Opponent;
                        _trajectory.Add((new Vector3(opponent.PX, opponent.PY, opponent.PZ),
                            opponent.HitstunTicks > 0 ? 'H' : opponent.IsGrounded ? 'G' : 'F'));
                    }
                }
                return _trajectory;
            }
            if (_scenarioTrajectorySource != null)
            {
                _scenarioTrajectorySource = null;
                _trajectory.Clear();
                _trajDirty = "";
            }
            if (Def == null || Baked == null) { _trajectory.Clear(); return _trajectory; }
            var events = CurrentWorkingEvents();
            if (events.Length == 0 || PreviewHitboxIndex < 0 || PreviewHitboxIndex >= events.Length) { _trajectory.Clear(); return _trajectory; }
            var hit = events[PreviewHitboxIndex];
            if (hit.Knockback.Profile != KnockbackProfile.Custom) { _trajectory.Clear(); return _trajectory; } // custom-only (like the tool)

            // Cache: recompute only when a preview input (hitbox values, %, facing, dummy pos) changes.
            string key = $"{PreviewHitboxIndex}|{TrajectoryPercent:0.0}|{FacingYaw:0.000}|{DummyPosition():0.00}|{hit.Radius:0.00}|" +
                $"{hit.Damage:0.0}|{hit.StunTicks}|{hit.Knockback.Angle}|{hit.Knockback.BaseKnockback:0.0}|{hit.Knockback.KnockbackGrowth:0.0}";
            if (_trajectory.Count > 0 && key == _trajDirty) return _trajectory;
            _trajDirty = key;
            _trajectory.Clear();

            // Launch the dummy away from the attacker, along the attack's facing.
            float groundY = DummyPosition().y - Def.CapsuleHeight * 0.5f; // arena floor under the dummy's feet
            var state = new CharacterState
            {
                PX = DummyPosition().x,
                PY = DummyPosition().y,
                PZ = DummyPosition().z,
                IsGrounded = true,
                State = ActionState.Idle,
                FacingYaw = FacingYaw,
                DamagePercent = (ushort)(TrajectoryPercent + (int)hit.Damage), // post-hit, matches tool parity
            };
            float dirX = Mathf.Sin(FacingYaw), dirZ = Mathf.Cos(FacingYaw);
            SlopArena.Shared.Simulation.ApplyKnockback(ref state, dirX, dirZ, (sbyte)hit.Knockback.Angle,
                hit.Knockback.BaseKnockback, hit.Knockback.KnockbackGrowth,
                hit.Damage, hit.StunTicks, Def.Weight);

            var sim = new ServerSimulation(LabArena(groundY));
            sim.RegisterEntity(1, Def, state);
            var inputs = new Dictionary<ulong, InputState> { [1] = default };

            _trajectory.Add((new Vector3(state.PX, state.PY, state.PZ), 'H'));
            float maxPy = state.PY;
            bool apexMarked = false;
            for (int t = 0; t < 2400; t++)
            {
                sim.Tick(inputs);
                var s = sim.GetState(1);
                bool atApex = !apexMarked && s.PY <= maxPy && t > 0 && !s.IsGrounded && s.HitstunTicks == 0;
                if (s.PY > maxPy) maxPy = s.PY;
                else if (atApex) apexMarked = true;
                char phase = s.IsGrounded ? 'G'
                    : s.HitstunTicks > 0 ? 'H'
                    : atApex ? 'A' : 'F';
                _trajectory.Add((new Vector3(s.PX, s.PY, s.PZ), phase));
                if (s.IsGrounded) break;
            }
            return _trajectory;
        }

        /// <summary>A minimal flat arena for trajectory stepping (floor at the given world Y).</summary>
        private static ArenaDefinition LabArena(float floorY)
        {
            const int w = 100, h = 100;
            var data = new float[w * h];
            for (int i = 0; i < data.Length; i++) data[i] = floorY;
            return new ArenaDefinition
            {
                Name = "lab",
                DisplayName = "Ability Lab",
                KillHeight = floorY - 20f,
                SpawnPoints = new[] { new SpawnPoint { X = 0, Y = floorY, Z = 0, Yaw = 0 } },
                Heightmap = new ArenaHeightmap
                {
                    Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f,
                },
            };
        }

        // ── Scrub / edit ──

        // Source timeline data remains the package authoring document; the cooked definition does not retain presentation IDs.
        private CharacterStageSource? CurrentSourceStage()
        {
            if (_sourceDocument == null || !IsPackagePreview || string.IsNullOrEmpty(SelectedSlotId))
                return null;

            string current = SelectedSlotId;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (visited.Add(current))
            {
                var slot = _sourceDocument.Character.Slots?.FirstOrDefault(candidate => candidate != null && candidate.Id == current);
                if (slot != null)
                    return slot.Timeline?.Stages != null && StageIndex >= 0 && StageIndex < slot.Timeline.Stages.Count
                        ? slot.Timeline.Stages[StageIndex]
                        : null;
                var alias = _sourceDocument.Character.Aliases?.FirstOrDefault(candidate => candidate != null && candidate.From == current);
                if (alias == null) break;
                current = alias.To;
            }
            return null;
        }

        public Vector3 CalculateLungeDisplacement()
        {
            var sourceStage = CurrentSourceStage();
            if (sourceStage?.Operations == null) return Vector3.zero;

            float totalDistance = 0f;
            foreach (var op in sourceStage.Operations)
            {
                if (op is ForwardLungeOperationSource lunge)
                {
                    if (Tick > lunge.Tick)
                    {
                        int activeTicks = Math.Min((int)Tick - lunge.Tick, (int)lunge.DurationTicks);
                        totalDistance += lunge.Speed * (activeTicks / TickRate);
                    }
                }
            }

            return new Vector3(
                Mathf.Sin(FacingYaw) * totalDistance,
                0f,
                Mathf.Cos(FacingYaw) * totalDistance);
        }

        public void SetSourceDocument(CharacterPackageSource source)
        {
            _sourceDocument = source ?? throw new ArgumentNullException(nameof(source));
        }

        public void SetPresentationBindings(CharacterAssetCatalog.PresentationBinding[] bindings)
        {
            _presentationBindings = bindings ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>();
            _presentationPreviewer.SetBindings(_presentationBindings);
            RefreshPose();
        }

        public void InvalidatePresentationPreview() => RefreshPose();

        /// <summary>
        /// Pose package previews from the Shared animation phase and runtime playback speed.
        /// Source-only previews sample the authored stage clip; the dummy holds idle frame 0.
        /// </summary>
        public void RefreshPose()
        {
#if UNITY_EDITOR
            if (EditorApplication.isCompiling || _waitingForAssemblyReload) return;
#endif
            EnsurePreviewRenderers();
            if (_previewRenderer == null || Def == null)
            {
                _presentationPreviewer.Clear();
                _weaponAttach?.SetHitboxTrailActive(false);
                _simulationPreviewer.Clear();
                QueueEditorRefresh();
                return;
            }
            if (Scenario != null)
            {
                RefreshScenarioPose();
                return;
            }
            if (PhasePreviewActive)
            {
                RefreshPhasePose();
                return;
            }
            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage))
            {
                _presentationPreviewer.Clear();
                _simulationPreviewer.Clear();
                _weaponAttach?.SetHitboxTrailActive(false);
                QueueEditorRefresh();
                return;
            }
            _previewRenderer.EnsureModel();
            if (_previewRenderer.transform.childCount == 0)
                ConfigureRenderer(_previewRenderer, DisplayDef, "LabCharacter");
            Vector3 lungeDisplacement = CalculateLungeDisplacement();
            _previewRenderer.transform.position = BasePosition() + lungeDisplacement;
            float normalized = stage.DurationTicks > 0 ? (float)Tick / stage.DurationTicks : 0f;
            _previewRenderer.PlayScrubbed(AnimNameFor(spec, StageIndex), normalized);
            _weaponAttach?.SetPreviewState((byte)(SlotIndex + 1), Tick, true);
            if (_dummyRenderer != null)
            {
                _dummyRenderer.EnsureModel();
                _dummyRenderer.gameObject.SetActive(ShowDummy);
                if (ShowDummy)
                {
                    _dummyRenderer.PlayScrubbed("idle", 0f);
                    PositionDummy();
                }
            }
            var sourceStage = CurrentSourceStage();
            if (IsPackagePreview && Def.GetCookedSlotAbility((byte)(SlotIndex + 1), Airborne) != null)
            {
                int simulationTick = Tick;
                for (int index = 0; index < StageIndex && index < spec.Stages.Length; index++)
                    simulationTick += spec.Stages[index].DurationTicks;
                _simulationPreviewer.Simulate(
                    Def,
                    Baked,
                    (byte)(SlotIndex + 1),
                    Airborne,
                    BasePosition(),
                    FacingYaw,
                    simulationTick);
                _weaponAttach?.SetHitboxTrailActive(_simulationPreviewer.ActiveSwordHitboxSeconds >= 0f);
                _weaponAttach?.SetPreviewTrailHistory(
                    _simulationPreviewer.StateHistory, simulationTick, Airborne);
                if (_simulationPreviewer.StateHistory.Count > simulationTick)
                {
                    var previewState = _simulationPreviewer.StateHistory[simulationTick];
                    _previewRenderer.transform.SetPositionAndRotation(
                        new Vector3(previewState.PX, previewState.PY + _previewRenderer.ModelYOffset, previewState.PZ),
                        Quaternion.Euler(0f, previewState.FacingYaw * Mathf.Rad2Deg, 0f));
                    bool bakedWeaponPose = _weaponAttach?.HasSwordTrail == true
                        && _previewRenderer.TrySampleBakedWeaponPath(previewState, Airborne, out _, out _);
                    if (!bakedWeaponPose &&
                        !_previewRenderer.PlayScrubbedState(previewState, Airborne, simulationTick))
                    {
                        if (!_previewRenderer.LastAnimationClipNotInstalled)
                            throw new InvalidOperationException($"Authoring pose has a missing animation binding at tick {simulationTick}.");
                        // The clip is bound but not installed in this checkout (ADR-0038):
                        // keep the model's pose; the overlays come from cooked pose data.
                        WarnClipNotInstalledOnce();
                    }
                    _weaponAttach?.SetPreviewState(
                        previewState.AttackSlot, previewState.AttackElapsedTicks,
                        previewState.State == ActionState.Attacking);
                }
                _presentationPreviewer.SetSimulationFrame(
                    _simulationPreviewer.PresentationEvents,
                    (uint)(simulationTick + 1),
                    _presentationBindings,
                    _previewRenderer,
                    _previewRenderer.transform,
                    _simulationPreviewer.StateHistory);
            }
            else
            {
                _weaponAttach?.SetHitboxTrailActive(false);
                _presentationPreviewer.SetFrame(sourceStage, Tick, _presentationBindings, _previewRenderer.transform, _previewRenderer);
            }
            _weaponAttach?.RefreshPresentation();
            if (ShowDummy) _dummyWeaponAttach?.RefreshPresentation();
            QueueEditorRefresh();
        }
        private readonly HashSet<string> _clipNotInstalledWarnings = new HashSet<string>(StringComparer.Ordinal);

        // A bound clip that is not installed (ADR-0038) still lets the scenario run:
        // its outcome and overlays come from the Shared simulation and cooked poses.
        private static bool CanPoseOrClipNotInstalled(PlayerRenderer renderer, in CharacterState state, bool airborne)
            => renderer.CanPlayScrubbedState(state, airborne) || renderer.LastAnimationClipNotInstalled;

        private void WarnClipNotInstalledOnce()
        {
            string key = $"{SelectedPackageId}/{SelectedSlotId}";
            if (_clipNotInstalledWarnings.Add(key))
                Debug.LogWarning($"[AbilityLab] {key}: the animation clip is not installed in this checkout. " +
                    "Hitboxes and hurtboxes still come from the cooked pose data; the model keeps its pose.");
        }

        private void RefreshPhasePose()
        {
            if (!TryResolvePhaseAnimation(
                Phase, out string clipName, out AnimationClip clip, out int duration, out float playbackSpeed))
            {
                _presentationPreviewer.Clear();
                _simulationPreviewer.Clear();
                _weaponAttach?.SetHitboxTrailActive(false);
                QueueEditorRefresh();
                return;
            }
            _previewRenderer.EnsureModel();
            if (_previewRenderer.transform.childCount == 0)
                ConfigureRenderer(_previewRenderer, DisplayDef, "LabCharacter");
            _phaseTick = Mathf.Clamp(_phaseTick, 0, duration - 1);
            _previewRenderer.transform.SetPositionAndRotation(
                BasePosition(), Quaternion.Euler(0f, FacingYaw * Mathf.Rad2Deg, 0f));
            float sampleTime = _phaseTick / TickRate * playbackSpeed;
            _previewRenderer.PlayScrubbed(
                clipName, clip.length > 0f ? Mathf.Clamp01(sampleTime / clip.length) : 0f);
            _weaponAttach?.SetPreviewState(
                (byte)(SlotIndex + 1), _phaseTick, Phase == AuthoringPhase.Fire);
            _weaponAttach?.SetHitboxTrailActive(false);
            _simulationPreviewer.Clear();
            if (Phase == AuthoringPhase.Fire)
                _presentationPreviewer.SetFrame(
                    CurrentSourceStage(), (ushort)_phaseTick, _presentationBindings,
                    _previewRenderer.transform, _previewRenderer);
            else
                _presentationPreviewer.Clear();
            _weaponAttach?.RefreshPresentation();
            QueueEditorRefresh();
        }

        private void QueueEditorRefresh()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && IsPackagePreview)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
#endif
        }


        // ── Rendering (OnRenderObject → visible in Game view AND Scene view) ──

        private static Material _lineMat;
        private static Material LineMat
        {
            get
            {
                if (_lineMat == null)
                {
                    var shader = Shader.Find("Hidden/Internal-Colored");
                    _lineMat = shader != null ? new Material(shader) { hideFlags = HideFlags.HideAndDontSave } : null;
                }
                return _lineMat;
            }
        }

        private void OnRenderObject()
        {
#if UNITY_EDITOR
            var camera = UnityEngine.Camera.current;
            if (camera == null ||
                ((EditorSceneManager.IsPreviewScene(gameObject.scene) || EditorSceneManager.IsPreviewScene(camera.scene)) &&
                 camera.scene != gameObject.scene)) return;
#endif
            if (Def == null) return;
            if (Character == CharacterClass.None ? !IsPackagePreview : !Application.isPlaying) return;
            var mat = LineMat;
            if (mat == null) return;
            mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            if (ShowHurtboxes)
            {
                var hurtboxes = ResolveHurtboxes();
                for (int i = 0; i < hurtboxes.Count; i++)
                {
                    var hb = hurtboxes[i];
                    GL.Color(new Color(0f, 1f, 0.35f));
                    WireSphere(new Vector3(hb.PosX, hb.PosY, hb.PosZ), hb.Radius);
                }
            }
            if (ShowHitboxes)
            {
                foreach (var (index, evt, start, end) in ResolveHitboxes())
                {
                    GL.Color(index == SelectedHitboxEventIndex
                        ? new Color(1f, 1f, 0.1f)
                        : new Color(1f, 0.45f, 0f));
                    if (evt.Shape == HitboxShape.Capsule) WireCapsule(start, end, evt.Radius);
                    else WireSphere(start, evt.Radius);
                }
            }
            if (ShowDummy)
            {
                GL.Color(new Color(1f, 0.35f, 0.35f));
                foreach (var hb in ResolveDummyHurtboxes())
                {
                    if (hb.Shape == HitboxShape.Capsule)
                        WireCapsule(new Vector3(hb.PosX, hb.PosY, hb.PosZ), new Vector3(hb.EndX, hb.EndY, hb.EndZ), hb.Radius);
                    else WireSphere(new Vector3(hb.PosX, hb.PosY, hb.PosZ), hb.Radius);
                }
            }
            if (ShowTrajectory)
            {
                var arc = ResolveTrajectory();
                if (arc.Count > 1)
                {
                    // Hitstun portion in cyan, post-hitstun flight in blue; apex marker white.
                    for (int i = 0; i < arc.Count - 1; i++)
                    {
                        char phase = arc[i].phase;
                        GL.Color(phase == 'H' ? new Color(0.2f, 0.9f, 0.9f)
                            : phase == 'A' ? new Color(1f, 1f, 1f)
                            : new Color(0.3f, 0.55f, 1f));
                        Line(arc[i].pos, arc[i + 1].pos);
                    }
                    // Landing point: red marker.
                    GL.Color(new Color(1f, 0.3f, 0.2f));
                    WireSphere(arc[^1].pos, 0.12f);
                }
            }
            if (ShowBakedBones)
                DrawBakedBonePoints();
            GL.End();
            GL.PopMatrix();
        }

        private static void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        private static void WireSphere(Vector3 c, float r)
        {
            const int segs = 16;
            for (int ring = 0; ring < 3; ring++)
            {
                for (int i = 0; i < segs; i++)
                {
                    float t0 = i * 2f * Mathf.PI / segs, t1 = (i + 1) * 2f * Mathf.PI / segs;
                    Vector3 a = c, b = c;
                    switch (ring)
                    {
                        case 0: a = c + new Vector3(Mathf.Cos(t0) * r, 0, Mathf.Sin(t0) * r); b = c + new Vector3(Mathf.Cos(t1) * r, 0, Mathf.Sin(t1) * r); break;
                        case 1: a = c + new Vector3(Mathf.Cos(t0) * r, Mathf.Sin(t0) * r, 0); b = c + new Vector3(Mathf.Cos(t1) * r, Mathf.Sin(t1) * r, 0); break;
                        default: a = c + new Vector3(0, Mathf.Cos(t0) * r, Mathf.Sin(t0) * r); b = c + new Vector3(0, Mathf.Cos(t1) * r, Mathf.Sin(t1) * r); break;
                    }
                    Line(a, b);
                }
            }
        }
        private void DrawBakedBonePoints()
        {
            if (Baked == null) return;

            var spec = CurrentSpec();
            if (spec == null || !TryGetStage(out var stage)) return;

            string animName = AnimNameFor(spec, StageIndex);
            if (!ResolvePose(animName, stage.DurationTicks, out string resolvedAnim, out int bakedFrame)) return;

            float cos = Mathf.Cos(FacingYaw);
            float sin = Mathf.Sin(FacingYaw);
            float scale = DisplayDef?.HurtboxBoneScale ?? 1f;
            var state = new CharacterState { PX = transform.position.x, PY = BasePosition().y, PZ = transform.position.z };

            for (int i = 0; i < Baked.BoneNames.Length; i++)
            {
                string bone = Baked.BoneNames[i];
                if (!Baked.GetBonePosition(resolvedAnim, bakedFrame, i, out float x, out float y, out float z)) continue;

                bool weaponPoint = bone.StartsWith("_", StringComparison.Ordinal);
                float pointScale = weaponPoint ? 1f : scale;
                float wx = x * pointScale;
                float wy = y * pointScale;
                float wz = z * pointScale;
                var world = new Vector3(
                    state.PX + wx * cos + wz * sin,
                    DisplayDef.BoneYToWorldY(state.PY, wy),
                    state.PZ - wx * sin + wz * cos);

                GL.Color(bone == "_weapon_tip"
                    ? new Color(1f, 0.1f, 1f)
                    : bone == "_weapon_hilt"
                        ? new Color(1f, 1f, 0.1f)
                        : new Color(0.1f, 0.8f, 1f));
                WireSphere(world, weaponPoint ? 0.09f : 0.045f);
            }
        }

        private static void WireCapsule(Vector3 a, Vector3 b, float radius)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 0.0001f) { WireSphere(a, radius); return; }
            Vector3 dir = axis / len;
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(dir, up)) > 0.99f) up = Vector3.right;
            Vector3 right = Vector3.Cross(dir, up).normalized;
            Vector3 fwd = Vector3.Cross(right, dir).normalized;
            Vector3 a2 = a + dir * radius, b2 = b - dir * radius;
            const int segs = 12;
            for (int i = 0; i < segs; i++)
            {
                float t0 = i * 2f * Mathf.PI / segs, t1 = (i + 1) * 2f * Mathf.PI / segs;
                Vector3 p0 = a2 + (right * Mathf.Cos(t0) + fwd * Mathf.Sin(t0)) * radius;
                Vector3 p1 = a2 + (right * Mathf.Cos(t1) + fwd * Mathf.Sin(t1)) * radius;
                Line(p0, p1);
                Vector3 q0 = b2 + (right * Mathf.Cos(t0) + fwd * Mathf.Sin(t0)) * radius;
                Vector3 q1 = b2 + (right * Mathf.Cos(t1) + fwd * Mathf.Sin(t1)) * radius;
                Line(q0, q1);
                Line(p0, q0);
            }

            // The collision capsule includes hemispherical ends centered at the
            // resolved endpoints. The cylinder rings above are inset by one
            // radius, so draw the endpoint spheres too; without them the
            // wireframe appears shorter than the actual capsule.
            WireSphere(a, radius);
            WireSphere(b, radius);
        }
        private static string FormatDiagnostics(IReadOnlyList<CharacterDiagnostic> diagnostics)
            => string.Join("; ", diagnostics.Select(d => $"{d.Code} ({d.Path}): {d.Message}"));
    }
}
