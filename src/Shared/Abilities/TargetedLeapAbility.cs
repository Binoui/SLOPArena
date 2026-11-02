using System;

namespace SlopArena.Shared.Abilities;

/// <summary>Fixed rise, tracked hang, locked swept drop, fall and confirmed-hit recovery (ADR-0059).</summary>
public sealed class TargetedLeapAbility : ServerAbility, ILandingContinuationCapability
{
    private enum Phase : byte { Rise, Hang, Drop, Fall, Landing, Ended }
    private readonly CookedTargetedLeapCapabilityParameters _p;
    private Phase _phase;
    private ushort _ticks, _recovery;
    private ulong _targetId;
    private byte _targetDeaths;
    private float _originX, _originZ;
    private MoveSpot _spot;
    private bool _confirmed;

    public TargetedLeapAbility(CookedTargetedLeapCapabilityParameters parameters) => _p = parameters;
    public override bool OwnsVerticalMotion => _phase != Phase.Ended;
    public override float GravityMultiplier => _phase is Phase.Fall or Phase.Ended ? 1f : 0f;
    public override MoveSpot? Spot => _phase is Phase.Hang or Phase.Drop ? _spot : null;
    // A target's body at a ledge would stop the drop short and drop him off the stage (QA check #49).
    public override bool IgnoresFighterPushboxes => _phase == Phase.Drop;
    public override bool HoldsFighterPushboxPosition => _phase == Phase.Landing;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Rise; _ticks = 0;
        _originX = s.PX; _originZ = s.PZ;
        ClearVelocityOwnership(ref s);
        s.IsAiming = false; s.IsFastFalling = false; s.IsGrounded = false;
        s.AirTimeTicks = def.Movement.FloatWindowTicks;
        s.State = ActionState.Attacking; s.AnimLockTicks = ushort.MaxValue;
        ShowClip(ref s, _p.RiseAnimationId);
        Rise(ref s);
        Emit(in s, def, _p.RisePresentationId);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_phase == Phase.Rise && _ticks == 0)
        {
            OwnerSimulation!.TryGetCurrentTarget(in s, out _targetId, out _);
            if (SimulationStates!.TryGetValue(_targetId, out var target)) _targetDeaths = target.Deaths;
        }
        _ticks++;
        switch (_phase)
        {
            case Phase.Rise:
                if (_ticks >= _p.RiseTicks)
                {
                    Enter(Phase.Hang); Stop(ref s); ShowClip(ref s, _p.HangAnimationId);
                    UpdateSpot(in s, in input, def); Emit(in s, def, _p.HangPresentationId);
                }
                else Rise(ref s);
                break;
            case Phase.Hang:
                Stop(ref s); s.AttackElapsedTicks = _p.HangPoseTick;
                UpdateSpot(in s, in input, def);
                if (_ticks >= _p.HangTicks)
                {
                    Enter(Phase.Drop);
                    ShowClip(ref s, _p.DropAnimationId); s.AttackElapsedTicks = _p.HangPoseTick; Drop(ref s, def); Emit(in s, def, _p.LockPresentationId);
                }
                break;
            case Phase.Drop:
                if (s.IsGrounded) Land(ref s, def);
                else if (_ticks >= _p.DropTicks) { Enter(Phase.Fall); s.VX = s.VZ = 0; }
                else Drop(ref s, def);
                break;
            case Phase.Fall:
                s.VX = s.VZ = 0;
                if (s.IsGrounded) Land(ref s, def);
                else if (_ticks >= _p.MaxFallTicks) Finish(ref s);
                break;
            case Phase.Landing:
                Stop(ref s);
                if (_ticks >= _recovery) Finish(ref s);
                break;
        }
    }

    private void Rise(ref CharacterState s)
    {
        Stop(ref s);
        float k = Math.Min(_p.RiseTicks, _ticks + 1);
        float step = _p.RiseHeight * (2 * (_p.RiseTicks - k) + 1) / (_p.RiseTicks * _p.RiseTicks);
        s.VY = step / Simulation.TickDt;
    }

    private void UpdateSpot(in CharacterState s, in InputState input, CharacterDefinition def)
    {
        float yaw = input.MoveX * input.MoveX + input.MoveY * input.MoveY > .04f
            ? MathF.Atan2(input.MoveX, input.MoveY) : s.FacingYaw;
        float x = _originX + MathF.Sin(yaw) * _p.NoTargetDistance;
        float z = _originZ + MathF.Cos(yaw) * _p.NoTargetDistance;
        float y = s.PY - _p.RiseHeight - def.CapsuleHeight * .5f;
        if (_targetId != 0 && SimulationStates!.TryGetValue(_targetId, out var target) && target.Deaths == _targetDeaths)
        {
            x = target.PX; z = target.PZ;
            y = target.PY - OwnerSimulation!.GetDefinition(_targetId).CapsuleHeight * .5f;
        }
        float dx = x - _originX, dz = z - _originZ;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance > _p.MaxRange) { x = _originX + dx * _p.MaxRange / distance; z = _originZ + dz * _p.MaxRange / distance; }
        if (Arena is { } arena)
        {
            if (Resolver.TryProjectStraightDrop(arena, x, s.PY - def.CapsuleHeight * .5f, z, .01f, out float floor))
                y = floor;
        }
        _spot = new MoveSpot(x, MathF.Min(y, s.PY - def.CapsuleHeight * .5f), z);
    }

    private void Drop(ref CharacterState s, CharacterDefinition def)
    {
        ClearVelocityOwnership(ref s);
        float remaining = Math.Max(1, _p.DropTicks - _ticks) * Simulation.TickDt;
        s.VX = (_spot.X - s.PX) / remaining;
        s.VZ = (_spot.Z - s.PZ) / remaining;
        s.VY = MathF.Min(0, (_spot.Y + def.CapsuleHeight * .5f - s.PY) / remaining);
        s.IsFastFalling = false;
    }

    private void Land(ref CharacterState s, CharacterDefinition def)
    {
        Enter(Phase.Landing); _recovery = _p.MissRecoveryTicks;
        Stop(ref s); ShowClip(ref s, _p.LandingAnimationId); s.AttackElapsedTicks = 0;
        s.AnimLockTicks = _recovery;
        SpawnHitbox(ref s, CookedCharacterRuntimeAdapter.ToHitboxEvent(_p.Hitbox));
        Emit(in s, def, _p.LandingPresentationId, PresentationEventSource.CapabilityLanding);
    }

    public override void OnHitConfirmed(in SpellResolver.HitResult hit, in CharacterState target, int operationIndex)
    {
        if (_phase != Phase.Landing || _confirmed || hit.Blocked || hit.OwnerEntityId == hit.TargetEntityId) return;
        _confirmed = true;
        _recovery = (ushort)Math.Min(_recovery, _ticks + _p.HitRecoveryTicks);
    }

    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, CharacterDefinition targetDef, ref float damage, ref float knockbackForce)
    {
        if (_phase != Phase.Landing || _confirmed) return;
        attacker.AttackElapsedTicks = _p.HitLandingSeekTick;
        attacker.AnimLockTicks = _p.HitRecoveryTicks;
    }

    private void ShowClip(ref CharacterState s, string id)
    {
        int index = Move?.FindClip(id) ?? -1;
        if (index < 0 || index > byte.MaxValue) return;
        AnimIndex = (byte)index; s.ComboStage = (byte)index; s.AttackElapsedTicks = 0;
    }
    private static void Stop(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s); s.VX = s.VY = s.VZ = 0; s.IsAiming = false; s.IsFastFalling = false;
    }
    private void Enter(Phase phase) { _phase = phase; _ticks = 0; }
    private void Finish(ref CharacterState s) { _phase = Phase.Ended; EndAbility(ref s); }
    public override void OnEnd(ref CharacterState s) { _phase = Phase.Ended; s.VX = s.VZ = 0; }
    public override void OnCancel(ref CharacterState s) { _phase = Phase.Ended; }
    private void Emit(in CharacterState s, CharacterDefinition def, string id, PresentationEventSource source = PresentationEventSource.CapabilityLaunch)
    {
        if (id.Length == 0) return;
        PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, PresentationOperationIndex, id,
            PresentationAttackSequence, source, s.PX, s.PY - def.CapsuleHeight * .5f, s.PZ, s.FacingYaw));
    }
    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w); w.WriteByte((byte)_phase); w.WriteUShort(_ticks); w.WriteUShort(_recovery);
        w.WriteULong(_targetId); w.WriteByte(_targetDeaths); w.WriteBool(_confirmed);
        foreach (float value in new[] { _originX, _originZ, _spot.X, _spot.Y, _spot.Z }) w.WriteFloat(value);
    }
    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r); _phase = (Phase)r.ReadByte();
        if (_phase > Phase.Ended) throw SnapshotReader.Malformed("leap phase");
        _ticks = r.ReadUShort(); _recovery = r.ReadUShort(); _targetId = r.ReadULong(); _targetDeaths = r.ReadByte(); _confirmed = r.ReadBool();
        _originX = r.ReadFloat(); _originZ = r.ReadFloat(); _spot = new MoveSpot(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
    }
}
