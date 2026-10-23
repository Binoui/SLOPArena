using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Shows <see cref="Variant"/> only when the owner's last fired charged attack on
    /// <see cref="Slot"/> reached <see cref="MinimumTier"/> (0-based), e.g. a fully charged
    /// projectile's hit. The variant starts inactive.
    /// </summary>
    public sealed class ChargeTierVariant : MonoBehaviour, IPresentationOwnerReceiver
    {
        public byte Slot = AbilitySlots.A;
        public int MinimumTier = 2;
        public GameObject Variant;

        public void BindOwner(PlayerRenderer owner)
        {
            if (Variant == null || owner == null || owner.LastFiredChargeSlot != Slot) return;
            Variant.SetActive(ChargeTiers.Of(owner.LastFiredChargeMove, owner.LastFiredChargeTicks) >= MinimumTier);
        }
    }
}
