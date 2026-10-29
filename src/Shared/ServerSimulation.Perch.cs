using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>
    /// The perch's pop (ADR-0051, Nyx's Hex Crow). A damaging hit from the thrower's other form on a fighter carrying
    /// her perch takes the perch and queues its pop. The pop joins the hit pass on the victim's first unfrozen tick
    /// after that hit, so it lands as a second hit, credited to her and through shields. It is not tied to any
    /// activation, so a move started after the swap pops it too.
    /// </summary>
    public partial class ServerSimulation
    {
        /// <summary>One queued pop. It outlives the perch so a restore between the hit and the pop replays it.</summary>
        internal readonly record struct PendingPop(ulong OwnerId, ulong VictimId, byte VictimDeaths, uint QueuedTick,
            byte AttackSlot, bool Airborne, byte Form, byte AttackSequence, ProjectilePerch Pop);

        private readonly List<PendingPop> _pendingPops = new();
        private System.Func<ulong, ulong, bool>? _opposesProjectileOwner;

        private bool OpposesProjectileOwner(ulong attackOwnerId, ulong projectileOwnerId)
            => attackOwnerId != projectileOwnerId && _states.ContainsKey(attackOwnerId)
               && CanAttackTarget(attackOwnerId, projectileOwnerId);

        /// <summary>After a confirmed damaging hit: takes the attacker's perch on the victim if this hit pops it.</summary>
        private void QueuePerchPop(in SpellResolver.HitResult hit, in CharacterState victim)
        {
            if (!_spellResolver.TryTakePerch(hit.OwnerEntityId, hit.TargetEntityId, hit.Form, out var perch)) return;
            _pendingPops.Add(new PendingPop(hit.OwnerEntityId, hit.TargetEntityId, victim.Deaths, _tick, perch.AttackSlot,
                perch.ActivationAirborne, perch.Form, perch.AttackSequence, perch.Perch!.Value));
        }

        /// <summary>
        /// Adds the pops whose victim is out of its freeze to this tick's hits. A victim that left that life, or a
        /// thrower who is gone, drops its pop.
        /// </summary>
        private void AddDuePops(List<SpellResolver.HitResult> hits)
        {
            for (int i = 0; i < _pendingPops.Count; i++)
            {
                var pop = _pendingPops[i];
                bool sameLife = _states.TryGetValue(pop.VictimId, out var victim) && victim.Deaths == pop.VictimDeaths
                    && !IsEliminated(victim);
                if (sameLife && (pop.QueuedTick == _tick || victim.HitstopTicks > 0)) continue;
                _pendingPops.RemoveAt(i--);
                if (!sameLife || !_states.ContainsKey(pop.OwnerId) || !TryDef(pop.VictimId, out var victimDef)) continue;
                hits.Add(new SpellResolver.HitResult
                {
                    TargetEntityId = pop.VictimId,
                    OwnerEntityId = pop.OwnerId,
                    AttackSlot = pop.AttackSlot,
                    AttackSequence = pop.AttackSequence,
                    Airborne = pop.Airborne,
                    Form = pop.Form,
                    Damage = pop.Pop.Damage,
                    KnockbackAngle = pop.Pop.Angle,
                    KnockbackDirection = AuthoringKnockbackDirection.AwayFromOwner,
                    BaseKnockback = pop.Pop.BaseKnockback,
                    KnockbackGrowth = pop.Pop.KnockbackGrowth,
                    StunTicks = pop.Pop.StunTicks,
                    Unblockable = true,
                    HitX = victim.PX,
                    HitY = victim.PY + victimDef.CapsuleHeight * 0.5f,
                    HitZ = victim.PZ,
                    ContactTime = 1f,
                    HitPresentationId = pop.Pop.PresentationId,
                });
            }
        }

        /// <summary>A knocked-out fighter's perches go, and so do pops queued by or on them.</summary>
        private void ForgetPerches(ulong id)
        {
            _spellResolver.RemovePerches(id);
            _pendingPops.RemoveAll(pop => pop.OwnerId == id || pop.VictimId == id);
        }

        private void WritePendingPops(SnapshotWriter w)
        {
            w.WriteInt(_pendingPops.Count);
            foreach (var pop in _pendingPops)
            {
                w.WriteULong(pop.OwnerId); w.WriteULong(pop.VictimId); w.WriteByte(pop.VictimDeaths);
                w.WriteUInt(pop.QueuedTick); w.WriteByte(pop.AttackSlot); w.WriteBool(pop.Airborne);
                w.WriteByte(pop.Form); w.WriteByte(pop.AttackSequence);
                w.WriteFloat(pop.Pop.Damage); w.WriteSByte(pop.Pop.Angle); w.WriteFloat(pop.Pop.BaseKnockback);
                w.WriteFloat(pop.Pop.KnockbackGrowth); w.WriteUShort(pop.Pop.StunTicks); w.WriteString(pop.Pop.PresentationId);
            }
        }

        private static PendingPop[] ReadPendingPops(SnapshotReader r)
        {
            var pops = new PendingPop[r.ReadCount(40)];
            for (int i = 0; i < pops.Length; i++)
                pops[i] = new PendingPop(r.ReadULong(), r.ReadULong(), r.ReadByte(), r.ReadUInt(), r.ReadByte(),
                    r.ReadBool(), r.ReadByte(), r.ReadByte(), new ProjectilePerch
                    {
                        Damage = r.ReadFloat(), Angle = r.ReadSByte(), BaseKnockback = r.ReadFloat(),
                        KnockbackGrowth = r.ReadFloat(), StunTicks = r.ReadUShort(), PresentationId = r.ReadString(),
                    });
            return pops;
        }
    }
}
