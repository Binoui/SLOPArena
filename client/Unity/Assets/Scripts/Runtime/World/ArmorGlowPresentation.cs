using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// An armour-window cue: holds a rim glow on its fighter while the cue lives (bind it with the
    /// armour window's duration and <see cref="AttackBoundPresentation"/>, so interruption ends it),
    /// and plays a copy of <see cref="Absorb"/> each time that armour absorbs a hit. The copy is
    /// detached, so it outlives a window that ends right after the hit. Cosmetic only: it reads
    /// accepted hits from <see cref="FighterHitFeel"/> and never affects gameplay.
    /// </summary>
    public sealed class ArmorGlowPresentation : MonoBehaviour, IPresentationOwnerReceiver
    {
        public Color RimColor = new(1f, 0.3f, 0.25f, 1f);
        [Tooltip("Inactive child copied, detached and played when the armour absorbs a hit.")]
        public GameObject? Absorb;
        public float AbsorbSeconds = 1.2f;

        private FighterHitFeel? _hitFeel;

        public void BindOwner(PlayerRenderer owner)
        {
            Release();
            _hitFeel = owner != null ? owner.HitFeel : null;
            if (_hitFeel == null) return;
            _hitFeel.BeginGlow(this, RimColor);
            _hitFeel.ArmorAbsorbed += OnArmorAbsorbed;
        }

        private void OnArmorAbsorbed()
        {
            if (Absorb == null) return;
            Transform source = Absorb.transform;
            GameObject copy = Instantiate(Absorb, source.position, source.rotation);
            copy.transform.localScale = source.lossyScale;
            copy.SetActive(true);
            Destroy(copy, AbsorbSeconds);
        }

        private void OnDestroy() => Release();

        private void Release()
        {
            if (_hitFeel == null) return;
            _hitFeel.EndGlow(this);
            _hitFeel.ArmorAbsorbed -= OnArmorAbsorbed;
            _hitFeel = null;
        }
    }
}
