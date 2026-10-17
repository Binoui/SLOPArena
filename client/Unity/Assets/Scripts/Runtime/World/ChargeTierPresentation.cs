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
        public static int Of(CharacterDefinition definition, byte slot, ushort chargeTicks) => Find(definition, slot) switch
        {
            CookedChargedProjectileCapabilityParameters projectile => projectile.GetChargeTier(chargeTicks),
            CookedChargedDirectionalDashCapabilityParameters dash => dash.GetChargeTier(chargeTicks),
            // Below the first threshold the charge still reads as tier 1.
            CookedMankiAerosolInfernoCapabilityParameters aerosol => Mathf.Max(0, Reached(aerosol, chargeTicks) - 1),
            _ => -1,
        };

        /// <summary>The charge from 0 to 1 at the capability's full charge; 0 when the slot has none.</summary>
        public static float Progress(CharacterDefinition definition, byte slot, ushort chargeTicks)
        {
            ushort full = Find(definition, slot) switch
            {
                CookedChargedProjectileCapabilityParameters projectile => projectile.MaxChargeTicks,
                CookedChargedDirectionalDashCapabilityParameters dash => dash.MaxChargeTicks,
                CookedMankiAerosolInfernoCapabilityParameters aerosol => aerosol.MaxChargeTicks,
                _ => (ushort)0,
            };
            return full > 0 ? Mathf.Clamp01(chargeTicks / (float)full) : 0f;
        }

        private static int Reached(CookedMankiAerosolInfernoCapabilityParameters aerosol, ushort chargeTicks)
        {
            int reached = 0;
            foreach (var tier in aerosol.ChargeTiers ?? System.Array.Empty<MankiAerosolChargeTier>())
                if (chargeTicks >= tier.ChargeTicks) reached++;
            return reached;
        }

        private static CookedCapabilityParameters Find(CharacterDefinition definition, byte slot)
        {
            foreach (bool airborne in new[] { false, true })
            {
                var cooked = definition?.GetCookedSlotAbility(slot, airborne);
                if (cooked == null) continue;
                foreach (var stage in cooked.Timeline.Stages)
                foreach (var operation in stage.Operations)
                    if (operation is CookedStartCapabilityOperation { Parameters: CookedChargedProjectileCapabilityParameters
                        or CookedChargedDirectionalDashCapabilityParameters or CookedMankiAerosolInfernoCapabilityParameters } start)
                        return start.Parameters;
            }
            return null;
        }
    }
}
