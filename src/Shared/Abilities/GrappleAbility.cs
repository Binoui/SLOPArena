using System;
namespace SlopArena.Shared.Abilities;

/// <summary>ADR-0064: a loop catch followed by a self-haul or a fighter zip and kick.</summary>
public sealed class GrappleAbility : ServerAbility, ITetherCatch, IRecoveryTurnCapability, ILandingContinuationCapability
{
    private enum Phase : byte { Windup, Out, Haul, Pop, Zip, Kick, Miss, Done }
    private readonly CookedGrappleCapabilityParameters _p;
    private Phase _phase;
    private ushort _ticks, _flightTicks;
    private bool _stage;
    private float _x, _y, _z, _startX, _startY, _startZ, _popX, _popY, _popZ, _stickX, _stickZ;
    public GrappleAbility(CookedGrappleCapabilityParameters parameters) => _p = parameters;
    public override ITetherCatch Tether => this;
    public bool Catches => _phase == Phase.Out;
    public bool Reeling => _phase == Phase.Zip;
    public bool Launches => _phase == Phase.Kick;
    public override bool OwnsVerticalMotion => _phase is Phase.Haul or Phase.Zip;
    public override bool IgnoresFighterPushboxes => _phase == Phase.Zip;
    public override float GravityMultiplier => _phase is Phase.Windup or Phase.Out ? _p.AirGravityScale : OwnsVerticalMotion ? 0f : 1f;
    public override MoveSpot? Spot => _phase is Phase.Out or Phase.Haul or Phase.Pop ? new MoveSpot(_x, _y, _z) : null;
    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Windup; _ticks = 0;
        s.State = ActionState.Attacking; s.IsAiming = false; s.IsFastFalling = false;
        ClearVelocityOwnership(ref s); s.VX = s.VZ = 0f;
        s.AnimLockTicks = ushort.MaxValue;
        OnRecoveryTurn(ref s, ActivationInput);
    }
    public void OnRecoveryTurn(ref CharacterState s, in InputState input)
    {
        if (_phase != Phase.Windup) return;
        _stickX = input.MoveX; _stickZ = input.MoveY;
        if (_stickX * _stickX + _stickZ * _stickZ > .01f) s.FacingYaw = MathF.Atan2(_stickX, _stickZ);
    }
    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        ++_ticks;
        if (_ticks == 1)
        {
            string clip = _phase switch {
                Phase.Out => _p.OutAnimationId, Phase.Haul => _p.HaulAnimationId, Phase.Pop => _p.PopAnimationId,
                Phase.Zip => _p.ZipAnimationId, Phase.Kick => _p.KickAnimationId, Phase.Miss => _p.MissAnimationId, _ => "" };
            int index = Move?.FindClip(clip) ?? -1;
            if (clip.Length > 0 && index > 0 && index <= byte.MaxValue)
            { s.ComboStage = AnimIndex = (byte)index; s.AttackElapsedTicks = 0; }
        }
        switch (_phase)
        {
            case Phase.Windup:
                if (_ticks >= _p.ThrowTick) Throw(ref s, def);
                break;
            case Phase.Out:
                if (!Resolver.TryGetActivationProjectile(s.EntityId, ActivationId, out var loop)) { Miss(ref s); break; }
                if (_stage && _ticks >= _flightTicks)
                {
                    Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
                    (_startX, _startY, _startZ) = (s.PX, s.PY, s.PZ);
                    s.VX = s.VY = s.VZ = 0f; s.IsGrounded = false;
                    Set(Phase.Haul); break;
                }
                if (Arena is { } arena && Resolver.MeetsStage(loop, arena, out _, out _, out _)) Miss(ref s);
                break;
            case Phase.Haul:
                float u = MathF.Min(1f, _ticks / (float)_p.HaulTicks);
                float t = u * u;
                float dx = _popX - _x, dz = _popZ - _z, length = MathF.Sqrt(dx * dx + dz * dz);
                float ox = _x - dx / length * (def.CapsuleRadius + .08f), oz = _z - dz / length * (def.CapsuleRadius + .08f);
                float oy = _y + def.CapsuleHeight * .5f + _p.PopClearance;
                float nx, ny, nz;
                if (t < .7f) { float v = t / .7f; nx = _startX + (ox - _startX) * v; ny = _startY + (oy - _startY) * v; nz = _startZ + (oz - _startZ) * v; }
                else { float v = (t - .7f) / .3f; nx = ox + (_popX - ox) * v; ny = oy; nz = oz + (_popZ - oz) * v; }
                if (!OwnerSimulation!.MoveGrappleBody(ref s, def, nx, ny, nz)) { Miss(ref s); break; }
                if (_ticks >= _p.HaulTicks)
                {
                    s.VX = dx / length * _p.PopVelocity; s.VZ = dz / length * _p.PopVelocity; s.VY = _p.PopVelocity;
                    s.AirTimeTicks = def.Movement.FloatWindowTicks; Set(Phase.Pop);
                }
                break;
            case Phase.Pop:
                if (s.IsGrounded || _ticks >= _p.PopTicks) Finish(ref s);
                break;
            case Phase.Zip:
                var step = OwnerSimulation!.ZipToPartner(ref s, def, _p, out var partner);
                (_x, _y, _z) = partner;
                if (step == TetherStep.Arrived)
                {
                    SpawnHitbox(ref s, CookedCharacterRuntimeAdapter.ToHitboxEvent(_p.Kick));
                    s.VX = -MathF.Sin(s.FacingYaw) * _p.KickRecoil; s.VZ = -MathF.Cos(s.FacingYaw) * _p.KickRecoil;
                    s.VY = _p.KickRecoil; s.IsGrounded = false; s.AirTimeTicks = def.Movement.FloatWindowTicks;
                    Set(Phase.Kick);
                }
                else if (step != TetherStep.Holding) Miss(ref s);
                break;
            case Phase.Kick: if (_ticks >= _p.KickRecoveryTicks) Finish(ref s); break;
            case Phase.Miss: if (_ticks >= _p.MissRecoveryTicks) Finish(ref s); break;
        }
    }
    private void Throw(ref CharacterState s, CharacterDefinition def)
    {
        bool target = OwnerSimulation!.TryGetCurrentTarget(in s, out _, out var position);
        _stage = false;
        if ((!s.IsGrounded || !target) && Arena is { } arena && OwnerSimulation.FindGrappleAnchor(s, def, _p, _stickX, _stickZ, out var anchor))
        {
            _stage = true; (_x, _y, _z) = (anchor.X, anchor.Y, anchor.Z);
            (_popX, _popY, _popZ) = (anchor.PopX, anchor.PopY, anchor.PopZ);
        }
        else if (target) (_x, _y, _z) = (position.X, position.Y + def.CapsuleHeight * .25f, position.Z);
        else (_x, _y, _z) = (s.PX + MathF.Sin(s.FacingYaw) * _p.AnchorReach, s.PY + _p.MaxRise, s.PZ + MathF.Cos(s.FacingYaw) * _p.AnchorReach);
        var p = _p.Projectile;
        float hx = s.PX + p.LaunchOffsetX * MathF.Cos(s.FacingYaw) + p.LaunchOffsetZ * MathF.Sin(s.FacingYaw);
        float hz = s.PZ - p.LaunchOffsetX * MathF.Sin(s.FacingYaw) + p.LaunchOffsetZ * MathF.Cos(s.FacingYaw);
        float endX = _x, endY = _y, endZ = _z;
        if (_stage)
        {
            float ax = _popX - _x, az = _popZ - _z, al = MathF.Sqrt(ax * ax + az * az);
            endX -= ax / al * (p.Radius + .04f); endZ -= az / al * (p.Radius + .04f); endY += p.Radius + .04f;
        }
        float dx = endX - hx, dy = endY - (s.PY + p.LaunchOffsetY), dz = endZ - hz;
        s.AimYaw = MathF.Atan2(dx, dz); s.AimPitch = MathF.Atan2(dy, MathF.Sqrt(dx * dx + dz * dz));
        s.FacingYaw = s.AimYaw;
        _flightTicks = (ushort)Math.Max(1, MathF.Ceiling(MathF.Sqrt(dx * dx + dy * dy + dz * dz) / (p.Speed * Simulation.TickDt)));
        SpawnCookedProjectile(ref s, p, PresentationOperationIndex, false, true);
        Set(Phase.Out);
    }
    public void OnCaught(ref CharacterState s, float x, float y, float z)
    { (_x, _y, _z) = (x, y, z); s.VX = s.VY = s.VZ = 0f; Set(Phase.Zip); }
    private void Set(Phase phase) { _phase = phase; _ticks = 0; }
    private void Miss(ref CharacterState s)
    {
        Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
        s.VX = s.VY = s.VZ = 0f;
        Set(Phase.Miss);
        PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, PresentationOperationIndex, "", PresentationAttackSequence,
            PresentationEventSource.GrappleMiss, _x, _y, _z, s.FacingYaw));
    }
    private void Finish(ref CharacterState s) { Set(Phase.Done); s.AnimLockTicks = 0; EndAbility(ref s); }
    public override void OnCancel(ref CharacterState s) => Set(Phase.Done);
    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w); w.WriteByte((byte)_phase); w.WriteUShort(_ticks); w.WriteUShort(_flightTicks); w.WriteBool(_stage);
        w.WriteFloat(_x); w.WriteFloat(_y); w.WriteFloat(_z); w.WriteFloat(_startX); w.WriteFloat(_startY); w.WriteFloat(_startZ);
        w.WriteFloat(_popX); w.WriteFloat(_popY); w.WriteFloat(_popZ); w.WriteFloat(_stickX); w.WriteFloat(_stickZ);
    }
    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r); _phase = (Phase)r.ReadByte(); if (_phase > Phase.Done) throw SnapshotReader.Malformed("grapple phase");
        _ticks = r.ReadUShort(); _flightTicks = r.ReadUShort(); _stage = r.ReadBool();
        _x = r.ReadFloat(); _y = r.ReadFloat(); _z = r.ReadFloat(); _startX = r.ReadFloat(); _startY = r.ReadFloat(); _startZ = r.ReadFloat();
        _popX = r.ReadFloat(); _popY = r.ReadFloat(); _popZ = r.ReadFloat(); _stickX = r.ReadFloat(); _stickZ = r.ReadFloat();
    }
}
