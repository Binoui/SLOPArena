using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    /// <summary>What one reel step did. Holding keeps reeling; the others end the pair.</summary>
    internal enum TetherStep : byte
    {
        /// <summary>The caught fighter moved toward her and is not there yet.</summary>
        Holding,
        /// <summary>The caught fighter is at her feet, stuck for the arrival window. The pair has ended.</summary>
        Arrived,
        /// <summary>A wall stopped the caught fighter, or the reel ran out of time. They are free.</summary>
        Snapped,
        /// <summary>The pair was already broken (a hit, a death, a reset).</summary>
        Lost,
    }

    /// <summary>
    /// Tethers (ADR-0048): a thrower's line holding one fighter. The pair lives in the ordinary interaction fields
    /// with phase <see cref="DefenseInteractionPhase.Reeling"/>, so death, removal, resets and damaging hits end it
    /// through <see cref="InterruptInteraction"/> like a grab. This file opens the pair from a ring contact, steps
    /// the caught fighter (its only position writer while reeled) and checks both sides of every pair.
    /// </summary>
    public partial class ServerSimulation
    {
        // A reel step that gains less than this share of its commanded travel toward her is stuck: a head-on wall.
        private const float SnagProgressRatio = 0.25f;
        // Room kept between the two bodies on arrival, so their pushboxes do not shove the delivered fighter.
        private const float ArrivalGap = 0.05f;
        private const float ArrivalTolerance = 0.05f;

        /// <summary>
        /// Two rings that meet each other's throwers in one batch clash: both contacts drop, so both rings are spent
        /// without a catch and both throwers reel back empty, as reciprocal grabs clash.
        /// </summary>
        private void ClashTetherCatches(List<SpellResolver.HitResult> hits)
        {
            List<int>? rings = null;
            for (int i = 0; i < hits.Count; i++)
                if (ContactAbility(hits[i])?.Tether is { Catches: true }
                    && _states.TryGetValue(hits[i].TargetEntityId, out var target) && target.State != ActionState.Shielding)
                    (rings ??= new List<int>()).Add(i);
            if (rings == null || rings.Count < 2) return;
            var clashed = new SortedSet<int>();
            foreach (int a in rings)
                foreach (int b in rings)
                    if (a < b && hits[a].OwnerEntityId == hits[b].TargetEntityId && hits[a].TargetEntityId == hits[b].OwnerEntityId)
                    {
                        clashed.Add(a);
                        clashed.Add(b);
                    }
            foreach (int index in clashed.Reverse())
                hits.RemoveAt(index);
        }

        /// <summary>
        /// A ring contact on a fighter, after the shield, invincibility and mode-target rules. A fighter already held,
        /// or already hit in this batch, bounces the ring (a miss). Otherwise the catch supersedes their move and any
        /// reaction in progress, pairs both fighters, deals the ring's damage with no launch or hitstop, credits the
        /// hit and starts the reel. Armour does not resist it, as for grabs.
        /// </summary>
        private void CatchOnTether(in SpellResolver.HitResult hit, TetherAbility tether,
            ref CharacterState thrower, ref CharacterState victim, Dictionary<ulong, InputState> inputs, int operationIndex)
        {
            ulong throwerId = hit.OwnerEntityId, victimId = hit.TargetEntityId;
            if (!tether.Catches || thrower.InteractionId != 0 || victim.InteractionId != 0
                || _damagedThisTick.Contains(victimId))
                return;
            RetireAbility(victimId, ref victim, AbilityExit.Superseded);
            ClearReaction(ref victim);
            unchecked
            {
                _nextInteractionId++;
                if (_nextInteractionId == 0)
                    _nextInteractionId = 1;
            }
            thrower.InteractionId = victim.InteractionId = _nextInteractionId;
            thrower.InteractionPartnerId = victimId;
            victim.InteractionPartnerId = throwerId;
            thrower.InteractionPhase = victim.InteractionPhase = (byte)DefenseInteractionPhase.Reeling;
            thrower.InteractionTick = victim.InteractionTick = _tick;
            victim.State = ActionState.Reeled;
            victim.StateTicks = 0;
            victim.FacingYaw = victim.AimYaw = MathF.Atan2(thrower.PX - victim.PX, thrower.PZ - victim.PZ);

            float damage = IsMayhemShove(throwerId, victimId) ? 0f : hit.Damage;
            victim.DamagePercent = (ushort)Math.Min(999, victim.DamagePercent + (ushort)damage);
            if (damage > 0f)
            {
                _damagedThisTick.Add(victimId);
                TryLockOnAfterHit(throwerId, victimId, ref thrower, inputs);
                TryLockOnAfterHit(victimId, throwerId, ref victim, inputs);
            }
            if (_mayhem == null) _lastHitCredits[victimId] = (throwerId, _tick, hit.AttackSlot);
            _lastHitContexts[victimId] = (throwerId, _tick, hit.AttackSlot);
            tether.OnCaught(ref thrower, victim.PX, victim.PY, victim.PZ);

            var resolved = hit;
            resolved.MatchTick = _tick;
            resolved.Damage = damage;
            resolved.ImpactForce = 0f;
            resolved.HitstopTicks = 0;
            LastTickHits.Add(resolved);
            EmitHitPresentation(in hit, damage, in thrower, attackerExists: true, operationIndex);
        }

        /// <summary>Ends whatever the caught fighter was going through: a freeze, a stun, a launch queued or flying.</summary>
        private static void ClearReaction(ref CharacterState s)
        {
            Simulation.ClearMovementInterruptionFlags(ref s);
            s.VX = s.VY = s.VZ = 0f;
            s.KVX = s.KVY = s.KVZ = 0f;
            s.HitstopTicks = 0;
            s.HitstunTicks = 0;
            s.HitstunLevel = 0;
            s.BlockStunTicks = 0;
            s.BlockHitstopKind = (byte)DefenseBlockHitstopKind.None;
            s.LandingLagTicks = 0;
            s.ShieldDropTicks = 0;
            s.AirDodgeRecoveryTicks = 0;
            s.WasAirborneDuringKnockback = false;
            s.DIX = s.DIY = 0f;
            s.SdiApplied = false;
            s.QueuedArmorHitstop = false;
            s.QueuedKBDirX = s.QueuedKBDirZ = 0f;
            s.QueuedKBAngle = 0;
            s.QueuedKBBase = s.QueuedKBGrowth = s.QueuedKBDamage = s.QueuedKBForce = 0f;
            s.QueuedKBResolvedForce = false;
            s.QueuedKBZero = false;
            s.QueuedKBStun = 0;
            s.QueuedKBFixedHitstunTicks = 0;
            s.QueuedKBStunGate = 0;
        }

        /// <summary>
        /// One reel step, called by the thrower's tether once per ability tick, so her freeze pauses it.
        /// <paramref name="thrower"/> is her in-flight state. The caught fighter moves toward an anchor in front of
        /// her feet: ground distance and height close separately at the reel speed each, so a fighter below a
        /// ledge lip is lifted over it, and the body mover slides it along walls. A step that gains too little,
        /// or the reel running past its time, snaps the line. <paramref name="ring"/> is where the caught fighter
        /// is when there still was one: where the ring sits.
        /// </summary>
        internal TetherStep ReelTether(ref CharacterState thrower, CookedTetherCapabilityParameters p,
            out (float X, float Y, float Z) ring)
        {
            ring = default;
            ulong victimId = thrower.InteractionPartnerId;
            if (thrower.InteractionId == 0 || thrower.InteractionPhase != (byte)DefenseInteractionPhase.Reeling
                || !_states.TryGetValue(victimId, out var victim) || !_defs.TryGetValue(victimId, out var victimDef)
                || victim.State != ActionState.Reeled || victim.InteractionId != thrower.InteractionId
                || victim.InteractionPartnerId != thrower.EntityId)
                return TetherStep.Lost;
            if (_tick - thrower.InteractionTick > p.MaxReelTicks)
            {
                ring = (victim.PX, victim.PY, victim.PZ);
                InterruptInteraction(ref thrower);
                return TetherStep.Snapped;
            }

            var anchor = TetherAnchor(in thrower, _defs[thrower.EntityId], victimDef, p.AnchorDistance);
            float dx = anchor.X - victim.PX, dy = anchor.Y - victim.PY, dz = anchor.Z - victim.PZ;
            float horizontal = MathF.Sqrt(dx * dx + dz * dz);
            float reach = p.ReelSpeed * Simulation.TickDt;
            float scale = horizontal > reach ? reach / horizontal : 1f;
            float mx = dx * scale, mz = dz * scale;
            // A grounded fighter is dragged along the ground, never pushed into it.
            float my = victim.IsGrounded && dy < 0f ? 0f : Math.Clamp(dy, -reach, reach);
            float commanded = MathF.Sqrt(mx * mx + my * my + mz * mz);
            float startX = victim.PX, startY = victim.PY, startZ = victim.PZ;
            victim.VX = mx / Simulation.TickDt;
            victim.VY = my / Simulation.TickDt;
            victim.VZ = mz / Simulation.TickDt;
            Simulation.MoveDefenseBody(ref victim, victimDef, _arena);
            victim.VX = victim.VY = victim.VZ = 0f;
            // Carried, not jumping: once the line lets go in the air, gravity applies at once (as after a walk-off).
            if (!victim.IsGrounded)
                victim.AirTimeTicks = Math.Max(victim.AirTimeTicks, victimDef.Movement.FloatWindowTicks);
            ring = (victim.PX, victim.PY, victim.PZ);

            float leftX = anchor.X - victim.PX, leftY = anchor.Y - victim.PY, leftZ = anchor.Z - victim.PZ;
            bool there = leftX * leftX + leftZ * leftZ <= ArrivalTolerance * ArrivalTolerance
                && (MathF.Abs(leftY) <= ArrivalTolerance || victim.IsGrounded && leftY < 0f);
            if (there)
            {
                ulong interactionId = thrower.InteractionId;
                victim.StateTicks = p.ArrivalStunTicks;
                victim.FacingYaw = victim.AimYaw = MathF.Atan2(thrower.PX - victim.PX, thrower.PZ - victim.PZ);
                MarkInteractionTerminal(ref victim, interactionId);
                MarkInteractionTerminal(ref thrower, interactionId);
                _states[victimId] = victim;
                return TetherStep.Arrived;
            }
            _states[victimId] = victim;
            float progress = commanded > 0f
                ? ((victim.PX - startX) * mx + (victim.PY - startY) * my + (victim.PZ - startZ) * mz) / commanded
                : 0f;
            if (progress < SnagProgressRatio * commanded)
            {
                InterruptInteraction(ref thrower);
                return TetherStep.Snapped;
            }
            return TetherStep.Holding;
        }

        /// <summary>
        /// Where a reeled fighter is delivered: in front of the thrower along her facing, feet level with hers, at
        /// the anchor distance or just outside body contact, whichever is farther.
        /// </summary>
        private static (float X, float Y, float Z) TetherAnchor(in CharacterState thrower, CharacterDefinition throwerDef,
            CharacterDefinition victimDef, float anchorDistance)
        {
            float distance = MathF.Max(anchorDistance, throwerDef.CapsuleRadius + victimDef.CapsuleRadius + ArrivalGap);
            return (thrower.PX + MathF.Sin(thrower.FacingYaw) * distance,
                thrower.PY - throwerDef.CapsuleHeight * 0.5f + victimDef.CapsuleHeight * 0.5f,
                thrower.PZ + MathF.Cos(thrower.FacingYaw) * distance);
        }

        /// <summary>
        /// Whether a Reeling pair still holds, seen from either side: the thrower is Attacking in a tether that is
        /// reeling, the caught fighter is Reeled, both name each other on the same interaction, and neither has a
        /// hit reaction pending (the catch cleared the victim's, and she makes none of her own). Anything else is
        /// interrupted by the pair pass, so a hit of any damage, or a path that forgot to end the line, converges
        /// on the next pass.
        /// </summary>
        private bool TetherHolds(ulong id, in CharacterState state)
        {
            bool isVictim = state.State == ActionState.Reeled;
            ulong throwerId = isVictim ? state.InteractionPartnerId : id;
            ulong victimId = isVictim ? id : state.InteractionPartnerId;
            return _states.TryGetValue(throwerId, out var thrower) && _states.TryGetValue(victimId, out var victim)
                && thrower.State == ActionState.Attacking && victim.State == ActionState.Reeled
                && thrower.InteractionId == state.InteractionId && victim.InteractionId == state.InteractionId
                && thrower.InteractionPartnerId == victimId && victim.InteractionPartnerId == throwerId
                && thrower.InteractionPhase == (byte)DefenseInteractionPhase.Reeling
                && victim.InteractionPhase == (byte)DefenseInteractionPhase.Reeling
                && !IsEliminated(thrower) && !IsEliminated(victim)
                && Unhit(in thrower) && Unhit(in victim)
                && _activeAbilities.TryGetValue(throwerId, out var ability) && ability.Tether is { Reeling: true };
        }

        private static bool Unhit(in CharacterState s)
            => s.HitstopTicks == 0 && s.HitstunTicks == 0 && !Simulation.HasKnockback(s);
    }
}
