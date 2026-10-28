using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// slop.ability.thrust.v1 (ADR-0045): picks one direction when its move starts, with no wind-up, and drives that
/// velocity with gravity off for its thrust ticks, then leaves the velocity to whatever owns her next. Bashie's
/// Hydro Jet aims with the stick and trails a shove; Shell Dive leans toward a target ahead and keeps a faster
/// incoming speed (the jet it cancels). A tuck move that lands is retired by the shell, not by the thrust.
/// </summary>
public sealed class ThrustAbility : ServerAbility
{
    private const float StickDeadZone = 0.2f;

    private readonly CookedThrustCapabilityParameters _parameters;
    private float _dirX, _dirY, _dirZ;
    private float _speed;
    private ushort _ticks;
    private HashSet<ulong>? _trailHits;

    public ThrustAbility(CookedThrustCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    private bool Thrusting => _ticks < _parameters.ThrustTicks;
    public override bool OwnsVerticalMotion => Thrusting;
    // Fighters move before abilities tick: gravity is switched off to hold the line, not only rewritten after.
    public override float GravityMultiplier => Thrusting ? 0f : 1f;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;
        var input = ActivationInput;
        float stick = MathF.Min(1f, MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY));
        float yaw = stick > StickDeadZone ? MathF.Atan2(input.MoveX, input.MoveY) : s.FacingYaw;
        float pitchDegrees = _parameters.NeutralPitchDegrees
            + (_parameters.StickPitchDegrees - _parameters.NeutralPitchDegrees) * (stick > StickDeadZone ? stick : 0f);
        if (_parameters.Aim == ThrustAim.TargetAhead
            && OwnerSimulation!.TryGetTargetAhead(in s, _parameters.TargetConeDegrees, out float targetYaw))
        {
            yaw = targetYaw;
            pitchDegrees = _parameters.NeutralPitchDegrees;
        }
        float pitch = pitchDegrees * MathF.PI / 180f;
        float cosPitch = MathF.Cos(pitch), sinPitch = MathF.Sin(pitch);
        _dirX = cosPitch * MathF.Sin(yaw);
        _dirY = sinPitch;
        _dirZ = cosPitch * MathF.Cos(yaw);
        if (cosPitch > 0.001f) s.FacingYaw = yaw;
        float incoming = MathF.Sqrt(s.VX * s.VX + s.VY * s.VY + s.VZ * s.VZ);
        _speed = _parameters.KeepIncomingSpeed ? MathF.Max(_parameters.Speed, incoming) : _parameters.Speed;
        if (_parameters.TrailHitbox is { } trail)
            _trailHits = SpawnHitbox(ref s, TravelFrame(CookedCharacterRuntimeAdapter.ToHitboxEvent(trail), cosPitch, sinPitch)
                with { DurationTicks = _parameters.ThrustTicks, HitGroup = 0 }, followOwner: true);
        Drive(ref s);
        if (_dirY > 0f) s.IsGrounded = false;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (!Thrusting) return;
        _ticks++;
        if (!Thrusting)
        {
            RemoveTrail(ref s);
            // The thrust spent its float: ordinary gravity takes over at once.
            s.AirTimeTicks = def.Movement.FloatWindowTicks;
            return;
        }
        _speed = MathF.Min(_speed + _parameters.Acceleration * Simulation.TickDt, MathF.Max(_parameters.MaxSpeed, _speed));
        Drive(ref s);
    }

    public override void OnCancel(ref CharacterState s) => RemoveTrail(ref s);

    private void Drive(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.VX = _dirX * _speed;
        s.VY = _dirY * _speed;
        s.VZ = _dirZ * _speed;
    }

    private void RemoveTrail(ref CharacterState s)
    {
        if (_trailHits == null) return;
        Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId, _trailHits);
        _trailHits = null;
    }

    /// <summary>Rotates offsets authored along the travel (+Z) into the facing frame by the travel pitch.</summary>
    private static HitboxEvent TravelFrame(HitboxEvent evt, float cosPitch, float sinPitch)
        => evt with
        {
            OffY = evt.OffY * cosPitch + evt.OffZ * sinPitch,
            OffZ = evt.OffZ * cosPitch - evt.OffY * sinPitch,
            EndOffY = evt.EndOffY * cosPitch + evt.EndOffZ * sinPitch,
            EndOffZ = evt.EndOffZ * cosPitch - evt.EndOffY * sinPitch,
        };

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteFloat(_dirX);
        w.WriteFloat(_dirY);
        w.WriteFloat(_dirZ);
        w.WriteFloat(_speed);
        w.WriteUShort(_ticks);
        w.WriteSet(_trailHits);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _dirX = r.ReadFloat();
        _dirY = r.ReadFloat();
        _dirZ = r.ReadFloat();
        _speed = r.ReadFloat();
        _ticks = r.ReadUShort();
        _trailHits = r.ReadSet();
    }
}
