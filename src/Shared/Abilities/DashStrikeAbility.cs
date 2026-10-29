using System;

namespace SlopArena.Shared.Abilities;

public sealed class DashStrikeAbility : ServerAbility, ILandingContinuationCapability
{
    private enum Phase : byte { Dash, Tip, Recovery, Done }
    private readonly CookedDashStrikeCapabilityParameters _p;
    private Phase _phase;
    private float _yaw, _dx, _dz, _startX, _startZ, _progress, _commanded;
    private ushort _phaseTicks;
    private bool _wasGrounded;
    private ulong _lightVictim;
    private byte _victimDeaths;

    public DashStrikeAbility(CookedDashStrikeCapabilityParameters parameters) => _p = parameters;
    public override bool IgnoresFighterPushboxes => _phase == Phase.Dash;
    public override bool HoldsFighterPushboxPosition => _phase is Phase.Tip or Phase.Recovery;
    public override bool OwnsVerticalMotion => _phase == Phase.Dash;
    public override float GravityMultiplier => _phase == Phase.Dash ? 0f : 1f;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Dash;
        _yaw = s.FacingYaw;
        if (OwnerSimulation!.TryGetCurrentTarget(in s, out _, out var target))
        {
            float dx = target.X - s.PX, dz = target.Z - s.PZ;
            if (dx * dx + dz * dz > 0.000001f) _yaw = MathF.Atan2(dx, dz);
        }
        _dx = MathF.Sin(_yaw); _dz = MathF.Cos(_yaw);
        _startX = s.PX; _startZ = s.PZ; _wasGrounded = s.IsGrounded;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.FacingYaw = _yaw;
        s.VY = 0f;
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        s.AnimLockTicks = (ushort)(_p.TravelTicks + _p.TipHitbox.DurationTicks + _p.RecoveryTicks);
        Command(ref s, _p.Distance);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        s.IsAiming = false;
        s.FacingYaw = _yaw;
        switch (_phase)
        {
            case Phase.Dash:
                float progress = (s.PX - _startX) * _dx + (s.PZ - _startZ) * _dz;
                bool landed = s.IsGrounded && !_wasGrounded;
                _wasGrounded = s.IsGrounded;
                if (progress - _progress < _commanded * .6f && !landed)
                {
                    Recover(ref s);
                    break;
                }
                _progress = progress;
                SpawnHitbox(ref s, Cut(_p.LightHitbox, 1), followOwner: true, freezesOwner: false);
                float remaining = _p.Distance - progress;
                if (remaining <= .0001f)
                {
                    s.PX += _dx * remaining; s.PZ += _dz * remaining;
                    s.VX = s.VZ = 0f;
                    _phase = Phase.Tip; _phaseTicks = 0;
                    s.AttackElapsedTicks = _p.TipPoseTick;
                    // Spawn last so the endpoint tip wins the final interval's shared contact history.
                    SpawnHitbox(ref s, Cut(_p.TipHitbox, _p.TipHitbox.DurationTicks));
                }
                else Command(ref s, remaining);
                break;
            case Phase.Tip:
                s.VX = s.VZ = 0f;
                s.AttackElapsedTicks = _p.TipPoseTick;
                if (++_phaseTicks >= _p.TipHitbox.DurationTicks) Recover(ref s);
                break;
            case Phase.Recovery:
                s.VX = s.VZ = 0f;
                if (++_phaseTicks >= _p.RecoveryTicks)
                {
                    if (_lightVictim != 0 && SimulationStates!.TryGetValue(_lightVictim, out var victim) &&
                        victim.Deaths == _victimDeaths && victim.InvincibilityTicks == 0)
                    {
                        float dx = victim.PX - s.PX, dz = victim.PZ - s.PZ;
                        if (dx * dx + dz * dz > .000001f) s.FacingYaw = MathF.Atan2(dx, dz);
                    }
                    _phase = Phase.Done;
                    EndAbility(ref s);
                }
                break;
        }
    }

    private void Command(ref CharacterState s, float remaining)
    {
        _commanded = MathF.Min(_p.Speed * Simulation.TickDt, remaining);
        s.VX = _dx * (_commanded / Simulation.TickDt);
        s.VZ = _dz * (_commanded / Simulation.TickDt);
        s.VY = 0f;
    }

    private void Recover(ref CharacterState s)
    {
        Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
        _phase = Phase.Recovery; _phaseTicks = 0;
        s.VX = s.VZ = 0f;
        s.AnimLockTicks = _p.RecoveryTicks;
    }

    public override void OnShieldBlock(ref CharacterState attacker, ref CharacterState defender)
    {
        if (_phase is Phase.Dash or Phase.Tip) Recover(ref attacker);
    }

    public override void OnHitConfirmed(in SpellResolver.HitResult hit, in CharacterState target, int operationIndex)
    {
        if (!hit.FreezesOwner && _lightVictim == 0)
        {
            _lightVictim = target.EntityId; _victimDeaths = target.Deaths;
        }
        if (hit.FreezesOwner && !hit.ArmorProtected && !string.IsNullOrEmpty(_p.TipPresentationId))
            PresentationSink?.Invoke(new TimelinePresentationEvent(hit.MatchTick, hit.OwnerEntityId,
                operationIndex, _p.TipPresentationId, hit.AttackSequence, PresentationEventSource.HitContact,
                hit.HitX, hit.HitY, hit.HitZ, _yaw));
    }

    public override void OnCancel(ref CharacterState s)
    {
        if (_phase == Phase.Dash) s.VX = s.VZ = 0f;
        _phase = Phase.Done;
        s.IsAiming = false;
    }

    private static HitboxEvent Cut(CookedHitbox x, ushort lifetime)
        => CookedCharacterRuntimeAdapter.ToHitboxEvent(x) with { HitGroup = 1, DurationTicks = lifetime };

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteByte((byte)_phase); w.WriteFloat(_yaw); w.WriteFloat(_dx); w.WriteFloat(_dz);
        w.WriteFloat(_startX); w.WriteFloat(_startZ); w.WriteFloat(_progress); w.WriteFloat(_commanded);
        w.WriteUShort(_phaseTicks); w.WriteBool(_wasGrounded); w.WriteULong(_lightVictim); w.WriteByte(_victimDeaths);
    }
    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte(); _yaw = r.ReadFloat(); _dx = r.ReadFloat(); _dz = r.ReadFloat();
        _startX = r.ReadFloat(); _startZ = r.ReadFloat(); _progress = r.ReadFloat(); _commanded = r.ReadFloat();
        _phaseTicks = r.ReadUShort(); _wasGrounded = r.ReadBool(); _lightVictim = r.ReadULong(); _victimDeaths = r.ReadByte();
    }
}
