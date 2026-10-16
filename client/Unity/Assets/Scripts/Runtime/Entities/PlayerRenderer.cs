using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using Animancer;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Animation;
using SlopArena.Client.UI;
using SlopArena.Client.Combat;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Visual representation of a CharacterState.
    /// Applies server-authoritative position, rotation, and drives
    /// animation state on an Animator. Works for both player and NPC entities.
    /// </summary>
    public class PlayerRenderer : MonoBehaviour
    {
        [Header("Entity")]
        [SerializeField] private string _entityName = "Entity";
        [SerializeField] private ulong _entityId;

        [Header("Animation")]
        [SerializeField] private AnimancerComponent _animancer;

        [SerializeField] private CharacterAnimationConfig _charConfig;
        private CharacterAnimationCatalog _animationCatalog;
        private AnimationClip _grabClip;
        private AnimationClip _throwClip;
        private AnimationClip _grabHoldClip, _grabbedHoldClip;
        private AnimationClip _grabWalkForwardClip, _grabWalkBackwardClip;
        private AnimationClip _grabbedWalkForwardClip, _grabbedWalkBackwardClip;
        private AnimationClip _thrownClip;
        private ProjectileVFXManager _projectileVFX;
        private Transform _kiShotHand;

        [Header("Thresholds")]
        [SerializeField] private float _runSpeedThreshold = 0.1f;

        [Header("Debug Visualization")]
        [SerializeField] private float _capsuleRadius = 0.6f;
        [SerializeField] private float _capsuleHeight = 1.3f;

        public float CapsuleRadius
        {
            get => _capsuleRadius;
            set => _capsuleRadius = value;
        }
        public float CapsuleHeight
        {
            get => _capsuleHeight;
            set => _capsuleHeight = value;
        }
        private HurtboxBoneDef[] _hurtboxBoneDefs = System.Array.Empty<HurtboxBoneDef>();

        // ── State color mapping for gizmo visualization ──

        private static readonly Dictionary<ActionState, Color> StateColors = new()
        {
            { ActionState.Idle,       new Color(1f, 1f, 1f, 0.8f) },       // white
            { ActionState.Hitstun,    new Color(1f, 0.2f, 0.2f, 0.8f) },   // red
            { ActionState.Sliding,    new Color(0.5f, 0.5f, 0.5f, 0.8f) }, // gray
            { ActionState.Attacking,  new Color(1f, 0.6f, 0f, 0.8f) },     // orange
            { ActionState.AirDodgeMovement, new Color(1f, 0.8f, 0f, 0.8f) }, // yellow
            { ActionState.AirDodgeRecovery, new Color(1f, 0.8f, 0f, 0.8f) }, // yellow
            { ActionState.JumpSquat,  new Color(0.8f, 0.3f, 1f, 0.8f) },   // purple
            { ActionState.Aiming,     new Color(0.3f, 1f, 1f, 0.8f) },     // cyan-light
            { ActionState.Run,        new Color(0f, 1f, 1f, 0.8f) },       // cyan
            { ActionState.LedgeHang,  new Color(1f, 0.8f, 0f, 0.8f) },     // yellow
        };
        private static readonly AnimationCurve AttackAccentWidth = new(
            new Keyframe(0f, 0f),
            new Keyframe(0.28f, 1f),
            new Keyframe(0.72f, 0.72f),
            new Keyframe(1f, 0f));

        private sealed class AttackAccentTrail
        {
            public GameObject Root;
            public TrailRenderer Primary;
            public TrailRenderer Secondary;
        }


        [Header("Visual Offset")]
        [SerializeField] private float _modelYOffset;

        [Header("Bone Trail VFX")]
        [SerializeField] private GameObject _boneTrailPrefab;
        private Dictionary<string, ParticleSystem> _activeTrails = new();
        private Dictionary<string, AttackAccentTrail> _attackAccentTrails = new();

        /// <summary>Character definition (capsule geometry, weight, baked bones) — set at spawn.</summary>
        public CharacterDefinition? CharacterDef => _charDef;

        /// <summary>Set the temporary/generated semantic animation catalog for package previews.</summary>
        public void SetAnimationCatalog(CharacterAnimationCatalog catalog) => _animationCatalog = catalog;
        /// <summary>Set animation config from MatchBase (drag-drop), skipping Resources.Load fallback.</summary>
        public void SetAnimationConfig(CharacterAnimationConfig config) => _charConfig = config;

        /// <summary>
        /// Y offset to align the visual model's feet with the collision capsule bottom.
        /// Set from CharacterDefinition.ModelYOffset (≈ -0.52 for Manki).
        /// </summary>
        public float ModelYOffset
        {
            get => _modelYOffset;
            set => _modelYOffset = value;
        }

        [SerializeField] private GameObject _modelPrefab;
        [SerializeField] private string _modelResourcePath = "";
        [SerializeField] private string _modelName = "";
        [SerializeField] private float _modelVisualScale = 1f;
        private CharacterDefinition? _charDef;
        private GameObject _modelInstance;
        private Vector3 _attackPoseModelOffset;

        private void ApplyAttackPoseModel(CharacterState state)
        {
            if (_modelInstance == null || _charDef == null) return;
            float pitch = state.AttackPosePitch;
            Transform model = _modelInstance.transform;
            model.localRotation = Quaternion.identity;
            model.localPosition = Vector3.zero;
            if (pitch == 0f)
            {
                model.localRotation = Quaternion.identity;
                model.localPosition = Vector3.zero;
                _attackPoseModelOffset = Vector3.zero;
                return;
            }

            Vector3 pivotWorld = new(state.PX,
                _charDef.BoneYToWorldY(state.PY, 0f), state.PZ);
            Vector3 pivotParent = transform.InverseTransformPoint(pivotWorld);
            Quaternion pitchRotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, 0f, 0f);
            Vector3 pivotScaled = Vector3.Scale(model.localScale, model.InverseTransformPoint(pivotWorld));
            Vector3 localOffset = pivotParent - pitchRotation * pivotScaled;
            model.localRotation = pitchRotation;
            model.localPosition = localOffset;
            _attackPoseModelOffset = localOffset;
        }
        private GameObject _shieldInstance;
        private bool _reportedModelRootDrift;

        // The simulation owns the entity root. Animation may pose the model,
        // but must never move that root or accumulate presentation drift.
        private const float ModelRootDriftWarningDistance = 0.001f;

        public void SetCharacterDefinition(CharacterDefinition? def) => _charDef = def;

        private ArenaCollision.BlastLines _blastLines;
        private bool _hasBlastLines;

        public void SetBlastLines(ArenaDefinition arena)
        {
            _blastLines = ArenaCollision.ResolveBlastLines(in arena);
            _hasBlastLines = true;
        }

        private Vector3 KnockoutLimitPosition(CharacterState state)
        {
            Vector3 position = new(state.PX, state.PY + _modelYOffset, state.PZ);
            if (!_hasBlastLines)
                return position;

            if (position.x < _blastLines.KillMinX)
                position.x = _blastLines.KillMinX;
            else if (position.x > _blastLines.KillMaxX)
                position.x = _blastLines.KillMaxX;
            if (position.z < _blastLines.KillMinZ)
                position.z = _blastLines.KillMinZ;
            else if (position.z > _blastLines.KillMaxZ)
                position.z = _blastLines.KillMaxZ;
            if (position.y < _blastLines.KillHeight + _modelYOffset)
                position.y = _blastLines.KillHeight + _modelYOffset;
            else if (position.y > _blastLines.KillTop + _modelYOffset)
                position.y = _blastLines.KillTop + _modelYOffset;
            return position;
        }

        /// <summary>
        /// Load the 3D model for this character from Resources.
        /// Reuses the current model when its source is unchanged and replaces only
        /// after the requested prefab has been resolved successfully.
        /// Must be called before ApplyServerState.
        /// </summary>
        /// <param name="def">Character definition with ModelResourcePath as fallback.</param>
        /// <param name="prefabOverride">If non-null, use this prefab instead of Resources.Load.</param>
        public void LoadModel(CharacterDefinition def, GameObject prefabOverride = null)
        {
            if (def == null) return;
            LoadSharedAnimations();
            string resourcePath = def.ModelResourcePath ?? "";
            if (prefabOverride == null && string.IsNullOrEmpty(resourcePath)) return;

            GameObject prefab = prefabOverride != null
                ? prefabOverride
                : Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[PlayerRenderer] Model not found — override null and Resources/{resourcePath} missing");
                return;
            }

#if UNITY_EDITOR
            if (Selection.activeTransform != null && Selection.activeTransform.IsChildOf(transform))
                Selection.activeObject = gameObject;
#endif

            bool sourceUnchanged = _modelInstance != null
                && _modelPrefab == prefab
                && _modelResourcePath == resourcePath;
            _modelPrefab = prefab;
            _modelResourcePath = resourcePath;
            _modelName = def.Class.ToString();
            _modelVisualScale = def.VisualScale;

            if (sourceUnchanged)
            {
                _modelInstance.name = _modelName;
                _modelInstance.transform.localScale = Vector3.one * _modelVisualScale;
                return;
            }

            ReplaceModel(prefab, _modelName, _modelVisualScale);
            ConfigureModelAnimation(def);
            if (TryGetAnimation(def.IdleAnim, out var idleClip, out _))
            {
                _currentAnimState = _animancer.Play(idleClip, 0f);
                _animancer.Evaluate(0f);
            }
        }

        /// <summary>
        /// Recreate the last successfully selected model only when its instance is missing.
        /// A missing source is a no-op so the last valid presentation remains untouched.
        /// </summary>
        public void EnsureModel()
        {
            if (_modelInstance != null && _modelInstance.transform.parent == transform && transform.childCount > 0)
                return;
            _modelInstance = null;

            GameObject prefab = _modelPrefab;
            if (prefab == null && !string.IsNullOrEmpty(_modelResourcePath))
                prefab = Resources.Load<GameObject>(_modelResourcePath);
            if (prefab == null) return;

            _modelPrefab = prefab;
            ReplaceModel(prefab, _modelName, _modelVisualScale);
        }


        private void HideShield()
        {
            if (_shieldInstance == null) return;
            _shieldInstance.SetActive(false);
            if (Application.isPlaying)
                Destroy(_shieldInstance);
            else
                DestroyImmediate(_shieldInstance);
            _shieldInstance = null;
        }

        private void UpdateShield(CharacterState state)
        {
            if (state.State != ActionState.Shielding || _charDef == null)
            {
                HideShield();
                return;
            }

            if (_shieldInstance == null)
            {
                GameObject prefab = Resources.Load<GameObject>("VFX/ShieldGuard");
                if (prefab == null) return;
                _shieldInstance = Instantiate(prefab, transform);
                _shieldInstance.name = "ShieldGuard";
                foreach (var collider in _shieldInstance.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                foreach (var renderer in _shieldInstance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (renderer.sharedMaterial == null || !renderer.sharedMaterial.HasProperty("_Opacity_Global"))
                        continue;
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    properties.SetFloat("_Opacity_Global", 0.35f);
                    renderer.SetPropertyBlock(properties);
                }
            }

            Transform shield = _shieldInstance.transform;
            shield.localPosition = new Vector3(0f, -_modelYOffset, 0f);
            shield.localRotation = Quaternion.identity;
            shield.localScale = Vector3.one * (2f * _charDef.ShieldRadius);
        }
        private void ReplaceModel(GameObject prefab, string modelName, float visualScale)
        {
            HideShield();
            _projectileVFX?.ClearHeldVisual(_entityId);
            _kiShotHand = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }

            var instance = Instantiate(prefab, transform);
            instance.name = modelName;
            _modelInstance = instance;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * visualScale;

            _animancer = instance.GetComponent<AnimancerComponent>();
            if (_animancer == null)
                _animancer = instance.AddComponent<AnimancerComponent>();

            var animator = instance.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.applyRootMotion = false;
            _weaponHips = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Hips)
                : FindBone("mixamorig:Hips");
        }

        private void ConfigureModelAnimation(CharacterDefinition def)
        {
            if (def.Class == CharacterClass.FightGuy && _animationCatalog == null)
            {
                Debug.LogError("[PlayerRenderer] FightGuy requires generated cooked animation assets.");
                return;
            }
            if (_animationCatalog == null && _charConfig == null)
            {
                string path = $"AnimationConfigs/{def.Class}_AnimConfig";
                _charConfig = Resources.Load<CharacterAnimationConfig>(path);
                if (_charConfig == null && !string.IsNullOrEmpty(def.ModelResourcePath))
                {
                    string modelClass = def.ModelResourcePath.Substring(
                        def.ModelResourcePath.LastIndexOf('/') + 1);
                    string fallbackPath = $"AnimationConfigs/{modelClass}_AnimConfig";
                    if (modelClass.Length > 0 && fallbackPath != path)
                        _charConfig = Resources.Load<CharacterAnimationConfig>(fallbackPath);
                    if (_charConfig != null)
                        Debug.Log($"[PlayerRenderer] {def.Class} has no AnimConfig — borrowing {modelClass} clips from Resources/{fallbackPath} (placeholder art)");
                    else
                        Debug.LogError($"[PlayerRenderer] AnimConfig not found at Resources/{path} (model fallback Resources/{fallbackPath} missing too)");
                }
            }
        }

        /// <summary>Hurtbox bone definitions for debug visualization.</summary>
        public HurtboxBoneDef[] HurtboxBoneDefs
        {
            get => _hurtboxBoneDefs;
            set => _hurtboxBoneDefs = value;
        }

        /// <summary>Entity display name (settable in Inspector).</summary>
        public string EntityName
        {
            get => _entityName;
            set => _entityName = value;
        }

        /// <summary>Server-assigned entity ID.</summary>
        public ulong EntityId
        {
            get => _entityId;
            set => _entityId = value;
        }

        /// <summary>
        /// Last applied action state. Read by weapon attach components.
        /// </summary>
        public ActionState CurrentActionState => _lastAnimState;

        /// <summary>
        /// Last applied attack slot (1-6). 0 when not attacking. Read by weapon attach components.
        /// </summary>
        public byte CurrentAttackSlot => _lastAttackSlot;
        /// <summary>Ticks since the current attack began (0 when not attacking). Read by weapon attach components.</summary>
        public int CurrentAttackElapsedTicks => _lastState.AttackElapsedTicks;
        /// <summary>Identity of the last applied attack, for attached presentation cleanup.</summary>
        public byte CurrentAttackSequence => _lastState.AttackSequence;
        /// <summary>Resolve the actual charge or release clip and its preview envelope.</summary>
        public bool TryGetAbilityPhaseAnimation(
            byte attackSlot,
            bool airborne,
            int comboStage,
            bool charge,
            out string clipName,
            out AnimationClip clip,
            out int durationTicks,
            out float playbackSpeed)
        {
            clipName = null;
            clip = null;
            durationTicks = 0;
            playbackSpeed = 1f;
            if (_charDef == null || attackSlot == 0)
                return false;
            var spec = _charDef.GetSlotAbility(attackSlot - 1, airborne);
            if (spec?.AnimationNames is not { Length: > 0 } || spec.Stages is not { Length: > 0 })
                return false;
            int animationIndex = comboStage % spec.AnimationNames.Length;
            if (charge)
            {
                clipName = spec.AimAnimationId;
                if (string.IsNullOrEmpty(clipName) || !TryGetAnimation(clipName, out clip, out _))
                    return false;
                // Do not add a duplicate loop-end tick for float-rounded clip lengths.
                durationTicks = Mathf.Max(1, Mathf.CeilToInt(clip.length * 60f - 0.0001f));
                return true;
            }

            clipName = spec.AnimationNames[animationIndex];
            if (!TryGetAnimation(clipName, out clip, out _))
                return false;
            playbackSpeed = GetAbilityAnimationSpeed(spec, comboStage, clipName);
            if (spec.AnimSpeed > 0f)
                durationTicks = Mathf.Max(1, Mathf.CeilToInt(clip.length / spec.AnimSpeed * 60f - 0.0001f));
            else
            {
                int bakedIndex = _bakedData != null ? _bakedData.FindAnimIndex(clipName) : -1;
                durationTicks = bakedIndex >= 0 && animationIndex < spec.Stages.Length
                    ? spec.Stages[animationIndex].DurationTicks
                    : Mathf.CeilToInt(clip.length * 60f - 0.0001f);
                if (comboStage > 0 && bakedIndex >= 0 && animationIndex < spec.Stages.Length)
                    durationTicks = Mathf.Min(durationTicks, _bakedData.Animations[bakedIndex].FrameCount);
            }
            durationTicks = Mathf.Max(1, durationTicks);
            return true;
        }


        private bool _currentAttackAirborne;

        /// <summary>
        /// Sample the authored blade pose on the Shared tick clock. The bake is
        /// hips-relative; cosmetic projection uses the animated hips, not the
        /// simulation's fixed hip-height approximation.
        /// </summary>
        public bool TrySampleBakedWeaponPath(
            in CharacterState state, bool airborne, out Vector3 hilt, out Vector3 tip)
        {
            hilt = tip = default;
            if (_animancer == null || _weaponHips == null
                || !TryGetBakedWeaponPose(state, airborne, out var pose, out var animationName, out int frame)
                || !TryGetAnimation(animationName, out var clip, out _))
                return false;
            if (_modelInstance != null)
            {
                _modelInstance.transform.localRotation = Quaternion.identity;
                _modelInstance.transform.localPosition = Vector3.zero;
            }
            // History needs a valid animated origin even before the rig becomes
            // visible to a camera; Unity's animator culling must not skip it.
            if (_animancer.Animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                _animancer.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var animation = _animancer.States.Current;
            if (animation == null || animation.Clip != clip
                || state.AttackElapsedTicks >= 3 && animation.Weight < 0.999f)
                animation = _animancer.Play(clip, 0f);
            // The existing 0.05s attack crossfade has finished after three Shared
            // ticks, even when several ticks arrive in one rendered frame.
            animation.Time = frame / 60f;
            animation.Speed = 0f;
            _animancer.Evaluate(0f);

            Vector3 hips = Quaternion.Inverse(transform.rotation)
                * (_weaponHips.position - transform.position);
            int hiltOffset = _weaponHiltBoneIndex * 3;
            int tipOffset = _weaponTipBoneIndex * 3;
            Vector3 localHilt = hips + new Vector3(pose[hiltOffset], pose[hiltOffset + 1], pose[hiltOffset + 2]);
            Vector3 localTip = hips + new Vector3(pose[tipOffset], pose[tipOffset + 1], pose[tipOffset + 2]);
            float sin = Mathf.Sin(state.FacingYaw);
            float cos = Mathf.Cos(state.FacingYaw);
            hilt = new Vector3(
                state.PX + localHilt.x * cos + localHilt.z * sin,
                state.PY + _modelYOffset + localHilt.y,
                state.PZ - localHilt.x * sin + localHilt.z * cos);
            tip = new Vector3(
                state.PX + localTip.x * cos + localTip.z * sin,
                state.PY + _modelYOffset + localTip.y,
                state.PZ - localTip.x * sin + localTip.z * cos);
            if (state.AttackPosePitch != 0f && _charDef != null)
            {
                float hx = hilt.x, hy = hilt.y, hz = hilt.z;
                float tx = tip.x, ty = tip.y, tz = tip.z;
                HitboxGeometry.ApplyAttackPosePitch(in state, _charDef, ref hx, ref hy, ref hz);
                HitboxGeometry.ApplyAttackPosePitch(in state, _charDef, ref tx, ref ty, ref tz);
                hilt = new Vector3(hx, hy, hz);
                tip = new Vector3(tx, ty, tz);
            }
            ApplyAttackPoseModel(state);
            return true;
        }

        private bool TryGetBakedWeaponPose(
            in CharacterState state, bool airborne, out float[] pose, out string animationName, out int frame)
        {
            pose = null;
            animationName = null;
            frame = 0;
            if (_bakedData == null || _charDef == null || state.AttackSlot == 0
                || _weaponHiltBoneIndex < 0 || _weaponTipBoneIndex < 0)
                return false;
            var spec = _charDef.GetSlotAbility((byte)(state.AttackSlot - 1), airborne);
            if (spec?.Stages is not { Length: > 0 } || spec.AnimationNames is not { Length: > 0 })
                return false;
            int stageIndex = Math.Min(state.ComboStage, spec.Stages.Length - 1);
            animationName = spec.AnimationNames[stageIndex % spec.AnimationNames.Length];
            int bakedIndex = _bakedData.FindAnimIndex(animationName);
            if (bakedIndex < 0)
                return false;
            var animation = _bakedData.Animations[bakedIndex];
            if (animation.FrameCount <= 0)
                return false;
            int durationTicks = spec.Stages[stageIndex].DurationTicks;
            frame = durationTicks > 0
                ? Math.Min(state.AttackElapsedTicks * animation.FrameCount / durationTicks, animation.FrameCount - 1)
                : Math.Min(state.AttackElapsedTicks, animation.FrameCount - 1);
            pose = animation.Frames[frame];
            return _weaponHiltBoneIndex * 3 + 2 < pose.Length
                && _weaponTipBoneIndex * 3 + 2 < pose.Length;
        }

        public bool IsSwordTrailTick(CharacterState state, bool airborne)
        {
            if (state.State != ActionState.Attacking || state.AttackSlot == 0 || _charDef == null)
                return false;
            var spec = _charDef.GetSlotAbility((byte)(state.AttackSlot - 1), airborne);
            if (spec?.Stages is not { Length: > 0 })
                return false;
            var stage = spec.Stages[Math.Min(state.ComboStage, spec.Stages.Length - 1)];
            if (state.AttackElapsedTicks >= stage.DurationTicks)
                return false;
            if (TryGetChargedSwordTrigger(state, airborne, out ushort finisherTick)
                && state.AttackElapsedTicks >= finisherTick)
                return true;
            var events = stage.HitboxEvents;
            if (events == null)
                return false;
            foreach (var hitbox in events)
                if (hitbox.BoneName == "_weapon_hilt"
                    && hitbox.EndBoneName == "_weapon_tip"
                    // The blade's visual follow-through outlives its damage window.
                    && state.AttackElapsedTicks >= hitbox.TriggerTick)
                    return true;
            return false;
        }
        private bool TryGetChargedSwordTrigger(in CharacterState state, bool airborne, out ushort triggerTick)
        {
            triggerTick = 0;
            var slot = _charDef?.GetCookedSlotAbility(state.AttackSlot, airborne);
            if (slot == null || slot.Timeline.Stages.Count == 0)
                return false;
            var operations = slot.Timeline.Stages[Math.Min(state.ComboStage, slot.Timeline.Stages.Count - 1)].Operations;
            for (int i = 0; i < operations.Count; i++)
                if (operations[i] is CookedStartCapabilityOperation
                    { Parameters: CookedChargedDirectionalDashCapabilityParameters dash }
                    && dash.FinisherHitbox.StartBoneId == "_weapon_hilt"
                    && dash.FinisherHitbox.EndBoneId == "_weapon_tip")
                {
                    triggerTick = dash.FinisherSeekTick;
                    return true;
                }
            return false;
        }


        private void CaptureWeaponTrailState(CharacterState state)
        {
            if (_weaponAttach == null)
                _weaponAttach = GetComponent<WeaponAttach>();
            if (_weaponAttach == null || !_weaponAttach.HasSwordTrail) return;
            Vector3 hilt = default, tip = default;
            bool hasPose = state.State == ActionState.Attacking
                && TrySampleBakedWeaponPath(state, _currentAttackAirborne, out hilt, out tip);
            _weaponAttach.CaptureState(state, _currentAttackAirborne, hasPose, hilt, tip);
        }

        private void CaptureAttackAirborneIdentity(CharacterState state)
        {
            // AttackSequence is the restart edge; an elapsed seek (Bonk E landing sync)
            // keeps the airborne identity captured at activation.
            if (state.State == ActionState.Attacking
                && (_lastState.State != ActionState.Attacking
                    || state.AttackSequence != _lastState.AttackSequence
                    || state.AttackSlot != _lastState.AttackSlot
                    || state.ComboStage != _lastState.ComboStage))
                _currentAttackAirborne = !state.IsGrounded;
        }




        public BakedAnimationData? BakedData => _bakedData;

        /// <summary>Expose the Animator for external access (e.g. VFX hooks).</summary>
        public Animator Animator => _animancer != null ? _animancer.Animator : null;

        // ── Tracking state for change detection ──

        private CharacterState _lastState;
        private bool _jumpArcActive;
        private ActionState _lastAnimState;
        private ushort _presentedHitReactionSequence;
        private byte _lastAttackSlot;
        private byte _lastComboStage;
        private int _deathFlashTicks;
        private const int DeathFlashDuration = 6;
        private bool _wasGrounded = true;
        private bool _wasAttacking;
        private bool _attackAccentsEnabled;
        private bool _attackStartedGrounded;
        private bool _hasAppliedState;
        private byte _lastPresentedDeaths;
        private bool _hasPresentedDeaths;
        private MovementFeedbackEffect _airDodgeTrail;
        private MovementFeedbackEffect _respawnInvincibilityEffect;
        private bool _respawnInvincibilityActive;
        private ulong _activeAccentWindowSignature;
        private ClipExtrapolator? _activeExtrapolator;
        private ExtrapolationMode _currentExtrapolationMode;
        private AnimancerState? _currentAnimState;
        // ── Hitstun extrapolation tracking ──
        private bool _inHitstunAnim;
        private float _hitstunAnimStartTime;
        private float _hitstunAnimLength;
        private string _lastHitstunAnimName = "";
        // ── Airborne launch tumble tracking ──
        private bool _tumbleActive;
        // ── Aerial landing-lag presentation ──
        private bool _landingLagActive;
        private float _tumbleReleaseNormalizedTime = float.PositiveInfinity;
        // ── Hitstop freeze tracking (ADR-0012) ──
        // True while the current clip is paused at Speed 0 for a hitstop freeze;
        // _hitstopPausedSpeed holds the pre-freeze speed for the resume frame.
        private bool _hitstopPaused;
        private float _hitstopPausedSpeed = 1f;
        private AudioSource _sfxSource;
        private AudioClip _jumpSfx;
        private readonly AudioClip[] _wibouSwordHitboxSfx = new AudioClip[7];
        private int _wibouSwordSfxSequence;
        private readonly AudioClip[] _bonkSwooshSfx = new AudioClip[7];
        private readonly AudioClip[] _commonSwooshSfx = new AudioClip[13];


        private void Awake()
        {
            if (_animancer == null)
                _animancer = GetComponent<AnimancerComponent>();
            if (_modelInstance == null && transform.childCount > 0)
                _modelInstance = transform.GetChild(0).gameObject;
            LoadSharedAnimations();
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("SFX");
            _sfxSource.playOnAwake = false;
            for (int i = 0; i < _wibouSwordHitboxSfx.Length; i++)
                _wibouSwordHitboxSfx[i] = Resources.Load<AudioClip>(
                    $"Audio/SFX/Wibou/hit_{i + 1:00}");
            for (int i = 0; i < _bonkSwooshSfx.Length; i++)
                _bonkSwooshSfx[i] = Resources.Load<AudioClip>(
                    $"Audio/SFX/Bonk/bonk_swoosh_{i + 1:00}");
            for (int i = 0; i < _commonSwooshSfx.Length; i++)
                _commonSwooshSfx[i] = Resources.Load<AudioClip>(
                    $"Audio/SFX/Whoosh/whoosh_{i + 1:00}");
            _sfxSource.spatialBlend = 0f;
            _sfxSource.volume = 0.7f;
            _jumpSfx = Resources.Load<AudioClip>("Audio/SFX/jump");
            if (Application.isPlaying)
                MovementFeedbackEffect.Prewarm();
        }
        // ── Frame-by-frame animation control ──

        private BakedAnimationData? _bakedData;
        private int _weaponHiltBoneIndex = -1;
        private int _weaponTipBoneIndex = -1;
        private WeaponAttach _weaponAttach;
        private Transform _weaponHips;

        /// <summary>
        /// Set baked skeleton data for frame-accurate animation.
        /// Must be called before first ApplyServerState.
        /// </summary>
        public void SetBakedData(BakedAnimationData? data)
        {
            _bakedData = data;
            _weaponHiltBoneIndex = data?.BoneNames != null
                ? Array.IndexOf(data.BoneNames, "_weapon_hilt") : -1;
            _weaponTipBoneIndex = data?.BoneNames != null
                ? Array.IndexOf(data.BoneNames, "_weapon_tip") : -1;
        }

        /// <summary>
        /// Sample an explicitly selected clip at normalized time with zero speed.
        /// State-driven package previews use PlayScrubbedState to follow runtime
        /// animation phases and playback speed. A missing clip preserves the prior pose.
        /// </summary>
        public void PlayScrubbed(string clipName, float normalizedTime)
        {
            if (_animancer == null || !TryGetAnimation(clipName, out var clip, out _)) return;
            if (!Application.isPlaying) HideShield();
            var state = _animancer.Play(clip, 0f);
            state.Time = clip.length > 0f ? normalizedTime * clip.length : 0f;
            state.Speed = 0f;
            EvaluateScrubbedPose();
        }

        private void EvaluateScrubbedPose()
        {
            var animator = _animancer.Animator;
            var cullingMode = animator.cullingMode;
            if (cullingMode == AnimatorCullingMode.AlwaysAnimate)
            {
                _animancer.Evaluate(0f);
                return;
            }

            // Explicit seeks must write bones even before any camera has seen
            // this model. Keep ordinary runtime visibility culling unchanged.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            try { _animancer.Evaluate(0f); }
            finally { animator.cullingMode = cullingMode; }
        }

        private void LoadSharedAnimations()
        {
            if (_grabClip != null && _throwClip != null && _grabHoldClip != null && _grabbedHoldClip != null
                && _grabWalkForwardClip != null && _grabWalkBackwardClip != null
                && _grabbedWalkForwardClip != null && _grabbedWalkBackwardClip != null && _thrownClip != null) return;
            var shared = Resources.Load<CharacterAnimationConfig>("AnimationConfigs/Shared_AnimConfig");
            _grabClip = shared?.GetClipByName("grab");
            _throwClip = shared?.GetClipByName("throw");
            _grabHoldClip = shared?.GetClipByName("grab_idle_a");
            _grabbedHoldClip = shared?.GetClipByName("grab_idle_b");
            _grabWalkForwardClip = shared?.GetClipByName("Grab_Walk_F_A");
            _grabWalkBackwardClip = shared?.GetClipByName("Grab_Walk_B_A");
            _grabbedWalkForwardClip = shared?.GetClipByName("Grab_Walk_F_B");
            _grabbedWalkBackwardClip = shared?.GetClipByName("Grab_Walk_B_B");
            _thrownClip = shared?.GetClipByName("Grab_Throw_B");
        }

        private bool TryResolveGrabAnimation(in CharacterState snapshot, out AnimationClip clip, out float time)
        {
            clip = null;
            time = 0f;
            LoadSharedAnimations();
            int duration;
            bool loop = false;
            if (snapshot.State == ActionState.GrabAttempt)
            {
                clip = _grabClip;
                duration = DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks + DefenseConfig.GrabWhiffRecoveryTicks;
            }
            else if (snapshot.State == ActionState.Throwing)
            {
                clip = _throwClip;
                duration = DefenseConfig.ThrowAnimationTicks;
            }
            else if (snapshot.State == ActionState.Thrown
                || snapshot.State == ActionState.Grabbed && snapshot.InteractionPhase == (byte)DefenseInteractionPhase.Throwing)
            {
                clip = _thrownClip;
                duration = DefenseConfig.ThrownAnimationTicks;
            }
            else if (snapshot.State is ActionState.Grabbing or ActionState.Grabbed)
            {
                bool grabber = snapshot.State == ActionState.Grabbing;
                float yaw = snapshot.CapturedYaw * (Mathf.PI / 18000f);
                bool forward = snapshot.VX * Mathf.Sin(yaw) + snapshot.VZ * Mathf.Cos(yaw) >= 0f;
                bool walking = snapshot.VX * snapshot.VX + snapshot.VZ * snapshot.VZ > 0.0025f;
                clip = !walking ? (grabber ? _grabHoldClip : _grabbedHoldClip)
                    : grabber ? (forward ? _grabWalkForwardClip : _grabWalkBackwardClip)
                    : forward ? _grabbedWalkForwardClip : _grabbedWalkBackwardClip;
                duration = DefenseConfig.GrabHoldTicks;
                loop = true;
            }
            else return false;

            if (clip == null)
            {
                // Shared grab clips are local installation assets, not package bindings.
                LastAnimationClipNotInstalled = true;
                return false;
            }
            time = Mathf.Max(0, duration - snapshot.StateTicks) * SlopArena.Shared.Simulation.TickDt;
            if (snapshot.State == ActionState.Thrown && !snapshot.IsGrounded)
                time = Mathf.Min(time, (DefenseConfig.ThrownLandingTicks - 2) * SlopArena.Shared.Simulation.TickDt);
            time = loop && clip.length > 0f ? time % clip.length : Mathf.Min(time, clip.length);
            return true;
        }

        /// <summary>Sample a recorded Shared state without Play Mode audio or movement effects.</summary>
        private bool TryResolveScrubbedState(in CharacterState snapshot, bool airborne, int poseTicks,
            out AnimationClip clip, out float time)
        {
            clip = null;
            time = 0f;
            LastAnimationClipNotInstalled = false;
            if (_animancer == null || _charDef == null) return false;
            if (snapshot.State is ActionState.GrabAttempt or ActionState.Grabbing or ActionState.Grabbed
                or ActionState.Throwing or ActionState.Thrown)
            {
                return TryResolveGrabAnimation(in snapshot, out clip, out time);
            }
            else
            {
                string id;
                float speed = 1f;
                int elapsed = poseTicks;
                if (snapshot.State is ActionState.Attacking or ActionState.Aiming && snapshot.AttackSlot > 0)
                {
                    var spec = _charDef.GetSlotAbility(snapshot.AttackSlot - 1, airborne);
                    if (spec?.AnimationNames is not { Length: > 0 } || spec.Stages is not { Length: > 0 })
                        return false;
                    int animationIndex = snapshot.ComboStage % spec.AnimationNames.Length;
                    id = snapshot.State == ActionState.Aiming && !string.IsNullOrEmpty(spec.AimAnimationId)
                        ? spec.AimAnimationId : spec.AnimationNames[animationIndex];
                    if (snapshot.State == ActionState.Attacking)
                    {
                        speed = GetAbilityAnimationSpeed(spec, snapshot.ComboStage, id);
                        elapsed = snapshot.AttackElapsedTicks;
                    }
                }
                else if (snapshot.State == ActionState.Hitstun)
                {
                    id = !snapshot.IsGrounded && (snapshot.KVX * snapshot.KVX + snapshot.KVZ * snapshot.KVZ > 0.001f)
                        && TryGetAnimation(_charDef.TumbleAnim, out _, out _)
                        ? _charDef.TumbleAnim
                        : snapshot.HitstunLevel switch
                        {
                            1 => _charDef.HitMediumAnim,
                            2 => _charDef.HitHardAnim,
                            _ => _charDef.HitSmallAnim,
                        };
                }
                else if (snapshot.State == ActionState.Crouching) id = _charDef.CrouchAnim;
                else if (snapshot.State == ActionState.Sliding) id = _charDef.SlideAnim;
                else if (!snapshot.IsGrounded) id = snapshot.VY > 0f ? _charDef.JumpAnim : _charDef.FallAnim;
                else id = snapshot.VX * snapshot.VX + snapshot.VZ * snapshot.VZ > _runSpeedThreshold * _runSpeedThreshold
                    ? _charDef.RunAnim : _charDef.IdleAnim;
                if (!TryGetAnimation(id, out clip, out _)) return false;
                time = elapsed / 60f * speed;
            }
            return true;
        }

        public bool CanPlayScrubbedState(in CharacterState snapshot, bool airborne)
            => TryResolveScrubbedState(snapshot, airborne, 0, out _, out _);

        public bool PlayScrubbedState(in CharacterState snapshot, bool airborne, int poseTicks)
        {
            if (!TryResolveScrubbedState(snapshot, airborne, poseTicks, out var clip, out float time))
                return false;
            transform.SetPositionAndRotation(
                new Vector3(snapshot.PX, snapshot.PY + _modelYOffset, snapshot.PZ),
                Quaternion.Euler(0f, snapshot.FacingYaw * Mathf.Rad2Deg, 0f));
            _hasInterpolationTarget = false;
            _simulatedPosition = transform.position;
            _visualCorrection = Vector3.zero;
            var animation = _animancer.Play(clip, 0f);
            animation.Time = clip.isLooping ? time : Mathf.Min(time, clip.length);
            animation.Speed = 0f;
            _currentAnimState = animation;
            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            _lastState = snapshot;
            _lastAnimState = snapshot.State;
            _lastAttackSlot = snapshot.AttackSlot;
            _lastComboStage = snapshot.ComboStage;
            _currentAttackAirborne = airborne;
            _wasAttacking = snapshot.State == ActionState.Attacking;
            _wasGrounded = snapshot.IsGrounded;
            UpdateShield(snapshot);
            EvaluateScrubbedPose();
            ApplyAttackPoseModel(snapshot);
            return true;
        }

        /// <summary>
        /// True when the last animation lookup found a binding whose clip is not
        /// installed in this checkout (ADR-0038), as opposed to a missing binding.
        /// </summary>
        public bool LastAnimationClipNotInstalled { get; private set; }

        private bool TryGetAnimation(
            string semanticId, out AnimationClip clip, out ExtrapolationMode extrapolation)
        {
            clip = null;
            extrapolation = ExtrapolationMode.None;
            LastAnimationClipNotInstalled = false;
            if (_animationCatalog != null)
            {
                foreach (var entry in _animationCatalog.Animations ?? Array.Empty<CharacterAnimationCatalog.AnimationEntry>())
                {
                    if (entry != null && entry.SemanticId == semanticId)
                    {
                        clip = entry.Clip;
                        extrapolation = entry.Extrapolation;
                        LastAnimationClipNotInstalled = clip == null;
                        return clip != null;
                    }
                }
                return false;
            }
            clip = _charConfig?.GetClipByName(semanticId);
            return clip != null;
        }

        // ── Lifecycle ──


        private void OnDisable()
        {
            HideShield();
            _projectileVFX?.ClearHeldVisual(_entityId);
            if (_airDodgeTrail != null)
            {
                _airDodgeTrail.EndDashTrail();
                _airDodgeTrail = null;
            }
            if (_respawnInvincibilityEffect != null)
            {
                _respawnInvincibilityEffect.EndInvincibility();
                _respawnInvincibilityEffect = null;
            }
            _respawnInvincibilityActive = false;
        }
        private void Start()
        {
            if (_boneTrailPrefab == null)
            {
                _boneTrailPrefab = Resources.Load<GameObject>("VFX/BoneTrail");
                if (_boneTrailPrefab == null)
                    Debug.LogWarning("[PlayerRenderer] BoneTrail prefab not found at Resources/VFX/BoneTrail");
            }
        }


        // ── Public API ──

        /// <summary>
        /// Apply a server-authoritative CharacterState to this entity.
        /// Updates position, rotation, and drives animation state.
        /// Call from Update() after reconciliation.
        /// </summary>
        private void EmitSwordHitboxSfx(CharacterState state)
        {
            bool wibou = _charDef?.Class == CharacterClass.Wibou;
            bool bonk = _charDef?.Class == CharacterClass.Bonk;
            bool common = _charDef?.Class is CharacterClass.Manki or CharacterClass.FightGuy;
            if (!_hasAppliedState
                || (!wibou && !bonk && !common)
                || state.AttackSlot == 0
                || (state.State != ActionState.Attacking && state.State != ActionState.Aiming))
                return;

            if (state.AttackElapsedTicks == _lastState.AttackElapsedTicks
                && state.AttackSequence == _lastState.AttackSequence)
                return;

            var ability = _charDef.GetSlotAbility(
                (byte)(state.AttackSlot - 1), airborne: !state.IsGrounded);
            if (ability?.Stages is not { Length: > 0 })
                return;

            if (state.State == ActionState.Attacking
                && TryGetChargedSwordTrigger(state, _currentAttackAirborne, out ushort finisherTick)
                && _lastState.AttackElapsedTicks < finisherTick
                && state.AttackElapsedTicks >= finisherTick)
            {
                PlaySwordHitboxSfx(wibou, bonk);
                return;
            }
            int stageIndex = Math.Min(state.ComboStage, ability.Stages.Length - 1);
            var hitboxes = ability.Stages[stageIndex].HitboxEvents;
            if (hitboxes == null)
                return;

            foreach (var hitbox in hitboxes)
            {
                if (_lastState.AttackElapsedTicks > hitbox.TriggerTick
                    || state.AttackElapsedTicks < hitbox.TriggerTick)
                    continue;

                PlaySwordHitboxSfx(wibou, bonk);
                return;
            }
        }

        private void PlaySwordHitboxSfx(bool wibou, bool bonk)
        {
            AudioClip[] pool = wibou ? _wibouSwordHitboxSfx : bonk ? _bonkSwooshSfx : _commonSwooshSfx;
            AudioClip clip = pool[_wibouSwordSfxSequence++ % pool.Length];
            if (clip != null)
                _sfxSource.PlayOneShot(clip);
        }

        /// <summary>
        /// Draw the fighter between fixed simulation steps instead of jumping once per step,
        /// for matches that apply state from FixedUpdate. Adds at most one step of display delay.
        /// </summary>
        public bool RenderInterpolation { get; set; }
        private Vector3 _simulatedPosition;
        private Vector3 _visualCorrection;
        private Vector3 _interpolateFrom, _interpolateTo;
        private Quaternion _interpolateFromRotation = Quaternion.identity, _interpolateToRotation = Quaternion.identity;
        private bool _hasInterpolationTarget;
        private const float CorrectionDecayPerStep = 0.8f;
        private const float MaxSmoothedCorrection = 3f;

        /// <summary>
        /// The simulation moved this fighter by <paramref name="delta"/> without time passing
        /// (authoritative correction or prediction-track switch). Keep it drawn where it was
        /// and ease the difference out over a few steps; teleports still snap.
        /// </summary>
        public void AddVisualCorrection(Vector3 delta)
        {
            if (!_hasAppliedState || delta.sqrMagnitude < 1e-8f) return;
            _visualCorrection -= delta;
            if (_visualCorrection.sqrMagnitude > MaxSmoothedCorrection * MaxSmoothedCorrection)
                _visualCorrection = Vector3.zero;
        }

        private void Update()
        {
            if (!RenderInterpolation || !_hasInterpolationTarget) return;
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            transform.SetPositionAndRotation(
                Vector3.LerpUnclamped(_interpolateFrom, _interpolateTo, alpha),
                Quaternion.Slerp(_interpolateFromRotation, _interpolateToRotation, alpha));
        }

        public void ApplyServerState(CharacterState state)
        {
            Vector3 targetPos = new Vector3(state.PX, state.PY + _modelYOffset, state.PZ);

            bool stockTransition = _hasAppliedState
                && state.Deaths != _lastState.Deaths
                && (!_hasPresentedDeaths || state.Deaths != _lastPresentedDeaths);
            if (stockTransition)
            {
                PresentStockTransition(_lastState, state, targetPos);
                _lastPresentedDeaths = state.Deaths;
                _hasPresentedDeaths = true;
            }
            if (_hasAppliedState
                && state.JumpsLeft < _lastState.JumpsLeft
                && _jumpSfx != null)
                _sfxSource.PlayOneShot(_jumpSfx);
            EmitSwordHitboxSfx(state);

            if (!_hasAppliedState || stockTransition)
            {
                _simulatedPosition = targetPos;
                _visualCorrection = Vector3.zero;
            }
            else if (state.HitstunTicks > 0)
                _simulatedPosition = Vector3.Lerp(_simulatedPosition, targetPos, 0.6f);
            else
                _simulatedPosition = targetPos;   // Normal movement follows the simulation exactly.
            _visualCorrection *= CorrectionDecayPerStep;
            if (_visualCorrection.sqrMagnitude < 1e-6f)
                _visualCorrection = Vector3.zero;
            Vector3 drawnPosition = _simulatedPosition + _visualCorrection;
            Quaternion drawnRotation = Quaternion.Euler(0f, state.FacingYaw * Mathf.Rad2Deg, 0f);
            if (RenderInterpolation && Application.isPlaying)
            {
                bool snap = !_hasInterpolationTarget || stockTransition;
                _interpolateFrom = snap ? drawnPosition : _interpolateTo;
                _interpolateFromRotation = snap ? drawnRotation : _interpolateToRotation;
                _interpolateTo = drawnPosition;
                _interpolateToRotation = drawnRotation;
                _hasInterpolationTarget = true;
                drawnPosition = _interpolateFrom;
                drawnRotation = _interpolateFromRotation;
            }
            transform.position = drawnPosition;
            if (Application.isPlaying)
            {
                UpdateAirDodgeFeedback(state, targetPos);
                if (!stockTransition)
                    EmitMovementFeedback(state, targetPos);
                UpdateRespawnInvincibility(state, targetPos);
            }

            transform.rotation = drawnRotation;
            // Death flash blink
            if (_deathFlashTicks > 0)
            {
                _deathFlashTicks--;
                if (_modelInstance != null)
                {
                    var mr = _modelInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (mr != null)
                        mr.enabled = (_deathFlashTicks % 2 == 0);
                }
            }
            else
            {
                if (_modelInstance != null)
                {
                    var mr = _modelInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (mr != null && !mr.enabled)
                        mr.enabled = true;
                }
            }


            CaptureAttackAirborneIdentity(state);
            UpdateAnimationState(state);
            MaintainTumbleLoop();
            ApplyAttackPoseModel(state);

            UpdateShield(state);
            CaptureWeaponTrailState(state);
            UpdateKiShotVisual(state);
            _lastState = state;
            _hasAppliedState = true;
            if (!_hasPresentedDeaths)
            {
                _lastPresentedDeaths = state.Deaths;
                _hasPresentedDeaths = true;
            }
        }

        private void UpdateKiShotVisual(CharacterState state)
        {
            if (_charDef?.Class != CharacterClass.FightGuy)
            {
                _projectileVFX?.ClearHeldVisual(_entityId);
                return;
            }
            if (_projectileVFX == null && state.State == ActionState.Aiming)
                _projectileVFX = FindFirstObjectByType<ProjectileVFXManager>();
            if (_projectileVFX == null) return;
            if (_kiShotHand == null && state.State == ActionState.Aiming)
                _kiShotHand = ResolvePresentationBone("bone.right-hand");
            _projectileVFX.UpdateHeldVisual(_entityId, state, _kiShotHand, _charDef);
        }

        private void PresentStockTransition(
            CharacterState previous, CharacterState respawned, Vector3 respawnPosition)
        {
            Vector3 deathDirection = new(previous.KVX, previous.KVY, previous.KVZ);
            if (deathDirection.sqrMagnitude < 0.01f)
                deathDirection = new(previous.VX, previous.VY, previous.VZ);
            if (deathDirection.sqrMagnitude < 0.01f)
                deathDirection = Vector3.up;

            MovementFeedbackEffect.Spawn(
                KnockoutLimitPosition(previous), deathDirection, MovementFeedbackKind.Knockout);
            OnDeath();

            if (respawned.InvincibilityTicks == 0)
                return;

            MovementFeedbackEffect.SpawnRespawn(
                respawnPosition, Vector3.up, RespawnColor());
            _respawnInvincibilityEffect?.EndInvincibility();
            _respawnInvincibilityEffect = MovementFeedbackEffect.BeginInvincibility(
                respawnPosition, RespawnColor());
            _respawnInvincibilityActive = _respawnInvincibilityEffect != null;
        }

        private void UpdateRespawnInvincibility(CharacterState state, Vector3 position)
        {
            if (!_respawnInvincibilityActive)
                return;
            if (state.InvincibilityTicks == 0)
            {
                _respawnInvincibilityEffect?.EndInvincibility();
                _respawnInvincibilityEffect = null;
                _respawnInvincibilityActive = false;
                return;
            }
            _respawnInvincibilityEffect?.FollowInvincibility(position);
        }

        private Color RespawnColor() => _charDef?.Class switch
        {
            CharacterClass.Manki => new Color(1f, 0.36f, 0.08f, 0.95f),
            CharacterClass.FightGuy => new Color(0.28f, 0.72f, 1f, 0.95f),
            CharacterClass.Wibou => new Color(0.76f, 0.9f, 1f, 0.95f),
            CharacterClass.Bonk => new Color(0.96f, 0.58f, 0.18f, 0.95f),
        };

        private void UpdateAirDodgeFeedback(CharacterState state, Vector3 position)
        {
            if (state.State != ActionState.AirDodgeMovement)
            {
                if (_airDodgeTrail != null)
                {
                    _airDodgeTrail.EndDashTrail();
                    _airDodgeTrail = null;
                }
                return;
            }

            Vector3 airDodgeDirection = new(state.DashDirX, 0f, state.DashDirZ);
            if (airDodgeDirection.sqrMagnitude < 0.001f)
                airDodgeDirection = new Vector3(state.VX, 0f, state.VZ);
            Vector3 trailDirection = -airDodgeDirection;
            if (_airDodgeTrail == null)
                _airDodgeTrail = MovementFeedbackEffect.BeginDashTrail(position, trailDirection);
            else
                _airDodgeTrail.FollowDashTrail(position, trailDirection);
        }

        private void EmitMovementFeedback(CharacterState state, Vector3 position)
        {
            if (!_hasAppliedState)
            {
                _hasAppliedState = true;
                return;
            }



            bool tookOff = _lastState.IsGrounded && !state.IsGrounded;
            bool doubleJumped = !_lastState.IsGrounded
                && !state.IsGrounded
                && state.JumpsLeft < _lastState.JumpsLeft;
            if (doubleJumped)
            {
                MovementFeedbackEffect.Spawn(position, Vector3.down, MovementFeedbackKind.DoubleJump);
            }
            else if (tookOff && state.HitstunTicks == 0)
            {
                MovementFeedbackEffect.Spawn(position, Vector3.down, MovementFeedbackKind.Jump);
            }

            if (!_lastState.IsGrounded && state.IsGrounded)
            {
                float landingSpeed = Mathf.Max(0f, -_lastState.VY);
                MovementFeedbackKind kind = landingSpeed >= 24f
                    ? MovementFeedbackKind.HeavyLanding
                    : MovementFeedbackKind.Landing;
                MovementFeedbackEffect.Spawn(position, Vector3.up, kind);
            }

            if (_charDef != null && !state.IsGrounded && state.VY < 0f)
            {
                float fastFallSpeed = _charDef.Movement.FastFallSpeed;
                bool isFastFall = Mathf.Abs(state.VY + fastFallSpeed) <= 0.25f;
                bool wasFastFall = Mathf.Abs(_lastState.VY + fastFallSpeed) <= 0.25f;
                if (isFastFall && !wasFastFall)
                    MovementFeedbackEffect.Spawn(position, Vector3.up, MovementFeedbackKind.FastFall);
            }

            Vector3 knockback = new(state.KVX, state.KVY, state.KVZ);
            Vector3 previousKnockback = new(_lastState.KVX, _lastState.KVY, _lastState.KVZ);
            const float launchSpeed = 12f;
            if (knockback.sqrMagnitude >= launchSpeed * launchSpeed
                && previousKnockback.sqrMagnitude < launchSpeed * launchSpeed)
            {
                MovementFeedbackEffect.Spawn(position, -knockback, MovementFeedbackKind.Launch);
            }
        }

        // ── Animancer-driven animation ──
        //
        /// <summary>
        /// Compute animation playback speed so that <c>frameCount</c> frames from
        /// baked data play in exactly <c>durationTicks</c> ticks (at 60 Hz).
        /// Returns 1f when baked data or the named animation is unavailable.
        /// </summary>
        private float GetAnimSpeedFromDuration(string animName, int durationTicks)
        {
            if (_bakedData == null || durationTicks <= 0) return 1f;
            int bakedIdx = _bakedData.FindAnimIndex(animName);
            if (bakedIdx < 0) return 1f;
            int frameCount = _bakedData.Animations[bakedIdx].FrameCount;
            return (float)frameCount / durationTicks;
        }

        private void ClearLandingLagPresentation()
        {
            _landingLagActive = false;
        }
        private bool TryPlayLowPose(string animationId)
        {
            if (string.IsNullOrEmpty(animationId) || _bakedData == null)
                return false;
            int bakedIndex = _bakedData.FindAnimIndex(animationId);
            if (bakedIndex < 0 || _bakedData.Animations[bakedIndex].FrameCount <= 0
                || !TryGetAnimation(animationId, out var clip, out _))
                return false;

            var current = _animancer.States.Current;
            AnimancerState poseState = current != null && current.Clip == clip
                ? current
                : _animancer.Play(clip, 0f);
            poseState.Time = 0f;
            poseState.Speed = 0f;
            _currentAnimState = poseState;
            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            return true;
        }

        private float GetAbilityAnimationSpeed(AbilitySpec spec, int comboStage, string animationName)
        {
            if (spec.AnimSpeed > 0f) return spec.AnimSpeed;
            int animationIndex = comboStage % spec.AnimationNames.Length;
            if (_bakedData == null || spec.Stages == null || animationIndex >= spec.Stages.Length)
                return 1f;
            int bakedIndex = _bakedData.FindAnimIndex(animationName);
            if (bakedIndex < 0) return 1f;
            int frameCount = _bakedData.Animations[bakedIndex].FrameCount;
            int durationTicks = spec.Stages[animationIndex].DurationTicks;
            if (comboStage > 0 && durationTicks > frameCount)
                durationTicks = frameCount;
            return durationTicks > 0 ? (float)frameCount / durationTicks : 1f;
        }


        /// <summary>
        /// Play the ability clip for a combo stage with baked-frame speed and
        /// extrapolation overrides. Returns true when a clip played.
        /// </summary>
        private bool PlayAbilityAnim(CharacterState state, AbilitySpec spec, int comboStage)
        {
            if (spec == null)
                return false;

            string animName = "spell_lmb_1";
            float animSpeed = 1f;
            if (spec.AnimationNames != null && spec.AnimationNames.Length > 0)
            {
                // Stage index may exceed the authored clip list (single-anim slots wrap —
                // e.g. Manki A release ComboStage 1, only anim.manki.ga). GetAnimationName
                // wraps; using the wrapped index keeps the baked-duration lookup consistent.
                int stageIdx = comboStage % spec.AnimationNames.Length;
                animName = spec.AnimationNames[stageIdx];
                animSpeed = GetAbilityAnimationSpeed(spec, comboStage, animName);
            }

            if (!TryGetAnimation(animName, out var clip, out var extrapolation))
                return false;

            float fadeDuration = _lastAnimState is ActionState.Crouching or ActionState.Sliding ? 0f : 0.05f;
            var animState = _animancer.Play(clip, fadeDuration);
            // Animancer intentionally preserves a reused state's time. Attack
            // restarts, including same-slot IASA, must rewind the clip.
            animState.Time = 0;
            animState.Speed = animSpeed;
            _currentExtrapolationMode = ExtrapolationMode.None;
            // Cooked catalogs own extrapolation metadata; legacy configs retain
            // their existing per-definition overrides.
            if (_bakedData != null && extrapolation == ExtrapolationMode.Continuous)
            {
                _currentExtrapolationMode = ExtrapolationMode.Continuous;
                _activeExtrapolator = ClipExtrapolator.FromBakedData(_bakedData, animName);
            }
            if (_bakedData != null && _currentExtrapolationMode == ExtrapolationMode.None
                && _animationCatalog == null && _charDef?.ClipOverrides != null)
            {
                foreach (var cfg in _charDef.ClipOverrides)
                {
                    if (cfg.Name == animName && cfg.Extrapolation == ExtrapolationMode.Continuous)
                    {
                        _currentExtrapolationMode = ExtrapolationMode.Continuous;
                        _activeExtrapolator = ClipExtrapolator.FromBakedData(_bakedData, animName);
                        break;
                    }
                }
            }
            return true;
        }

        private bool IsStrongAirborneLaunch(CharacterState state)
        {
            if (state.IsGrounded || !state.WasAirborneDuringKnockback)
                return false;

            // InPostHitstunFlight is authoritative in the local simulation. The
            // motion fallback keeps the presentation correct for states reconstructed
            // from the wire, where that server-local marker is not serialized.
            if (state.InPostHitstunFlight)
                return true;

            const float motionEpsilon = 0.01f;
            return Mathf.Abs(state.VY) > motionEpsilon
                || state.VX * state.VX + state.VZ * state.VZ > motionEpsilon * motionEpsilon;
        }

        private void ClearTumble()
        {
            _tumbleActive = false;
            _tumbleReleaseNormalizedTime = float.PositiveInfinity;
        }

        private bool TryPlayFall(float fadeDuration)
        {
            if (!TryGetAnimation(_charDef?.FallAnim ?? "fall", out var clip, out _))
                return false;

            _animancer.Play(clip, fadeDuration);
            _jumpArcActive = false;
            return true;
        }

        private bool TryPlayTumble()
        {
            if (!TryGetAnimation(_charDef.TumbleAnim, out var clip, out _))
                return false;

            // Tumble loops while hitstun is active. Once the server makes the
            // fighter actionable, the current cycle is allowed to finish before
            // falling. The explicit release time handles Loop Time-enabled clips.
            clip.wrapMode = WrapMode.Loop;
            var tumbleState = _animancer.Play(clip, 0.5f);
            tumbleState.Speed = 1f;
            _tumbleActive = true;
            _tumbleReleaseNormalizedTime = float.PositiveInfinity;
            _currentAnimState = tumbleState;
            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            return true;
        }

        private void UpdateAnimationState(CharacterState state)
        {
            if (_animancer == null || _charDef == null)
                return;
            if (state.State != ActionState.Attacking && _attackAccentsEnabled)
                DisableAttackAccents();
            // Clear extrapolation on any state change — new clip starts
            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            _currentAnimState = null;
            if (_landingLagActive
                && (state.State == ActionState.Hitstun
                    || state.HitstunTicks > 0
                    || !state.IsGrounded))
                ClearLandingLagPresentation();

            // ── Hitstop (ADR-0012): freeze the animation in place — the pose holds at the
            // impact frame for the freeze's duration. The server timeline pauses
            // symmetrically (TickTimers, ability ticks), so the same paused clip resumes
            // in sync when the freeze pops — no clip restart, no combo-extension artifact.
            if (state.HitstopTicks > 0)
            {
                if (!_hitstopPaused)
                {
                    // DIAG: hitstop lifecycle — correlate with in-game hit reactions.
                    Debug.Log($"[Hitstop] freeze START ent={_entityId} ticks={state.HitstopTicks} " +
                        $"dmg={state.DamagePercent} prevDmg={_lastState.DamagePercent} " +
                        $"clip={CurrentClipName()} level={state.HitstunLevel}");
                    _hitstopPaused = true;
                    if (_animancer.States.Current is { } frozen)
                    {
                        _hitstopPausedSpeed = frozen.Speed;
                        frozen.Speed = 0f;
                    }
                }
                else if (_animancer.States.Current is { } fs)
                {
                    fs.Speed = 0f;
                }
                return; // nothing may change during the freeze
            }
            if (_hitstopPaused)
            {
                _hitstopPaused = false;
                // DIAG: hitstop lifecycle — freeze popped, queued launch applied.
                Debug.Log($"[Hitstop] freeze END ent={_entityId} hs={state.HitstunTicks} " +
                    $"st={state.State} clip={CurrentClipName()}");
                if (_animancer.States.Current is { } resumed)
                    resumed.Speed = _hitstopPausedSpeed;
            }

            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;

            // ── Action animation state (Attacking / AirDodgeMovement / Hitstun) ──
            bool isCombat = state.State == ActionState.Attacking
                || state.State == ActionState.AirDodgeMovement
                || state.State == ActionState.Hitstun;

            // ── Hitstun animation (always, even during extrapolation guard) ──
            // Separate from stateChanged gate to handle re-hits during guard period.
            if (state.State == ActionState.Hitstun)
            {
                string hitName = state.HitstunLevel switch
                {
                    1 => _charDef.HitMediumAnim,
                    2 => _charDef.HitHardAnim,
                    _ => _charDef.HitSmallAnim
                };
                // Raw opponent snapshots may repeat or skip ticks. Reaction identity
                // distinguishes actual re-hits, including shorter-stun follow-ups.
                _jumpArcActive = false;

                bool newHit = HitstunPresentation.ShouldRestart(
                    _lastAnimState, _presentedHitReactionSequence, state);
                if (newHit)
                    ClearTumble();
                else if (_inHitstunAnim
                    && Time.time - _hitstunAnimStartTime >= _hitstunAnimLength
                    && IsStrongAirborneLaunch(state)
                    && TryPlayTumble())
                {
                    _inHitstunAnim = false;
                    _lastAnimState = ActionState.Hitstun;
                    return;
                }

                if (newHit)
                {
                    if (TryGetAnimation(hitName, out var clip, out _))
                    {
                        float hsAnimSpeed = GetAnimSpeedFromDuration(hitName, state.HitstunTicks);
                        var hsState = _animancer.Play(clip, 0f);
                        hsState.Speed = hsAnimSpeed;
                        _lastHitstunAnimName = hitName;
                        _inHitstunAnim = true;
                        _presentedHitReactionSequence = state.HitReactionSequence;
                        _hitstunAnimStartTime = Time.time;
                        _hitstunAnimLength = clip.length / hsAnimSpeed;
                        // DIAG: hitstun clip restart — which clip, why, and the
                        // tick delta vs the previous applied state.
                        Debug.Log($"[HitstunAnim] RESTART ent={_entityId} clip={hitName} " +
                            $"ticks={state.HitstunTicks} prevTicks={_lastState.HitstunTicks} " +
                            $"level={state.HitstunLevel} firstEntry={_lastAnimState != ActionState.Hitstun} " +
                            $"speed={hsAnimSpeed:F2}");
                    }
                }
                _lastAnimState = ActionState.Hitstun;
                return;
            }

            // ── Hitstun extrapolation guard ──
            // Play the full hit reaction before blending. This guard also
            // covers actionable Idle while post-hitstun flight still carries a launch.
            if (_inHitstunAnim)
            {
                float elapsed = Time.time - _hitstunAnimStartTime;
                bool actionStarted = (state.State != ActionState.Hitstun
                        && state.State != ActionState.Idle
                        && state.State != ActionState.Run)
                    || state.JumpsLeft < _lastState.JumpsLeft
                    || (_lastState.IsGrounded && !state.IsGrounded);
                bool airborneLaunch = !actionStarted && IsStrongAirborneLaunch(state);
                if (elapsed < _hitstunAnimLength
                    && (state.State == ActionState.Hitstun || airborneLaunch))
                {
                    _lastAnimState = state.State;
                    return;
                }
                if (elapsed >= _hitstunAnimLength
                    && airborneLaunch
                    && TryPlayTumble())
                {
                    _inHitstunAnim = false;
                    _lastAnimState = state.State;
                    return;
                }
                _inHitstunAnim = false;
            }

            // Tumble loops through hitstun, then releases into fall once the
            // authoritative hitstun marker says the fighter is actionable.
            if (_tumbleActive)
            {
                bool jumpStarted = state.JumpsLeft < _lastState.JumpsLeft
                    || (_lastState.IsGrounded && !state.IsGrounded);
                bool explicitAction = state.State != ActionState.Idle
                    && state.State != ActionState.Run
                    && state.State != ActionState.Hitstun;
                bool actionable = state.State != ActionState.Hitstun
                    && state.HitstunTicks == 0;

                if (state.IsGrounded || jumpStarted || explicitAction)
                {
                    ClearTumble();
                }
                else if (actionable && !state.IsGrounded)
                {
                    var tumble = _animancer.States.Current;
                    if (tumble != null && TryGetAnimation(_charDef.TumbleAnim, out var tumbleClip, out _) && tumble.Clip == tumbleClip)
                    {
                        if (float.IsPositiveInfinity(_tumbleReleaseNormalizedTime))
                        {
                            _tumbleReleaseNormalizedTime =
                                Mathf.Floor(tumble.NormalizedTime) + 1f;
                            // Disable the asset loop for the release phase. The
                            // normalized-time boundary remains authoritative even
                            // if Animancer cached the old loop mode.
                            tumbleClip.wrapMode = WrapMode.ClampForever;
                        }

                        if (tumble.NormalizedTime >= _tumbleReleaseNormalizedTime)
                        {
                            ClearTumble();
                            if (TryPlayFall(0.15f))
                            {
                                _lastAnimState = state.State;
                                _wasGrounded = state.IsGrounded;
                                return;
                            }
                        }
                    }
                }
                else if (!isCombat)
                {
                    _lastAnimState = state.State;
                    _wasGrounded = state.IsGrounded;
                    return;
                }
                if (_tumbleActive)
                {
                    _lastAnimState = state.State;
                    _wasGrounded = state.IsGrounded;
                    return;
                }
            }
            // Landing lag blends the current aerial pose into idle for the
            // authoritative remaining lock duration. Auto-cancel/ordinary
            // landings carry zero ticks and fall straight through to idle/run.
            bool landingLag = state.IsGrounded
                && state.LandingLagTicks > 0
                && state.HitstunTicks == 0
                && state.State != ActionState.Hitstun;
            if (landingLag)
            {
                if (!_landingLagActive
                    && TryGetAnimation(_charDef.IdleAnim, out var idleClip, out _))
                {
                    float fadeDuration = state.LandingLagTicks / 60f;
                    var idleState = _animancer.Play(idleClip, fadeDuration);
                    idleState.Speed = 1f;
                    _landingLagActive = true;
                }
                if (_landingLagActive)
                {
                    _lastAnimState = state.State;
                    _wasGrounded = state.IsGrounded;
                    return;
                }
            }
            else if (_landingLagActive)
            {
                // The authoritative zero is the handoff tick. Do not hold the
                // landing fade or wait for clip completion before locomotion resumes.
                ClearLandingLagPresentation();
            }

            if (state.State is ActionState.GrabAttempt or ActionState.Grabbing or ActionState.Grabbed
                or ActionState.Throwing or ActionState.Thrown)
            {
                _jumpArcActive = false;
                if (TryResolveGrabAnimation(in state, out var clip, out float time))
                {
                    var animation = _animancer.States.Current;
                    if (animation == null || animation.Clip != clip)
                        animation = _animancer.Play(clip, 0.05f);
                    // Replicated phase time owns the pose, including late snapshots,
                    // whiffs and rollback rewinds. No animation callback drives capture.
                    animation.Time = time;
                    animation.Speed = 0f;
                    _currentAnimState = animation;
                    _activeExtrapolator = null;
                    _currentExtrapolationMode = ExtrapolationMode.None;
                    _animancer.Evaluate(0f);
                }
                UpdateAttackAccents(state, _lastComboStage);
                _lastComboStage = state.ComboStage;
                _lastAttackSlot = state.AttackSlot;
                _wasAttacking = false;
                _lastAnimState = state.State;
                _wasGrounded = state.IsGrounded;
                return;
            }

            // ── Non-combat: ground/air state machine ──
            if (!isCombat)
            {
                // Aim holds: an authored aim-loop (slot.AimAnimationId, e.g. Manki Q /
                // FightGuy Q) plays while holding, for BOTH movement policies. Without
                // one, fixed-policy holds play the slot's first animation and mobile
                // policy aimers fall through to locomotion below.
                bool aiming = state.State == ActionState.Aiming && state.AttackSlot > 0 && _charDef != null;
                var aimSpec = aiming ? _charDef.GetSlotAbility(state.AttackSlot - 1, !state.IsGrounded) : null;
                AnimationClip? loopClip = null;
                bool aimLoop = aiming && aimSpec?.AimAnimationId != null
                    && TryGetAnimation(aimSpec.AimAnimationId, out loopClip, out _);
                bool fixedAimHold = aiming && !aimLoop
                    && _charDef.GetAimMovementMode(state.AttackSlot, !state.IsGrounded) == AimMovementMode.Fixed;
                if (aimLoop)
                {
                    if (state.State != _lastAnimState)
                    {
                        float fadeDuration = _lastAnimState is ActionState.Crouching or ActionState.Sliding
                            ? 0f : 0.1f;
                        var aimAnimation = _animancer.Play(loopClip, fadeDuration);
                        // The same clip may have been sampled at zero speed by
                        // the preceding attack (Bonk F) or a preview. Reused
                        // Animancer states retain both time and speed.
                        aimAnimation.Time = 0;
                        aimAnimation.Speed = 1f;
                    }
                }
                else if (fixedAimHold)
                {
                    if (state.State != _lastAnimState)
                        PlayAbilityAnim(state, aimSpec, 0);
                }
                else if (state.State == ActionState.JumpSquat)
                {
                    if (TryGetAnimation(_charDef.JumpAnim, out var clip, out _))
                    {
                        float fadeDuration = _lastAnimState is ActionState.Crouching or ActionState.Sliding
                            ? 0f : 0.05f;
                        _animancer.Play(clip, fadeDuration);
                        _jumpArcActive = true;
                    }
                }
                else if (state.State == ActionState.LedgeHang)
                {
                    // Placeholder pose: no hang anim asset yet (ADR-0020 §4) — play fall.
                    if (TryGetAnimation(_charDef.FallAnim, out var clip, out _)
                        && _animancer.States.Current?.Clip != clip)
                        _animancer.Play(clip, 0.1f);
                }
                else if (!state.IsGrounded)
                {
                    bool isAscending = state.VY > 0f;
                    bool wasAscending = _lastState.VY > 0f;

                    // Detect double jump: JumpsLeft decreased this tick
                    bool doubleJumped = state.JumpsLeft < _lastState.JumpsLeft;

                    if (isAscending)
                    {
                        // New ascent: initial jump, or double jump from descent
                        if (!wasAscending)
                        {
                            // Only play jump_up if it's a fresh jump (not recovery from attack hitstun)
                            if (_jumpArcActive || doubleJumped)
                            {
                                if (TryGetAnimation(_charDef.JumpAnim, out var clip, out _))
                                {
                                    _animancer.Play(clip, 0.05f);
                                    _jumpArcActive = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Descending: transition from jump_up / attack pose into fall.
                        if (wasAscending && _jumpArcActive)
                        {
                            TryPlayFall(0.15f);
                            _jumpArcActive = false;
                        }
                        else if (!_jumpArcActive)
                        {
                            // Already descending (after attack, after jump_down expired, etc.)
                            // Ensure fall is eventually playing.
                            TryPlayFall(0.15f);
                        }
                    }
                }
                else
                {
                    _jumpArcActive = false;
                    string lowAnimationId = state.State == ActionState.Crouching
                        ? _charDef.CrouchAnim
                        : state.State == ActionState.Sliding ? _charDef.SlideAnim : "";
                    bool lowPosePlayed = state.IsGrounded
                        && (state.State is ActionState.Crouching or ActionState.Sliding)
                        && TryPlayLowPose(lowAnimationId);
                    if (!lowPosePlayed)
                    {
                        // Missing legacy low content intentionally stays upright;
                        // never fabricate a low visual/hurtbox presentation.
                        string animationId = hSpeed > _runSpeedThreshold
                            ? _charDef.RunAnim
                            : _charDef.IdleAnim;
                        if (TryGetAnimation(animationId, out var clip, out _))
                        {
                            float fadeDuration = _lastAnimState is ActionState.Crouching or ActionState.Sliding
                                ? 0f : 0.1f;
                            _animancer.Play(clip, fadeDuration);
                        }
                    }
                }
            }

            // ── Combat state changes (excluding hitstun — handled above) ──
            bool stateChanged = state.State != _lastAnimState
                || (state.State == ActionState.Attacking && (
                    state.AttackSlot != _lastAttackSlot
                    || state.ComboStage != _lastComboStage
                    // AttackSequence is the restart edge, including same-slot IASA
                    // restarts. Elapsed seeks (Bonk E landing sync) must not replay.
                    || state.AttackSequence != _lastState.AttackSequence));

            if (isCombat && stateChanged)
            {
                _jumpArcActive = false;
                if (state.State == ActionState.Attacking)
                {
                    if (_charDef != null)
                    {
                        byte slot = (byte)(state.AttackSlot - 1);
                        var spec = _charDef.GetSlotAbility(slot, !state.IsGrounded);
                        PlayAbilityAnim(state, spec, state.ComboStage);
                    }
                }
                else if (state.State == ActionState.AirDodgeMovement)
                {
                    string airDodgeAnim = string.IsNullOrEmpty(_charDef.AirDodgeAnim)
                        ? _charDef.DashAnim
                        : _charDef.AirDodgeAnim;
                    if (TryGetAnimation(airDodgeAnim, out var clip, out _))
                    {
                        float dodgeAnimSpeed = GetAnimSpeedFromDuration(
                            airDodgeAnim, DefenseConfig.AirDodgeMovementTicks);
                        var dodgeState = _animancer.Play(clip, 0f);
                        dodgeState.Speed = dodgeAnimSpeed;
                    }
                }
            }

            byte prevComboStage = _lastComboStage;
            _lastComboStage = state.ComboStage;
            _wasGrounded = state.IsGrounded;

            // ── Attack accent lifecycle ──
            UpdateAttackAccents(state, prevComboStage);
            _lastAttackSlot = state.AttackSlot;
            _wasAttacking = state.State == ActionState.Attacking;
            _lastAnimState = state.State;
        }

        private void MaintainTumbleLoop()
        {
            if (!_tumbleActive
                || !(_animancer?.States.Current is { } tumble)
                || !float.IsPositiveInfinity(_tumbleReleaseNormalizedTime)
                || tumble.IsLooping
                || tumble.Length <= 0f
                || tumble.Time < tumble.Length)
                return;

            // Loop non-looping imports only while hitstun owns the phase.
            // Keep release time monotonic so the pending cycle boundary can be reached.
            tumble.Time %= tumble.Length;
            tumble.IsPlaying = true;
        }

        private void LateUpdate()
        {
            MaintainTumbleLoop();

            if (_activeExtrapolator != null && _currentAnimState != null
                && _currentExtrapolationMode == ExtrapolationMode.Continuous
                && _currentAnimState.IsPlaying)
            {
                float currentTime = (float)_currentAnimState.Time;
                float clipLength = (float)_currentAnimState.Length;
                if (currentTime > clipLength)
                {
                    float extraTime = currentTime - clipLength;
                    Transform rootBone = FindBone("mixamorig:Hips");
                    if (rootBone != null)
                        _activeExtrapolator.Apply(rootBone, extraTime);
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_modelInstance != null)
            {
                float drift = (_modelInstance.transform.localPosition - _attackPoseModelOffset).magnitude;
                if (drift > ModelRootDriftWarningDistance && !_reportedModelRootDrift)
                {
                    Debug.LogWarning(
                        $"[PresentationDrift] {_entityName} model root drifted {drift:F4}m " +
                        $"from its authoritative renderer root.");
                    _reportedModelRootDrift = true;
                }
                else if (drift <= ModelRootDriftWarningDistance)
                {
                    _reportedModelRootDrift = false;
                }
            }
        #endif
        }

        private void UpdateAttackAccents(CharacterState state, byte prevComboStage)
        {
            bool isAttacking = state.State == ActionState.Attacking;
            bool slotChanged = isAttacking && _lastAttackSlot != state.AttackSlot;
            bool stageChanged = isAttacking && prevComboStage != state.ComboStage;
            bool attackStarted = isAttacking
                && (_lastState.State != ActionState.Attacking
                    || slotChanged
                    || state.AttackSequence != _lastState.AttackSequence);
            if (attackStarted)
                _attackStartedGrounded = state.IsGrounded;

            bool isFightGuyNormal = isAttacking
                && _charDef?.Class == CharacterClass.FightGuy
                && IsNormalAttackSlot(state.AttackSlot);
            if (!isFightGuyNormal)
            {
                if (_attackAccentsEnabled)
                    DisableAttackAccents();
                UpdateLegacyBoneTrails(state, slotChanged, stageChanged);
                return;
            }

            byte slot = (byte)(state.AttackSlot - 1);
            var spec = _charDef.GetSlotAbility(slot, airborne: !_attackStartedGrounded);
            if (spec?.Stages == null || spec.Stages.Length == 0)
            {
                DisableAttackAccents();
                return;
            }

            int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
            var stage = spec.Stages[stageIdx];
            var trails = stage.BoneTrails ?? spec.BoneTrails;
            ulong activeWindowSignature = GetActiveWindowSignature(
                state.AttackElapsedTicks, stage.HitboxEvents);
            if (activeWindowSignature == 0 || trails == null || trails.Length == 0)
            {
                if (_attackAccentsEnabled)
                    DisableAttackAccents();
                if (slotChanged || stageChanged)
                    DisableAllTrails();
                return;
            }

            if (_attackAccentsEnabled
                && _activeAccentWindowSignature == activeWindowSignature
                && !slotChanged
                && !stageChanged
                && !attackStarted)
                return;
            if (_attackAccentsEnabled)
                DisableAttackAccents();
            DisableAllTrails();

            float activeDamage = GetMaxActiveDamage(
                state.AttackElapsedTicks, stage.HitboxEvents);
            bool heavy = activeDamage >= 12f;
            bool secondaryAssigned = false;
            foreach (var trailDef in trails)
            {
                if (!IsTrailActive(
                    trailDef.BoneName, state.AttackElapsedTicks, stage.HitboxEvents))
                    continue;

                var accent = GetOrCreateAttackAccent(trailDef.BoneName);
                if (accent == null) continue;

                bool emitSecondary = heavy && !secondaryAssigned;
                ConfigureAttackAccent(accent, trailDef, activeDamage, emitSecondary);
                secondaryAssigned |= emitSecondary;
                _attackAccentsEnabled = true;
            }
            _activeAccentWindowSignature = activeWindowSignature;
        }

        private void UpdateLegacyBoneTrails(CharacterState state, bool slotChanged, bool stageChanged)
        {
            bool isAttacking = state.State == ActionState.Attacking;
            if ((isAttacking && !_wasAttacking || slotChanged || stageChanged) && _charDef != null)
            {
                if (slotChanged || stageChanged)
                    DisableAllTrails();

                byte slot = (byte)(state.AttackSlot - 1);
                var spec = _charDef.GetSlotAbility(slot, !state.IsGrounded);
                if (spec?.Stages == null || spec.Stages.Length == 0) return;

                int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
                var stage = spec.Stages[stageIdx];
                var trails = stage.BoneTrails ?? spec.BoneTrails;
                if (trails == null) return;

                foreach (var trailDef in trails)
                {
                    var ps = GetOrCreateBoneTrail(trailDef.BoneName);
                    if (ps == null) continue;

                    var main = ps.main;
                    main.startColor = new Color(trailDef.R, trailDef.G, trailDef.B, trailDef.A);
                    main.startSize = trailDef.Width;
                    var emission = ps.emission;
                    emission.enabled = true;
                }
            }
            else if (!isAttacking && _wasAttacking)
            {
                DisableAllTrails();
            }
        }
        private static bool IsNormalAttackSlot(byte attackSlot)
            => attackSlot == AbilitySlots.Slot1
                || attackSlot == AbilitySlots.Slot2
                || attackSlot == AbilitySlots.Slot3
                || attackSlot == AbilitySlots.Slot4;


        private static ulong GetActiveWindowSignature(
            ushort elapsedTicks, HitboxEvent[] events)
        {
            if (events == null) return 0;
            ulong signature = 0;
            for (int i = 0; i < events.Length; i++)
            {
                if (!IsHitboxActive(elapsedTicks, events[i])) continue;
                signature |= 1UL << (i & 63);
            }
            return signature;
        }

        private static float GetMaxActiveDamage(
            ushort elapsedTicks, HitboxEvent[] events)
        {
            float maxDamage = 0f;
            if (events == null) return maxDamage;
            foreach (var hitbox in events)
            {
                if (IsHitboxActive(elapsedTicks, hitbox))
                    maxDamage = Math.Max(maxDamage, hitbox.Damage);
            }
            return maxDamage;
        }

        private static bool IsTrailActive(
            string boneName, ushort elapsedTicks, HitboxEvent[] events)
        {
            if (events == null) return false;
            foreach (var hitbox in events)
            {
                if (!IsHitboxActive(elapsedTicks, hitbox)) continue;
                if (string.IsNullOrEmpty(hitbox.BoneName)
                    || hitbox.BoneName == boneName
                    || hitbox.EndBoneName == boneName)
                    return true;
            }
            return false;
        }

        private static bool IsHitboxActive(ushort elapsedTicks, HitboxEvent hitbox)
        {
            int endExclusive = hitbox.TriggerTick + hitbox.DurationTicks;
            return elapsedTicks >= hitbox.TriggerTick && elapsedTicks < endExclusive;
        }


        private static void ConfigureAttackAccent(
            AttackAccentTrail accent,
            BoneTrailDef def,
            float damage,
            bool emitSecondary)
        {
            bool heavy = damage >= 12f;
            bool medium = !heavy && damage >= 7f;
            float widthScale = heavy ? 1.8f : medium ? 1.25f : 0.75f;
            float lifetime = heavy ? 0.16f : medium ? 0.12f : 0.08f;
            var color = new Color(def.R, def.G, def.B, def.A);

            accent.Primary.widthMultiplier = def.Width * widthScale;
            accent.Primary.time = lifetime;
            accent.Primary.startColor = new Color(color.r, color.g, color.b, color.a * 0.08f);
            accent.Primary.endColor = new Color(color.r, color.g, color.b, color.a * 0.9f);
            accent.Primary.Clear();
            accent.Primary.emitting = true;

            accent.Secondary.emitting = false;
            accent.Secondary.Clear();
            if (!emitSecondary) return;

            accent.Secondary.transform.localPosition =
                new Vector3(def.Width * 0.45f, 0f, 0f);
            accent.Secondary.widthMultiplier = def.Width * widthScale * 0.38f;
            accent.Secondary.time = lifetime * 0.7f;
            accent.Secondary.startColor =
                new Color(color.r, color.g, color.b, color.a * 0.03f);
            accent.Secondary.endColor =
                new Color(color.r, color.g, color.b, color.a * 0.45f);
            accent.Secondary.emitting = true;
        }

        private AttackAccentTrail GetOrCreateAttackAccent(string boneName)
        {
            if (_attackAccentTrails.TryGetValue(boneName, out var existing))
                return existing;
            var bone = FindBone(boneName);
            if (_boneTrailPrefab == null || bone == null) return null;
            var sourceRenderer = _boneTrailPrefab.GetComponent<ParticleSystemRenderer>();
            if (sourceRenderer == null || sourceRenderer.sharedMaterial == null) return null;

            var root = new GameObject($"AttackAccent_{boneName}");
            root.transform.SetParent(bone, false);
            var primary = CreateAttackAccentTrail(
                root, sourceRenderer.sharedMaterial);

            var secondaryObject = new GameObject("HeavyFragment");
            secondaryObject.transform.SetParent(root.transform, false);
            var secondary = CreateAttackAccentTrail(
                secondaryObject, sourceRenderer.sharedMaterial);

            var accent = new AttackAccentTrail
            {
                Root = root,
                Primary = primary,
                Secondary = secondary,
            };
            _attackAccentTrails[boneName] = accent;
            return accent;
        }

        private static TrailRenderer CreateAttackAccentTrail(
            GameObject owner, Material material)
        {
            var trail = owner.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.widthCurve = AttackAccentWidth;
            trail.minVertexDistance = 0.025f;
            trail.numCornerVertices = 3;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.generateLightingData = false;
            trail.autodestruct = false;
            trail.emitting = false;
            return trail;
        }

        private ParticleSystem GetOrCreateBoneTrail(string boneName)
        {
            if (_activeTrails.TryGetValue(boneName, out var existing))
                return existing;
            var bone = FindBone(boneName);
            if (_boneTrailPrefab == null) return null;
            if (bone == null) return null;
            var trail = UnityEngine.Object.Instantiate(_boneTrailPrefab, bone).GetComponent<ParticleSystem>();
            trail.transform.localPosition = Vector3.zero;
            trail.transform.localRotation = Quaternion.identity;

            trail.Play();
            var emission = trail.emission;
            emission.enabled = false;

            _activeTrails[boneName] = trail;
            return trail;
        }

        private void DisableAllTrails()
        {
            foreach (var kvp in _activeTrails)
            {
                var emission = kvp.Value.emission;
                emission.enabled = false;
            }
        }

        private void DisableAttackAccents()
        {
            foreach (var kvp in _attackAccentTrails)
            {
                kvp.Value.Primary.emitting = false;
                kvp.Value.Primary.Clear();
                kvp.Value.Secondary.emitting = false;
                kvp.Value.Secondary.Clear();
            }
            _attackAccentsEnabled = false;
            _activeAccentWindowSignature = 0;
        }


        /// <summary>
        /// Reset animation state to defaults. Useful on respawn.
        /// </summary>
        public void ResetAnimationState()
        {
            HideShield();
            _animancer.Stop();
            _wasGrounded = true;
            _lastAnimState = default;
            _lastAttackSlot = 0;
            _lastComboStage = 0;
            _jumpArcActive = false;

            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            _currentAnimState = null;
            _hitstopPaused = false;
            _tumbleActive = false;
            ClearLandingLagPresentation();
            _wasAttacking = false;
            _attackStartedGrounded = false;
            DisableAttackAccents();
            DisableAllTrails();
            foreach (var kvp in _activeTrails)
            {
                var go = kvp.Value.gameObject;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(go);
                else
                    UnityEngine.Object.DestroyImmediate(go);
            }
            foreach (var kvp in _attackAccentTrails)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(kvp.Value.Root);
                else
                    UnityEngine.Object.DestroyImmediate(kvp.Value.Root);
            }
            _attackAccentTrails.Clear();
            _activeTrails.Clear();
        }

        /// <summary>
        /// Called when the entity dies (void death detected via Deaths counter change).
        /// Triggers a brief visual flash + animation reset.
        /// </summary>
        public void OnDeath()
        {
            _deathFlashTicks = DeathFlashDuration;
            ResetAnimationState();
        }

        // ── Gizmos ──

        private void OnDrawGizmos()
        {
            // ── Lookup color for current state ──
            Color stateColor = StateColors.GetValueOrDefault(_lastState.State, Color.white);

            // ── Entity name label ──
            Gizmos.color = Color.white;
            Vector3 labelPos = transform.position + Vector3.up * 2.5f;
#if UNITY_EDITOR
            // UnityEditor.Handles is editor-only; the guard keeps the player
            // build compiling (release blocker found 2026-08-02).
            UnityEditor.Handles.Label(labelPos, $"{_lastState.State}");
#endif

            // ── State color indicator sphere (above label) ──
            Gizmos.color = stateColor;
            //Gizmos.DrawSphere(transform.position + Vector3.up * 3.2f, 0.25f);

            // ── Collision capsule ──
            Gizmos.color = stateColor;
            Vector3 capCenter = transform.position - Vector3.up * _modelYOffset;
            float halfH = Mathf.Max(_capsuleHeight * 0.5f - _capsuleRadius, 0f);
            Vector3 top = capCenter + Vector3.up * halfH;
            Vector3 bot = capCenter - Vector3.up * halfH;

            // Cooked capture volume in simulation space; only the orange phase can connect.
            if (_lastState.State == ActionState.GrabAttempt
                && _lastState.InteractionPhase == (byte)DefenseInteractionPhase.Attempt
                && _charDef?.CaptureGeometry is { } capture)
            {
                int activeEnd = DefenseConfig.GrabWhiffRecoveryTicks + DefenseConfig.GrabActiveTicks;
                bool active = _lastState.StateTicks > DefenseConfig.GrabWhiffRecoveryTicks
                    && _lastState.StateTicks <= activeEnd
                    && _lastState.IsGrounded && _lastState.HitstopTicks == 0
                    && _lastState.VX * _lastState.VX + _lastState.VZ * _lastState.VZ <= 0.0025f;
                Gizmos.color = active ? new Color(1f, 0.25f, 0.05f, 0.95f)
                    : _lastState.StateTicks > activeEnd ? new Color(1f, 0.8f, 0.1f, 0.7f)
                    : new Color(0.7f, 0.7f, 0.7f, 0.35f);
                Matrix4x4 previous = Gizmos.matrix;
                Vector3 origin = new(_lastState.PX, _lastState.PY + capture.OffsetY, _lastState.PZ);
                Gizmos.matrix = Matrix4x4.TRS(origin,
                    Quaternion.Euler(0f, _lastState.CapturedYaw * 0.01f, 0f), Vector3.one);
                Gizmos.DrawWireCube(new Vector3(0f, 0f, capture.Reach * 0.5f),
                    new Vector3(capture.Width, capture.Height, capture.Reach));
                Gizmos.matrix = previous;
                Gizmos.color = stateColor;
            }
        }

        /// <summary>DIAG: name of the currently playing clip, or "none".</summary>
        private string CurrentClipName()
            => _animancer?.States.Current?.Clip != null
                ? _animancer.States.Current.Clip.name
                : "none";

        public Transform ResolvePresentationBone(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name is "_weapon_hilt" or "_weapon_tip")
            {
                if (_weaponAttach == null) _weaponAttach = GetComponent<WeaponAttach>();
                return _weaponAttach?.ResolveTrailAnchor(name);
            }
            string rigName = name switch
            {
                "bone.head" => "mixamorig:Head",
                "bone.hips" => "mixamorig:Hips",
                "bone.right-hand" => "mixamorig:RightHand",
                "bone.left-hand" => "mixamorig:LeftHand",
                "bone.right-foot" => "mixamorig:RightFoot",
                "bone.left-foot" => "mixamorig:LeftFoot",
                _ => name,
            };
            return FindBone(rigName);
        }

        private Transform FindBone(string name)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
