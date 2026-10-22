using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's R (slot 4): Bazooka — hold to aim (camera direction), release to fire.
    /// 
    /// Phases:
    ///   0 (Aiming): Hold key, show loop animation, camera controls aim direction.
    ///     Transition to Firing on release (input.IsAiming == false).
    ///   1 (Firing): Short cast animation, spawn rocket at trigger tick in AimYaw/AimPitch direction.
    ///   2 (Recovery): Endlag, then EndAbility.
    ///
    /// Projectile has gravity + ground collision + explosion with CanHitOwner=true.
    /// Rocket jump: aim at feet → projectile ground-collides near self → explosion.
    /// </summary>
    public sealed class MankiBazooka : ServerAbility, IAimHoldCapability
    {
        public override bool OwnsVerticalMotion => true;

        private readonly CookedMankiBazookaCapabilityParameters _parameters;
        private enum BazookaPhase { Aiming, Firing, Recovery }
        private BazookaPhase _phase;
        private bool _projectileSpawned;

        public MankiBazooka(CookedMankiBazookaCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            ClearVelocityOwnership(ref s);
            s.IsFastFalling = false;

            _phase = BazookaPhase.Aiming;
            _projectileSpawned = false;

            // Fixed aim stops on the ground and coasts with existing momentum in the air.
            s.State = ActionState.Aiming;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;  // spell_r_loop (aim hold)
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            s.AnimLockTicks = 180;
        }

        public override void OnEnd(ref CharacterState s)
        {
            ClearVelocityOwnership(ref s);
            s.IsFastFalling = false;
            s.IsAiming = false;
            s.VY = 0f;
            if (s.IsGrounded)
                s.VX = s.VZ = 0f;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            switch (_phase)
            {
                case BazookaPhase.Aiming:
                    TickAiming(ref s, input, def);
                    break;
                case BazookaPhase.Firing:
                    TickFiring(ref s, def);
                    break;
                case BazookaPhase.Recovery:
                    TickRecovery(ref s, def);
                    break;
            }
        }

        private void TickAiming(ref CharacterState s, InputState input, CharacterDefinition def)
        {
            s.IsAiming = true;
            // Server no longer enforces facing = aim direction — character
            // faces naturally based on movement direction.

            if (!input.IsAiming)
            {
                // Firing is an action phase — re-enter Attacking (hold was Aiming).
                s.State = ActionState.Attacking;
                _phase = BazookaPhase.Firing;
                s.AttackElapsedTicks = 0;
                AnimIndex = 1;      // spell_r_attack
                s.ComboStage = 1;   // renderer switches loop -> attack clip
                s.AnimLockTicks = _parameters.CastDuration;
            }
        }

        private void TickFiring(ref CharacterState s, CharacterDefinition def)
        {
            if (!_projectileSpawned && s.AttackElapsedTicks >= _parameters.FireTriggerTick)
            {
                _projectileSpawned = true;
                SpawnRocket(ref s, def);
            }

            if (s.AttackElapsedTicks >= _parameters.CastDuration)
            {
                _phase = BazookaPhase.Recovery;
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
            float pitch = s.AimPitch;
            float yaw = s.AimYaw;

            float cosPitch = MathF.Cos(pitch);
            float vx = speed * cosPitch * MathF.Sin(yaw);
            float vy = speed * MathF.Sin(pitch);
            float vz = speed * cosPitch * MathF.Cos(yaw);

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiBazooka] Firing! pitch={pitch:F3}({pitch*(180f/MathF.PI):F1}°) yaw={yaw:F3}({yaw*(180f/MathF.PI):F1}°) vy={vy:F2} vz={vz:F2}");

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
                Explosion = new ProjectileExplosion
                {
                    Radius = _parameters.ExplosionRadius,
                    Damage = 10f,
                    Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)_parameters.ExplosionKbAngle, BaseKnockback = _parameters.ExplosionKbBase, KnockbackGrowth = _parameters.ExplosionKbGrowth },
                    StunTicks = _parameters.ExplosionStunTicks,
                    DurationTicks = _parameters.ExplosionDurationTicks,
                    CanHitOwner = true,
                    ExplosionPresentationId = _parameters.ExplosionPresentationId,
                    PresentationAttackSequence = PresentationAttackSequence,
                    PresentationOperationIndex = PresentationOperationIndex,
                    PresentationYaw = s.FacingYaw,
                },
            });
        }

        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef, CharacterDefinition targetDef,
            ref float damage, ref float knockbackForce)
        {
            if (target.EntityId == attacker.EntityId)
            {
                int corrected = target.DamagePercent - (ushort)damage + 4;
                target.DamagePercent = (ushort)Math.Clamp(corrected, 0, 999);
            }
        }

        internal override void WriteSnapshot(SnapshotWriter w)
        {
            base.WriteSnapshot(w);
            w.WriteByte((byte)_phase);
            w.WriteBool(_projectileSpawned);
        }

        internal override void ReadSnapshot(SnapshotReader r)
        {
            base.ReadSnapshot(r);
            _phase = (BazookaPhase)r.ReadByte();
            _projectileSpawned = r.ReadBool();
        }
    }
}
