using System;
using SlopArena.Shared;

namespace SlopArena.Shared.Abilities;

/// <summary>Policy-controlled aim, bounded targeted flight, landing hitbox, pose seek, and recovery.</summary>
public sealed class TargetedLeapAbility : ServerAbility, IAimHoldCapability, ILandingContinuationCapability
{
    private const ushort ReleaseDebounceTicks = 8;
    private readonly CookedTargetedLeapCapabilityParameters _parameters;
    private Phase _phase;
    private ushort _phaseTicks;
    private float _aimYaw;
    private float _aimDistance;
    private float _horizontalSpeed;
    private bool _hitboxSpawned;

    private enum Phase : byte { Aim, Flight, Landing }

    public override bool OwnsVerticalMotion => true;

    public TargetedLeapAbility(CookedTargetedLeapCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Aim;
        _phaseTicks = 0;
        _aimYaw = s.AimYaw;
        _aimDistance = s.AimTargetDistance;
        _horizontalSpeed = 0f;
        _hitboxSpawned = false;

        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.State = ActionState.Aiming;
        s.AttackSlot = (byte)(Slot + 1);
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.IsAiming = true;
        s.AnimLockTicks = _parameters.MaxAimTicks;
        AnimIndex = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _phaseTicks++;
        switch (_phase)
        {
            case Phase.Aim:
                TickAim(ref s, input, def);
                break;
            case Phase.Flight:
                TickFlight(ref s, def);
                break;
            case Phase.Landing:
                TickLanding(ref s);
                break;
        }
    }

    private void TickAim(ref CharacterState s, InputState input, CharacterDefinition def)
    {
        if (input.IsAiming)
        {
            _aimYaw = s.AimYaw;
            _aimDistance = s.AimTargetDistance;
        }

        if (_phaseTicks > ReleaseDebounceTicks &&
            (!input.IsAiming || (_parameters.MaxAimTicks > 0 && _phaseTicks >= _parameters.MaxAimTicks)))
            StartFlight(ref s, def);
    }

    private void StartFlight(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Flight;
        _phaseTicks = 0;
        _aimDistance = Math.Clamp(_aimDistance, _parameters.MinRange, _parameters.MaxRange);
        _aimYaw = NormalizeYaw(_aimYaw);

        float gravity = def.Movement.Gravity;
        float flightSeconds = gravity > 0f
            ? 2f * _parameters.LaunchVerticalSpeed / gravity
            : _parameters.MaxFlightTicks * Simulation.TickDt;
        if (flightSeconds <= 0f)
            flightSeconds = _parameters.MaxFlightTicks * Simulation.TickDt;
        _horizontalSpeed = _aimDistance / flightSeconds;

        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.FacingYaw = _aimYaw;
        s.IsGrounded = false;
        s.AirTimeTicks = def.Movement.FloatWindowTicks;
        s.VX = MathF.Sin(_aimYaw) * _horizontalSpeed;
        s.VY = _parameters.LaunchVerticalSpeed;
        s.VZ = MathF.Cos(_aimYaw) * _horizontalSpeed;
        s.AttackElapsedTicks = 0;
        s.AnimLockTicks = _parameters.MaxFlightTicks;
        EmitPhasePresentation(ref s, def, _parameters.LaunchPresentationId, PresentationEventSource.CapabilityLaunch);
    }

    private void TickFlight(ref CharacterState s, CharacterDefinition def)
    {
        s.IsAiming = false;
        if (s.IsGrounded)
        {
            StartLanding(ref s, def);
            return;
        }

        if (_phaseTicks >= _parameters.MaxFlightTicks)
        {
            EndAbility(ref s);
            return;
        }

        s.FacingYaw = _aimYaw;
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.VX = MathF.Sin(_aimYaw) * _horizontalSpeed;
        s.VZ = MathF.Cos(_aimYaw) * _horizontalSpeed;
    }

    private void StartLanding(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Landing;
        _phaseTicks = 0;
        s.State = ActionState.Attacking;
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.IsAiming = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
        s.AttackElapsedTicks = _parameters.LandingSeekTick;
        s.AnimLockTicks = _parameters.RecoveryTicks;
        SpawnLandingHitbox(ref s);
        EmitPhasePresentation(ref s, def, _parameters.LandingPresentationId, PresentationEventSource.CapabilityLanding);
    }

    private void EmitPhasePresentation(ref CharacterState s, CharacterDefinition def, string id, PresentationEventSource source)
    {
        if (string.IsNullOrEmpty(id)) return;
        // Ground bursts use the actual capsule feet at the phase transition, never
        // a cursor destination or an assumed flight/animation duration.
        PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, PresentationOperationIndex,
            id, PresentationAttackSequence, source, s.PX, s.PY - def.CapsuleHeight * 0.5f, s.PZ, s.FacingYaw));
    }

    private void TickLanding(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.IsAiming = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
        if (_phaseTicks >= _parameters.RecoveryTicks)
            EndAbility(ref s);
    }

    private void SpawnLandingHitbox(ref CharacterState s)
    {
        if (_hitboxSpawned)
            return;
        _hitboxSpawned = true;
        SpawnHitbox(ref s, CookedCharacterRuntimeAdapter.ToHitboxEvent(_parameters.Hitbox));
    }

    public override void OnEnd(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.IsAiming = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
    }

    public override void OnCancel(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.IsAiming = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
        s.AttackSlot = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.AnimLockTicks = 0;
    }

    private static float NormalizeYaw(float yaw)
    {
        while (yaw > MathF.PI) yaw -= 2f * MathF.PI;
        while (yaw < -MathF.PI) yaw += 2f * MathF.PI;
        return yaw;
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteByte((byte)_phase);
        w.WriteUShort(_phaseTicks);
        w.WriteFloat(_aimYaw);
        w.WriteFloat(_aimDistance);
        w.WriteFloat(_horizontalSpeed);
        w.WriteBool(_hitboxSpawned);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte();
        _phaseTicks = r.ReadUShort();
        _aimYaw = r.ReadFloat();
        _aimDistance = r.ReadFloat();
        _horizontalSpeed = r.ReadFloat();
        _hitboxSpawned = r.ReadBool();
    }
}
