using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// slop.ability.command-grab.v1 (ADR-0051), Nyx's Selfie Grab. Lunge toward the current target, then the authored
/// capture volume. A catch pairs both fighters in the command-hold phase (the simulation's CatchOnCommandGrab); each
/// tick of the hold she stays planted and places the victim at her grab anchor, and on the release tick the pair ends
/// and the authored launch lands as her hit. A whiff, a clash or a lost pair leaves the rest of the stage as recovery:
/// the timeline's stage clock ends the move.
/// </summary>
public sealed class CommandGrabAbility : ServerAbility
{
    private enum Phase : byte { Lunge, Capture, Hold, Done }

    private readonly CookedCommandGrabCapabilityParameters _p;
    private Phase _phase;
    // Ticks since the activation, in step with the timeline's stage clock.
    private ushort _ticks;
    private ulong _targetId;
    private bool _lunging;

    public CommandGrabAbility(CookedCommandGrabCapabilityParameters parameters)
        => _p = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override CommandGrabAbility? CommandGrab => this;
    public override bool OwnsVerticalMotion => _phase == Phase.Hold;
    public override float GravityMultiplier => _phase == Phase.Hold ? 0f : 1f;

    /// <summary>The capture volume is open and its contacts catch.</summary>
    internal bool Catches => _phase == Phase.Capture;
    /// <summary>A grabbed fighter is held.</summary>
    internal bool Holding => _phase == Phase.Hold;
    internal ushort ReleaseTick => _p.ReleaseTick;
    internal ushort Ticks => _ticks;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        _phase = Phase.Lunge;
        _ticks = 0;
        _targetId = 0;
        if (OwnerSimulation != null && OwnerSimulation.TryGetCurrentTarget(in s, out ulong targetId, out var target))
        {
            _targetId = targetId;
            float dx = target.X - s.PX, dz = target.Z - s.PZ;
            if (dx * dx + dz * dz > 0.000001f) s.FacingYaw = MathF.Atan2(dx, dz);
        }
        _lunging = _p.LungeTicks > 0 && _p.LungeSpeed > 0f;
        Lunge(ref s);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_ticks < ushort.MaxValue) _ticks++;
        switch (_phase)
        {
            case Phase.Lunge:
            case Phase.Capture:
                Lunge(ref s);
                if (_phase == Phase.Lunge && _ticks >= _p.CaptureTick) OpenCapture(ref s);
                else if (_phase == Phase.Capture && _ticks >= _p.CaptureTick + _p.Capture.DurationTicks) CloseCapture(ref s);
                break;
            case Phase.Hold:
                Plant(ref s);
                if (!OwnerSimulation!.HoldCommandGrab(ref s))
                    _phase = Phase.Done;
                else if (_ticks >= _p.ReleaseTick)
                {
                    OwnerSimulation.ReleaseCommandGrab(ref s, _p);
                    Emit(ref s, _p.ReleasePresentationId);
                    _phase = Phase.Done;
                }
                break;
        }
    }

    /// <summary>
    /// The lunge's travel along her facing, until its time runs out or her target is within the stop distance. It
    /// stops for good: the capture then stands where she is.
    /// </summary>
    private void Lunge(ref CharacterState s)
    {
        if (!_lunging) return;
        bool near = _targetId != 0 && SimulationStates != null && SimulationStates.TryGetValue(_targetId, out var target)
            && (target.PX - s.PX) * (target.PX - s.PX) + (target.PZ - s.PZ) * (target.PZ - s.PZ)
                <= _p.StopDistance * _p.StopDistance;
        if (_ticks >= _p.LungeTicks || near)
        {
            _lunging = false;
            ClearVelocityOwnership(ref s);
            s.VX = s.VZ = 0f;
            return;
        }
        ClearVelocityOwnership(ref s);
        s.VX = MathF.Sin(s.FacingYaw) * _p.LungeSpeed;
        s.VZ = MathF.Cos(s.FacingYaw) * _p.LungeSpeed;
    }

    private void OpenCapture(ref CharacterState s)
    {
        var capture = CookedCharacterRuntimeAdapter.ToHitboxEvent(_p.Capture, _ticks) with { HitGroup = 1 };
        SpawnHitbox(ref s, capture, followOwner: true, freezesOwner: false);
        _phase = Phase.Capture;
    }

    private void CloseCapture(ref CharacterState s)
    {
        Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
        _phase = Phase.Done;
    }

    /// <summary>Called by the simulation in the tick its capture takes a fighter: the hold starts.</summary>
    internal void OnCaught(ref CharacterState s)
    {
        Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
        _phase = Phase.Hold;
        _lunging = false;
        Plant(ref s);
        Emit(ref s, _p.CatchPresentationId);
    }

    /// <summary>Called by the simulation when the catch met a grab on the same tick: both grabs miss.</summary>
    internal void OnClashed(ref CharacterState s)
    {
        Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
        _phase = Phase.Done;
        _lunging = false;
        ClearVelocityOwnership(ref s);
        s.VX = s.VZ = 0f;
    }

    public override void OnCancel(ref CharacterState s) => _phase = Phase.Done;

    private static void Plant(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.VX = s.VY = s.VZ = 0f;
    }

    private void Emit(ref CharacterState s, string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, PresentationOperationIndex, id,
            PresentationAttackSequence, PresentationEventSource.CapabilityLaunch, s.PX, s.PY, s.PZ, s.FacingYaw));
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteByte((byte)_phase);
        w.WriteUShort(_ticks);
        w.WriteULong(_targetId);
        w.WriteBool(_lunging);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte();
        if (_phase > Phase.Done) throw SnapshotReader.Malformed("command grab phase");
        _ticks = r.ReadUShort();
        _targetId = r.ReadULong();
        _lunging = r.ReadBool();
    }
}
