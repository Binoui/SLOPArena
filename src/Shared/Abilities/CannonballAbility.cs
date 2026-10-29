using System;

namespace SlopArena.Shared.Abilities;

/// <summary>One solved hop: the yaw she faces, the horizontal and vertical launch speeds, and the planned air time.</summary>
internal readonly record struct CannonballArc(float Yaw, float HorizontalSpeed, float VerticalSpeed, ushort FlightTicks);

/// <summary>The cannonball's hop, solved once from her feet to a landing point (ADR-0050).</summary>
internal static class CannonballFlight
{
    /// <summary>
    /// Clamps the horizontal distance to the max range, scales the flight time from the min flight ticks at her feet to
    /// the max flight ticks at max range, and solves the launch for the simulation's own integration: the takeoff tick
    /// ends on her support, so her first flight tick rises without gravity, then each tick applies gravity and moves.
    /// The launch is capped at the max launch speed, and never below the hop to her own feet, so she always leaves the
    /// ground. A landing point at her feet keeps her facing.
    /// </summary>
    internal static CannonballArc Solve(float feetX, float feetY, float feetZ, float facingYaw,
        float landingX, float landingY, float landingZ, CookedCannonballCapabilityParameters p, float gravity)
    {
        float dx = landingX - feetX, dz = landingZ - feetZ;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        float yaw = distance > 0.001f ? MathF.Atan2(dx, dz) : facingYaw;
        distance = MathF.Min(distance, p.MaxRange);
        ushort ticks = (ushort)MathF.Round(p.MinFlightTicks + (p.MaxFlightTicks - p.MinFlightTicks) * distance / p.MaxRange);
        float seconds = ticks * Simulation.TickDt;
        float hop = gravity * Simulation.TickDt * (ticks - 1) * 0.5f;
        float vertical = Math.Clamp((landingY - feetY) / seconds + hop, MathF.Min(hop, p.MaxLaunchSpeed), p.MaxLaunchSpeed);
        return new CannonballArc(yaw, distance / seconds, vertical, ticks);
    }
}

/// <summary>
/// slop.ability.cannonball.v1 (ADR-0050): takes the landing point from her current target in its first tick (their
/// feet, else the no-target distance along her facing), squats until the takeoff tick, then hops on the solved arc
/// and holds its horizontal speed and heading. It only flies: the shell's touchdown rule ends the tucked move with
/// the splash, and with no landing by the stage end the move completes and she falls free keeping her speed.
/// </summary>
public sealed class CannonballAbility : ServerAbility
{
    private readonly CookedCannonballCapabilityParameters _parameters;
    private ushort _ticks;
    private bool _airborne;
    private float _yaw;
    private float _horizontalSpeed;
    private float _verticalSpeed;

    public CannonballAbility(CookedCannonballCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    // Jump, fast-fall and the float window cannot bend the solved arc once she is up.
    public override bool OwnsVerticalMotion => _airborne;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;
        _airborne = false;
        _yaw = s.FacingYaw;
        _horizontalSpeed = _verticalSpeed = 0f;
        if (s.IsGrounded) s.VX = s.VZ = 0f;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_airborne)
        {
            ClearVelocityOwnership(ref s);
            s.IsFastFalling = false;
            s.FacingYaw = _yaw;
            s.VX = MathF.Sin(_yaw) * _horizontalSpeed;
            s.VZ = MathF.Cos(_yaw) * _horizontalSpeed;
            return;
        }
        if (_ticks == 0) Aim(in s, def);
        if (s.IsGrounded) s.VX = s.VZ = 0f;
        if (_ticks++ < _parameters.TakeoffTick) return;
        TakeOff(ref s, def);
    }

    private void Aim(in CharacterState s, CharacterDefinition def)
    {
        float feetY = s.PY - def.CapsuleHeight * 0.5f;
        float x, y, z;
        if (OwnerSimulation!.TryGetCurrentTargetFeet(in s, out var feet))
            (x, y, z) = (feet.X, feet.Y, feet.Z);
        else
            (x, y, z) = (s.PX + MathF.Sin(s.FacingYaw) * _parameters.NoTargetDistance, feetY,
                s.PZ + MathF.Cos(s.FacingYaw) * _parameters.NoTargetDistance);
        var arc = CannonballFlight.Solve(s.PX, feetY, s.PZ, s.FacingYaw, x, y, z, _parameters, def.Movement.Gravity);
        (_yaw, _horizontalSpeed, _verticalSpeed) = (arc.Yaw, arc.HorizontalSpeed, arc.VerticalSpeed);
    }

    private void TakeOff(ref CharacterState s, CharacterDefinition def)
    {
        _airborne = true;
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.IsGrounded = false;
        // Full gravity at once, as the solve assumes.
        s.AirTimeTicks = def.Movement.FloatWindowTicks;
        s.FacingYaw = _yaw;
        s.VX = MathF.Sin(_yaw) * _horizontalSpeed;
        s.VY = _verticalSpeed;
        s.VZ = MathF.Cos(_yaw) * _horizontalSpeed;
        if (!string.IsNullOrEmpty(_parameters.LaunchPresentationId))
            PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, PresentationOperationIndex,
                _parameters.LaunchPresentationId, PresentationAttackSequence, PresentationEventSource.CapabilityLaunch,
                s.PX, s.PY - def.CapsuleHeight * 0.5f, s.PZ, s.FacingYaw));
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteUShort(_ticks);
        w.WriteBool(_airborne);
        w.WriteFloat(_yaw);
        w.WriteFloat(_horizontalSpeed);
        w.WriteFloat(_verticalSpeed);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _ticks = r.ReadUShort();
        _airborne = r.ReadBool();
        _yaw = r.ReadFloat();
        _horizontalSpeed = r.ReadFloat();
        _verticalSpeed = r.ReadFloat();
    }
}
