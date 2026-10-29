using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// slop.ability.clinch.v1 (ADR-0053), FightGuy's Title Match. Lunge: a fixed, level dash toward the current target
/// carrying a catch sphere. A catch pairs the two fighters (the simulation's CatchOnClinch) and the move holds the
/// victim in front of it, one placement per tick, while the timeline's hold stage plays its hits; on the release tick
/// the pair ends with the victim stuck, and the timeline finishes. A lunge that ends empty (range, a wall, a dodge)
/// holds the lunge stage for the whiff recovery, then ends the move. A lost pair ends the move at once.
/// Phases: Lunge, then Held and Released after a catch, Released alone after an object, or Whiff; Done ends it.
/// </summary>
public sealed class ClinchAbility : ServerAbility
{
    private enum Phase : byte { Lunge, Held, Whiff, Released, Done }

    private readonly CookedClinchCapabilityParameters _p;
    private Phase _phase;
    // Ticks since the activation; the timeline's stage clock advances in step, so stage 1 starts at LungeStageTicks.
    private ushort _ticks;
    private ushort _phaseTicks;
    private ushort _travelTicks;
    private float _dx, _dz, _lastX, _lastZ, _commanded;
    private bool _wasGrounded;

    public ClinchAbility(CookedClinchCapabilityParameters parameters)
        => _p = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override ClinchAbility? Clinch => this;
    public override bool OwnsVerticalMotion => _phase is Phase.Lunge or Phase.Held;
    public override float GravityMultiplier => _phase is Phase.Lunge or Phase.Held ? 0f : 1f;
    // The whiff owns the move's end; a finished clinch leaves no stage to start.
    internal override bool HoldsStage => _phase is Phase.Whiff or Phase.Done;

    /// <summary>The lunge is out and its contacts catch.</summary>
    internal bool Catches => _phase == Phase.Lunge;
    /// <summary>A clinched fighter is held: this activation's hits on it deal damage and hitstop only.</summary>
    internal bool Holding => _phase == Phase.Held;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        float yaw = s.FacingYaw;
        if (OwnerSimulation!.TryGetCurrentTarget(in s, out _, out var target))
        {
            float tx = target.X - s.PX, tz = target.Z - s.PZ;
            if (tx * tx + tz * tz > 0.000001f) yaw = MathF.Atan2(tx, tz);
        }
        _phase = Phase.Lunge;
        _dx = MathF.Sin(yaw);
        _dz = MathF.Cos(yaw);
        _lastX = s.PX;
        _lastZ = s.PZ;
        _wasGrounded = s.IsGrounded;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.IsFastFalling = false;
        s.FacingYaw = yaw;
        ClearVelocityOwnership(ref s);
        s.VY = 0f;
        Command(ref s);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_ticks < ushort.MaxValue) _ticks++;
        switch (_phase)
        {
            case Phase.Lunge:
                TickLunge(ref s);
                break;
            case Phase.Held:
                Plant(ref s);
                if (!OwnerSimulation!.HoldClinch(ref s, _p))
                {
                    Finish(ref s);
                    break;
                }
                if (_ticks >= _p.LungeStageTicks + _p.ReleaseTick)
                {
                    OwnerSimulation.ReleaseClinch(ref s, _p.ReleaseLockTicks);
                    _phase = Phase.Released;
                }
                break;
            case Phase.Whiff:
                Plant(ref s);
                if (++_phaseTicks >= _p.WhiffRecoveryTicks) Finish(ref s);
                break;
        }
    }

    /// <summary>
    /// One travel tick: a tick that kept under 60% of its commanded travel met a wall (a landing excepted), and the
    /// tick after the last travel decides the whiff. Every travel tick carries a fresh catch at the new position.
    /// </summary>
    private void TickLunge(ref CharacterState s)
    {
        float progress = (s.PX - _lastX) * _dx + (s.PZ - _lastZ) * _dz;
        bool landed = s.IsGrounded && !_wasGrounded;
        _wasGrounded = s.IsGrounded;
        _lastX = s.PX;
        _lastZ = s.PZ;
        if (_travelTicks >= _p.TravelTicks || _commanded > 0f && progress < _commanded * .6f && !landed)
        {
            StartWhiff(ref s);
            return;
        }
        var catchBox = CookedCharacterRuntimeAdapter.ToHitboxEvent(_p.CatchHitbox) with { HitGroup = 1, DurationTicks = 1 };
        SpawnHitbox(ref s, catchBox, followOwner: true, freezesOwner: false);
        _travelTicks++;
        if (_travelTicks < _p.TravelTicks) Command(ref s);
        else
        {
            _commanded = 0f;
            s.VX = s.VZ = 0f;
        }
    }

    /// <summary>The next travel interval; the last one takes whatever distance remains.</summary>
    private void Command(ref CharacterState s)
    {
        float step = _p.LungeSpeed * Simulation.TickDt;
        _commanded = _travelTicks + 1 >= _p.TravelTicks ? MathF.Max(0f, _p.LungeDistance - step * _travelTicks) : step;
        s.VX = _dx * (_commanded / Simulation.TickDt);
        s.VZ = _dz * (_commanded / Simulation.TickDt);
        s.VY = 0f;
    }

    /// <summary>Called by the simulation in the tick a catch pairs the two fighters.</summary>
    internal void OnCaught(ref CharacterState s)
    {
        _phase = Phase.Held;
        Plant(ref s);
    }

    /// <summary>
    /// A lunge that meets an object (a course target, the ball, a Mayhem wall) stops there and the hold stage plays on it
    /// with nobody held, as Fist of Fury did on the courses' bonus targets.
    /// </summary>
    public override void OnHitObject(ref CharacterState attacker, CharacterDefinition attackerDef,
        ref float damage, ref float knockbackForce)
    {
        if (_phase != Phase.Lunge) return;
        _phase = Phase.Released;
        Plant(ref attacker);
    }

    private void StartWhiff(ref CharacterState s)
    {
        _phase = Phase.Whiff;
        _phaseTicks = 0;
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

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteByte((byte)_phase);
        w.WriteUShort(_ticks);
        w.WriteUShort(_phaseTicks);
        w.WriteUShort(_travelTicks);
        w.WriteFloat(_dx);
        w.WriteFloat(_dz);
        w.WriteFloat(_lastX);
        w.WriteFloat(_lastZ);
        w.WriteFloat(_commanded);
        w.WriteBool(_wasGrounded);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte();
        if (_phase > Phase.Done) throw SnapshotReader.Malformed("clinch phase");
        _ticks = r.ReadUShort();
        _phaseTicks = r.ReadUShort();
        _travelTicks = r.ReadUShort();
        _dx = r.ReadFloat();
        _dz = r.ReadFloat();
        _lastX = r.ReadFloat();
        _lastZ = r.ReadFloat();
        _commanded = r.ReadFloat();
        _wasGrounded = r.ReadBool();
    }
}
