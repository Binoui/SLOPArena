using System.Collections.Generic;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;
using SlopArena.Shared;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Converts accepted simulation hits into the shared light/medium/heavy/launch grammar.
    /// Package hit presentations replace only the graphic; impact sounds and gameplay feedback remain.
    /// </summary>
    public sealed class CombatFeedback : MonoBehaviour
    {
        private const float MediumDamage = 6f;
        private const float HeavyDamage = 11f;
        private const float LaunchForce = 12f;

        private ISimulationBridge _bridge;
        private CombatSFX _sfx;
        private readonly Dictionary<ulong, CharacterDefinition> _definitions = new();
        private readonly Dictionary<ulong, PlayerRenderer> _renderers = new();

        private void Awake()
        {
            _sfx = GetComponent<CombatSFX>() ?? gameObject.AddComponent<CombatSFX>();
        }

        public void RegisterRenderer(PlayerRenderer renderer)
        {
            if (renderer == null)
                return;
            _renderers[renderer.EntityId] = renderer;
            if (renderer.CharacterDef != null)
                _definitions[renderer.EntityId] = renderer.CharacterDef;
        }

        public void SetSimulation(ISimulationBridge bridge)
        {
            _bridge = bridge;
            GraphicHitEffect.Prewarm();
        }

        /// <summary>Call once after the bridge advances its simulation tick.</summary>
        public void OnTick()
        {
            if (_bridge == null)
                return;

            foreach (var hit in _bridge.LastTickHits)
            {
                if (hit.Blocked) continue; // block feedback arrives once via its authoritative semantic event
                ImpactTier tier = Classify(in hit);
                _definitions.TryGetValue(hit.OwnerEntityId, out var definition);
                var slot = hit.AttackSlot > 0
                    ? definition?.GetSlotAbility((byte)(hit.AttackSlot - 1), hit.Airborne)
                    : null;
                // Overrides arrive through the authoritative semantic event dispatcher.
                if (string.IsNullOrEmpty(slot?.HitPresentationId))
                    GraphicHitEffect.Spawn(in hit, tier);
                // A pure push (no damage, no stun: a drop of Bashie's Hose Down stream) shows only its own
                // presentation. No flash, impact sound or rumble, at up to 20 drops a second.
                if (hit.Damage <= 0f && hit.StunTicks == 0) continue;
                if (_renderers.TryGetValue(hit.TargetEntityId, out var victim) && victim != null)
                    victim.HitFeel?.ReceiveHit(in hit, tier, MatchVisualStyle.RimColorFor(hit.OwnerEntityId));
                _sfx.Play(tier, definition?.Class ?? CharacterClass.FightGuy);
                ClientSettingsService.Instance.PlayConfirmedImpact();
            }
        }

        public static ImpactTier Classify(in SpellResolver.HitResult hit)
        {
            if (hit.ImpactForce >= LaunchForce)
                return ImpactTier.Launch;
            if (hit.Damage >= HeavyDamage)
                return ImpactTier.Heavy;
            if (hit.Damage >= MediumDamage)
                return ImpactTier.Medium;
            return ImpactTier.Light;
        }
    }
}
