using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Makes the owner's held prop for <see cref="Slot"/> show the charge (Manki's spray can for
    /// Aerosol Inferno): it grows, shakes harder and heats toward <see cref="Hot"/> as the charge
    /// builds, kicks at each new tier and throbs once the charge is full. The cue root follows the
    /// prop's tip as it grows, so effects placed there (a pilot flame) stay on it. Put it on an
    /// attack-bound cue spawned at tick 0, attached to the bone that holds the prop. Cosmetic
    /// only: it reads the replicated charge and restores the prop on release or when the cue ends.
    /// </summary>
    [DefaultExecutionOrder(100)] // after WeaponAttach poses the prop in its LateUpdate
    public sealed class ChargePropFeedback : MonoBehaviour, IPresentationOwnerReceiver
    {
        public byte Slot = AbilitySlots.F;
        [Tooltip("Extra size at full charge: 0.35 is 35% bigger.")]
        public float Grow = 0.35f;
        [Tooltip("Shake in degrees at the start and at full charge.")]
        public float ShakeStart = 1.5f, ShakeFull = 11f;
        [Tooltip("Positional jitter in metres at full charge.")]
        public float Jitter = 0.02f;
        [Tooltip("Tint at full heat; HDR values glow under the match bloom.")]
        public Color Hot = new(1.6f, 0.32f, 0.2f, 1f);
        [Tooltip("Optional shake marks whose emission rate rises with the charge.")]
        public ParticleSystem Marks;
        public float MarksBoost = 2.5f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private PlayerRenderer _owner;
        private Transform _prop;
        private Vector3 _propScale, _cueLocal;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private float _marksRate, _kick;
        private int _tier = -1;
        private bool _applied;

        public void BindOwner(PlayerRenderer owner) => _owner = owner;

        private void Awake()
        {
            if (Marks != null) _marksRate = Marks.emission.rateOverTimeMultiplier;
        }

        private void LateUpdate()
        {
            if (_owner == null) return;
            if (_owner.CurrentActionState != ActionState.Aiming || _owner.CurrentAttackSlot != Slot)
            {
                Restore();
                return;
            }
            if (_prop == null && !Capture()) return;
            if (!_prop.gameObject.activeInHierarchy) return;

            ushort charge = _owner.CurrentChargeTicks;
            float progress = ChargeTiers.Progress(_owner.CharacterDef, Slot, charge);
            int tier = ChargeTiers.Of(_owner.CharacterDef, Slot, charge);
            if (tier > _tier)
            {
                if (_tier >= 0) _kick = 1f;
                _tier = tier;
            }
            _kick = Mathf.MoveTowards(_kick, 0f, Time.deltaTime / 0.15f);
            bool full = progress >= 1f;
            float throb = full ? 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 10f) : 0f;

            // Grow about the prop's pivot and keep the cue on the prop's tip.
            float size = 1f + Grow * progress + 0.08f * _kick + 0.04f * throb;
            Vector3 pivot = transform.parent != null ? transform.parent.InverseTransformPoint(_prop.position) : Vector3.zero;
            _prop.localScale = _propScale * size;
            if (transform.parent != null) transform.localPosition = pivot + (_cueLocal - pivot) * size;

            // Shake harder as the charge builds, with a kick at each tier.
            float degrees = Mathf.Lerp(ShakeStart, ShakeFull, progress) * (1f + _kick);
            _prop.rotation *= Quaternion.Euler(Random.Range(-degrees, degrees), Random.Range(-degrees, degrees),
                Random.Range(-degrees, degrees));
            _prop.position += Random.insideUnitSphere * (Jitter * progress);

            // Heat up toward red; once full, throb.
            float heat = Mathf.Clamp01(0.9f * progress * progress + 0.25f * _kick + 0.15f * throb);
            Tint(Color.Lerp(Color.white, Hot, heat));
            if (Marks != null)
            {
                var emission = Marks.emission;
                emission.rateOverTimeMultiplier = _marksRate * (1f + MarksBoost * progress);
            }
            _applied = true;
        }

        private bool Capture()
        {
            var attach = _owner.GetComponent<WeaponAttach>();
            _prop = attach != null ? attach.PropFor(Slot) : null;
            if (_prop == null) return false;
            // The dispatcher places the cue after Awake, so read its placement here.
            _cueLocal = transform.localPosition;
            _propScale = _prop.localScale;
            _renderers = _prop.GetComponentsInChildren<Renderer>(true);
            _block ??= new MaterialPropertyBlock();
            return true;
        }

        private void Tint(Color color)
        {
            foreach (var renderer in _renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void Restore()
        {
            if (!_applied) return;
            _applied = false;
            if (_prop != null) _prop.localScale = _propScale;
            if (_renderers != null)
                foreach (var renderer in _renderers)
                    if (renderer != null) renderer.SetPropertyBlock(null);
            transform.localPosition = _cueLocal;
        }

        private void OnDisable() => Restore();
    }
}
