using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>
    /// Perching projectiles (ADR-0051, Nyx's Hex Crow). They ride the sticky path without its explosions: at most one
    /// per thrower on each carrier, broken in flight by an opposing attack, and taken by the simulation's pop rule.
    /// </summary>
    public partial class SpellResolver
    {
        private readonly List<ulong> _perchedThisTick = new();

        /// <summary>
        /// The fragile rule: a perching projectile still in flight that overlaps an active hitbox of a fighter who may
        /// attack its thrower is destroyed, not reflected. Other perching projectiles are marks, not attacks.
        /// </summary>
        internal void BreakFragileProjectiles(Func<ulong, ulong, bool> opposes)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var crow = _hitboxes[i];
                if (!crow.Active || !crow.Perch.HasValue || crow.AttachedEntityId != 0) continue;
                var body = new EntityData
                {
                    Id = crow.OwnerId, PosX = crow.X, PosY = crow.Y, PosZ = crow.Z, EndX = crow.X, EndY = crow.Y,
                    EndZ = crow.Z, Radius = crow.Radius, Shape = HitboxShape.Sphere, Active = true,
                };
                bool broken = false;
                foreach (var attack in _hitboxes)
                {
                    if (!attack.Active || attack.Perch.HasValue || attack.AttachedEntityId != 0
                        || attack.OwnerId == crow.OwnerId || !opposes(attack.OwnerId, crow.OwnerId))
                        continue;
                    broken = OverlapsAtCurrentPosition(in attack, in body) || SweepOverlaps(in attack, in body);
                    if (broken) break;
                }
                if (!broken) continue;
                OnHitboxRemoved?.Invoke(crow, crow.X, crow.Y, crow.Z);
                _hitboxes.RemoveAt(i);
            }
        }

        private static bool SweepOverlaps(in Hitbox attack, in EntityData body)
        {
            if (attack.Sweep == null) return false;
            foreach (var pose in attack.Sweep)
            {
                var probe = attack;
                probe.X = pose.X; probe.Y = pose.Y; probe.Z = pose.Z;
                probe.EndX = pose.EndX; probe.EndY = pose.EndY; probe.EndZ = pose.EndZ;
                if (OverlapsAtCurrentPosition(in probe, in body)) return true;
            }
            return false;
        }

        /// <summary>A new perch replaces its thrower's older one on the same carrier; the later emission wins a tie.</summary>
        private void ReplacePerches()
        {
            if (_perchedThisTick.Count == 0) return;
            _perchedThisTick.Sort();
            foreach (ulong emission in _perchedThisTick)
            {
                int index = _hitboxes.FindIndex(h => h.EmissionId == emission && h.AttachedEntityId != 0);
                if (index < 0) continue;
                var kept = _hitboxes[index];
                for (int i = _hitboxes.Count - 1; i >= 0; i--)
                {
                    var hb = _hitboxes[i];
                    if (!hb.Perch.HasValue || hb.EmissionId == emission || hb.OwnerId != kept.OwnerId
                        || hb.AttachedEntityId != kept.AttachedEntityId)
                        continue;
                    OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
                    _hitboxes.RemoveAt(i);
                }
            }
            _perchedThisTick.Clear();
        }

        /// <summary>
        /// Removes and returns <paramref name="ownerId"/>'s perch on <paramref name="carrierId"/> when the hit came
        /// from a form other than the one that threw it.
        /// </summary>
        internal bool TryTakePerch(ulong ownerId, ulong carrierId, byte hitForm, out Hitbox perch)
        {
            for (int i = 0; i < _hitboxes.Count; i++)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.Perch.HasValue || hb.OwnerId != ownerId || hb.AttachedEntityId != carrierId
                    || hb.Form == hitForm)
                    continue;
                perch = hb;
                OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
                _hitboxes.RemoveAt(i);
                return true;
            }
            perch = default;
            return false;
        }

        /// <summary>A knocked-out thrower loses every perching projectile, in flight or perched.</summary>
        internal void RemovePerches(ulong ownerId)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Perch.HasValue || hb.OwnerId != ownerId) continue;
                OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
                _hitboxes.RemoveAt(i);
            }
        }
    }
}
