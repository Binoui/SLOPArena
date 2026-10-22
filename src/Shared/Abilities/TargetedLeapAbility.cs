using System;
using SlopArena.Shared;

namespace SlopArena.Shared.Abilities;

/// <summary>Policy-controlled aim, bounded targeted flight, landing hitbox, pose seek, and recovery. With two clips in
/// its stage the flight plays the first and the landing switches to the second (ComboStage 1).</summary>
public sealed class TargetedLeapAbility : ServerAbility, IAimHoldCapability, ILandingContinuationCapability
{
    private const ushort ReleaseDebounceTicks = 8;
    private const ushort LandingApproachTicks = 18;
    private readonly CookedTargetedLeapCapabilityParameters _parameters;
    private readonly int[] _landingCandidates = new int[512];
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
        UpdateFlightPose(ref s, def);
    }

    private void UpdateFlightPose(ref CharacterState s, CharacterDefinition def)
    {
        var slot = def.GetCookedSlotAbility((byte)(Slot + 1), AirborneAtStart);
        if (AnimationNames.Length != 2 || slot?.Timeline.Stages.Count != 1 || Arena == null)
            return;

        int lastTick = slot.Timeline.Stages[0].DurationTicks - 1;
        if (lastTick <= 0)
            return;
        int approachTicks = Math.Min(LandingApproachTicks, lastTick);
        int holdTick = lastTick - approachTicks;
        float remaining = s.VY < 0f ? LandingTicks(s, def, approachTicks) : approachTicks;
        // Ease through the flight's final poses as the ground approaches. Holding the
        // pre-swing pose extends a long fall; a raised platform brings the swing forward.
        // The real phase clock still owns timeout, touchdown, damage and recovery.
        float fraction = remaining / approachTicks;
        float poseTick = holdTick + approachTicks * (1f - fraction * fraction);
        int tick = Math.Max(Math.Min(_phaseTicks, holdTick), (int)MathF.Round(poseTick));
        // A disappearing support must not play the swing backwards.
        s.AttackElapsedTicks = (ushort)Math.Clamp(Math.Max(s.AttackElapsedTicks - 1, tick), 0, lastTick);
    }

    private float LandingTicks(in CharacterState s, CharacterDefinition def, int horizon)
    {
        var arena = Arena!.Value;
        float x = s.PX, y = s.PY, z = s.PZ, vy = s.VY;
        bool triangles = ArenaCollision.HasTriangles(arena);
        for (int tick = 1; tick <= horizon; tick++)
        {
            vy = MathF.Max(vy - def.Movement.Gravity * Simulation.TickDt, -def.Movement.MaxFallSpeed);
            float nextX = x + s.VX * Simulation.TickDt;
            float nextY = y + vy * Simulation.TickDt;
            float nextZ = z + s.VZ * Simulation.TickDt;
            if (triangles)
            {
                int count = ArenaCollision.GetCandidateTrianglesForSweep(x, y, z, nextX, nextY, nextZ,
                    def.CapsuleRadius, def.CapsuleHeight, in arena, _landingCandidates);
                if (ArenaCollision.SweepCapsule(x, y, z, nextX, nextY, nextZ,
                    def.CapsuleRadius, def.CapsuleHeight, in arena, _landingCandidates, count, out var contact))
                {
                    // A wall is not a landing. Leave its eventual slide/contact to physics.
                    return contact.NormalY > 0.5f && ArenaCollision.IsUpwardFacingTriangle(contact.TriangleIndex, in arena)
                        ? tick - 1 + contact.Time : horizon;
                }
            }
            else
            {
                float surface = arena.Heightmap.Data != null
                    ? arena.Heightmap.Sample(nextX, nextZ) : arena.KillHeight + 1f;
                float floor = surface + def.CapsuleHeight * 0.5f;
                if (surface > float.MinValue && nextY <= floor)
                    return tick - 1 + Math.Clamp((y - floor) / (y - nextY), 0f, 1f);
            }
            x = nextX;
            y = nextY;
            z = nextZ;
        }
        return horizon;
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
        // A stage that lists a second clip plays it from the landing: the flight and the landing are separate
        // clips, so the landing (impact and recovery) keeps its own timing however long the flight was. Cooked
        // stages sort their clip IDs, so the landing clip is named after the flight's (anim.bonk.ge-land).
        if (AnimationNames.Length > 1)
        {
            s.ComboStage = 1;
            AnimIndex = 1;
        }
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
