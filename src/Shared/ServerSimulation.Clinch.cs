using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    /// <summary>
    /// Clinches (ADR-0053): a lunge's catch holding one fighter in front of its holder. The pair lives in the ordinary
    /// interaction fields with phase <see cref="DefenseInteractionPhase.Clinching"/>, so death, removal, resets and a
    /// third party's damaging hit end it through <see cref="InterruptInteraction"/> like a grab. This file opens the
    /// pair from a catch, lets the holder's own hits land as damage and hitstop, places the victim (its only position
    /// writer while paired), releases it, and checks both sides of every pair.
    /// </summary>
    public partial class ServerSimulation
    {
        /// <summary>
        /// A lunge contact on a fighter, after invincibility and before the shield: a shield does not stop it. A fighter
        /// already paired or held, or already damaged in this batch, is refused and the lunge goes on. Otherwise the
        /// catch supersedes the victim's move and any reaction in progress, pairs both fighters, deals the catch's
        /// damage with no launch or hitstop and credits it. Armour does not resist it, as for grabs.
        /// </summary>
        private void CatchOnClinch(in SpellResolver.HitResult hit, ClinchAbility clinch,
            ref CharacterState holder, ref CharacterState victim, Dictionary<ulong, InputState> inputs, int operationIndex)
        {
            ulong holderId = hit.OwnerEntityId, victimId = hit.TargetEntityId;
            if (holderId == victimId || holder.InteractionId != 0 || victim.InteractionId != 0
                || ActionStates.Captive(victim.State) || _damagedThisTick.Contains(victimId))
                return;
            RetireAbility(victimId, ref victim, AbilityExit.Superseded);
            Flying.End(ref victim, Def(victimId), victimId != NoCooldownsEntityId);
            ClearReaction(ref victim);
            unchecked
            {
                _nextInteractionId++;
                if (_nextInteractionId == 0)
                    _nextInteractionId = 1;
            }
            holder.InteractionId = victim.InteractionId = _nextInteractionId;
            holder.InteractionPartnerId = victimId;
            victim.InteractionPartnerId = holderId;
            holder.InteractionPhase = victim.InteractionPhase = (byte)DefenseInteractionPhase.Clinching;
            holder.InteractionTick = victim.InteractionTick = _tick;
            victim.State = ActionState.Clinched;
            victim.StateTicks = 0;
            victim.FacingYaw = victim.AimYaw = MathF.Atan2(holder.PX - victim.PX, holder.PZ - victim.PZ);

            float damage = IsMayhemShove(holderId, victimId) ? 0f : hit.Damage;
            victim.DamagePercent = (ushort)Math.Min(999, victim.DamagePercent + (ushort)damage);
            if (damage > 0f)
            {
                _damagedThisTick.Add(victimId);
                TryLockOnAfterHit(holderId, victimId, ref holder, inputs);
                TryLockOnAfterHit(victimId, holderId, ref victim, inputs);
            }
            if (_mayhem == null) _lastHitCredits[victimId] = (holderId, _tick, hit.AttackSlot);
            _lastHitContexts[victimId] = (holderId, _tick, hit.AttackSlot);
            clinch.OnCaught(ref holder);

            var resolved = hit;
            resolved.MatchTick = _tick;
            resolved.Damage = damage;
            resolved.ImpactForce = 0f;
            resolved.HitstopTicks = 0;
            LastTickHits.Add(resolved);
            EmitHitPresentation(in hit, damage, in holder, attackerExists: true, operationIndex);
        }

        /// <summary>
        /// A hit from the holder's own move on the fighter it holds: damage, credit, presentation and hitstop for both,
        /// with no launch, no stun and no interruption, so the next authored hit finds the victim where it was.
        /// </summary>
        private void HitClinchedFighter(in SpellResolver.HitResult hit, ref CharacterState victim,
            ref CharacterState holder, Dictionary<ulong, InputState> inputs, int operationIndex)
        {
            ulong holderId = hit.OwnerEntityId, victimId = hit.TargetEntityId;
            float damage = IsMayhemShove(holderId, victimId) ? 0f : hit.Damage;
            victim.DamagePercent = (ushort)Math.Min(999, victim.DamagePercent + (ushort)damage);
            ushort freeze = damage <= 0f && hit.StunTicks == 0 ? (ushort)0 : ComputeHitstopTicks(damage);
            victim.HitstopTicks = freeze;
            if (freeze > 0 && hit.FreezesOwner) holder.HitstopTicks = freeze;
            // A new reaction for the client's flinch; the held pose is all it shows.
            victim.HitReactionSequence = unchecked((ushort)(victim.HitReactionSequence + 1));
            if (damage > 0f)
            {
                _damagedThisTick.Add(victimId);
                TryLockOnAfterHit(holderId, victimId, ref holder, inputs);
                TryLockOnAfterHit(victimId, holderId, ref victim, inputs);
            }
            if (_mayhem == null) _lastHitCredits[victimId] = (holderId, _tick, hit.AttackSlot);
            _lastHitContexts[victimId] = (holderId, _tick, hit.AttackSlot);

            var resolved = hit;
            resolved.MatchTick = _tick;
            resolved.Damage = damage;
            resolved.ImpactForce = 0f;
            resolved.HitstopTicks = freeze;
            LastTickHits.Add(resolved);
            ContactAbility(in hit)?.OnHitConfirmed(in resolved, in victim, operationIndex);
            EmitHitPresentation(in hit, damage, in holder, attackerExists: true, operationIndex);
        }

        /// <summary>
        /// One hold step, called by the holder's clinch once per ability tick (its freeze pauses it): the victim moves
        /// to the anchor in front of the holder's feet through the body mover, so walls stop it, and faces the holder.
        /// It floats there in the air as on the ground. False when the pair is gone (a hit, a death, a reset).
        /// </summary>
        internal bool HoldClinch(ref CharacterState holder, CookedClinchCapabilityParameters p)
        {
            ulong victimId = holder.InteractionPartnerId;
            if (holder.InteractionId == 0 || holder.InteractionPhase != (byte)DefenseInteractionPhase.Clinching
                || !_states.TryGetValue(victimId, out var victim) || !TryDef(victimId, out var victimDef)
                || victim.State != ActionState.Clinched || victim.InteractionId != holder.InteractionId
                || victim.InteractionPartnerId != holder.EntityId)
                return false;
            var anchor = TetherAnchor(in holder, Def(holder.EntityId), victimDef, p.AnchorDistance);
            float dy = anchor.Y - victim.PY;
            victim.VX = (anchor.X - victim.PX) / Simulation.TickDt;
            victim.VY = (victim.IsGrounded && dy < 0f ? 0f : dy) / Simulation.TickDt;
            victim.VZ = (anchor.Z - victim.PZ) / Simulation.TickDt;
            Simulation.MoveDefenseBody(ref victim, victimDef, _arena);
            victim.VX = victim.VY = victim.VZ = 0f;
            // Carried, not jumping: once released in the air, gravity applies at once (as after a walk-off).
            if (!victim.IsGrounded)
                victim.AirTimeTicks = Math.Max(victim.AirTimeTicks, victimDef.Movement.FloatWindowTicks);
            victim.FacingYaw = victim.AimYaw = MathF.Atan2(holder.PX - victim.PX, holder.PZ - victim.PZ);
            _states[victimId] = victim;
            return true;
        }

        /// <summary>
        /// The pair ends on both sides and the victim stays Clinched, unpaired, for <paramref name="lockTicks"/> with
        /// ordinary gravity, as a tether's arrival does, so the holder's next authored hit meets it as an ordinary hit.
        /// </summary>
        internal void ReleaseClinch(ref CharacterState holder, ushort lockTicks)
        {
            ulong interactionId = holder.InteractionId;
            ulong victimId = holder.InteractionPartnerId;
            if (_states.TryGetValue(victimId, out var victim) && victim.InteractionId == interactionId
                && victim.State == ActionState.Clinched)
            {
                victim.StateTicks = lockTicks;
                MarkInteractionTerminal(ref victim, interactionId);
                _states[victimId] = victim;
            }
            MarkInteractionTerminal(ref holder, interactionId);
        }

        /// <summary>
        /// Whether a Clinching pair still holds, seen from either side: the holder is Attacking in a clinch that holds,
        /// the victim is Clinched, both name each other on the same interaction, and neither is in hitstun or flying
        /// from a launch. Hitstop is the holder's own hits landing. Anything else is interrupted by the pair pass.
        /// </summary>
        private bool ClinchHolds(ulong id, in CharacterState state)
        {
            bool isVictim = state.State == ActionState.Clinched;
            ulong holderId = isVictim ? state.InteractionPartnerId : id;
            ulong victimId = isVictim ? id : state.InteractionPartnerId;
            return _states.TryGetValue(holderId, out var holder) && _states.TryGetValue(victimId, out var victim)
                && holder.State == ActionState.Attacking && victim.State == ActionState.Clinched
                && holder.InteractionId == state.InteractionId && victim.InteractionId == state.InteractionId
                && holder.InteractionPartnerId == victimId && victim.InteractionPartnerId == holderId
                && holder.InteractionPhase == (byte)DefenseInteractionPhase.Clinching
                && victim.InteractionPhase == (byte)DefenseInteractionPhase.Clinching
                && !IsEliminated(holder) && !IsEliminated(victim)
                && Unlaunched(in holder) && Unlaunched(in victim)
                && _activeAbilities.TryGetValue(holderId, out var ability) && ability.Clinch is { Holding: true };
        }

        private static bool Unlaunched(in CharacterState s) => s.HitstunTicks == 0 && !Simulation.HasKnockback(s);
    }
}
