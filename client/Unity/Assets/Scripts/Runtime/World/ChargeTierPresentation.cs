using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Implemented by presentation components that read their owner's cosmetic state.
    /// <see cref="TimelinePresentationDispatcher"/> binds them right after spawning a cue or
    /// hit effect. Previews that do not bind leave them on their default look.
    /// </summary>
    public interface IPresentationOwnerReceiver
    {
        void BindOwner(PlayerRenderer owner);
    }

    /// <summary>
    /// Charge tiers of a slot's charged capability: a charged projectile or directional dash
    /// (three tiers) or Manki's Aerosol Inferno (five).
    /// </summary>
    public static class ChargeTiers
    {
        /// <summary>0 for tier 1, 1 for tier 2 and so on; -1 when the slot has no charged capability.</summary>
        public static int Of(CharacterDefinition definition, byte slot, ushort chargeTicks)
        {
            foreach (bool airborne in new[] { false, true })
            {
                var cooked = definition?.GetCookedSlotAbility(slot, airborne);
                if (cooked == null) continue;
                foreach (var stage in cooked.Timeline.Stages)
                foreach (var operation in stage.Operations)
                {
                    if (operation is not CookedStartCapabilityOperation start) continue;
                    if (start.Parameters is CookedChargedProjectileCapabilityParameters projectile)
                        return projectile.GetChargeTier(chargeTicks);
                    if (start.Parameters is CookedChargedDirectionalDashCapabilityParameters dash)
                        return dash.GetChargeTier(chargeTicks);
                    if (start.Parameters is CookedMankiAerosolInfernoCapabilityParameters aerosol)
                    {
                        // Below the first threshold the charge still reads as tier 1.
                        int reached = 0;
                        foreach (var tier in aerosol.ChargeTiers ?? System.Array.Empty<MankiAerosolChargeTier>())
                            if (chargeTicks >= tier.ChargeTicks) reached++;
                        return Mathf.Max(0, reached - 1);
                    }
                }
            }
            return -1;
        }
    }
}
