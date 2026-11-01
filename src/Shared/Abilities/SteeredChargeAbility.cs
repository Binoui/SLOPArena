using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.Abilities;

/// <summary>The rear, gallop, scoop, carry and fling of steered-charge.v2 (ADR-0059).</summary>
public sealed class SteeredChargeAbility : ServerAbility, ILandingContinuationCapability, ICommandHold
{
    private enum Phase : byte { Rear, Gallop, Carry, AirStrides, AirCarry, LookDown, Fling, HitEnding, MissEnding, Ended }
    private enum Outcome : byte { None, Hit, Blocked }

    // Terrain stops the gallop when a tick keeps less than this share of its forward travel, as the
    // directional dash does: walls hit head-on stop it, seams and gentle ramps do not.
    private const float BlockedTravelRatio = 0.6f;
    // Airborne ticks a ground gallop tolerates before it counts as galloping off a ledge: a small step
    // down or a seam must not start the air ending.
    internal const ushort LedgeGraceTicks = 4;
    private const float SteerDeadZone = 0.2f;

    private readonly CookedSteeredChargeCapabilityParameters _parameters;
    private Phase _phase;
    private Outcome _outcome;
    private ushort _phaseTicks;
    private ushort _gallopTicks;
    private ushort _airborneTicks;
    private float _yaw;
    private float _prevX;
    private float _prevZ;
    private float _prevY;
    private ulong _targetId;
    private byte _targetDeaths;
    private bool _challenged, _tracking;
    private ushort _rearTicks, _carryTicks;
    public bool ReleaseRequested { get; private set; }
    public override ICommandHold? CommandHold => this;
    public HoldCatch CatchKind => HoldCatch.LandedHit;
    public bool Catches => _outcome == Outcome.None && _phase is Phase.Gallop or Phase.AirStrides;
    public bool Holding => _phase is Phase.Carry or Phase.AirCarry;
    public float? AnchorDistance => _parameters.Carry.AnchorDistance;
    public float AnchorHeight => _parameters.Carry.AnchorHeight;
    public CookedGrabLaunch? Launch => _parameters.Carry.Fling;
    public ushort ReleaseLockTicks => 0;

    public override bool TryConsumeActiveSlotPress(ref CharacterState owner, in InputState input)
    {
        if (input.ActiveSlot != Slot + 1 || _phase == Phase.Ended) return false;
        if (Holding) ReleaseRequested = true;
        owner.BufferedSlot = 0;
        return true;
    }

    public void OnCaught(ref CharacterState owner)
    {
        _phase = _phase == Phase.AirStrides ? Phase.AirCarry : Phase.Carry;
        _carryTicks = 0;
        RemoveLance(ref owner);
        Drive(ref owner, _parameters.GallopSpeed);
        Emit(ref owner, CharacterDef!, _parameters.Carry.ScoopPresentationId, PresentationEventSource.CapabilityLaunch);
    }
    public void OnClashed(ref CharacterState owner) => _outcome = Outcome.Hit;
    public void OnPlaced(ref CharacterState owner, bool blocked)
    {
        if (blocked) ReleaseRequested = true;
    }
    public void OnReleased(ref CharacterState owner)
    {
        ReleaseRequested = false;
        if (_phase == Phase.AirCarry) StartLookDown(ref owner, CharacterDef!);
        else
        {
            Enter(ref owner, Phase.Fling);
            ShowClip(ref owner, _parameters.Carry.FlingAnimationId);
            Hold(ref owner);
        }
        Emit(ref owner, CharacterDef!, _parameters.Carry.FlingPresentationId, PresentationEventSource.CapabilityLanding);
    }
    private bool _measureTravel;
    // A timeline that starts this capability after tick zero ticks it in the same frame, before the
    // movement step has applied the velocity OnStart set: that tick has nothing to measure or count.
    private bool _startedThisTick;
    private HashSet<ulong>? _lanceHits;

    public SteeredChargeAbility(CookedSteeredChargeCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override bool OwnsVerticalMotion => _phase is Phase.AirStrides or Phase.AirCarry or Phase.LookDown || _phase == Phase.Rear && AirborneAtStart;
    // Fighters move before abilities tick, so the strides and the look-down switch gravity off to
    // hold their height instead of only zeroing the vertical speed afterwards.
    public override float GravityMultiplier => OwnsVerticalMotion ? 0f : 1f;
    public override bool HasArmor => _parameters.Armored && (_phase is Phase.Gallop or Phase.Carry or Phase.AirStrides or Phase.AirCarry);

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _outcome = Outcome.None;
        _gallopTicks = 0;
        _airborneTicks = 0;
        _yaw = s.FacingYaw;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.IsFastFalling = false;
        _challenged = OwnerSimulation!.TryTakeChallenge(ref s, out _targetId);
        if (!_challenged) _targetId = 0;
        if (SimulationStates!.TryGetValue(_targetId, out var target)) _targetDeaths = target.Deaths;
        _tracking = _targetId != 0;
        _rearTicks = _challenged ? _parameters.ChallengedRearTicks : Move!.Timeline.Stages[0].DurationTicks;
        Enter(ref s, Phase.Rear);
        s.AnimLockTicks = ushort.MaxValue;
        Hold(ref s);
        if (AirborneAtStart) s.VY = 0f;
        FaceTarget(ref s);
        if (_challenged) Emit(ref s, def, _parameters.ChallengePresentationId, PresentationEventSource.CapabilityLaunch);
        _startedThisTick = true;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        if (_startedThisTick)
        {
            _startedThisTick = false;
            if (!_challenged) OwnerSimulation!.TryGetCurrentTarget(in s, out _targetId, out _);
            if (SimulationStates!.TryGetValue(_targetId, out var target)) _targetDeaths = target.Deaths;
            _tracking = _targetId != 0;
            FaceTarget(ref s);
            _phaseTicks = 1;
            return;
        }
        if (_phaseTicks < ushort.MaxValue) _phaseTicks++;
        switch (_phase)
        {
            case Phase.Rear:
                Hold(ref s);
                if (AirborneAtStart) s.VY = 0f;
                FaceTarget(ref s);
                if (_phaseTicks >= _rearTicks) StartGallop(ref s, def);
                break;
            case Phase.Carry:
            case Phase.AirCarry: TickCarry(ref s, input, def); break;
            case Phase.Fling:
                Hold(ref s);
                if (_phaseTicks >= _parameters.Carry.FlingTicks)
                    Finish(ref s);
                break;
            case Phase.Gallop: TickGallop(ref s, input, def); break;
            case Phase.AirStrides: TickAirStrides(ref s, input, def); break;
            case Phase.LookDown: TickLookDown(ref s); break;
            case Phase.HitEnding: TickHitEnding(ref s); break;
            case Phase.MissEnding: TickMissEnding(ref s); break;
        }
    }

    private void StartGallop(ref CharacterState s, CharacterDefinition def)
    {
        ShowClip(ref s, _parameters.GallopAnimationId);
        Enter(ref s, s.IsGrounded ? Phase.Gallop : Phase.AirStrides);
        var lance = CookedCharacterRuntimeAdapter.ToHitboxEvent(_parameters.LanceHitbox) with { DurationTicks = ushort.MaxValue, HitGroup = 1 };
        var chest = CookedCharacterRuntimeAdapter.ToHitboxEvent(_parameters.ChestHitbox) with { DurationTicks = ushort.MaxValue, HitGroup = 1 };
        _lanceHits = SpawnHitbox(ref s, lance, followOwner: true);
        SpawnHitbox(ref s, chest, followOwner: true);
        s.VY = 0f;
        Drive(ref s, _parameters.GallopSpeed);
        Emit(ref s, def, _parameters.GallopPresentationId, PresentationEventSource.CapabilityLaunch);
    }

    private void FaceTarget(ref CharacterState s)
    {
        if (!_tracking || !SimulationStates!.TryGetValue(_targetId, out var target) || target.Deaths != _targetDeaths) return;
        float x = target.PX - s.PX, z = target.PZ - s.PZ;
        if (x * x + z * z > .000001f) _yaw = MathF.Atan2(x, z);
        s.FacingYaw = _yaw;
    }

    private void TickCarry(ref CharacterState s, InputState input, CharacterDefinition def)
    {
        if (!OwnerSimulation!.HasCommandHold(in s))
        {
            if (_phase == Phase.AirCarry) StartLookDown(ref s, def);
            else StartEnding(ref s, def, Phase.HitEnding);
            return;
        }
        bool air = _phase == Phase.AirCarry;
        if (!air && !s.IsGrounded)
        {
            // A ground passenger must never pull the rider off the stage.
            s.PX = _prevX; s.PY = _prevY; s.PZ = _prevZ;
            s.IsGrounded = true; s.VY = 0f;
            ReleaseRequested = true;
        }
        if (_measureTravel && Blocked(in s)) ReleaseRequested = true;
        if (++_carryTicks >= _parameters.Carry.MaxCarryTicks || air && _phaseTicks >= _parameters.AirStrideTicks)
            ReleaseRequested = true;
        if (air && s.IsGrounded) _phase = Phase.Carry;
        Steer(in s, input, air);
        LoopGallopClip(ref s);
        if (air) s.VY = 0f;
        Drive(ref s, ReleaseRequested ? 0f : _parameters.GallopSpeed);
    }

    private void TickGallop(ref CharacterState s, InputState input, CharacterDefinition def)
    {
        if (_outcome != Outcome.None)
        {
            StartEnding(ref s, def, _outcome == Outcome.Hit ? Phase.HitEnding : Phase.MissEnding);
            return;
        }
        if (!s.IsGrounded)
        {
            if (++_airborneTicks > LedgeGraceTicks)
            {
                StartAirStrides(ref s);
                Drive(ref s, _parameters.GallopSpeed);
                return;
            }
        }
        else
        {
            if (_measureTravel && _airborneTicks == 0 && Blocked(s))
            {
                StartEnding(ref s, def, Phase.MissEnding);
                return;
            }
            _airborneTicks = 0;
        }
        if (++_gallopTicks >= _parameters.MaxGallopTicks)
        {
            StartEnding(ref s, def, Phase.MissEnding);
            return;
        }
        Steer(in s, input, _phase == Phase.AirStrides);
        LoopGallopClip(ref s);
        Drive(ref s, _parameters.GallopSpeed);
    }

    private void TickAirStrides(ref CharacterState s, InputState input, CharacterDefinition def)
    {
        if (s.IsGrounded && _phaseTicks > 1)
        {
            // Landed on something during the strides: the rest of the gallop goes on along the ground.
            Enter(ref s, Phase.Gallop);
            Drive(ref s, _parameters.GallopSpeed);
            return;
        }
        if (_outcome != Outcome.None || _phaseTicks > _parameters.AirStrideTicks)
        {
            StartLookDown(ref s, def);
            return;
        }
        Steer(in s, input, _phase == Phase.AirStrides);
        LoopGallopClip(ref s);
        s.VY = 0f;
        s.IsFastFalling = false;
        Drive(ref s, _parameters.GallopSpeed);
    }

    private void TickLookDown(ref CharacterState s)
    {
        Hold(ref s);
        s.VY = 0f;
        s.IsFastFalling = false;
        if (_phaseTicks >= _parameters.LookDownTicks)
            Finish(ref s);
    }

    private void TickHitEnding(ref CharacterState s)
    {
        Hold(ref s);
        if (_phaseTicks >= _parameters.HitEndingTicks)
            Finish(ref s);
    }

    private void TickMissEnding(ref CharacterState s)
    {
        if (!s.IsGrounded && ++_airborneTicks > LedgeGraceTicks)
        {
            // Skidded off a ledge: the move ends and he falls like anyone else.
            Finish(ref s);
            return;
        }
        if (s.IsGrounded) _airborneTicks = 0;
        float skid = _parameters.SkidTicks == 0 ? 0f
            : MathF.Max(0f, 1f - (float)_phaseTicks / _parameters.SkidTicks);
        Drive(ref s, _parameters.GallopSpeed * skid);
        if (_phaseTicks >= _parameters.MissEndingTicks)
        {
            if (_parameters.TurnAroundOnMiss)
            {
                _yaw = NormalizeYaw(_yaw + MathF.PI);
                s.FacingYaw = _yaw;
            }
            Finish(ref s);
        }
    }

    private void StartAirStrides(ref CharacterState s)
    {
        Enter(ref s, Phase.AirStrides);
        ClearVelocityOwnership(ref s);
        s.VY = 0f;
        s.IsFastFalling = false;
    }

    private void StartLookDown(ref CharacterState s, CharacterDefinition def)
    {
        RemoveLance(ref s);
        Enter(ref s, Phase.LookDown);
        ShowClip(ref s, _parameters.AirEndingAnimationId);
        Hold(ref s);
        s.VY = 0f;
        Emit(ref s, def, _parameters.AirEndingPresentationId, PresentationEventSource.CapabilityLanding);
        if (_parameters.LookDownTicks == 0)
            Finish(ref s);
    }

    private void StartEnding(ref CharacterState s, CharacterDefinition def, Phase ending)
    {
        RemoveLance(ref s);
        Enter(ref s, ending);
        _airborneTicks = 0;
        bool hit = ending == Phase.HitEnding;
        ShowClip(ref s, hit ? _parameters.HitEndingAnimationId : _parameters.MissEndingAnimationId);
        Emit(ref s, def, hit ? _parameters.HitEndingPresentationId : _parameters.MissEndingPresentationId,
            PresentationEventSource.CapabilityLanding);
        if (hit)
            Hold(ref s);
        else
            Drive(ref s, _parameters.SkidTicks == 0 ? 0f : _parameters.GallopSpeed);
    }

    private void Enter(ref CharacterState s, Phase phase)
    {
        _phase = phase;
        _phaseTicks = 0;
        _measureTravel = false;
        s.AnimLockTicks = phase switch
        {
            Phase.Gallop => (ushort)Math.Min(ushort.MaxValue,
                _parameters.MaxGallopTicks - _gallopTicks + _parameters.MissEndingTicks + 1),
            Phase.AirStrides => (ushort)Math.Min(ushort.MaxValue,
                _parameters.AirStrideTicks + _parameters.LookDownTicks + 1),
            Phase.LookDown => _parameters.LookDownTicks,
            Phase.Fling => _parameters.Carry.FlingTicks,
            Phase.HitEnding => _parameters.HitEndingTicks,
            Phase.MissEnding => _parameters.MissEndingTicks,
            _ => 0,
        };
    }

    /// <summary>True when the last tick kept too little of its commanded forward travel: a wall.</summary>
    private bool Blocked(in CharacterState s)
    {
        float expected = _parameters.GallopSpeed * Simulation.TickDt;
        float moved = (s.PX - _prevX) * MathF.Sin(_yaw) + (s.PZ - _prevZ) * MathF.Cos(_yaw);
        return moved < expected * BlockedTravelRatio;
    }

    private void Steer(in CharacterState owner, InputState input, bool air)
    {
        var self = owner;
        float? assist = Catches && _parameters.Assist is { } a
            ? AssistHeading(in owner, _yaw, _targetId, a, SimulationStates!.Values.Where(t =>
                OwnerSimulation!.IsEligibleEnemy(self.EntityId, t.EntityId, self.PX, self.PZ, a.AssistRange, out _))) : null;
        if (assist is { } heading)
        {
            TurnToward(heading, _parameters.Assist!.AssistDegreesPerSecond);
            return;
        }
        bool stick = input.MoveX * input.MoveX + input.MoveY * input.MoveY >= SteerDeadZone * SteerDeadZone;
        float desired;
        float rate = _parameters.TurnDegreesPerSecond;
        if (_tracking && SimulationStates!.TryGetValue(_targetId, out var target) && target.Deaths == _targetDeaths)
        {
            desired = MathF.Atan2(target.PX - _prevX, target.PZ - _prevZ);
            if (MathF.Abs(NormalizeYaw(desired - _yaw)) > MathF.PI * .5f) _tracking = false;
        }
        else { _tracking = false; desired = _yaw; }
        if (stick) desired = MathF.Atan2(input.MoveX, input.MoveY);
        else
        {
            if (air || !_tracking) return;
            rate = _challenged ? _parameters.ChallengedTrackDegreesPerSecond : _parameters.TrackDegreesPerSecond;
        }
        TurnToward(desired, rate);
    }

    private void TurnToward(float desired, float rate)
    {
        float turn = rate * MathF.PI / 180f * Simulation.TickDt;
        _yaw = NormalizeYaw(_yaw + Math.Clamp(NormalizeYaw(desired - _yaw), -turn, turn));
    }

    internal static float? AssistHeading(in CharacterState owner, float yaw, ulong preferred,
        ChargeAssistParameters assist, IEnumerable<CharacterState> eligible)
    {
        float best = assist.AssistRange * assist.AssistRange;
        ulong bestId = ulong.MaxValue;
        float? heading = null;
        foreach (var target in eligible)
        {
            float dx = target.PX - owner.PX, dz = target.PZ - owner.PZ;
            float distance = dx * dx + dz * dz;
            float desired = MathF.Atan2(dx, dz);
            if (distance < .000001f || distance > assist.AssistRange * assist.AssistRange
                || MathF.Abs(NormalizeYaw(desired - yaw)) > assist.AssistHalfAngleDegrees * MathF.PI / 180f) continue;
            if (target.EntityId == preferred) return desired;
            if (distance > best || distance == best && target.EntityId >= bestId) continue;
            best = distance; bestId = target.EntityId; heading = desired;
        }
        return heading;
    }

    private void Drive(ref CharacterState s, float speed)
    {
        ClearVelocityOwnership(ref s);
        s.FacingYaw = _yaw;
        s.VX = MathF.Sin(_yaw) * speed;
        s.VZ = MathF.Cos(_yaw) * speed;
        _prevY = s.PY;
        _prevX = s.PX;
        _prevZ = s.PZ;
        _measureTravel = speed > 0f;
    }

    private void Hold(ref CharacterState s)
    {
        ClearVelocityOwnership(ref s);
        s.FacingYaw = _yaw;
        s.VX = s.VZ = 0f;
        _measureTravel = false;
    }

    /// <summary>The gallop clip loops: its phase clip plays in real time from AttackElapsedTicks.</summary>
    private void LoopGallopClip(ref CharacterState s)
    {
        if (string.IsNullOrEmpty(_parameters.GallopAnimationId) || _parameters.GallopLoopTicks == 0) return;
        int elapsed = _phase == Phase.Gallop ? _gallopTicks : _phaseTicks;
        s.AttackElapsedTicks = (ushort)(elapsed % _parameters.GallopLoopTicks);
    }

    /// <summary>Switches to a phase clip listed in the stage, from its first frame.</summary>
    private void ShowClip(ref CharacterState s, string animationId)
    {
        if (string.IsNullOrEmpty(animationId)) return;
        int index = Move?.FindClip(animationId) ?? -1;
        if (index <= 0 || index > byte.MaxValue) return;
        s.ComboStage = (byte)index;
        AnimIndex = (byte)index;
        s.AttackElapsedTicks = 0;
    }

    private void RemoveLance(ref CharacterState s)
    {
        if (_lanceHits == null) return;
        Resolver.RemoveOwnedHitboxes(s.EntityId, ActivationId, _lanceHits);
        _lanceHits = null;
    }

    private void Finish(ref CharacterState s)
    {
        _phase = Phase.Ended;
        EndAbility(ref s);
    }

    private void Emit(ref CharacterState s, CharacterDefinition def, string id, PresentationEventSource source)
    {
        if (string.IsNullOrEmpty(id)) return;
        PresentationSink?.Invoke(new TimelinePresentationEvent(0, s.EntityId, PresentationOperationIndex,
            id, PresentationAttackSequence, source, s.PX, s.PY - def.CapsuleHeight * 0.5f, s.PZ, s.FacingYaw));
    }

    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, CharacterDefinition targetDef, ref float damage, ref float knockbackForce)
    {
        if (target.EntityId != attacker.EntityId && Catches)
        {
            _outcome = Outcome.Hit;
            // He stops dead on the hit. The hit ending holds him too, but only from the next ability tick: the hitstop
            // would otherwise release one more stride of gallop speed first.
            attacker.VX = attacker.VZ = 0f;
        }
    }

    public override void OnShieldBlock(ref CharacterState attacker, ref CharacterState defender)
    {
        if ((_phase is Phase.Gallop or Phase.Carry or Phase.AirStrides or Phase.AirCarry) && _outcome == Outcome.None)
            _outcome = Outcome.Blocked;
    }

    public override void OnEnd(ref CharacterState s)
    {
        _phase = Phase.Ended;
        if (s.State != ActionState.Hitstun && s.HitstunTicks == 0 && !Simulation.HasKnockback(s))
            s.VX = s.VZ = 0f;
    }

    public override void OnCancel(ref CharacterState s)
    {
        bool ownsVelocity = _phase is Phase.Gallop or Phase.Carry or Phase.AirStrides or Phase.AirCarry or Phase.MissEnding;
        _phase = Phase.Ended;
        if (ownsVelocity)
            s.VX = s.VZ = 0f;
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
        w.WriteULong(_targetId); w.WriteByte(_targetDeaths); w.WriteBool(_challenged); w.WriteBool(_tracking);
        w.WriteUShort(_rearTicks); w.WriteUShort(_carryTicks); w.WriteBool(ReleaseRequested); w.WriteFloat(_prevY);
        w.WriteByte((byte)_outcome);
        w.WriteUShort(_phaseTicks);
        w.WriteUShort(_gallopTicks);
        w.WriteUShort(_airborneTicks);
        w.WriteFloat(_yaw);
        w.WriteFloat(_prevX);
        w.WriteFloat(_prevZ);
        w.WriteBool(_measureTravel);
        w.WriteBool(_startedThisTick);
        w.WriteSet(_lanceHits);
    }

    internal override void ReadSnapshot(SnapshotReader r)
    {
        base.ReadSnapshot(r);
        _phase = (Phase)r.ReadByte();
        if (_phase > Phase.Ended) throw SnapshotReader.Malformed("charge phase");
        _targetId = r.ReadULong(); _targetDeaths = r.ReadByte(); _challenged = r.ReadBool(); _tracking = r.ReadBool();
        _rearTicks = r.ReadUShort(); _carryTicks = r.ReadUShort(); ReleaseRequested = r.ReadBool(); _prevY = r.ReadFloat();
        _outcome = (Outcome)r.ReadByte();
        _phaseTicks = r.ReadUShort();
        _gallopTicks = r.ReadUShort();
        _airborneTicks = r.ReadUShort();
        _yaw = r.ReadFloat();
        _prevX = r.ReadFloat();
        _prevZ = r.ReadFloat();
        _measureTravel = r.ReadBool();
        _startedThisTick = r.ReadBool();
        _lanceHits = r.ReadSet();
    }
}
