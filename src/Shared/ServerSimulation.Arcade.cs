using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    public partial class ServerSimulation
    {
        private readonly ArcadeBallSimulation? _arcadeBall;
        private readonly SlopballSimulation? _slopball;
        private readonly CharacterCourseSimulation? _course;
        /// <summary>Local input alias only; the ball retains a distinct ulong collision identity.</summary>
        public const byte ArcadeBallTargetInputId = byte.MaxValue;
        public ArcadeBallState? ArcadeBall => _arcadeBall?.State;
        public SlopballState? Slopball => _slopball?.State;
        public CharacterCourseState? Course => _course?.State;
        public IReadOnlyList<CharacterCourseEvent> LastTickCourseEvents => _course?.Events ?? Array.Empty<CharacterCourseEvent>();
        public bool IsCourseTargetBroken(int index) => _course?.IsBroken(index) ?? false;
        /// <summary>Hits a course target still needs (0 once broken); a bonus target starts above 1.</summary>
        public int CourseTargetHitsLeft(int index) => _course?.HitsLeft(index) ?? 0;
        public byte CourseTargetInputAlias(int index)
            => _course != null && index >= 0 && index < _arena.Arcade!.Course!.Targets.Count
                ? (byte)(CharacterCourseSimulation.FirstInputAlias + index) : (byte)0;
        public ulong CourseTargetEntityId(int index)
            => CourseTargetInputAlias(index) != 0 ? CharacterCourseSimulation.FirstEntityId + (ulong)index : 0;
        public uint SimulationTick => _tick;
        public IReadOnlyList<SlopballEvent> LastTickSlopballEvents => _slopball?.Events ?? Array.Empty<SlopballEvent>();
        /// <summary>The Arcade ball's bounces during the last tick (walls, net, ceiling, floor and fighter bodies), for presentation.</summary>
        public IReadOnlyList<ArcadeBallContact> LastTickArcadeBallContacts => _arcadeBall?.Contacts ?? Array.Empty<ArcadeBallContact>();

        public void StopSlopball() => _slopball?.Stop(_arcadeBall!);
        public void StopCourse() => _course?.Stop();

        public void BeginCourseAttempt(ushort countdownTicks)
        {
            if (_course == null || _tick != 0 || _states.Count != 1)
                throw new InvalidOperationException("A course must begin from a clean single-fighter simulation.");
            _course.Begin(countdownTicks);
            _arcadeInputReleaseRequired = true;
        }

        private bool _arcadeInputReleaseRequired;

        /// <summary>Admit a clean solo attempt; countdown ticks do not advance fighters or cooldowns.</summary>
        public void BeginSlopballAttempt(ushort countdownTicks, uint? sessionSeed = null)
        {
            if (_slopball == null || _tick != 0 || _states.Count != 1)
                throw new InvalidOperationException("Slopball must begin from a clean single-fighter simulation.");
            _slopball.BeginCountdown(_arcadeBall!, countdownTicks, sessionSeed);
            _arcadeInputReleaseRequired = true;
        }

        private Dictionary<ulong, InputState> GateArcadeStartInput(Dictionary<ulong, InputState> inputs)
        {
            if (!_arcadeInputReleaseRequired) return inputs;
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
            _arcadeInputReleaseRequired = false;
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
            if (_course != null && _course.TryIndex(id, out int index) && !_course.IsBroken(index))
            {
                position = _arena.Arcade!.Course!.Targets[index].Position;
                return true;
            }
            position = default;
            return false;
        }

        private ulong ResolveObjectTargetAlias(byte id)
            => _course != null && id >= CharacterCourseSimulation.FirstInputAlias ? _course.ResolveAlias(id)
                : _arcadeBall != null && id == ArcadeBallTargetInputId
                ? (_arcadeBall.State.Active ? _arcadeBall.State.EntityId : 0) : id;

        private IEnumerable<ulong> CombatTargetIds()
        {
            foreach (ulong id in _states.Keys) yield return id;
            if (_arcadeBall != null && _arcadeBall.State.Active) yield return _arcadeBall.State.EntityId;
            if (_course != null)
                foreach (var target in _course.CollisionTargets()) yield return target.Id;
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
            if (_slopball != null && damage > 0f) _slopball.AssistReturn(ball, launch);
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

        private void ResolveCourseTargetHit(in SpellResolver.HitResult hit, int index,
            Dictionary<ulong, InputState> inputs, int operationIndex)
        {
            if (_course!.IsBroken(index) || hit.Damage <= 0f || !_states.TryGetValue(hit.OwnerEntityId, out var owner)) return;
            var definition = _defs[hit.OwnerEntityId];
            float damage = hit.Damage, force = hit.BaseKnockback;
            if (_activeAbilities.TryGetValue(hit.OwnerEntityId, out var ability))
                ability.OnHitObject(ref owner, definition, ref damage, ref force);
            if (damage <= 0f) { _states[hit.OwnerEntityId] = owner; return; }
            if (!_course.Hit(index)) return;
            var spec = hit.AttackSlot > 0 ? definition.GetSlotAbility(hit.AttackSlot - 1, hit.Airborne) : null;
            ushort freeze = ComputeHitstopTicks(damage, spec);
            if (hit.FreezesOwner) owner.HitstopTicks = freeze;
            // A bonus target that still needs hits keeps the lock; a broken one releases it.
            if (_course.IsBroken(index) && owner.TargetEntityId == hit.TargetEntityId) { owner.TargetEntityId = 0; owner.LockOn = false; }
            _states[hit.OwnerEntityId] = owner;
            var accepted = hit;
            accepted.MatchTick = _tick; accepted.Damage = damage; accepted.HitstopTicks = freeze;
            LastTickHits.Add(accepted);
            if (!string.IsNullOrEmpty(spec?.HitPresentationId))
                _presentationEvents.Add(new TimelinePresentationEvent(_tick, hit.OwnerEntityId, operationIndex,
                    spec!.HitPresentationId!, hit.AttackSequence, PresentationEventSource.HitContact,
                    hit.HitX, hit.HitY, hit.HitZ, owner.FacingYaw)
                { Placement = new PresentationPlacement(DurationTicks: 150) });
        }

        private void FinishCourseTick(ArcadePoint previous)
        {
            if (_course == null || _states.Count != 1) return;
            foreach (var pair in _states)
            {
                var current = pair.Value;
                int saved = _course.State.SavedCheckpoint;
                _course.FinishTick(previous, new(current.PX, current.PY, current.PZ), LastTickDeaths.Count > 0);
                if (_course.State.SavedCheckpoint != saved)
                {
                    var checkpoint = _arena.Arcade!.Course!.Checkpoints[_course.State.SavedCheckpoint];
                    SetRespawnPosition(pair.Key, checkpoint.Respawn.X, checkpoint.Respawn.Y, checkpoint.Respawn.Z, checkpoint.RespawnYaw);
                }
            }
        }
    }
}
