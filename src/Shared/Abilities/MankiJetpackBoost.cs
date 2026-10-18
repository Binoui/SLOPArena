
namespace SlopArena.Shared.Abilities;

/// <summary>
/// Manki's E recovery: a short vulnerable compression followed by one
/// owner-centered ignition hitbox and an unsteerable vertical launch.
/// </summary>
public sealed class MankiJetpackBoost : ServerAbility
{
    public override bool OwnsVerticalMotion => true;

    private readonly CookedMankiJetpackBoostCapabilityParameters _parameters;
    private ushort _elapsedTicks;
    private bool _ignited;

    public MankiJetpackBoost(CookedMankiJetpackBoostCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;

        _elapsedTicks = 0;
        _ignited = false;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.VX = 0f;
        s.VZ = 0f;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (!_ignited)
        {
            _elapsedTicks++;
            if (_elapsedTicks < _parameters.StartupTicks)
                return;

            _ignited = true;
            Launch(ref s, input, def);
            return;
        }

        // Gravity runs before TickAbilities. End on the first post-apex tick;
        // normal drift/actions resume on the following simulation tick.
        if (s.VY <= 0f)
        {
            s.AnimLockTicks = 0;
            EndAbility(ref s);
        }
    }

    private void Launch(ref CharacterState s, InputState input, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        float moveMagnitude = MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY);
        float moveX = 0f;
        float moveY = 0f;
        if (moveMagnitude > 0.001f)
        {
            moveX = input.MoveX / moveMagnitude;
            moveY = input.MoveY / moveMagnitude;
        }

        s.VX = moveX * _parameters.HorizontalSpeed;
        s.VY = _parameters.VerticalSpeed;
        s.VZ = moveY * _parameters.HorizontalSpeed;
        s.IsGrounded = false;
        s.AirTimeTicks = def.Movement.FloatWindowTicks;

        SpawnHitbox(ref s, new HitboxEvent
        {
            TriggerTick = 0,
            DurationTicks = _parameters.ExplosionDurationTicks,
            Shape = HitboxShape.Sphere,
            Radius = _parameters.ExplosionRadius,
            OffX = 0f,
            OffY = -def.CapsuleHeight * 0.5f,
            OffZ = 0f,
            EndOffX = 0f,
            EndOffY = 0f,
            EndOffZ = 0f,
            BoneName = null,
            EndBoneName = null,
            Damage = _parameters.ExplosionDamage,
            Knockback = new KnockbackData
            {
                Profile = KnockbackProfile.Custom,
                Angle = (sbyte)_parameters.ExplosionKbAngle,
                BaseKnockback = _parameters.ExplosionKbBase,
                KnockbackGrowth = _parameters.ExplosionKbGrowth,
            },
            StunTicks = _parameters.ExplosionStunTicks,
            Interruptible = true,
            HitGroup = 0,
        });
            PresentationSink?.Invoke(new TimelinePresentationEvent(
                0, s.EntityId, PresentationOperationIndex, _parameters.ExplosionPresentationId,
                PresentationAttackSequence, PresentationEventSource.CapabilityExplosion,
                s.PX, s.PY - def.CapsuleHeight * 0.5f, s.PZ, s.FacingYaw)
            {
                Placement = new PresentationPlacement(DurationTicks: 150),
            });
    }

    public override void OnEnd(ref CharacterState s)
    {
        s.IsAiming = false;
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_elapsedTicks);
        w.WriteBool(_ignited);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _elapsedTicks = r.ReadUShort();
        _ignited = r.ReadBool();
    }
}
