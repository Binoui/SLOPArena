using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Plays a child effect each time the owner's charge reaches a new tier while it holds
    /// <see cref="Slot"/>. Put it on an attack-bound cue spawned at tick 0. Attack ticks keep
    /// counting while an aim-hold charges, so give the cue a duration longer than any charge;
    /// the attack's end still clears it.
    /// </summary>
    public sealed class ChargeTierCues : MonoBehaviour, IPresentationOwnerReceiver
    {
        public byte Slot = AbilitySlots.A;
        [Tooltip("Played on reaching tiers 2 to 5, with an AudioSource on the same object if any; children start stopped.")]
        public ParticleSystem Tier2, Tier3, Tier4, Tier5;
        [Tooltip("Optional effect that runs only while charging; it stops emitting on release.")]
        public ParticleSystem Charging;
        [Tooltip("Optional transform scaled by 1 + GrowPerTier × tier while charging, e.g. a pilot flame (use Hierarchy scaling).")]
        public Transform Grow;
        public float GrowPerTier;

        private PlayerRenderer _owner;
        private int _shownTier;

        public void BindOwner(PlayerRenderer owner) => _owner = owner;

        private void LateUpdate()
        {
            if (_owner == null) return;
            if (_owner.CurrentActionState != ActionState.Aiming || _owner.CurrentAttackSlot != Slot)
            {
                // isPlaying, not isEmitting: a silent controller system never emits but still owns
                // the charge effects below it.
                if (Charging != null && Charging.isPlaying) Charging.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            int tier = ChargeTiers.Of(_owner.CurrentMove, _owner.CurrentChargeTicks);
            for (int reached = _shownTier + 1; reached <= tier; reached++)
                Play(reached switch { 1 => Tier2, 2 => Tier3, 3 => Tier4, 4 => Tier5, _ => null });
            _shownTier = Mathf.Max(_shownTier, tier);
            if (Grow != null) Grow.localScale = Vector3.one * (1f + GrowPerTier * Mathf.Max(0, _shownTier));
        }

        private static void Play(ParticleSystem cue)
        {
            if (cue == null) return;
            cue.Play(true);
            if (cue.TryGetComponent(out AudioSource sound)) sound.Play();
        }
    }
}
