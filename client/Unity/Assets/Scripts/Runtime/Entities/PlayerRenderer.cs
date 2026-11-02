using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using System.Linq;
using Animancer;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Animation;
using SlopArena.Client.UI;
using SlopArena.Client.Combat;
using SlopArena.Client.World;

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
        private CharacterAnimationConfig _resolvedAnimConfig;
        private CharacterAnimationCatalog _animationCatalog;
        private AnimationClip _grabClip;
        private AnimationClip _throwClip;
        private AnimationClip _grabHoldClip, _grabbedHoldClip;
        private AnimationClip _grabVictimThrowClip;
        private ProjectileVFXManager _projectileVFX;

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
            { ActionState.Shell,      new Color(0.2f, 0.8f, 0.7f, 0.8f) }, // teal
            { ActionState.Attacking,  new Color(1f, 0.6f, 0f, 0.8f) },     // orange
            { ActionState.AirDodgeMovement, new Color(1f, 0.8f, 0f, 0.8f) }, // yellow
            { ActionState.AirDodgeRecovery, new Color(1f, 0.8f, 0f, 0.8f) }, // yellow
            { ActionState.JumpSquat,  new Color(0.8f, 0.3f, 1f, 0.8f) },   // purple
            { ActionState.Aiming,     new Color(0.3f, 1f, 1f, 0.8f) },     // cyan-light
            { ActionState.Run,        new Color(0f, 1f, 1f, 0.8f) },       // cyan
            { ActionState.LedgeHang,  new Color(1f, 0.8f, 0f, 0.8f) },     // yellow
        };
        [Header("Visual Offset")]
        [SerializeField] private float _modelYOffset;

        /// <summary>Character definition (capsule geometry, weight, baked bones) — set at spawn.</summary>
        public CharacterDefinition? CharacterDef => _charDef;
        public CharacterAnimationCatalog PresentationCatalog => _animationCatalog;

        public void BindProjectilePresentation(ProjectileVFXManager manager) => _projectileVFX = manager;

        private void OnDestroy()
        {
            _projectileVFX?.UnregisterOwner(_entityId, this);
            if (_flightUpperMask != null) Destroy(_flightUpperMask);
        }

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
        /// <summary>The definition of the form she shows now (ADR-0051); every role and move read uses it.</summary>
        private CharacterDefinition? _charDef;
        private CharacterDefinition? _registeredDef;
        private FormPresentation? _forms;
        private GameObject _modelInstance;
        private Vector3 _attackPoseModelOffset;
        private float _kamikazePitch;
        private AnimancerLayer _flightUpperLayer;
        private AvatarMask _flightUpperMask;

        private void ApplyAttackPoseModel(CharacterState state)
        {
            if (_modelInstance == null || _charDef == null) return;
            // The pitch pivot is solved in the undeformed model frame; hit feel deforms on top.
            _hitFeel?.SuspendDeformation();
            try { ApplyAttackPosePitch(state, _charDef); }
            finally { _hitFeel?.ResumeDeformation(); }
        }

        private void ApplyAttackPosePitch(CharacterState state, CharacterDefinition definition)
        {
            float travelPitch = 0f;
            if (state.State == ActionState.Attacking && state.AttackElapsedTicks >= SlopArena.Shared.Abilities.MankiKamikaze.StartupTicks && _activeMove != null
                && _activeMove.Timeline.Stages.Any(stage => stage.Operations.Any(operation =>
                    operation is CookedStartCapabilityOperation { Parameters: CookedMankiKamikazeCapabilityParameters })))
                travelPitch = Mathf.Atan2(state.VY, Mathf.Sqrt(state.VX * state.VX + state.VZ * state.VZ));
            // Replicated velocity pitches only the presentation; the resolver keeps its baked body.
            if (state.HitstopTicks == 0)
                _kamikazePitch = Application.isPlaying
                    ? Mathf.MoveTowards(_kamikazePitch, travelPitch, 15f * Mathf.Deg2Rad)
                    : travelPitch;
            float pitch = state.AttackPosePitch + _kamikazePitch;
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
                definition.BoneYToWorldY(state.PY, 0f), state.PZ);
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
        /// Install the definition, geometry, baked poses and animation source together.
        /// An empty model path with no rig configures definition-only presentation.
        /// Rejected inputs preserve the current configuration and model.
        /// </summary>
        public bool Configure(
            CharacterDefinition definition, BakedAnimationData? baked,
            CharacterAnimationCatalog animationCatalog, GameObject prefabOverride,
            out string error, CharacterAnimationConfig animationConfigOverride = null)
        {
            error = "";
            if (definition == null)
            {
                error = "A character definition is required.";
                return false;
            }
            if (!(definition.VisualScale > 0f) || float.IsInfinity(definition.VisualScale)
                || float.IsNaN(definition.ModelYOffset) || float.IsInfinity(definition.ModelYOffset))
            {
                error = "The model scale must be positive and finite, and its offset must be finite.";
                return false;
            }
            if (!(definition.CapsuleRadius >= 0f) || float.IsInfinity(definition.CapsuleRadius)
                || !(definition.CapsuleHeight >= 0f) || float.IsInfinity(definition.CapsuleHeight))
            {
                error = "Capsule dimensions must be nonnegative and finite.";
                return false;
            }

            string resourcePath = definition.ModelResourcePath ?? "";
            GameObject prefab = prefabOverride != null
                ? prefabOverride
                : animationCatalog != null && animationCatalog.Rig != null
                    ? animationCatalog.Rig
                    : string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<GameObject>(resourcePath);
            if (prefab == null && !string.IsNullOrEmpty(resourcePath))
            {
                error = $"Model not found at Resources/{resourcePath}.";
                return false;
            }

            var animConfig = animationConfigOverride != null ? animationConfigOverride : _charConfig;
            if (prefab != null)
            {
                if (prefab.GetComponentInChildren<Animator>(true) == null)
                {
                    error = $"Model {prefab.name} has no Animator.";
                    return false;
                }
                if (animationCatalog == null)
                {
                    if (definition.Class == CharacterClass.FightGuy)
                    {
                        error = "FightGuy requires generated cooked animation assets.";
                        return false;
                    }
                    if (animConfig == null)
                        animConfig = LoadFallbackAnimationConfig(definition);
                    if (animConfig == null)
                    {
                        error = $"No animation catalog or AnimConfig is available for {definition.Class}.";
                        return false;
                    }
                }
            }

            bool sourceUnchanged = _modelInstance != null
                && _modelInstance.transform.IsChildOf(transform)
                && _animancer != null && _animancer.Animator != null
                && _modelPrefab == prefab
                && _modelResourcePath == resourcePath;
            GameObject instance = null;
            AnimancerComponent animancer = null;
            if (prefab != null && !sourceUnchanged
                && !TryCreateModel(prefab, out instance, out animancer, out error))
                return false;

#if UNITY_EDITOR
            if (Selection.activeTransform != null && Selection.activeTransform.IsChildOf(transform))
                Selection.activeObject = gameObject;
#endif

            if (_animancer != null)
                ResetAnimationState();
            bool hadModel = _modelPrefab != null;
            _charDef = definition;
            _registeredDef = definition;
            _bakedData = baked;
            _weaponHiltBoneIndex = baked?.FindBoneIndex("_weapon_hilt") ?? -1;
            _weaponTipBoneIndex = baked?.FindBoneIndex("_weapon_tip") ?? -1;
            _animationCatalog = animationCatalog;
            _resolvedAnimConfig = animConfig;
            LastAnimationClipNotInstalled = false;
            _modelYOffset = definition.ModelYOffset;
            _capsuleRadius = definition.CapsuleRadius;
            _capsuleHeight = definition.CapsuleHeight;
            _hurtboxBoneDefs = definition.HurtboxBoneDefs?.ToArray() ?? Array.Empty<HurtboxBoneDef>();
            _modelPrefab = prefab;
            _modelResourcePath = resourcePath;
            _modelName = definition.Class.ToString();
            _modelVisualScale = definition.VisualScale;

            if (prefab == null)
            {
                if (hadModel)
                    ReplaceModel(null, null);
                return true;
            }

            if (!sourceUnchanged)
                ReplaceModel(instance, animancer);
            else
                ApplyModelSettings();
            LoadSharedAnimations();
            if (TryGetAnimation(definition.IdleAnim, out var idleClip, out _))
            {
                _currentAnimState = _animancer.Play(idleClip, 0f);
                _animancer.Evaluate(0f);
            }
            return true;
        }

        /// <summary>
        /// Recreate the last successfully selected model only when its instance is missing.
        /// A missing source is a no-op so the last valid presentation remains untouched.
        /// </summary>
        public void EnsureModel()
        {
            // The model sits below the hit-feel pivots in Play Mode.
            if (_modelInstance != null && _modelInstance.transform.IsChildOf(transform))
                return;
            _modelInstance = null;

            GameObject prefab = _modelPrefab;
            if (prefab == null && !string.IsNullOrEmpty(_modelResourcePath))
                prefab = Resources.Load<GameObject>(_modelResourcePath);
            if (prefab == null) return;

            if (TryCreateModel(prefab, out var instance, out var animancer, out var error))
            {
                _modelPrefab = prefab;
                ReplaceModel(instance, animancer);
            }
            else
                Debug.LogError($"[PlayerRenderer] {error}");
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
        private bool TryCreateModel(
            GameObject prefab, out GameObject instance, out AnimancerComponent animancer, out string error)
        {
            instance = null;
            animancer = null;
            error = "";
            try
            {
                instance = Instantiate(prefab, transform);
                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null)
                    throw new InvalidOperationException($"Model {prefab.name} has no Animator.");
                animator.applyRootMotion = false;
                animancer = instance.GetComponent<AnimancerComponent>()
                    ?? instance.AddComponent<AnimancerComponent>();
                animancer.Animator = animator;
                return true;
            }
            catch (Exception exception)
            {
                if (instance != null)
                {
                    if (Application.isPlaying) Destroy(instance);
                    else DestroyImmediate(instance);
                }
                instance = null;
                animancer = null;
                error = $"Could not create model {prefab.name}: {exception.Message}";
                return false;
            }
        }

        private void ReplaceModel(GameObject instance, AnimancerComponent animancer)
        {
            _flightUpperLayer = null;
            if (_flightUpperMask != null)
            {
                if (Application.isPlaying) Destroy(_flightUpperMask);
                else DestroyImmediate(_flightUpperMask);
                _flightUpperMask = null;
            }
            HideShield();
            _projectileVFX?.ClearHeldVisual(_entityId);
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child == instance) continue;
                child.SetActive(false);
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }

            _modelInstance = instance;
            _forms = instance != null ? instance.GetComponentInChildren<FormPresentation>(true) : null;
            _animancer = animancer;
            _weaponHips = null;
            if (instance == null) return;
            ApplyModelSettings();
            var animator = _animancer.Animator;
            _weaponHips = animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Hips)
                : FindBone("mixamorig:Hips");
        }

        private void ApplyModelSettings()
        {
            var instance = _modelInstance;
            instance.name = _modelName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * _modelVisualScale;
            _hitFeel?.AttachModel(instance.transform);
            _airDodge?.AttachModel(instance.transform);
            _homingDash?.AttachModel(instance.transform);
        }

        private static CharacterAnimationConfig LoadFallbackAnimationConfig(CharacterDefinition def)
        {
            var config = Resources.Load<CharacterAnimationConfig>($"AnimationConfigs/{def.Class}_AnimConfig");
            if (config != null || string.IsNullOrEmpty(def.ModelResourcePath))
                return config;
            string modelClass = def.ModelResourcePath.Substring(def.ModelResourcePath.LastIndexOf('/') + 1);
            return modelClass.Length > 0
                ? Resources.Load<CharacterAnimationConfig>($"AnimationConfigs/{modelClass}_AnimConfig") : null;
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

        public int PlayerNumber { get; set; }

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
        /// <summary>Stage or phase clip the current attack shows (a steered charge's gallop, say). Read by presentation cues.</summary>
        public byte CurrentComboStage => _lastState.ComboStage;
        public byte CurrentClipIndex => _lastState.AnimIndex;
        /// <summary>Whether the last applied state stood on the ground. Read by presentation cues (no dust on thin air).</summary>
        public bool CurrentIsGrounded => _lastState.IsGrounded;
        /// <summary>Wire slot whose returning projectile is still out (Sir Bonk's pot lid), 0 when none. Read by weapon
        /// attach components: the lid stays off his shoulder until the catch.</summary>
        public byte CurrentReturningProjectileSlot => _lastState.ReturningProjectileSlot;
        /// <summary>Charge of the current charged attack while aiming or firing; 0 otherwise.</summary>
        public ushort CurrentChargeTicks => _lastState.ChargeTicks;
        /// <summary>
        /// Charge and slot of the last charged attack that fired. Kept after the attack ends, so a
        /// cosmetic hit effect can tell how charged its projectile was.
        /// </summary>
        public ushort LastFiredChargeTicks { get; private set; }
        public CookedSlotDefinition? LastFiredChargeMove { get; private set; }
        public byte LastFiredChargeSlot => LastFiredChargeMove == null ? AbilitySlots.None
            : CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[LastFiredChargeMove.Ordinal]);
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
            var spec = MoveFor(attackSlot, airborne);
            if (spec == null || spec.ClipCount == 0 || spec.Timeline.Stages is not { Count: > 0 })
                return false;
            int animationIndex = comboStage % spec.ClipCount;
            if (charge)
            {
                clipName = spec.AimAnimationId;
                if (string.IsNullOrEmpty(clipName) || !TryGetAnimation(clipName, out clip, out _))
                    return false;
                // Do not add a duplicate loop-end tick for float-rounded clip lengths.
                durationTicks = Mathf.Max(1, Mathf.CeilToInt(clip.length * 60f - 0.0001f));
                return true;
            }

            clipName = spec.Clip(animationIndex).AnimationId;
            if (!TryGetAnimation(clipName, out clip, out _))
                return false;
            playbackSpeed = GetAbilityAnimationSpeed(spec, comboStage, clipName);
            int bakedIndex = _bakedData != null ? _bakedData.FindAnimIndex(clipName) : -1;
            var moveClip = spec.Clip(comboStage);
            durationTicks = bakedIndex >= 0 && !moveClip.IsPhaseClip
                ? moveClip.DurationTicks : Mathf.CeilToInt(clip.length * 60f - 0.0001f);
            if (comboStage > 0 && bakedIndex >= 0 && !moveClip.IsPhaseClip)
                durationTicks = Mathf.Min(durationTicks, _bakedData.Animations[bakedIndex].FrameCount);
            durationTicks = Mathf.Max(1, durationTicks);
            return true;
        }


        private bool _currentAttackAirborne => _activeMove?.IsAir ?? false;

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
                || !TryGetBakedWeaponPose(state, airborne, out var bakedHilt, out var bakedTip,
                    out var animationName, out float frame)
                || !TryGetAnimation(animationName, out var clip, out _))
                return false;
            // Sample the undeformed pose; the result is mapped through the hit-feel deformation below.
            _hitFeel?.SuspendDeformation();
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
            Vector3 localHilt = hips + bakedHilt;
            Vector3 localTip = hips + bakedTip;
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
            if (_hitFeel != null)
            {
                // Keep the cosmetic blade ribbon on the visible, deformed blade.
                hilt = _hitFeel.DeformPoint(hilt);
                tip = _hitFeel.DeformPoint(tip);
                _hitFeel.ResumeDeformation();
            }
            ApplyAttackPoseModel(state);
            return true;
        }

        /// <summary>
        /// Hips-relative baked hilt and tip at the attack's clip time, the same fractional
        /// frame and rigid between-frame blend the Shared hitbox uses.
        /// </summary>
        private bool TryGetBakedWeaponPose(
            in CharacterState state, bool airborne, out Vector3 hilt, out Vector3 tip,
            out string animationName, out float frame)
        {
            hilt = tip = default;
            animationName = null;
            frame = 0f;
            if (_bakedData == null || _charDef == null || state.AttackSlot == 0
                || _weaponHiltBoneIndex < 0 || _weaponTipBoneIndex < 0)
                return false;
            var spec = MoveFor(state.AttackSlot, airborne);
            if (spec?.Timeline.Stages is not { Count: > 0 } || spec.ClipCount == 0)
                return false;
            var movePose = spec.Clip(state.AnimIndex).At(state.AttackElapsedTicks);
            animationName = movePose.AnimationId;
            int bakedIndex = _bakedData.FindAnimIndex(animationName);
            if (bakedIndex < 0)
                return false;
            var animation = _bakedData.Animations[bakedIndex];
            if (animation.FrameCount <= 0)
                return false;
            frame = movePose.FrameAt(animation.FrameCount);
            int frame0 = (int)frame;
            int hiltOffset = _weaponHiltBoneIndex * 3;
            int tipOffset = _weaponTipBoneIndex * 3;
            var pose = animation.Frame(frame0);
            if (hiltOffset + 2 >= pose.Length || tipOffset + 2 >= pose.Length)
                return false;
            float hx = pose[hiltOffset], hy = pose[hiltOffset + 1], hz = pose[hiltOffset + 2];
            float tx = pose[tipOffset], ty = pose[tipOffset + 1], tz = pose[tipOffset + 2];
            float blend = frame - frame0;
            if (blend > 0f)
            {
                var next = animation.Frame(frame0 + 1);
                HitboxGeometry.BlendSegment(true, blend, ref hx, ref hy, ref hz, ref tx, ref ty, ref tz,
                    next[hiltOffset], next[hiltOffset + 1], next[hiltOffset + 2],
                    next[tipOffset], next[tipOffset + 1], next[tipOffset + 2]);
            }
            hilt = new Vector3(hx, hy, hz);
            tip = new Vector3(tx, ty, tz);
            return true;
        }

        public bool IsSwordTrailTick(CharacterState state, bool airborne)
        {
            if (state.State != ActionState.Attacking || state.AttackSlot == 0 || _charDef == null)
                return false;
            var spec = MoveFor(state.AttackSlot, airborne);
            if (spec?.Timeline.Stages is not { Count: > 0 })
                return false;
            var stage = spec.Timeline.Stages[spec.Clip(state.AnimIndex).StageIndex];
            if (state.AttackElapsedTicks >= stage.DurationTicks)
                return false;
            var events = stage.ReadHitboxes();
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




        public BakedAnimationData? BakedData => _bakedData;

        /// <summary>Expose the Animator for external access (e.g. VFX hooks).</summary>
        public Animator Animator => _animancer != null ? _animancer.Animator : null;

        // ── Tracking state for change detection ──

        private CharacterState _lastState;
        private bool _jumpArcActive;
        private ActionState _lastAnimState;
        private ushort _presentedHitReactionSequence;
        private byte _lastAttackSlot;
        private byte _lastClipIndex;
        private int _deathFlashTicks;
        private const int DeathFlashDuration = 6;
        private bool _wasGrounded = true;
        private bool _hasAppliedState;
        private byte _lastPresentedDeaths;
        private bool _hasPresentedDeaths;
        private MovementFeedbackEffect _respawnInvincibilityEffect;
        private bool _respawnInvincibilityActive;
        private ClipExtrapolator? _activeExtrapolator;
        private ExtrapolationMode _currentExtrapolationMode;
        private AnimancerState? _currentAnimState;
        // ── Hitstun extrapolation tracking ──
        private bool _inHitstunAnim;
        private float _hitstunAnimStartTime;
        private float _hitstunAnimLength;
        private string _lastHitstunAnimName = "";
        // ── Airborne launch tumble (docs/design/airborne-tumble-animation.md) ──
        // Strong launches play the hit clip's impact snap at its own speed, then the
        // tumble's one-shot build-up, then its loop until the fighter acts or lands.
        private const int TumbleMinHitstunTicks = 24;
        private const float TumbleMinLaunchElevationDegrees = 15f;
        private const int TumbleImpactSnapTicks = 10;
        private const float TumbleEntryFadeSeconds = 8f / 60f;
        // The shared tumble clip's build-up; clips imported without Loop Time cycle from here.
        private const float TumbleLoopStartSeconds = 24f / 60f;
        private bool _tumbleActive;
        private AnimancerState? _tumbleState;
        private bool _tumbleLaunchPending;
        private int _tumbleLaunchHitstunTicks;
        // ── Aerial landing-lag presentation ──
        private bool _landingLagActive;
        // ── Hitstop freeze tracking (ADR-0012) ──
        // True while the current clip is paused at Speed 0 for a hitstop freeze;
        // _hitstopPausedSpeed holds the pre-freeze speed for the resume frame.
        private bool _hitstopPaused;
        private float _hitstopPausedSpeed = 1f;
        private AudioSource _sfxSource;
        private AudioClip _jumpSfx;
        private int _swingSequence;
        private (byte Sequence, byte Slot) _swordSfxAttack;
        private readonly HashSet<(byte Stage, ushort Tick)> _playedSwordSfxTriggers = new();


        private void Awake()
        {
            if (_animancer == null)
                _animancer = GetComponent<AnimancerComponent>();
            if (_modelInstance == null && transform.childCount > 0)
            {
                _modelInstance = transform.GetChild(0).gameObject;
                _forms = _modelInstance.GetComponentInChildren<FormPresentation>(true);
            }
            LoadSharedAnimations();
            if (Application.isPlaying && GetComponent<FighterXRay>() == null) gameObject.AddComponent<FighterXRay>();
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("SFX");
            _sfxSource.playOnAwake = false;
            _sfxSource.spatialBlend = 0f;
            _sfxSource.volume = 0.7f;
            _jumpSfx = Resources.Load<AudioClip>("Audio/SFX/jump");
            if (Application.isPlaying)
            {
                MovementFeedbackEffect.Prewarm();
                _hitFeel = GetComponent<FighterHitFeel>() ?? gameObject.AddComponent<FighterHitFeel>();
                _hitFeel.Init(this);
                _airDodge = TryGetComponent(out AirDodgePresentation airDodge)
                    ? airDodge : gameObject.AddComponent<AirDodgePresentation>();
                _airDodge.Init(this);
                _homingDash = TryGetComponent(out HomingDashPresentation homingDash)
                    ? homingDash : gameObject.AddComponent<HomingDashPresentation>();
                _homingDash.Init(this);
            }
        }

        private FighterHitFeel? _hitFeel;
        private AirDodgePresentation? _airDodge;
        private HomingDashPresentation? _homingDash;
        private HeldCutPresentation? _heldCut;

        /// <summary>Plays a one-shot on this fighter's SFX source (2D, SFX mixer bus).</summary>
        internal void PlayOneShot(AudioClip clip, float volume) => _sfxSource?.PlayOneShot(clip, volume);

        /// <summary>Presentation-only hit feel; null outside Play Mode.</summary>
        public FighterHitFeel? HitFeel => _hitFeel;
        // ── Frame-by-frame animation control ──

        private BakedAnimationData? _bakedData;
        private int _weaponHiltBoneIndex = -1;
        private int _weaponTipBoneIndex = -1;
        private WeaponAttach _weaponAttach;
        private Transform _weaponHips;

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
                && _grabVictimThrowClip != null) return;
            var shared = Resources.Load<CharacterAnimationConfig>("AnimationConfigs/Shared_AnimConfig");
            _grabClip = shared?.GetClipByName("grab");
            _throwClip = shared?.GetClipByName("throw");
            _grabHoldClip = shared?.GetClipByName("grab_idle_a");
            _grabbedHoldClip = shared?.GetClipByName("grab_idle_b");
            _grabVictimThrowClip = shared?.GetClipByName("Grab_Throw_B");
        }

        // Where the stand-in reeled pose holds the small-hit clip: past its flinch, before it recovers.
        private const int ReeledPoseTicks = 6;

        // A command-grabbed fighter (ADR-0057) flinches again with each of its grabber's hits: the hitstop the hit set
        // replays the small-hit clip into the held frame, so no grabbed clip is needed.
        private static int CaptivePoseTicks(in CharacterState state)
            => state.State == ActionState.CommandGrabbed
                ? ReeledPoseTicks - Mathf.Min(state.HitstopTicks, ReeledPoseTicks) : ReeledPoseTicks;

        private bool TryResolveGrabAnimation(in CharacterState snapshot, out AnimationClip clip, out float time)
        {
            clip = null;
            time = 0f;
            LoadSharedAnimations();
            int duration = 0;
            bool loop = false;
            bool throwPose = GrabThrow.TrySamplePose(in snapshot, out var pose);
            if (throwPose)
            {
                clip = pose.Role == GrabThrowRole.Thrower ? _throwClip : _grabVictimThrowClip;
                time = pose.Seconds;
            }
            else if (snapshot.State == ActionState.GrabAttempt)
            {
                clip = _grabClip;
                duration = DefenseConfig.GrabStartupTicks + DefenseConfig.GrabActiveTicks + DefenseConfig.GrabWhiffRecoveryTicks;
            }
            else if (snapshot.State is ActionState.Grabbing or ActionState.Grabbed)
            {
                bool grabber = snapshot.State == ActionState.Grabbing;
                clip = grabber ? _grabHoldClip : _grabbedHoldClip;
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
            if (!throwPose)
                time = Mathf.Max(0, duration - snapshot.StateTicks) * SlopArena.Shared.Simulation.TickDt;
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
            ShowForm(in snapshot);
            if (_animancer == null || _charDef == null) return false;
            if (snapshot.State is ActionState.GrabAttempt or ActionState.Grabbing or ActionState.Grabbed
                or ActionState.Throwing)
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
                    var spec = MoveFor(snapshot.AttackSlot, airborne);
                    if (spec == null || spec.ClipCount == 0 || spec.Timeline.Stages is not { Count: > 0 })
                        return false;
                    int animationIndex = snapshot.AnimIndex % spec.ClipCount;
                    id = snapshot.State == ActionState.Aiming && !string.IsNullOrEmpty(spec.AimAnimationId)
                        ? spec.AimAnimationId : spec.Clip(animationIndex).AnimationId;
                    if (snapshot.State == ActionState.Attacking)
                    {
                        speed = GetAbilityAnimationSpeed(spec, snapshot.AnimIndex, id);
                        elapsed = snapshot.AttackElapsedTicks;
                    }
                }
                else if (snapshot.State == ActionState.Hitstun)
                {
                    id = snapshot.HitstunLevel switch
                    {
                        1 => _charDef.HitMediumAnim,
                        2 => _charDef.HitHardAnim,
                        _ => _charDef.HitSmallAnim,
                    };
                    // The pose clock restarts with each hit and holds through Hitstop, so it counts
                    // hitstun ticks. Rebuild the applied hitstun and the launch's rise: horizontal KV
                    // stays constant in hitstun and flight gravity takes the rise after the pop window.
                    float launchRise = snapshot.KVY + (snapshot.IsGrounded ? 0f
                        : SlopArena.Shared.Simulation.FlightGravity * SlopArena.Shared.Simulation.TickDt
                            * Mathf.Max(0, snapshot.AirTimeTicks - SlopArena.Shared.Simulation.KnockbackPopTicks));
                    if (elapsed >= TumbleImpactSnapTicks && !snapshot.IsGrounded
                        && IsTumbleLaunch(snapshot.HitstunTicks + elapsed, snapshot.KVX, launchRise, snapshot.KVZ)
                        && TryGetAnimation(_charDef.TumbleAnim, out var tumbleClip, out _))
                    {
                        clip = tumbleClip;
                        time = TumbleTime(tumbleClip.isLooping, tumbleClip.length, (elapsed - TumbleImpactSnapTicks) / 60f);
                        return true;
                    }
                }
                else if (ActionStates.Captive(snapshot.State))
                {
                    id = _charDef.HitSmallAnim;
                    elapsed = CaptivePoseTicks(in snapshot);
                }
                else if (snapshot.State == ActionState.Crouching) id = _charDef.CrouchAnim;
                else if (snapshot.State == ActionState.Sliding) id = _charDef.SlideAnim;
                else if (snapshot.State == ActionState.Shell && !string.IsNullOrEmpty(_charDef.ShellAnim)) id = _charDef.ShellAnim;
                else if (snapshot.State == ActionState.Flying && !string.IsNullOrEmpty(_charDef.FlyingAnim))
                {
                    id = _charDef.FlyingAnim;
                    elapsed = Flying.Parameters(_charDef) is { } flight
                        ? Math.Max(0, flight.FuelTicks - snapshot.Flight.FuelTicksLeft) : poseTicks;
                }
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
            _lastClipIndex = snapshot.AnimIndex;
            _activeMove = _charDef?.GetCookedSlotAbility(snapshot.AttackSlot, airborne);
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
            clip = _resolvedAnimConfig?.GetClipByName(semanticId);
            return clip != null;
        }

        // ── Lifecycle ──


        private void OnDisable() => EndStateEffects();

        private void EndStateEffects()
        {
            HideShield();
            _projectileVFX?.ClearHeldVisual(_entityId);
            _airDodge?.Clear();
            _homingDash?.Clear();
            _heldCut?.Clear();
            if (_respawnInvincibilityEffect != null)
            {
                _respawnInvincibilityEffect.EndInvincibility();
                _respawnInvincibilityEffect = null;
            }
            _respawnInvincibilityActive = false;
        }

        /// <summary>
        /// An Arcade retry replaces the simulation. Present its first state as a fresh spawn:
        /// no interpolation from the old position, stock transition, animation, trail or reaction.
        /// </summary>
        public void ResetForFreshSimulation()
        {
            EndStateEffects();
            ResetAnimationState();
            _hitFeel?.ResetForFreshSimulation();
            _deathFlashTicks = 0;
            foreach (var skin in BodySkins()) if (skin != null) skin.enabled = true;
            _inHitstunAnim = false;
            _presentedHitReactionSequence = 0;
            _hasAppliedState = false;
            _hasPresentedDeaths = false;
            _hasInterpolationTarget = false;
            _visualCorrection = Vector3.zero;
        }
        // ── Public API ──

        /// <summary>
        /// Apply a server-authoritative CharacterState to this entity.
        /// Updates position, rotation, and drives animation state.
        /// Call from Update() after reconciliation.
        /// </summary>
        private void EmitSwordHitboxSfx(CharacterState state)
        {
            if (_animationCatalog?.PresentationProfile == null
                || state.AttackSlot == 0
                || (state.State != ActionState.Attacking && state.State != ActionState.Aiming))
                return;

            if (!_hasAppliedState
                || _swordSfxAttack != (state.AttackSequence, state.AttackSlot))
            {
                _swordSfxAttack = (state.AttackSequence, state.AttackSlot);
                _playedSwordSfxTriggers.Clear();
            }

            var ability = _activeMove;
            if (ability?.Timeline.Stages is not { Count: > 0 })
                return;

            int stageIndex = ability.Clip(state.AnimIndex).StageIndex;
            var hitboxes = ability.Timeline.Stages[stageIndex].ReadHitboxes();
            if (hitboxes == null)
                return;

            foreach (var hitbox in hitboxes)
            {
                if (state.AttackElapsedTicks < hitbox.TriggerTick
                    || !_playedSwordSfxTriggers.Add((state.AnimIndex, hitbox.TriggerTick)))
                    continue;

                PlaySwordHitboxSfx();
            }
        }

        private void PlaySwordHitboxSfx()
        {
            AudioClip clip = _animationCatalog.PresentationProfile.SelectSwing(ref _swingSequence);
            if (clip != null) PlayOneShot(clip, 1f);
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

        private MankiFlightPresentation _flightPresentation;
        private CookedSlotDefinition? _activeMove;
        public CookedSlotDefinition? CurrentMove => _activeMove;

        /// <summary>Follow her form (ADR-0051): roles and moves come from its definition, and its body draws.</summary>
        private void ShowForm(in CharacterState state)
        {
            if (_registeredDef != null) _charDef = _registeredDef.ForForm(state.Form.Active);
            if (_forms != null) _forms.Show(state.Form.Active, HidesThrustTravel(in state));
        }

        // Bat Scatter's body follows the deterministic travel window, including hitstop and rollback.
        private bool HidesThrustTravel(in CharacterState state)
        {
            if (state.State != ActionState.Attacking) return false;
            var move = MoveFor(state.AttackSlot, !state.IsGrounded);
            if (move == null) return false;
            int stageStart = 0;
            foreach (var stage in move.Timeline.Stages)
            {
                foreach (var operation in stage.Operations)
                    if (operation is CookedStartCapabilityOperation { Parameters: CookedThrustCapabilityParameters thrust })
                    {
                        int start = stageStart + operation.Tick;
                        if (state.AttackElapsedTicks >= start && state.AttackElapsedTicks < start + thrust.ThrustTicks)
                            return true;
                    }
                stageStart += stage.DurationTicks;
            }
            return false;
        }

        /// <summary>The move behind an attack slot, from the shown form. A move still running from the other form
        /// (F after its swap) keeps its own clip.</summary>
        private CookedSlotDefinition? MoveFor(byte attackSlot, bool airborne)
            => _activeMove != null && _charDef != null && _activeMove.Form != _charDef.Form
                && CanonicalSlotProjection.TryGet(attackSlot, airborne, out var address) && _activeMove.Ordinal == address.Ordinal
                ? _activeMove
                : _charDef?.GetCookedSlotAbility(attackSlot, airborne);

        /// <summary>The meshes the death blink and the respawn act on: both forms' for a two-form fighter, else the
        /// model's first skinned mesh.</summary>
        private SkinnedMeshRenderer[] BodySkins()
        {
            if (_forms != null) return _forms.Skins;
            _firstSkin[0] = _modelInstance != null ? _modelInstance.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            return _firstSkin;
        }
        private readonly SkinnedMeshRenderer[] _firstSkin = new SkinnedMeshRenderer[1];

        public void ApplyServerState(CharacterState state, CookedSlotDefinition? activeMove)
        {
            _activeMove = activeMove;
            bool wallJumped = !state.IsGrounded && state.WallJumpUsed && !_lastState.WallJumpUsed;
            ShowForm(in state);
            if (_charDef != null && Flying.Parameters(_charDef) is { } flight)
            {
                if (_flightPresentation == null) _flightPresentation = gameObject.AddComponent<MankiFlightPresentation>();
                _flightPresentation.Apply(this, state, flight.FuelTicks);
            }
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
                && (state.JumpsLeft < _lastState.JumpsLeft
                    || wallJumped)
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
                if (!stockTransition)
                    EmitMovementFeedback(state, targetPos, wallJumped);
                UpdateRespawnInvincibility(state, targetPos);
            }

            transform.rotation = drawnRotation;
            // Death flash blink
            if (_deathFlashTicks > 0)
            {
                _deathFlashTicks--;
                foreach (var skin in BodySkins())
                    if (skin != null) skin.enabled = _deathFlashTicks % 2 == 0;
            }
            else
            {
                foreach (var skin in BodySkins())
                    if (skin != null && !skin.enabled)
                        skin.enabled = true;
            }


            UpdateAnimationState(state, wallJumped);
            MaintainTumbleLoop();
            ApplyAttackPoseModel(state);

            UpdateShield(state);
            CaptureWeaponTrailState(state);
            UpdateKiShotVisual(state);
            _hitFeel?.OnState(in state, _charDef, _activeMove);
            _airDodge?.OnState(in state);
            _homingDash?.OnState(in state);
            if (Application.isPlaying && (state.State == ActionState.CutHeld || _heldCut != null))
            {
                if (_heldCut == null) _heldCut = gameObject.AddComponent<HeldCutPresentation>();
                _heldCut.Apply(this, in state);
            }
            if (state.State == ActionState.Attacking && state.ChargeTicks > 0)
            {
                LastFiredChargeTicks = state.ChargeTicks;
                LastFiredChargeMove = _activeMove;
            }
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
            var row = _animationCatalog?.PresentationProfile?.FindProjectile(state.AttackSlot, _activeMove?.IsAir ?? _currentAttackAirborne);
            if (string.IsNullOrEmpty(row?.HeldBoneId))
            {
                _projectileVFX?.ClearHeldVisual(_entityId);
                return;
            }
            if (_projectileVFX == null) return;
            var hand = ResolvePresentationBone(row.HeldBoneId);
            _projectileVFX.UpdateHeldVisual(_entityId, state, hand, _activeMove);
        }

        private void PresentStockTransition(
            CharacterState previous, CharacterState respawned, Vector3 respawnPosition)
        {
            // Capture rigs and some setups leave PlayerNumber at 0, which has no seat.
            KnockoutBurst.Spawn(KnockoutLimitPosition(previous),
                PlayerNumber > 0 ? SeatColours.ForFighter(EntityId, PlayerNumber) : Color.white);
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

        private Color RespawnColor()
            => _animationCatalog?.PresentationProfile?.RespawnTint ?? new Color(1f, 1f, 1f, .95f);

        private void EmitMovementFeedback(CharacterState state, Vector3 position, bool wallJumped)
        {
            if (!_hasAppliedState)
            {
                _hasAppliedState = true;
                return;
            }



            bool tookOff = _lastState.IsGrounded && !state.IsGrounded;
            bool doubleJumped = !_lastState.IsGrounded
                && !state.IsGrounded
                && (state.JumpsLeft < _lastState.JumpsLeft
                    || wallJumped);
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
        /// <summary>The shell pose (ADR-0045) skating and coasting; it continues a tuck move that showed the same clip.</summary>
        private bool TryPlayShellPose()
        {
            if (!TryGetAnimation(_charDef.ShellAnim, out var clip, out _))
                return false;
            if (_animancer.States.Current?.Clip != clip)
                _currentAnimState = _animancer.Play(clip, 0.1f);
            return true;
        }

        private bool TryPlayFlying(CharacterState state)
        {
            if (string.IsNullOrEmpty(_charDef.FlyingAnim)
                || !TryGetAnimation(_charDef.FlyingAnim, out var clip, out _)
                || Flying.Parameters(_charDef) is not { } flight)
                return false;

            var loop = _animancer.Layers[0].CurrentState;
            if (loop?.Clip != clip) loop = _animancer.Layers[0].Play(clip, 0.1f);
            // Fuel is a restored simulation clock, and continues through Q/R without restarting the loop.
            loop.Time = Mathf.Repeat(Math.Max(0, flight.FuelTicks - state.Flight.FuelTicksLeft) / 60f, clip.length);
            loop.Speed = 1f;
            _currentAnimState = loop;

            bool upperBody = state.State is ActionState.Attacking or ActionState.Aiming
                && Flying.KeepsFlight(state.AttackSlot) && _activeMove != null;
            if (upperBody)
            {
                if (_flightUpperLayer == null)
                {
                    _flightUpperMask = new AvatarMask { name = "Flight upper body", hideFlags = HideFlags.HideAndDontSave };
                    for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                        _flightUpperMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part,
                            part is (int)AvatarMaskBodyPart.Body or (int)AvatarMaskBodyPart.Head
                                or (int)AvatarMaskBodyPart.LeftArm or (int)AvatarMaskBodyPart.RightArm
                                or (int)AvatarMaskBodyPart.LeftFingers or (int)AvatarMaskBodyPart.RightFingers);
                    _flightUpperLayer = _animancer.Layers[1];
                    _flightUpperLayer.Mask = _flightUpperMask;
                }
                if (state.State != _lastAnimState || state.AttackSlot != _lastAttackSlot
                    || state.AnimIndex != _lastClipIndex || state.AttackSequence != _lastState.AttackSequence
                    || _flightUpperLayer.TargetWeight == 0f)
                    PlayAbilityAnim(state, _activeMove, state.AnimIndex, _flightUpperLayer);
            }
            else FadeFlightUpperBody();
            return true;
        }

        private void FadeFlightUpperBody()
        {
            if (_flightUpperLayer != null && _flightUpperLayer.TargetWeight != 0f)
                _flightUpperLayer.StartFade(0f, 0.1f);
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

        /// <summary>Largest gap, in ticks, a fresh attack clip seeks forward to meet the simulation.</summary>
        private const int MaxClipStartSeekTicks = 8;

        /// <summary>Ticks into the current stage; AttackElapsedTicks counts from the activation.</summary>
        private static int StageElapsedTicks(in CharacterState state, CookedSlotDefinition spec, int clipIndex)
            => (int)spec.Clip(clipIndex).At(state.AttackElapsedTicks).ElapsedTicks;

        private static bool IsPhaseClip(CookedSlotDefinition spec, int clipIndex)
            => spec.Clip(clipIndex).IsPhaseClip;

        private float GetAbilityAnimationSpeed(CookedSlotDefinition spec, int clipIndex, string animationName)
        {
            var moveClip = spec.Clip(clipIndex);
            if (_bakedData == null || moveClip.IsPhaseClip) return 1f;
            int bakedIndex = _bakedData.FindAnimIndex(animationName);
            if (bakedIndex < 0) return 1f;
            int frameCount = _bakedData.Animations[bakedIndex].FrameCount;
            int duration = moveClip.DurationTicks;
            if (clipIndex > 0 && duration > frameCount) duration = frameCount;
            return duration > 0 ? (float)frameCount / duration : 1f;
        }

        /// <summary>
        /// Play the ability clip for a combo stage with baked-frame speed and
        /// extrapolation overrides. Returns true when a clip played.
        /// </summary>
        private bool PlayAbilityAnim(CharacterState state, CookedSlotDefinition spec, int comboStage, AnimancerLayer layer = null)
        {
            if (spec == null)
                return false;

            string animName = "spell_lmb_1";
            float animSpeed = 1f;
            if (spec.ClipCount > 0)
            {
                // Stage index may exceed the authored clip list (single-anim slots wrap —
                // e.g. Manki A release ComboStage 1, only anim.manki.ga). GetAnimationName
                // wraps; using the wrapped index keeps the baked-duration lookup consistent.
                int stageIdx = comboStage % spec.ClipCount;
                animName = spec.Clip(stageIdx).AnimationId;
                animSpeed = GetAbilityAnimationSpeed(spec, comboStage, animName);
            }

            if (!TryGetAnimation(animName, out var clip, out var extrapolation))
                return false;

            float fadeDuration = _lastAnimState is ActionState.Crouching or ActionState.Sliding ? 0f : 0.05f;
            // A phase clip continues the attack from the previous clip's last pose (Bonk E lands on his swing's
            // end). A crossfade would blend two far-apart poses of the swing in muscle space and flip the plank.
            if (_lastAnimState == ActionState.Attacking && IsPhaseClip(spec, comboStage))
                fadeDuration = 0f;
            var animState = layer != null ? layer.Play(clip, fadeDuration) : _animancer.Play(clip, fadeDuration);
            // Animancer intentionally preserves a reused state's time. Attack
            // restarts, including same-slot IASA, must rewind the clip.
            animState.Time = 0;
            animState.Speed = animSpeed;
            // The first attack state a client applies is already a tick or more into the
            // attack; start the clip where the simulation is, so drawn limbs match the
            // tested ones. A larger gap (an aim hold's running clock) keeps the restart.
            if (state.State == ActionState.Attacking && spec.Timeline.Stages is { Count: > 0 })
            {
                int elapsed = StageElapsedTicks(state, spec, comboStage);
                if (elapsed <= MaxClipStartSeekTicks)
                    animState.Time = elapsed / 60f * animSpeed;
            }
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
            _tumbleState = null;
            _tumbleLaunchPending = false;
        }

        /// <summary>
        /// Strong launches tumble: enough applied hitstun, sent upward steeply enough.
        /// Spikes, flat ground kicks and small pops keep the hit reaction.
        /// </summary>
        private static bool IsTumbleLaunch(int appliedHitstunTicks, float kvx, float kvy, float kvz)
        {
            if (appliedHitstunTicks < TumbleMinHitstunTicks || kvy <= 0f)
                return false;
            float horizontal = Mathf.Sqrt(kvx * kvx + kvz * kvz);
            return Mathf.Atan2(kvy, horizontal) * Mathf.Rad2Deg >= TumbleMinLaunchElevationDegrees;
        }

        /// <summary>Clip time for a tumble that has played for `seconds`: the build-up once, then the loop.</summary>
        private static float TumbleTime(bool clipLoops, float length, float seconds)
        {
            if (length <= 0f || seconds < length)
                return Mathf.Max(0f, seconds);
            if (clipLoops)
                return seconds % length;
            float loopStart = length > TumbleLoopStartSeconds + 0.1f ? TumbleLoopStartSeconds : 0f;
            return loopStart + (seconds - loopStart) % (length - loopStart);
        }

        /// <summary>A launch that lands inside hitstun finishes the hit clip's ground recovery.</summary>
        private void PlayHitRecovery(string hitName, CharacterState state)
        {
            if (!TryGetAnimation(hitName, out var clip, out _) || clip.length <= 0f)
                return;
            int applied = Math.Max(1, _tumbleLaunchHitstunTicks);
            float progress = Mathf.Clamp01((float)(applied - state.HitstunTicks) / applied);
            var recovery = _animancer.Play(clip, 0.1f);
            recovery.Time = progress * clip.length;
            recovery.Speed = clip.length * (1f - progress) / (Math.Max(1, (int)state.HitstunTicks) / 60f);
            _currentAnimState = recovery;
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

            // The build-up starts from its compact catch, whichever hit snap it blends from;
            // MaintainTumbleLoop then cycles the rest until the fighter acts or lands.
            var tumbleState = _animancer.Play(clip, TumbleEntryFadeSeconds);
            tumbleState.Time = 0f;
            tumbleState.Speed = 1f;
            _tumbleActive = true;
            _tumbleState = tumbleState;
            _tumbleLaunchPending = false;
            _currentAnimState = tumbleState;
            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            return true;
        }

        private void UpdateAnimationState(CharacterState state, bool wallJumped)
        {
            if (_animancer == null || _charDef == null)
                return;
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
                if (_flightUpperLayer != null) _flightUpperLayer.Speed = 0f;
                if (GrabThrow.TrySamplePose(in state, out _)
                    && TryResolveGrabAnimation(in state, out var releaseClip, out float releaseTime))
                {
                    var releasePose = _animancer.Play(releaseClip, 0f);
                    releasePose.Time = releaseTime;
                    releasePose.Speed = 0f;
                }
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

            if (_flightUpperLayer != null) _flightUpperLayer.Speed = 1f;
            if (state.Flight.Active && TryPlayFlying(state))
            {
                ClearTumble();
                _jumpArcActive = false;
                _lastClipIndex = state.AnimIndex;
                _lastAttackSlot = state.AttackSlot;
                _lastAnimState = state.State;
                _wasGrounded = state.IsGrounded;
                return;
            }
            FadeFlightUpperBody();

            float hSpeed = new Vector3(state.VX, 0f, state.VZ).magnitude;

            // ── Action animation state (Attacking / AirDodgeMovement / Hitstun) ──
            bool isCombat = state.State == ActionState.Attacking
                || state.State == ActionState.AirDodgeMovement
                || state.State is ActionState.HomingDashFloat or ActionState.HomingDash
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
                {
                    ClearTumble();
                    if (TryGetAnimation(hitName, out var clip, out _))
                    {
                        // A strong launch shows only the reaction's impact snap at its own
                        // speed; the tumble takes over after TumbleImpactSnapTicks. Other hits
                        // stretch the whole reaction over hitstun.
                        bool launch = IsTumbleLaunch(state.HitstunTicks, state.KVX, state.KVY, state.KVZ)
                            && TryGetAnimation(_charDef.TumbleAnim, out _, out _);
                        float hsAnimSpeed = launch ? 1f : GetAnimSpeedFromDuration(hitName, state.HitstunTicks);
                        var hsState = _animancer.Play(clip, 0f);
                        hsState.Speed = hsAnimSpeed;
                        _lastHitstunAnimName = hitName;
                        _inHitstunAnim = !launch;
                        _tumbleLaunchPending = launch;
                        _tumbleLaunchHitstunTicks = state.HitstunTicks;
                        _presentedHitReactionSequence = state.HitReactionSequence;
                        _hitstunAnimStartTime = Time.time;
                        _hitstunAnimLength = clip.length / hsAnimSpeed;
                        // DIAG: hitstun clip restart — which clip, why, and the
                        // tick delta vs the previous applied state.
                        Debug.Log($"[HitstunAnim] RESTART ent={_entityId} clip={hitName} " +
                            $"ticks={state.HitstunTicks} prevTicks={_lastState.HitstunTicks} " +
                            $"level={state.HitstunLevel} firstEntry={_lastAnimState != ActionState.Hitstun} " +
                            $"speed={hsAnimSpeed:F2} tumble={launch}");
                    }
                }
                else if (_tumbleLaunchPending
                    && _tumbleLaunchHitstunTicks - state.HitstunTicks >= TumbleImpactSnapTicks)
                {
                    _tumbleLaunchPending = false;
                    if (state.IsGrounded)
                        PlayHitRecovery(hitName, state);
                    else
                        TryPlayTumble();
                }
                else if (_tumbleActive && state.IsGrounded)
                {
                    // Landed inside hitstun (on a platform): the reaction's ground recovery fills the rest.
                    ClearTumble();
                    PlayHitRecovery(hitName, state);
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
                    || (state.JumpsLeft < _lastState.JumpsLeft
                        || wallJumped)
                    || (_lastState.IsGrounded && !state.IsGrounded);
                bool airborneLaunch = !actionStarted && IsStrongAirborneLaunch(state);
                if (elapsed < _hitstunAnimLength
                    && (state.State == ActionState.Hitstun || airborneLaunch))
                {
                    _lastAnimState = state.State;
                    return;
                }
                _inHitstunAnim = false;
            }
            // Leaving hitstun before the impact snap ends (Burst) drops the pending tumble.
            _tumbleLaunchPending = false;

            // The tumble keeps carrying the fighter after hitstun, as in Melee, until it
            // acts (the action's clip cuts in at once) or lands (locomotion blends in).
            if (_tumbleActive)
            {
                bool jumpStarted = (state.JumpsLeft < _lastState.JumpsLeft
                        || wallJumped)
                    || (_lastState.IsGrounded && !state.IsGrounded);
                bool explicitAction = state.State != ActionState.Idle
                    && state.State != ActionState.Run;
                if (state.IsGrounded || jumpStarted || explicitAction)
                {
                    ClearTumble();
                }
                else
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
                or ActionState.Throwing)
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
                _lastClipIndex = state.AnimIndex;
                _lastAttackSlot = state.AttackSlot;
                _lastAnimState = state.State;
                _wasGrounded = state.IsGrounded;
                return;
            }

            // Caught on a tether (ADR-0048) or carried by a wave (ADR-0050): a stand-in held small-hit pose while
            // the line holds, the arrival window runs or the wave carries. The Shared hurtbox uses the same clip.
            if (ActionStates.Captive(state.State))
            {
                _jumpArcActive = false;
                if (TryGetAnimation(_charDef.HitSmallAnim, out var reeledClip, out _))
                {
                    var animation = _animancer.States.Current;
                    if (animation == null || animation.Clip != reeledClip)
                        animation = _animancer.Play(reeledClip, 0.08f);
                    animation.Time = Mathf.Min(CaptivePoseTicks(in state) / 60f, reeledClip.length);
                    animation.Speed = 0f;
                    _currentAnimState = animation;
                }
                _lastClipIndex = state.AnimIndex;
                _lastAttackSlot = state.AttackSlot;
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
                var aimSpec = aiming ? _activeMove : null;
                AnimationClip? loopClip = null;
                bool aimLoop = aiming && aimSpec?.AimAnimationId != null
                    && TryGetAnimation(aimSpec.AimAnimationId, out loopClip, out _);
                bool fixedAimHold = aiming && !aimLoop
                    && (_activeMove?.AimMovement ?? AuthoringAimMovementMode.Fixed) == AuthoringAimMovementMode.Fixed;
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
                else if (state.State == ActionState.Shell && TryPlayShellPose())
                {
                    _jumpArcActive = false;
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

                    bool doubleJumped = (state.JumpsLeft < _lastState.JumpsLeft
                        || wallJumped);

                    if (isAscending)
                    {
                        // New ascent: initial jump, or double jump from descent
                        if (!wasAscending || wallJumped)
                        {
                            // Only play jump_up if it's a fresh jump (not recovery from attack hitstun)
                            if (_jumpArcActive || doubleJumped)
                            {
                                if ((wallJumped && !string.IsNullOrEmpty(_charDef.WallJumpAnim)
                                        && TryGetAnimation(_charDef.WallJumpAnim, out var clip, out _))
                                    || TryGetAnimation(_charDef.JumpAnim, out clip, out _))
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
                            var locomotion = _animancer.Play(clip, fadeDuration);
                            if (animationId == _charDef.RunAnim)
                                locomotion.Speed = Mathf.Max(0.35f, hSpeed / _charDef.Movement.RunSpeed);
                            else
                                locomotion.Speed = 1f;
                        }
                    }
                }
            }

            // ── Combat state changes (excluding hitstun — handled above) ──
            bool stateChanged = state.State != _lastAnimState
                || (state.State == ActionState.Attacking && (
                    state.AttackSlot != _lastAttackSlot
                    || state.AnimIndex != _lastClipIndex
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
                        var spec = _activeMove;
                        PlayAbilityAnim(state, spec, state.AnimIndex);
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
                else if (state.State == ActionState.HomingDashFloat)
                {
                    // Placeholder: the dodge clip winds up to its midpoint over the float,
                    // then holds that lean through the dash.
                    string dashAnim = string.IsNullOrEmpty(_charDef.AirDodgeAnim)
                        ? _charDef.DashAnim : _charDef.AirDodgeAnim;
                    if (TryGetAnimation(dashAnim, out var clip, out _))
                    {
                        var floatState = _animancer.Play(clip, 0.05f);
                        floatState.Time = 0f;
                        floatState.Speed = clip.length * 0.5f / (HomingDashConfig.FloatTicks / 60f);
                    }
                }
                else if (state.State == ActionState.HomingDash && _animancer.States.Current is { } dashPose)
                    dashPose.Speed = 0f;
            }

            _lastClipIndex = state.AnimIndex;
            _wasGrounded = state.IsGrounded;

            // ── Attack accent lifecycle ──
            _lastAttackSlot = state.AttackSlot;
            _lastAnimState = state.State;
        }

        private void MaintainTumbleLoop()
        {
            if (!_tumbleActive
                || _tumbleState == null
                || !ReferenceEquals(_animancer?.States.Current, _tumbleState)
                || _tumbleState.IsLooping
                || _tumbleState.Length <= 0f
                || _tumbleState.Time < _tumbleState.Length)
                return;

            // Clips imported without Loop Time play their build-up once, then cycle from its end.
            _tumbleState.Time = TumbleTime(false, _tumbleState.Length, _tumbleState.Time);
            _tumbleState.IsPlaying = true;
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

        /// <summary>
        /// Reset animation state to defaults. Useful on respawn.
        /// </summary>
        public void ResetAnimationState()
        {
            HideShield();
            _animancer.Stop();
            if (_flightUpperLayer != null)
            {
                _flightUpperLayer.Weight = 0f;
                _flightUpperLayer.Speed = 1f;
            }
            _kamikazePitch = 0f;
            _wasGrounded = true;
            _lastAnimState = default;
            _lastAttackSlot = 0;
            _lastClipIndex = 0;
            _jumpArcActive = false;

            _activeExtrapolator = null;
            _currentExtrapolationMode = ExtrapolationMode.None;
            _currentAnimState = null;
            _hitstopPaused = false;
            ClearTumble();
            ClearLandingLagPresentation();

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
            var point = CharacterBoneRegistry.Find(name);
            var animator = Animator;
            if (point != null && animator != null && animator.isHuman)
                return animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), point.HumanoidBone));
            if (CharacterBoneRegistry.IsRigId(name))
                return FindBone(name.Substring(CharacterBoneRegistry.RigPrefix.Length));
            return FindBone(PresentationBoneName(name));
        }

        public static string PresentationBoneName(string name)
            => CharacterBoneRegistry.BakedName(name);

        private Transform FindBone(string name)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
