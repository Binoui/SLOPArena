using System;

namespace SlopArena.Shared.Abilities;

public sealed class MankiJetpackBoost : ServerAbility
{
    public override bool OwnsVerticalMotion => true;
    private readonly CookedMankiJetpackBoostCapabilityParameters _parameters;
    private ushort _elapsedTicks;

    public MankiJetpackBoost(CookedMankiJetpackBoostCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.VX = s.VZ = 0f;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (++_elapsedTicks < _parameters.StartupTicks) return;
        SpawnHitbox(ref s, new HitboxEvent
        {
            DurationTicks = _parameters.ExplosionDurationTicks,
            Shape = HitboxShape.Sphere, Radius = _parameters.ExplosionRadius,
            OffY = -def.CapsuleHeight * .5f,
            Damage = _parameters.ExplosionDamage,
            Knockback = new()
            {
                Profile = KnockbackProfile.Custom, Angle = (sbyte)_parameters.ExplosionKbAngle,
                BaseKnockback = _parameters.ExplosionKbBase, KnockbackGrowth = _parameters.ExplosionKbGrowth,
            },
            StunTicks = _parameters.ExplosionStunTicks, Interruptible = true,
        });
        PresentationSink?.Invoke(new TimelinePresentationEvent(
            0, s.EntityId, PresentationOperationIndex, _parameters.ExplosionPresentationId,
            PresentationAttackSequence, PresentationEventSource.CapabilityExplosion,
            s.PX, s.PY - def.CapsuleHeight * .5f, s.PZ, s.FacingYaw)
        { Placement = new PresentationPlacement(DurationTicks: 150) });
        EndAbility(ref s);
        Flying.Enter(ref s, _parameters, def);
        if (input.MoveX * input.MoveX + input.MoveY * input.MoveY > .01f)
            s.Flight.HeadingYaw = MathF.Atan2(input.MoveX, input.MoveY);
        Flying.ApplyMotion(ref s, _parameters);
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_elapsedTicks);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _elapsedTicks = r.ReadUShort();
    }
}
