using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    /// <summary>
    /// The shell's server hooks (ADR-0045, ADR-0050): the splash of a tucked landing, the ride on her wave and the
    /// wave's catch and carry. The rules themselves live in <see cref="Shell"/>.
    /// </summary>
    public partial class ServerSimulation
    {
        /// <summary>
        /// A tuck move that touches down ends there (its cooldown starts, its thrust and trail go) and the shell
        /// splashes: she pops up in place and a hitbox at her feet resolves in this tick's hit pass. Returns true
        /// when it did.
        /// </summary>
        private bool ResolveShellTouchdown(ulong id, ref CharacterState state, CharacterDefinition def,
            bool wasGrounded, ServerAbility? activeAbility)
        {
            if (wasGrounded || !state.IsGrounded || state.VY > 0f || def.Shell?.Splash is not { } splash
                || state.State != ActionState.Attacking || activeAbility?.Move?.ShellPolicy != ShellPolicy.Tuck)
                return false;
            var move = activeAbility;
            RetireAbility(id, ref state, AbilityExit.Cancelled);
            Shell.Splash(ref state, splash);
            SpawnSplash(id, in state, def, splash, move);
            return true;
        }

        /// <summary>
        /// The splash hitbox: still, at her feet, with the retired move's slot, activation and attack sequence so
        /// credit and cues match the move. It is not cancelled with the move: it outlives it by design.
        /// </summary>
        private void SpawnSplash(ulong id, in CharacterState state, CharacterDefinition def, CookedShellSplash splash,
            ServerAbility move)
        {
            var evt = CookedCharacterRuntimeAdapter.ToHitboxEvent(splash.Hitbox) with { HitGroup = 0 };
            HitboxGeometry.ResolvePositions(state, evt, null, def, null, 0,
                out float x, out float y, out float z, out float endX, out float endY, out float endZ);
            var (angle, baseKnockback, growth) = evt.Knockback.Resolve();
            _spellResolver.Spawn(new Hitbox
            {
                X = x, Y = y, Z = z, EndX = endX, EndY = endY, EndZ = endZ,
                Radius = evt.Radius,
                Shape = evt.Shape,
                Damage = evt.Damage,
                BaseKnockback = baseKnockback,
                KnockbackGrowth = growth,
                KnockbackAngle = angle,
                KnockbackDirection = evt.KnockbackDirection,
                StunTicks = evt.StunTicks,
                FixedHitstunTicks = evt.FixedHitstunTicks,
                DurationTicks = evt.DurationTicks,
                OwnerId = id,
                ActivationId = move.ActivationId,
                ActivationAirborne = move.AirborneAtStart,
                AttackSequence = move.PresentationAttackSequence,
                FreezesOwner = true,
                HitsMultipleOpponents = true,
                HitEntities = new HashSet<ulong>(),
                SourceEvent = evt,
                Def = def,
                Slot = move.Slot,
                AttackSlot = (byte)(move.Slot + 1),
                Airborne = move.AirborneAtStart,
            });
            if (!string.IsNullOrEmpty(splash.PresentationId))
                _presentationEvents.Add(new TimelinePresentationEvent(_tick, id, move.PresentationOperationIndex,
                    splash.PresentationId!, move.PresentationAttackSequence, PresentationEventSource.CapabilityLanding,
                    state.PX, state.PY - def.CapsuleHeight * 0.5f, state.PZ, state.FacingYaw)
                {
                    Placement = new PresentationPlacement(DurationTicks: 60),
                });
        }

        /// <summary>
        /// A ride move that completed puts her on the ride, heading for her current target's ground point, else along
        /// her facing.
        /// </summary>
        private void EnterRide(ref CharacterState state, CookedShellRide ride)
        {
            float heading = state.FacingYaw;
            if (TryGetCurrentTarget(in state, out _, out var target))
            {
                float dx = target.X - state.PX, dz = target.Z - state.PZ;
                if (dx * dx + dz * dz > 0.000001f) heading = MathF.Atan2(dx, dz);
            }
            Shell.Ride(ref state, ride, heading);
        }

        /// <summary>
        /// Makes each fighter's wave match its state: present exactly while she rides, with a fresh window and hit set
        /// per ride. A grounded ride turns toward her current target, then the wave carries everyone it caught. Runs
        /// once per tick before hurtboxes, in entity order, so the windows' activation ids stay deterministic.
        /// </summary>
        private void SyncShellWindows()
        {
            foreach (ulong id in SortedEntityIds())
            {
                var state = _states[id];
                var def = Def(id);
                bool want = state.State == ActionState.Shell && def.Shell?.Ride != null && !IsEliminated(state);
                bool has = _spellResolver.HasOwnerWindow(id);
                if (has && (!want || _lastTickAcceptedActions.Contains(id)))
                {
                    _spellResolver.RemoveOwnerWindow(id);
                    has = false;
                }
                if (!want) continue;
                var ride = def.Shell!.Ride!;
                if (state.IsGrounded && !_hitstopFrozenThisTick.Contains(id))
                    SteerRide(ref state, ride);
                _states[id] = state;
                if (!has) _spellResolver.Spawn(WaveWindow(id, in state, def));
                foreach (ulong victimId in _spellResolver.OwnerWindowHits(id))
                {
                    // A fighter a third party hit is no longer Swept: it is released with that hit's own reaction.
                    if (!_states.TryGetValue(victimId, out var victim) || victim.State != ActionState.Swept
                        || victim.HitstopTicks > 0 || _hitstopFrozenThisTick.Contains(victimId))
                        continue;
                    Shell.Carry(ref victim, in state);
                    _states[victimId] = victim;
                }
            }
        }

        /// <summary>Turns the ride toward her current target, unless that target is already held.</summary>
        private void SteerRide(ref CharacterState state, CookedShellRide ride)
        {
            if (ride.TurnDegreesPerSecond <= 0f || !TryGetCurrentTarget(in state, out ulong targetId, out var target)
                || _states.TryGetValue(targetId, out var held) && ActionStates.Captive(held.State))
                return;
            float dx = target.X - state.PX, dz = target.Z - state.PZ;
            if (dx * dx + dz * dz <= 0.000001f) return;
            Shell.Steer(ref state, MathF.Atan2(dx, dz), ride.TurnDegreesPerSecond * (MathF.PI / 180f) * Simulation.TickDt);
        }

        /// <summary>The wave: it follows her, is unblockable (water carries shields) and never freezes her.</summary>
        private Hitbox WaveWindow(ulong id, in CharacterState owner, CharacterDefinition def)
        {
            var evt = CookedCharacterRuntimeAdapter.ToHitboxEvent(def.Shell!.Ride!.Wave) with
            {
                DurationTicks = ushort.MaxValue,
                HitGroup = 0,
            };
            HitboxGeometry.ResolvePositions(owner, evt, null, def, null, 0,
                out float x, out float y, out float z, out float endX, out float endY, out float endZ);
            var (angle, baseKnockback, growth) = evt.Knockback.Resolve();
            unchecked
            {
                _nextActivationId++;
                if (_nextActivationId == 0) _nextActivationId = 1;
            }
            return new Hitbox
            {
                X = x, Y = y, Z = z, EndX = endX, EndY = endY, EndZ = endZ,
                Radius = evt.Radius,
                Shape = evt.Shape,
                Damage = evt.Damage,
                BaseKnockback = baseKnockback,
                KnockbackGrowth = growth,
                KnockbackAngle = angle,
                KnockbackDirection = evt.KnockbackDirection,
                StunTicks = evt.StunTicks,
                FixedHitstunTicks = evt.FixedHitstunTicks,
                DurationTicks = ushort.MaxValue,
                OwnerId = id,
                ActivationId = _nextActivationId,
                ActivationAirborne = !owner.IsGrounded,
                AttackSequence = owner.AttackSequence,
                Unblockable = true,
                HitsMultipleOpponents = true,
                HitEntities = new HashSet<ulong>(),
                TracksBone = true,
                SourceEvent = evt,
                Def = def,
                OwnerWindow = true,
            };
        }

        /// <summary>
        /// A wave contact on a fighter carries instead of hitting. Refused, and forgotten so the wave can still catch
        /// them once free: a fighter in a pair (on a line, reeling one, grabbed or grabbing), one already Swept, or
        /// one another hit damaged this batch. Otherwise their move and reaction end, they become Swept with her
        /// speed, take the wave's damage once and the hit is credited to her. Armour does not resist water.
        /// </summary>
        private void CatchOnWave(in SpellResolver.HitResult hit, ref CharacterState rider, ref CharacterState victim,
            Dictionary<ulong, InputState> inputs, int operationIndex)
        {
            ulong riderId = hit.OwnerEntityId, victimId = hit.TargetEntityId;
            if (victim.InteractionId != 0 || victim.State is ActionState.Swept or ActionState.CutHeld
                || _damagedThisTick.Contains(victimId))
            {
                _spellResolver.ForgetOwnerWindowContact(riderId, victimId);
                return;
            }
            RetireAbility(victimId, ref victim, AbilityExit.Superseded);
            Flying.End(ref victim, Def(victimId), victimId != NoCooldownsEntityId);
            ClearReaction(ref victim);
            victim.IsFastFalling = false;
            victim.State = ActionState.Swept;
            victim.FacingYaw = victim.AimYaw = MathF.Atan2(rider.PX - victim.PX, rider.PZ - victim.PZ);
            Shell.Carry(ref victim, in rider);
            // One writer per carried fighter: no other wave keeps it.
            _spellResolver.ForgetOwnerWindowContacts(victimId, exceptOwnerId: riderId);

            float damage = IsMayhemShove(riderId, victimId) ? 0f : hit.Damage;
            victim.DamagePercent = (ushort)Math.Min(999, victim.DamagePercent + (ushort)damage);
            if (damage > 0f)
            {
                _damagedThisTick.Add(victimId);
                TryLockOnAfterHit(riderId, victimId, ref rider, inputs);
                TryLockOnAfterHit(victimId, riderId, ref victim, inputs);
            }
            if (_mayhem == null) _lastHitCredits[victimId] = (riderId, _tick, hit.AttackSlot);
            _lastHitContexts[victimId] = (riderId, _tick, hit.AttackSlot);

            var resolved = hit;
            resolved.MatchTick = _tick;
            resolved.Damage = damage;
            resolved.ImpactForce = 0f;
            resolved.HitstopTicks = 0;
            LastTickHits.Add(resolved);
            EmitHitPresentation(in hit, damage, in rider, attackerExists: true, operationIndex);
        }

        /// <summary>A grounded ride that a runtime wall or the Slopball net stopped head-on pops up.</summary>
        private void StopShellAtRuntimeWall(ulong id, in CharacterState before, ref CharacterState state)
        {
            if (state.State == ActionState.Shell && state.IsGrounded && Def(id).Shell is { } shell
                && Shell.LostForwardSpeed(state.FacingYaw, before.VX, before.VZ, state.VX, state.VZ))
                Shell.Blocked(ref state, shell);
        }

        /// <summary>Her current target's feet (a fighter's capsule bottom, an object's position).</summary>
        internal bool TryGetCurrentTargetFeet(in CharacterState s, out ArcadePoint feet)
        {
            if (!TryGetCurrentTarget(in s, out ulong targetId, out var position))
            {
                feet = default;
                return false;
            }
            feet = _states.ContainsKey(targetId) && TryDef(targetId, out var def)
                ? new ArcadePoint(position.X, position.Y - def.CapsuleHeight * 0.5f, position.Z)
                : position;
            return true;
        }

        /// <summary>
        /// The yaw toward her current target when it lies inside the cone around her facing. Used by a thrust that
        /// leans toward a target ahead.
        /// </summary>
        internal bool TryGetTargetAhead(in CharacterState s, float coneDegrees, out float yaw)
        {
            yaw = s.FacingYaw;
            if (!TryGetCurrentTarget(in s, out _, out var target))
                return false;
            float dx = target.X - s.PX, dz = target.Z - s.PZ;
            if (dx * dx + dz * dz <= 0.000001f)
                return false;
            float toward = MathF.Atan2(dx, dz);
            if (MathF.Abs(CorrectionAngleDelta(toward - s.FacingYaw)) > coneDegrees * (MathF.PI / 180f))
                return false;
            yaw = toward;
            return true;
        }
    }
}
