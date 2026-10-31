using System;

namespace SlopArena.Shared.Abilities;

public sealed class MankiRoundBomb : ServerAbility
{
    private readonly CookedMankiRoundBombCapabilityParameters _parameters;
    public MankiRoundBomb(CookedMankiRoundBombCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        bool airborne = !s.IsGrounded;
        if (!airborne) s.VX = s.VZ = 0f;
        var blast = new ProjectileExplosion
        {
            Radius = _parameters.ExplosionRadius, Damage = _parameters.ExplosionDamage,
            Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)_parameters.ExplosionKbAngle,
                BaseKnockback = _parameters.ExplosionKbBase, KnockbackGrowth = _parameters.ExplosionKbGrowth },
            StunTicks = _parameters.ExplosionStunTicks, DurationTicks = _parameters.ExplosionDurationTicks,
            ExplosionPresentationId = _parameters.ExplosionPresentationId,
            PresentationAttackSequence = PresentationAttackSequence,
            PresentationOperationIndex = PresentationOperationIndex, PresentationYaw = s.FacingYaw,
        };
        float bombY = s.PY - def.CapsuleHeight * .5f + _parameters.HitboxRadius;
        SpawnResolverHitbox(new Hitbox
        {
            X = s.PX, Y = bombY, Z = s.PZ,
            Radius = _parameters.HitboxRadius, Shape = HitboxShape.Sphere,
            DurationTicks = airborne ? _parameters.MaxFlightTicks : _parameters.LifetimeTicks,
            OwnerId = s.EntityId, AttackSlot = (byte)(Slot + 1),
            Gravity = airborne ? _parameters.Gravity : 0f,
            MinePhase = airborne ? MinePhase.Falling : MinePhase.Arming,
            MineTicksLeft = _parameters.ArmTicks, MineArmTicks = _parameters.ArmTicks,
            MineTriggerRadius = _parameters.TriggerRadius, MineWarningTicks = _parameters.WarningTicks,
            MineLifetimeTicks = _parameters.LifetimeTicks, MineMaxCount = _parameters.MaxMines,
            Explosion = blast, ContactExplosion = true,
        });
        s.State = ActionState.Attacking;
        s.IsAiming = false;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (s.IsGrounded) s.VX = s.VZ = 0f;
    }
}
