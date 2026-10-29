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
    /// Charge tiers of a slot's charged capability: a charged projectile
    /// (three tiers). A charged stream has a charge but no tiers.
    /// </summary>
    public static class ChargeTiers
    {
        /// <summary>0 for tier 1, 1 for tier 2 and so on; -1 when the slot has no charged capability.</summary>
        public static int Of(CookedSlotDefinition? move, ushort chargeTicks) => Find(move) switch
        {
            CookedChargedProjectileCapabilityParameters projectile => projectile.GetChargeTier(chargeTicks),
            // Below the first threshold the charge still reads as tier 1.
            _ => -1,
        };

        /// <summary>The charge from 0 to 1 at the capability's full charge; 0 when the slot has none.</summary>
        public static float Progress(CookedSlotDefinition? move, ushort chargeTicks)
        {
            ushort full = Find(move) switch
            {
                CookedChargedProjectileCapabilityParameters projectile => projectile.MaxChargeTicks,
                CookedChargedStreamCapabilityParameters stream => stream.MaxChargeTicks,
                _ => (ushort)0,
            };
            return full > 0 ? Mathf.Clamp01(chargeTicks / (float)full) : 0f;
        }

        /// <summary>Steps a HUD card shows for the slot's charge: its tiers, three for a stream; 0 when it has none.</summary>
        public static int Steps(CookedSlotDefinition? move) => Find(move) switch
        {
            null => 0,
            _ => 3,
        };

        /// <summary>Steps reached at this charge: tiered moves light tier 1 on the press.</summary>
        public static int StepsReached(CookedSlotDefinition? move, ushort chargeTicks)
        {
            if (chargeTicks == 0) return 0;
            return Find(move) switch
            {
                CookedChargedStreamCapabilityParameters => Mathf.CeilToInt(Progress(move, chargeTicks) * 3f),
                null => 0,
                _ => Of(move, chargeTicks) + 1,
            };
        }

        private static CookedCapabilityParameters Find(CookedSlotDefinition? move)
        {
            if (move == null) return null;
            foreach (var stage in move.Timeline.Stages)
            foreach (var operation in stage.Operations)
                if (operation is CookedStartCapabilityOperation { Parameters: CookedChargedProjectileCapabilityParameters
                    or CookedChargedStreamCapabilityParameters } start)
                    return start.Parameters;
            return null;
        }
    }
}
