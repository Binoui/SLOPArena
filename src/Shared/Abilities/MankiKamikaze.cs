using System;

namespace SlopArena.Shared.Abilities;

public sealed class MankiKamikaze : ServerAbility, ILandingContinuationCapability
{
    public const ushort StartupTicks = 16;
    private enum KamikazePhase : byte { Startup, Travel }
    private readonly CookedMankiKamikazeCapabilityParameters _parameters;
    private KamikazePhase _phase;
    private ushort _elapsedTicks;
    private ulong _target;
    private float _dx, _dy, _dz, _previousX, _previousY, _previousZ;
    public override bool OwnsVerticalMotion => true;
    public override float GravityMultiplier => _phase == KamikazePhase.Startup ? 1f : 0f;
    public override bool IgnoresFighterPushboxes => _phase == KamikazePhase.Travel;
    public override bool HoldsFighterPushboxPosition => _phase == KamikazePhase.Startup;

    public MankiKamikaze(CookedMankiKamikazeCapabilityParameters parameters) => _parameters = parameters;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        _phase = KamikazePhase.Startup;
        _elapsedTicks = 0;
        _target = ActivationInput.TargetEntityId != 0 && OwnerSimulation != null
            ? OwnerSimulation.ResolveObjectTargetAlias(ActivationInput.TargetEntityId, s.EntityId) : s.TargetEntityId;
        _dx = MathF.Sin(s.FacingYaw); _dz = MathF.Cos(s.FacingYaw);
        _dy = 0f;
        s.VX = s.VZ = 0f;
        if (s.IsGrounded) s.VY = 0f;
        _previousX = s.PX; _previousY = s.PY; _previousZ = s.PZ;
    }

    public override void OnCancel(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.IsAiming = false;
        s.IsFastFalling = false;
        s.VX = s.VZ = 0f;
        if (_phase == KamikazePhase.Travel) s.VY = 0f;
    }

    private void Steer(ref CharacterState s)
    {
        if (OwnerSimulation != null && OwnerSimulation.TryGetProjectileTargetPosition(s.EntityId, _target, out var target))
        {
            float dx = target.X - s.PX, dy = target.Y - s.PY, dz = target.Z - s.PZ;
            float length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length > .001f) { _dx = dx / length; _dy = dy / length; _dz = dz / length; }
        }
        s.FacingYaw = MathF.Atan2(_dx, _dz);
        s.VX = _dx * _parameters.Speed; s.VY = _dy * _parameters.Speed; s.VZ = _dz * _parameters.Speed;
        if (s.VY > 0f) s.IsGrounded = false;
    }

    private ProjectileExplosion Blast(in CharacterState s) => new()
    {
        Radius = _parameters.ExplosionRadius, Damage = _parameters.ExplosionDamage,
        Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)_parameters.ExplosionKbAngle,
            BaseKnockback = _parameters.ExplosionKbBase, KnockbackGrowth = _parameters.ExplosionKbGrowth },
        StunTicks = _parameters.ExplosionStunTicks, DurationTicks = _parameters.ExplosionDurationTicks,
        SelfPushSpeed = _parameters.SelfPushSpeed, EndsOwnerActivation = true,
        ExplosionPresentationId = _parameters.ExplosionPresentationId,
        PresentationAttackSequence = PresentationAttackSequence, PresentationOperationIndex = PresentationOperationIndex,
        PresentationYaw = s.FacingYaw,
    };

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_phase == KamikazePhase.Startup)
        {
            s.VX = s.VZ = 0f;
            if (++_elapsedTicks < StartupTicks) return;
            _phase = KamikazePhase.Travel;
            _elapsedTicks = 0;
            _previousX = s.PX; _previousY = s.PY; _previousZ = s.PZ;
            Steer(ref s);
            return;
        }
        if (++_elapsedTicks >= _parameters.RunTicks)
        {
            Resolver!.QueueExplosion(s.PX, s.PY, s.PZ, Blast(s), s.EntityId, (byte)(Slot + 1),
                ActivationId, AirborneAtStart, PresentationAttackSequence);
            s.AnimLockTicks = 0;
            EndAbility(ref s);
            return;
        }
        SpawnResolverHitbox(new Hitbox
        {
            X = s.PX, Y = s.PY, Z = s.PZ,
            EndX = _previousX, EndY = _previousY, EndZ = _previousZ,
            Shape = HitboxShape.Capsule, Radius = _parameters.ContactRadius, DurationTicks = 1,
            OwnerId = s.EntityId, AttackSlot = (byte)(Slot + 1), CancelWithAbility = true,
            ContactExplosion = true, Explosion = Blast(s),
        });
        _previousX = s.PX; _previousY = s.PY; _previousZ = s.PZ;
        Steer(ref s);
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteByte((byte)_phase);
        w.WriteUShort(_elapsedTicks); w.WriteULong(_target);
        w.WriteFloat(_dx); w.WriteFloat(_dy); w.WriteFloat(_dz);
        w.WriteFloat(_previousX); w.WriteFloat(_previousY); w.WriteFloat(_previousZ);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (KamikazePhase)r.ReadByte();
        _elapsedTicks = r.ReadUShort(); _target = r.ReadULong();
        _dx = r.ReadFloat(); _dy = r.ReadFloat(); _dz = r.ReadFloat();
        _previousX = r.ReadFloat(); _previousY = r.ReadFloat(); _previousZ = r.ReadFloat();
    }
}
