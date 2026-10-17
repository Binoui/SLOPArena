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
        [Tooltip("Played on reaching tier 2 and tier 3; children start stopped.")]
        public ParticleSystem Tier2, Tier3;
        [Tooltip("Optional effect that runs only while charging; it stops emitting on release.")]
        public ParticleSystem Charging;

        private PlayerRenderer _owner;
        private int _shownTier;

        public void BindOwner(PlayerRenderer owner) => _owner = owner;

        private void LateUpdate()
        {
            if (_owner == null) return;
            if (_owner.CurrentActionState != ActionState.Aiming || _owner.CurrentAttackSlot != Slot)
            {
                if (Charging != null && Charging.isEmitting) Charging.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            int tier = ChargeTiers.Of(_owner.CharacterDef, Slot, _owner.CurrentChargeTicks);
            for (int reached = _shownTier + 1; reached <= tier; reached++)
                (reached == 1 ? Tier2 : Tier3)?.Play(true);
            _shownTier = Mathf.Max(_shownTier, tier);
        }
    }
}
