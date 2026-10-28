using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Hold to aim, then lob a can. The resolver owns its landing pop, fuse and fire reactions.
    /// </summary>
    public sealed class MankiRoundBomb : ServerAbility, IAimHoldCapability
    {
        /// <summary>Release height above the body centre. The throw lands at foot height at the aimed distance.</summary>
        public const float LaunchOffsetY = 1.2f;
        private readonly CookedMankiRoundBombCapabilityParameters _parameters;
        private bool _fired;
        private float _cachedAimDistance;
        private float _cachedAimYaw;

        public MankiRoundBomb(CookedMankiRoundBombCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _fired = false;
            _cachedAimDistance = 0f;
            _cachedAimYaw = 0f;
            s.State = ActionState.Aiming;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            s.AnimLockTicks = 8;
            s.ChargeTicks = 0;

            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiQ] OnStart slot={Slot} animLock={s.AnimLockTicks} airborne={!s.IsGrounded}");
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            if (s.ComboStage == 0)
            {
                if (input.IsAiming)
                {
                    // Release input can omit distance and yaw; keep the last held cursor.
                    _cachedAimDistance = s.AimTargetDistance;
                    _cachedAimYaw = s.AimYaw;
                }
                if (s.AttackElapsedTicks > 8 && !input.IsAiming)
                {
                    s.State = ActionState.Attacking;
                    s.ComboStage = 1;
                    AnimIndex = 1;
                    s.AttackElapsedTicks = 0;
                }
                return;
            }

            if (!_fired && s.AttackElapsedTicks >= _parameters.ThrowTriggerTick)
            {
                _fired = true;
                s.IsAiming = false;
                SpawnBomb(ref s, def);
            }
            if (s.AttackElapsedTicks >= 60)
                EndAbility(ref s);
        }

        private void SpawnBomb(ref CharacterState s, CharacterDefinition def)
        {
            if (Simulation.OnDebugLog != null)
                Simulation.OnDebugLog.Invoke(
                    $"[MankiQ] Projectile spawned! dist={_cachedAimDistance:F2} yaw={_cachedAimYaw:F2}rad");
            float D = Math.Clamp(_cachedAimDistance, 0.5f, _parameters.MaxRange);
            float g = _parameters.Gravity;
            const float launchOffsetY = LaunchOffsetY;
            float dY = -def.CapsuleHeight * 0.5f - launchOffsetY;
            float launchRad = _parameters.LaunchAngle * (MathF.PI / 180f);
            CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY, out float _, out float hSpeed, out float vSpeed);
            float aimCos = MathF.Cos(_cachedAimYaw);
            float aimSin = MathF.Sin(_cachedAimYaw);
            var blast = new ProjectileExplosion
            {
                Radius = _parameters.ExplosionRadius, Damage = _parameters.ExplosionDamage,
                Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)_parameters.ExplosionKbAngle,
                    BaseKnockback = _parameters.ExplosionKbBase, KnockbackGrowth = _parameters.ExplosionKbGrowth },
                StunTicks = _parameters.ExplosionStunTicks, DurationTicks = _parameters.ExplosionDurationTicks,
                CanHitOwner = true, OwnerDamage = 0f, IgnitesOwnerCans = true,
                ExplosionPresentationId = _parameters.ExplosionPresentationId,
                PresentationAttackSequence = PresentationAttackSequence,
                PresentationOperationIndex = PresentationOperationIndex,
                PresentationYaw = s.FacingYaw,
            };
            var earlyBlast = blast;
            earlyBlast.Damage = _parameters.EarlyDamage;
            earlyBlast.Radius = _parameters.EarlyRadius;
            earlyBlast.Knockback.BaseKnockback *= 1.35f;
            var impact = blast;
            impact.Damage = _parameters.Damage;
            impact.Radius = _parameters.ImpactRadius;
            impact.StunTicks = _parameters.StunTicks;
            impact.Knockback.BaseKnockback = 2f;
            impact.Knockback.KnockbackGrowth = 12f;
            impact.Knockback.Angle = (sbyte)_parameters.KbAngle;
            impact.CanHitOwner = false;
            impact.ExplosionPresentationId = Move?.HitPresentationId ?? "";
            SpawnResolverHitbox(new Hitbox
            {
                X = s.PX, Y = s.PY + launchOffsetY, Z = s.PZ,
                VX = hSpeed * aimSin, VY = vSpeed, VZ = hSpeed * aimCos,
                Radius = _parameters.HitboxRadius, Shape = HitboxShape.Sphere,
                EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                Damage = _parameters.Damage, BaseKnockback = 4f, KnockbackGrowth = 6f,
                KnockbackAngle = (sbyte)_parameters.KbAngle, DurationTicks = _parameters.MaxFlightTicks,
                OwnerId = s.EntityId, AttackSlot = (byte)(Slot + 1), Gravity = g,
                Explosion = impact,
                MankiCan = new(_parameters.FuseTicks, _parameters.WarningTicks, blast, earlyBlast),
            });
        }

        internal override void WriteSnapshot(SnapshotWriter w)
        {
            base.WriteSnapshot(w);
            w.WriteBool(_fired);
            w.WriteFloat(_cachedAimDistance);
            w.WriteFloat(_cachedAimYaw);
        }

        internal override void ReadSnapshot(SnapshotReader r)
        {
            base.ReadSnapshot(r);
            _fired = r.ReadBool();
            _cachedAimDistance = r.ReadFloat();
            _cachedAimYaw = r.ReadFloat();
        }
    }
}
