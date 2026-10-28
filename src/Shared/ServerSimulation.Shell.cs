using System;
using System.Collections.Generic;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
    /// <summary>The shell stance's server hooks (ADR-0045). The rules themselves live in <see cref="Shell"/>.</summary>
    public partial class ServerSimulation
    {
        /// <summary>
        /// A tuck move that touches down ends there, as an aerial does (its cooldown starts, its thrust and trail
        /// go), and she settles into the skate with the touchdown's downward speed. Returns true when it did.
        /// </summary>
        private bool ResolveShellTouchdown(ulong id, ref CharacterState state, CharacterDefinition def,
            bool wasGrounded, ServerAbility? activeAbility, float touchdownDownSpeed)
        {
            if (wasGrounded || !state.IsGrounded || state.VY > 0f || def.Shell == null
                || state.State != ActionState.Attacking || activeAbility?.Move?.ShellPolicy != ShellPolicy.Tuck)
                return false;
            RetireAbility(id, ref state, AbilityExit.Cancelled);
            Shell.Settle(ref state, def.Shell, touchdownDownSpeed, restartClock: true);
            return true;
        }

        /// <summary>
        /// Makes each fighter's bowling hitbox match its state: present exactly while it is in the shell. Each
        /// stay gets a fresh window and hit set, so an action accepted this tick replaces the window. Runs once
        /// per tick before hurtboxes, in entity order, so the windows' activation ids stay deterministic.
        /// </summary>
        private void SyncShellWindows()
        {
            foreach (ulong id in SortedEntityIds())
            {
                var state = _states[id];
                var def = _defs[id];
                bool want = state.State == ActionState.Shell && def.Shell != null && !IsEliminated(state);
                bool has = _spellResolver.HasOwnerWindow(id);
                if (has && (!want || _lastTickAcceptedActions.Contains(id)))
                {
                    _spellResolver.RemoveOwnerWindow(id);
                    has = false;
                }
                if (want && !has)
                    _spellResolver.Spawn(ShellWindow(id, in state, def));
            }
        }

        private Hitbox ShellWindow(ulong id, in CharacterState owner, CharacterDefinition def)
        {
            var evt = CookedCharacterRuntimeAdapter.ToHitboxEvent(def.Shell!.Hitbox) with
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
                FreezesOwner = true,
                HitsMultipleOpponents = true,
                HitEntities = new HashSet<ulong>(),
                TracksBone = true,
                SourceEvent = evt,
                Def = def,
                OwnerWindow = true,
            };
        }

        /// <summary>A grounded skate that a runtime wall or the Slopball net stopped head-on pops up.</summary>
        private void StopShellAtRuntimeWall(ulong id, in CharacterState before, ref CharacterState state)
        {
            if (state.State == ActionState.Shell && state.IsGrounded && _defs[id].Shell is { } shell
                && Shell.LostForwardSpeed(state.FacingYaw, before.VX, before.VZ, state.VX, state.VZ))
                Shell.Blocked(ref state, shell);
        }

        /// <summary>
        /// The yaw toward her current target (or the nearest enemy in lock range) when it lies inside the cone
        /// around her facing. Used by a thrust that leans toward a target ahead.
        /// </summary>
        internal bool TryGetTargetAhead(in CharacterState s, float coneDegrees, out float yaw)
        {
            yaw = s.FacingYaw;
            ulong targetId = s.TargetEntityId;
            if (targetId == 0 || !IsEligibleEnemy(s.EntityId, targetId, s.PX, s.PZ, LockRangeMeters, out _))
                targetId = FindClosestEnemy(s.EntityId, s.PX, s.PZ, LockRangeMeters, out _);
            if (targetId == 0 || !TryGetCombatTargetPosition(targetId, out var target))
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
