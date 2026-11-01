using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>
    /// Perching projectiles (ADR-0051, Nyx's Hex Crow). They rise, mark and dive (section 9), then ride the sticky
    /// path without its explosions: at most one per thrower on each carrier, broken in flight by an opposing attack,
    /// swatted, pecking and popped by the simulation's rules. Every ending but a pop or a KO scatters the perch.
    /// </summary>
    public partial class SpellResolver
    {
        private readonly List<ulong> _perchedThisTick = new();
        private readonly List<PerchScatter> _scatteredPerches = new();

        /// <summary>A perch that left without a pop, where it was. The simulation drains these into presentation.</summary>
        internal readonly record struct PerchScatter(Hitbox Perch, float X, float Y, float Z);

        /// <summary>Perches scattered since the simulation last drained them.</summary>
        internal List<PerchScatter> ScatteredPerches => _scatteredPerches;

        private void Scatter(in Hitbox perch, float x, float y, float z)
            => _scatteredPerches.Add(new PerchScatter(perch, x, y, z));

        /// <summary>
        /// Steps every perch in flight before the hit pass. Rising follows its target's feet toward the hover point;
        /// arriving or running out of rise time starts Marking, which fixes the spot. Marking holds still, then Diving
        /// flies straight at the spot and lands on it in a whole number of ticks; a dive that arrives without
        /// perching has missed. <paramref name="targetFeet"/> gives a live target's feet for the thrower.
        /// </summary>
        internal void UpdatePerchFlights(Func<ulong, ulong, (bool Exists, float X, float Y, float Z)> targetFeet)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || hb.Perch is not { } perch || perch.Phase == PerchPhase.Perched) continue;
                if (perch.PhaseTicks < ushort.MaxValue) perch.PhaseTicks++;
                switch (perch.Phase)
                {
                    case PerchPhase.Rising:
                        if (perch.TargetId != 0)
                        {
                            var feet = targetFeet(hb.OwnerId, perch.TargetId);
                            if (feet.Exists) (perch.SpotX, perch.SpotY, perch.SpotZ) = (feet.X, feet.Y, feet.Z);
                            else perch.TargetId = 0;
                        }
                        float dx = perch.SpotX - hb.X, dy = perch.SpotY + perch.HoverHeight - hb.Y, dz = perch.SpotZ - hb.Z;
                        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                        float step = perch.RiseSpeed * Simulation.TickDt;
                        if (distance <= step)
                        {
                            (hb.VX, hb.VY, hb.VZ) = (dx / Simulation.TickDt, dy / Simulation.TickDt, dz / Simulation.TickDt);
                            StartMarking(ref perch);
                        }
                        else if (perch.PhaseTicks >= perch.MaxRiseTicks)
                        {
                            hb.VX = hb.VY = hb.VZ = 0f;
                            StartMarking(ref perch);
                        }
                        else
                            (hb.VX, hb.VY, hb.VZ) = (dx / distance * perch.RiseSpeed, dy / distance * perch.RiseSpeed,
                                dz / distance * perch.RiseSpeed);
                        break;
                    case PerchPhase.Marking:
                        hb.VX = hb.VY = hb.VZ = 0f;
                        if (perch.PhaseTicks <= perch.MarkTicks) break;
                        float lx = perch.SpotX - hb.X, ly = perch.SpotY - hb.Y, lz = perch.SpotZ - hb.Z;
                        float length = MathF.Sqrt(lx * lx + ly * ly + lz * lz);
                        // Rounded up, forgiving float noise on an exact whole number of ticks.
                        perch.DiveTicks = (ushort)Math.Max(1, (int)MathF.Ceiling(length / (perch.DiveSpeed * Simulation.TickDt) - 1e-3f));
                        float seconds = perch.DiveTicks * Simulation.TickDt;
                        (hb.VX, hb.VY, hb.VZ) = (lx / seconds, ly / seconds, lz / seconds);
                        perch.Phase = PerchPhase.Diving;
                        perch.PhaseTicks = 1;
                        break;
                    case PerchPhase.Diving:
                        if (perch.PhaseTicks <= perch.DiveTicks) break;
                        Scatter(in hb, hb.X, hb.Y, hb.Z);
                        OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
                        _hitboxes.RemoveAt(i);
                        continue;
                }
                hb.Perch = perch;
                _hitboxes[i] = hb;
            }
        }

        private static void StartMarking(ref ProjectilePerch perch)
        {
            perch.Phase = PerchPhase.Marking;
            perch.PhaseTicks = 0;
            perch.TargetId = 0;
        }

        /// <summary>Whether <paramref name="carrierId"/> carries a perch thrown by <paramref name="ownerId"/>.</summary>
        internal bool CarriesPerch(ulong ownerId, ulong carrierId)
            => _hitboxes.Exists(hb => hb.Active && hb.Perch is { Phase: PerchPhase.Perched } && hb.OwnerId == ownerId
                && hb.AttachedEntityId == carrierId);

        /// <summary>Removes one perch by emission and scatters it (a swat).</summary>
        internal void ScatterPerch(ulong emissionId)
        {
            int index = _hitboxes.FindIndex(hb => hb.EmissionId == emissionId && hb.Perch.HasValue);
            if (index < 0) return;
            var hb = _hitboxes[index];
            Scatter(in hb, hb.X, hb.Y, hb.Z);
            OnHitboxRemoved?.Invoke(hb, hb.X, hb.Y, hb.Z);
            _hitboxes.RemoveAt(index);
        }

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
                Scatter(in crow, crow.X, crow.Y, crow.Z);
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
                    Scatter(in hb, hb.X, hb.Y, hb.Z);
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
