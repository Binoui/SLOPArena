using System;

namespace SlopArena.Shared.Abilities
{
    public sealed class MankiBazooka : ServerAbility
    {
        public override bool OwnsVerticalMotion => true;

        private readonly CookedMankiBazookaCapabilityParameters _parameters;
        private enum BazookaPhase { Charging, Firing, Recovery }
        private BazookaPhase _phase;
        private bool _projectileSpawned;
        private ulong _targetId;
        private float _launchYaw;

        public MankiBazooka(CookedMankiBazookaCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            ClearVelocityOwnership(ref s);
            s.IsFastFalling = false;

            _phase = _parameters.ChargeTicks > 0 ? BazookaPhase.Charging : BazookaPhase.Firing;
            _projectileSpawned = false;
            _targetId = ActivationInput.TargetEntityId != 0 && OwnerSimulation != null
                ? OwnerSimulation.ResolveObjectTargetAlias(ActivationInput.TargetEntityId, s.EntityId) : s.TargetEntityId;
            _launchYaw = s.FacingYaw;
            s.ChargeTicks = 0;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 1;
            s.ComboStage = 1;
            s.AttackElapsedTicks = 0;
            s.IsAiming = _phase == BazookaPhase.Charging;
            s.AnimLockTicks = (ushort)(_parameters.ChargeTicks + _parameters.CastDuration);
        }

        public override void OnEnd(ref CharacterState s)
        {
            ClearVelocityOwnership(ref s);
            s.IsFastFalling = false;
            s.IsAiming = false;
            s.ChargeTicks = 0;
            s.VY = 0f;
            if (s.IsGrounded)
                s.VX = s.VZ = 0f;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            s.IsAiming = _phase == BazookaPhase.Charging;
            if (_parameters.ChargeTicks > 0) s.TargetEntityId = _targetId;
            if (s.IsGrounded) s.VX = s.VZ = 0f;
            switch (_phase)
            {
                case BazookaPhase.Charging:
                    if (OwnerSimulation != null && OwnerSimulation.TryGetProjectileTargetPosition(s.EntityId, _targetId, out var target))
                    {
                        float dx = target.X - s.PX, dz = target.Z - s.PZ;
                        if (dx * dx + dz * dz > .000001f)
                            s.FacingYaw = MathF.Atan2(dx, dz);
                    }
                    else s.FacingYaw = _launchYaw;
                    s.ChargeTicks = (ushort)Math.Min(s.AttackElapsedTicks, _parameters.ChargeTicks);
                    if (s.AttackElapsedTicks >= _parameters.ChargeTicks)
                    {
                        _phase = BazookaPhase.Firing;
                        _projectileSpawned = true;
                        s.IsAiming = false;
                        SpawnRocket(ref s, def);
                    }
                    break;
                case BazookaPhase.Firing:
                    TickFiring(ref s, def);
                    break;
                case BazookaPhase.Recovery:
                    TickRecovery(ref s, def);
                    break;
            }
        }

        private void TickFiring(ref CharacterState s, CharacterDefinition def)
        {
            if (!_projectileSpawned && s.AttackElapsedTicks >= _parameters.FireTriggerTick)
            {
                _projectileSpawned = true;
                SpawnRocket(ref s, def);
            }

            if (s.AttackElapsedTicks >= _parameters.ChargeTicks + _parameters.CastDuration)
            {
                _phase = BazookaPhase.Recovery;
                s.ChargeTicks = 0;
                s.AttackElapsedTicks = 0;
                s.AnimLockTicks = _parameters.RecoveryDuration;
            }
        }

        private void TickRecovery(ref CharacterState s, CharacterDefinition def)
        {
            if (s.AttackElapsedTicks >= _parameters.RecoveryDuration)
            {
                EndAbility(ref s);
            }
        }

        private void SpawnRocket(ref CharacterState s, CharacterDefinition def)
        {
            float speed = _parameters.ProjectileSpeed;
            float yaw = _parameters.ChargeTicks > 0 ? _launchYaw : s.FacingYaw;
            float vx = speed * MathF.Sin(yaw), vy = 0f, vz = speed * MathF.Cos(yaw);
            ulong wantedTarget = _parameters.ChargeTicks > 0 ? _targetId : s.TargetEntityId;
            ulong targetId = 0;
            if (OwnerSimulation != null
                && OwnerSimulation.TryGetProjectileTargetPosition(s.EntityId, wantedTarget, out var target))
            {
                float dx = target.X - s.PX, dy = target.Y - (s.PY + 1f), dz = target.Z - s.PZ;
                float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance > 0.0001f)
                {
                    targetId = wantedTarget;
                    vx = dx / distance * speed;
                    vy = dy / distance * speed;
                    vz = dz / distance * speed;
                }
            }

            SpawnResolverHitbox(new Hitbox
            {
                X = s.PX,
                Y = s.PY + 1.0f,
                Z = s.PZ,
                VX = vx, VY = vy, VZ = vz,
                Radius = _parameters.HitboxRadius,
                Shape = HitboxShape.Sphere,
                EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                Damage = _parameters.Damage,
                BaseKnockback = 6f,
                KnockbackGrowth = 9f,
                KnockbackAngle = (sbyte)_parameters.KbAngle,
                StunTicks = _parameters.StunTicks,
                DurationTicks = _parameters.MaxFlightTicks,
                OwnerId = s.EntityId,
                AttackSlot = (byte)(Slot + 1),
                Gravity = _parameters.Gravity,
                HomingTargetEntityId = targetId,
                HomingTurnCos = _parameters.HomingTurnCos,
                HomingTurnSin = _parameters.HomingTurnSin,
                Explosion = new ProjectileExplosion
                {
                    Radius = _parameters.ExplosionRadius,
                    Damage = 10f,
                    Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)_parameters.ExplosionKbAngle, BaseKnockback = _parameters.ExplosionKbBase, KnockbackGrowth = _parameters.ExplosionKbGrowth },
                    StunTicks = _parameters.ExplosionStunTicks,
                    DurationTicks = _parameters.ExplosionDurationTicks,
                    CanHitOwner = true,
                    OwnerDamage = 4f,
                    ExplosionPresentationId = _parameters.ExplosionPresentationId,
                    PresentationAttackSequence = PresentationAttackSequence,
                    PresentationOperationIndex = PresentationOperationIndex,
                    PresentationYaw = s.FacingYaw,
                },
            });
        }

        internal override void WriteSnapshot(SnapshotWriter w)
        {
            base.WriteSnapshot(w);
            w.WriteByte((byte)_phase);
            w.WriteBool(_projectileSpawned);
            w.WriteULong(_targetId);
            w.WriteFloat(_launchYaw);
        }

        internal override void ReadSnapshot(SnapshotReader r)
        {
            base.ReadSnapshot(r);
            _phase = (BazookaPhase)r.ReadByte();
            _projectileSpawned = r.ReadBool();
            _targetId = r.ReadULong();
            _launchYaw = r.ReadFloat();
        }
    }
}
