using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// slop.ability.command-grab.v1 (ADR-0057). Lunge toward the current target, then the capture, placed afresh each tick
/// of its window. A catch pairs both fighters in the command-hold phase (the simulation's CatchOnCommandGrab); each tick
/// of the hold the grabber stays planted and places the victim, and on the release tick the pair ends with the authored
/// launch or a short lock before the timeline's finisher. With a hold stage, a whiff holds the lunge stage for the whiff
/// recovery and a lost pair ends the move; without, the rest of the stage is the recovery.
/// Phases: Lunge, Capture, then Hold after a catch, Whiff for a hold stage's miss, Spent once the timeline plays on alone;
/// Done ends it.
/// </summary>
public sealed class CommandGrabAbility : ServerAbility, ICommandHold
{
    private enum Phase : byte { Lunge, Capture, Hold, Whiff, Spent, Done }

    private readonly CookedCommandGrabCapabilityParameters _p;
    private Phase _phase;
    // Ticks since the activation, in step with the timeline's stage clock.
    private ushort _ticks;
    private ushort _whiffTicks;
    private ulong _targetId;
    private bool _lunging;
    // A level lunge's last commanded travel and position, for its wall check.
    private float _commanded, _lastX, _lastZ;
    private bool _wasGrounded;

    public CommandGrabAbility(CookedCommandGrabCapabilityParameters parameters)
        => _p = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override ICommandHold? CommandHold => this;
    public HoldCatch CatchKind => HoldCatch.Capture;
    public float? AnchorDistance => _p.AnchorDistance;
    public float AnchorHeight => 0f;
    public CookedGrabLaunch? Launch => _p.Launch;
    public ushort ReleaseLockTicks => _p.ReleaseLockTicks;
    public bool ReleaseRequested { get; private set; }
    public void OnPlaced(ref CharacterState owner, bool blocked) { }
    public void OnReleased(ref CharacterState owner)
    {
        ReleaseRequested = false;
        Emit(ref owner, _p.ReleasePresentationId);
        _phase = Phase.Spent;
    }
    public override bool OwnsVerticalMotion => _phase == Phase.Hold || _p.Level && _phase is Phase.Lunge or Phase.Capture;
    public override float GravityMultiplier => OwnsVerticalMotion ? 0f : 1f;
    // A hold stage's whiff owns the move's end; a finished grab leaves no stage to start.
    internal override bool HoldsStage => _phase is Phase.Whiff or Phase.Done;

    internal CookedCommandGrabCapabilityParameters Parameters => _p;
    /// <summary>The capture is open and its contacts catch.</summary>
    public bool Catches => _phase == Phase.Capture;
    /// <summary>A grabbed fighter is held: this activation's hits on it deal damage and hitstop only.</summary>
    public bool Holding => _phase == Phase.Hold;

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
        if (_p.Level)
        {
            s.IsFastFalling = false;
            s.VY = 0f;
        }
        _lastX = s.PX;
        _lastZ = s.PZ;
        _wasGrounded = s.IsGrounded;
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
                if (_phase == Phase.Lunge && _ticks >= _p.CaptureTick) _phase = Phase.Capture;
                if (_phase == Phase.Capture && _ticks >= _p.CaptureTick + _p.Capture.DurationTicks || _p.Level && Blocked(in s))
                {
                    EndEmpty(ref s);
                    break;
                }
                if (_phase == Phase.Capture)
                {
                    var capture = CookedCharacterRuntimeAdapter.ToHitboxEvent(_p.Capture) with { HitGroup = 1, DurationTicks = 1 };
                    SpawnHitbox(ref s, capture, followOwner: true, freezesOwner: false);
                }
                Lunge(ref s);
                break;
            case Phase.Hold:
                Plant(ref s);
                if (!OwnerSimulation!.HasCommandHold(in s))
                {
                    if (_p.HasHoldStage) Finish(ref s);
                    else _phase = Phase.Spent;
                }
                else if (_ticks >= _p.ReleaseTick)
                {
                    ReleaseRequested = true;
                }
                break;
            case Phase.Whiff:
                Plant(ref s);
                if (++_whiffTicks >= _p.WhiffRecoveryTicks) Finish(ref s);
                break;
        }
    }

    /// <summary>
    /// The lunge's travel along the fighter's facing, until its time runs out or its target is within the stop distance.
    /// It stops for good. The lunge covers lungeTicks × lungeSpeed: its last tick takes whatever distance remains.
    /// </summary>
    private void Lunge(ref CharacterState s)
    {
        if (!_lunging) return;
        bool near = _p.StopDistance > 0f && _targetId != 0 && SimulationStates != null
            && SimulationStates.TryGetValue(_targetId, out var target)
            && (target.PX - s.PX) * (target.PX - s.PX) + (target.PZ - s.PZ) * (target.PZ - s.PZ)
                <= _p.StopDistance * _p.StopDistance;
        ClearVelocityOwnership(ref s);
        if (_ticks >= _p.LungeTicks || near)
        {
            _lunging = false;
            _commanded = 0f;
            s.VX = s.VZ = 0f;
            return;
        }
        float step = _p.LungeSpeed * Simulation.TickDt;
        _commanded = _ticks + 1 >= _p.LungeTicks ? MathF.Max(0f, step * _p.LungeTicks - step * _ticks) : step;
        s.VX = MathF.Sin(s.FacingYaw) * (_commanded / Simulation.TickDt);
        s.VZ = MathF.Cos(s.FacingYaw) * (_commanded / Simulation.TickDt);
        if (_p.Level) s.VY = 0f;
    }

    /// <summary>A level lunge's travel tick that kept under 60% of its commanded travel met a wall (a landing excepted).</summary>
    private bool Blocked(in CharacterState s)
    {
        float progress = (s.PX - _lastX) * MathF.Sin(s.FacingYaw) + (s.PZ - _lastZ) * MathF.Cos(s.FacingYaw);
        bool landed = s.IsGrounded && !_wasGrounded;
        _wasGrounded = s.IsGrounded;
        _lastX = s.PX;
        _lastZ = s.PZ;
        return _commanded > 0f && progress < _commanded * .6f && !landed;
    }

    /// <summary>Called by the simulation in the tick its capture takes a fighter: the hold starts.</summary>
    public void OnCaught(ref CharacterState s)
    {
        _phase = Phase.Hold;
        _lunging = false;
        Plant(ref s);
        Emit(ref s, _p.CatchPresentationId);
    }

    /// <summary>Called by the simulation when the catch met a grab on the same tick: both grabs miss.</summary>
    public void OnClashed(ref CharacterState s)
    {
        _lunging = false;
        ClearVelocityOwnership(ref s);
        s.VX = s.VZ = 0f;
        EndEmpty(ref s);
    }

    /// <summary>
    /// A capture with damage that meets an object (a course target, the ball, a Mayhem wall) stops there and the rest of
    /// the move plays on it with nobody held, as Fist of Fury did on the courses' bonus targets.
    /// </summary>
    public override void OnHitObject(ref CharacterState attacker, CharacterDefinition attackerDef,
        ref float damage, ref float knockbackForce)
    {
        if (_phase != Phase.Capture || _p.Capture.Damage <= 0f) return;
        _phase = Phase.Spent;
        Plant(ref attacker);
    }

    /// <summary>The capture closed empty. A hold stage never plays empty: the lunge stage holds for the whiff recovery.</summary>
    private void EndEmpty(ref CharacterState s)
    {
        if (!_p.HasHoldStage)
        {
            _phase = Phase.Spent;
            return;
        }
        _phase = Phase.Whiff;
        _whiffTicks = 0;
        Plant(ref s);
        // Free on the tick after the last recovery tick, as the stage it holds would be.
        s.AnimLockTicks = (ushort)(_p.WhiffRecoveryTicks + 1);
    }

    private void Finish(ref CharacterState s)
    {
        _phase = Phase.Done;
        s.AnimLockTicks = 0;
        EndAbility(ref s);
    }

    private static void Plant(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.VX = s.VZ = 0f;
        if (!s.IsGrounded) s.VY = 0f;
    }

    public override void OnCancel(ref CharacterState s) => _phase = Phase.Done;

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
        w.WriteBool(ReleaseRequested);
        w.WriteUShort(_ticks);
        w.WriteUShort(_whiffTicks);
        w.WriteULong(_targetId);
        w.WriteBool(_lunging);
        w.WriteFloat(_commanded);
        w.WriteFloat(_lastX);
        w.WriteFloat(_lastZ);
        w.WriteBool(_wasGrounded);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte();
        ReleaseRequested = r.ReadBool();
        if (_phase > Phase.Done) throw SnapshotReader.Malformed("command grab phase");
        _ticks = r.ReadUShort();
        _whiffTicks = r.ReadUShort();
        _targetId = r.ReadULong();
        _lunging = r.ReadBool();
        _commanded = r.ReadFloat();
        _lastX = r.ReadFloat();
        _lastZ = r.ReadFloat();
        _wasGrounded = r.ReadBool();
    }
}
