using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    /// <summary>
    /// Command grabs (ADR-0057, slop.ability.command-grab.v1): a slot's capture pairs both fighters in the
    /// <see cref="DefenseInteractionPhase.CommandHold"/> phase, the grabber Attacking in its grab and the victim
    /// CommandGrabbed. Grab rules apply: the capture is routed before the shield check, armour does not resist it, and a
    /// grab that takes the grabber on the same tick clashes. Deaths, resets and a third party's damaging hit end the pair
    /// through <see cref="InterruptInteraction"/>, and <see cref="CommandHolds"/> checks both sides on every pair pass.
    /// This file opens the pair from a catch, lets the grabber's own hits land as damage and hitstop, places the victim
    /// (its only position writer while paired) and releases it.
    /// </summary>
    public partial class ServerSimulation
    {
        // Releases queued by grabs this tick; the hit pass of the same tick resolves them, so they are never snapshotted.
        private readonly List<SpellResolver.HitResult> _commandReleases = new();

        /// <summary>
        /// A capture contact on a fighter, after invincibility and mode-target rules and before the shield. A fighter
        /// already paired, frozen by a held cut or damaged in this batch, or one a grab anchor cannot reach, is ignored and
        /// the capture stays open. Otherwise the catch supersedes the victim's move, flight and any reaction in progress
        /// and pairs both fighters. A capture with damage deals it with no launch or hitstop and credits it.
        /// </summary>
        private void CatchOnCommandGrab(in SpellResolver.HitResult hit, CommandGrabAbility grab,
            ref CharacterState grabber, ref CharacterState victim, Dictionary<ulong, InputState> inputs, int operationIndex)
        {
            ulong grabberId = hit.OwnerEntityId, victimId = hit.TargetEntityId;
            if (!grab.Catches || grabber.InteractionId != 0 || victim.InteractionId != 0
                || victim.State == ActionState.CutHeld || _damagedThisTick.Contains(victimId) || IsEliminated(victim))
                return;
            if (UniversalGrabWouldTake(victimId, in victim, grabberId))
            {
                victim.StateTicks = DefenseConfig.GrabClashRecoveryTicks;
                victim.InteractionId = 0;
                victim.InteractionPartnerId = 0;
                victim.InteractionPhase = (byte)DefenseInteractionPhase.None;
                victim.InteractionTick = 0;
                grab.OnClashed(ref grabber);
                return;
            }
            var p = grab.Parameters;
            float x = 0f, y = 0f, z = 0f;
            if (p.AnchorDistance == null)
            {
                grabber.CapturedYaw = CapturedYawOf(grabber.FacingYaw);
                if (!CanPlaceVictim(in grabber, in victim, Def(grabberId), Def(victimId), out x, out y, out z))
                    return;
            }
            RetireAbility(victimId, ref victim, AbilityExit.Superseded);
            Flying.End(ref victim, Def(victimId), victimId != NoCooldownsEntityId);
            ClearReaction(ref victim);
            victim.ClearAttackFields();
            unchecked
            {
                _nextInteractionId++;
                if (_nextInteractionId == 0)
                    _nextInteractionId = 1;
            }
            grabber.InteractionId = victim.InteractionId = _nextInteractionId;
            grabber.InteractionPartnerId = victimId;
            victim.InteractionPartnerId = grabberId;
            grabber.InteractionPhase = victim.InteractionPhase = (byte)DefenseInteractionPhase.CommandHold;
            grabber.InteractionTick = victim.InteractionTick = _tick;
            victim.State = ActionState.CommandGrabbed;
            victim.StateTicks = 0;
            if (p.AnchorDistance == null)
            {
                victim.CapturedYaw = grabber.CapturedYaw;
                victim.FacingYaw = victim.AimYaw = grabber.FacingYaw;
                victim.PX = x; victim.PY = y; victim.PZ = z;
                victim.VX = victim.VY = victim.VZ = 0f;
            }
            else
                victim.FacingYaw = victim.AimYaw = MathF.Atan2(grabber.PX - victim.PX, grabber.PZ - victim.PZ);

            if (hit.Damage > 0f)
            {
                float damage = IsMayhemShove(grabberId, victimId) ? 0f : hit.Damage;
                victim.DamagePercent = (ushort)Math.Min(999, victim.DamagePercent + (ushort)damage);
                if (damage > 0f)
                {
                    _damagedThisTick.Add(victimId);
                    TryLockOnAfterHit(grabberId, victimId, ref grabber, inputs);
                    TryLockOnAfterHit(victimId, grabberId, ref victim, inputs);
                }
                if (_mayhem == null) _lastHitCredits[victimId] = (grabberId, _tick, hit.AttackSlot);
                _lastHitContexts[victimId] = (grabberId, _tick, hit.AttackSlot);
                grab.OnCaught(ref grabber);

                var resolved = hit;
                resolved.MatchTick = _tick;
                resolved.Damage = damage;
                resolved.ImpactForce = 0f;
                resolved.HitstopTicks = 0;
                LastTickHits.Add(resolved);
                EmitHitPresentation(in hit, damage, in grabber, attackerExists: true, operationIndex);
            }
            else
                grab.OnCaught(ref grabber);
        }

        /// <summary>Whether the fighter's own grab attempt resolves on the grabber this tick: the two grabs clash.</summary>
        private bool UniversalGrabWouldTake(ulong attemptId, in CharacterState attempt, ulong targetId)
            => PredictCoupledInteractions && IsActiveGrabAttempt(in attempt) && !_damagedThisTick.Contains(attemptId)
               && attempt.HitstopTicks == 0 && attempt.IsGrounded
               && attempt.VX * attempt.VX + attempt.VZ * attempt.VZ <= 0.0025f
               && FindGrabTarget(attemptId, in attempt) == targetId;

        private static short CapturedYawOf(float yaw)
        {
            int centidegrees = (int)MathF.Round(yaw * (18000f / MathF.PI));
            centidegrees = ((centidegrees + 18000) % 36000 + 36000) % 36000 - 18000;
            return (short)centidegrees;
        }

        /// <summary>
        /// A hit from the grabber's own move on the fighter it holds: damage, credit, presentation and hitstop for both,
        /// with no launch, no stun and no interruption, so the next authored hit finds the victim where it was.
        /// </summary>
        private void HitCommandGrabbedFighter(in SpellResolver.HitResult hit, ref CharacterState victim,
            ref CharacterState grabber, Dictionary<ulong, InputState> inputs, int operationIndex)
        {
            ulong grabberId = hit.OwnerEntityId, victimId = hit.TargetEntityId;
            float damage = IsMayhemShove(grabberId, victimId) ? 0f : hit.Damage;
            victim.DamagePercent = (ushort)Math.Min(999, victim.DamagePercent + (ushort)damage);
            ushort freeze = damage <= 0f && hit.StunTicks == 0 ? (ushort)0 : ComputeHitstopTicks(damage);
            victim.HitstopTicks = freeze;
            if (freeze > 0 && hit.FreezesOwner) grabber.HitstopTicks = freeze;
            // A new reaction for the client's flinch; the held pose is all it shows.
            victim.HitReactionSequence = unchecked((ushort)(victim.HitReactionSequence + 1));
            if (damage > 0f)
            {
                _damagedThisTick.Add(victimId);
                TryLockOnAfterHit(grabberId, victimId, ref grabber, inputs);
                TryLockOnAfterHit(victimId, grabberId, ref victim, inputs);
            }
            if (_mayhem == null) _lastHitCredits[victimId] = (grabberId, _tick, hit.AttackSlot);
            _lastHitContexts[victimId] = (grabberId, _tick, hit.AttackSlot);

            var resolved = hit;
            resolved.MatchTick = _tick;
            resolved.Damage = damage;
            resolved.ImpactForce = 0f;
            resolved.HitstopTicks = freeze;
            LastTickHits.Add(resolved);
            ContactAbility(in hit)?.OnHitConfirmed(in resolved, in victim, operationIndex);
            EmitHitPresentation(in hit, damage, in grabber, attackerExists: true, operationIndex);
        }

        /// <summary>
        /// One hold step, called by the grabber's grab once per ability tick (its freeze pauses it): the pair must still
        /// hold. With an anchor distance the victim moves to that anchor in front of the grabber's feet through the body
        /// mover, so walls stop it, floats there in the air as on the ground and faces the grabber. Without, it is placed
        /// at the grabber's grab anchor, facing its way; a placement a wall refuses keeps it where it was.
        /// </summary>
        internal bool HoldCommandGrab(ref CharacterState grabber, CookedCommandGrabCapabilityParameters p)
        {
            ulong victimId = grabber.InteractionPartnerId;
            if (grabber.InteractionId == 0 || grabber.InteractionPhase != (byte)DefenseInteractionPhase.CommandHold
                || !_states.TryGetValue(victimId, out var victim) || !TryDef(victimId, out var victimDef)
                || victim.State != ActionState.CommandGrabbed || victim.InteractionId != grabber.InteractionId
                || victim.InteractionPartnerId != grabber.EntityId)
                return false;
            if (p.AnchorDistance is { } distance)
            {
                var anchor = TetherAnchor(in grabber, Def(grabber.EntityId), victimDef, distance);
                float dy = anchor.Y - victim.PY;
                victim.VX = (anchor.X - victim.PX) / Simulation.TickDt;
                victim.VY = (victim.IsGrounded && dy < 0f ? 0f : dy) / Simulation.TickDt;
                victim.VZ = (anchor.Z - victim.PZ) / Simulation.TickDt;
                Simulation.MoveDefenseBody(ref victim, victimDef, _arena);
                victim.VX = victim.VY = victim.VZ = 0f;
                // Carried, not jumping: once released in the air, gravity applies at once (as after a walk-off).
                if (!victim.IsGrounded)
                    victim.AirTimeTicks = Math.Max(victim.AirTimeTicks, victimDef.Movement.FloatWindowTicks);
                victim.FacingYaw = victim.AimYaw = MathF.Atan2(grabber.PX - victim.PX, grabber.PZ - victim.PZ);
            }
            else
            {
                if (CanPlaceVictim(in grabber, in victim, Def(grabber.EntityId), victimDef, out float x, out float y, out float z))
                {
                    victim.PX = x; victim.PY = y; victim.PZ = z;
                }
                victim.VX = victim.VY = victim.VZ = 0f;
                victim.FacingYaw = victim.AimYaw = grabber.FacingYaw;
            }
            _states[victimId] = victim;
            return true;
        }

        /// <summary>
        /// The release. With a launch, the pair ends with the victim free of the hold and the launch is queued as the
        /// grabber's hit for this tick's hit pass, after both fighters have moved. Without, the victim stays
        /// CommandGrabbed, unpaired, for the release lock with ordinary gravity, as a tether's arrival does, so the
        /// grabber's next authored hit meets it as an ordinary hit.
        /// </summary>
        internal void ReleaseCommandGrab(ref CharacterState grabber, CookedCommandGrabCapabilityParameters p)
        {
            ulong victimId = grabber.InteractionPartnerId, interactionId = grabber.InteractionId;
            if (p.Launch is not { } launch)
            {
                if (_states.TryGetValue(victimId, out var held) && held.InteractionId == interactionId
                    && held.State == ActionState.CommandGrabbed)
                {
                    held.StateTicks = p.ReleaseLockTicks;
                    MarkInteractionTerminal(ref held, interactionId);
                    _states[victimId] = held;
                }
                MarkInteractionTerminal(ref grabber, interactionId);
                return;
            }
            if (!_states.TryGetValue(victimId, out var victim) || !_activeAbilities.TryGetValue(grabber.EntityId, out var ability)
                || !TryDef(victimId, out var victimDef))
                return;
            victim.State = ActionState.Idle;
            victim.StateTicks = 0;
            MarkInteractionTerminal(ref victim, interactionId);
            MarkInteractionTerminal(ref grabber, interactionId);
            _states[victimId] = victim;
            _commandReleases.Add(new SpellResolver.HitResult
            {
                TargetEntityId = victimId,
                OwnerEntityId = grabber.EntityId,
                AttackSlot = (byte)(ability.Slot + 1),
                ActivationId = ability.ActivationId,
                AttackSequence = ability.PresentationAttackSequence,
                Airborne = ability.AirborneAtStart,
                Form = ability.CharacterDef?.Form ?? 0,
                Damage = launch.Damage,
                KnockbackAngle = (sbyte)launch.Angle,
                KnockbackDirection = AuthoringKnockbackDirection.AwayFromOwner,
                BaseKnockback = launch.BaseKnockback,
                KnockbackGrowth = launch.KnockbackGrowth,
                StunTicks = launch.StunTicks,
                Unblockable = true,
                FreezesOwner = true,
                HitX = victim.PX,
                HitY = victim.PY + victimDef.CapsuleHeight * 0.25f,
                HitZ = victim.PZ,
                ContactTime = 1f,
            });
        }

        /// <summary>
        /// Whether a command hold still holds, seen from either side: the grabber is Attacking in a grab that is holding,
        /// the victim is CommandGrabbed, both name each other on the same interaction in the command-hold phase, and
        /// neither is in hitstun or flying from a launch. Hitstop is the grabber's own hits landing. Anything else is
        /// interrupted by the pair pass.
        /// </summary>
        private bool CommandHolds(ulong id, in CharacterState state)
        {
            bool isVictim = state.State == ActionState.CommandGrabbed;
            ulong grabberId = isVictim ? state.InteractionPartnerId : id;
            ulong victimId = isVictim ? id : state.InteractionPartnerId;
            return _states.TryGetValue(grabberId, out var grabber) && _states.TryGetValue(victimId, out var victim)
                && grabber.State == ActionState.Attacking && victim.State == ActionState.CommandGrabbed
                && grabber.InteractionId == state.InteractionId && victim.InteractionId == state.InteractionId
                && grabber.InteractionPartnerId == victimId && victim.InteractionPartnerId == grabberId
                && grabber.InteractionPhase == (byte)DefenseInteractionPhase.CommandHold
                && victim.InteractionPhase == (byte)DefenseInteractionPhase.CommandHold
                && !IsEliminated(grabber) && !IsEliminated(victim)
                && Unlaunched(in grabber) && Unlaunched(in victim)
                && _activeAbilities.TryGetValue(grabberId, out var ability) && ability.CommandGrab is { Holding: true };
        }

        private static bool Unlaunched(in CharacterState s) => s.HitstunTicks == 0 && !Simulation.HasKnockback(s);
    }
}
