using System.Collections.Generic;
using UnityEngine;
using SlopArena.Shared;

namespace SlopArena.Client.Entities
{
    [ExecuteAlways]
    public class WeaponAttach : MonoBehaviour
    {
        private PlayerRenderer _owner;
        private CharacterDefinition _characterDefinition;
        private SkinnedMeshRenderer _skin;
        private bool _previewStateActive;
        private byte _previewAttackSlot;
        private int _previewAttackElapsedTicks;
        private bool _previewFirePhase;
        private Transform[] _bones;
        private Transform[] _hilts;
        private Transform[] _tips;
        private GameObject[] _instances;
        private SwordSweepTrail[] _sweepTrails;
        private WeaponEntry[] _entries;
        private WeaponAttachConfig _config;
        private bool _hitboxTrailActive;
        private bool _hasAttackIdentity;
        private byte _attackSequence;
        private byte _attackSlot;
        private byte _comboStage;
        private ushort _attackElapsedTicks;
        private byte _deaths;
        private bool _hasCapturedPosition;
        private Vector3 _lastPosition;
        private float _runtimeClock;
        private ushort _lastHitstopTicks;
        private float _previewTimelineSeconds = -1f;
        public bool HasSwordTrail { get; private set; }

        /// <summary>Resolve the equipped blade anchors used by baked weapon presentation IDs.</summary>
        public Transform ResolveTrailAnchor(string id)
        {
            Transform[] anchors = id == "_weapon_hilt" ? _hilts : id == "_weapon_tip" ? _tips : null;
            if (anchors == null || _instances == null) return null;
            for (int i = 0; i < anchors.Length; i++)
                if (anchors[i] != null && _instances[i] != null && _instances[i].activeSelf)
                    return anchors[i];
            return null;
        }

        public void Init(PlayerRenderer owner, WeaponAttachConfig config)
        {
            Cleanup();
            _owner = owner;
            _config = config;
            _characterDefinition = owner != null ? owner.CharacterDef : null;
            if (config == null || config.Entries == null || config.Entries.Length == 0)
                return;

            _skin = owner != null ? owner.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (_skin == null)
            {
                Debug.LogWarning($"[WeaponAttach] No SkinnedMeshRenderer under {owner?.name}");
                return;
            }
            _entries = config.Entries;
            int count = _entries.Length;
            _bones = new Transform[count];
            _hilts = new Transform[count];
            _tips = new Transform[count];
            _instances = new GameObject[count];
            _sweepTrails = new SwordSweepTrail[count];

            for (int i = 0; i < count; i++)
            {
                _bones[i] = FindBone(_entries[i].BoneName);
                if (_entries[i].Prefab == null)
                {
                    Debug.LogWarning($"[WeaponAttach] Prefab is null for entry {i} ({_entries[i].BoneName})");
                    continue;
                }
                _instances[i] = Instantiate(_entries[i].Prefab, owner.transform, true);
                _instances[i].SetActive(false);
                if (_entries[i].TrailStylePrefab == null)
                    continue;

                var styleSystem = _entries[i].TrailStylePrefab.GetComponentInChildren<ParticleSystem>(true);
                var styleRenderer = styleSystem != null
                    ? styleSystem.GetComponent<ParticleSystemRenderer>() : null;
                if (styleRenderer == null || styleRenderer.sharedMaterial == null)
                    continue;
                Transform weapon = _instances[i].transform;
                _hilts[i] = weapon.Find(_entries[i].TrailHiltAnchor);
                _tips[i] = weapon.Find(_entries[i].TrailTipAnchor);
                _sweepTrails[i] = new SwordSweepTrail(
                    owner.transform, _entries[i].TrailStylePrefab, _entries[i].HitboxMotionTime,
                    _entries[i].TrailBladeWidth);
                HasSwordTrail = true;
            }
        }

        /// <summary>The spawned prop for an attack slot (Manki's spray can for F), or null.</summary>
        public Transform PropFor(byte attackSlot)
        {
            if (_entries == null || _instances == null) return null;
            for (int i = 0; i < _entries.Length && i < _instances.Length; i++)
                if (_entries[i] != null && _entries[i].AttackSlot == attackSlot && _instances[i] != null)
                    return _instances[i].transform;
            return null;
        }

        public void SetPreviewState(byte attackSlot, int attackElapsedTicks, bool firePhase = false)
        {
            _previewStateActive = true;
            _previewAttackSlot = attackSlot;
            _previewAttackElapsedTicks = attackElapsedTicks;
            _previewFirePhase = firePhase;
        }

        public void SetHitboxTrailActive(bool active)
        {
            // Baked swings own their cosmetic window; resolver/snapshot gates
            // remain authoritative for the unbaked legacy attachment path.
            if (_sweepTrails == null || !_previewStateActive && _owner?.BakedData != null)
                return;
            SetTrailActive(active);
            if (_previewStateActive && !active)
            {
                ClearTrails();
                _previewTimelineSeconds = -1f;
            }
        }

        private void SetTrailActive(bool active)
        {
            _hitboxTrailActive = active;
            foreach (var trail in _sweepTrails)
                trail?.SetActive(active);
        }

        /// <summary>Capture every authoritative presentation tick, not just rendered frames.</summary>
        public void CaptureState(
            in CharacterState state, bool airborne, bool hasPose, Vector3 hilt, Vector3 tip)
        {
            if (_owner == null || _entries == null || _sweepTrails == null)
                return;
            // A swap changes the shown form's definition, not the fighter (ADR-0051).
            if (_owner.CharacterDef?.ForForm(0) != _characterDefinition?.ForForm(0))
            {
                ClearTrails();
                _hasAttackIdentity = false;
                _characterDefinition = _owner.CharacterDef;
            }
            Vector3 position = new(state.PX, state.PY, state.PZ);
            bool teleported = _hasCapturedPosition && (state.Deaths != _deaths
                || (position - _lastPosition).sqrMagnitude > 9f);
            bool attacking = state.State == ActionState.Attacking && state.AttackSlot != 0;
            bool newIdentity = attacking && (!_hasAttackIdentity
                || state.AttackSequence != _attackSequence
                || state.AttackSlot != _attackSlot
                || state.ComboStage != _comboStage
                || state.AttackElapsedTicks < _attackElapsedTicks);

            if (teleported || newIdentity)
                ClearTrails();
            if (_owner.BakedData != null)
            {
                // Hitstop's final zero-tick snapshot still holds the prior pose.
                if (_hasCapturedPosition && _lastHitstopTicks == 0)
                    _runtimeClock += 1f / 60f;
                SetTrailActive(_owner.IsSwordTrailTick(state, airborne));
            }
            _lastHitstopTicks = state.HitstopTicks;
            if (!attacking)
            {
                _hasAttackIdentity = false;
                _deaths = state.Deaths;
                _lastPosition = position;
                _hasCapturedPosition = true;
                return;
            }

            _hasAttackIdentity = true;
            _attackSequence = state.AttackSequence;
            _attackSlot = state.AttackSlot;
            _comboStage = state.ComboStage;
            _attackElapsedTicks = state.AttackElapsedTicks;
            _deaths = state.Deaths;
            _lastPosition = position;
            _hasCapturedPosition = true;
            if (_hitboxTrailActive && hasPose)
                foreach (var trail in _sweepTrails)
                    trail?.SampleAt(_runtimeClock, hilt, tip);
        }

        /// <summary>
        /// Rebuild Ability Lab's recent trail from authoritative preview states. The
        /// caller supplies the entire simulation history through the selected tick.
        /// </summary>
        public void SetPreviewTrailHistory(
            IReadOnlyList<CharacterState> states, int targetTick, bool airborne)
        {
            if (!HasSwordTrail || states == null || targetTick < 0)
                return;
            UpdateTrailWidths();
            int lastTick = Mathf.Min(targetTick, states.Count - 1);
            if (lastTick < 0)
            {
                ClearTrails();
                SetTrailActive(false);
                return;
            }
            float targetTime = targetTick / 60f;
            _previewTimelineSeconds = targetTime;
            float oldestTime = targetTime - TrailLifetime();
            float sampleTime = lastTick / 60f;
            int firstTick = lastTick;
            // Walk by animation time, not wall ticks: frozen poses retain the tail.
            while (firstTick > 0 && sampleTime >= oldestTime)
            {
                if (states[firstTick - 1].HitstopTicks == 0)
                    sampleTime -= 1f / 60f;
                firstTick--;
            }
            ClearTrails();
            bool wasSampling = false;
            for (int tick = firstTick; tick <= lastTick; tick++)
            {
                CharacterState state = states[tick];
                if (tick > firstTick && states[tick - 1].HitstopTicks == 0)
                    sampleTime += 1f / 60f;
                if (tick > firstTick)
                {
                    CharacterState previous = states[tick - 1];
                    Vector3 movement = new(state.PX - previous.PX, state.PY - previous.PY, state.PZ - previous.PZ);
                    bool reset = state.Deaths != previous.Deaths || movement.sqrMagnitude > 9f
                        || state.State == ActionState.Attacking
                            && (previous.State != ActionState.Attacking
                                || state.AttackSequence != previous.AttackSequence
                                || state.AttackSlot != previous.AttackSlot
                                || state.ComboStage != previous.ComboStage
                                || state.AttackElapsedTicks < previous.AttackElapsedTicks);
                    if (reset)
                    {
                        ClearTrails();
                        wasSampling = false;
                    }
                }
                Vector3 hilt = default, tip = default;
                bool sampleable = sampleTime >= oldestTime
                    && _owner.IsSwordTrailTick(state, airborne);
                if (sampleable)
                    sampleable = _owner.TrySampleBakedWeaponPath(
                        state, airborne, out hilt, out tip);
                if (!sampleable)
                {
                    if (wasSampling)
                    {
                        foreach (var trail in _sweepTrails)
                            trail?.SetActive(false);
                        wasSampling = false;
                    }
                    continue;
                }
                if (!wasSampling)
                {
                    foreach (var trail in _sweepTrails)
                        trail?.SetActive(true);
                    wasSampling = true;
                }
                foreach (var trail in _sweepTrails)
                    trail?.SampleAt(sampleTime, hilt, tip);
            }

            foreach (var trail in _sweepTrails)
            {
                trail?.SetActive(_owner.IsSwordTrailTick(states[lastTick], airborne));
                trail?.DrawAt(targetTime);
            }
            _hitboxTrailActive = _owner.IsSwordTrailTick(states[lastTick], airborne);
        }

        private float TrailLifetime()
        {
            float lifetime = 0f;
            if (_sweepTrails != null)
                foreach (var trail in _sweepTrails)
                    if (trail != null)
                        lifetime = Mathf.Max(lifetime, trail.VisibleLifetime);
            return lifetime;
        }


        private void ClearTrails()
        {
            foreach (var trail in _sweepTrails)
                trail?.Clear();
        }

        private void UpdateTrailWidths()
        {
            var entries = _config != null ? _config.Entries : null;
            if (entries == null || _sweepTrails == null) return;
            for (int i = 0; i < entries.Length && i < _sweepTrails.Length; i++)
            {
                var entry = entries[i];
                if (_instances[i] == null || entry == null) continue;
                if (_sweepTrails[i]?.StylePrefab != entry.TrailStylePrefab)
                {
                    var replacement = entry.TrailStylePrefab != null
                        ? new SwordSweepTrail(_owner.transform, entry.TrailStylePrefab,
                            entry.HitboxMotionTime, entry.TrailBladeWidth)
                        : null;
                    var previous = _sweepTrails[i];
                    _sweepTrails[i] = replacement;
                    previous?.Dispose();
                    replacement?.SetActive(_hitboxTrailActive);
                }
                _sweepTrails[i]?.SetBladeWidth(entry.TrailBladeWidth);
            }
            HasSwordTrail = false;
            foreach (var trail in _sweepTrails)
                if (trail != null) { HasSwordTrail = true; break; }
        }

        private void LateUpdate() => RefreshPresentation();

        /// <summary>Apply attachment placement and trail drawing after an Ability Lab scrub, without waiting for a frame.</summary>
        public void RefreshPresentation()
        {
            if (_owner == null || _entries == null || _instances == null || _sweepTrails == null)
                return;
            UpdateTrailWidths();
            if (_owner.BakedData == null)
            {
                if (Application.isPlaying)
                    _runtimeClock = Mathf.Max(_runtimeClock, Time.unscaledTime);
                else if (!_previewStateActive)
                    _runtimeClock += 1f / 60f;
            }
            float drawTime = _previewStateActive && _previewTimelineSeconds >= 0f
                ? _previewTimelineSeconds : _runtimeClock;

            byte slot = _previewStateActive ? _previewAttackSlot : _owner.CurrentAttackSlot;
            bool isAttacking = _previewStateActive || _owner.CurrentActionState == ActionState.Attacking;
            bool isAiming = !_previewStateActive && _owner.CurrentActionState == ActionState.Aiming;
            bool firePhase = _previewStateActive
                ? _previewFirePhase
                : _owner.CurrentActionState == ActionState.Attacking;
            int elapsedTicks = _previewStateActive
                ? _previewAttackElapsedTicks : _owner.CurrentAttackElapsedTicks;
            byte returningSlot = _previewStateActive ? (byte)0 : _owner.CurrentReturningProjectileSlot;
            for (int i = 0; i < _entries.Length; i++)
            {
                GameObject go = _instances[i];
                if (go == null)
                    continue;
                WeaponEntry entry = _entries[i];
                byte entrySlot = entry.AttackSlot;
                bool withinHold = _previewStateActive || entry.HideAfterTicks <= 0
                    || !isAttacking || elapsedTicks < entry.HideAfterTicks;
                // A prop that changes hands (Sir Bonk's pot lid: shoulder, hand, thrown, caught) follows its ticks in
                // the Ability Lab too, so a scrub shows one lid.
                bool shownYet = entry.ShowFromTicks <= 0 || !isAttacking || elapsedTicks >= entry.ShowFromTicks;
                bool takenAway = entry.HiddenBySlot != 0 && (returningSlot == entry.HiddenBySlot
                    || isAttacking && slot == entry.HiddenBySlot && elapsedTicks >= entry.HiddenBySlotFromTicks);
                bool visible = (entrySlot == 0 || (isAttacking || isAiming) && slot == entrySlot)
                    && withinHold && shownYet && !takenAway;
                if (go.activeSelf != visible)
                    go.SetActive(visible);
                if ((visible || _hitboxTrailActive) && _bones[i] != null)
                {
                    Vector3 positionOffset = firePhase && _entries[i].HasFirePhaseOverride
                        ? _entries[i].FirePositionOffset : _entries[i].PositionOffset;
                    Vector3 rotationOffset = firePhase && _entries[i].HasFirePhaseOverride
                        ? _entries[i].FireRotationOffset : _entries[i].RotationOffset;
                    go.transform.position = _bones[i].TransformPoint(positionOffset);
                    go.transform.rotation = _bones[i].rotation
                        * Quaternion.Euler(rotationOffset);
                }
                if (_hitboxTrailActive && _owner.BakedData == null
                    && _hilts[i] != null && _tips[i] != null && visible)
                    _sweepTrails[i]?.SampleAt(drawTime, _hilts[i].position, _tips[i].position);
                _sweepTrails[i]?.DrawAt(drawTime);
            }
        }

        public WeaponAttachInspectionEntry[] ReadInspectionEntries()
        {
            if (_entries == null) return null;
            var result = new WeaponAttachInspectionEntry[_entries.Length];
            for (int i = 0; i < _entries.Length; i++)
            {
                WeaponEntry entry = _entries[i];
                Transform bone = _bones != null && i < _bones.Length ? _bones[i] : null;
                GameObject instance = _instances != null && i < _instances.Length ? _instances[i] : null;
                result[i] = new WeaponAttachInspectionEntry
                {
                    Index = i,
                    BoneName = entry != null ? entry.BoneName : null,
                    BoneResolved = bone != null,
                    InstanceName = instance != null ? instance.name : null,
                    InstanceExists = instance != null,
                    Active = instance != null && instance.activeSelf,
                    WorldPosition = instance != null ? new[] { instance.transform.position.x, instance.transform.position.y, instance.transform.position.z } : null,
                    WorldRotation = instance != null ? new[] { instance.transform.rotation.x, instance.transform.rotation.y, instance.transform.rotation.z, instance.transform.rotation.w } : null
                };
            }
            return result;
        }

        public sealed class WeaponAttachInspectionEntry
        {
            [Newtonsoft.Json.JsonProperty("index")] public int Index { get; set; }
            [Newtonsoft.Json.JsonProperty("boneName")] public string BoneName { get; set; }
            [Newtonsoft.Json.JsonProperty("boneResolved")] public bool BoneResolved { get; set; }
            [Newtonsoft.Json.JsonProperty("instanceName")] public string InstanceName { get; set; }
            [Newtonsoft.Json.JsonProperty("instanceExists")] public bool InstanceExists { get; set; }
            [Newtonsoft.Json.JsonProperty("active")] public bool Active { get; set; }
            [Newtonsoft.Json.JsonProperty("worldPosition")] public float[] WorldPosition { get; set; }
            [Newtonsoft.Json.JsonProperty("worldRotation")] public float[] WorldRotation { get; set; }
        }

        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            if (_sweepTrails != null)
                foreach (var trail in _sweepTrails)
                    trail?.Dispose();
            if (_instances != null)
                foreach (var go in _instances)
                {
                    if (go == null) continue;
                    if (Application.isPlaying) Destroy(go);
                    else DestroyImmediate(go);
                }
            _owner = null;
            _config = null;
            _characterDefinition = null;
            _skin = null;
            _bones = null;
            _hilts = null;
            _tips = null;
            _instances = null;
            _sweepTrails = null;
            _entries = null;
            _hitboxTrailActive = false;
            HasSwordTrail = false;
            _previewStateActive = false;
            _previewFirePhase = false;
            _previewAttackElapsedTicks = 0;
            _hasAttackIdentity = false;
            _hasCapturedPosition = false;
            _runtimeClock = 0f;
            _lastHitstopTicks = 0;
            _previewTimelineSeconds = -1f;
        }

        private Transform FindBone(string boneName)
        {
            if (_skin == null) return null;
            foreach (var bone in _skin.bones)
                if (bone != null && bone.name == boneName)
                    return bone;
            if (boneName == "mixamorig:RightHand")
            {
                var animator = _skin.GetComponentInParent<Animator>();
                var humanoidHand = animator?.GetBoneTransform(HumanBodyBones.RightHand);
                if (humanoidHand != null)
                    return humanoidHand;
            }
            Debug.LogWarning($"[WeaponAttach] Bone '{boneName}' not found on {_owner.name}");
            return null;
        }
    }
}
