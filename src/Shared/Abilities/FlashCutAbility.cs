using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// The flash (ADR-0052). A still stance locks every eligible opponent in range on its last tick and fixes the pop
/// tick. Then one fixed-duration straight leg dashes through each locked victim in turn, its cut reaching only that
/// victim and holding them, and he sheathes. Every held victim pops on that tick. With nobody locked he whiffs.
/// </summary>
public sealed class FlashCutAbility : ServerAbility
{
    private enum Phase : byte { Stance, Legs, Sheathe, Recovery, Whiff, Done }
    private const float BlockedTravelRatio = .6f;
    /// <summary>The longest his recovery waits past its authored length for popped victims to leave hitstun.</summary>
    internal const ushort MaxReelWaitTicks = 60;
    private readonly CookedFlashCutCapabilityParameters _p;
    private Phase _phase;
    private ushort _phaseTicks;
    private uint _popTick;
    /// <summary>Locked victims in cut order, fixed at the lock; a respawn changes the death count and skips its leg.</summary>
    private (ulong Id, byte Deaths)[] _victims = Array.Empty<(ulong, byte)>();
    private byte _leg;
    private float _legX, _legZ, _lastX, _lastZ, _commanded;
    /// <summary>The leg's end point, fixed when it starts: a victim who runs during the leg escapes it.</summary>
    private float _aimX, _aimY, _aimZ, _aimYaw;
    private bool _revealed;
    /// <summary>Victims still held at the reveal, by lock index: the ones that pop.</summary>
    private byte _popped;

    public FlashCutAbility(CookedFlashCutCapabilityParameters parameters)
        => _p = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override bool IgnoresFighterPushboxes => _phase == Phase.Legs;
    public override bool HoldsFighterPushboxPosition => _phase is Phase.Stance or Phase.Sheathe or Phase.Recovery or Phase.Whiff;
    public override bool OwnsVerticalMotion => _phase is Phase.Stance or Phase.Legs or Phase.Sheathe or Phase.Whiff;
    public override float GravityMultiplier => OwnsVerticalMotion ? 0f : 1f;

    public override HeldCutRelease? HeldCutFor(ulong victimId)
    {
        if (_phase is not (Phase.Legs or Phase.Sheathe) || OwnerSimulation!.CurrentTick >= _popTick) return null;
        for (int i = 0; i < _victims.Length; i++)
            if (_victims[i].Id == victimId)
                return new HeldCutRelease(_popTick, _p.PopPresentationId, VictimCue(i));
        return null;
    }

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Stance;
        _phaseTicks = 0;
        _popped = 0;
        _revealed = false;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        ClearVelocityOwnership(ref s);
        s.IsFastFalling = false;
        Hold(ref s);
        s.AnimLockTicks = _p.StanceTicks;
        ShowClip(ref s, _p.StanceAnimationId);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        s.IsAiming = false;
        uint now = OwnerSimulation!.CurrentTick;
        switch (_phase)
        {
            case Phase.Stance:
                Hold(ref s);
                if (++_phaseTicks >= _p.StanceTicks) Lock(ref s, def, now);
                break;
            case Phase.Legs:
                Leg(ref s, def);
                break;
            case Phase.Sheathe:
                Hold(ref s);
                Sheathe(ref s, now);
                break;
            case Phase.Recovery:
                s.VX = s.VZ = 0f;
                // The pop is the payoff: he stays sheathed until his victims can act, so nothing follows it for free.
                if (++_phaseTicks >= _p.RecoveryTicks
                    && (!PoppedVictimReeling() || _phaseTicks >= _p.RecoveryTicks + MaxReelWaitTicks))
                    Finish(ref s);
                else s.AnimLockTicks = Math.Max(s.AnimLockTicks, (ushort)1);
                break;
            case Phase.Whiff:
                Hold(ref s);
                if (++_phaseTicks >= _p.WhiffTicks)
                {
                    Emit(in s, _p.SheathePresentationId, PresentationOperationIndex, s.PX, s.PY, s.PZ);
                    Finish(ref s);
                }
                break;
        }
    }

    private void Lock(ref CharacterState s, CharacterDefinition def, uint now)
    {
        _victims = OwnerSimulation!.LockFlashTargets(in s, _p.LockRange, _p.MaxTargets);
        _phaseTicks = 0;
        if (_victims.Length == 0)
        {
            _phase = Phase.Whiff;
            s.AnimLockTicks = _p.WhiffTicks;
            return;
        }
        int popAfter = _p.PopAfterLock(_victims.Length);
        _popTick = now + (uint)popAfter;
        s.AnimLockTicks = (ushort)(popAfter + _p.RecoveryTicks);
        for (int i = 0; i < _victims.Length; i++)
            if (SimulationStates!.TryGetValue(_victims[i].Id, out var victim))
                Emit(in s, _p.LockPresentationId, VictimCue(i), victim.PX, victim.PY, victim.PZ);
        _leg = 0;
        StartLeg(ref s, def);
    }

    /// <summary>Starts the next leg whose victim is still valid; a skipped leg's time folds into the sheathe.</summary>
    private void StartLeg(ref CharacterState s, CharacterDefinition def)
    {
        while (_leg < _victims.Length && !OwnerSimulation!.IsFlashVictim(_victims[_leg].Id, _victims[_leg].Deaths, allowHeld: false))
            _leg++;
        if (_leg >= _victims.Length || OwnerSimulation!.CurrentTick >= _popTick)
        {
            EnterSheathe(ref s);
            return;
        }
        _phase = Phase.Legs;
        _phaseTicks = 0;
        _legX = s.PX;
        _legZ = s.PZ;
        ShowClip(ref s, _p.LegAnimationId);
        // One contact pass per leg tick plus the end point, which the next leg's first pass would otherwise miss.
        SpawnHitbox(ref s, CookedCharacterRuntimeAdapter.ToHitboxEvent(_p.CutHitbox) with
        {
            HitGroup = 0,
            DurationTicks = (ushort)(_p.LegTicks + 1),
        }, followOwner: true, freezesOwner: false, onlyTargetId: _victims[_leg].Id);
        if (!Steer(ref s, def)) EndLeg(ref s, def, keepCut: false);
    }

    private void Leg(ref CharacterState s, CharacterDefinition def)
    {
        float mx = s.PX - _lastX, mz = s.PZ - _lastZ;
        bool blocked = _commanded > .01f
            && mx * mx + mz * mz < _commanded * _commanded * (BlockedTravelRatio * BlockedTravelRatio);
        bool done = ++_phaseTicks >= _p.LegTicks;
        if (!blocked && !done && Steer(ref s, def)) return;
        EndLeg(ref s, def, keepCut: done && !blocked);
    }

    /// <summary>A finished leg keeps its cut for the end point's pass; a wall or a lost victim ends it where he stands.</summary>
    private void EndLeg(ref CharacterState s, CharacterDefinition def, bool keepCut)
    {
        if (!keepCut) Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId);
        Hold(ref s);
        _leg++;
        StartLeg(ref s, def);
    }

    /// <summary>
    /// Aims this tick's travel at the leg's end point, past where the victim stood when the leg started and level
    /// with their feet, so the leg arrives on time. A step never outruns the cut's sampling bound.
    /// </summary>
    private bool Steer(ref CharacterState s, CharacterDefinition def)
    {
        if (_leg >= _victims.Length) return false;
        var (id, deaths) = _victims[_leg];
        if (!OwnerSimulation!.IsFlashVictim(id, deaths, allowHeld: true)
            || !SimulationStates!.TryGetValue(id, out var victim))
            return false;
        if (_phaseTicks == 0)
        {
            float hx = victim.PX - _legX, hz = victim.PZ - _legZ, h = MathF.Sqrt(hx * hx + hz * hz);
            if (h > .0001f) { hx /= h; hz /= h; }
            else { hx = MathF.Sin(s.FacingYaw); hz = MathF.Cos(s.FacingYaw); }
            float victimHalf = OwnerSimulation.GetDefinition(id).CapsuleHeight * .5f;
            _aimX = victim.PX + hx * _p.PassThroughDistance;
            _aimY = victim.PY - victimHalf + def.CapsuleHeight * .5f;
            _aimZ = victim.PZ + hz * _p.PassThroughDistance;
            _aimYaw = MathF.Atan2(hx, hz);
        }
        float ticks = Math.Max(1, _p.LegTicks - _phaseTicks);
        float dx = (_aimX - s.PX) / ticks;
        float dy = (_aimY - s.PY) / ticks;
        float dz = (_aimZ - s.PZ) / ticks;
        float step = MathF.Sqrt(dx * dx + dy * dy + dz * dz), maxStep = 2f * _p.CutHitbox.Radius;
        if (step > maxStep)
        {
            float scale = maxStep / step;
            dx *= scale; dy *= scale; dz *= scale;
        }
        s.VX = dx / Simulation.TickDt;
        s.VY = dy / Simulation.TickDt;
        s.VZ = dz / Simulation.TickDt;
        s.FacingYaw = _aimYaw;
        _commanded = MathF.Sqrt(dx * dx + dz * dz);
        _lastX = s.PX;
        _lastZ = s.PZ;
        return true;
    }

    private void EnterSheathe(ref CharacterState s)
    {
        _phase = Phase.Sheathe;
        _phaseTicks = 0;
        Hold(ref s);
        ShowClip(ref s, _p.SheatheAnimationId.Length > 0 ? _p.SheatheAnimationId : _p.StanceAnimationId);
    }

    private void Sheathe(ref CharacterState s, uint now)
    {
        if (!_revealed && now + _p.RevealLeadTicks >= _popTick)
        {
            _revealed = true;
            for (int i = 0; i < _victims.Length; i++)
            {
                if (!OwnerSimulation!.IsHeldBy(_victims[i].Id, s.EntityId, ActivationId)
                    || !SimulationStates!.TryGetValue(_victims[i].Id, out var victim))
                    continue;
                _popped |= (byte)(1 << i);
                Emit(in s, _p.RevealPresentationId, VictimCue(i), victim.PX,
                    victim.PY + OwnerSimulation.GetDefinition(_victims[i].Id).CapsuleHeight * .5f, victim.PZ);
            }
        }
        if (now < _popTick) return;
        Emit(in s, _p.SheathePresentationId, PresentationOperationIndex, s.PX, s.PY, s.PZ);
        _phase = Phase.Recovery;
        _phaseTicks = 0;
    }

    private bool PoppedVictimReeling()
    {
        for (int i = 0; i < _victims.Length; i++)
            if ((_popped & (1 << i)) != 0 && SimulationStates!.TryGetValue(_victims[i].Id, out var victim)
                && victim.Deaths == _victims[i].Deaths
                && (victim.State == ActionState.Hitstun || victim.HitstopTicks > 0))
                return true;
        return false;
    }

    private void Finish(ref CharacterState s)
    {
        _phase = Phase.Done;
        EndAbility(ref s);
    }

    public override void OnCancel(ref CharacterState s)
    {
        if (_phase == Phase.Legs) Hold(ref s);
        _phase = Phase.Done;
        s.IsAiming = false;
    }

    private static void Hold(ref CharacterState s) => s.VX = s.VY = s.VZ = 0f;

    /// <summary>Per-victim cues share a tick (locks, reveals, pops), so each takes its own index past Wibou's own cue.</summary>
    private int VictimCue(int victim) => PresentationOperationIndex + 1 + victim;

    private void Emit(in CharacterState s, string id, int operationIndex, float x, float y, float z)
    {
        if (string.IsNullOrEmpty(id)) return;
        PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, operationIndex, id,
            PresentationAttackSequence, PresentationEventSource.CapabilityLaunch, x, y, z, s.FacingYaw));
    }

    /// <summary>Switches to a clip listed in the stage, from its first frame.</summary>
    private void ShowClip(ref CharacterState s, string animationId)
    {
        int index = Move?.FindClip(animationId) ?? -1;
        if (index < 0 || index > byte.MaxValue) return;
        s.ComboStage = (byte)index;
        AnimIndex = (byte)index;
        s.AttackElapsedTicks = 0;
    }

    internal override void WriteSnapshot(SnapshotWriter w)
    {
        base.WriteSnapshot(w);
        w.WriteByte((byte)_phase); w.WriteUShort(_phaseTicks); w.WriteUInt(_popTick); w.WriteByte(_leg);
        w.WriteByte((byte)_victims.Length);
        foreach (var (id, deaths) in _victims) { w.WriteULong(id); w.WriteByte(deaths); }
        w.WriteFloat(_legX); w.WriteFloat(_legZ); w.WriteFloat(_lastX); w.WriteFloat(_lastZ);
        w.WriteFloat(_commanded); w.WriteBool(_revealed);
        w.WriteFloat(_aimX); w.WriteFloat(_aimY); w.WriteFloat(_aimZ); w.WriteFloat(_aimYaw); w.WriteByte(_popped);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte(); _phaseTicks = r.ReadUShort(); _popTick = r.ReadUInt(); _leg = r.ReadByte();
        int count = r.ReadByte();
        if (_phase > Phase.Done || count > FlashCutContract.MaxTargetsLimit)
            throw SnapshotReader.Malformed("flash cut");
        _victims = new (ulong, byte)[count];
        for (int i = 0; i < count; i++) _victims[i] = (r.ReadULong(), r.ReadByte());
        _legX = r.ReadFloat(); _legZ = r.ReadFloat(); _lastX = r.ReadFloat(); _lastZ = r.ReadFloat();
        _commanded = r.ReadFloat(); _revealed = r.ReadBool();
        _aimX = r.ReadFloat(); _aimY = r.ReadFloat(); _aimZ = r.ReadFloat(); _aimYaw = r.ReadFloat(); _popped = r.ReadByte();
    }
}
