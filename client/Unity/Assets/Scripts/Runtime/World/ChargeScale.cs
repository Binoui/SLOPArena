using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Grows a cue with the charge of the attack that fired it, once, as it spawns. Its scale goes from
    /// <see cref="MinScale"/> (a tap) to <see cref="MaxScale"/> (a full charge). Its particle bursts multiply up to
    /// <see cref="MaxCount"/> times and their start speeds up to <see cref="MaxSpeed"/> times. Bashie's Hose Down
    /// splashes more water the longer she pumped, as Manki's Aerosol Inferno grows its flame. It reads the owner's
    /// last fired charge on <see cref="Slot"/>; children should use Hierarchy scaling.
    /// </summary>
    public sealed class ChargeScale : MonoBehaviour, IPresentationOwnerReceiver
    {
        public byte Slot = AbilitySlots.A;
        public float MinScale = 0.7f;
        public float MaxScale = 1.8f;
        public float MaxCount = 3f;
        public float MaxSpeed = 1f;

        public void BindOwner(PlayerRenderer owner)
        {
            if (owner == null) return;
            ushort charge = owner.LastFiredChargeSlot == Slot ? owner.LastFiredChargeTicks : owner.CurrentChargeTicks;
            float progress = ChargeTiers.Progress(owner.LastFiredChargeSlot == Slot ? owner.LastFiredChargeMove : owner.CurrentMove, charge);
            transform.localScale = Vector3.one * Mathf.Lerp(MinScale, MaxScale, progress);
            float count = Mathf.Lerp(1f, MaxCount, progress), speed = Mathf.Lerp(1f, MaxSpeed, progress);
            foreach (var system in GetComponentsInChildren<ParticleSystem>(true))
            {
                var emission = system.emission;
                var bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);
                for (int i = 0; i < bursts.Length; i++)
                {
                    var c = bursts[i].count;
                    bool range = c.mode == ParticleSystemCurveMode.TwoConstants;
                    bursts[i].count = new ParticleSystem.MinMaxCurve(
                        Mathf.Round((range ? c.constantMin : c.constant) * count),
                        Mathf.Round((range ? c.constantMax : c.constant) * count));
                }
                emission.SetBursts(bursts);
                var main = system.main;
                main.maxParticles = Mathf.CeilToInt(main.maxParticles * count);
                main.startSpeedMultiplier *= speed;
            }
        }
    }
}
