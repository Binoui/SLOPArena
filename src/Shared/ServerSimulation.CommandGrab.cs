using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    /// <summary>
    /// Command grabs (ADR-0051, slop.ability.command-grab.v1): a slot's capture pairs both fighters in the
    /// <see cref="DefenseInteractionPhase.CommandHold"/> phase, the grabber Attacking in her grab and the victim Grabbed.
    /// Grab rules apply: the capture is routed before the shield check, armour does not resist it, and a grab that
    /// takes her on the same tick clashes. Deaths, resets and damaging hits end the pair through
    /// <see cref="InterruptInteraction"/>, and <see cref="CommandHolds"/> checks both sides on every pair pass.
    /// </summary>
    public partial class ServerSimulation
    {
        // Releases queued by grabs this tick; the hit pass of the same tick resolves them, so they are never snapshotted.
        private readonly List<SpellResolver.HitResult> _commandReleases = new();

        /// <summary>
        /// A capture contact on a fighter, after invincibility and mode-target rules. A fighter already paired, held or
        /// hit in this batch, or one that cannot be placed at her anchor, is ignored and the window stays open.
        /// </summary>
        private void CatchOnCommandGrab(in SpellResolver.HitResult hit, CommandGrabAbility grab,
            ref CharacterState grabber, ref CharacterState victim)
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
            grabber.CapturedYaw = CapturedYawOf(grabber.FacingYaw);
            if (!CanPlaceVictim(in grabber, in victim, Def(grabberId), Def(victimId), out float x, out float y, out float z))
                return;
            RetireAbility(victimId, ref victim, AbilityExit.Superseded);
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
            victim.State = ActionState.Grabbed;
            victim.StateTicks = (ushort)Math.Max(1, grab.ReleaseTick - grab.Ticks);
            victim.CapturedYaw = grabber.CapturedYaw;
            victim.FacingYaw = victim.AimYaw = grabber.FacingYaw;
            victim.PX = x; victim.PY = y; victim.PZ = z;
            victim.VX = victim.VY = victim.VZ = 0f;
            grab.OnCaught(ref grabber);
        }

        /// <summary>Whether the fighter's own grab attempt resolves on her this tick: the two grabs clash.</summary>
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
        /// One hold step, called by the grabber's grab once per ability tick: the pair must still hold, and the victim
        /// is placed at her grab anchor. A placement a wall refuses keeps the victim where it was.
        /// </summary>
        internal bool HoldCommandGrab(ref CharacterState grabber)
        {
            ulong victimId = grabber.InteractionPartnerId;
            if (grabber.InteractionId == 0 || grabber.InteractionPhase != (byte)DefenseInteractionPhase.CommandHold
                || !_states.TryGetValue(victimId, out var victim) || victim.State != ActionState.Grabbed
                || victim.InteractionId != grabber.InteractionId || victim.InteractionPartnerId != grabber.EntityId)
                return false;
            if (CanPlaceVictim(in grabber, in victim, Def(grabber.EntityId), Def(victimId), out float x, out float y, out float z))
            {
                victim.PX = x; victim.PY = y; victim.PZ = z;
            }
            victim.VX = victim.VY = victim.VZ = 0f;
            victim.FacingYaw = victim.AimYaw = grabber.FacingYaw;
            _states[victimId] = victim;
            return true;
        }

        /// <summary>
        /// The release: the pair ends with the victim free of the hold, and the authored launch is queued as the
        /// grabber's hit for this tick's hit pass, after both fighters have moved.
        /// </summary>
        internal void ReleaseCommandGrab(ref CharacterState grabber, CookedCommandGrabCapabilityParameters p)
        {
            ulong victimId = grabber.InteractionPartnerId, interactionId = grabber.InteractionId;
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
                Damage = p.Launch.Damage,
                KnockbackAngle = (sbyte)p.Launch.Angle,
                KnockbackDirection = AuthoringKnockbackDirection.AwayFromOwner,
                BaseKnockback = p.Launch.BaseKnockback,
                KnockbackGrowth = p.Launch.KnockbackGrowth,
                StunTicks = p.Launch.StunTicks,
                Unblockable = true,
                FreezesOwner = true,
                HitX = victim.PX,
                HitY = victim.PY + victimDef.CapsuleHeight * 0.25f,
                HitZ = victim.PZ,
                ContactTime = 1f,
            });
        }

        /// <summary>
        /// Whether a command hold still holds, seen from either side: the grabber is Attacking in a grab that is
        /// holding, the victim is Grabbed, both name each other on the same interaction in the command-hold phase, and
        /// neither has a hit reaction pending. Anything else is interrupted by the pair pass.
        /// </summary>
        private bool CommandHolds(ulong id, in CharacterState state)
        {
            bool isVictim = state.State == ActionState.Grabbed;
            ulong grabberId = isVictim ? state.InteractionPartnerId : id;
            ulong victimId = isVictim ? id : state.InteractionPartnerId;
            return _states.TryGetValue(grabberId, out var grabber) && _states.TryGetValue(victimId, out var victim)
                && grabber.State == ActionState.Attacking && victim.State == ActionState.Grabbed
                && grabber.InteractionId == state.InteractionId && victim.InteractionId == state.InteractionId
                && grabber.InteractionPartnerId == victimId && victim.InteractionPartnerId == grabberId
                && grabber.InteractionPhase == (byte)DefenseInteractionPhase.CommandHold
                && victim.InteractionPhase == (byte)DefenseInteractionPhase.CommandHold
                && !IsEliminated(grabber) && !IsEliminated(victim)
                && Unhit(in grabber) && Unhit(in victim)
                && _activeAbilities.TryGetValue(grabberId, out var ability) && ability.CommandGrab is { Holding: true };
        }
    }
}
