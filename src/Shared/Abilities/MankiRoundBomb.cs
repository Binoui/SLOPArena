using System;

namespace SlopArena.Shared.Abilities;

public sealed class MankiRoundBomb : ServerAbility
{
    private readonly CookedMankiRoundBombCapabilityParameters _parameters;
    public MankiRoundBomb(CookedMankiRoundBombCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        bool flying = s.Flight.Active;
        var blast = new ProjectileExplosion
        {
            Radius = _parameters.ExplosionRadius, Damage = _parameters.ExplosionDamage,
            Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)_parameters.ExplosionKbAngle,
                BaseKnockback = _parameters.ExplosionKbBase, KnockbackGrowth = _parameters.ExplosionKbGrowth },
            StunTicks = _parameters.ExplosionStunTicks, DurationTicks = _parameters.ExplosionDurationTicks,
            SelfPushSpeed = _parameters.SelfPushSpeed,
            ExplosionPresentationId = _parameters.ExplosionPresentationId,
            PresentationAttackSequence = PresentationAttackSequence,
            PresentationOperationIndex = PresentationOperationIndex, PresentationYaw = s.FacingYaw,
        };
        float bombY = s.PY - def.CapsuleHeight * .5f + _parameters.HitboxRadius;
        SpawnResolverHitbox(new Hitbox
        {
            X = s.PX, Y = bombY, Z = s.PZ,
            Radius = _parameters.HitboxRadius, Shape = HitboxShape.Sphere,
            DurationTicks = flying ? _parameters.MaxFlightTicks : _parameters.FuseTicks,
            OwnerId = s.EntityId, AttackSlot = (byte)(Slot + 1),
            Gravity = flying ? _parameters.Gravity : 0f,
            Explosion = blast, ContactExplosion = true, ExplodeOnExpiry = !flying,
        });
        if (!flying)
        {
            float yaw = s.FacingYaw;
            if (OwnerSimulation != null && OwnerSimulation.TryGetProjectileTargetPosition(s.EntityId, s.TargetEntityId != 0 ? s.TargetEntityId : ActivationInput.TargetEntityId, out var target))
                yaw = MathF.Atan2(target.X - s.PX, target.Z - s.PZ);
            ClearVelocityOwnership(ref s);
            s.VX = -MathF.Sin(yaw) * _parameters.HopBackSpeed;
            s.VZ = -MathF.Cos(yaw) * _parameters.HopBackSpeed;
            s.VY = _parameters.HopUpSpeed;
            s.IsGrounded = false;
            s.IsFastFalling = false;
        }
        s.State = ActionState.Attacking;
        s.IsAiming = false;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def) { }
}
