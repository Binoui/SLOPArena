using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>What a contact of a flash-cut activation installs on its victim (ADR-0052).</summary>
    public readonly record struct HeldCutRelease(uint PopTick, string PopPresentationId, int CueIndex);

    /// <summary>
    /// The held cut (ADR-0052): a hit whose victim stays frozen until its cutter's pop tick, then takes the ordinary
    /// queued launch. The freeze is the victim's own hitstop, so the pop happens whatever befalls the cutter.
    /// </summary>
    public partial class ServerSimulation
    {
        /// <summary>One held victim. It outlives the cutter's move so the release can play the pop where it stood.</summary>
        internal readonly record struct HeldCut(ulong VictimId, byte VictimDeaths, ulong CutterId, ulong ActivationId,
            uint PopTick, byte AttackSequence, int CueIndex, string PopPresentationId);

        private readonly List<HeldCut> _heldCuts = new();
        private readonly List<(float DistanceSquared, ulong Id, byte Deaths)> _flashCandidates = new();

        /// <summary>Holds an unarmoured damaging contact's victim until the pop and returns that freeze.</summary>
        private ushort HoldCut(in SpellResolver.HitResult hit, ref CharacterState victim, HeldCutRelease release)
        {
            victim.State = ActionState.CutHeld;
            _heldCuts.Add(new HeldCut(hit.TargetEntityId, victim.Deaths, hit.OwnerEntityId, hit.ActivationId,
                release.PopTick, hit.AttackSequence, release.CueIndex, release.PopPresentationId));
            return (ushort)Math.Min(ushort.MaxValue, release.PopTick - _tick);
        }

        /// <summary>
        /// A contact on a fighter already held: its damage, its presentation and the hitter's ordinary owner freeze.
        /// The hold, the queued launch and the KO credit stay the cut's, so the pop still lands on time.
        /// </summary>
        private void HitHeldFighter(in SpellResolver.HitResult hit, ref CharacterState held,
            ref CharacterState attacker, bool attackerExists, int operationIndex)
        {
            float damage = IsMayhemShove(hit.OwnerEntityId, hit.TargetEntityId) ? 0f : hit.Damage;
            held.DamagePercent = (ushort)Math.Min(999, held.DamagePercent + (ushort)damage);
            if (damage > 0f) _damagedThisTick.Add(hit.TargetEntityId);
            ushort freeze = damage <= 0f && hit.StunTicks == 0 ? (ushort)0 : ComputeHitstopTicks(damage);
            if (freeze > 0 && hit.FreezesOwner && attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
                attacker.HitstopTicks = freeze;
            var resolved = hit;
            resolved.MatchTick = _tick;
            resolved.Damage = damage;
            resolved.ImpactForce = 0f;
            resolved.HitstopTicks = 0;
            LastTickHits.Add(resolved);
            EmitHitPresentation(in hit, damage, in attacker, attackerExists, operationIndex);
        }

        /// <summary>
        /// The flash's lock: eligible enemy fighters whose centre lies within <paramref name="range"/> of the caster's,
        /// nearest first, ties by entity ID. Shielding and air-dodging fighters lock (their defence decides the cut);
        /// fighters in respawn grace, in a pair or already held do not.
        /// </summary>
        internal (ulong Id, byte Deaths)[] LockFlashTargets(in CharacterState s, float range, int max)
        {
            _flashCandidates.Clear();
            foreach (var (id, target) in _states)
            {
                if (!IsFlashVictim(id, target.Deaths, allowHeld: false) || target.InteractionId != 0
                    || target.InvincibilityTicks > 0
                        && target.State is not (ActionState.AirDodgeMovement or ActionState.AirDodgeRecovery)
                    || !CanAttackTarget(s.EntityId, id) || !IsEligibleEnemy(s.EntityId, id, s.PX, s.PZ, range, out _))
                    continue;
                float dx = target.PX - s.PX, dy = target.PY - s.PY, dz = target.PZ - s.PZ;
                float distanceSquared = dx * dx + dy * dy + dz * dz;
                if (distanceSquared <= range * range) _flashCandidates.Add((distanceSquared, id, target.Deaths));
            }
            _flashCandidates.Sort((a, b) => a.DistanceSquared != b.DistanceSquared
                ? a.DistanceSquared.CompareTo(b.DistanceSquared) : a.Id.CompareTo(b.Id));
            var locked = new (ulong Id, byte Deaths)[Math.Min(max, _flashCandidates.Count)];
            for (int i = 0; i < locked.Length; i++)
                locked[i] = (_flashCandidates[i].Id, _flashCandidates[i].Deaths);
            return locked;
        }

        /// <summary>Whether a locked fighter is still the same live, reachable life; held ones only when allowed.</summary>
        internal bool IsFlashVictim(ulong id, byte deaths, bool allowHeld)
            => _states.TryGetValue(id, out var victim) && _defs.ContainsKey(id) && victim.Deaths == deaths
               && !IsEliminated(victim) && !IsOutsideBlastZone(in victim)
               && (allowHeld || victim.State != ActionState.CutHeld);

        /// <summary>Whether this activation's cut holds the fighter now.</summary>
        internal bool IsHeldBy(ulong victimId, ulong cutterId, ulong activationId)
        {
            if (!_states.TryGetValue(victimId, out var victim) || victim.State != ActionState.CutHeld) return false;
            foreach (var cut in _heldCuts)
                if (cut.VictimId == victimId && cut.CutterId == cutterId && cut.ActivationId == activationId
                    && cut.VictimDeaths == victim.Deaths)
                    return true;
            return false;
        }

        /// <summary>
        /// After movement, where held freezes end: a victim released on its pop tick was launched there, so the
        /// cutter's pop plays where it stood. A record whose victim left the hold another way (death, respawn, a
        /// mode reset) goes quietly.
        /// </summary>
        private void ReleaseHeldCuts()
        {
            for (int i = 0; i < _heldCuts.Count; i++)
            {
                var cut = _heldCuts[i];
                bool sameLife = _states.TryGetValue(cut.VictimId, out var victim) && victim.Deaths == cut.VictimDeaths;
                if (sameLife && victim.State == ActionState.CutHeld) continue;
                if (sameLife && _tick == cut.PopTick && !string.IsNullOrEmpty(cut.PopPresentationId)
                    && _states.ContainsKey(cut.CutterId))
                    _presentationEvents.Add(new TimelinePresentationEvent(_tick, cut.CutterId, cut.CueIndex,
                        cut.PopPresentationId, cut.AttackSequence, PresentationEventSource.CapabilityLaunch,
                        victim.PX, victim.PY, victim.PZ,
                        victim.KVX * victim.KVX + victim.KVZ * victim.KVZ > 0.000001f
                            ? MathF.Atan2(victim.KVX, victim.KVZ) : victim.FacingYaw));
                _heldCuts.RemoveAt(i--);
            }
        }

        /// <summary>
        /// On its pop tick, before movement fires the queued launch, a held victim is aimed away from its cutter while
        /// he is still in that flash. The cut stored the direction toward where he stops, so a low-percent pop would
        /// launch the victim into his back. An interrupted cutter leaves the cut's own direction.
        /// </summary>
        private void AimHeldCutPops()
        {
            foreach (var cut in _heldCuts)
            {
                if (cut.PopTick != _tick
                    || !_states.TryGetValue(cut.VictimId, out var victim) || victim.State != ActionState.CutHeld
                    || victim.Deaths != cut.VictimDeaths
                    || !_activeAbilities.TryGetValue(cut.CutterId, out var flash) || flash.ActivationId != cut.ActivationId
                    || !_states.TryGetValue(cut.CutterId, out var cutter))
                    continue;
                float dx = victim.PX - cutter.PX, dz = victim.PZ - cutter.PZ;
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance <= 0.001f) continue;
                victim.QueuedKBDirX = dx / distance;
                victim.QueuedKBDirZ = dz / distance;
                _states[cut.VictimId] = victim;
            }
        }

        private void WriteHeldCuts(SnapshotWriter w)
        {
            w.WriteInt(_heldCuts.Count);
            foreach (var cut in _heldCuts)
            {
                w.WriteULong(cut.VictimId); w.WriteByte(cut.VictimDeaths); w.WriteULong(cut.CutterId);
                w.WriteULong(cut.ActivationId); w.WriteUInt(cut.PopTick); w.WriteByte(cut.AttackSequence);
                w.WriteInt(cut.CueIndex); w.WriteString(cut.PopPresentationId);
            }
        }

        private static HeldCut[] ReadHeldCuts(SnapshotReader r)
        {
            var cuts = new HeldCut[r.ReadCount(30)];
            for (int i = 0; i < cuts.Length; i++)
                cuts[i] = new HeldCut(r.ReadULong(), r.ReadByte(), r.ReadULong(), r.ReadULong(), r.ReadUInt(),
                    r.ReadByte(), r.ReadInt(), r.ReadString() ?? "");
            return cuts;
        }
    }
}
