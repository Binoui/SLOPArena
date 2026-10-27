using System;

namespace SlopArena.Shared.Abilities;

/// <summary>One shot per activation; an empty cylinder commits to a full, interruptible reload.</summary>
public sealed class FingerGunsAbility : ServerAbility, ILandingContinuationCapability
{
    private readonly CookedFingerGunsCapabilityParameters _parameters;
    private ushort _ticks;
    private bool _reloading;
    private float _aimYaw;
    private float _aimPitch;

    // Integrate capped gravity here so ordinary movement cannot accelerate past the fall limit.
    public override bool OwnsVerticalMotion => true;
    public override float GravityMultiplier => 0f;

    public FingerGunsAbility(CookedFingerGunsCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;
        _reloading = false;
        _aimYaw = s.AimYaw;
        _aimPitch = ActivationInput.AimPitch * (MathF.PI / 18000f);
        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        s.AttackElapsedTicks = 0;
        s.IsAiming = false;
        s.ComboStage = AnimIndex = 0;
        if (def.GetCookedSlotAbility((byte)(Slot + 1), AirborneAtStart)?.AimMode != AuthoringAimMode.None)
            s.FacingYaw = _aimYaw;
        s.AnimLockTicks = (ushort)(_parameters.FireTriggerTick + _parameters.RecoveryTicks);
        LimitMotion(ref s);
        if (s.FingerGunsShotsSpent >= _parameters.ShotsPerReload)
            BeginReload(ref s);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (!s.IsGrounded)
        {
            float gravity = s.AirTimeTicks < def.Movement.FloatWindowTicks
                ? def.Movement.AirFloatGravity : def.Movement.Gravity;
            s.VY -= gravity * Simulation.TickDt;
        }
        LimitMotion(ref s);
        _ticks++;
        s.AttackElapsedTicks = _ticks;

        if (_reloading)
        {
            if (_ticks >= _parameters.ReloadTicks)
            {
                s.FingerGunsShotsSpent = 0;
                EndAbility(ref s);
            }
            return;
        }

        if (_ticks == _parameters.FireTriggerTick)
        {
            s.AimYaw = _aimYaw;
            s.AimPitch = _aimPitch;
            bool autoTarget = def.GetCookedSlotAbility((byte)(Slot + 1), AirborneAtStart)?.AimMode == AuthoringAimMode.None;
            var shot = SpawnCookedProjectile(ref s, _parameters.Projectile, PresentationOperationIndex, autoTarget);
            s.FingerGunsShotsSpent++;
            float across = MathF.Sqrt(shot.VX * shot.VX + shot.VZ * shot.VZ);
            if (!s.IsGrounded && across > 0.0001f)
            {
                ClearVelocityOwnership(ref s);
                s.VX = -shot.VX / across * _parameters.AirRecoilSpeed;
                s.VZ = -shot.VZ / across * _parameters.AirRecoilSpeed;
            }
            if (s.FingerGunsShotsSpent >= _parameters.ShotsPerReload)
            {
                BeginReload(ref s);
                return;
            }
        }
        if (_ticks >= _parameters.FireTriggerTick + _parameters.RecoveryTicks)
            EndAbility(ref s);
    }

    public override void OnEnd(ref CharacterState s) => s.AnimLockTicks = 0;

    // Cancellation never refills the cylinder. The character state survives runtime retirement.
    private void BeginReload(ref CharacterState s)
    {
        _reloading = true;
        _ticks = 0;
        s.AttackElapsedTicks = 0;
        s.AnimLockTicks = _parameters.ReloadTicks;
        int index = Move?.FindClip(_parameters.ReloadAnimationId) ?? -1;
        if (index > 0 && index <= byte.MaxValue)
            s.ComboStage = AnimIndex = (byte)index;
    }

    private void LimitMotion(ref CharacterState s)
    {
        s.IsFastFalling = false;
        ClearVelocityOwnership(ref s);
        if (s.IsGrounded) s.VX = s.VZ = 0f;
        else s.VY = MathF.Max(s.VY, -_parameters.AirMaxFallSpeed);
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_ticks);
        w.WriteBool(_reloading);
        w.WriteFloat(_aimYaw);
        w.WriteFloat(_aimPitch);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _ticks = r.ReadUShort();
        _reloading = r.ReadBool();
        _aimYaw = r.ReadFloat();
        _aimPitch = r.ReadFloat();
    }
}
