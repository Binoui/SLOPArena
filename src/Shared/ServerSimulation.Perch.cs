using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    /// <summary>
    /// The perch's simulation rules (ADR-0051, Nyx's Hex Crow): its target's feet, the swat, the pecks, the scatter
    /// presentation and the pop. A damaging hit from the thrower's other form on a fighter carrying
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
        private System.Func<ulong, ulong, (bool Exists, float X, float Y, float Z)>? _perchTargetFeet;

        /// <summary>A target a perch may rise over: an enemy of the thrower, at its feet (a fighter's capsule bottom).</summary>
        internal bool TryGetPerchTargetFeet(ulong ownerId, ulong targetId, out ArcadePoint feet)
        {
            feet = default;
            if (targetId == 0 || targetId == ownerId || !CanAttackTarget(ownerId, targetId)
                || !TryGetCombatTargetPosition(targetId, out var position))
                return false;
            float y = _states.ContainsKey(targetId) && TryDef(targetId, out var def)
                ? position.Y - def.CapsuleHeight * 0.5f : position.Y;
            feet = new ArcadePoint(position.X, y, position.Z);
            return true;
        }

        private (bool Exists, float X, float Y, float Z) PerchTargetFeet(ulong ownerId, ulong targetId)
            => TryGetPerchTargetFeet(ownerId, targetId, out var feet) ? (true, feet.X, feet.Y, feet.Z) : (false, 0f, 0f, 0f);

        /// <summary>Whether <paramref name="carrierId"/> carries a perch thrown by <paramref name="ownerId"/> (ADR-0051 section 10).</summary>
        internal bool CarriesPerch(ulong ownerId, ulong carrierId) => _spellResolver.CarriesPerch(ownerId, carrierId);

        /// <summary>A carrier who shields or starts an air dodge swats its perches off, before this tick's hit pass.</summary>
        private void SwatPerches()
        {
            var hitboxes = _spellResolver.GetActiveHitboxes();
            for (int i = hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = hitboxes[i];
                if (hb.Perch is not { Phase: PerchPhase.Perched } || !_states.TryGetValue(hb.AttachedEntityId, out var carrier)
                    || carrier.State is not (ActionState.Shielding or ActionState.AirDodgeMovement))
                    continue;
                _spellResolver.ScatterPerch(hb.EmissionId);
            }
        }

        /// <summary>
        /// Perched pecks due this tick: chip damage on the carrier with no hitstun, hitstop, launch or credit, so
        /// whatever the carrier is doing goes on. Invincibility skips a peck.
        /// </summary>
        private void PeckCarriers()
        {
            foreach (var hb in _spellResolver.GetActiveHitboxes())
            {
                if (hb.Perch is not { PecksNow: true } perch || !_states.TryGetValue(hb.AttachedEntityId, out var carrier)
                    || carrier.InvincibilityTicks > 0 || IsEliminated(carrier))
                    continue;
                carrier.DamagePercent = (ushort)Math.Min(999, carrier.DamagePercent + (int)perch.PeckDamage);
                _states[hb.AttachedEntityId] = carrier;
                if (perch.PeckPresentationId is { } id && TryDef(hb.AttachedEntityId, out var def))
                    EmitPerchEvent(in hb, id, PresentationEventSource.CapabilityLaunch, carrier.PX,
                        carrier.PY + def.CapsuleHeight * 0.5f, carrier.PZ, 20);
            }
        }

        /// <summary>Every perch that left without a pop this tick scatters where it was.</summary>
        private void EmitPerchScatters()
        {
            foreach (var gone in _spellResolver.ScatteredPerches)
                if (gone.Perch.Perch?.ScatterPresentationId is { } id)
                    EmitPerchEvent(gone.Perch, id, PresentationEventSource.CapabilityLanding, gone.X, gone.Y, gone.Z, 30);
            _spellResolver.ScatteredPerches.Clear();
        }

        private void EmitPerchEvent(in Hitbox perch, string id, PresentationEventSource source, float x, float y, float z,
            ushort durationTicks)
            => _presentationEvents.Add(new TimelinePresentationEvent(_tick, perch.OwnerId, perch.VisualOperationIndex, id,
                perch.AttackSequence, source, x, y, z, 0f)
            {
                Placement = new PresentationPlacement(DurationTicks: durationTicks),
            });

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
