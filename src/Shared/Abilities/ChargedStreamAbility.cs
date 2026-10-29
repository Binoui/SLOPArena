using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Hold to pump, release to spray a stream of projectiles (Bashie's Hose Down, like FLUDD). A manually aimed slot turns
/// the fighter to its aim while she pumps; the stream keeps the release direction. The pump sets the stream's length and
/// push, and the stream pushes its owner back while she is in the air. The capability owns the end of its move: its spray
/// and end phase clips play in real time, and it ends the move after its recovery.
/// </summary>
public sealed class ChargedStreamAbility : ServerAbility, IAimHoldCapability, ILandingContinuationCapability
{
    private readonly CookedChargedStreamCapabilityParameters _parameters;
    private bool _released;
    private ushort _ticks;
    private ushort _streamTicks;
    private float _baseKnockback;
    private float _recoilSpeed;
    private float _aimYaw;
    private float _aimPitch;

    public ChargedStreamAbility(CookedChargedStreamCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _released = false;
        _ticks = _streamTicks = 0;
        _baseKnockback = _recoilSpeed = 0f;
        _aimYaw = s.AimYaw;
        _aimPitch = s.AimPitch;
        s.State = ActionState.Aiming;
        s.AttackSlot = (byte)(Slot + 1);
        s.AttackElapsedTicks = 0;
        s.ChargeTicks = 0;
        s.IsAiming = true;
        s.ComboStage = 0;
        AnimIndex = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        bool autoTarget = def.GetCookedSlotAbility((byte)(Slot + 1), AirborneAtStart)?.AimMode == AuthoringAimMode.None;
        if (!_released)
        {
            if (input.IsAiming)
            {
                if (s.ChargeTicks < _parameters.MaxChargeTicks) s.ChargeTicks++;
                _aimYaw = s.AimYaw;
                _aimPitch = s.AimPitch;
                // Like Manki's Aerosol Inferno: she turns to her aim while she pumps.
                if (!autoTarget) s.FacingYaw = s.AimYaw;
                return;
            }
            _released = true;
            _streamTicks = _parameters.GetStreamTicks(s.ChargeTicks);
            _baseKnockback = _parameters.GetBaseKnockback(s.ChargeTicks);
            _recoilSpeed = _parameters.GetRecoilSpeed(s.ChargeTicks);
            s.State = ActionState.Attacking;
            s.IsAiming = false;
            ShowClip(ref s, _parameters.SprayAnimationId);
        }
        else if (_ticks < ushort.MaxValue)
        {
            _ticks++;
        }

        int streamEnd = _parameters.FireTriggerTick + _streamTicks;
        // Planted on the ground: attacks keep their velocity, so the recoil of a stream begun in the air would slide on.
        if (s.IsGrounded)
        {
            ClearVelocityOwnership(ref s);
            s.VX = s.VZ = 0f;
        }
        if (_ticks >= _parameters.FireTriggerTick && _ticks < streamEnd
            && (_ticks - _parameters.FireTriggerTick) % _parameters.EmitIntervalTicks == 0)
            Emit(ref s, autoTarget);

        if (_ticks < streamEnd)
        {
            s.AttackElapsedTicks = _parameters.SprayLoopTicks > 0
                ? (ushort)(_ticks % _parameters.SprayLoopTicks) : _ticks;
        }
        else
        {
            if (_ticks == streamEnd) ShowClip(ref s, _parameters.EndAnimationId);
            s.AttackElapsedTicks = (ushort)(_ticks - streamEnd);
            if (_ticks >= streamEnd + _parameters.RecoveryTicks)
                EndAbility(ref s);
        }
    }

    public override void OnEnd(ref CharacterState s)
    {
        s.IsAiming = false;
        s.ChargeTicks = 0;
    }

    public override void OnCancel(ref CharacterState s)
    {
        s.IsAiming = false;
        s.ChargeTicks = 0;
    }

    /// <summary>One projectile of the stream, along the release aim; in the air it pushes her back.</summary>
    private void Emit(ref CharacterState s, bool autoTarget)
    {
        s.AimYaw = _aimYaw;
        s.AimPitch = _aimPitch;
        var shot = SpawnCookedProjectile(ref s, _parameters.Projectile with { BaseKnockback = _baseKnockback },
            PresentationOperationIndex, autoTarget);
        float across = MathF.Sqrt(shot.VX * shot.VX + shot.VZ * shot.VZ);
        if (s.IsGrounded || _recoilSpeed <= 0f || across <= 0.0001f) return;
        ClearVelocityOwnership(ref s);
        s.VX = -shot.VX / across * _recoilSpeed;
        s.VZ = -shot.VZ / across * _recoilSpeed;
    }

    /// <summary>Switches to a phase clip listed in the stage after its first clip.</summary>
    private void ShowClip(ref CharacterState s, string animationId)
    {
        if (string.IsNullOrEmpty(animationId)) return;
        int index = Move?.FindClip(animationId) ?? -1;
        if (index <= 0 || index > byte.MaxValue) return;
        s.ComboStage = (byte)index;
        AnimIndex = (byte)index;
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteBool(_released);
        w.WriteUShort(_ticks);
        w.WriteUShort(_streamTicks);
        w.WriteFloat(_baseKnockback);
        w.WriteFloat(_recoilSpeed);
        w.WriteFloat(_aimYaw);
        w.WriteFloat(_aimPitch);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _released = r.ReadBool();
        _ticks = r.ReadUShort();
        _streamTicks = r.ReadUShort();
        _baseKnockback = r.ReadFloat();
        _recoilSpeed = r.ReadFloat();
        _aimYaw = r.ReadFloat();
        _aimPitch = r.ReadFloat();
    }
}
