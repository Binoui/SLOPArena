using System;

namespace SlopArena.Shared.Abilities;

/// <summary>Ignition followed by fuel-limited steering, with jump taps supplying lift.</summary>
public sealed class MankiJetpackBoost : ServerAbility, ILandingContinuationCapability
{
    public override bool OwnsVerticalMotion => true;
    public override float GravityMultiplier => _ignited ? 0f : 1f;

    private readonly CookedMankiJetpackBoostCapabilityParameters _parameters;
    private ushort _elapsedTicks;
    private bool _ignited;
    private ushort _fuelTicksLeft;
    private ushort _burstTicksLeft;
    private float _liftSpeed;
    private float _headingYaw;
    private float _previousX, _previousY, _previousZ;

    public MankiJetpackBoost(CookedMankiJetpackBoostCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        _elapsedTicks = 0;
        _ignited = false;
        _fuelTicksLeft = _parameters.FuelTicks;
        _burstTicksLeft = 0;
        _headingYaw = s.FacingYaw;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.VX = s.VZ = 0f;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (!_ignited)
        {
            if (++_elapsedTicks < _parameters.StartupTicks) return;
            _ignited = true;
            _burstTicksLeft = _parameters.JumpBurstTicks;
            _liftSpeed = _parameters.VerticalSpeed;
            s.IsGrounded = false;
            s.AirTimeTicks = def.Movement.FloatWindowTicks;
            _previousX = s.PX; _previousY = s.PY; _previousZ = s.PZ;
            Ignite(ref s, def);
        }
        else if (input.ActiveSlot == Slot + 1 || _fuelTicksLeft == 0)
        {
            s.AnimLockTicks = 0;
            EndAbility(ref s);
            return;
        }

        if (input.Jump)
        {
            _burstTicksLeft = _parameters.JumpBurstTicks;
            _liftSpeed = _parameters.JumpBurstSpeed;
        }
        if (input.MoveX * input.MoveX + input.MoveY * input.MoveY > .01f)
            _headingYaw = MathF.Atan2(input.MoveX, input.MoveY);
        s.FacingYaw = _headingYaw;
        s.VX = MathF.Sin(_headingYaw) * _parameters.HorizontalSpeed;
        s.VZ = MathF.Cos(_headingYaw) * _parameters.HorizontalSpeed;
        s.VY = _burstTicksLeft > 0 ? _liftSpeed : -_parameters.SinkSpeed;
        s.IsFastFalling = false;
        s.IsGrounded = false;
        s.AnimLockTicks = (ushort)(_fuelTicksLeft + 1);
        _fuelTicksLeft--;
        if (_burstTicksLeft > 0) _burstTicksLeft--;

        SpawnResolverHitbox(new Hitbox
        {
            OwnerId = s.EntityId, AttackSlot = (byte)(Slot + 1), DurationTicks = 1,
            Shape = HitboxShape.Capsule, Radius = _parameters.ExhaustRadius, IsCanIgniterOnly = true,
            X = _previousX, Y = _previousY - def.CapsuleHeight * .5f, Z = _previousZ,
            EndX = s.PX - MathF.Sin(_headingYaw), EndY = s.PY - def.CapsuleHeight * .5f,
            EndZ = s.PZ - MathF.Cos(_headingYaw),
        });
        _previousX = s.PX; _previousY = s.PY; _previousZ = s.PZ;
    }

    private void Ignite(ref CharacterState s, CharacterDefinition def)
    {
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
        }, ignitesOwnerCans: true);
        PresentationSink?.Invoke(new TimelinePresentationEvent(
            0, s.EntityId, PresentationOperationIndex, _parameters.ExplosionPresentationId,
            PresentationAttackSequence, PresentationEventSource.CapabilityExplosion,
            s.PX, s.PY - def.CapsuleHeight * .5f, s.PZ, s.FacingYaw)
        { Placement = new PresentationPlacement(DurationTicks: 150) });
    }

    public override void OnEnd(ref CharacterState s)
    {
        s.IsAiming = false;
        s.VY = MathF.Min(s.VY, 0f);
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_elapsedTicks); w.WriteBool(_ignited);
        w.WriteUShort(_fuelTicksLeft); w.WriteUShort(_burstTicksLeft);
        w.WriteFloat(_liftSpeed); w.WriteFloat(_headingYaw);
        w.WriteFloat(_previousX); w.WriteFloat(_previousY); w.WriteFloat(_previousZ);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _elapsedTicks = r.ReadUShort(); _ignited = r.ReadBool();
        _fuelTicksLeft = r.ReadUShort(); _burstTicksLeft = r.ReadUShort();
        _liftSpeed = r.ReadFloat(); _headingYaw = r.ReadFloat();
        _previousX = r.ReadFloat(); _previousY = r.ReadFloat(); _previousZ = r.ReadFloat();
    }
}
