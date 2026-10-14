using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    public partial class ServerSimulation
    {
        private readonly ArcadeBallSimulation? _arcadeBall;
        private readonly SlopballSimulation? _slopball;
        /// <summary>Local input alias only; the ball retains a distinct ulong collision identity.</summary>
        public const byte ArcadeBallTargetInputId = byte.MaxValue;
        public ArcadeBallState? ArcadeBall => _arcadeBall?.State;
        public SlopballState? Slopball => _slopball?.State;
        public uint SimulationTick => _tick;
        public IReadOnlyList<SlopballEvent> LastTickSlopballEvents => _slopball?.Events ?? Array.Empty<SlopballEvent>();

        public void StopSlopball() => _slopball?.Stop(_arcadeBall!);

        private bool _slopballInputReleaseRequired;

        /// <summary>Admit a clean solo attempt; countdown ticks do not advance fighters or cooldowns.</summary>
        public void BeginSlopballAttempt(ushort countdownTicks)
        {
            if (_slopball == null || _tick != 0 || _states.Count != 1)
                throw new InvalidOperationException("Slopball must begin from a clean single-fighter simulation.");
            _slopball.BeginCountdown(_arcadeBall!, countdownTicks);
            _slopballInputReleaseRequired = true;
        }

        private Dictionary<ulong, InputState> GateSlopballStartInput(Dictionary<ulong, InputState> inputs)
        {
            if (!_slopballInputReleaseRequired) return inputs;
            foreach (var pair in inputs)
            {
                var input = pair.Value;
                if (input.MoveX != 0f || input.MoveY != 0f || input.Up || input.Down || input.Left || input.Right
                    || input.DownPressed || input.Jump || input.JumpHeld || input.Dash || input.Burst
                    || input.ShieldHeld || input.ShieldPressed || input.GrabPressed || input.ActiveSlot != 0
                    || input.IsAiming || input.FaceToCamera || input.ToggleLock || input.RetargetPressed)
                {
                    var neutral = new Dictionary<ulong, InputState>();
                    foreach (var state in _states)
                    {
                        short yaw = (short)MathF.Round(state.Value.FacingYaw * (18000f / MathF.PI));
                        neutral[state.Key] = new InputState { FacingYaw = yaw, AimYaw = yaw };
                    }
                    return neutral;
                }
            }
            _slopballInputReleaseRequired = false;
            return inputs;
        }

        /// <summary>Fresh ball identity/percent; normal fighter and projectile state continues.</summary>
        public void ResetArcadeBall()
        {
            if (_arcadeBall == null) throw new InvalidOperationException("This arena has no Arcade ball.");
            if (_slopball != null) throw new InvalidOperationException("Retry Slopball with a clean simulation; a debug ball reset is not a challenge retry.");
            _arcadeBall.Reset();
        }

        public bool TryGetCombatTargetPosition(ulong id, out ArcadePoint position)
        {
            if (_states.TryGetValue(id, out var state) && _defs.ContainsKey(id) && !_rule.IsEliminated(state))
            {
                position = new(state.PX, state.PY, state.PZ);
                return true;
            }
            if (_arcadeBall != null && _arcadeBall.State.Active && _arcadeBall.State.EntityId == id)
            {
                position = _arcadeBall.State.Position;
                return true;
            }
            position = default;
            return false;
        }

        private ulong ResolveObjectTargetAlias(byte id)
            => _arcadeBall != null && id == ArcadeBallTargetInputId
                ? (_arcadeBall.State.Active ? _arcadeBall.State.EntityId : 0) : id;

        private IEnumerable<ulong> CombatTargetIds()
        {
            foreach (ulong id in _states.Keys) yield return id;
            if (_arcadeBall != null && _arcadeBall.State.Active) yield return _arcadeBall.State.EntityId;
        }

        private byte CombatTargetDeaths(ulong id) => _states.TryGetValue(id, out var state) ? state.Deaths : (byte)0;
        private float CombatTargetAimY(ulong id, float y)
            => _defs.TryGetValue(id, out var def) ? y + def.CapsuleHeight * .25f : y;

        private void ResolveArcadeBallHit(in SpellResolver.HitResult hit,
            Dictionary<ulong, InputState> inputs, int operationIndex)
        {
            var ball = _arcadeBall!;
            if (_slopball != null && !_slopball.BeforeAttack(ball, hit.ContactTime, _states.ContainsKey(hit.OwnerEntityId))) return;
            float damage = hit.Damage;
            float afterPercent = Math.Clamp(ball.State.DamagePercent + MathF.Max(0f, damage), 0f, 999f);
            float forceDefault = hit.BaseKnockback + hit.KnockbackGrowth * (afterPercent * .01f);
            float force = forceDefault;
            bool ownerExists = _states.TryGetValue(hit.OwnerEntityId, out var owner);
            AbilitySpec? spec = null;
            if (ownerExists && _defs.TryGetValue(hit.OwnerEntityId, out var def))
            {
                if (hit.AttackSlot > 0) spec = def.GetSlotAbility(hit.AttackSlot - 1, hit.Airborne);
                if (_activeAbilities.TryGetValue(hit.OwnerEntityId, out var ability))
                    ability.OnHitObject(ref owner, def, ref damage, ref force);
            }
            if (force == forceDefault)
            {
                afterPercent = Math.Clamp(ball.State.DamagePercent + MathF.Max(0f, damage), 0f, 999f);
                force = (hit.BaseKnockback + hit.KnockbackGrowth * (afterPercent * .01f + 1f)
                    + damage * .1f) * CombatTuning.KnockbackScale;
            }
            var contact = ball.PositionAt(hit.ContactTime);
            float dirX = hit.DirX, dirZ = hit.DirZ;
            if (ownerExists)
            {
                float dx = contact.X - owner.PX, dz = contact.Z - owner.PZ;
                float length = MathF.Sqrt(dx * dx + dz * dz);
                dirX = length > .001f ? dx / length : MathF.Sin(owner.FacingYaw);
                dirZ = length > .001f ? dz / length : MathF.Cos(owner.FacingYaw);
            }
            if (hit.KnockbackDirection == AuthoringKnockbackDirection.TowardOwner)
            { dirX = -dirX; dirZ = -dirZ; }
            ushort freeze = ComputeHitstopTicks(damage, spec);
            float launch = ball.Hit(hit, damage, force, dirX, dirZ, freeze);
            if (ownerExists)
            {
                if (hit.FreezesOwner) owner.HitstopTicks = freeze;
                if (damage > 0f) TryLockOnAfterHit(hit.OwnerEntityId, hit.TargetEntityId, ref owner, inputs);
                _states[hit.OwnerEntityId] = owner;
            }
            var accepted = hit;
            accepted.MatchTick = _tick; accepted.Damage = damage;
            accepted.DirX = dirX; accepted.DirZ = dirZ;
            accepted.ImpactForce = launch; accepted.HitstopTicks = freeze;
            LastTickHits.Add(accepted);
            if (!string.IsNullOrEmpty(spec?.HitPresentationId))
            {
                float scale = Math.Clamp(MathF.Sqrt(MathF.Max(0f, damage) / 6f), .6f, 1.6f);
                _presentationEvents.Add(new TimelinePresentationEvent(
                    _tick, hit.OwnerEntityId, operationIndex, spec!.HitPresentationId!, hit.AttackSequence,
                    PresentationEventSource.HitContact, hit.HitX, hit.HitY, hit.HitZ,
                    ownerExists ? owner.FacingYaw : 0f)
                {
                    Placement = new PresentationPlacement(LocalScaleX: scale, LocalScaleY: scale,
                        LocalScaleZ: scale, DurationTicks: 150),
                });
            }
        }

        private void ConstrainSlopballFighters()
        {
            if (_slopball == null) return;
            var rules = _arena.Arcade!.Slopball!;
            var ids = new ulong[_states.Count]; _states.Keys.CopyTo(ids, 0);
            foreach (ulong id in ids)
            {
                var state = _states[id];
                float limit = rules.NetZ - rules.NetHalfThickness - _defs[id].CapsuleRadius;
                if (state.PZ <= limit) continue;
                state.PZ = limit;
                if (state.VZ > 0f) state.VZ = 0f;
                if (state.KVZ > 0f) state.KVZ = 0f;
                _states[id] = state;
            }
        }
    }
}
